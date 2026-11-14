using Silk.NET.OpenGL;
using Vibresia.Core;

namespace Vibresia.Render;

/// <summary>
/// Draws one vibescape as a full-screen quad, feeding it upstream's uniforms.
/// Owns the quad geometry; the program is swapped when the vibescape changes.
/// </summary>
public sealed class VibescapeRenderer : IDisposable
{
    private static readonly float[] Quad = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];

    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private ShaderProgram? _program;

    public string? Current { get; private set; }

    public unsafe VibescapeRenderer(GL gl)
    {
        _gl = gl;
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, Quad, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.BindVertexArray(0);
    }

    /// <summary>Compiles and switches to a built-in. On failure the previous vibescape keeps drawing and the exception says why.</summary>
    public void UseBuiltin(string name)
    {
        var program = new ShaderProgram(_gl, ShaderSources.VertexShader, ShaderSources.BuiltinFragment(name));
        _program?.Dispose();
        _program = program;
        Current = name;
    }

    public void Draw(FrameUniforms u)
    {
        if (_program is null) return;
        _gl.UseProgram(_program.Handle);
        var p = _program;
        p.Set("uTime", u.Time);
        p.Set("uBass", u.Bass);
        p.Set("uMid", u.Mid);
        p.Set("uTreble", u.Treble);
        p.Set("uBeat", u.Beat);
        p.Set("uBeatStrength", u.BeatStrength);
        p.Set("uLevel", u.Level);
        p.Set("uResolution", u.ResolutionX, u.ResolutionY);
        p.Set("uIdle", u.Idle);
        p.Set("uBands", u.Bands);
        p.Set("uActive", u.Active);
        p.Set("uBeats", u.Beats);
        p.Set("uScroll", u.Scroll);
        p.Set("uClock", u.Clock);
        p.Set("uLevelSmooth", u.LevelSmooth);
        p.Set("uBassSmooth", u.BassSmooth);
        p.Set("uPresence", u.Presence);
        p.Set("uMouse", u.MouseX, u.MouseY);
        p.Set("uMouseDown", u.MouseDown);
        p.Set("uMouseHere", u.MouseHere);
        // Features v1 doesn't have read as "nothing to know": no genre, no
        // track position, no key or MIDI input (GL zero-initialises uniforms,
        // which is exactly those values, so nothing needs setting).

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _program?.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
