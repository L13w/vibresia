using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>Which header drop-down is open; only one at a time, as on the Mac.</summary>
public enum ChromeMenu { None, Vibescape, Palette, Source, Settings }

/// <summary>What the chrome shows this frame.</summary>
/// <param name="Status">A line about audio (a failed source, or none chosen), or null.</param>
public sealed record ChromeView(
    IReadOnlyList<VibescapeEntry> Vibescapes, VibescapeEntry Current,
    int PaletteIndex,
    AudioChoice ChosenSource, SourceState SourceState,
    bool FullScreen, bool KeepDisplayAwake,
    float[] Bands, float BeatStrength,
    string? Status, MessageSeverity StatusSeverity);

/// <summary>Where the chrome's choices go.</summary>
public sealed record ChromeActions(
    Action<VibescapeEntry> PickVibescape, Action<int> PickPalette, Action<AudioChoice> PickSource,
    Action ToggleFullScreen, Action ToggleKeepDisplayAwake, Action OpenCustomFolder, Action OpenLogFolder);

/// <summary>
/// The Mac's interface over the vibescape (VisualiserMac UI/ContentView,
/// HUDView, DropdownMenu, MeterView, FooterView), measured in the Mac's
/// points and scaled by the display's scaling: the header with the brand
/// and the vibescape, palette and source drop-downs (plus a settings
/// drop-down standing in for the Mac's menu bar), the meter at the right
/// edge, both of which slide away when the pointer leaves the window, and
/// the footer naming the vibescape, which stays. Messages the Mac would show
/// on its intro screen (a failed source) sit under the header until the
/// intro is ported. Everything is drawn on ImGui's draw list with the chrome's
/// own hit-testing, so the layout follows the Swift views rather than ImGui's widgets.
/// </summary>
public sealed class MacChrome : IDisposable
{
    // The Mac's colours (HUDView, VibescapePicker, MeterView).
    private static readonly Vector4 Magenta = new(1f, 0.17f, 0.84f, 1f);
    private static readonly Vector4 Cyan = new(0.17f, 0.95f, 1f, 1f);
    private static readonly Vector4 Violet = new(0.54f, 0.30f, 1f, 1f);
    private static readonly Vector4 SettingsTint = new(0.55f, 0.55f, 0.55f, 1f);

    private readonly GL _gl;
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private ImGuiController _imgui;
    private readonly DisplayScale _scale;
    private readonly ChromeFonts _fonts = new();
    private readonly Log? _log;
    private readonly List<(Vector2 Min, Vector2 Max)> _controls = [];
    private readonly Tween _menuFade = new(0.14, Easing.Out);
    private string? _notice;
    private MessageSeverity _noticeTone;
    private double _noticeLeft;
    private float _s = 1;

    public ChromeMenu OpenMenu { get; private set; }

    public MacChrome(GL gl, IWindow window, IInputContext input, DisplayScale scale, Log? log = null)
    {
        _gl = gl;
        _window = window;
        _input = input;
        _scale = scale;
        _log = log;
        _imgui = CreateController();
        _log?.Info("ui", $"interface fonts: {_fonts.Description}; display scaling {scale.Value * 100:F0}%");
    }

    private ImGuiController CreateController() => new(_gl, _window, _input, () =>
    {
        unsafe { ImGui.GetIO().NativePtr->IniFilename = null; } // no imgui.ini written beside the exe
        _fonts.Build(ImGui.GetIO().Fonts, _scale.Value);
    });

    /// <summary>Shows <paramref name="text"/> under the header for <paramref name="seconds"/>, replacing any notice already there.</summary>
    public void Notice(string text, MessageSeverity tone, double seconds)
    {
        _notice = text;
        _noticeTone = tone;
        _noticeLeft = seconds;
    }

    public void CloseMenu() => OpenMenu = ChromeMenu.None;

    /// <summary>Opens a drop-down as clicking its pill does (it shows on the next frame the header is drawn).</summary>
    public void Open(ChromeMenu menu) => OpenMenu = menu;

