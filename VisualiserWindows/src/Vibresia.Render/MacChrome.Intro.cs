using System.Numerics;
using ImGuiNET;
using Vibresia.Core.Ui;

namespace Vibresia.Render;

/// <summary>
/// The Mac's intro (IntroView): the picture dimmed by black at 0.65, the
/// "FEED IT SOME SOUND." title with its glow, a line of explanation, a
/// button per audio source, and the footnote, centred in the window.
/// </summary>
public sealed partial class MacChrome
{
    private const float IntroTitlePoints = 48;

    private void DrawIntro(ref Frame f, ChromeView v, ChromeActions actions, float alpha)
    {
        var s = _s;
        var d = f.Draw;
        d.AddRectFilled(Vector2.Zero, f.Size, Colour(0, 0, 0, 0.65f * alpha));

        // The VStack's widths (title free, subtitle 540, buttons 580, footnote 500), inside 40 pt side padding.
        var room = Math.Max(160 * s, f.Size.X - 2 * 40 * s);
        var subtitleWidth = Math.Min(540 * s, room);
        var buttonsWidth = Math.Min(580 * s, room);
        var footWidth = Math.Min(500 * s, room);

        // A narrow window gets a smaller title rather than a broken one.
        var titleWidth = MeasureTracked(Face.SansBold, IntroTitlePoints, 1, IntroScreen.Title);
        var titleScale = Math.Min(1, room / titleWidth);
        var titleHeight = IntroTitlePoints * s * titleScale;
        var subtitle = MeasureWrapped(Face.Sans, 15, IntroScreen.Subtitle, subtitleWidth, lineGapPoints: 4);
        var buttonHeight = (14 + 14 + 4 + 10 + 14) * s;
        var buttonsHeight = IntroScreen.Buttons.Count * buttonHeight + (IntroScreen.Buttons.Count - 1) * 10 * s;
        var problem = v.IntroProblem is { } p ? MeasureWrapped(Face.Sans, 12, p, buttonsWidth) : Vector2.Zero;
        var foot = MeasureWrapped(Face.Sans, 11, IntroScreen.Footnote, footWidth, lineGapPoints: 2);
        var height = titleHeight + 14 * s + subtitle.Y + 28 * s + buttonsHeight + 28 * s
                     + (v.IntroProblem is null ? 0 : problem.Y + 18 * s) + foot.Y;
        var y = Math.Max(16 * s, (f.Size.Y - height) / 2);
        var cx = f.Size.X / 2;

        // Title: white to 0.72 grey top to bottom, with a soft magenta glow (shadow radius 30 at 0.3).
        var titlePos = new Vector2(cx - titleWidth * titleScale / 2, y);
        for (var k = 6; k >= 1; k--)
        {
            var r = k * 4 * s;
            foreach (var dir in GlowDirections)
                DrawTracked(d, Face.SansBold, IntroTitlePoints, titlePos + dir * r, 1, IntroScreen.Title,
                    Colour(Magenta, 0.3f * 0.06f * alpha), scale: titleScale);
        }
        var first = d.VtxBuffer.Size;
        DrawTracked(d, Face.SansBold, IntroTitlePoints, titlePos, 1, IntroScreen.Title, Colour(1, 1, 1, alpha), scale: titleScale);
        ShadeVertically(d, first, titlePos.Y, titlePos.Y + titleHeight, new Vector4(1, 1, 1, alpha), new Vector4(0.72f, 0.72f, 0.72f, alpha));
        y += titleHeight + 14 * s;

        DrawWrappedCentred(d, Face.Sans, 15, IntroScreen.Subtitle, cx, y, subtitleWidth, Colour(0.75f, 0.75f, 0.75f, alpha), lineGapPoints: 4);
        y += subtitle.Y + 28 * s;

        var left = cx - buttonsWidth / 2;
        for (var i = 0; i < IntroScreen.Buttons.Count; i++)
        {
            var (choice, label, detail) = IntroScreen.Buttons[i];
            var min = new Vector2(left, y);
            var max = min + new Vector2(buttonsWidth, buttonHeight);
            if (DrawSourceButton(ref f, min, max, label, detail, primary: i == 0, alpha))
                actions.PickSource(choice);
            y = max.Y + 10 * s;
        }
        y += (28 - 10) * s;

        if (v.IntroProblem is { } why)
        {
            DrawWrappedCentred(d, Face.Sans, 12, why, cx, y, buttonsWidth, Colour(1f, 0.55f, 0.5f, alpha));
            y += problem.Y + 18 * s;
        }
        DrawWrappedCentred(d, Face.Sans, 11, IntroScreen.Footnote, cx, y, footWidth, Colour(0.45f, 0.45f, 0.45f, alpha), lineGapPoints: 2);
    }

