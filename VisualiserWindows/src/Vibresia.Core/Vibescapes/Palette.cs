namespace Vibresia.Core.Vibescapes;

/// <summary>
/// Upstream's colour grade (VisualiserMac Render/Palette.swift): every
/// finished frame is mapped between a shadow and a highlight colour, then
/// re-saturated and lifted. Without the grade no built-in looks the way it
/// does on the Mac. The five palettes and their order are upstream's; the
/// first, NEON, is the default and what every launch starts on, because the
/// Mac doesn't remember the choice either.
/// </summary>
public sealed record Palette(string Name, (float R, float G, float B) Shadow, (float R, float G, float B) Highlight,
    float Saturation, float Duotone, float Lift)
{
    public static Palette Neon { get; } = new("NEON", (0.07f, 0.00f, 0.11f), (1.00f, 0.36f, 0.95f),
        Saturation: 1.15f, Duotone: 0.25f, Lift: 0.22f);

    /// <summary>Upstream's palettes in picker order (Palette.all).</summary>
    public static IReadOnlyList<Palette> All { get; } =
    [
        Neon,
        new("EMBER", (0.09f, 0.02f, 0.00f), (1.00f, 0.62f, 0.24f), Saturation: 1.10f, Duotone: 0.72f, Lift: 0.28f),
        new("TOXIC", (0.00f, 0.07f, 0.01f), (0.56f, 1.00f, 0.34f), Saturation: 1.25f, Duotone: 0.72f, Lift: 0.24f),
        new("ABYSS", (0.00f, 0.02f, 0.11f), (0.56f, 0.86f, 1.00f), Saturation: 1.05f, Duotone: 0.78f, Lift: 0.32f),
        new("MONO", (0.02f, 0.02f, 0.03f), (1.00f, 1.00f, 1.00f), Saturation: 0.00f, Duotone: 0.00f, Lift: 0.08f),
    ];

    /// <summary>The palette at <paramref name="index"/>; NEON for an index out of range, as upstream's palette(at:).</summary>
    public static Palette At(int index) => index >= 0 && index < All.Count ? All[index] : All[0];

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
