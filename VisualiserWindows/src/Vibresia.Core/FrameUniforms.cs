namespace Vibresia.Core;

/// <summary>
/// One frame's values for upstream's shader prelude uniforms. Names and
/// meanings are upstream's contract (VisualiserMac FunShaders.prelude):
/// custom vibescapes are written against them, so they are not ours to rename.
/// Uniforms for features v1 doesn't have (genre, track position, MIDI, keys)
/// stay at their "nothing to know" values.
/// </summary>
public sealed class FrameUniforms
{
    public const int BandCount = 96;

    public float Time;
    public float Bass, Mid, Treble, Level;
    public float Beat, BeatStrength;
    public float Idle;
    public float Active;
    public float Beats;
    public float Scroll;
    public float Clock;
    public float LevelSmooth, BassSmooth, Presence;
    public float MouseX, MouseY, MouseHere, MouseDown;
    public float ResolutionX, ResolutionY;
    public readonly float[] Bands = new float[BandCount];
}
