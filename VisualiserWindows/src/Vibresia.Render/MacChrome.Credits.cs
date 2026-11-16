using System.Numerics;
using ImGuiNET;
using Vibresia.Core.Ui;

namespace Vibresia.Render;

/// <summary>
/// The Mac's credits button and popover (AboutButton): "🩷 from Minith Labs"
/// in the bottom-right corner, which stays when the rest of the chrome
/// hides, opening the credits: VIBRESIA and minithlabs.com, as on the Mac,
/// and who made this Windows version. The Mac also keeps two settings here
/// (Start at login, Float above other apps); on Windows they are in the
/// Settings drop-down with the rest, where a Windows user looks for them.
/// Left out: the "Proudly Shareware" section and its coffee button, and the
/// share link above the button, because an unofficial port shouldn't ask for
/// money on Minith Labs' behalf; and the Mac's song identification,
/// analytics, music-app watcher and legal links, which this port doesn't have.
/// </summary>
public sealed partial class MacChrome
{
    private const string CreditsLabel = "♥ from Minith Labs";
    private const string PortCredit = "Windows version by Llew Roberts";
    private const string PortSiteLabel = "inertialnavigation.com";

    // 🩷, drawn: the interface fonts have no colour emoji.
    private static readonly Vector4 PinkHeart = new(1f, 0.6f, 0.78f, 1f);

    private (Vector2 Min, Vector2 Max) _creditsButton;

    private void DrawCreditsButton(ref Frame f, bool interactive)
    {
        var s = _s;
        var textWidth = MeasureTracked(Face.Mono, 10, 0.6f, CreditsLabel);
        var size = new Vector2(textWidth + 2 * 10 * s, (10 + 2 * 6 + 2) * s);
        var max = new Vector2(f.Size.X - 28 * s, f.Size.Y - 16 * s);
        var min = max - size;
        _creditsButton = (min, max);
        var d = f.Draw;
        d.AddRectFilled(min, max, Colour(0, 0, 0, 0.35f), size.Y / 2);
        d.AddRect(min, max, Colour(1, 1, 1, 0.08f), size.Y / 2, ImDrawFlags.None, 1 * s);
        DrawTracked(d, Face.Mono, 10, new Vector2(min.X + 10 * s, min.Y + (size.Y - 10 * s) / 2 - 1 * s), 0.6f, CreditsLabel,
            Colour(0.62f, 0.62f, 0.62f, 1), heart: Colour(PinkHeart, 1));
        if (interactive && Clicked(ref f, min, max))
        {
            if (OpenPanel == ChromePanel.Credits) OpenPanel = ChromePanel.None;
            else Open(ChromePanel.Credits);
        }
    }

    private void DrawCredits(ref Frame f, ChromeView v, ChromeActions actions)
    {
        var s = _s;
        var d = f.Draw;
        var alpha = (float)_panelFade.Value;
        const float widthPoints = 320, padPoints = 18, gapPoints = 14;
        var width = widthPoints * s;
        var inner = (widthPoints - 2 * padPoints) * s;
        var gap = gapPoints * s;

        var height = 2 * padPoints * s
                     + (13 + 4 + 10) * s * 1.25f                    // VIBRESIA, minithlabs.com
                     + gap + gap                                    // divider
                     + (11 + 4 + 10) * s * 1.25f;                   // the Windows version's credit

        // Above the button, its arrow pointing down at it (arrowEdge: .bottom), kept inside the window.
        var button = _creditsButton;
        var arrow = 8 * s;
        var centreX = (button.Min.X + button.Max.X) / 2;
        var left = Math.Clamp(centreX - width / 2, 8 * s, Math.Max(8 * s, f.Size.X - 8 * s - width));
        var min = new Vector2(left, Math.Max(8 * s, button.Min.Y - arrow - 4 * s - height + (1 - alpha) * 6 * s));
        var max = min + new Vector2(width, height);
        _controls.Add((min, max));
        for (var k = 1; k <= 5; k++)
        {
            var grow = k * 3 * s;
            d.AddRectFilled(min - new Vector2(grow, grow - 4 * s), max + new Vector2(grow, grow + 4 * s), Colour(0, 0, 0, 0.5f / 5 * (1 - k / 6f) * alpha), (10 + k * 3) * s);
        }
        var background = new Vector4(0.06f, 0.03f, 0.11f, 1);
        d.AddRectFilled(min, max, Colour(background, alpha), 10 * s);
        d.AddRect(min, max, Colour(1, 1, 1, 0.1f * alpha), 10 * s, ImDrawFlags.None, 1 * s);
        var tip = new Vector2(Math.Clamp(centreX, min.X + 16 * s, max.X - 16 * s), max.Y);
        d.AddTriangleFilled(tip + new Vector2(-arrow, -0.5f * s), tip + new Vector2(arrow, -0.5f * s), tip + new Vector2(0, arrow), Colour(background, alpha));

        var x = min.X + padPoints * s;
        var y = min.Y + padPoints * s;

        // VIBRESIA → vibresia.com (underlined on hover), minithlabs.com → itself.
        var nameWidth = MeasureTracked(Face.SansBold, 13, 3, "VIBRESIA");
        var nameMax = new Vector2(x + nameWidth, y + 13 * 1.25f * s);
        var nameHovered = Inside(f.Mouse, new Vector2(x, y), nameMax);
        DrawTracked(d, Face.SansBold, 13, new Vector2(x, y), 3, "VIBRESIA", Colour(1, 1, 1, alpha));
        if (nameHovered) d.AddLine(new Vector2(x, nameMax.Y - 1 * s), new Vector2(x + nameWidth - 3 * s, nameMax.Y - 1 * s), Colour(1, 1, 1, alpha), 1 * s);
        if (Clicked(ref f, new Vector2(x, y), nameMax)) actions.OpenWebsite(CreditsLinks.ProductSite);
        y = nameMax.Y + 4 * s;
        var siteMax = new Vector2(x + Measure(Face.Mono, 10, "minithlabs.com"), y + 10 * 1.25f * s);
        DrawText(d, Face.Mono, 10, new Vector2(x, y), "minithlabs.com", Colour(Cyan, alpha));
        if (Clicked(ref f, new Vector2(x, y), siteMax)) actions.OpenWebsite(CreditsLinks.MakerSite);
        y = siteMax.Y;

        // Who made this Windows version, and where to find them.
        y = Divider(d, x, y, inner, gap, alpha);
        DrawText(d, Face.Sans, 11, new Vector2(x, y), PortCredit, Colour(0.7f, 0.7f, 0.7f, alpha));
        y += (11 * 1.25f + 4) * s;
        var portMax = new Vector2(x + Measure(Face.Mono, 10, PortSiteLabel), y + 10 * 1.25f * s);
        DrawText(d, Face.Mono, 10, new Vector2(x, y), PortSiteLabel, Colour(Cyan, alpha));
        if (Clicked(ref f, new Vector2(x, y), portMax)) actions.OpenWebsite(CreditsLinks.PortSite);

        // A click inside the popover keeps it open; one outside closes it (Render does that).
        if (f.Click && !f.ClickTaken && Inside(f.Mouse, min, max)) f.ClickTaken = true;
    }

    private float Divider(ImDrawListPtr d, float x, float y, float width, float gap, float alpha)
    {
        y += gap;
        d.AddLine(new Vector2(x, y), new Vector2(x + width, y), Colour(1, 1, 1, 0.1f * alpha), 1 * _s);
        return y + gap;
    }
}
