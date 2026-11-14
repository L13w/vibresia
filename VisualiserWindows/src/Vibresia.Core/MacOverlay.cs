namespace Vibresia.Core;

/// <summary>
/// The timing of the two layers the Mac draws over every vibescape
/// (VisualiserMac UI/ContentView.swift and Audio/AudioEngine.swift):
/// the CRT overlay, shown only while there is sound and faded over 0.5 s;
/// and the beat flash, a pink tint whose strength follows the beat. The
/// drawing itself is in the renderer; this is the part that can be tested.
/// </summary>
public sealed class MacOverlay
{
    private const float SoundingOn = 0.008f;   // rms to start counting as sounding
    private const float SoundingOff = 0.003f;  // rms below which it stops (hysteresis)
    private const double CrtFadeSeconds = 0.5;
    private const double FlashFadeSeconds = 0.18;
    private const double FlashVisibleThreshold = 0.04;

    private readonly Tween _crt = new(CrtFadeSeconds, Easing.InOut);
    private readonly Tween _flash = new(FlashFadeSeconds, Easing.Out);
    private bool _flashVisible;

    /// <summary>AudioEngine.isSounding: a live source with rms over the threshold, with hysteresis.</summary>
    public bool IsSounding { get; private set; }

    /// <summary>The CRT overlay's opacity, 0..1.</summary>
    public float CrtOpacity => (float)_crt.Value;

    /// <summary>BeatFlashLevel.strength: the beat strength stepped to 0.02, zero below 0.04.</summary>
    public double FlashStrength { get; private set; }

    /// <summary>
    /// The flash's overall opacity. Upstream applies <c>.opacity(isVisible ? strength : 0)</c>
    /// twice (once more for Reduce Motion), so the visible opacity is that value squared.
    /// </summary>
    public float FlashOpacity => (float)(_flash.Value * _flash.Value);

    /// <summary>The gradient's centre alpha: <c>min(0.4, strength × 0.6)</c>, not animated.</summary>
    public float FlashCentreAlpha => (float)Math.Min(0.4, FlashStrength * 0.6);

    /// <param name="live">A source is running (upstream: live or demo).</param>
    /// <param name="reduceMotion">The user asked for less motion: the flash is held off.</param>
    public void Advance(FrameUniforms u, bool live, bool reduceMotion, double dt)
    {
        IsSounding = live && (IsSounding ? u.Rms > SoundingOff : u.Rms > SoundingOn);
        _crt.Target(IsSounding ? 1 : 0);
        _crt.Advance(dt);

        var raw = u.BeatStrength;
        FlashStrength = raw < 0.04f ? 0 : Math.Round(raw * 50.0) / 50.0;
        var visible = FlashStrength > FlashVisibleThreshold && !reduceMotion;
        var target = visible ? FlashStrength : 0;
        if (visible != _flashVisible) _flash.Target(target); // animated only when visibility flips
        else _flash.Snap(target);                             // otherwise it follows the strength at once
        _flashVisible = visible;
        _flash.Advance(dt);
    }
}

/// <summary>
/// The Mac draws vibescapes at 1× (OpenGLView.swift turns off
/// best-resolution surfaces: "full-screen shaders at Retina resolution are
/// ~4x the GPU work"). On Windows the equivalent of 1× is the framebuffer
/// divided by the display's scaling factor; the frame is then stretched to
/// the screen.
/// </summary>
public static class RenderScale
{
    public static (int Width, int Height) SceneSize(int framebufferWidth, int framebufferHeight, float contentScale)
    {
        var s = float.IsFinite(contentScale) && contentScale > 1 ? contentScale : 1;
        return (Math.Max(1, (int)Math.Round(framebufferWidth / s)), Math.Max(1, (int)Math.Round(framebufferHeight / s)));
    }
}
