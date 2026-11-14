using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Vibresia.Core;

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
    private readonly Action<FrameUniforms, float> _fillUniforms;
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

    public VibescapeHost(IWindow window, GL gl, Log log, string vibescape, Action<FrameUniforms, float> fillUniforms)
    {
        _window = window;
        _gl = gl;
        _log = log;
        _fillUniforms = fillUniforms;

        _renderer = new VibescapeRenderer(gl);
        _renderer.UseBuiltin(vibescape);
        log.Info("vibescape", $"showing {vibescape}");

        gl.Viewport(window.FramebufferSize);
        window.FramebufferResize += OnResize;
        window.Render += OnRender;
        window.Closing += OnClosing;
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
        _fillUniforms(_uniforms, (float)_clock.Elapsed.TotalSeconds);
        _uniforms.ResolutionX = size.X;
        _uniforms.ResolutionY = size.Y;
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _renderer.Draw(_uniforms);
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

    private void OnClosing()
    {
        _renderer?.Dispose();
        _renderer = null;
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
