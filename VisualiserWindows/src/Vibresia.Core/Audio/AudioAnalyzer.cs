namespace Vibresia.Core.Audio;

/// <summary>
/// FFT, log-binned spectrum, automatic gain, noise gate, beat detection and
/// tempo — a port of upstream's AudioAnalyzer.swift with its constants
/// unchanged, because custom vibescapes are tuned against the ranges these
/// produce. Ticked once per rendered frame; reads the newest window from the
/// ring buffer (overlapping windows, so the analysis never updates in bursts).
/// </summary>
public sealed class AudioAnalyzer
{
    public const int FftSize = 2048;
    public const int BandCount = 96;

    private const float GainAttack = 0.5f;
    private const float GainRelease = 0.995f;
    private const float GainFloor = 0.05f;
    private const float GainHeadroom = 0.9f;
    private const float GateLow = 0.002f;
    private const float GateHigh = 0.01f;
    private const float Smoothing = 0.32f;

    private readonly AudioRingBuffer _ring;
    private readonly RealFft _fft = new(FftSize);
    private readonly float[] _window = RealFft.HannNorm(FftSize);
    private readonly float[] _time = new float[FftSize];
    private readonly float[] _windowed = new float[FftSize];
    private readonly float[] _mags = new float[FftSize / 2];
    private readonly float[] _bandsRaw = new float[BandCount];
    private readonly float[] _bandsSmooth = new float[BandCount];
    private readonly float[] _bassHistory = new float[43];
    private int _historyIndex;
    private int _framesSinceLastBeat;
    private float _bandPeak = GainFloor;
    private int[] _bandEdges;
    private double _bandEdgesRate;

    private TempoTracker _tempo;
    private long _tempoCursor;
    private readonly float[] _tempoScratch = new float[4096];

    public float Rms { get; private set; }
    public float Bass { get; private set; }
    public float Mid { get; private set; }
    public float Treble { get; private set; }
    public float Level { get; private set; }
    public bool Beat { get; private set; }
    public float BeatStrength { get; private set; }
    public double Bpm { get; private set; }

    /// <summary>The noise gate this frame: 0 below the noise floor (treated as silence), 1 once there's clearly sound.</summary>
    public float Gate { get; private set; }
    public ReadOnlySpan<float> Bands => _bandsSmooth;

    public AudioAnalyzer(AudioRingBuffer ring)
    {
        _ring = ring;
        _bandEdgesRate = ring.SampleRate;
        _bandEdges = ComputeBandEdges(_bandEdgesRate);
        _tempo = new TempoTracker(_bandEdgesRate);
    }

    private static int[] ComputeBandEdges(double sampleRate)
    {
        var nyquist = sampleRate / 2;
        var binCount = FftSize / 2;
        var binHz = nyquist / binCount;
        const double fMin = 40.0;
        var fMax = Math.Min(16000.0, nyquist);
        var edges = new int[BandCount + 1];
        for (var i = 0; i <= BandCount; i++)
        {
            var f = fMin * Math.Pow(fMax / fMin, (double)i / BandCount);
            edges[i] = Math.Max(1, Math.Min(binCount - 1, (int)Math.Round(f / binHz, MidpointRounding.AwayFromZero)));
        }
        return edges;
    }