    /// <summary>
    /// Whether a click at <paramref name="pointer"/> (window pixels) belongs
    /// to the interface rather than the vibescape: on a control, or anywhere
    /// while a drop-down is open (the Mac's click-away catcher takes it).
    /// </summary>
    public bool TakesClick(Vector2 pointer) =>
        OpenMenu != ChromeMenu.None || _controls.Exists(r => Inside(pointer, r.Min, r.Max));

    /// <summary>Draws the chrome. Call after the vibescape, every frame.</summary>
    public void Render(float dt, ChromeView view, ChromeState chrome, ChromeActions actions)
    {
        if (_scale.Value != _fonts.Scale)
        {
            // Between frames, a new controller with fonts at the new size (this
            // Silk.NET version doesn't expose rebuilding the font texture alone).
            _imgui.Dispose();
            _imgui = CreateController();
            _log?.Info("ui", $"display scaling now {_scale.Value * 100:F0}%; interface fonts rebuilt");
        }
        _imgui.Update(dt);
        _s = _fonts.Scale;
        var io = ImGui.GetIO();
        var frame = new Frame(ImGui.GetForegroundDrawList(), io.DisplaySize, io.MousePos, ImGui.IsMouseClicked(ImGuiMouseButton.Left));
        _controls.Clear();

        var tier = Layout.ForWidth(frame.Size.X / _s);
        var interactive = chrome.Visible;
        if (!tier.ShowsPills() || !interactive) OpenMenu = ChromeMenu.None;
        _menuFade.Target(OpenMenu == ChromeMenu.None ? 0 : 1);
        _menuFade.Advance(dt);

        (Vector2 Min, Vector2 Max, Vector4 Tint)? anchor = null;
        if (chrome.Shown > 0.001f) anchor = DrawHeader(ref frame, view, tier, chrome.Shown, interactive);
        if (chrome.Shown > 0.001f) DrawMeter(frame, view, chrome.Shown);
        DrawFooter(frame, view);
        _noticeLeft -= dt;
        DrawMessages(frame, view);
        if (OpenMenu != ChromeMenu.None && anchor is { } a) DrawMenu(ref frame, view, actions, a.Min, a.Max, a.Tint);

        if (frame.Click && !frame.ClickTaken && OpenMenu != ChromeMenu.None) OpenMenu = ChromeMenu.None; // click away
        _imgui.Render();
    }

    private struct Frame(ImDrawListPtr draw, Vector2 size, Vector2 mouse, bool click)
    {
        public readonly ImDrawListPtr Draw = draw;
        public readonly Vector2 Size = size;
        public readonly Vector2 Mouse = mouse;
        public readonly bool Click = click;
        public bool ClickTaken;
    }

    // ---- header (HUDView) ----

    private enum Lead { None, Glyph, Swatch, Dot, Lines }

    private sealed record Pill(ChromeMenu Menu, Vector4 Tint, Lead Lead, string Title, string Glyph = "", Palette? Swatch = null);

