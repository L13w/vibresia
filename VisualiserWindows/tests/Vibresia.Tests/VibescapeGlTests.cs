using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.Tests;

/// <summary>Switching, custom vibescapes and the palette grade, on real GL (Mesa under xvfb).</summary>
[Collection("GL")]
[Trait("Category", "GL")]
public sealed class VibescapeGlTests : IDisposable
{
    private const int W = 160, H = 90;
    private static readonly VibescapeCatalog Catalog = new([]);

    // Upstream's starter template (Vibescapes.swift CustomVibescapeGuide.starterTemplate).
    private const string StarterTemplate = """
        // Your vibescape. Edit the colour, press Test.
        // Full list of uniforms and helpers: press "What's available?" below.
        vec3 render(vec2 p, float t)
        {
            // A colour that brightens with the bass.
            float pulse = 0.5 + 0.5 * sin(t * 2.0);
            float amount = uBass * 0.6 + pulse * 0.2;

            vec3 colour = vec3(1.0, 0.2, 0.8) * amount;   // pink
            colour += vec3(0.2, 0.9, 1.0) * uTreble;      // cyan on the highs

            // Rest dim and still when there's no music — quiet, not black.
            return colour * (0.2 + 0.8*uActive);
        }
        """;

    private readonly IWindow _window;
    private readonly GL _gl;
    private readonly uint _fbo, _tex;
    private readonly string _logDir = Path.Combine(Path.GetTempPath(), "vibresia-vgl-" + Guid.NewGuid().ToString("N"));

