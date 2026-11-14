namespace Vibresia.Core;

/// <summary>
/// A stand-in for real audio analysis: a steady 120 bpm groove computed from
/// time alone, so the first Windows build shows a vibescape moving before
/// audio capture exists. Upstream's vibescapes go flat in silence by design
/// (uActive = 0), which would make a time-only build look broken.
/// To be replaced by the audio analyser; nothing else should depend on it.
/// </summary>
public static class SyntheticPulse
{
    public const float Bpm = 120f;

    public static void Fill(FrameUniforms u, float seconds)
    {
        var beatsPerSecond = Bpm / 60f;
        var beats = seconds * beatsPerSecond;
        var phase = beats - MathF.Floor(beats);
        var hit = MathF.Exp(-phase * 6f);                    // sharp attack, exponential decay

        u.Time = seconds;
        u.Clock = seconds;
        u.Beats = beats;
        u.Beat = phase < 0.08f ? 1f : 0f;
        u.BeatStrength = hit;
        u.Bass = 0.25f + 0.5f * hit;
        u.Mid = 0.35f + 0.15f * MathF.Sin(seconds * 0.7f);
        u.Treble = 0.25f + 0.15f * MathF.Sin(seconds * 1.3f + 1f);
        u.Level = 0.4f + 0.3f * hit;
        u.LevelSmooth = 0.5f;
        u.BassSmooth = 0.45f;
        u.Presence = 1f;
        u.Active = 1f;
        u.Idle = 0f;
        u.Scroll = seconds * 0.5f;                            // forward-only, like upstream's
        for (var i = 0; i < FrameUniforms.BandCount; i++)
        {
            var x = i / (float)FrameUniforms.BandCount;
            var tilt = 1f - 0.7f * x;                         // music is bass-heavy
            u.Bands[i] = Math.Clamp(tilt * (0.3f + 0.5f * hit * (1f - x))
                + 0.1f * MathF.Sin(seconds * 2f + i * 0.4f), 0f, 1f);
        }
    }
}
