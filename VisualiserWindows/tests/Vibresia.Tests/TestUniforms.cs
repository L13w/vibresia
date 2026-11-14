using Vibresia.Core;

namespace Vibresia.Tests;

/// <summary>Fixed uniform values that look like music playing, for render tests that don't exercise audio.</summary>
internal static class TestUniforms
{
    public static void Music(FrameUniforms u)
    {
        u.Time = 3.25f; u.Clock = 3.25f; u.Beats = 6.5f; u.Scroll = 1.6f;
        u.Active = 1; u.Presence = 1;
        u.Bass = 0.6f; u.Mid = 0.4f; u.Treble = 0.3f; u.Level = 0.45f;
        u.BeatStrength = 0.5f; u.LevelSmooth = 0.45f; u.BassSmooth = 0.55f;
        for (var i = 0; i < FrameUniforms.BandCount; i++) u.Bands[i] = 0.7f * (1 - i / 96f) + 0.1f;
    }
}
