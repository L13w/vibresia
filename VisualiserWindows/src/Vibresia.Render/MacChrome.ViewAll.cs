using System.Numerics;
using ImGuiNET;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>
/// The Mac's View All panel (VibescapesView, its Installed list): every
/// vibescape as a card with its picture, name and on/off switch, three
/// across, in the user's order. Drag a card onto another to move it; click a
/// picture to switch to it (switching it on if it was off) and close the
/// panel. The Gallery tab, the Make your own card and editing, copying or
/// deleting your own are left out (sharing and the editor are out of scope,
/// and the port never writes your vibescape files); the footer opens the
/// vibescapes folder instead.
/// </summary>
public sealed partial class MacChrome
{
    private const float PanelWidthPoints = 720, PanelHeightPoints = 560, CardPicturePoints = 96;
    private const int Columns = 3;

    private sealed record Drag(VibescapeId Id, string Title, Vector2 Start)
    {
        public bool Moving { get; set; }
    }

    private Drag? _drag;
    private float _scroll;
    private bool _introUp;
    private bool _hover;
    private PreviewPictures? _previews;
    private VibescapeCatalog? _panelCatalog;

    /// <summary>Before any other drawing in a frame: draws one waiting picture of a custom vibescape while the panel is open.</summary>
    private void PreparePanel(ChromeView v)
    {
        if (OpenPanel != ChromePanel.ViewAll) return;
        _previews ??= new PreviewPictures(_gl, _log);
        _previews.DrawNext(Palette.At(v.PaletteIndex));
    }

