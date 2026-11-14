using Silk.NET.OpenGL;

namespace Vibresia.Render;

/// <summary>
/// Two triangles covering clip space, at attribute 0: what every pass draws
/// (the vibescape, the grade, the CRT and the flash). One is made per
/// renderer and shared by its passes.
/// </summary>
internal sealed class FullScreenQuad : IDisposable
{
    private static readonly float[] Corners = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];

    private readonly GL _gl;
    private readonly uint _vao, _vbo;

    public unsafe FullScreenQuad(GL gl)
    {
        _gl = gl;
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, Corners, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.BindVertexArray(0);
    }

    /// <summary>Draws the quad with whatever program and state are bound.</summary>
    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
