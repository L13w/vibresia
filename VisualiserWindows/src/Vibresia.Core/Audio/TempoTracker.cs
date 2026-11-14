namespace Vibresia.Core.Audio;

/// <summary>
/// Estimates tempo from spectral-flux onsets by autocorrelation over the last
/// 8 s, with a prior toward 120 bpm to settle the octave and a voting step so
/// the estimate doesn't flicker. A port of upstream's TempoTracker.swift,
/// constants unchanged.
/// </summary>
public sealed class TempoTracker
{
    private const int FrameSize = 1024;
    private const int HopSize = 512;
    private const double WindowSeconds = 8.0;
    private const double WarmupSeconds = 4.0;
    private const double AnalysisSeconds = 0.25;
    private const double MinBpm = 60.0;
    private const double MaxBpm = 200.0;
    private const double PreferredBpm = 120.0;
    private const float MinConfidence = 0.2f;
    private const double Timeout = 3.0;
    private const float Gate = 0.002f;
    private const int SwitchVotes = 6;
    private const int LockVotes = 3;

    private static readonly float[] Blur = Enumerable.Range(-4, 9).Select(k => MathF.Exp(-0.5f * k * k / 4)).ToArray();

    public double SampleRate { get; }
    public double Bpm { get; private set; }
    public float Confidence { get; private set; }

    private readonly double _fps;
    private readonly RealFft _fft = new(FrameSize);
    private readonly float[] _window = RealFft.HannNorm(FrameSize);
    private readonly float[] _frame = new float[FrameSize];
    private readonly float[] _mags = new float[FrameSize / 2];
    private readonly float[] _prevMags = new float[FrameSize / 2];
    private readonly int _maxBin;
    private readonly List<float> _pending = new(FrameSize * 4);
    private readonly float[] _envelope;
    private int _envCount;
    private int _hopsSinceAnalysis;
    private int _quietHops;
    private int _weakHops;
    private double _candidate;
    private int _candidateVotes;