    public VibescapeGlTests()
    {
        (_window, _gl, _) = GlWindow.Create(GlWindow.Options(visible: false, fullscreen: false));
        _fbo = _gl.GenFramebuffer();
        _tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _tex);
        _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, W, H, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _tex, 0);
    }

    private static VibescapeEntry Custom(string body, string id = "test") => new(VibescapeId.Custom(id), id.ToUpperInvariant(), "me", body);

    /// <summary>Draws one frame into the test's own framebuffer (as the window would be) and reads it back.</summary>
    private byte[] Draw(VibescapeRenderer r, Action<FrameUniforms>? tweak = null, (int W, int H)? screen = null, float crtOpacity = 0)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        var u = new FrameUniforms();
        TestUniforms.Music(u);
        u.Beat = 1;
        u.ResolutionX = W; u.ResolutionY = H;
        tweak?.Invoke(u);
        r.Draw(u, 1 / 60.0, screen?.W ?? 0, screen?.H ?? 0);
        if (crtOpacity > 0) r.DrawCrt(crtOpacity, screen?.W ?? (int)u.ResolutionX, screen?.H ?? (int)u.ResolutionY);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        var px = new byte[W * H * 4];
        _gl.ReadPixels<byte>(0, 0, W, H, PixelFormat.Rgba, PixelType.UnsignedByte, px);
        Assert.Equal(GLEnum.NoError, _gl.GetError());
        return px;
    }

    private static int Distinct(byte[] px) =>
        Enumerable.Range(0, px.Length / 4).Select(i => (px[i * 4], px[i * 4 + 1], px[i * 4 + 2])).Distinct().Count();

    public static TheoryData<string> Listed() => new(VibescapeCatalog.Builtins.Select(b => b.Slug));

    /// <summary>Each listed built-in compiles and draws a picture through the grade, with its extra uniforms bound.</summary>
    [Theory]
    [MemberData(nameof(Listed))]
    public void EveryListedBuiltinDrawsAPicture(string slug)
    {
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Catalog.Find(VibescapeId.Builtin(slug))!));
        var px = Draw(r);
        Assert.True(Distinct(px) > 10, $"{slug}: expected a picture, got {Distinct(px)} distinct colours");
    }

    /// <summary>The grade maps a known colour exactly as upstream's gradeFS does with the NEON palette.</summary>
    [Fact]
    public void CustomVibescapesAreGradedThroughNeon()
    {
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Custom("vec3 render(vec2 p, float t) { return vec3(0.5, 0.25, 0.75); }")));
        var px = Draw(r);
        var expected = Grade((0.5f, 0.25f, 0.75f), Palette.Neon);
        AssertPixel(px, expected);
    }

    /// <summary>
    /// Matches the Mac, including its quirk: upstream declares uPalette in the
    /// wrapper that comes after the user's code, so a custom vibescape can't
    /// use it (GLSL needs a declaration before use), though upstream's guide
    /// suggests it. If upstream moves the declaration, extraction picks it up
    /// and this test says so.
    /// </summary>
    [Fact]
    public void ACustomUsingUPaletteFailsToCompileAsOnTheMac()
    {
        using var r = new VibescapeRenderer(_gl);
        var p = r.Use(Custom("vec3 render(vec2 p, float t) { return uPalette[1]; }"));
        Assert.NotNull(p);
        Assert.Contains("uPalette", p!.DriverLog);
        Assert.Equal(1, p.Line);
    }

    /// <summary>An ungraded vibescape's colours reach the screen unchanged (the grade is switched off, not skipped).</summary>
    [Fact]
    public void UngradedVibescapesPassThroughUnchanged()
    {
        // A known colour drawn under an ungraded built-in's id, so only the grade setting differs.
        var entry = new VibescapeEntry(VibescapeId.Builtin("danceFloor"), "X", "", "vec3 render(vec2 p, float t) { return vec3(0.5, 0.25, 0.75); }");
        Assert.False(entry.Graded);
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(entry));
        AssertPixel(Draw(r), (0.5f, 0.25f, 0.75f));
    }

    [Fact]
    public void TheUpstreamStarterTemplateCompilesAndDraws()
    {
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Custom(StarterTemplate)));
        Assert.False(Draw(r).All(b => b == 0));
    }

    /// <summary>A compile error comes back with the line in the user's own code, not the assembled shader's.</summary>
    [Fact]
    public void ACompileErrorNamesTheUsersLine()
    {
        using var r = new VibescapeRenderer(_gl);
        var body = "vec3 render(vec2 p, float t)\n{\n    float x = nope * 2.0;\n    return vec3(x);\n}";
        var problem = r.Use(Custom(body));
        Assert.NotNull(problem);
        Assert.Equal(3, problem!.Line);
        Assert.Contains("nope", problem.DriverLog);
    }

    [Fact]
    public void ABrokenCustomLeavesThePreviousVibescapeDrawing()
    {
        using var r = new VibescapeRenderer(_gl);
        var kaleidoscope = Catalog.Find(VibescapeCatalog.Default)!;
        Assert.Null(r.Use(kaleidoscope));
        Assert.NotNull(r.Use(Custom("vec3 render(vec2 p, float t) { return oops; }")));
        Assert.Equal(kaleidoscope, r.Current);
        Assert.True(Distinct(Draw(r)) > 50);
    }

    [Fact]
    public void AShapeProblemIsReportedWithoutCompiling()
    {
        using var r = new VibescapeRenderer(_gl);
        var p = r.Use(Custom("void main() { fragColor = vec4(1.0); }"));
        Assert.Contains("render, not main", p!.Message);
        Assert.Null(r.Current);
    }

    /// <summary>The host logs a failed compile with the vibescape's id, and a broken start falls back.</summary>
    [Fact]
    public void HostFallsBackFromABrokenStartAndLogsTheId()
    {
        var (window, gl, _) = GlWindow.Create(GlWindow.Options(visible: false, fullscreen: false));
        try
        {
            using var log = new Log(_logDir);
            var fallback = Catalog.Find(VibescapeCatalog.Default)!;
            var host = new VibescapeHost(window, gl, log, Custom("vec3 render(vec2 p, float t) { return bad; }", "broken-one"),
                fallback, (u, _) => TestUniforms.Music(u));
            Assert.Equal(fallback.Id, host.Current.Id);
            Assert.Equal(VibescapeId.Custom("broken-one"), host.StartupProblem!.Value.Entry.Id);
            var problem = host.Show(Custom("vec3 render(vec2 p, float t) { return worse; }", "also-broken"));
            Assert.NotNull(problem);
            Assert.Equal(fallback.Id, host.Current.Id);
            var text = File.ReadAllText(log.FilePath);
            Assert.Contains("custom:broken-one didn't compile", text);
            Assert.Contains("falling back to builtin:kaleidoscope", text);
            Assert.Contains("custom:also-broken didn't compile; still showing builtin:kaleidoscope", text);
        }
        finally
        {
            window.Dispose();
        }
    }

    /// <summary>
    /// The Mac's chrome draws its header with each drop-down open, the meter,
    /// the footer and messages without an ImGui or GL error; its controls take
    /// clicks and the picture doesn't; an open drop-down takes every click;
    /// and once the pointer has gone the header slides away and stops taking clicks.
    /// </summary>
    [Fact]
    public void ChromeDrawsEveryMenuAndOnlyItsControlsTakeClicks()
    {
        var (window, gl, _) = GlWindow.Create(GlWindow.Options(visible: true, fullscreen: false));
        try
        {
            using var log = new Log(_logDir);
            var catalog = new VibescapeCatalog([new CustomVibescape("mine", "Mine", "Ada", "vec3 render(vec2 p, float t){return vec3(1.0);}", true)]);
            var host = new VibescapeHost(window, gl, log, catalog.Entries[^1], catalog.Entries[0], (u, _) => TestUniforms.Music(u));
            var input = Silk.NET.Input.InputWindowExtensions.CreateInput(window);
            var chrome = new MacChrome(gl, window, input, host.Scale, log);
            host.ReleaseOnClose(chrome);
            // A '%' in untrusted text must be drawn, not read as a format directive.
            chrome.Notice("Line 3 needs a fix: %s %n %d\nsecond line", MessageSeverity.Error, 10);
            var state = new ChromeState();
            var inside = true;
            var actions = new ChromeActions();
            var bands = Enumerable.Range(0, FrameUniforms.BandCount).Select(i => (i % 7) / 7f).ToArray();
            var menus = new[] { ChromeMenu.Vibescape, ChromeMenu.Palette, ChromeMenu.Source, ChromeMenu.Settings, ChromeMenu.None };
            var frames = 0;
            var headerPoint = System.Numerics.Vector2.Zero;
            bool? controlTook = null, pictureTook = null, menuTookAnywhere = null, hiddenTook = null;
            host.Overlay += dt =>
            {
                state.Advance(dt, () => inside, chrome.AnythingOpen);
                var view = new ChromeView
                {
                    Catalog = catalog, Current = host.Current, PaletteIndex = 2, ChosenSource = AudioChoice.Loopback,
                    SourceState = SourceState.Live, KeepDisplayAwake = true, Bands = bands, BeatStrength = 0.6f,
                };
                if (frames < menus.Length && menus[frames] != ChromeMenu.None) chrome.Open(menus[frames]);
                chrome.Render((float)dt, view, state, actions);
                var size = window.Size;
                // The settings pill is the rightmost: 28 pt in from the edge, in the 30 pt header row 22 pt down.
                headerPoint = new System.Numerics.Vector2(size.X - 40 * host.Scale.Value, (22 + 15) * host.Scale.Value);
                if (frames == 1) menuTookAnywhere = chrome.TakesClick(new(size.X / 2f, size.Y / 2f));
                if (frames == 5)
                {
                    chrome.CloseMenu();
                    controlTook = chrome.TakesClick(headerPoint);
                    pictureTook = chrome.TakesClick(new(size.X / 2f, size.Y / 2f));
                    inside = false;
                }
                if (frames == 120) hiddenTook = chrome.TakesClick(headerPoint);
                if (++frames >= 121) window.Close();
            };
            window.Run();
            Assert.Equal(121, frames);
            Assert.True(menuTookAnywhere, "an open drop-down should take a click anywhere");
            Assert.True(controlTook, "a header pill should take its click");
            Assert.False(pictureTook, "a click on the picture is the vibescape's");
            Assert.False(hiddenTook, "a hidden header takes no clicks");
            Assert.Equal(0f, state.Shown);
            Assert.Contains("interface fonts:", File.ReadAllText(log.FilePath));
        }
        finally
        {
            window.Dispose();
        }
    }

    /// <summary>
    /// The intro, the credits and the View All panel draw without a GL error:
    /// the intro takes every click while it is up, an open panel takes every
    /// click, the panel's built-in pictures decode, and drawing a custom
    /// vibescape's picture is how the panel finds one that needs a fix.
    /// </summary>
    [Fact]
    public void TheIntroAndPanelsDrawAndTakeEveryClick()
    {
        var (window, gl, _) = GlWindow.Create(GlWindow.Options(visible: true, fullscreen: false));
        try
        {
            using var log = new Log(_logDir);
            var catalog = new VibescapeCatalog(
            [
                new CustomVibescape("mine", "Mine", "Ada", "vec3 render(vec2 p, float t){return vec3(p.x, 0.5, 1.0);}", true),
                new CustomVibescape("broken", "Broken", "", "vec3 render(vec2 p, float t){return vec3(uBas);}", false),
            ], [VibescapeId.Custom("mine"), VibescapeId.Custom("broken")]);
            var host = new VibescapeHost(window, gl, log, catalog.Find(VibescapeCatalog.Default)!, catalog.Find(VibescapeCatalog.Default)!, (u, _) => TestUniforms.Music(u));
            var input = Silk.NET.Input.InputWindowExtensions.CreateInput(window);
            var chrome = new MacChrome(gl, window, input, host.Scale, log);
            host.ReleaseOnClose(chrome);
            var state = new ChromeState();
            var frames = 0;
            bool? introTook = null, creditsTook = null, panelTook = null, clearTook = null;
            var glError = GLEnum.NoError;
            host.Overlay += dt =>
            {
                var intro = frames < 30;
                state.Advance(dt, () => true, chrome.AnythingOpen);
                var view = new ChromeView
                {
                    Catalog = catalog, Current = host.Current, StartAtLogin = true,
                    IntroShown = intro, IntroProblem = intro ? "Couldn't start system audio: %s device gone." : null,
                };
                if (frames == 40) chrome.Open(ChromePanel.Credits);
                if (frames == 70) chrome.Open(ChromePanel.ViewAll);
                chrome.Render((float)dt, view, state, new ChromeActions());
                var centre = new System.Numerics.Vector2(window.Size.X / 2f, window.Size.Y / 2f);
                if (frames == 20) introTook = chrome.TakesClick(centre);
                if (frames == 60) creditsTook = chrome.TakesClick(new(4, 4));
                if (frames == 150) panelTook = chrome.TakesClick(new(4, 4));
                if (frames == 160)
                {
                    chrome.CloseMenu();
                    glError = gl.GetError();
                }
                if (frames == 170) clearTook = chrome.TakesClick(centre);
                if (++frames >= 171) window.Close();
            };
            window.Run();
            Assert.True(introTook, "the intro covers the window and takes every click");
            Assert.True(creditsTook, "the open credits take a click anywhere (one outside closes them)");
            Assert.True(panelTook, "the View All panel is a sheet: it takes every click");
            Assert.False(clearTook, "with nothing open and no intro, the picture gets its clicks");
            Assert.Equal(GLEnum.NoError, glError);
            var text = File.ReadAllText(log.FilePath);
            Assert.Contains("custom:broken needs a fix", text);
            Assert.DoesNotContain("custom:mine needs a fix", text);
            Assert.DoesNotContain("no picture for", text);
        }
        finally
        {
            window.Dispose();
        }
    }

    /// <summary>
    /// The chosen palette is what uPalette hands a shader, as upstream binds
    /// it: Dance Floor is drawn ungraded but colours itself from uPalette, so
    /// a palette change reaches its pixels only through the uniform.
    /// </summary>
    [Fact]
    public void UPaletteFollowsTheChosenPalette()
    {
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Catalog.Find(VibescapeId.Builtin("danceFloor"))!));
        var neon = Draw(r);
        r.Palette = Palette.All[2]; // TOXIC: green, far from NEON's magenta
        var toxic = Draw(r);
        long Channel(byte[] px, int c) { long sum = 0; for (var i = c; i < px.Length; i += 4) sum += px[i]; return sum; }
        Assert.True(Channel(toxic, 1) > Channel(neon, 1), "TOXIC should make Dance Floor greener than NEON");
    }

    /// <summary>The chosen palette grades the frame, exactly as upstream's gradeFS does with it.</summary>
    [Fact]
    public void TheChosenPaletteGradesTheFrame()
    {
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Custom("vec3 render(vec2 p, float t) { return vec3(0.5, 0.25, 0.75); }")));
        foreach (var palette in Palette.All)
        {
            r.Palette = palette;
            AssertPixel(Draw(r), Grade((0.5f, 0.25f, 0.75f), palette));
        }
    }

    /// <summary>Every uniform upstream's prelude declares is fed by the renderer, or deliberately left at zero (NotFed).</summary>
    [Fact]
    public void EveryPreludeUniformIsFedOrDeclaredNotFed()
    {
        var declared = System.Text.RegularExpressions.Regex.Matches(ShaderSources.Prelude, @"^uniform\s+\w+\s+(\w+)", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains("uMouse", declared);
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Catalog.Find(VibescapeCatalog.Default)!));
        Draw(r);
        var missing = declared.Except(r.Fed).Except(VibescapeRenderer.NotFed).ToList();
        Assert.True(missing.Count == 0, "not fed and not declared NotFed: " + string.Join(", ", missing));
        Assert.Empty(VibescapeRenderer.NotFed.Intersect(r.Fed));
    }

    /// <summary>A scene drawn at 1× smaller than the screen is stretched to fill all of it.</summary>
    [Fact]
    public void ASmallerSceneFillsTheWholeScreen()
    {
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Custom("vec3 render(vec2 p, float t) { return vec3(0.5, 0.25, 0.75); }")));
        var px = Draw(r, u => { u.ResolutionX = W / 2; u.ResolutionY = H / 2; }, screen: (W, H));
        var want = Grade((0.5f, 0.25f, 0.75f), Palette.Neon);
        foreach (var (x, y) in new[] { (0, 0), (W - 1, H - 1), (W - 1, 0), (W / 2, H / 2) })
            AssertPixel(px, want, x, y);
    }

    /// <summary>The CRT overlay darkens the edges (vignette) more than the centre, and the beat flash tints the centre pink.</summary>
    [Fact]
    public void CrtOverlayAndBeatFlashDrawOverTheFrame()
    {
        using var r = new VibescapeRenderer(_gl);
        Assert.Null(r.Use(Custom("vec3 render(vec2 p, float t) { return vec3(0.6); }")));
        var plain = Draw(r);
        var crt = Draw(r, crtOpacity: 1);
        static int Lum(byte[] px, int x, int y) { var i = (y * W + x) * 4; return px[i] + px[i + 1] + px[i + 2]; }
        Assert.True(Lum(crt, 0, 0) < Lum(plain, 0, 0) - 60, "corner should be vignetted");
        Assert.True(Lum(plain, W / 2, H / 2) - Lum(crt, W / 2, H / 2) < 40, "centre mostly untouched");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        r.DrawFlash(0.4f, 1f, W, H, 0.1f); // small point size so the flash covers the test frame
        var flashed = new byte[W * H * 4];
        _gl.ReadPixels<byte>(0, 0, W, H, PixelFormat.Rgba, PixelType.UnsignedByte, flashed);
        var c = (H / 2 * W + W / 2) * 4;
        Assert.True(flashed[c] > crt[c] + 20 && flashed[c + 2] > crt[c + 2] + 10, "centre should be tinted pink");
        Assert.Equal(GLEnum.NoError, _gl.GetError());
    }

    /// <summary>Upstream gradeFS, on the CPU.</summary>
    private static (float R, float G, float B) Grade((float R, float G, float B) c, Palette p)
    {
        static float Luma((float R, float G, float B) v) => 0.2126f * v.R + 0.7152f * v.G + 0.0722f * v.B;
        var l = Luma(c);
        (float R, float G, float B) graded = (p.Shadow.R + (p.Highlight.R - p.Shadow.R) * l,
            p.Shadow.G + (p.Highlight.G - p.Shadow.G) * l, p.Shadow.B + (p.Highlight.B - p.Shadow.B) * l);
        c = (c.R + (graded.R - c.R) * p.Duotone, c.G + (graded.G - c.G) * p.Duotone, c.B + (graded.B - c.B) * p.Duotone);
        l = Luma(c);
        c = (l + (c.R - l) * p.Saturation, l + (c.G - l) * p.Saturation, l + (c.B - l) * p.Saturation);
        c = (c.R + p.Shadow.R * p.Lift * (1 - l), c.G + p.Shadow.G * p.Lift * (1 - l), c.B + p.Shadow.B * p.Lift * (1 - l));
        return (Math.Clamp(c.R, 0, 1), Math.Clamp(c.G, 0, 1), Math.Clamp(c.B, 0, 1));
    }

    private static void AssertPixel(byte[] px, (float R, float G, float B) expected, int x = W / 2, int y = H / 2)
    {
        var i = (y * W + x) * 4;
        var actual = (px[i], px[i + 1], px[i + 2]);
        var want = ((byte)Math.Round(expected.R * 255), (byte)Math.Round(expected.G * 255), (byte)Math.Round(expected.B * 255));
        Assert.True(Math.Abs(actual.Item1 - want.Item1) <= 2 && Math.Abs(actual.Item2 - want.Item2) <= 2 && Math.Abs(actual.Item3 - want.Item3) <= 2,
            $"expected rgb{want}, got rgb{actual}");
    }

    public void Dispose()
    {
        _window.MakeCurrent(); // a test that made its own window left that context current
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_tex);
        _window.Dispose();
        if (Directory.Exists(_logDir)) Directory.Delete(_logDir, true);
    }
}
