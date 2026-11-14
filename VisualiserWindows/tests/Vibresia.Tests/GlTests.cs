using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.Tests;

/// <summary>
/// Real GL, headless: run under <c>xvfb-run</c> with Mesa's software renderer
/// on the dev VM. Mesa's GLSL compiler is strict, so a shader that compiles
/// here is very likely to compile on Windows drivers too; pixel output is not
/// compared with any GPU's, only checked for "drew something".
/// GLFW is single-threaded, so these share one collection and never run in parallel.
/// </summary>
[Collection("GL")]
[Trait("Category", "GL")]
public sealed class GlTests : IDisposable
{
    private readonly IWindow _window;
    private readonly GL _gl;
    private readonly GlInfo _info;

    public GlTests()
    {
        (_window, _gl, _info) = GlWindow.Create(GlWindow.Options(visible: false, fullscreen: false));
    }

    [Fact]
    public void ContextIsCoreProfileAtLeast33() =>
        Assert.True(GlVersion.MeetsRequirement(_info.Version), _info.ToString());

    public static TheoryData<string> Builtins() => new(ShaderSources.BuiltinOrder);

    [Theory]
    [MemberData(nameof(Builtins))]
    public void EveryBuiltinCompilesAndLinks(string name)
    {
        using var program = new ShaderProgram(_gl, ShaderSources.VertexShader, ShaderSources.BuiltinFragment(name));
        Assert.NotEqual(0u, program.Handle);
    }

    [Fact]
    public void BrokenShaderReportsTheDriverError()
    {
        var e = Assert.Throws<ShaderCompileException>(() =>
            new ShaderProgram(_gl, ShaderSources.VertexShader, ShaderSources.Prelude + "void main() { fragColor = nope; }"));
        Assert.Contains("nope", e.DriverLog);
    }

    [Fact]
    public void KaleidoscopeDrawsAPictureWhenMusicIsPlaying()
    {
        const int w = 160, h = 90;
        var fbo = _gl.GenFramebuffer();
        var tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, tex, 0);
        Assert.Equal(GLEnum.FramebufferComplete, _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
        _gl.Viewport(0, 0, w, h);

        using var renderer = new VibescapeRenderer(_gl);
        Assert.Null(renderer.Use(Catalog.Find(VibescapeCatalog.Default)!));
        var u = new FrameUniforms();
        TestUniforms.Music(u);
        u.ResolutionX = w; u.ResolutionY = h;
        renderer.Draw(u, 1 / 60.0);

        var pixels = new byte[w * h * 4];
        _gl.ReadPixels<byte>(0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        _gl.DeleteFramebuffer(fbo);
        _gl.DeleteTexture(tex);

        var distinct = Enumerable.Range(0, w * h).Select(i => (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2])).Distinct().Count();
        Assert.True(distinct > 50, $"expected a picture, got {distinct} distinct colours");
        Assert.Equal(GLEnum.NoError, _gl.GetError());
    }

    private static readonly VibescapeCatalog Catalog = new([]);

    public void Dispose() => _window.Dispose();
}

[Collection("GL")]
[Trait("Category", "GL")]
public class GlUnavailableTests
{
    /// <summary>Asking for a GL version no driver offers takes the "needs a newer driver" path, not a crash.</summary>
    [Fact]
    public void ImpossibleVersionBecomesAPlainMessage()
    {
        var e = Assert.Throws<GlUnavailableException>(() =>
            GlWindow.Create(GlWindow.Options(visible: false, fullscreen: false, major: 9, minor: 9)));
        Assert.Contains("graphics driver", e.Message);
    }
}

[CollectionDefinition("GL", DisableParallelization = true)]
public class GlCollection;

/// <summary>
/// The app's real lifecycle: create, run the frame loop, close. Closing used to
/// crash because GL objects were freed after Silk.NET had already destroyed the
/// context at the end of Run(); they must be released while the window closes.
/// </summary>
[Collection("GL")]
[Trait("Category", "GL")]
public sealed class HostLifecycleTests : IDisposable
{
    private readonly string _logDir = Path.Combine(Path.GetTempPath(), "vibresia-host-" + Guid.NewGuid().ToString("N"));
    private static readonly VibescapeEntry Kaleidoscope = new VibescapeCatalog([]).Find(VibescapeCatalog.Default)!;

    [Fact]
    public void RunsFramesThenClosesWithoutTouchingADeadContext()
    {
        var (window, gl, _) = GlWindow.Create(GlWindow.Options(visible: true, fullscreen: false));
        using var log = new Log(_logDir);
        var host = new VibescapeHost(window, gl, log, Kaleidoscope, Kaleidoscope, (u, _) => TestUniforms.Music(u));
        window.Render += _ => { if (host.FramesDrawn >= 10) window.Close(); };

        window.Run();
        window.Dispose();

        Assert.True(host.FramesDrawn >= 10);
        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("closing after", text);
        Assert.Contains("[frame] first frame:", text);
        Assert.NotNull(host.LastSample);
        Assert.False(host.LastSample!.LooksBlank, host.LastSample.ToString());
    }

    /// <summary>A requested sample is logged once its time comes, so "drew black" can be told from "screen showed black".</summary>
    [Fact]
    public void RequestedSamplesAreLoggedWhenDue()
    {
        var (window, gl, _) = GlWindow.Create(GlWindow.Options(visible: true, fullscreen: false));
        using var log = new Log(_logDir);
        var host = new VibescapeHost(window, gl, log, Kaleidoscope, Kaleidoscope, (u, _) => TestUniforms.Music(u));
        host.RequestSample("0.2 s after the pointer left", 0.2);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        window.Render += _ => { if (watch.Elapsed.TotalSeconds > 0.6) window.Close(); };
        window.Run();
        window.Dispose();
        Assert.Contains("[frame] 0.2 s after the pointer left: sampled 64 points", File.ReadAllText(log.FilePath));
    }

    [Fact]
    public void ComposedFullScreenOverhangsTheMonitorByOnePixel()
    {
        var (window, _, _) = GlWindow.Create(GlWindow.Options(visible: true, fullscreen: true), composed: true);
        try
        {
            var bounds = MonitorArea.Primary()!.Value;
            Assert.Equal(bounds.Origin, window.Position);
            Assert.Equal(bounds.Size + new Silk.NET.Maths.Vector2D<int>(0, 1), window.Size);
        }
        finally
        {
            window.Dispose();
        }
    }

    [Fact]
    public void FullScreenIsABorderlessWindowCoveringTheMonitor()
    {
        var (window, _, _) = GlWindow.Create(GlWindow.Options(visible: true, fullscreen: true));
        try
        {
            // The whole monitor, not Silk's work area (which excludes the taskbar).
            // Xvfb has no taskbar, so here the two coincide; the owner's PC is where they differ.
            var bounds = MonitorArea.Primary()!.Value;
            Assert.Equal(WindowBorder.Hidden, window.WindowBorder);
            Assert.NotEqual(WindowState.Fullscreen, window.WindowState);
            Assert.Equal(bounds.Origin, window.Position);
            Assert.Equal(bounds.Size, window.Size);
        }
        finally
        {
            window.Dispose();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDir)) Directory.Delete(_logDir, true);
    }
}
