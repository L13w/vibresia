namespace Vibresia.Tests;

/// <summary>Repository locations, found by walking up from the test binary.</summary>
internal static class Paths
{
    public static string Windows { get; } = FindWindowsRoot();
    public static string Shaders => Path.Combine(Windows, "shaders");
    public static string VisualiserMac => Path.Combine(Windows, "..", "VisualiserMac");

    private static string FindWindowsRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Vibresia.slnx")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualiserWindows root (Vibresia.slnx) not found above the test binary");
    }
}
