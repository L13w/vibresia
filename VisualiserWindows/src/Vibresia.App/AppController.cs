using System.Diagnostics;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Audio;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.App;

/// <summary>
/// What the user can ask for, from the header's drop-downs or the keys, and
/// what the chrome shows each frame: switching vibescape, palette and audio
/// source, full screen and keep-awake, opening folders, and remembering the
/// choices that are remembered. Kept out of Program.Run, which only builds
/// the pieces and wires their events.
/// </summary>
internal sealed class AppController
{
    /// <summary>How long a vibescape's compile error stays on screen.</summary>
    public const double ErrorSeconds = 10;

    private readonly Log _log;
    private readonly IWindow _window;
    private readonly Settings _settings;
    private readonly string _customFolder;
    private readonly AudioSources _sources;
    private readonly AudioPipeline _audio;
    private readonly VibescapeHost _host;
    private readonly MacChrome _chrome;
    private readonly WindowMode _windowMode;
    private readonly Action<bool> _keepAwake;
    private readonly SourceSelection _source;
    private readonly OpenEdge _listOpening = new();
    private VibescapeCatalog _catalog;

    /// <summary>The palette in use. Remembered when picked from the drop-down, unlike on the Mac (owner's choice).</summary>
    public int PaletteIndex { get; private set; }

    public ChromeActions Actions { get; }

    /// <param name="keepAwake">Asks Windows to keep the display awake, or stops asking.</param>
    public AppController(Log log, IWindow window, Settings settings, string customFolder, VibescapeCatalog catalog,
        AudioSources sources, AudioPipeline audio, VibescapeHost host, MacChrome chrome, WindowMode windowMode,
        int paletteIndex, Action<bool> keepAwake)
    {
        _log = log;
        _window = window;
        _settings = settings;
        _customFolder = customFolder;
        _catalog = catalog;
        _sources = sources;
        _audio = audio;
        _host = host;
        _chrome = chrome;
        _windowMode = windowMode;
        _keepAwake = keepAwake;
        _source = new SourceSelection(settings, sources.HasLoopback);
        PaletteIndex = paletteIndex;
        host.Palette = Palette.At(paletteIndex);
        Actions = new ChromeActions(
            PickVibescape: Switch,
            PickPalette: PickPalette,
            PickSource: c =>
            {
                StartSource(_source.Pick(c));
                Remember();
            },
            ToggleFullScreen: ToggleFullScreen,
            ToggleKeepDisplayAwake: ToggleKeepDisplayAwake,
            OpenCustomFolder: () => OpenFolder(customFolder, log),
            OpenLogFolder: () => OpenFolder(log.Directory, log));
    }

    /// <summary>Starts the remembered source (<see cref="SourceSelection.AtLaunch"/>).</summary>
    public void StartAtLaunch()
    {
        if (_source.AtLaunch() is { } choice) StartSource(choice);
        else _log.Info("audio", "the microphone was last in use; not starting it without being asked");
    }

    private void StartSource(AudioChoice choice)
    {
        if (_sources.For(choice) is { } source) _audio.Use(source);
        else
        {
            _audio.Stop();
            _log.Warn("audio", $"{choice} can't be captured on this build");
        }
    }

    private void Remember()
    {
        _settings.Vibescape = _host.Current.Id;
        _settings.Save(_log);
    }

    private void Switch(VibescapeEntry entry)
    {
        if (_host.Show(entry) is { } problem)
            _chrome.Notice($"{entry.Title} didn't load.\n{problem.ForScreen()}", MessageSeverity.Error, ErrorSeconds);
        else
            Remember();
    }

    private void PickPalette(int index)
    {
        PaletteIndex = index;
        _host.Palette = Palette.At(index);
        _log.Info("palette", $"palette {Palette.At(index).Name}");
        _settings.PaletteIndex = index;
        _settings.Save(_log);
    }

    private void ToggleFullScreen()
    {
        _windowMode.Toggle();
        _log.Info("window", _windowMode.FullScreen ? $"full screen on {MonitorArea.Containing(_window)?.Size}" : $"windowed at {_window.Size}");
    }

    private void ToggleKeepDisplayAwake()
    {
        _settings.KeepDisplayAwake = !_settings.KeepDisplayAwake;
        _keepAwake(_settings.KeepDisplayAwake);
        _settings.Save(_log);
    }

    /// <summary>Carries out what a key asked for (<see cref="KeyCommands"/>).</summary>
    public void Execute(AppCommand command, char shortcut)
    {
        switch (command)
        {
            case AppCommand.Quit: _window.Close(); break;
            case AppCommand.CloseMenu: _chrome.CloseMenu(); break;
            case AppCommand.OpenLogFolder: OpenFolder(_log.Directory, _log); break;
            case AppCommand.ToggleFullScreen: ToggleFullScreen(); break;
            case AppCommand.Next: Switch(_catalog.Step(_host.Current.Id, 1)); break;
            case AppCommand.Previous: Switch(_catalog.Step(_host.Current.Id, -1)); break;
            case AppCommand.Pick when _catalog.ForShortcut(shortcut) is { } picked: Switch(picked); break;
            case AppCommand.ToggleDemo:
                StartSource(_source.ToggleDemo());
                Remember();
                break;
        }
    }

    /// <summary>What the chrome shows this frame.</summary>
    /// <param name="u">The frame's uniforms, for the meter.</param>
    public ChromeView View(FrameUniforms u)
    {
        var (text, severity) = AudioStatusMessage.For(_audio.Status, _audio.FailureMessage, onDemo: _source.Chosen == AudioChoice.Demo);
        return new ChromeView(_catalog.Entries, _host.Current, PaletteIndex,
            _source.Chosen, SourcePill.State(_audio.Status, _sources.ChoiceOf(_audio.Source)),
            _windowMode.FullScreen, _settings.KeepDisplayAwake,
            u.Bands, u.BeatStrength, text, severity);
    }

    /// <summary>After the chrome has drawn: rescans the custom folder as the vibescape list opens, so a file copied in shows up without a restart.</summary>
    public void AfterRender()
    {
        if (_listOpening.Update(_chrome.OpenMenu == ChromeMenu.Vibescape))
            _catalog = new VibescapeCatalog(CustomVibescapeLoader.LoadAll(_customFolder, _log));
    }

    private static void OpenFolder(string folder, Log log)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            log.Warn("ui", $"couldn't open {folder}: {e.Message}");
        }
    }
}