    private (Vector2, Vector2, Vector4)? DrawHeader(ref Frame f, ChromeView v, LayoutTier tier, float shown, bool interactive)
    {
        var s = _s;
        var top = (22 - ChromeState.HeaderSlidePoints * (1 - shown)) * s;
        const float rowPoints = 30; // the brand block: 17 pt name, 3 pt gap, 10 pt version
        var centreY = top + rowPoints / 2 * s;

        // Brand: the wave mark, then the name and version.
        var markMin = new Vector2(28 * s, centreY - 14 * s);
        DrawBrandMark(f.Draw, markMin, 28 * s, shown);
        var textX = markMin.X + (28 + 12) * s;
        DrawTracked(f.Draw, Face.SansBold, 17, new Vector2(textX, centreY - rowPoints / 2 * s), 3.5f, "VIBRESIA", Colour(1, 1, 1, shown));
        DrawTracked(f.Draw, Face.Mono, 10, new Vector2(textX, centreY - rowPoints / 2 * s + 20 * s), 1.6f,
            ("v" + AppVersion.Current).ToUpperInvariant(), Colour(0.45f, 0.45f, 0.45f, shown));

        if (!tier.ShowsPills()) return null;
        var current = v.Current;
        var shortcut = IndexOf(v.Vibescapes, current) is var ci and >= 0 && ci < VibescapeCatalog.ShortcutKeys.Length
            ? VibescapeCatalog.ShortcutKeys[ci].ToString() : "";
        var palette = Palette.At(v.PaletteIndex);
        Pill[] pills =
        [
            new(ChromeMenu.Vibescape, Magenta, Lead.Glyph, current.Title, Glyph: shortcut),
            new(ChromeMenu.Palette, Cyan, Lead.Swatch, palette.Name, Swatch: palette),
            new(ChromeMenu.Source, SourceTint(v.SourceState), Lead.Dot, SourcePill.Title(v.SourceState, v.ChosenSource)),
            new(ChromeMenu.Settings, SettingsTint, Lead.Lines, "SETTINGS"),
        ];
        var showText = tier.ShowsPillText();
        var widths = pills.Select(p => PillWidth(p, showText)).ToArray();
        var height = 26 * s;
        var x = f.Size.X - 28 * s - (widths.Sum() + 8 * s * (pills.Length - 1));
        (Vector2, Vector2, Vector4)? anchor = null;
        for (var i = 0; i < pills.Length; i++)
        {
            var min = new Vector2(x, centreY - height / 2);
            var max = min + new Vector2(widths[i], height);
            DrawPill(f.Draw, pills[i], min, max, showText, shown);
            if (interactive)
            {
                _controls.Add((min, max));
                if (f.Click && !f.ClickTaken && Inside(f.Mouse, min, max))
                {
                    f.ClickTaken = true;
                    OpenMenu = OpenMenu == pills[i].Menu ? ChromeMenu.None : pills[i].Menu;
                    if (OpenMenu != ChromeMenu.None) _menuFade.Snap(0);
                }
            }
            if (pills[i].Menu == OpenMenu) anchor = (min, max, pills[i].Tint);
            x = max.X + 8 * s;
        }
        return anchor;
    }

    private float PillWidth(Pill p, bool showText)
    {
        var s = _s;
        var w = 12f + LeadWidth(p.Lead);
        if (p.Lead != Lead.None) w += 8;
        w *= s;
        if (showText) w += MeasureTracked(Face.Mono, 10, 1.6f, p.Title) + 8 * s;
        return w + (8 + 12) * s; // chevron, padding
    }

    private static float LeadWidth(Lead lead) => lead switch
    {
        Lead.Glyph or Lead.Swatch => 16,
        Lead.Dot => 8,
        Lead.Lines => 12,
        _ => 0,
    };

    private void DrawPill(ImDrawListPtr d, Pill p, Vector2 min, Vector2 max, bool showText, float alpha)
    {
        var s = _s;
        var radius = (max.Y - min.Y) / 2;
        d.AddRectFilled(min, max, Colour(0, 0, 0, 0.45f * alpha), radius);
        d.AddRect(min, max, Colour(p.Tint, 0.5f * alpha), radius, ImDrawFlags.None, 1 * s);
        var cy = (min.Y + max.Y) / 2;
        var x = min.X + 12 * s;
        switch (p.Lead)
        {
            case Lead.Glyph:
                DrawText(d, Face.MonoBold, 11, new Vector2(x, cy - 11 * s / 2), p.Glyph, Colour(p.Tint, alpha));
                break;
            case Lead.Swatch:
                DrawSwatch(d, new Vector2(x, cy - 6 * s), p.Swatch!, alpha);
                break;
            case Lead.Dot:
                var c = new Vector2(x + 4 * s, cy);
                d.AddCircleFilled(c, 10 * s, Colour(p.Tint, 0.08f * alpha)); // the dot's glow (shadow radius 6)
                d.AddCircleFilled(c, 7 * s, Colour(p.Tint, 0.18f * alpha));
                d.AddCircleFilled(c, 4 * s, Colour(p.Tint, alpha));
                break;
            case Lead.Lines:
                for (var i = -1; i <= 1; i++)
                    d.AddLine(new Vector2(x, cy + i * 3.5f * s), new Vector2(x + 12 * s, cy + i * 3.5f * s), Colour(0.85f, 0.85f, 0.85f, alpha), 1.3f * s);
                break;
        }
        x += (LeadWidth(p.Lead) + (p.Lead != Lead.None ? 8 : 0)) * s;
        if (showText)
        {
            DrawTracked(d, Face.Mono, 10, new Vector2(x, cy - 10 * s / 2), 1.6f, p.Title, Colour(0.85f, 0.85f, 0.85f, alpha));
            x += MeasureTracked(Face.Mono, 10, 1.6f, p.Title) + 8 * s;
        }
        DrawChevron(d, new Vector2(x + 4 * s, cy), Colour(0.55f, 0.55f, 0.55f, alpha));
    }

