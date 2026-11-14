using Vibresia.Core;
using Vibresia.Render;

namespace Vibresia.Tests;

public sealed class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vibresia-log-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WritesOneFormattedLinePerEvent()
    {
        using (var log = new Log(_dir)) log.Error("shader", "kaleidoscope failed");
        var line = Assert.Single(File.ReadAllLines(Path.Combine(_dir, Log.FileName)));
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} [+-]\d\d:\d\d ERROR \[shader\] kaleidoscope failed$", line);
    }

    [Fact]
    public void RollsAtCapAndKeepsOnlyConfiguredFiles()
    {
        using (var log = new Log(_dir, maxBytes: 200, keep: 3))
            for (var i = 0; i < 40; i++) log.Info("t", $"line {i:D3} padding padding");

        var files = Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(new[] { "vibresia.log", "vibresia.log.1", "vibresia.log.2" }, files);
        Assert.All(Directory.GetFiles(_dir), f => Assert.True(new FileInfo(f).Length <= 200));
        // The newest line is in the live file; the oldest have rolled off entirely.
        Assert.Contains("line 039", File.ReadAllText(Path.Combine(_dir, "vibresia.log")));
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => File.ReadAllText(f).Contains("line 000"));
    }

    [Fact]
    public void KeepsLoggingWhenARollCannotMoveFiles()
    {
        // A directory squatting on the roll target makes the move throw.
        Directory.CreateDirectory(Path.Combine(_dir, "vibresia.log.1"));
        using (var log = new Log(_dir, maxBytes: 120, keep: 3))
            for (var i = 0; i < 10; i++) log.Info("t", $"line {i} padding padding padding");
        Assert.Contains("line 9", File.ReadAllText(Path.Combine(_dir, Log.FileName)));
    }

    [Fact]
    public void AppendsAcrossRuns()
    {
        using (var a = new Log(_dir)) a.Info("t", "first");
        using (var b = new Log(_dir)) b.Info("t", "second");
        Assert.Equal(2, File.ReadAllLines(Path.Combine(_dir, Log.FileName)).Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

public class FrameStatsTests
{
    [Fact]
    public void SummarisesOncePerPeriodThenResets()
    {
        var stats = new FrameStats(periodSeconds: 1.0, longFrameSeconds: 0.02);
        string? summary = null;
        for (var i = 0; i < 59; i++) Assert.Null(stats.Add(1.0 / 60));
        summary = stats.Add(0.05); // one long frame completes the period
        Assert.NotNull(summary);
        Assert.Contains("frames 60", summary);
        Assert.Contains("over 20 ms: 1", summary);
        Assert.Null(stats.Add(1.0 / 60)); // a fresh period has started
    }
}

public class GlVersionTests
{
    [Theory]
    [InlineData("4.5 (Core Profile) Mesa 26.0.3", true)]
    [InlineData("3.3.0 NVIDIA 560.94", true)]
    [InlineData("3.2.0 - Build 9.17", false)]
    [InlineData("2.1 Mesa", false)]
    [InlineData("garbage", false)]
    public void ChecksTheLeadingMajorMinor(string version, bool ok) =>
        Assert.Equal(ok, GlVersion.MeetsRequirement(version));
}

public class AppVersionTests
{
    [Fact]
    public void EmbedsTheVersionFile() =>
        Assert.Equal(File.ReadAllText(Path.Combine(Paths.Windows, "VERSION")).Trim(), AppVersion.Current);
}
