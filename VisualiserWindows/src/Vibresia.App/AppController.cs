using System.Diagnostics;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Audio;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.App;

/// <summary>What the controller asks of the operating system; off Windows each does nothing.</summary>
/// <param name="KeepAwake">Asks Windows to keep the display awake, or stops asking.</param>
/// <param name="ReadStartAtLogin">Whether Vibresia opens when the user signs in.</param>
/// <param name="SetStartAtLogin">Makes it open at sign-in, or stops it.</param>
internal sealed record SystemHooks(Action<bool> KeepAwake, Func<bool> ReadStartAtLogin, Action<bool> SetStartAtLogin)
{
    public static SystemHooks None { get; } = new(_ => { }, () => false, _ => { });
}

/// <summary>
/// What the user can ask for, from the header's drop-downs, the intro, the
/// credits, the View All panel or the keys, and what the chrome shows each
/// frame: switching vibescape, palette and audio source, arranging and
/// switching vibescapes, full screen, keep-awake, start at login and floating
/// above other windows, opening folders and the credits' web pages, and
/// remembering the choices that are remembered. Kept out of Program.Run,
/// which only builds the pieces and wires their events.
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
    private readonly SystemHooks _system;
    private readonly SourceSelection _source;
    private readonly ChromeOpenings _openings = new();
    private readonly VibescapeList _list;
    private bool _startAtLogin;

    private VibescapeCatalog Catalog => _list.Catalog;

    /// <summary>The palette in use. Remembered when picked from the drop-down, unlike on the Mac, so a restart keeps the look someone chose.</summary>
    public int PaletteIndex { get; private set; }

    public ChromeActions Actions { get; }

    public AppController(Log log, IWindow window, Settings settings, string customFolder, IReadOnlyList<CustomVibescape> customs,
        AudioSources sources, AudioPipeline audio, VibescapeHost host, MacChrome chrome, WindowMode windowMode,
        int paletteIndex, SystemHooks system)
    {
        _log = log;
        _window = window;
        _settings = settings;
        _customFolder = customFolder;
        _list = new VibescapeList(settings, customs);
        _sources = sources;
        _audio = audio;
        _host = host;
        _chrome = chrome;
        _windowMode = windowMode;
        _system = system;
        _source = new SourceSelection(settings, sources.HasLoopback);
        PaletteIndex = paletteIndex;
        host.Palette = Palette.At(paletteIndex);
        host.CrtEffect = settings.CrtEffect;
        ApplyFloatOnTop();
        Actions = new ChromeActions
        {
            PickVibescape = Switch,
            PickPalette = PickPalette,
            PickSource = c =>
            {
                StartSource(_source.Pick(c));
                Remember();
            },
            ToggleFullScreen = ToggleFullScreen,
            ToggleKeepDisplayAwake = ToggleKeepDisplayAwake,
            ToggleCrtEffect = ToggleCrtEffect,
            ToggleStartAtLogin = () =>
            {
                _system.SetStartAtLogin(!_startAtLogin);
                _startAtLogin = _system.ReadStartAtLogin();
            },
            ToggleFloatOnTop = () =>
            {
                _settings.FloatOnTop = !_settings.FloatOnTop;
                ApplyFloatOnTop();
                _settings.Save(_log);
            },
            OpenCustomFolder = () => OpenFolder(customFolder, log),
            OpenLogFolder = () => OpenFolder(log.Directory, log),
            OpenWebsite = OpenWebsite,
            MoveVibescape = (id, index) => Arranged(_list.Move(id, index)),
            SwitchVibescape = (id, on) => Arranged(_list.Switch(id, on)),
            ResetOrder = () => Arranged(_list.ResetOrder()),
        };
    }

    /// <summary>Starts the remembered source (<see cref="SourceSelection.AtLaunch"/>); with none, the intro asks.</summary>
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
        Arranged(_list.SwitchOnToPick(entry.Id));
        if (_host.Show(entry) is { } problem)
            _chrome.Notice($"{entry.Title} didn't load.\n{problem.ForScreen()}", MessageSeverity.Error, ErrorSeconds);
        else
            Remember();
    }

    private void Arranged(bool changed)
    {
        if (!changed) return;
        _settings.Save(_log);
        _log.Info("vibescape", $"list: {(_settings.Order is null ? "shipped order" : "own order")}, {Catalog.Entries.Count} of {Catalog.All.Count} switched on");
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
        ApplyFloatOnTop();
        _log.Info("window", _windowMode.FullScreen ? $"full screen on {MonitorArea.Containing(_window)?.Size}" : $"windowed at {_window.Size}");
    }

    /// <summary>Float above other apps, suspended in full screen (as the Mac suspends it while full screen).</summary>
    private void ApplyFloatOnTop()
    {
        var top = WindowFloat.Wanted(_settings.FloatOnTop, _windowMode.FullScreen);
        if (_window.TopMost == top) return;
        _window.TopMost = top;
        _log.Info("window", top ? "floating above other windows" : "not floating above other windows");
    }

    private void ToggleKeepDisplayAwake()
    {
        _settings.KeepDisplayAwake = !_settings.KeepDisplayAwake;
        _system.KeepAwake(_settings.KeepDisplayAwake);
        _settings.Save(_log);
    }

    private void ToggleCrtEffect()
    {
        _settings.CrtEffect = !_settings.CrtEffect;
        _host.CrtEffect = _settings.CrtEffect;
        _log.Info("render", _settings.CrtEffect ? "CRT effect on" : "CRT effect off");
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
            case AppCommand.Next: Switch(Catalog.Step(_host.Current.Id, 1)); break;
            case AppCommand.Previous: Switch(Catalog.Step(_host.Current.Id, -1)); break;
            case AppCommand.Pick when Catalog.ForShortcut(shortcut) is { } picked: Switch(picked); break;
            case AppCommand.ToggleDemo:
                StartSource(_source.ToggleDemo());
                Remember();
                break;
        }
    }

    /// <summary>What the chrome shows this frame.</summary>
    /// <param name="u">The frame's uniforms, for the meter.</param>
    public ChromeView View(FrameUniforms u) => new()
    {
        Catalog = Catalog,
        Current = _host.Current,
        PaletteIndex = PaletteIndex,
        ChosenSource = _source.Chosen,
        SourceState = SourcePill.State(_audio.Status, _sources.ChoiceOf(_audio.Source)),
        FullScreen = _windowMode.FullScreen,
        KeepDisplayAwake = _settings.KeepDisplayAwake,
        CrtEffect = _settings.CrtEffect,
        StartAtLogin = _startAtLogin,
        FloatOnTop = _settings.FloatOnTop,
        Bands = u.Bands,
        BeatStrength = u.BeatStrength,
        IntroShown = IntroScreen.Shows(_audio.Status),
        IntroProblem = IntroScreen.Problem(_audio.Status, _source.Chosen, _audio.FailureMessage),
    };

    /// <summary>
    /// After the chrome has drawn: rescans the custom folder as the vibescape
    /// list or the View All panel opens, so a file copied in shows up without
    /// a restart, and reads Start at login back from Windows as the Settings drop-down opens.
    /// </summary>
    public void AfterRender()
    {
        var (rescan, readStartAtLogin) = _openings.Update(_chrome.OpenMenu == ChromeMenu.Vibescape,
            _chrome.OpenPanel == ChromePanel.ViewAll, _chrome.OpenMenu == ChromeMenu.Settings);
        if (rescan) _list.Rescan(CustomVibescapeLoader.LoadAll(_customFolder, _log));
        if (readStartAtLogin) _startAtLogin = _system.ReadStartAtLogin();
    }

    /// <summary>Opens one of the credits' own web addresses; nothing else is ever handed to the browser.</summary>
    private void OpenWebsite(string url)
    {
        if (!CreditsLinks.IsAllowed(url) || !OperatingSystem.IsWindows()) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Warn("ui", $"couldn't open {url}: {e.Message}");
        }
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
