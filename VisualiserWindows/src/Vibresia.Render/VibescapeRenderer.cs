using Silk.NET.OpenGL;
using Vibresia.Core;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>
/// Draws one vibescape as a full-screen quad, feeding it upstream's uniforms,
/// into an offscreen target, then grades that frame onto the screen through
/// the palette, as upstream's renderer does. Owns the quad geometry and the
/// grade pass; the vibescape's program is swapped when the vibescape changes,
/// and a vibescape that fails to compile leaves the previous one drawing.
/// </summary>
public sealed class VibescapeRenderer : IDisposable
{
    private static readonly float[] Quad = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];

    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly GradePass _grade;
    private readonly BuiltinState _builtinState = new();
    private readonly float[] _palette = Palette.Neon.UniformValues();
    private ShaderProgram? _program;

    /// <summary>
    /// Prelude uniforms deliberately left at zero, upstream's "nothing to
    /// know" value, because the port has no source for them: genre and track
    /// position need song identification, and MIDI input isn't ported.
    /// </summary>
    public static IReadOnlySet<string> NotFed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "uGenre", "uTrackPos", "uTrackDuration", "uTrackProgress", "uTrackKnown",
        "uMidiNote", "uMidiVelocity", "uMidiNotes", "uMidiCC", "uMidiCCValue",
    };

    /// <summary>Uniform names set on the current program so far; for checking every prelude uniform is fed or in <see cref="NotFed"/>.</summary>
    public IReadOnlySet<string> Fed => _program?.Assigned ?? new HashSet<string>();

    /// <summary>The vibescape drawing now; null until one has compiled.</summary>
    public VibescapeEntry? Current { get; private set; }

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
        _grade = new GradePass(gl);
    }

    /// <summary>
    /// Compiles and switches to <paramref name="entry"/>. A custom
    /// vibescape's problems come back explained, with line numbers in the
    /// user's own code; a built-in's (which would be a port bug) throw.
    /// Either way the previous vibescape keeps drawing.
    /// </summary>
    /// <returns>Null on success, else what is wrong with the custom vibescape.</returns>
    public ShaderProblem? Use(VibescapeEntry entry)
    {
        ShaderProgram program;
        if (entry.CustomBody is not { } body)
            program = new ShaderProgram(_gl, ShaderSources.VertexShader, ShaderSources.BuiltinFragment(entry.Id.Slug));
        else
        {
            if (ShaderCheck.CheckShape(body) is { } shape) return shape;
            try
            {
                program = new ShaderProgram(_gl, ShaderSources.VertexShader, ShaderSources.CustomFragment(body));
            }
            catch (ShaderCompileException e)
            {
                return ShaderCheck.Explain(e.DriverLog, ShaderSources.CustomPreludeLines);
            }
        }
        _program?.Dispose();
        _program = program;
        Current = entry;
        return null;
    }

    /// <summary>Draws the current vibescape to the bound (default) framebuffer, graded.</summary>
    /// <param name="dt">Seconds since the last frame, for the built-ins that keep their own state.</param>
    public void Draw(FrameUniforms u, double dt)
    {
        if (_program is null || Current is null) return;
        var width = (int)u.ResolutionX;
        var height = (int)u.ResolutionY;
        if (width <= 0 || height <= 0) return;

        _grade.BeginScene(width, height);
        DrawScene(u, dt);
        _grade.Present(width, height, Current.Graded ? Palette.Neon : null);
    }

    private void DrawScene(FrameUniforms u, double dt)
    {
        var p = _program!;
        _gl.UseProgram(p.Handle);
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
        p.Set("uKey", u.Key);
        p.Set("uKeyDown", u.KeyDown);
        // Features v1 doesn't have read as "nothing to know" (NotFed): GL
        // zero-initialises uniforms, which is exactly those values.
        p.SetVec3Array("uPalette", _palette);

        if (!Current!.Id.IsCustom)
        {
            _builtinState.Advance(Current.Id.Slug, u, dt);
            p.Set("uPeaks", _builtinState.Peaks);
            p.Set("uEventKind", _builtinState.EventKind);
            p.Set("uEventAge", _builtinState.EventAge);
            p.Set("uSpriteClock", (float)_builtinState.SpriteClock);
            // No user sprites in this port: Hamster Dance draws its own hamsters.
            p.Set("uSpriteKinds", 0);
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _program?.Dispose();
        _grade.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
