using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>How a status message should read.</summary>
public enum StatusTone { Info, Hint, Error }

/// <summary>What the vibescape list shows, and where its choices go.</summary>
/// <param name="CurrentIndex">The row showing now, marked in the list; -1 for none.</param>
public sealed record PickerView(
    IReadOnlyList<VibescapeEntry> Entries, PickerState State, int CurrentIndex,
    Action<int> Choose, Action OpenCustomFolder);

/// <summary>
/// The text drawn over the vibescape, for what the user must know and
/// nothing else: a status line bottom-left (audio), a notice top-left (which
/// vibescape, or why a custom one didn't compile), and the vibescape list
/// when it is open. Scales with the screen so it reads the same on 1080p and 4K.
/// </summary>
public sealed class StatusOverlay : IDisposable
{
    private readonly ImGuiController _imgui;
    private readonly IWindow _window;
    private string? _text;
    private StatusTone _tone;
    private string? _notice;
    private StatusTone _noticeTone;
    private double _noticeLeft;

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

    /// <summary>Shows <paramref name="text"/> top-left for <paramref name="seconds"/>, replacing any notice already there.</summary>
    public void Notice(string text, StatusTone tone, double seconds)
    {
        _notice = text;
        _noticeTone = tone;
        _noticeLeft = seconds;
    }

    /// <summary>Draw after the vibescape, every frame.</summary>
    /// <param name="picker">The vibescape list, or null when it is closed.</param>
    public void Render(float dt, PickerView? picker = null)
    {
        _imgui.Update(dt);
        var io = ImGui.GetIO();
        var scale = Math.Max(1f, _window.FramebufferSize.Y / 1080f) * 1.4f;
        io.FontGlobalScale = scale;
        var margin = 24f * scale;
        const ImGuiWindowFlags passive = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
                                         ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing;

        if (_text is { } text)
        {
            ImGui.SetNextWindowPos(new Vector2(margin, io.DisplaySize.Y - margin), ImGuiCond.Always, new Vector2(0, 1));
            ImGui.SetNextWindowBgAlpha(0.55f);
            ImGui.Begin("status", passive);
            ImGui.TextColored(Colour(_tone), text);
            ImGui.End();
        }

        _noticeLeft -= dt;
        if (_notice is { } notice && _noticeLeft > 0)
        {
            ImGui.SetNextWindowPos(new Vector2(margin, margin), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.65f);
            ImGui.Begin("notice", passive);
            ImGui.PushTextWrapPos(io.DisplaySize.X * 0.6f);
            ImGui.TextColored(Colour(_noticeTone), notice);
            ImGui.PopTextWrapPos();
            ImGui.End();
        }

        if (picker is not null && picker.State.IsOpen) DrawPicker(picker, io, scale);

        _imgui.Render();
    }

    private static void DrawPicker(PickerView view, ImGuiIOPtr io, float scale)
    {
        var size = new Vector2(Math.Min(io.DisplaySize.X * 0.8f, 520f * scale), io.DisplaySize.Y * 0.7f);
        ImGui.SetNextWindowPos(io.DisplaySize / 2, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.85f);
        ImGui.Begin("Vibescapes", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
                                  ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav);
        ImGui.TextDisabled("Up/Down and Enter, or click. Tab or Esc closes.");
        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        ImGui.BeginChild("list", new Vector2(0, -footer), ImGuiChildFlags.None, ImGuiWindowFlags.NoNav);
        var state = view.State;
        for (var i = 0; i < view.Entries.Count; i++)
        {
            var e = view.Entries[i];
            var key = i < VibescapeCatalog.ShortcutKeys.Length ? VibescapeCatalog.ShortcutKeys[i] : ' ';
            var label = (i == view.CurrentIndex ? "> " : "  ") + key + "  " + e.Title +
                        (e.Id.IsCustom ? $"   (custom{(e.Author.Length > 0 ? ", " + e.Author : "")})" : "");
            if (ImGui.Selectable(label + "##" + e.Id, i == state.Selected)) view.Choose(i);
            if (i == state.Selected && state.ScrollToSelected)
            {
                ImGui.SetScrollHereY(0.5f);
                state.ScrollToSelected = false;
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Open custom folder")) view.OpenCustomFolder();
        ImGui.End();
    }

    private static Vector4 Colour(StatusTone tone) => tone switch
    {
        StatusTone.Error => new Vector4(1f, 0.55f, 0.5f, 1f),
        StatusTone.Hint => new Vector4(1f, 0.9f, 0.6f, 1f),
        _ => new Vector4(0.9f, 0.9f, 0.95f, 1f),
    };

    public void Dispose() => _imgui.Dispose();
}
