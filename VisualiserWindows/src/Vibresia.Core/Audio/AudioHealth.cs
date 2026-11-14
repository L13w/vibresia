namespace Vibresia.Core.Audio;

/// <summary>
/// A once-a-period summary of what the analyser heard, for the log: loudness,
/// how much of the time the noise gate held the picture down, how lively the
/// motion was, and the tempo. It is how "the screen went dark for a while" is
/// told apart, from a log alone, as quiet music (gated) or broken capture
/// (no signal at all).
/// </summary>
public sealed class AudioHealth(double periodSeconds = 60)
{
    private double _elapsed, _rmsSum, _activitySum, _bpmSum;
    private float _rmsMax;
    private int _frames, _closed, _partly, _bpmFrames;

    /// <param name="gate">The analyser's noise gate this frame: 0 closed (treated as silence), 1 fully open.</param>
    /// <param name="activity">uActive this frame.</param>
    /// <returns>A summary when the period completes, else null.</returns>
    public string? Add(float rms, float gate, float activity, double bpm, double dt)
    {
        _frames++;
        _elapsed += dt;
        _rmsSum += rms;
        _rmsMax = Math.Max(_rmsMax, rms);
        _activitySum += activity;
        if (gate <= 0) _closed++;
        else if (gate < 1) _partly++;
        if (bpm > 0) { _bpmSum += bpm; _bpmFrames++; }
        if (_elapsed < periodSeconds) return null;

        var summary = $"{_elapsed:F0} s: rms avg {_rmsSum / _frames:F4} max {_rmsMax:F4}; " +
                      $"gate closed {100.0 * _closed / _frames:F0}% partly {100.0 * _partly / _frames:F0}%; " +
                      $"activity avg {_activitySum / _frames:F2}; " +
                      (_bpmFrames > 0 ? $"bpm {_bpmSum / _bpmFrames:F0}" : "no tempo");
        _elapsed = _rmsSum = _activitySum = _bpmSum = 0;
        _rmsMax = 0;
        _frames = _closed = _partly = _bpmFrames = 0;
        return summary;
    }
}