    private void DrawBrandMark(ImDrawListPtr d, Vector2 min, float size, float alpha)
    {
        // BrandWave.points: a heartbeat line in a unit box, y down.
        ReadOnlySpan<(float X, float Y)> unit = [(0.00f, 0.50f), (0.22f, 0.50f), (0.32f, 0.20f), (0.45f, 0.80f), (0.58f, 0.30f), (0.70f, 0.65f), (0.78f, 0.50f), (1.00f, 0.50f)];
        var points = new Vector2[unit.Length];
        for (var i = 0; i < unit.Length; i++) points[i] = min + new Vector2(unit[i].X, unit[i].Y) * size;
        // The glow (shadow radius 8 at 0.7), as widening faint strokes, then the 2.4 pt line.
        for (var k = 4; k >= 1; k--)
            RoundStroke(d, points, Colour(Magenta, 0.7f * 0.12f * alpha), (2.4f + k * 3f) * _s);
        RoundStroke(d, points, Colour(Magenta, alpha), 2.4f * _s);
    }

    /// <summary>
    /// A line through <paramref name="points"/> with round caps and joins, as
    /// the Mac strokes the mark. ImGui's polylines mitre their joins, which at
    /// the wave's sharp peaks shoot long spikes out of a thick stroke.
    /// </summary>
    private static void RoundStroke(ImDrawListPtr d, Vector2[] points, uint colour, float width)
    {
        for (var i = 0; i + 1 < points.Length; i++) d.AddLine(points[i], points[i + 1], colour, width);
        foreach (var p in points) d.AddCircleFilled(p, width / 2, colour);
    }

    private void DrawSwatch(ImDrawListPtr d, Vector2 min, Palette p, float alpha)
    {
        var max = min + new Vector2(16, 12) * _s;
        d.AddRectFilledMultiColor(min, max, Colour(p.Shadow, alpha), Colour(p.Highlight, alpha), Colour(p.Highlight, alpha), Colour(p.Shadow, alpha));
        d.AddRect(min, max, Colour(1, 1, 1, 0.25f * alpha), 3 * _s, ImDrawFlags.None, Math.Max(1, 0.5f * _s));
    }

    private void DrawChevron(ImDrawListPtr d, Vector2 centre, uint colour)
    {
        var s = _s;
        Vector2[] pts = [centre + new Vector2(-3.5f, -1.8f) * s, centre + new Vector2(0, 1.8f) * s, centre + new Vector2(3.5f, -1.8f) * s];
        d.AddPolyline(ref pts[0], pts.Length, colour, ImDrawFlags.None, 1.4f * s);
    }

    private void DrawCheck(ImDrawListPtr d, Vector2 centre, uint colour)
    {
        var s = _s;
        Vector2[] pts = [centre + new Vector2(-4, 0) * s, centre + new Vector2(-1.3f, 2.8f) * s, centre + new Vector2(4, -3) * s];
        d.AddPolyline(ref pts[0], pts.Length, colour, ImDrawFlags.None, 1.6f * s);
    }

    // ---- drop-down lists (DropdownMenu) ----

    private sealed record Row(string Title, Action Pick, bool Selected, string? Subtitle = null, string? Glyph = null, Palette? Swatch = null);