    public TempoTracker(double sampleRate)
    {
        SampleRate = sampleRate;
        _fps = sampleRate / HopSize;
        _maxBin = Math.Min(FrameSize / 2, (int)(8000 / (sampleRate / FrameSize)));
        _envelope = new float[(int)(WindowSeconds * _fps)];
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples) _pending.Add(s);
        var start = 0;
        var buf = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending);
        while (_pending.Count - start >= FrameSize)
        {
            ProcessFrame(buf.Slice(start, FrameSize));
            start += HopSize;
        }
        if (start > 0) _pending.RemoveRange(0, start);
    }

    private void ProcessFrame(ReadOnlySpan<float> x)
    {
        double sq = 0;
        for (var i = 0; i < FrameSize; i++) { sq += x[i] * x[i]; _frame[i] = x[i] * _window[i]; }
        var rms = (float)Math.Sqrt(sq / FrameSize);
        _fft.Magnitudes(_frame, _mags);

        // Log compression makes the flux about relative change, so a quiet
        // hi-hat counts for something next to a loud bass.
        float flux = 0;
        for (var i = 1; i < _maxBin; i++)
        {
            var m = MathF.Log(1 + 100 * _mags[i] / FrameSize);
            var d = m - _prevMags[i];
            if (d > 0) flux += d;
            _prevMags[i] = m;
        }
        var quiet = rms < Gate;
        _quietHops = quiet ? _quietHops + 1 : 0;
        AppendEnvelope(quiet ? 0 : flux);
    }

    private void AppendEnvelope(float v)
    {
        if (_envCount < _envelope.Length)
        {
            _envelope[_envCount++] = v;
        }
        else
        {
            Array.Copy(_envelope, 1, _envelope, 0, _envelope.Length - 1);
            _envelope[^1] = v;
        }
        if (_quietHops > Timeout * _fps)
        {
            _envCount = 0;
            Bpm = 0;
            Confidence = 0;
            _candidateVotes = 0;
            return;
        }
        _hopsSinceAnalysis++;
        if (_hopsSinceAnalysis >= (int)(AnalysisSeconds * _fps) && _envCount >= WarmupSeconds * _fps)
        {
            _hopsSinceAnalysis = 0;
            Analyse();
        }
    }

    private void Analyse()
    {
        var n = _envCount;
        var e = new float[n];
        for (var i = 0; i < n; i++)
        {
            float s = 0, w = 0;
            for (var k = 0; k < Blur.Length; k++)
            {
                var j = i + k - Blur.Length / 2;
                if (j >= 0 && j < n) { s += _envelope[j] * Blur[k]; w += Blur[k]; }
            }
            e[i] = s / w;
        }
        var mean = e.Average();
        for (var i = 0; i < n; i++) e[i] -= mean;

        var minLag = (int)Math.Floor(60 / MaxBpm * _fps);
        var maxLag = (int)Math.Ceiling(60 / MinBpm * _fps);
        var topLag = Math.Min(n / 2, 4 * maxLag + 2);
        if (topLag <= maxLag * 2) return;
        var ac = new float[topLag + 1];
        for (var lag = 0; lag <= topLag; lag++)
        {
            float s = 0;
            for (var i = 0; i < n - lag; i++) s += e[i] * e[i + lag];
            ac[lag] = s / (n - lag); // unbiased: long lags overlap fewer samples
        }
        if (ac[0] <= 1e-9f) return;

        // A beat period also shows at twice the lag; reward both, and let the
        // prior toward 120 bpm settle the octave.
        var bestLag = 0;
        var bestScore = float.MinValue;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var bpmAtLag = 60 * _fps / lag;
            var octaves = Math.Log2(bpmAtLag / PreferredBpm);
            var prior = (float)Math.Exp(-0.5 * octaves * octaves);
            var score = prior * (ac[lag] + 0.5f * ac[Math.Min(topLag, 2 * lag)]);
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }
        if (bestLag <= minLag || bestLag >= maxLag) return;
        Confidence = ac[bestLag] / ac[0];

        if (Confidence < MinConfidence)
        {
            _weakHops += (int)(AnalysisSeconds * _fps);
            if (_weakHops > Timeout * _fps) { Bpm = 0; _candidateVotes = 0; }
            return;
        }
        _weakHops = 0;

        // Measure the peak at the furthest multiple of the period that fits:
        // one lag step is several percent of the period, and the error shrinks with k.
        double period = bestLag;
        for (var k = 4; k >= 1; k--)
        {
            var centre = (int)Math.Round(period * k, MidpointRounding.AwayFromZero); // Swift's rounded()
            if (centre + k + 1 > topLag) continue;
            var peak = centre;
            for (var lag = centre - k; lag <= centre + k; lag++) if (ac[lag] > ac[peak]) peak = lag;
            if (peak <= 0 || peak >= topLag) continue;
            float a = ac[peak - 1], b = ac[peak], c = ac[peak + 1];
            var denom = a - 2 * b + c;
            var offset = denom < 0 ? 0.5 * (a - c) / denom : 0;
            period = (peak + offset) / k;
            break;
        }
        Update(60 * _fps / period);
    }

    private void Update(double estimate)
    {
        if (Bpm > 0 && Math.Abs(estimate - Bpm) / Bpm < 0.04)
        {
            _candidateVotes = 0;
            Bpm += (estimate - Bpm) * 0.3;
            return;
        }
        if (_candidateVotes > 0 && Math.Abs(estimate - _candidate) / _candidate < 0.04) _candidateVotes++;
        else { _candidate = estimate; _candidateVotes = 1; }
        if (_candidateVotes >= (Bpm > 0 ? SwitchVotes : LockVotes))
        {
            Bpm = estimate;
            _candidateVotes = 0;
        }
    }
}
