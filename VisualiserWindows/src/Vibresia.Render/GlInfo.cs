using System.Text.RegularExpressions;
using Silk.NET.OpenGL;

namespace Vibresia.Render;

/// <summary>What the driver says it is. Logged at startup so a failure on a GPU we never see can be diagnosed from the log.</summary>
public sealed record GlInfo(string Vendor, string Renderer, string Version, string ShadingLanguage)
{
    public static GlInfo Query(GL gl) => new(
        gl.GetStringS(StringName.Vendor) ?? "?",
        gl.GetStringS(StringName.Renderer) ?? "?",
        gl.GetStringS(StringName.Version) ?? "?",
        gl.GetStringS(StringName.ShadingLanguageVersion) ?? "?");

    public override string ToString() =>
        $"vendor='{Vendor}' renderer='{Renderer}' version='{Version}' glsl='{ShadingLanguage}'";
}

public static partial class GlVersion
{
    public static readonly (int Major, int Minor) Required = (3, 3);

    [GeneratedRegex(@"(?<maj>\d+)\.(?<min>\d+)")]
    private static partial Regex Leading();

    /// <summary>Parses the leading "major.minor" of a GL_VERSION string ("4.5 (Core Profile) Mesa 26.0.3").</summary>
    public static (int Major, int Minor)? Parse(string version)
    {
        var m = Leading().Match(version);
        return m.Success ? (int.Parse(m.Groups["maj"].Value), int.Parse(m.Groups["min"].Value)) : null;
    }

    public static bool MeetsRequirement(string version) =>
        Parse(version) is { } v && (v.Major, v.Minor).CompareTo(Required) >= 0;
}

/// <summary>The machine can't give us an OpenGL 3.3 core context. Shown to the user as a plain message, never a crash.</summary>
public sealed class GlUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
