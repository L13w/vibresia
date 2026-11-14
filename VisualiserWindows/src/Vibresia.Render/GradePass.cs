using Silk.NET.OpenGL;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>
/// The offscreen scene target and upstream's palette grade
/// (Shaders.swift gradeFS): the vibescape draws into a texture the size of
/// the framebuffer, and a full-screen pass maps it through the palette onto
/// the screen. An ungraded vibescape goes through the same pass with the
/// grade switched off, as upstream does.
/// </summary>
internal sealed class GradePass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly FullScreenQuad _quad;
    private uint _fbo, _tex;
    private int _width, _height;
    private int _previousFramebuffer;

    public GradePass(GL gl, FullScreenQuad quad)
    {
        _gl = gl;
        _quad = quad;
        _program = new ShaderProgram(gl, ShaderSources.GradeVertex, ShaderSources.GradeFragment);
    }

    /// <summary>Binds the scene target (resizing it if the framebuffer changed) and clears it to upstream's backdrop.</summary>
    public void BeginScene(int width, int height)
    {
        // Whatever the caller had bound is where the graded frame goes (the
        // window, or a test's own framebuffer).
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out _previousFramebuffer);
        EnsureTarget(width, height);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.02f, 0.004f, 0.03f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>Draws the scene onto the framebuffer that was bound before <see cref="BeginScene"/>.</summary>
    /// <param name="palette">The grade, or null to pass the scene through unchanged.</param>
    public void Present(int width, int height, Palette? palette)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)_previousFramebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.UseProgram(_program.Handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _tex);
        _program.Set("uTex", 0);
        var pal = palette ?? Palette.Neon;
        _program.Set3("uShadow", pal.Shadow);
        _program.Set3("uHighlight", pal.Highlight);
        _program.Set("uSaturation", palette?.Saturation ?? 1f);
        _program.Set("uDuotone", palette?.Duotone ?? 0f);
        _program.Set("uLift", palette?.Lift ?? 0f);
        _quad.Draw();
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private unsafe void EnsureTarget(int width, int height)
    {
        if (_fbo != 0 && width == _width && height == _height) return;
        DeleteTarget();
        _tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _tex);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _tex, 0);
        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)_previousFramebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"the offscreen scene target is incomplete ({status}) at {width}x{height}");
        _width = width;
        _height = height;
    }

    private void DeleteTarget()
    {
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        if (_tex != 0) _gl.DeleteTexture(_tex);
        _fbo = _tex = 0;
    }

    public void Dispose()
    {
        DeleteTarget();
        _program.Dispose();
    }
}
