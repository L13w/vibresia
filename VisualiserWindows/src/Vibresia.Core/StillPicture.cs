namespace Vibresia.Core;

/// <summary>
/// The stand-in music a still picture of a vibescape is drawn with, as the
/// Mac draws its custom-vibescape previews (OpenGLRenderer.renderStillImage
/// with AudioEngine.syntheticState and AudioAnalyzer.setSyntheticState): a
/// 124 bpm kick with a sharp attack, a bass-heavy spectrum with treble
/// sparkle, a beat clock on a 120 bpm grid, and full activity. The Mac adds a
/// little random sparkle to the bands; here it is seeded, so the same
/// vibescape always gets the same picture.
/// </summary>
public static class StillPicture
{
    /// <summary>The moment the Mac's previews are taken at.</summary>
    public const double Seconds = 1.5;

    public const int Width = 320, Height = 180;

    public static FrameUniforms Uniforms(double seconds = Seconds, int width = Width, int height = Height)
    {
        const double bpm = 124;
        var beat = 60.0 / bpm;
        var phase = seconds % beat / beat;
        var kick = (float)Math.Exp(-phase * 5.5);
        var barPhase = (float)(seconds % (beat * 4) / (beat * 4));

        var u = new FrameUniforms();
        var random = new Random(1240);
        for (var i = 0; i < FrameUniforms.BandCount; i++)
        {
            var t = (float)i / FrameUniforms.BandCount;
            var b = (1 - t) * 0.7f + 0.3f * MathF.Sin(t * 18);
            var swell = 1 + kick * 0.30f * (1 - t);
            var drift = 0.12f * MathF.Sin(t * 18 + barPhase * 2 * MathF.PI);
            u.Bands[i] = Math.Clamp(b * swell + drift + 0.08f * (float)random.NextDouble(), 0, 1);
        }
        u.Rms = 0.35f + 0.18f * kick;
        u.Bass = Math.Min(1, 0.62f + 0.28f * kick);
        u.Mid = 0.42f + 0.12f * kick;
        u.Treble = Math.Min(1, 0.22f + 0.20f * barPhase);
        u.Level = 0.42f + 0.22f * kick;
        u.Beat = kick > 0.75f ? 1 : 0;
        u.BeatStrength = 0.62f * Math.Max(0.15f, kick);
        u.Bpm = bpm;
        // The slow values start from the music, and the clocks run on the still's own time.
        u.LevelSmooth = u.Level;
        u.BassSmooth = u.Bass;
        u.Presence = 1;
        u.Active = 1;
        u.Time = (float)seconds;
        u.Beats = (float)(seconds * 2);
        u.Scroll = (float)seconds;
        u.Clock = (float)seconds;
        u.ResolutionX = width;
        u.ResolutionY = height;
        return u;
    }
}
