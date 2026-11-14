namespace Vibresia.Core.Ui;

/// <summary>
/// The meter's 64 bars from the analyser's bands (upstream MeterView.swift):
/// each bar is the louder of its band and the next one along, scaled by 1.1
/// and capped at the panel's height. Thirds are magenta, violet and cyan.
/// </summary>
public static class MeterBars
{
    public const int Count = 64;
    /// <summary>The beat tint shows above this beat strength.</summary>
    public const float BeatTintFrom = 0.05f;

    /// <summary>Bar heights as fractions of the panel height, 0..1.</summary>
    public static float[] Heights(ReadOnlySpan<float> bands)
    {
        var heights = new float[Count];
        if (bands.IsEmpty) return heights;
        var step = Math.Max(1, bands.Length / Count);
        for (var i = 0; i < Count; i++)
        {
            var v1 = bands[Math.Min(bands.Length - 1, i * step)];
            var v2 = bands[Math.Min(bands.Length - 1, (i + 1) * step)];
            heights[i] = Math.Clamp(Math.Max(v1, v2) * 1.1f, 0, 1);
        }
        return heights;
    }

    /// <summary>Which third a bar is in: 0 magenta, 1 violet, 2 cyan.</summary>
    public static int Third(int bar) => bar < Count / 3 ? 0 : bar < Count * 2 / 3 ? 1 : 2;

    /// <summary>The magenta wash over the panel on a beat: strength × 0.18 above the threshold, else none.</summary>
    public static float BeatTint(float beatStrength) => beatStrength > BeatTintFrom ? beatStrength * 0.18f : 0;
}
