namespace Vibresia.Core.Ui;

/// <summary>
/// When the header and meter are on screen (upstream ContentView.swift
/// PointerWatcher and chromeVisible): the pointer is polled against the
/// window every 0.15 s, because hover events stop arriving while another app
/// is in front, which is when a visualiser is usually being watched. Half a
/// second away counts as gone. An open drop-down keeps the chrome up. The
/// slide in and out is eased over 0.35 s.
/// </summary>
public sealed class ChromeState
{
    public const double PollSeconds = 0.15;
    public const double AwaySeconds = 0.5;
    public const double SlideSeconds = 0.35;
    /// <summary>How far the header slides up when hidden, in points.</summary>
    public const float HeaderSlidePoints = 110;
    /// <summary>How far the meter slides right when hidden, in points.</summary>
    public const float MeterSlidePoints = 300;

    private readonly Tween _shown = new(SlideSeconds, Easing.InOut, initial: 1);
    private double _clock, _sincePoll = PollSeconds;
    private double? _leftAt;

    /// <summary>Whether the pointer counts as over the window (it starts that way, as upstream's does).</summary>
    public bool PointerInside { get; private set; } = true;

    /// <summary>Whether the chrome is meant to be up (where the slide is heading).</summary>
    public bool Visible { get; private set; } = true;

    /// <summary>The slide, 0 hidden to 1 shown: offsets and opacity follow it.</summary>
    public float Shown => (float)_shown.Value;

    /// <param name="pointerInWindow">Asked at each poll: is the pointer over the window right now?</param>
    /// <param name="menuOpen">A drop-down is open.</param>
    public void Advance(double dt, Func<bool> pointerInWindow, bool menuOpen)
    {
        _clock += dt;
        _sincePoll += dt;
        if (_sincePoll >= PollSeconds)
        {
            _sincePoll = 0;
            if (pointerInWindow())
            {
                _leftAt = null;
                PointerInside = true;
            }
            else
            {
                _leftAt ??= _clock;
                if (_clock - _leftAt.Value > AwaySeconds) PointerInside = false;
            }
        }
        Visible = PointerInside || menuOpen;
        _shown.Target(Visible ? 1 : 0);
        _shown.Advance(dt);
    }
}
