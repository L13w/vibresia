using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>
/// Runs a vibescape in a window for the window's whole life: draws each
/// frame, logs startup and health, and releases GL objects while the context
/// still exists. Silk.NET destroys the context as its run loop ends, so GL
/// resources freed after <c>Run()</c> returns hit a dead context and crash —
/// they are released on <c>Closing</c> instead.
/// </summary>
public sealed class VibescapeHost
{
    private readonly IWindow _window;
    private readonly GL _gl;
    private readonly Log _log;
    private readonly Action<FrameUniforms, double> _fillUniforms;
    private readonly List<IDisposable> _releaseOnClose = [];
    private readonly FrameUniforms _uniforms = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly FrameStats _stats = new();
    private VibescapeRenderer? _renderer;
    private bool _sampledEarly;

    /// <summary>Seconds after start at which a second frame sample is logged, once things have settled.</summary>
    public const double SettledSampleSeconds = 5;

    public long FramesDrawn { get; private set; }

    /// <summary>The most recent sample of drawn pixels, for the log and for tests.</summary>
    public FrameSample? LastSample { get; private set; }

    /// <summary>Drawn over the vibescape each frame (after the pixel sample, so it never skews it).</summary>
    public event Action<double>? Overlay;

    /// <summary>Why the start vibescape didn't load, when the host fell back; null when it loaded.</summary>
    public (VibescapeEntry Entry, ShaderProblem Problem)? StartupProblem { get; }

    /// <summary>The vibescape drawing now.</summary>
    public VibescapeEntry Current => _renderer?.Current ?? throw new ObjectDisposedException(nameof(VibescapeHost));

    /// <param name="start">The vibescape to start on.</param>
    /// <param name="fallback">Shown instead when <paramref name="start"/> fails; if this fails too the error propagates (a built-in that doesn't compile is a port bug).</param>
    /// <param name="fillUniforms">Fills the frame's uniforms; given the seconds since the last frame.</param>
    public VibescapeHost(IWindow window, GL gl, Log log, VibescapeEntry start, VibescapeEntry fallback,
        Action<FrameUniforms, double> fillUniforms)
    {
        _window = window;
        _gl = gl;
        _log = log;
        _fillUniforms = fillUniforms;

        _renderer = new VibescapeRenderer(gl);
        if (Show(start) is { } problem)
        {
            StartupProblem = (start, problem);
            if (start.Id != fallback.Id)
            {
                log.Warn("vibescape", $"falling back to {fallback.Id}");
                problem = Show(fallback);
            }
            if (problem is not null)
            {
                _renderer.Dispose();
                throw new ShaderCompileException("fragment compile", problem.DriverLog.Length > 0 ? problem.DriverLog : problem.Message);
            }
        }

        gl.Viewport(window.FramebufferSize);
        window.FramebufferResize += OnResize;
        window.Render += OnRender;
        window.Closing += OnClosing;
    }

    /// <summary>
    /// Switches to <paramref name="entry"/>. If it fails to compile, the
    /// previous vibescape keeps drawing; the problem is logged with the
    /// vibescape's id and returned for the screen.
    /// </summary>
    public ShaderProblem? Show(VibescapeEntry entry)
    {
        if (_renderer is null) throw new ObjectDisposedException(nameof(VibescapeHost));
        ShaderProblem? problem;
        try
        {
            problem = _renderer.Use(entry);
        }
        catch (ShaderCompileException e)
        {
            // A built-in that doesn't compile on this driver: a port problem, but not worth losing the app over.
            problem = new ShaderProblem($"The built-in {entry.Title} didn't compile on this graphics driver. Please send the log.", null, e.DriverLog);
        }
        if (problem is null)
            _log.Info("vibescape", $"showing {entry.Id} ({entry.Title})");
        else
            _log.Warn("vibescape", $"{entry.Id} didn't compile; still showing {_renderer.Current?.Id.ToString() ?? "nothing"}: " +
                                   $"{problem.Title}: {problem.Message}" +
                                   (problem.DriverLog.Length > 0 ? $" | driver: {problem.DriverLog.ReplaceLineEndings(" / ").Trim()}" : ""));
        return problem;
    }

    private void OnResize(Vector2D<int> size)
    {
        _gl.Viewport(size);
        _log.Info("window", $"framebuffer resized to {size}");
    }

    private void OnRender(double delta)
    {
        if (_renderer is null) return;
        // Minimised: nothing to see, so spend nothing drawing it.
        if (_window.WindowState == WindowState.Minimized) return;

        var size = _window.FramebufferSize;
        _fillUniforms(_uniforms, delta);
        _uniforms.ResolutionX = size.X;
        _uniforms.ResolutionY = size.Y;
        _renderer.Draw(_uniforms, delta);
        FramesDrawn++;

        // Read back what was actually drawn, before the swap: tells "we drew
        // black" apart from "we drew a picture the screen never showed".
        if (FramesDrawn == 1)
            Sample("first frame");
        else if (!_sampledEarly && _clock.Elapsed.TotalSeconds >= SettledSampleSeconds)
        {
            _sampledEarly = true;
            Sample($"after {SettledSampleSeconds:F0} s");
        }

        Overlay?.Invoke(delta);

        if (_stats.Add(delta) is { } summary)
            _log.Info("health", $"{summary}; working set {Environment.WorkingSet / (1024 * 1024)} MB");
    }

    private void Sample(string when)
    {
        LastSample = FrameSample.Read(_gl, _window.FramebufferSize);
        var error = _gl.GetError();
        _log.Info("frame", $"{when}: {LastSample}; window state {_window.WindowState}, " +
                           $"position {_window.Position}, size {_window.Size}, framebuffer {_window.FramebufferSize}; GL error {error}");
    }

    /// <summary>GL-owning objects (the overlay) to release while the context is still alive.</summary>
    public void ReleaseOnClose(IDisposable resource) => _releaseOnClose.Add(resource);

    private void OnClosing()
    {
        _renderer?.Dispose();
        _renderer = null;
        foreach (var r in _releaseOnClose) r.Dispose();
        _releaseOnClose.Clear();
        _log.Info("window", $"closing after {FramesDrawn} frames");
    }
}

/// <summary>A coarse grid of pixels read back from the frame just drawn.</summary>
public sealed record FrameSample(int Points, int DistinctColours, double MeanLuminance, (byte R, byte G, byte B) Centre)
{
    private const int Grid = 8;

    public bool LooksBlank => DistinctColours <= 1 || MeanLuminance < 1.0;

    public static FrameSample Read(GL gl, Vector2D<int> size)
    {
        var px = new byte[4];
        var colours = new HashSet<(byte, byte, byte)>();
        double lum = 0;
        for (var gy = 0; gy < Grid; gy++)
        for (var gx = 0; gx < Grid; gx++)
        {
            var x = (int)((gx + 0.5) * size.X / Grid);
            var y = (int)((gy + 0.5) * size.Y / Grid);
            gl.ReadPixels<byte>(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, px);
            colours.Add((px[0], px[1], px[2]));
            lum += 0.2126 * px[0] + 0.7152 * px[1] + 0.0722 * px[2];
        }
        gl.ReadPixels<byte>(size.X / 2, size.Y / 2, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, px);
        return new FrameSample(Grid * Grid, colours.Count, lum / (Grid * Grid), (px[0], px[1], px[2]));
    }

    public override string ToString() =>
        $"sampled {Points} points: {DistinctColours} distinct colours, mean luminance {MeanLuminance:F1}/255, centre rgb{Centre}";
}
