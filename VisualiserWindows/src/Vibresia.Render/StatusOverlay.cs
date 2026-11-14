using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace Vibresia.Render;

/// <summary>How a status message should read.</summary>
public enum StatusTone { Info, Hint, Error }

/// <summary>
/// A small line of text in the bottom-left corner, over the vibescape — for
/// what the user must know (a dead audio source, nothing playing, which source
/// is listening) and nothing else. Scales with the screen so it reads the
/// same on 1080p and 4K.
/// </summary>
public sealed class StatusOverlay : IDisposable
{
    private readonly ImGuiController _imgui;
    private readonly IWindow _window;
    private string? _text;
    private StatusTone _tone;

    public StatusOverlay(GL gl, IWindow window, IInputContext input)
    {
        _window = window;
        _imgui = new ImGuiController(gl, window, input);
        unsafe { ImGui.GetIO().NativePtr->IniFilename = null; } // no imgui.ini written beside the exe
    }

    public void Show(string? text, StatusTone tone = StatusTone.Info)
    {
        _text = text;
        _tone = tone;
    }

    /// <summary>Draw after the vibescape, every frame.</summary>
    public void Render(float dt)
    {
        _imgui.Update(dt);
        if (_text is { } text)
        {
            var io = ImGui.GetIO();
            io.FontGlobalScale = Math.Max(1f, _window.FramebufferSize.Y / 1080f) * 1.4f;
            var margin = 24f * io.FontGlobalScale;
            ImGui.SetNextWindowPos(new Vector2(margin, io.DisplaySize.Y - margin), ImGuiCond.Always, new Vector2(0, 1));
            ImGui.SetNextWindowBgAlpha(0.55f);
            ImGui.Begin("status", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
                                  ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing);
            var colour = _tone switch
            {
                StatusTone.Error => new Vector4(1f, 0.55f, 0.5f, 1f),
                StatusTone.Hint => new Vector4(1f, 0.9f, 0.6f, 1f),
                _ => new Vector4(0.9f, 0.9f, 0.95f, 1f),
            };
            ImGui.TextColored(colour, text);
            ImGui.End();
        }
        _imgui.Render();
    }

    public void Dispose() => _imgui.Dispose();
}
