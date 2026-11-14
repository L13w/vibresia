namespace Vibresia.Core.Vibescapes;

/// <summary>
/// The few built-ins that need more than the shared uniforms keep a little
/// state on the CPU, advanced each frame only while that built-in is showing,
/// as upstream does (VisualiserMac Render/OpenGLRenderer.swift,
/// drawShaderProgram and its helpers):
/// Spectrum's falling peak caps, Badger's big-beat overlay, and Hamster
/// Dance's sprite clock.
/// </summary>
public sealed class BuiltinState
{
    public const int PeakCount = 48;
    private const float PeakFallPerSecond = 0.35f;

    private const double OverlaySeconds = 1.6;
    private double _now;
    private float _bigBeatAverage = 0.3f;
    private int _overlayCount;
    private double _overlayStart;

    private const double SpriteNativeBpm = 137.0;

    /// <summary><c>uPeaks</c>: the spectrum's band pairs, each holding its highest value and falling slowly.</summary>
    public float[] Peaks { get; } = new float[PeakCount];

    /// <summary><c>uEventKind</c>: 0 no overlay, 1 or 2 which one is showing.</summary>
    public float EventKind { get; private set; }

    /// <summary><c>uEventAge</c>: seconds since the overlay began.</summary>
    public float EventAge => (float)(_now - _overlayStart);

    /// <summary><c>uSpriteClock</c>: the hamsters' dance clock; it follows the tempo and stops in silence.</summary>
    public double SpriteClock { get; private set; }

    /// <summary>Advances the state of the built-in that is showing; the others stay where they were.</summary>
    public void Advance(string builtin, FrameUniforms u, double dt)
    {
        _now += dt;
        switch (builtin)
        {
            case "spectrum":
                for (var i = 0; i < PeakCount; i++)
                {
                    var v = Math.Max(u.Bands[i * 2], u.Bands[i * 2 + 1]);
                    Peaks[i] = Math.Max(v, Peaks[i] - (float)dt * PeakFallPerSecond);
                }
                break;
            case "badger":
                AdvanceOverlay(u);
                break;
            case "hamster":
                if (u.Idle < 0.5f)
                {
                    var rate = u.Bpm > 0 ? Math.Clamp(u.Bpm / SpriteNativeBpm, 0.5, 1.5) : 1;
                    SpriteClock = (SpriteClock + Math.Min(0.1, dt) * rate * u.Active) % 3600;
                }
                break;
        }
    }

    private void AdvanceOverlay(FrameUniforms u)
    {
        if (u.Beat > 0.5f)
        {
            var isBig = u.BeatStrength > Math.Max(0.35f, _bigBeatAverage * 1.3f);
            _bigBeatAverage += (u.BeatStrength - _bigBeatAverage) * 0.1f;
            if (isBig && (EventKind == 0 || _now - _overlayStart > OverlaySeconds))
            {
                _overlayCount++;
                EventKind = _overlayCount % 3 == 0 ? 2 : 1;
                _overlayStart = _now;
            }
        }
        if (EventKind != 0 && _now - _overlayStart > OverlaySeconds) EventKind = 0;
    }
}
