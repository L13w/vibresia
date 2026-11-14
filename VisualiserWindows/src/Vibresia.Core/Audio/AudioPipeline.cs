namespace Vibresia.Core.Audio;

public enum AudioStatus
{
    /// <summary>A source is running and sound is arriving.</summary>
    Playing,
    /// <summary>A source is running but nothing has been audible for a while — usually just nothing playing.</summary>
    NothingPlaying,
    /// <summary>The source stopped and couldn't be restarted.</summary>
    Failed,
    /// <summary>No source.</summary>
    None,
}

/// <summary>
/// Owns the current audio source and turns its samples into a frame's
/// uniforms: ring buffer → analyser → motion clocks. Also decides what the
/// screen should say about audio. Windows loopback capture delivers nothing
/// at all while nothing is playing, so silence is reported as a gentle
/// "nothing playing" hint, and only a real capture failure as an error —
/// either way never just swallowed (a dead source is the bug nothing else shows).
/// All methods are called on the render thread.
/// </summary>
public sealed class AudioPipeline : IDisposable
{
    /// <summary>How long without audible sound before the screen says so.</summary>
    public const double SilenceHintSeconds = 5;
    private const float AudibleRms = 0.002f; // the analyser's noise-gate floor

    private readonly Log _log;
    private readonly AudioRingBuffer _ring = new();
    private readonly AudioAnalyzer _analyzer;
    private readonly MotionClocks _clocks = new();
    private IAudioSource? _source;
    private volatile string? _pendingFailure;
    private volatile bool _pendingDeviceChange;
    private long _lastWritten;
    private double _silentFor;

    public AudioStatus Status { get; private set; } = AudioStatus.None;
    public string? SourceName => _source?.Name;
    public string? FailureMessage { get; private set; }
    public AudioAnalyzer Analyzer => _analyzer;
    public MotionClocks Clocks => _clocks;

    public AudioPipeline(Log log)
    {
        _log = log;
        _analyzer = new AudioAnalyzer(_ring);
    }

    /// <summary>Stops the current source (if any) and starts <paramref name="source"/>.</summary>
    public void Use(IAudioSource source)
    {
        StopCurrent();
        _source = source;
        source.Failed += OnFailed;
        source.DeviceChanged += OnDeviceChanged;
        FailureMessage = null;
        _silentFor = 0;
        _lastWritten = 0;
        try
        {
            source.Start(_ring);
            Status = AudioStatus.Playing;
            _log.Info("audio", $"started {source.Name} at {_ring.SampleRate} Hz");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException
                                      or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            Fail($"couldn't start {source.Name}: {e.Message}");
        }
    }

    private void OnFailed(string message) => _pendingFailure = message;
    private void OnDeviceChanged() => _pendingDeviceChange = true;

    /// <summary>Once per rendered frame: analyse, advance the clocks, write the uniforms.</summary>
    public void Tick(FrameUniforms u, double dt)
    {
        if (_pendingFailure is { } failure)
        {
            _pendingFailure = null;
            Fail(failure);
        }
        if (_pendingDeviceChange && _source is { } current)
        {
            _pendingDeviceChange = false;
            _log.Info("audio", "default playback device changed; restarting capture");
            Use(current);
        }

        _analyzer.Tick();
        TrackSilence(dt);
        var idle = Status is AudioStatus.Failed or AudioStatus.None;
        var snap = new AudioReading(_analyzer.Rms, _analyzer.Bass, _analyzer.Mid, _analyzer.Treble, _analyzer.Level,
            _analyzer.Beat, _analyzer.BeatStrength, _analyzer.Bpm, idle);
        _clocks.Advance(snap, dt);
        _clocks.Fill(u, _analyzer, snap);
    }

    private void TrackSilence(double dt)
    {
        if (Status is AudioStatus.Failed or AudioStatus.None) return;
        var written = _ring.TotalWritten;
        var arriving = written != _lastWritten;
        _lastWritten = written;
        var audible = arriving && _analyzer.Rms >= AudibleRms;
        _silentFor = audible ? 0 : _silentFor + dt;

        if (Status == AudioStatus.Playing && _silentFor >= SilenceHintSeconds)
        {
            Status = AudioStatus.NothingPlaying;
            _log.Info("audio", $"nothing audible from {_source?.Name} for {SilenceHintSeconds:F0} s");
        }
        else if (Status == AudioStatus.NothingPlaying && audible)
        {
            Status = AudioStatus.Playing;
            _log.Info("audio", $"sound from {_source?.Name} again");
        }
    }

    private void Fail(string message)
    {
        Status = AudioStatus.Failed;
        FailureMessage = message;
        _log.Error("audio", message);
    }

    private void StopCurrent()
    {
        if (_source is null) return;
        _source.Failed -= OnFailed;
        _source.DeviceChanged -= OnDeviceChanged;
        _source.Stop();
        _log.Info("audio", $"stopped {_source.Name}");
        _source = null;
        Status = AudioStatus.None;
    }

    public void Dispose()
    {
        var s = _source;
        StopCurrent();
        s?.Dispose();
    }
}