    private IEnumerable<Row> RowsFor(ChromeView v, ChromeActions a) => OpenMenu switch
    {
        ChromeMenu.Vibescape => v.Vibescapes.Select((e, i) => new Row(e.Title, () => a.PickVibescape(e), e.Id == v.Current.Id,
            Glyph: i < VibescapeCatalog.ShortcutKeys.Length ? VibescapeCatalog.ShortcutKeys[i].ToString() : "")),
        ChromeMenu.Palette => Palette.All.Select((p, i) => new Row(p.Name, () => a.PickPalette(i), i == v.PaletteIndex, Swatch: p)),
        ChromeMenu.Source => SourcePill.Choices.Select(c => new Row(SourcePill.MenuTitle(c), () => a.PickSource(c), c == v.ChosenSource, SourcePill.Subtitle(c))),
        ChromeMenu.Settings =>
        [
            new Row("Full Screen", a.ToggleFullScreen, v.FullScreen, "F11"),
            new Row("Keep the Display Awake", a.ToggleKeepDisplayAwake, v.KeepDisplayAwake, "Stops Windows dimming or sleeping it"),
            new Row("Open Custom Vibescapes Folder", a.OpenCustomFolder, false, "Drop .json vibescapes here"),
            new Row("Open Log Folder", a.OpenLogFolder, false, "F12"),
        ],
        _ => [],
    };

    private void DrawMenu(ref Frame f, ChromeView v, ChromeActions actions, Vector2 pillMin, Vector2 pillMax, Vector4 tint)
    {
        var s = _s;
        var alpha = (float)_menuFade.Value;
        var rows = RowsFor(v, actions).ToList();
        if (rows.Count == 0) return;
        var hasLead = rows.Exists(r => r.Glyph is not null || r.Swatch is not null);
        float RowHeight(Row r) => (r.Subtitle is null ? 11 + 2 * 5 : 11 + 1 + 9 + 2 * 6) * s;
        var inner = rows.Max(r =>
            (12 + (hasLead ? 16 + 10 : 0)) * s
            + Math.Max(MeasureTracked(Face.SansSemibold, 11, 1.4f, r.Title), r.Subtitle is null ? 0 : Measure(Face.Mono, 9, r.Subtitle))
            + (12 + 9 + 12) * s);
        var width = Math.Max(180 * s, inner + 2 * 6 * s);
        var height = rows.Sum(RowHeight) + 2 * 6 * s;
        var min = new Vector2(pillMax.X - width, pillMin.Y + 34 * s - (1 - alpha) * 8 * s);
        min.X = Math.Max(8 * s, min.X);
        var max = min + new Vector2(width, height);
        _controls.Add((min, max));

        var d = f.Draw;
        for (var k = 1; k <= 6; k++) // shadow: black 0.55, radius 18, 6 pt down
        {
            var grow = k * 3 * s;
            d.AddRectFilled(min + new Vector2(-grow, 6 * s - grow), max + new Vector2(grow, 6 * s + grow), Colour(0, 0, 0, 0.55f / 6 * (1 - k / 7f) * alpha), (12 + k * 3) * s);
        }
        d.AddRectFilled(min, max, Colour(0.06f, 0.03f, 0.11f, 0.96f * alpha), 12 * s);
        d.AddRect(min, max, Colour(tint, 0.35f * alpha), 12 * s, ImDrawFlags.None, 1 * s);

        var y = min.Y + 6 * s;
        Action? picked = null;
        foreach (var r in rows)
        {
            var rh = RowHeight(r);
            var rMin = new Vector2(min.X + 6 * s, y);
            var rMax = new Vector2(max.X - 6 * s, y + rh);
            if (r.Selected) d.AddRectFilled(rMin, rMax, Colour(tint, 0.16f * alpha), 6 * s);
            var x = rMin.X + 12 * s;
            var titleTop = y + (r.Subtitle is null ? 5 : 6) * s;
            if (r.Swatch is { } sw) DrawSwatch(d, new Vector2(x, y + rh / 2 - 6 * s), sw, alpha);
            else if (r.Glyph is { } g)
                DrawText(d, Face.Mono, 11, new Vector2(x, titleTop), g, r.Selected ? Colour(tint, alpha) : Colour(0.45f, 0.45f, 0.45f, alpha));
            if (hasLead) x += (16 + 10) * s;
            DrawTracked(d, Face.SansSemibold, 11, new Vector2(x, titleTop), 1.4f, r.Title,
                r.Selected ? Colour(1, 1, 1, alpha) : Colour(0.8f, 0.8f, 0.8f, alpha));
            if (r.Subtitle is { } sub)
                DrawText(d, Face.Mono, 9, new Vector2(x, titleTop + 12 * s), sub, Colour(0.4f, 0.4f, 0.4f, alpha));
            if (r.Selected) DrawCheck(d, new Vector2(rMax.X - 12 * s - 4.5f * s, y + rh / 2), Colour(tint, alpha));
            if (f.Click && !f.ClickTaken && Inside(f.Mouse, rMin, rMax)) picked = r.Pick;
            y += rh;
        }
        if (f.Click && !f.ClickTaken && Inside(f.Mouse, min, max))
        {
            f.ClickTaken = true; // a click on the list's padding keeps it open
            if (picked is not null)
            {
                OpenMenu = ChromeMenu.None;
                picked();
            }
        }
    }

