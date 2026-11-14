using System.Runtime.InteropServices;
using ImGuiNET;

namespace Vibresia.Render;

/// <summary>The faces the Mac's interface uses, as Windows has them: SF Pro → Segoe UI, SF Mono → Consolas.</summary>
public enum Face { Sans, SansSemibold, SansBold, Mono, MonoBold }

/// <summary>
/// The interface's fonts, rasterised at the exact pixel size each is drawn
/// at (points × the display's scaling), so text is as sharp as the Mac's.
/// Rebuilt when the window moves to a monitor with different scaling. Off
/// Windows (the Linux build and its tests) DejaVu stands in, and failing
/// that ImGui's own font, scaled.
/// </summary>
internal sealed class ChromeFonts
{
    /// <summary>Every face and point size the chrome draws with.</summary>
    public static readonly (Face Face, float Points)[] Needed =
    [
        (Face.SansBold, 17),      // VIBRESIA
        (Face.Mono, 10),          // version, pill titles, footer credit
        (Face.MonoBold, 11),      // the vibescape pill's shortcut glyph
        (Face.Mono, 11),          // shortcut glyphs in the list
        (Face.SansSemibold, 11),  // list rows
        (Face.Mono, 9),           // list subtitles
        (Face.MonoBold, 14),      // the footer's vibescape name
        (Face.Sans, 13),          // messages
    ];

    // Basic Latin, Latin-1 and Latin Extended-A (custom vibescape names), and the ellipsis for truncation.
    private static readonly IntPtr GlyphRanges = AllocRanges([0x0020, 0x017F, 0x2026, 0x2026, 0]);

    private readonly Dictionary<(Face, float), ImFontPtr> _fonts = [];

    public float Scale { get; private set; }

    /// <summary>Which font files were found, for the log.</summary>
    public string Description { get; private set; } = "";

    public void Build(ImFontAtlasPtr atlas, float scale)
    {
        atlas.Clear();
        _fonts.Clear();
        Scale = scale;
        var found = new List<string>();
        unsafe
        {
            var config = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
            config.OversampleH = 2;
            config.OversampleV = 1;
            ImFontPtr? fallback = null;
            foreach (var (face, points) in Needed)
            {
                var file = FileFor(face);
                if (file is null)
                {
                    fallback ??= atlas.AddFontDefault();
                    _fonts[(face, points)] = fallback.Value;
                    continue;
                }
                if (!found.Contains(Path.GetFileName(file))) found.Add(Path.GetFileName(file));
                _fonts[(face, points)] = atlas.AddFontFromFileTTF(file, points * scale, config, GlyphRanges);
            }
            ImGuiNative.ImFontConfig_destroy(config.NativePtr);
        }
        Description = found.Count > 0 ? string.Join(", ", found) : "ImGui's built-in font (no system fonts found)";
    }

    public ImFontPtr Get(Face face, float points) =>
        _fonts.TryGetValue((face, points), out var f) ? f : throw new ArgumentException($"font {face} {points} pt isn't in ChromeFonts.Needed");

    private static string? FileFor(Face face)
    {
        var windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        string[] candidates = face switch
        {
            Face.Sans => [Path.Combine(windows, "segoeui.ttf"), "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"],
            Face.SansSemibold => [Path.Combine(windows, "seguisb.ttf"), Path.Combine(windows, "segoeui.ttf"), "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"],
            Face.SansBold => [Path.Combine(windows, "segoeuib.ttf"), "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"],
            Face.Mono => [Path.Combine(windows, "consola.ttf"), "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"],
            _ => [Path.Combine(windows, "consolab.ttf"), "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"],
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // ImGui keeps the pointer for the atlas's life, so it is allocated once for the process.
    private static IntPtr AllocRanges(ushort[] ranges)
    {
        var p = Marshal.AllocHGlobal(ranges.Length * sizeof(ushort));
        for (var i = 0; i < ranges.Length; i++) Marshal.WriteInt16(p, i * sizeof(ushort), (short)ranges[i]);
        return p;
    }
}
