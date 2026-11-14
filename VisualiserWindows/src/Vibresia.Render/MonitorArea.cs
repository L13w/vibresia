using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace Vibresia.Render;

/// <summary>
/// The whole of the primary monitor, taskbar included. Silk.NET's
/// <c>IMonitor.Bounds</c> is GLFW's work area — the screen minus the
/// taskbar — so a borderless window sized from it stops short of the bottom
/// edge with the taskbar still showing (seen on a 3840×2160 display reported
/// as 3840×2088). GLFW's monitor position and current video mode give the
/// real rectangle.
/// </summary>
public static unsafe class MonitorArea
{
    public static Rectangle<int>? Primary()
    {
        var glfw = Glfw.GetApi();
        var monitor = glfw.GetPrimaryMonitor();
        if (monitor == null) return null;
        var mode = glfw.GetVideoMode(monitor);
        if (mode == null) return null;
        glfw.GetMonitorPos(monitor, out var x, out var y);
        return new Rectangle<int>(x, y, mode->Width, mode->Height);
    }

    /// <summary>The display scaling of the monitor the window is on (1.5 at Windows' 150%), from GLFW; 1 when unknown.</summary>
    /// <remarks>Silk.NET binds only GLFW's per-monitor scale, so the monitor under the window's centre is used.</remarks>
    public static float ContentScale(Silk.NET.Windowing.IWindow window)
    {
        var glfw = Glfw.GetApi();
        var centre = window.Position + window.Size / 2;
        var monitors = glfw.GetMonitors(out var count);
        Silk.NET.GLFW.Monitor* chosen = glfw.GetPrimaryMonitor();
        for (var i = 0; i < count; i++)
        {
            var mode = glfw.GetVideoMode(monitors[i]);
            if (mode == null) continue;
            glfw.GetMonitorPos(monitors[i], out var mx, out var my);
            if (centre.X >= mx && centre.X < mx + mode->Width && centre.Y >= my && centre.Y < my + mode->Height)
            {
                chosen = monitors[i];
                break;
            }
        }
        if (chosen == null) return 1;
        glfw.GetMonitorContentScale(chosen, out var x, out var y);
        var s = Math.Max(x, y);
        return float.IsFinite(s) && s > 0 ? s : 1;
    }
}
