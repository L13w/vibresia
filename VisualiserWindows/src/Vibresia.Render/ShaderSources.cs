using System.Reflection;

namespace Vibresia.Render;

/// <summary>
/// The upstream shaders, as extracted into VisualiserWindows/shaders and
/// embedded in this assembly. A built-in vibescape's fragment shader is the
/// shared prelude followed by its body, exactly as upstream assembles it.
/// </summary>
public static class ShaderSources
{
    public static string Prelude { get; } = Read("prelude.glsl");
    public static string VertexShader { get; } = Read("fullscreen.vert");
    public static string CustomWrapper { get; } = Read("custom-wrapper.glsl");

    /// <summary>Built-in vibescape names in upstream's catalog order.</summary>
    public static IReadOnlyList<string> BuiltinOrder { get; } =
        Read("builtin/order.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public static string BuiltinBody(string name) => Read($"builtin/{name}.glsl");

    /// <summary>The complete fragment shader for a built-in.</summary>
    public static string BuiltinFragment(string name) => Prelude + BuiltinBody(name);

    private static string Read(string relative)
    {
        var name = "shaders/" + relative;
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new KeyNotFoundException($"embedded shader '{name}' not found");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
