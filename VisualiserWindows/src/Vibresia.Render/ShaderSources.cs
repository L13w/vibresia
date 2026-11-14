using System.Reflection;

namespace Vibresia.Render;

/// <summary>
/// The upstream shaders, as extracted into VisualiserWindows/shaders and
/// embedded in this assembly. A built-in vibescape's fragment shader is the
/// shared prelude followed by its body, exactly as upstream assembles it.
/// </summary>
public static class ShaderSources
{
    // %(RecursiveDir) gives a backslash when built on Windows, so resource
    // names are normalised to forward slashes before lookup. Declared first:
    // static fields initialise in source order, and the ones below read through it.
    private static readonly Dictionary<string, string> ResourceNames =
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.Ordinal);

    public static string Prelude { get; } = Read("prelude.glsl");
    public static string VertexShader { get; } = Read("fullscreen.vert");
    public static string CustomWrapper { get; } = Read("custom-wrapper.glsl");
    public static string GradeVertex { get; } = Read("grade.vert");
    public static string GradeFragment { get; } = Read("grade.frag");

    /// <summary>Every shader built-in upstream defines, in its FunShaders mode-table order. The app lists them in the Mac's own order (VibescapeCatalog).</summary>
    public static IReadOnlyList<string> BuiltinOrder { get; } =
        Read("builtin/order.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public static string BuiltinBody(string name) => Read($"builtin/{name}.glsl");

    /// <summary>The complete fragment shader for a built-in.</summary>
    public static string BuiltinFragment(string name) => Prelude + BuiltinBody(name);

    /// <summary>
    /// The complete fragment shader for a custom vibescape body: prelude, the
    /// user's code, then the wrapper whose main() calls their render(). The
    /// wrapper goes last because GLSL has no forward declarations.
    /// </summary>
    public static string CustomFragment(string body) => Prelude + body + "\n" + CustomWrapper;

    /// <summary>Lines of <see cref="CustomFragment"/> before the user's first line, for mapping driver line numbers back to theirs.</summary>
    public static int CustomPreludeLines { get; } = Prelude.Count(c => c == '\n');

    private static string Read(string relative)
    {
        var name = "shaders/" + relative;
        using var s = (ResourceNames.TryGetValue(name, out var actual)
                ? Assembly.GetExecutingAssembly().GetManifestResourceStream(actual) : null)
            ?? throw new KeyNotFoundException($"embedded shader '{name}' not found");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
