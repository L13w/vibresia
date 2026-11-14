namespace Vibresia.Core;

/// <summary>
/// The pointer and keyboard as upstream hands them to shaders
/// (VisualiserMac UI/InputState.swift and UI/OpenGLView.swift): the pointer
/// in the same coordinates as <c>screenP()</c> (x across, y up from the
/// middle, both scaled by the height), eased so it drifts rather than
/// jumps; <c>uMouseHere</c> 0 until the pointer has moved over the window;
/// the last typed character's code, and whether a key is held. Events arrive
/// on the window thread, which is also the render thread.
/// </summary>
public sealed class PointerInput
{
    /// <summary>Upstream's easing time constant: the picture drifts after the pointer instead of swinging with every flick.</summary>
    public const double EaseSeconds = 0.15;

    private double _targetX, _targetY, _easedX, _easedY;

    public bool Here { get; private set; }
    public bool Down { get; set; }
    public float Key { get; private set; }
    public bool KeyDown { get; set; }

    /// <summary>The pointer moved, in window pixels from the top-left of a window this size.</summary>
    public void Move(double x, double y, double width, double height)
    {
        if (height <= 0) return;
        _targetX = (x - width / 2) / height;
        _targetY = (height / 2 - y) / height; // window y runs down; shader y runs up
        Here = true;
    }

    /// <summary>A character was typed. Keys with no character (arrows, F-keys) leave the last one alone, as upstream does.</summary>
    public void Typed(char c) => Key = c;

    /// <summary>Eases the pointer by <paramref name="dt"/> and writes the input uniforms.</summary>
    public void Fill(FrameUniforms u, double dt)
    {
        var k = 1 - Math.Exp(-Math.Clamp(dt, 0, 0.1) / EaseSeconds);
        _easedX += (_targetX - _easedX) * k;
        _easedY += (_targetY - _easedY) * k;
        u.MouseX = (float)_easedX;
        u.MouseY = (float)_easedY;
        u.MouseHere = Here ? 1 : 0;
        u.MouseDown = Down ? 1 : 0;
        u.Key = Key;
        u.KeyDown = KeyDown ? 1 : 0;
    }
}