    private static readonly Vector2[] GlowDirections =
        Enumerable.Range(0, 8).Select(i => new Vector2(MathF.Cos(i * MathF.PI / 4), MathF.Sin(i * MathF.PI / 4))).ToArray();

    /// <returns>Whether it was clicked this frame.</returns>
    private bool DrawSourceButton(ref Frame f, Vector2 min, Vector2 max, string label, string detail, bool primary, float alpha)
    {
        var s = _s;
        var d = f.Draw;
        var hovered = alpha > 0.99f && Inside(f.Mouse, min, max);
        if (hovered)
        {
            // scaleEffect(1.01) on hover.
            var grow = (max - min) * 0.005f;
            min -= grow;
            max += grow;
            _hover = true;
        }
        var radius = 12 * s;
        if (primary)
        {
            // Magenta 0.20 → violet 0.10 → cyan 0.05, top-left to bottom-right.
            d.AddRectFilled(min, max, Colour(Violet, 0.10f * alpha), radius);
            d.AddRectFilledMultiColor(min + new Vector2(radius * 0.3f), max - new Vector2(radius * 0.3f),
                Colour(Magenta, 0.12f * alpha), Colour(Violet, 0.02f * alpha), Colour(Cyan, 0.0f), Colour(Violet, 0.02f * alpha));
            d.AddRect(min, max, Colour(Magenta, 0.4f * alpha), radius, ImDrawFlags.None, 1 * s);
        }
        else
        {
            d.AddRectFilled(min, max, Colour(1, 1, 1, 0.02f * alpha), radius);
            d.AddRect(min, max, Colour(1, 1, 1, 0.08f * alpha), radius, ImDrawFlags.None, 1 * s);
        }
        var x = min.X + 18 * s;
        var top = min.Y + (max.Y - min.Y - (14 + 4 + 10 + 4) * s) / 2;
        DrawTracked(d, Face.SansSemibold, 14, new Vector2(x, top), 1, label, Colour(1, 1, 1, alpha));
        DrawText(d, Face.Mono, 10, new Vector2(x, top + (14 + 4 + 2) * s), detail, Colour(0.5f, 0.5f, 0.5f, alpha));

        if (alpha < 0.99f) return false;
        _controls.Add((min, max));
        if (!f.Click || f.ClickTaken || !Inside(f.Mouse, min, max)) return false;
        f.ClickTaken = true;
        return true;
    }

    /// <summary>
    /// Recolours the vertices added since <paramref name="firstVertex"/> from
    /// <paramref name="top"/> to <paramref name="bottom"/>: SwiftUI's vertical
    /// LinearGradient on text, which ImGui has no call for.
    /// </summary>
    private static void ShadeVertically(ImDrawListPtr d, int firstVertex, float top, float bottom, Vector4 from, Vector4 to)
    {
        var buffer = d.VtxBuffer;
        for (var i = firstVertex; i < buffer.Size; i++)
        {
            var vert = buffer[i];
            var t = Math.Clamp((vert.pos.Y - top) / Math.Max(1, bottom - top), 0, 1);
            vert.col = ImGui.GetColorU32(Vector4.Lerp(from, to, t));
        }
    }
}
