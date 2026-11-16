namespace Vibresia.Tests;

/// <summary>Repository locations, found by walking up from the test binary.</summary>
internal static class Paths
{
    public static string Windows { get; } = FindWindowsRoot();
    public static string Shaders => Path.Combine(Windows, "shaders");
    public static string VisualiserMac => Path.Combine(Windows, "..", "VisualiserMac");

    /// <summary>
    /// Reads a file another handle still has open for writing, as a running
    /// log does. File.ReadAllText refuses to share with a writer, which Linux
    /// ignores and Windows enforces, so it fails there while the Log is open.
    /// </summary>
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string FindWindowsRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Vibresia.slnx")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualiserWindows root (Vibresia.slnx) not found above the test binary");
    }
}
