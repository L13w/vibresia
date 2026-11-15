using System.Numerics;
using ImGuiNET;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>The chrome's drawing helpers: text with SwiftUI's tracking, the drawn glyphs the fonts lack, and colours.</summary>
public sealed partial class MacChrome
{
    // ---- text ----

    private void DrawText(ImDrawListPtr d, Face face, float points, Vector2 pos, string text, uint colour) =>
        d.AddText(_fonts.Get(face, points), points * _s, pos, colour, text);

    private float Measure(Face face, float points, string text)
    {
        var font = _fonts.Get(face, points);
        ImGui.PushFont(font);
        var w = ImGui.CalcTextSize(text).X * (points * _s / font.FontSize);
        ImGui.PopFont();
        return w;
    }

    /// <summary>Text with SwiftUI's tracking (extra space after each character, in points); ImGui has none, so it is set a character at a time.</summary>
    private float MeasureTracked(Face face, float points, float tracking, string text)
    {
        var font = _fonts.Get(face, points);
        var k = points * _s / font.FontSize;
        ImGui.PushFont(font);
        var w = 0f;
        foreach (var ch in text) w += (ch == '♥' ? points * _s * 0.8f : ImGui.CalcTextSize(ch.ToString()).X * k) + tracking * _s;
        ImGui.PopFont();
        return w;
    }

    /// <param name="scale">Draws smaller than <paramref name="points"/> from the same rasterised font (a title shrunk to fit).</param>
    private void DrawTracked(ImDrawListPtr d, Face face, float points, Vector2 pos, float tracking, string text, uint colour, uint? heart = null, float scale = 1)
    {
        var font = _fonts.Get(face, points);
        var px = points * _s * scale;
        var k = px / font.FontSize;
        ImGui.PushFont(font);
        foreach (var ch in text)
        {
            var w = ImGui.CalcTextSize(ch.ToString()).X * k;
            if (ch == '♥' && heart is { } h) DrawHeart(d, pos + new Vector2(0, px * 0.12f), px * 0.8f, h);
            else d.AddText(font, px, pos, colour, ch.ToString());
            pos.X += (ch == '♥' ? px * 0.8f : w) + tracking * _s * scale;
        }
        ImGui.PopFont();
    }

    /// <summary>The Mac's purple heart (💜), drawn: the interface fonts have no colour emoji.</summary>
    private static void DrawHeart(ImDrawListPtr d, Vector2 min, float size, uint colour)
    {
        var r = size * 0.27f;
        d.AddCircleFilled(min + new Vector2(size * 0.29f, size * 0.32f), r, colour);
        d.AddCircleFilled(min + new Vector2(size * 0.71f, size * 0.32f), r, colour);
        d.AddTriangleFilled(min + new Vector2(size * 0.04f, size * 0.42f), min + new Vector2(size * 0.96f, size * 0.42f), min + new Vector2(size * 0.5f, size * 0.92f), colour);
    }

