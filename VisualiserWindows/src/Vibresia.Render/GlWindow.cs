using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Vibresia.Render;

/// <summary>Creates a window with an OpenGL 3.3 core context, or says plainly why it can't.</summary>
public static class GlWindow
{
    public static WindowOptions Options(bool visible, bool fullscreen, int major = 3, int minor = 3)
    {
        var o = WindowOptions.Default with
        {
            Title = "VIBRESIA", // the Mac's window title
            Size = new Vector2D<int>(1280, 720),
            IsVisible = visible,
            VSync = true,
            // Full screen is a borderless window covering the monitor, not
            // exclusive full-screen mode: exclusive mode switching left a 4K
            // Intel UHD monitor black, and borderless is what Windows itself
            // treats as the reliable full-screen path for multi-monitor setups.
            WindowState = WindowState.Normal,
            WindowBorder = fullscreen ? WindowBorder.Hidden : WindowBorder.Resizable,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible,
                new APIVersion(major, minor)),
        };
        return o;
    }

    /// <summary>Initialises the window and its GL context. Throws <see cref="GlUnavailableException"/> when the driver refuses.</summary>
    /// <remarks>
    /// The full-screen window is one pixel taller than the monitor. Windows
    /// treats a window that exactly covers a monitor as a full-screen app and
    /// switches how it presents it when the pointer moves to another display:
    /// on a 4K monitor driven by Intel UHD that blanked the screen for 10–20 s
    /// and stalled frames for half a second, while a window overhanging by a
    /// pixel stays an ordinary composed window and did neither.
    /// </remarks>
    public static (IWindow Window, GL Gl, GlInfo Info) Create(WindowOptions options)
    {
        var (major, minor) = (options.API.Version.MajorVersion, options.API.Version.MinorVersion);
        if (GlProbe.CannotCreate(major, minor) is { } reason)
            throw new GlUnavailableException(
                $"This PC's graphics driver couldn't provide OpenGL {major}.{minor} (core profile), which Vibresia needs. Updating the graphics driver usually fixes this.",
                new InvalidOperationException(reason));

        var window = Window.Create(options);
        window.Initialize();
        if (options.WindowBorder == WindowBorder.Hidden && MonitorArea.Containing(window) is { } screen)
        {
            window.Position = screen.Origin;
            window.Size = screen.Size + new Vector2D<int>(0, 1);
        }
        else if (options.WindowBorder != WindowBorder.Hidden)
            WindowMode.PlaceWindowed(window, MonitorArea.ContentScale(window));

        var gl = GL.GetApi(window);
        var info = GlInfo.Query(gl);
        if (!GlVersion.MeetsRequirement(info.Version))
        {
            window.Dispose();
            throw new GlUnavailableException(
                $"This PC's graphics driver offers OpenGL {info.Version}; Vibresia needs 3.3 or newer. Updating the graphics driver usually fixes this.");
        }
        return (window, gl, info);
    }
}