    // ---- meter (MeterView) ----

    private void DrawMeter(Frame f, ChromeView v, float shown)
    {
        var s = _s;
        var alpha = 0.85f * shown;
        var size = new Vector2(240, 64) * s;
        var right = f.Size.X - 28 * s + ChromeState.MeterSlidePoints * s * (1 - shown);
        var areaTop = 60 * s;
        var areaBottom = f.Size.Y - 90 * s;
        var min = new Vector2(right - size.X, areaTop + Math.Max(0, (areaBottom - areaTop - size.Y) / 2));
        var max = min + size;
        var d = f.Draw;
        d.AddRectFilled(min, max, Colour(0, 0, 0, 0.45f * alpha), 14 * s);
        d.AddRect(min, max, Colour(1, 1, 1, 0.08f * alpha), 14 * s, ImDrawFlags.None, 1 * s);
        d.AddRectFilled(min, max, Colour(1, 1, 1, 0.02f * alpha)); // the canvas's background grid

        var heights = MeterBars.Heights(v.Bands);
        var barWidth = size.X / MeterBars.Count;
        for (var i = 0; i < MeterBars.Count; i++)
        {
            var bh = heights[i] * size.Y;
            if (bh <= 0) continue;
            var colour = MeterBars.Third(i) switch { 0 => Magenta, 1 => Violet, _ => Cyan };
            var bMin = new Vector2(min.X + i * barWidth + 1 * s, max.Y - bh);
            var bMax = new Vector2(min.X + (i + 1) * barWidth - 1 * s, max.Y);
            // Full colour at the bar's top, 0.2 at the bottom.
            d.AddRectFilledMultiColor(bMin, bMax, Colour(colour, alpha), Colour(colour, alpha), Colour(colour, 0.2f * alpha), Colour(colour, 0.2f * alpha));
        }
        if (MeterBars.BeatTint(v.BeatStrength) is var tint and > 0)
            d.AddRectFilled(min, max, Colour(Magenta, tint * alpha));
    }

    // ---- footer (FooterView) ----

    private void DrawFooter(Frame f, ChromeView v)
    {
        var s = _s;
        var x = (28 + 4) * s;
        var bottom = f.Size.Y - (14 + 8) * s;
        var credit = Credit(v.Current);
        var nameTop = bottom - 14 * s - (credit is null ? 0 : (4 + 10) * s);
        DrawTracked(f.Draw, Face.MonoBold, 14, new Vector2(x, nameTop), 0.5f, Truncate(Face.MonoBold, 14, 0.5f, v.Current.Title, 260 * s), Colour(1, 1, 1, 1));
        if (credit is not null)
            DrawTracked(f.Draw, Face.Mono, 10, new Vector2(x, nameTop + (14 + 4) * s), 0.8f,
                Truncate(Face.Mono, 10, 0.8f, $"Made with ♥ by {credit}", 260 * s), Colour(0.55f, 0.55f, 0.55f, 1), heart: Colour(0.69f, 0.36f, 0.94f, 1));
    }

