using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Audio;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.App;

public static partial class Program
{
    private const double ErrorSeconds = AppController.ErrorSeconds;

    public static int Main(string[] args)
    {
        Log log;
        try
        {
            log = new Log(Log.DefaultDirectory());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError($"Vibresia couldn't create its log folder: {e.Message}");
            return 1;
        }

        using (log)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                log.Error("crash", e.ExceptionObject.ToString() ?? "unknown exception");
                ShowError($"Vibresia hit an unexpected error and has to close.\n\nThe log is in {log.Directory} — please send it.");
            };
            log.Info("startup", $"Vibresia for Windows {AppVersion.Current}; .NET {Environment.Version}; " +
                                $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
            try
            {
                Run(log, args);
                log.Info("startup", "exited normally");
                return 0;
            }
            catch (GlUnavailableException e)
            {
                log.Error("gpu", $"{e.Message} ({e.InnerException?.Message})");
                ShowError(e.Message + $"\n\nThe log is in {log.Directory}");
                return 2;
            }
            catch (ShaderCompileException e)
            {
                log.Error("shader", $"no vibescape could be shown: {e.Stage}: {e.DriverLog}");
                ShowError($"Vibresia's vibescapes failed to compile on this graphics driver.\n\nThe log is in {log.Directory} — please send it.");
                return 3;
            }
        }
    }

    private static void Run(Log log, string[] args)
    {
        var started = Stopwatch.StartNew();
        // The Mac opens in a window, and so does this; --fullscreen starts full screen.
        var fullScreen = args.Contains("--fullscreen");
        // Hidden until it has a picture to show (VibescapeHost shows it), so there is no white flash at a default size and place.
        var (window, gl, info) = GlWindow.Create(GlWindow.Options(visible: false, fullscreen: fullScreen));
        using var _ = window;
        log.Info("gpu", info.ToString());
        log.Info("startup", $"window up after {started.ElapsedMilliseconds} ms; {(fullScreen ? "borderless full screen, 1 px taller than the monitor" : $"windowed at {window.Size}")}; " +
                            $"monitor {window.Monitor?.Name}: full {MonitorArea.Containing(window)?.Size}, work area {window.Monitor?.Bounds.Size}; framebuffer {window.FramebufferSize}");

        var settings = Settings.Load(Settings.DefaultPath(), log);
        var customFolder = CustomVibescapeLoader.DefaultFolder();
        var customs = CustomVibescapeLoader.LoadAll(customFolder, log);
        var catalog = new VibescapeList(settings, customs).Catalog;

        using var sources = new AudioSources();
        using var audio = new AudioPipeline(log);
        var marker = new SessionMarker(SessionMarker.DefaultPath());
        var unclean = marker.Begin(log);
        var fallback = catalog.Find(VibescapeCatalog.Default)!;
        var start = catalog.Find(settings.Vibescape);
        string? startNotice = null;
        if (start is null) log.Warn("vibescape", $"saved vibescape {settings.Vibescape} isn't available; starting on {fallback.Id}");
        else if (unclean && start.Id.IsCustom)
        {
            // It may be the reason the last run died (a shader that hung the GPU).
            log.Warn("vibescape", $"the last run didn't exit cleanly while showing {start.Id}; starting on {fallback.Id}");
            startNotice = $"Vibresia didn't close properly last time, so it isn't reopening {start.Title}.";
            start = null;
            // Saved now, or quitting from Kaleidoscope without switching would reopen it next time.
            settings.Vibescape = fallback.Id;
            settings.Save(log);
        }
        var pointer = new PointerInput();
        VibescapeHost? hostRef = null;
        FrameUniforms? lastUniforms = null;
        var host = new VibescapeHost(window, gl, log, start ?? fallback, fallback, (u, dt) =>
        {
            audio.Tick(u, dt);
            pointer.Fill(u, dt);
            lastUniforms = u;
            if (hostRef is not null) hostRef.SourceLive = audio.Status is AudioStatus.Playing or AudioStatus.NothingPlaying;
        });
        hostRef = host;

        // The last palette picked, so a restart keeps the look someone chose (the Mac starts on NEON every launch). --palette N picks one for this run.
        var paletteIndex = settings.PaletteIndex;
        if (Array.IndexOf(args, "--palette") is var pi and >= 0 && pi + 1 < args.Length && int.TryParse(args[pi + 1], out var asked))
            paletteIndex = asked >= 0 && asked < Palette.All.Count ? asked : paletteIndex;
#if WINDOWS
        using var windows = new WindowsSystem(log);
        if (settings.KeepDisplayAwake) windows.KeepDisplayAwake(true);
        else log.Info("power", "Windows may dim and sleep the display as usual (keep-awake is off)");
        windows.WatchDisplay();
        host.ReduceMotion = WindowsSystem.AnimationsOff();
        host.HealthExtra = () => $"input idle {WindowsSystem.InputIdleSeconds():F0} s";
        log.Info("startup", $"animation effects {(host.ReduceMotion ? "off: no beat flash" : "on")}");
#endif
        var firstFrameLogged = false;
        window.Render += _ =>
        {
            if (firstFrameLogged || host.FramesDrawn == 0) return;
            firstFrameLogged = true;
            log.Info("startup", $"first frame after {started.ElapsedMilliseconds} ms");
        };

        var input = window.CreateInput();
        var chrome = new MacChrome(gl, window, input, host.Scale, log);
        host.ReleaseOnClose(chrome);
        if (startNotice is not null)
            chrome.Notice(startNotice, MessageSeverity.Hint, ErrorSeconds);
        else if (host.StartupProblem is { } failed)
            chrome.Notice($"{failed.Entry.Title} didn't load, so this is {host.Current.Title}.\n{failed.Problem.ForScreen()}", MessageSeverity.Error, ErrorSeconds);
        var windowMode = new WindowMode(window, fullScreen);
#if WINDOWS
        StartAtLogin.FollowThisCopy(log);
        var system = new SystemHooks(windows.KeepDisplayAwake, () => StartAtLogin.IsOn(log), on => StartAtLogin.Set(on, log));
#else
        var system = SystemHooks.None;
#endif
        var app = new AppController(log, window, settings, customFolder, customs, sources, audio, host, chrome, windowMode, paletteIndex, system);
        app.StartAtLaunch();

        var chromeState = new ChromeState();
        host.Overlay += dt =>
        {
            chromeState.Advance(dt, () => WindowPointer.IsInside(window), chrome.AnythingOpen);
            chrome.Render((float)dt, app.View(lastUniforms ?? new FrameUniforms()), chromeState, app.Actions);
            app.AfterRender();
        };

        foreach (var mouse in input.Mice)
        {
            mouse.MouseMove += (_, pos) => pointer.Move(pos.X, pos.Y, window.Size.X, window.Size.Y);
            // A click on the interface (or anywhere while a drop-down is open) is the interface's, not the vibescape's.
            mouse.MouseDown += (m, _) => pointer.Down = !chrome.TakesClick(m.Position);
            mouse.MouseUp += (_, _) => pointer.Down = false;
        }
        foreach (var kb in input.Keyboards)
        {
            // Watched, not taken (as upstream): the app's own keys still work, and the vibescape sees them too.
            kb.KeyChar += (_, c) =>
            {
                pointer.Typed(c);
                app.Execute(KeyCommands.ForCharacter(c), c);
            };
            kb.KeyUp += (_, _, _) => pointer.KeyDown = false;
            kb.KeyDown += (_, key, _) =>
            {
                pointer.KeyDown = true;
                app.Execute(KeyCommands.For(key, chrome.AnythingOpen), '\0');
            };
        }

        window.Run();
        marker.End(log);
    }

    private static void ShowError(string message)
    {
        if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, message, "Vibresia", 0x10 /* MB_ICONERROR */);
        else Console.Error.WriteLine(message);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