    /// <summary>
    /// Breaks <paramref name="text"/> into lines no wider than
    /// <paramref name="width"/> at spaces (a single word too long for a line
    /// gets a line of its own), as SwiftUI wraps a Text with a frame width.
    /// </summary>
    private List<string> WrapLines(Face face, float points, string text, float width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(face, points, candidate) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = candidate;
            }
            lines.Add(line);
        }
        return lines;
    }

    private Vector2 MeasureWrapped(Face face, float points, string text, float width, float lineGapPoints = 0)
    {
        var lines = WrapLines(face, points, text, width);
        var lineHeight = (points * 1.25f + lineGapPoints) * _s;
        return new Vector2(lines.Max(l => Measure(face, points, l)), lines.Count * lineHeight - lineGapPoints * _s);
    }

    /// <summary>Wrapped text, each line centred on <paramref name="centreX"/> (multilineTextAlignment(.center)).</summary>
    private float DrawWrappedCentred(ImDrawListPtr d, Face face, float points, string text, float centreX, float top, float width, uint colour, float lineGapPoints = 0)
    {
        var lineHeight = (points * 1.25f + lineGapPoints) * _s;
        foreach (var line in WrapLines(face, points, text, width))
        {
            DrawText(d, face, points, new Vector2(centreX - Measure(face, points, line) / 2, top), line, colour);
            top += lineHeight;
        }
        return top;
    }

    /// <summary>Wrapped text from the left edge; returns the y below it.</summary>
    private float DrawWrapped(ImDrawListPtr d, Face face, float points, string text, Vector2 pos, float width, uint colour, float lineGapPoints = 0)
    {
        var lineHeight = (points * 1.25f + lineGapPoints) * _s;
        foreach (var line in WrapLines(face, points, text, width))
        {
            DrawText(d, face, points, pos, line, colour);
            pos.Y += lineHeight;
        }
        return pos.Y - lineGapPoints * _s;
    }

    // ---- drawn glyphs (SF Symbols and emoji the interface fonts don't have) ----

    /// <summary>The Mac's on/off switch (VibescapeSwitch): a 26×15 pt capsule, magenta when on, with an 11 pt white knob.</summary>
    private void DrawSwitch(ImDrawListPtr d, Vector2 min, bool on, float alpha)
    {
        var s = _s;
        var max = min + new Vector2(26, 15) * s;
        d.AddRectFilled(min, max, on ? Colour(Magenta, 0.85f * alpha) : Colour(0.22f, 0.22f, 0.22f, alpha), 7.5f * s);
        var knob = new Vector2(on ? max.X - (2 + 5.5f) * s : min.X + (2 + 5.5f) * s, (min.Y + max.Y) / 2);
        d.AddCircleFilled(knob + new Vector2(0, 0.5f * s), 6 * s, Colour(0, 0, 0, 0.25f * alpha));
        d.AddCircleFilled(knob, 5.5f * s, Colour(1, 1, 1, alpha));
    }

    /// <summary>★, five points, drawn as a filled polygon.</summary>
    private static void DrawStar(ImDrawListPtr d, Vector2 centre, float radius, uint colour)
    {
        var pts = new Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = -MathF.PI / 2 + i * MathF.PI / 5;
            var r = i % 2 == 0 ? radius : radius * 0.45f;
            pts[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
        }
        for (var i = 0; i < 10; i += 2)
            d.AddTriangleFilled(centre, pts[i], pts[(i + 1) % 10], colour);
        for (var i = 1; i < 10; i += 2)
            d.AddTriangleFilled(centre, pts[i], pts[(i + 1) % 10], colour);
    }

    /// <summary>person.crop.circle: a ring with a head and shoulders.</summary>
    private void DrawPerson(ImDrawListPtr d, Vector2 centre, float size, uint colour)
    {
        var r = size / 2;
        d.AddCircle(centre, r, colour, 16, Math.Max(1, 0.1f * size));
        d.AddCircleFilled(centre + new Vector2(0, -r * 0.22f), r * 0.32f, colour);
        d.PathArcTo(centre + new Vector2(0, r * 0.75f), r * 0.6f, MathF.PI * 1.1f, MathF.PI * 1.9f, 10);
        d.PathFillConvex(colour);
    }

    /// <summary>info.circle.</summary>
    private void DrawInfo(ImDrawListPtr d, Vector2 centre, float size, uint colour)
    {
        var r = size / 2;
        d.AddCircle(centre, r, colour, 16, Math.Max(1, 0.1f * size));
        d.AddLine(centre + new Vector2(0, -r * 0.05f), centre + new Vector2(0, r * 0.5f), colour, Math.Max(1, 0.14f * size));
        d.AddCircleFilled(centre + new Vector2(0, -r * 0.42f), Math.Max(0.8f, 0.08f * size), colour);
    }

    /// <summary>xmark.</summary>
    private void DrawCross(ImDrawListPtr d, Vector2 centre, float size, uint colour, float thickness)
    {
        var h = size / 2;
        d.AddLine(centre + new Vector2(-h, -h), centre + new Vector2(h, h), colour, thickness);
        d.AddLine(centre + new Vector2(h, -h), centre + new Vector2(-h, h), colour, thickness);
    }

    /// <summary>exclamationmark.triangle.fill.</summary>
    private void DrawWarning(ImDrawListPtr d, Vector2 min, float size, uint colour)
    {
        d.AddTriangleFilled(min + new Vector2(size / 2, 0), min + new Vector2(size, size * 0.9f), min + new Vector2(0, size * 0.9f), colour);
        d.AddLine(min + new Vector2(size / 2, size * 0.3f), min + new Vector2(size / 2, size * 0.58f), Colour(0, 0, 0, 0.8f), Math.Max(1, size * 0.12f));
        d.AddCircleFilled(min + new Vector2(size / 2, size * 0.72f), Math.Max(0.6f, size * 0.06f), Colour(0, 0, 0, 0.8f));
    }

    /// <summary>A click on <paramref name="min"/>–<paramref name="max"/> this frame (taking it), also marking the area as a control and the pointer as a hand over it.</summary>
    private bool Clicked(ref Frame f, Vector2 min, Vector2 max)
    {
        _controls.Add((min, max));
        if (!Inside(f.Mouse, min, max)) return false;
        _hover = true;
        if (!f.Click || f.ClickTaken) return false;
        f.ClickTaken = true;
        return true;
    }

    private string Truncate(Face face, float points, float tracking, string text, float maxWidth)
    {
        if (MeasureTracked(face, points, tracking, text) <= maxWidth) return text;
        for (var n = text.Length - 1; n > 0; n--)
        {
            var t = text[..n].TrimEnd() + "…";
            if (MeasureTracked(face, points, tracking, t) <= maxWidth) return t;
        }
        return "…";
    }

    // ---- helpers ----

    private static Vector4 SourceTint(SourceState state) => state switch
    {
        SourceState.Live => Magenta,
        SourceState.Demo => new Vector4(1f, 0.71f, 0.28f, 1f),
        SourceState.Error => new Vector4(1f, 0.35f, 0.35f, 1f),
        _ => new Vector4(0.55f, 0.55f, 0.55f, 1f),
    };

    private static bool Inside(Vector2 p, Vector2 min, Vector2 max) => p.X >= min.X && p.X < max.X && p.Y >= min.Y && p.Y < max.Y;

    private static uint Colour(float r, float g, float b, float a) => ImGui.GetColorU32(new Vector4(r, g, b, Math.Clamp(a, 0, 1)));
    private static uint Colour(Vector4 c, float a) => Colour(c.X, c.Y, c.Z, a);
    private static uint Colour((float R, float G, float B) c, float a) => Colour(c.R, c.G, c.B, a);

}
