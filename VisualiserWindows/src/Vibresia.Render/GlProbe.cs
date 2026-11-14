using Silk.NET.GLFW;

namespace Vibresia.Render;

/// <summary>
/// Asks GLFW directly whether this machine can make the GL context we need,
/// before Silk.NET's window does. Silk's own GLFW error callback throws, and
/// when context creation fails that throw happens inside native code, which
/// .NET cannot unwind: the process dies with no message. Probing first, with
/// a callback that only records, turns "old or broken driver" into a plain
/// managed failure we can show the user.
/// </summary>
public static unsafe class GlProbe
{
    private static string? _lastError;
    // Held in a static so the delegate outlives the native code holding a pointer to it.
    private static readonly GlfwCallbacks.ErrorCallback Recorder = (code, description) =>
        _lastError = $"{code}: {description}";

    /// <returns>null when the context can be made, else GLFW's reason.</returns>
    public static string? CannotCreate(int major, int minor)
    {
        var glfw = Glfw.GetApi();
        _lastError = null;
        glfw.SetErrorCallback(Recorder);
        if (!glfw.Init()) return _lastError ?? "GLFW could not initialise";

        glfw.DefaultWindowHints();
        glfw.WindowHint(WindowHintBool.Visible, false);
        glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGL);
        glfw.WindowHint(WindowHintInt.ContextVersionMajor, major);
        glfw.WindowHint(WindowHintInt.ContextVersionMinor, minor);
        glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true);

        var handle = glfw.CreateWindow(64, 64, "Vibresia GL probe", null, null);
        if (handle == null) return _lastError ?? "no OpenGL context was created";
        glfw.DestroyWindow(handle);
        glfw.DefaultWindowHints();
        return null;
    }
}
