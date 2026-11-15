using Vibresia.Core;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>Which header drop-down is open; only one at a time, as on the Mac.</summary>
public enum ChromeMenu { None, Vibescape, Palette, Source, Settings }

/// <summary>The two panels the chrome opens over everything else: the credits popover and the View All panel.</summary>
public enum ChromePanel { None, Credits, ViewAll }

/// <summary>What the chrome shows this frame.</summary>
public sealed record ChromeView
{
    /// <summary>Every vibescape, its order and switches; the drop-down lists the switched-on ones.</summary>
    public required VibescapeCatalog Catalog { get; init; }
    public required VibescapeEntry Current { get; init; }
    public int PaletteIndex { get; init; }
    public AudioChoice ChosenSource { get; init; }
    public SourceState SourceState { get; init; }
    public bool FullScreen { get; init; }
    public bool KeepDisplayAwake { get; init; }
    public bool CrtEffect { get; init; } = true;
    public bool StartAtLogin { get; init; }
    public bool FloatOnTop { get; init; }
    public float[] Bands { get; init; } = new float[FrameUniforms.BandCount];
    public float BeatStrength { get; init; }
    /// <summary>Nothing is listening: the intro is up and the rest of the interface is hidden.</summary>
    public bool IntroShown { get; init; }
    /// <summary>Why the intro came back (a source failed), or null.</summary>
    public string? IntroProblem { get; init; }
}

/// <summary>Where the chrome's choices go. Anything not set does nothing.</summary>
public sealed class ChromeActions
{
    public Action<VibescapeEntry> PickVibescape { get; init; } = _ => { };
    public Action<int> PickPalette { get; init; } = _ => { };
    public Action<AudioChoice> PickSource { get; init; } = _ => { };
    public Action ToggleFullScreen { get; init; } = () => { };
    public Action ToggleKeepDisplayAwake { get; init; } = () => { };
    public Action ToggleCrtEffect { get; init; } = () => { };
    public Action ToggleStartAtLogin { get; init; } = () => { };
    public Action ToggleFloatOnTop { get; init; } = () => { };
    public Action OpenCustomFolder { get; init; } = () => { };
    public Action OpenLogFolder { get; init; } = () => { };
    /// <summary>Opens one of the credits' fixed web addresses in the browser.</summary>
    public Action<string> OpenWebsite { get; init; } = _ => { };
    /// <summary>The View All panel: move a vibescape to a place in the whole list.</summary>
    public Action<VibescapeId, int> MoveVibescape { get; init; } = (_, _) => { };
    /// <summary>The View All panel: switch a vibescape on or off.</summary>
    public Action<VibescapeId, bool> SwitchVibescape { get; init; } = (_, _) => { };
    public Action ResetOrder { get; init; } = () => { };
}
