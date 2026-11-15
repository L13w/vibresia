using Silk.NET.OpenGL;
using StbImageSharp;
using Vibresia.Core;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>
/// The pictures on the View All panel's cards. Built-ins use the Mac's own
/// pictures (the website's site/media/modes, which the Mac app also ships),
/// embedded in this assembly. A custom vibescape is drawn for real, once, at
/// the moment and with the stand-in music the Mac uses (<see cref="StillPicture"/>),
/// and drawn again only if its shader changes; drawing it is also how the
/// panel learns that one needs a fix.
/// </summary>
internal sealed class PreviewPictures : IDisposable
{
    /// <summary>A texture to draw, and whether its rows run bottom-up (a GL render) rather than top-down (a decoded picture).</summary>
    public readonly record struct Picture(uint Texture, int Width, int Height, bool BottomUp);

    private readonly GL _gl;
    private readonly Log? _log;
    private readonly Dictionary<string, Picture?> _builtins = [];
    private readonly Dictionary<VibescapeId, (string Shader, Picture? Picture)> _customs = [];
    private readonly Dictionary<VibescapeId, ShaderProblem> _problems = [];
    private readonly Queue<VibescapeEntry> _pending = new();
    private VibescapeRenderer? _renderer;
    private uint _fbo;

    public PreviewPictures(GL gl, Log? log)
    {
        _gl = gl;
        _log = log;
    }

    /// <summary>The picture for <paramref name="entry"/>, or null while a custom one waits to be drawn (or failed to draw).</summary>
    public Picture? For(VibescapeEntry entry)
    {
        if (!entry.Id.IsCustom)
        {
            if (!_builtins.TryGetValue(entry.Id.Slug, out var picture))
                _builtins[entry.Id.Slug] = picture = LoadBuiltin(entry.Id.Slug);
            return picture;
        }
        if (_customs.TryGetValue(entry.Id, out var drawn) && drawn.Shader == entry.CustomBody) return drawn.Picture;
        if (!_pending.Contains(entry)) _pending.Enqueue(entry);
        return null;
    }

    /// <summary>What is wrong with a custom vibescape, found when its picture was drawn; null if nothing (or not drawn yet).</summary>
    public ShaderProblem? ProblemFor(VibescapeId id) => _problems.GetValueOrDefault(id);

    /// <summary>Whether <paramref name="entry"/> is still waiting for its picture.</summary>
    public bool Waiting(VibescapeEntry entry) => _pending.Contains(entry);

    /// <summary>
    /// Draws one waiting custom picture, so the panel draws between them
    /// rather than stalling on all at once (the Mac yields between them too).
    /// Call outside any other drawing: it binds its own framebuffer and puts
    /// the previous one and the viewport back.
    /// </summary>
    public void DrawNext(Palette palette)
    {
        if (!_pending.TryDequeue(out var entry)) return;
        _renderer ??= new VibescapeRenderer(_gl);
        if (_renderer.Use(entry) is { } problem)
        {
            _problems[entry.Id] = problem;
            Forget(entry.Id);
            _customs[entry.Id] = (entry.CustomBody!, null);
            _log?.Info("panel", $"{entry.Id} needs a fix: {problem.ForScreen().ReplaceLineEndings(" | ")}");
            return;
        }
        _problems.Remove(entry.Id);
        Forget(entry.Id);
        var picture = Render(palette);
        _customs[entry.Id] = (entry.CustomBody!, picture);
    }

    private Picture Render(Palette palette)
    {
        Span<int> viewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, viewport);
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out int previous);
        var texture = NewTexture(StillPicture.Width, StillPicture.Height, null);
        if (_fbo == 0) _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        _renderer!.Palette = palette;
        _renderer.Draw(StillPicture.Uniforms(), 1.0 / 60, StillPicture.Width, StillPicture.Height);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previous);
        _gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
        return new Picture(texture, StillPicture.Width, StillPicture.Height, BottomUp: true);
    }

    private Picture? LoadBuiltin(string slug)
    {
        using var stream = typeof(PreviewPictures).Assembly.GetManifestResourceStream($"previews/{slug}.jpg");
        if (stream is null)
        {
            _log?.Warn("panel", $"no picture for {slug} in this build");
            return null;
        }
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new Picture(NewTexture(image.Width, image.Height, image.Data), image.Width, image.Height, BottomUp: false);
    }

    private unsafe uint NewTexture(int width, int height, byte[]? rgba)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        fixed (byte* p = rgba)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, rgba is null ? null : p);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }

    /// <summary>Drops the pictures and problems of custom vibescapes not in <paramref name="ids"/> (their files were removed).</summary>
    public void KeepOnly(IEnumerable<VibescapeId> ids)
    {
        var keep = ids.ToHashSet();
        foreach (var gone in _customs.Keys.Where(id => !keep.Contains(id)).ToList()) Forget(gone);
        foreach (var gone in _problems.Keys.Where(id => !keep.Contains(id)).ToList()) _problems.Remove(gone);
    }

    private void Forget(VibescapeId id)
    {
        if (_customs.TryGetValue(id, out var old) && old.Picture is { } p) _gl.DeleteTexture(p.Texture);
        _customs.Remove(id);
    }

    public void Dispose()
    {
        foreach (var p in _builtins.Values) if (p is { } b) _gl.DeleteTexture(b.Texture);
        foreach (var (_, p) in _customs.Values) if (p is { } c) _gl.DeleteTexture(c.Texture);
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        _renderer?.Dispose();
    }
}