    /// <summary>
    /// Who to credit (upstream AppState.credit(for:)): a custom vibescape's
    /// author, unless blank or Minith Labs, whose name the Mac already shows
    /// in its corner. Built-ins are all Minith Labs, so it is only ever someone else's.
    /// </summary>
    public static string? Credit(VibescapeEntry entry)
    {
        var author = entry.Author.Trim();
        return author.Length == 0 || author.Equals(VibescapeCatalog.BuiltinAuthor, StringComparison.OrdinalIgnoreCase) ? null : author;
    }

    // ---- messages (the Mac shows these on its intro screen) ----

    private void DrawMessages(Frame f, ChromeView v)
    {
        var s = _s;
        var y = 72 * s;
        var wrap = Math.Min(f.Size.X * 0.6f, 640 * s);
        if (_notice is { } notice && _noticeLeft > 0) y = DrawMessage(f, notice, _noticeTone, y, wrap) + 8 * s;
        if (v.Status is { } status) DrawMessage(f, status, v.StatusSeverity, y, wrap);
    }

    private float DrawMessage(Frame f, string text, MessageSeverity tone, float top, float wrap)
    {
        var s = _s;
        var font = _fonts.Get(Face.Sans, 13);
        var px = 13 * s;
        ImGui.PushFont(font);
        var k = px / font.FontSize;
        var size = ImGui.CalcTextSize(text, false, wrap / k) * k;
        ImGui.PopFont();
        var pad = new Vector2(14, 9) * s;
        var min = new Vector2((f.Size.X - size.X) / 2 - pad.X, top);
        var max = min + size + pad * 2;
        f.Draw.AddRectFilled(min, max, Colour(0, 0, 0, 0.6f), 10 * s);
        var colour = tone switch
        {
            MessageSeverity.Error => Colour(1f, 0.55f, 0.5f, 1),
            MessageSeverity.Hint => Colour(1f, 0.9f, 0.6f, 1),
            _ => Colour(0.9f, 0.9f, 0.95f, 1),
        };
        // Untrusted text (custom vibescape names, driver logs) goes in as text, never as a format string.
        f.Draw.AddText(font, px, min + pad, colour, text, wrap);
        return max.Y;
    }

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
        foreach (var ch in text) w += ImGui.CalcTextSize(ch.ToString()).X * k + tracking * _s;
        ImGui.PopFont();
        return w;
    }

    private void DrawTracked(ImDrawListPtr d, Face face, float points, Vector2 pos, float tracking, string text, uint colour, uint? heart = null)
    {
        var font = _fonts.Get(face, points);
        var px = points * _s;
        var k = px / font.FontSize;
        ImGui.PushFont(font);
        foreach (var ch in text)
        {
            var w = ImGui.CalcTextSize(ch.ToString()).X * k;
            if (ch == '♥' && heart is { } h) DrawHeart(d, pos + new Vector2(0, px * 0.12f), px * 0.8f, h);
            else d.AddText(font, px, pos, colour, ch.ToString());
            pos.X += (ch == '♥' ? px * 0.8f : w) + tracking * _s;
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

    private static int IndexOf(IReadOnlyList<VibescapeEntry> list, VibescapeEntry e)
    {
        for (var i = 0; i < list.Count; i++) if (list[i].Id == e.Id) return i;
        return -1;
    }

    private static bool Inside(Vector2 p, Vector2 min, Vector2 max) => p.X >= min.X && p.X < max.X && p.Y >= min.Y && p.Y < max.Y;

    private static uint Colour(float r, float g, float b, float a) => ImGui.GetColorU32(new Vector4(r, g, b, Math.Clamp(a, 0, 1)));
    private static uint Colour(Vector4 c, float a) => Colour(c.X, c.Y, c.Z, a);
    private static uint Colour((float R, float G, float B) c, float a) => Colour(c.R, c.G, c.B, a);

    public void Dispose() => _imgui.Dispose();
}