    public void Tick()
    {
        if (!_ring.ReadLatest(_time)) return;
        var rate = _ring.SampleRate;
        if (rate != _bandEdgesRate)
        {
            _bandEdgesRate = rate;
            _bandEdges = ComputeBandEdges(rate);
        }
        UpdateTempo();

        double sq = 0;
        for (var i = 0; i < FftSize; i++) sq += _time[i] * _time[i];
        var rms = (float)Math.Sqrt(sq / FftSize);
        Rms = rms;
        // 0 below the noise floor, 1 once there's clearly sound; fades between.
        var gate = Math.Clamp((rms - GateLow) / (GateHigh - GateLow), 0f, 1f);
        Gate = gate;

        for (var i = 0; i < FftSize; i++) _windowed[i] = _time[i] * _window[i];
        _fft.Magnitudes(_windowed, _mags);
        // vDSP path upstream: squared magnitude × 1/N, then sqrt.
        var invSqrtN = 1f / MathF.Sqrt(FftSize);
        for (var i = 0; i < _mags.Length; i++) _mags[i] *= invSqrtN;

        for (var b = 0; b < BandCount; b++)
        {
            var lo = _bandEdges[b];
            var hi = Math.Max(lo + 1, _bandEdges[b + 1]);
            float sum = 0;
            for (var i = lo; i < hi; i++) sum += _mags[i];
            _bandsRaw[b] = sum / (hi - lo);
        }

        // Automatic gain: follow the loudest band (fast attack, slow release)
        // so quiet and loud sources both fill 0..1; held while gated so
        // silence doesn't wind it up to full boost.
        var frameMax = _bandsRaw.Max();
        if (gate > 0)
        {
            if (frameMax > _bandPeak) _bandPeak += (frameMax - _bandPeak) * GainAttack;
            else _bandPeak *= GainRelease;
            _bandPeak = Math.Max(_bandPeak, GainFloor);
        }
        var gain = GainHeadroom / _bandPeak * gate;
        for (var b = 0; b < BandCount; b++) _bandsRaw[b] = Math.Min(1f, _bandsRaw[b] * gain);

        const int third = BandCount / 3;
        float bSum = 0, mSum = 0, tSum = 0;
        for (var i = 0; i < third; i++) bSum += _bandsRaw[i];
        for (var i = third; i < third * 2; i++) mSum += _bandsRaw[i];
        for (var i = third * 2; i < BandCount; i++) tSum += _bandsRaw[i];
        var bassInstant = bSum / third;
        var midInstant = mSum / third;
        var trebInstant = tSum / (BandCount - third * 2);

        Bass += (bassInstant - Bass) * Smoothing;
        Mid += (midInstant - Mid) * Smoothing;
        Treble += (trebInstant - Treble) * Smoothing;
        Level = (Bass + Mid + Treble) / 3;
        for (var i = 0; i < BandCount; i++) _bandsSmooth[i] += (_bandsRaw[i] - _bandsSmooth[i]) * Smoothing;

        // Beat detection on bass: an adaptive threshold over the last ~0.7 s.
        var histAvg = _bassHistory.Average();
        double var = 0;
        foreach (var h in _bassHistory) var += (h - histAvg) * (h - histAvg);
        var histStd = (float)Math.Sqrt(var / _bassHistory.Length);
        var threshold = histAvg * 1.32f + histStd * 0.6f + 0.04f;
        var isBeat = false;
        if (bassInstant > threshold && bassInstant > 0.18f && gate >= 1 && _framesSinceLastBeat > 12)
        {
            isBeat = true;
            _framesSinceLastBeat = 0;
        }
        _bassHistory[_historyIndex] = bassInstant;
        _historyIndex = (_historyIndex + 1) % _bassHistory.Length;
        _framesSinceLastBeat++;
        Beat = isBeat;
        BeatStrength = isBeat ? Math.Min(1f, bassInstant) : BeatStrength * 0.86f;
    }

    /// <summary>Feeds the tempo tracker every sample written since last time, in order.</summary>
    private void UpdateTempo()
    {
        var written = _ring.TotalWritten;
        var rate = _ring.SampleRate;
        if (written < _tempoCursor || _tempo.SampleRate != rate)
        {
            _tempo = new TempoTracker(rate);
            _tempoCursor = written;
        }
        var maxBacklog = (long)(rate * 2);
        if (written - _tempoCursor > maxBacklog) _tempoCursor = written - maxBacklog;
        while (_tempoCursor < written)
        {
            var n = (int)Math.Min(_tempoScratch.Length, written - _tempoCursor);
            var span = _tempoScratch.AsSpan(0, n);
            if (!_ring.Copy(_tempoCursor, span)) { _tempoCursor = written; break; }
            _tempo.Push(span);
            _tempoCursor += n;
        }
        Bpm = _tempo.Bpm;
    }
}
