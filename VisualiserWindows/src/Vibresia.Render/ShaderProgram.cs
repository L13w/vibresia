using Silk.NET.OpenGL;

namespace Vibresia.Render;

/// <summary>A shader compile or link failure, carrying the driver's own words.</summary>
public sealed class ShaderCompileException(string stage, string driverLog)
    : Exception($"{stage} failed: {driverLog}")
{
    public string Stage { get; } = stage;
    public string DriverLog { get; } = driverLog;
}

/// <summary>A linked vertex + fragment program with cached uniform locations.</summary>
public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _locations = new(StringComparer.Ordinal);
    public uint Handle { get; }

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        var vs = Compile(ShaderType.VertexShader, vertexSource);
        uint fs;
        try { fs = Compile(ShaderType.FragmentShader, fragmentSource); }
        catch (ShaderCompileException) { gl.DeleteShader(vs); throw; }

        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vs);
        gl.AttachShader(Handle, fs);
        gl.BindAttribLocation(Handle, 0, "aPos");
        gl.LinkProgram(Handle);
        gl.DetachShader(Handle, vs);
        gl.DetachShader(Handle, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out var ok);
        if (ok == 0)
        {
            var log = gl.GetProgramInfoLog(Handle);
            gl.DeleteProgram(Handle);
            throw new ShaderCompileException("link", log);
        }
    }

    private uint Compile(ShaderType type, string source)
    {
        var id = _gl.CreateShader(type);
        _gl.ShaderSource(id, source);
        _gl.CompileShader(id);
        _gl.GetShader(id, ShaderParameterName.CompileStatus, out var ok);
        if (ok != 0) return id;
        var log = _gl.GetShaderInfoLog(id);
        _gl.DeleteShader(id);
        throw new ShaderCompileException(type == ShaderType.VertexShader ? "vertex compile" : "fragment compile", log);
    }

    /// <summary>-1 when the shader doesn't use the uniform — setting it is then a no-op, which is how upstream treats unused uniforms.</summary>
    public int Location(string name)
    {
        if (!_locations.TryGetValue(name, out var loc))
            _locations[name] = loc = _gl.GetUniformLocation(Handle, name);
        return loc;
    }

    public void Set(string name, float v) { var l = Location(name); if (l >= 0) _gl.Uniform1(l, v); }
    public void Set(string name, float x, float y) { var l = Location(name); if (l >= 0) _gl.Uniform2(l, x, y); }
    public void Set(string name, int v) { var l = Location(name); if (l >= 0) _gl.Uniform1(l, v); }
    public void Set(string name, ReadOnlySpan<float> values)
    {
        var l = Location(name);
        if (l >= 0) _gl.Uniform1(l, (uint)values.Length, values);
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
