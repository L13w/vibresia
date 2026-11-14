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
}