    private void DrawViewAll(ref Frame f, ChromeView v, ChromeActions actions, float dt)
    {
        var s = _s;
        var d = f.Draw;
        var alpha = (float)_panelFade.Value;
        var previews = _previews ??= new PreviewPictures(_gl, _log);
        var catalog = v.Catalog;
        if (!ReferenceEquals(catalog, _panelCatalog))
        {
            // The folder was read again: forget the pictures of vibescapes that are gone.
            previews.KeepOnly(catalog.All.Select(l => l.Entry.Id));
            _panelCatalog = catalog;
        }

        // A sheet: the window behind it dims and takes no clicks.
        d.AddRectFilled(Vector2.Zero, f.Size, Colour(0, 0, 0, 0.35f * alpha));
        var size = new Vector2(Math.Min(PanelWidthPoints * s, f.Size.X - 32 * s), Math.Min(PanelHeightPoints * s, f.Size.Y - 32 * s));
        var min = (f.Size - size) / 2 + new Vector2(0, (1 - alpha) * 10 * s);
        var max = min + size;
        for (var k = 1; k <= 6; k++)
        {
            var grow = k * 4 * s;
            d.AddRectFilled(min - new Vector2(grow, grow - 6 * s), max + new Vector2(grow, grow + 6 * s), Colour(0, 0, 0, 0.6f / 6 * (1 - k / 7f) * alpha), (10 + k * 4) * s);
        }
        d.AddRectFilled(min, max, Colour(0.05f, 0.02f, 0.09f, alpha), 10 * s);
        d.AddRect(min, max, Colour(1, 1, 1, 0.08f * alpha), 10 * s, ImDrawFlags.None, 1 * s);

        // ---- header ----
        var pad = new Vector2(18, 14) * s;
        var closeSize = 22 * s;
        var closeMin = new Vector2(max.X - pad.X - closeSize, min.Y + pad.Y);
        var hoveringClose = Inside(f.Mouse, closeMin, closeMin + new Vector2(closeSize));
        d.AddCircleFilled(closeMin + new Vector2(closeSize / 2), closeSize / 2, Colour(1, 1, 1, (hoveringClose ? 0.18f : 0.08f) * alpha));
        DrawCross(d, closeMin + new Vector2(closeSize / 2), 7 * s, hoveringClose ? Colour(1, 1, 1, alpha) : Colour(0.65f, 0.65f, 0.65f, alpha), 1.6f * s);
        if (Clicked(ref f, closeMin, closeMin + new Vector2(closeSize)))
        {
            CloseMenu();
            return;
        }
        var titleRight = closeMin.X - 10 * s;
        if (catalog.HasCustomOrder)
        {
            const string reset = "Reset Order";
            var w = Measure(Face.SansSemibold, 12, reset) + 2 * 12 * s;
            var rMin = new Vector2(closeMin.X - 10 * s - w, closeMin.Y + 1 * s);
            var rMax = rMin + new Vector2(w, (12 * 1.25f + 2 * 5) * s);
            d.AddRectFilled(rMin, rMax, Colour(1, 1, 1, 0.08f * alpha), 6 * s);
            DrawText(d, Face.SansSemibold, 12, rMin + new Vector2(12, 5) * s, reset, Colour(0.75f, 0.75f, 0.75f, alpha));
            if (Clicked(ref f, rMin, rMax)) actions.ResetOrder();
            titleRight = rMin.X - 10 * s;
        }
        var x = min.X + pad.X;
        DrawText(d, Face.SansSemibold, 17, new Vector2(x, min.Y + pad.Y), "Vibescapes", Colour(1, 1, 1, alpha));
        var headerBottom = DrawWrapped(d, Face.Sans, 11, "Switch off the ones you don't want in the list. Drag them to change the order.",
            new Vector2(x, min.Y + pad.Y + (17 * 1.25f + 3) * s), titleRight - x, Colour(0.55f, 0.55f, 0.55f, alpha));
        var gridTop = headerBottom + pad.Y;
        d.AddLine(new Vector2(min.X, gridTop), new Vector2(max.X, gridTop), Colour(1, 1, 1, 0.08f * alpha), 1 * s);

        // ---- footer ----
        var footerHeight = (10 * 1.25f + 2 * 10) * s;
        var gridBottom = max.Y - footerHeight;
        d.AddLine(new Vector2(min.X, gridBottom), new Vector2(max.X, gridBottom), Colour(1, 1, 1, 0.08f * alpha), 1 * s);
        DrawPanelFooter(ref f, catalog, actions, new Vector2(min.X + 18 * s, gridBottom + 10 * s), max.X - 18 * s, alpha);

        // ---- the grid ----
        var gridMin = new Vector2(min.X, gridTop + 1 * s);
        var gridMax = new Vector2(max.X, gridBottom - 1 * s);
        var cardWidth = (size.X - 2 * 18 * s - (Columns - 1) * 14 * s) / Columns;
        var rows = (catalog.All.Count + Columns - 1) / Columns;
        var rowHeights = Enumerable.Range(0, rows).Select(r =>
            catalog.All.Skip(r * Columns).Take(Columns).Max(l => CardHeight(l, previews))).ToArray();
        var contentHeight = 2 * 18 * s + rowHeights.Sum() + Math.Max(0, rows - 1) * 14 * s;
        var visible = gridMax.Y - gridMin.Y;
        var io = ImGui.GetIO();
        if (Inside(f.Mouse, gridMin, gridMax) && io.MouseWheel != 0) _scroll -= io.MouseWheel * 40 * s;
        if (_drag is { Moving: true })
        {
            // Dragging near an edge scrolls, so a card can be moved anywhere in a long list.
            if (f.Mouse.Y < gridMin.Y + 24 * s) _scroll -= 400 * s * dt;
            if (f.Mouse.Y > gridMax.Y - 24 * s) _scroll += 400 * s * dt;
        }
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, contentHeight - visible));

        d.PushClipRect(gridMin, gridMax, true);
        var pointerInGrid = Inside(f.Mouse, gridMin, gridMax);
        VibescapeListing? hovered = null;
        var y = gridMin.Y + 18 * s - _scroll;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < Columns && r * Columns + c < catalog.All.Count; c++)
            {
                var listing = catalog.All[r * Columns + c];
                var cMin = new Vector2(min.X + 18 * s + c * (cardWidth + 14 * s), y);
                var cMax = cMin + new Vector2(cardWidth, CardHeight(listing, previews));
                if (cMax.Y < gridMin.Y || cMin.Y > gridMax.Y) continue;
                var over = pointerInGrid && Inside(f.Mouse, cMin, cMax);
                if (over) hovered = listing;
                var isTarget = over && _drag is { Moving: true } drag && drag.Id != listing.Entry.Id;
                DrawCard(ref f, listing, previews, cMin, cMax, isTarget, pointerInGrid, actions, alpha);
            }
            y += rowHeights[r] + 14 * s;
        }
        d.PopClipRect();
        if (contentHeight > visible)
        {
            // An overlay scroll bar, as macOS draws one.
            var trackHeight = visible - 8 * s;
            var thumb = Math.Max(30 * s, trackHeight * visible / contentHeight);
            var top = gridMin.Y + 4 * s + (trackHeight - thumb) * (_scroll / (contentHeight - visible));
            d.AddRectFilled(new Vector2(max.X - 9 * s, top), new Vector2(max.X - 4 * s, top + thumb), Colour(1, 1, 1, 0.25f * alpha), 3 * s);
        }

        HandleDrag(ref f, catalog, hovered, actions);

        // The panel is a sheet: everything inside or outside it is its click.
        if (f.Click) f.ClickTaken = true;
        _controls.Add((Vector2.Zero, f.Size));
    }

    private float CardHeight(VibescapeListing listing, PreviewPictures previews)
    {
        var points = 8 + CardPicturePoints + 7 + 15 + 7 + 12 + 8;
        if (listing.Yours && previews.ProblemFor(listing.Entry.Id) is not null) points += 7 + 12;
        return points * _s;
    }

    private void DrawCard(ref Frame f, VibescapeListing listing, PreviewPictures previews, Vector2 min, Vector2 max,
        bool isTarget, bool pointerInGrid, ChromeActions actions, float alpha)
    {
        var s = _s;
        var d = f.Draw;
        var entry = listing.Entry;
        var on = listing.On;
        d.AddRectFilled(min, max, Colour(1, 1, 1, (on ? 0.045f : 0.015f) * alpha), 9 * s);
        var border = isTarget ? Colour(Magenta, alpha) : on ? Colour(Magenta, 0.28f * alpha) : Colour(1, 1, 1, 0.06f * alpha);
        d.AddRect(min, max, border, 9 * s, ImDrawFlags.None, (isTarget ? 2 : 1) * s);

        // The picture, filling a 96 pt strip (aspectRatio .fill, clipped, 5 pt corners).
        var pMin = min + new Vector2(8 * s);
        var pMax = new Vector2(max.X - 8 * s, pMin.Y + CardPicturePoints * s);
        if (previews.For(entry) is { } picture)
        {
            var (uv0, uv1) = FillUv(picture.Width, picture.Height, pMax.X - pMin.X, pMax.Y - pMin.Y, picture.BottomUp);
            d.AddImageRounded((IntPtr)picture.Texture, pMin, pMax, uv0, uv1, Colour(1, 1, 1, alpha), 5 * s);
        }
        else
        {
            d.AddRectFilled(pMin, pMax, Colour(1, 1, 1, 0.05f * alpha), 5 * s);
            if (previews.Waiting(entry))
            {
                // ProgressView: a turning arc while the picture is drawn.
                var centre = (pMin + pMax) / 2;
                var start = (float)(ImGui.GetTime() * 4 % (2 * Math.PI));
                d.PathArcTo(centre, 7 * s, start, start + 4.2f, 16);
                d.PathStroke(Colour(1, 1, 1, 0.6f * alpha), ImDrawFlags.None, 1.6f * s);
            }
        }

        // The name row: ★ for your own, the name (grey when off), the switch.
        var y = pMax.Y + 7 * s;
        var x = min.X + 8 * s;
        if (listing.Yours)
        {
            DrawStar(d, new Vector2(x + 4.5f * s, y + 7.5f * s), 4.5f * s, Colour(1f, 0.72f, 0.2f, alpha));
            x += 14 * s;
        }
        var switchMin = new Vector2(max.X - 8 * s - 26 * s, y);
        var title = Truncate(Face.SansSemibold, 11, 0.6f, entry.Title, switchMin.X - 4 * s - x);
        DrawTracked(d, Face.SansSemibold, 11, new Vector2(x, y + 1 * s), 0.6f, title,
            on ? Colour(1, 1, 1, alpha) : Colour(0.45f, 0.45f, 0.45f, alpha));
        DrawSwitch(d, switchMin, on, alpha);
        if (pointerInGrid && _drag is not { Moving: true } && Clicked(ref f, switchMin, switchMin + new Vector2(26, 15) * s))
            actions.SwitchVibescape(entry.Id, !on);
        y += (15 + 7) * s;

        // Who made it (not a link here: opening a web address from someone's file isn't worth the risk).
        DrawPerson(d, new Vector2(min.X + 8 * s + 4 * s, y + 6 * s), 8 * s, Colour(0.5f, 0.5f, 0.5f, alpha));
        var author = entry.Author.Trim().Length == 0 ? "Unknown" : entry.Author.Trim();
        DrawText(d, Face.Sans, 9, new Vector2(min.X + 8 * s + 12 * s, y), Truncate(Face.Sans, 9, 0, author, max.X - min.X - 28 * s),
            Colour(0.5f, 0.5f, 0.5f, alpha));
        y += 12 * s;
        if (listing.Yours && previews.ProblemFor(entry.Id) is not null)
        {
            y += 7 * s;
            DrawWarning(d, new Vector2(min.X + 8 * s, y + 1 * s), 10 * s, Colour(1f, 0.55f, 0.2f, alpha));
            DrawText(d, Face.SansSemibold, 9, new Vector2(min.X + 8 * s + 14 * s, y), "Needs a fix", Colour(1f, 0.55f, 0.2f, alpha));
        }

        if (isTarget)
        {
            // Where the dragged vibescape would land: a tint and a bar along the bottom.
            d.AddRectFilled(min, max, Colour(Magenta, 0.12f * alpha), 9 * s);
            d.AddRectFilled(new Vector2(min.X + 10 * s, max.Y - 8 * s), new Vector2(max.X - 10 * s, max.Y - 5 * s), Colour(Magenta, alpha), 2 * s);
        }

        // Pressing the card starts a drag; releasing on the picture without moving picks it (HandleDrag).
        if (pointerInGrid && Inside(f.Mouse, min, max))
        {
            if (Inside(f.Mouse, pMin, pMax)) _hover = true;
            if (f.Click && !f.ClickTaken)
            {
                f.ClickTaken = true;
                _drag = new Drag(entry.Id, entry.Title, f.Mouse);
                _pressedOnPicture = Inside(f.Mouse, pMin, pMax);
            }
        }
    }

    private bool _pressedOnPicture;

    private void HandleDrag(ref Frame f, VibescapeCatalog catalog, VibescapeListing? hovered, ChromeActions actions)
    {
        if (_drag is not { } drag) return;
        var s = _s;
        if (!drag.Moving && Vector2.Distance(f.Mouse, drag.Start) > 4 * s) drag.Moving = true;
        if (drag.Moving) DrawDragChip(f.Draw, drag.Title, f.Mouse);
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;

        _drag = null;
        if (drag.Moving)
        {
            if (hovered is { } target && target.Entry.Id != drag.Id)
            {
                var index = catalog.All.ToList().FindIndex(l => l.Entry.Id == target.Entry.Id);
                actions.MoveVibescape(drag.Id, index);
            }
        }
        else if (_pressedOnPicture && hovered?.Entry.Id == drag.Id)
        {
            actions.PickVibescape(hovered.Entry);
            CloseMenu();
        }
    }

    /// <summary>What follows the pointer while a card is dragged (DragChip): small, so it doesn't hide the drop bar.</summary>
    private void DrawDragChip(ImDrawListPtr d, string title, Vector2 at)
    {
        var s = _s;
        var text = Truncate(Face.SansSemibold, 11, 0, title, 200 * s);
        var size = new Vector2(Measure(Face.SansSemibold, 11, text) + (9 + 6 + 2 * 10) * s, (11 * 1.25f + 2 * 6) * s);
        var min = at + new Vector2(10, 8) * s;
        var max = min + size;
        for (var k = 1; k <= 3; k++)
            d.AddRectFilled(min - new Vector2(k * 2 * s, k * 2 * s - 3 * s), max + new Vector2(k * 2 * s, k * 2 * s + 3 * s), Colour(0, 0, 0, 0.5f / 3 * (1 - k / 4f)), (8 + k * 2) * s);
        d.AddRectFilled(min, max, Colour(0.08f, 0.04f, 0.14f, 0.95f), 8 * s);
        d.AddRect(min, max, Colour(Magenta, 1), 8 * s, ImDrawFlags.None, 1.5f * s);
        var cy = (min.Y + max.Y) / 2;
        for (var i = -1; i <= 1; i++)
            d.AddLine(new Vector2(min.X + 10 * s, cy + i * 2.6f * s), new Vector2(min.X + 19 * s, cy + i * 2.6f * s), Colour(1, 1, 1, 1), 1.3f * s);
        DrawText(d, Face.SansSemibold, 11, new Vector2(min.X + (10 + 9 + 6) * s, cy - 11 * 1.25f * s / 2), text, Colour(1, 1, 1, 1));
    }

    private void DrawPanelFooter(ref Frame f, VibescapeCatalog catalog, ChromeActions actions, Vector2 pos, float right, float alpha)
    {
        var s = _s;
        var d = f.Draw;
        var grey = Colour(0.45f, 0.45f, 0.45f, alpha);
        var count = $"{catalog.Entries.Count} of {catalog.All.Count} switched on";
        var countWidth = Measure(Face.Mono, 10, count);
        DrawText(d, Face.Mono, 10, new Vector2(right - countWidth, pos.Y), count, Colour(0.5f, 0.5f, 0.5f, alpha));

        DrawInfo(d, pos + new Vector2(5, 7) * s, 10 * s, Colour(0.4f, 0.4f, 0.4f, alpha));
        var x = pos.X + (10 + 10) * s;
        var room = right - countWidth - 16 * s - x;
        // The Mac's line, less what the port leaves out, with the folder as the way to add one.
        var (before, after, link) = Measure(Face.Sans, 10, "Your own vibescapes are marked     . Add another by putting its .json file in the vibescapes folder.") <= room
            ? ("Your own vibescapes are marked ", ". Add another by putting its .json file in the ", "vibescapes folder")
            : ("Yours are marked ", ". Add more in the ", "folder");
        DrawText(d, Face.Sans, 10, new Vector2(x, pos.Y), before, grey);
        x += Measure(Face.Sans, 10, before);
        DrawStar(d, new Vector2(x + 5f * s, pos.Y + 6.5f * s), 4.5f * s, Colour(1f, 0.72f, 0.2f, alpha));
        x += 11 * s;
        DrawText(d, Face.Sans, 10, new Vector2(x, pos.Y), after, grey);
        x += Measure(Face.Sans, 10, after);
        var linkMax = new Vector2(x + Measure(Face.Sans, 10, link), pos.Y + 10 * 1.25f * s);
        var hovering = Inside(f.Mouse, new Vector2(x, pos.Y), linkMax);
        DrawText(d, Face.Sans, 10, new Vector2(x, pos.Y), link, Colour(Cyan, alpha));
        if (hovering) d.AddLine(new Vector2(x, linkMax.Y - 1 * s), new Vector2(linkMax.X, linkMax.Y - 1 * s), Colour(Cyan, alpha), 1 * s);
        if (Clicked(ref f, new Vector2(x, pos.Y), linkMax)) actions.OpenCustomFolder();
        x = linkMax.X;
        DrawText(d, Face.Sans, 10, new Vector2(x, pos.Y), ".", grey);
    }

    /// <summary>Texture coordinates that fill <paramref name="boxW"/>×<paramref name="boxH"/> with a picture, cropping its middle (aspectRatio .fill, clipped).</summary>
    internal static (Vector2 Uv0, Vector2 Uv1) FillUv(int picW, int picH, float boxW, float boxH, bool bottomUp)
    {
        var picAspect = (float)picW / picH;
        var boxAspect = boxW / boxH;
        float u0 = 0, u1 = 1, v0 = 0, v1 = 1;
        if (picAspect > boxAspect)
        {
            var keep = boxAspect / picAspect;
            u0 = (1 - keep) / 2;
            u1 = 1 - u0;
        }
        else
        {
            var keep = picAspect / boxAspect;
            v0 = (1 - keep) / 2;
            v1 = 1 - v0;
        }
        return bottomUp ? (new Vector2(u0, v1), new Vector2(u1, v0)) : (new Vector2(u0, v0), new Vector2(u1, v1));
    }
}
