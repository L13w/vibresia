namespace Vibresia.Core.Audio;

/// <summary>What the analyser saw this frame, plus whether a source is running at all.</summary>
public readonly record struct AudioReading(
    float Rms, float Bass, float Mid, float Treble, float Level,
    bool Beat, float BeatStrength, double Bpm, bool SourceIdle);

/// <summary>
/// The clocks and eased values upstream's renderer derives from the analyser
/// (OpenGLRenderer.swift: advanceDanceClock, advanceBeatClock, easeLevels):
/// everything moves only while there is sound and freezes in silence, the
/// beat clock phase-locks to detected beats without ever stepping, and no
/// clock runs backwards. Constants are upstream's.
/// </summary>
public sealed class MotionClocks
{
    private double _beatCorrectionLeft, _beatCorrectionRate;

    public double Activity { get; private set; }
    public double DanceClock { get; private set; }
    public double BeatClock { get; private set; }
    public double BeatClockBpm { get; private set; } = 120;
    public double ScrollClock { get; private set; }
    public double WallClock { get; private set; }
    public double LevelSmooth { get; private set; }
    public double BassSmooth { get; private set; }
    public double Presence { get; private set; }
    private double _presenceVelocity;

    public void Advance(AudioReading snap, double dt)
    {
        dt = Math.Min(0.1, Math.Max(0, dt));
        // Same thresholds as the analyser's noise gate (~-54 to -40 dBFS RMS).
        var target = snap.SourceIdle ? 0 : Math.Clamp((snap.Rms - 0.002) / 0.008, 0, 1);
        Activity += (target - Activity) * Math.Min(1, dt * 6);
        DanceClock += dt * Activity;
        ScrollClock += dt * Activity * (0.4 + snap.Level * 0.6);
        WallClock += dt;
        AdvanceBeatClock(snap, dt);
        EaseLevels(snap, dt);
    }

    private void AdvanceBeatClock(AudioReading snap, double dt)
    {
        if (dt <= 0) return;
        var bpm = snap.Bpm > 0 ? snap.Bpm : 120;
        BeatClockBpm += (bpm - BeatClockBpm) * (1 - Math.Exp(-dt / 0.5));
        var advance = dt * Activity * BeatClockBpm / 60;
        if (snap.Beat) _beatCorrectionLeft = (Math.Round(BeatClock, MidpointRounding.AwayFromZero) - BeatClock) * 0.35;

        // Exact critically damped step toward the correction, capped at 45% of
        // the normal advance so the clock never runs backwards or lurches.
        const double omega = 12.0;
        double x0 = _beatCorrectionLeft, v0 = _beatCorrectionRate;
        var decay = Math.Exp(-omega * dt);
        var x = (x0 + (v0 + omega * x0) * dt) * decay;
        var v = (v0 - omega * (v0 + omega * x0) * dt) * decay;
        var step = x0 - x;
        var limit = advance * 0.45;
        if (Math.Abs(step) > limit)
        {
            step = Math.Clamp(step, -limit, limit);
            x = x0 - step;
            v = -step / dt;
        }
        _beatCorrectionLeft = x;
        _beatCorrectionRate = v;
        BeatClock += advance + step;
    }

    private void EaseLevels(AudioReading snap, double dt)
    {
        static double Ease(double value, double target, double dt)
        {
            var tau = target > value ? 0.3 : 1.5;
            return value + (target - value) * (1 - Math.Exp(-dt / tau));
        }
        LevelSmooth = Ease(LevelSmooth, snap.Level, dt);
        BassSmooth = Ease(BassSmooth, snap.Bass, dt);
        var m = LevelSmooth * (0.3 + 0.7 * Activity);
        var xs = Math.Clamp((m - 0.004) / 0.031, 0, 1);
        var target = xs * xs * (3 - 2 * xs);
        const double omega = 2.5;
        var h = Math.Min(dt, 0.1);
        _presenceVelocity += (omega * omega * (target - Presence) - 2 * omega * _presenceVelocity) * h;
        Presence = Math.Clamp(Presence + _presenceVelocity * h, 0, 1);
    }

    /// <summary>
    /// Writes the uniforms. The wrap points (uTime 3600 s, uBeats 1024,
    /// uScroll 4096, uClock 3600 s) are upstream's: they keep float precision
    /// in the shader, at the cost of a jump once an hour or so.
    /// </summary>
    public void Fill(FrameUniforms u, AudioAnalyzer analyzer, AudioReading snap)
    {
        u.Time = (float)(DanceClock % 3600);
        u.Clock = (float)(WallClock % 3600);
        u.Beats = (float)(BeatClock % 1024);
        u.Scroll = (float)(ScrollClock % 4096);
        u.Active = (float)Activity;
        u.Idle = snap.SourceIdle ? 1 : 0;
        u.Bass = snap.Bass;
        u.Mid = snap.Mid;
        u.Treble = snap.Treble;
        u.Level = snap.Level;
        u.Beat = snap.Beat ? 1 : 0;
        u.BeatStrength = snap.BeatStrength;
        u.LevelSmooth = (float)LevelSmooth;
        u.BassSmooth = (float)BassSmooth;
        u.Presence = (float)Presence;
        analyzer.Bands.CopyTo(u.Bands);
        u.Bpm = snap.Bpm;
        u.Rms = snap.Rms;
    }
}
