namespace Vibresia.Core;

/// <summary>
/// Collects frame times and, once a period has elapsed, returns a one-line
/// summary for the health log: average and 99th-percentile frame time, and
/// how many frames ran long. That is how stutter on the owner's PC becomes
/// visible in a log file.
/// </summary>
public sealed class FrameStats(double periodSeconds = 60, double longFrameSeconds = 1.0 / 30)
{
    private readonly List<double> _frames = [];
    private double _elapsed;

    /// <returns>A summary when the period completes, else null.</returns>
    public string? Add(double frameSeconds)
    {
        _frames.Add(frameSeconds);
        _elapsed += frameSeconds;
        if (_elapsed < periodSeconds) return null;

        _frames.Sort();
        var avg = _frames.Average();
        var p99 = _frames[Math.Min(_frames.Count - 1, (int)(_frames.Count * 0.99))];
        var slow = _frames.Count(f => f > longFrameSeconds);
        var summary = $"frames {_frames.Count} in {_elapsed:F0} s; avg {avg * 1000:F1} ms; p99 {p99 * 1000:F1} ms; over {longFrameSeconds * 1000:F0} ms: {slow}";
        _frames.Clear();
        _elapsed = 0;
        return summary;
    }
}
