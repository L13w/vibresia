namespace Vibresia.Core.Vibescapes;

/// <summary>
/// Upstream's colour grade (VisualiserMac Render/Palette.swift): every
/// finished frame is mapped between a shadow and a highlight colour, then
/// re-saturated and lifted. Upstream's default palette is NEON, and the port
/// has no palette picker, so NEON is the only one here. Without the grade no
/// built-in looks the way it does on the Mac.
/// </summary>
public sealed record Palette(string Name, (float R, float G, float B) Shadow, (float R, float G, float B) Highlight,
    float Saturation, float Duotone, float Lift)
{
    public static Palette Neon { get; } = new("NEON", (0.07f, 0.00f, 0.11f), (1.00f, 0.36f, 0.95f),
        Saturation: 1.15f, Duotone: 0.25f, Lift: 0.22f);

    /// <summary>Built-ins with fixed pixel-art colours, which upstream draws ungraded (OpenGLRenderer.ungradedVibescapes).</summary>
    public static IReadOnlySet<string> UngradedBuiltins { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "badger", "llama", "nyan", "danceFloor" };

    /// <summary>
    /// The nine floats of the <c>uPalette[3]</c> uniform: shadow, highlight,
    /// and their midpoint — what upstream's bindPalette hands a shader.
    /// </summary>
    public float[] UniformValues() =>
    [
        Shadow.R, Shadow.G, Shadow.B,
        Highlight.R, Highlight.G, Highlight.B,
        (Shadow.R + Highlight.R) / 2, (Shadow.G + Highlight.G) / 2, (Shadow.B + Highlight.B) / 2,
    ];
}
