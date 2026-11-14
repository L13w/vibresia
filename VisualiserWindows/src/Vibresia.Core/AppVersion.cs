using System.Reflection;

namespace Vibresia.Core;

/// <summary>The Windows build's version, from VisualiserWindows/VERSION (embedded at build time).</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VERSION")
            ?? throw new InvalidOperationException("VERSION resource missing from Vibresia.Core");
        using var r = new StreamReader(s);
        return r.ReadToEnd().Trim();
    }
}
