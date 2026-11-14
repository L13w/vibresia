using Silk.NET.Windowing;

namespace Vibresia.Render;

/// <summary>
/// The display scaling of the monitor the window is on (1.5 at Windows'
/// 150%): the size of a Mac point in pixels, used for the scene's 1× size
/// and the interface's measurements. Looked up when the window moves,
/// resizes or changes state rather than every frame, since finding the
/// monitor walks GLFW's monitor list.
/// </summary>
public sealed class DisplayScale
{
    private readonly IWindow _window;

    public float Value { get; private set; }

    /// <summary>Raised on the window thread when the scale changes (the window moved to a monitor with different scaling).</summary>
    public event Action<float>? Changed;

    public DisplayScale(IWindow window)
    {
        _window = window;
        Value = MonitorArea.ContentScale(window);
        window.Move += _ => Refresh();
        window.Resize += _ => Refresh();
        window.StateChanged += _ => Refresh();
    }

    public void Refresh()
    {
        var now = MonitorArea.ContentScale(_window);
        if (now == Value) return;
        Value = now;
        Changed?.Invoke(now);
    }
}
