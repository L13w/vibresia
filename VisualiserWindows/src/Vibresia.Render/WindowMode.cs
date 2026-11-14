using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Vibresia.Render;

/// <summary>
/// Windowed and full screen, switched while running (the Mac's Enter/Exit
/// Full Screen; F11 is Windows' key for it). The app starts windowed, as the
/// Mac does. Full screen is a borderless window over the whole monitor the
/// window is on, one pixel taller than it (see <see cref="GlWindow.Create"/>
/// for why); leaving it puts the window back where it was.
/// </summary>
public sealed class WindowMode
{
    /// <summary>The Mac's default window, in points (scaled by the display's scaling).</summary>
    public static readonly Vector2D<int> WindowedPoints = new(1280, 720);
    /// <summary>The Mac's smallest window (AppState.minimumWindowSize), in points.</summary>
    public static readonly Vector2D<int> MinimumPoints = new(420, 280);

    private readonly IWindow _window;
    private Rectangle<int>? _restore;

    public bool FullScreen { get; private set; }

    public WindowMode(IWindow window, bool fullScreen)
    {
        _window = window;
        FullScreen = fullScreen;
    }

    /// <summary>Sizes a new windowed window to the Mac's default in points, within the monitor, centred; and sets the Mac's minimum size.</summary>
    public static void PlaceWindowed(IWindow window, float scale)
    {
        SetMinimum(window, scale);
        if (MonitorArea.Containing(window) is not { } screen) return;
        var w = Math.Min((int)(WindowedPoints.X * scale), screen.Size.X * 9 / 10);
        var h = Math.Min((int)(WindowedPoints.Y * scale), screen.Size.Y * 9 / 10);
        window.Size = new Vector2D<int>(w, h);
        window.Position = screen.Origin + (screen.Size - window.Size) / 2;
    }

    private static unsafe void SetMinimum(IWindow window, float scale)
    {
        if (window.Native?.Glfw is not { } handle) return;
        Glfw.GetApi().SetWindowSizeLimits((WindowHandle*)handle, (int)(MinimumPoints.X * scale), (int)(MinimumPoints.Y * scale),
            Glfw.DontCare, Glfw.DontCare);
    }

    public void Toggle() => Set(!FullScreen);

    public void Set(bool fullScreen)
    {
        if (fullScreen == FullScreen) return;
        if (fullScreen)
        {
            if (MonitorArea.Containing(_window) is not { } screen) return;
            _restore = new Rectangle<int>(_window.Position, _window.Size);
            _window.WindowBorder = WindowBorder.Hidden;
            _window.Position = screen.Origin;
            _window.Size = screen.Size + new Vector2D<int>(0, 1);
        }
        else
        {
            _window.WindowBorder = WindowBorder.Resizable;
            if (_restore is { } r)
            {
                _window.Size = r.Size;
                _window.Position = r.Origin;
            }
            else PlaceWindowed(_window, MonitorArea.ContentScale(_window));
        }
        FullScreen = fullScreen;
    }
}
