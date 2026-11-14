using Silk.NET.GLFW;
using Silk.NET.Windowing;

namespace Vibresia.Render;

/// <summary>Where the pointer is relative to the window, asked directly, so it answers while another app is in front.</summary>
public static class WindowPointer
{
    /// <summary>Whether the pointer is over the window's content right now.</summary>
    public static unsafe bool IsInside(IWindow window)
    {
        if (window.Native?.Glfw is not { } handle) return true;
        Glfw.GetApi().GetCursorPos((WindowHandle*)handle, out var x, out var y);
        return x >= 0 && y >= 0 && x < window.Size.X && y < window.Size.Y;
    }
}
