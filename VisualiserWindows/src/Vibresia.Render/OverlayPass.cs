using Silk.NET.OpenGL;

namespace Vibresia.Render;

/// <summary>
/// The two layers the Mac's SwiftUI view draws over the vibescape
/// (VisualiserMac UI/ContentView.swift), redrawn here as shaders:
/// the CRT overlay — a 1280×720 image of a soft three-band scanline
/// gradient, a radial vignette and a faint pink glow, stretched to the
/// window — and the beat flash, a pink radial tint measured in points
/// (logical pixels) from the centre. Both blend over whatever is bound,
/// premultiplied.
/// </summary>
internal sealed class OverlayPass : IDisposable
{
    private const string Vertex = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        out vec2 vUV;
        void main() { vUV = aPos * 0.5 + 0.5; gl_Position = vec4(aPos, 0.0, 1.0); }
        """;

    // CRTCache.render(): drawn on a 1280x720 canvas (origin bottom-left), then stretched.
    private const string CrtFragment = """
        #version 330 core
        in vec2 vUV;
        uniform float uOpacity;
        out vec4 fragColor;
        void main() {
            vec2 px = vUV * vec2(1280.0, 720.0);
            // 1. scanlines: one linear gradient bottom to top, clear / 10% black / clear / 10% black at 0, .33, .66, 1
            float y = vUV.y, a;
            if (y < 0.33) a = mix(0.0, 0.10, y / 0.33);
            else if (y < 0.66) a = mix(0.10, 0.0, (y - 0.33) / 0.33);
            else a = mix(0.0, 0.10, (y - 0.66) / 0.34);
            // 2. vignette: 0 inside radius 200, 55% black from radius 700 out, drawn over
            float r = distance(px, vec2(640.0, 360.0));
            float v = 0.55 * clamp((r - 200.0) / 500.0, 0.0, 1.0);
            a = v + a * (1.0 - v);
            // 3. glow, screen-blended: pink at 4% inside radius 100, clear at 800 and beyond
            float g = r < 800.0 ? 0.04 * (1.0 - clamp((r - 100.0) / 700.0, 0.0, 1.0)) : 0.0;
            vec3 colour = vec3(1.0, 0.17, 0.84) * g;   // over black, screen leaves the glow's own colour
            a = g + a * (1.0 - g);
            fragColor = vec4(colour, a) * uOpacity;    // premultiplied
        }
        """;

    // BeatFlash: RadialGradient(pink at min(0.4, s*0.6) → clear, startRadius 50, endRadius 600 points).
    private const string FlashFragment = """
        #version 330 core
        uniform vec2 uCentre;       // framebuffer pixels
        uniform float uPointSize;   // framebuffer pixels per point
        uniform float uCentreAlpha;
        uniform float uOpacity;
        out vec4 fragColor;
        void main() {
            float r = distance(gl_FragCoord.xy, uCentre) / uPointSize;
            float a = uCentreAlpha * (1.0 - clamp((r - 50.0) / 550.0, 0.0, 1.0)) * uOpacity;
            fragColor = vec4(vec3(1.0, 0.17, 0.84) * a, a);
        }
        """;

    private readonly GL _gl;
    private readonly ShaderProgram _crt, _flash;
    private readonly FullScreenQuad _quad;

    public OverlayPass(GL gl, FullScreenQuad quad)
    {
        _gl = gl;
        _quad = quad;
        _crt = new ShaderProgram(gl, Vertex, CrtFragment);
        _flash = new ShaderProgram(gl, Vertex, FlashFragment);
    }

    public void DrawCrt(float opacity, int width, int height)
    {
        if (opacity <= 0) return;
        Begin(_crt, width, height);
        _crt.Set("uOpacity", opacity);
        End();
    }

    /// <param name="pointSize">Framebuffer pixels per logical pixel (the display's scaling factor).</param>
    public void DrawFlash(float centreAlpha, float opacity, int width, int height, float pointSize)
    {
        if (opacity <= 0 || centreAlpha <= 0) return;
        Begin(_flash, width, height);
        _flash.Set("uCentre", width / 2f, height / 2f);
        _flash.Set("uPointSize", Math.Max(1f, pointSize));
        _flash.Set("uCentreAlpha", centreAlpha);
        _flash.Set("uOpacity", opacity);
        End();
    }

    private void Begin(ShaderProgram p, int width, int height)
    {
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.UseProgram(p.Handle);
    }

    private void End()
    {
        _quad.Draw();
        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        _crt.Dispose();
        _flash.Dispose();
    }
}
