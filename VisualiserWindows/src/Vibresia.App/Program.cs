using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Audio;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.App;

public static partial class Program
{
    /// <summary>How long a custom vibescape's compile error stays on screen.</summary>
    private const double ErrorSeconds = 10;
    /// <summary>How long a vibescape's name shows after switching to it.</summary>
    private const double TitleSeconds = 2.5;

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
                Run(log, windowed: args.Contains("--windowed"));
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

    private static void Run(Log log, bool windowed)
    {
        var started = Stopwatch.StartNew();
        var (window, gl, info) = GlWindow.Create(GlWindow.Options(visible: true, fullscreen: !windowed));
        using var _ = window;
        log.Info("gpu", info.ToString());
        log.Info("startup", $"window up after {started.ElapsedMilliseconds} ms; {(windowed ? "windowed" : "borderless full screen")}; " +
                            $"monitor {window.Monitor?.Name}: full {MonitorArea.Primary()?.Size}, work area {window.Monitor?.Bounds.Size}; framebuffer {window.FramebufferSize}");

        var settings = Settings.Load(Settings.DefaultPath(), log);
        var customFolder = CustomVibescapeLoader.DefaultFolder();
        var catalog = new VibescapeCatalog(CustomVibescapeLoader.LoadAll(customFolder, log));

        using var sources = new AudioSources();
        using var audio = new AudioPipeline(log);
        audio.Use(settings.Audio == AudioChoice.Demo || !sources.HasLoopback ? sources.Demo : sources.Preferred);

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
        var host = new VibescapeHost(window, gl, log, start ?? fallback, fallback, (u, dt) =>
        {
            audio.Tick(u, dt);
            pointer.Fill(u, dt);
        });
        var firstFrameLogged = false;
        window.Render += _ =>
        {
            if (firstFrameLogged || host.FramesDrawn == 0) return;
            firstFrameLogged = true;
            log.Info("startup", $"first frame after {started.ElapsedMilliseconds} ms");
        };

        var input = window.CreateInput();
        var overlay = new StatusOverlay(gl, window, input);
        host.ReleaseOnClose(overlay);
        if (startNotice is not null)
            overlay.Notice(startNotice, StatusTone.Hint, ErrorSeconds);
        else if (host.StartupProblem is { } failed)
            overlay.Notice($"{failed.Entry.Title} didn't load, so this is {host.Current.Title}.\n{failed.Problem.ForScreen()}", StatusTone.Error, ErrorSeconds);
        else
            overlay.Notice(host.Current.Title, StatusTone.Info, TitleSeconds);

        void Remember()
        {
            settings.Vibescape = host.Current.Id;
            settings.Audio = ReferenceEquals(audio.Source, sources.Demo) ? AudioChoice.Demo : AudioChoice.Loopback;
            settings.Save(log);
        }

        void Switch(VibescapeEntry entry)
        {
            if (host.Show(entry) is { } problem)
                overlay.Notice($"{entry.Title} didn't load.\n{problem.ForScreen()}", StatusTone.Error, ErrorSeconds);
            else
            {
                overlay.Notice(entry.Title, StatusTone.Info, TitleSeconds);
                Remember();
            }
        }

        var picker = new PickerState();
        void OpenPicker()
        {
            // Rescan, so a file copied into the folder shows up without a restart.
            catalog = new VibescapeCatalog(CustomVibescapeLoader.LoadAll(customFolder, log));
            picker.Open(catalog.Entries.Count, catalog.IndexOf(host.Current.Id));
        }
        void Choose(int index)
        {
            picker.Close();
            if (index >= 0 && index < catalog.Entries.Count) Switch(catalog.Entries[index]);
        }
        var pickerView = () => new PickerView(catalog.Entries, picker, catalog.IndexOf(host.Current.Id), Choose,
            () => OpenFolder(customFolder, log));

        var announceUntil = 4.0; // seconds of "listening to …" after a source starts
        var elapsed = 0.0;
        host.Overlay += dt =>
        {
            elapsed += dt;
            var (text, severity) = AudioStatusMessage.For(audio.Status, audio.FailureMessage, audio.SourceName,
                onDemo: ReferenceEquals(audio.Source, sources.Demo), canSwitch: sources.HasLoopback,
                announce: elapsed < announceUntil);
            overlay.Show(text, severity switch
            {
                MessageSeverity.Error => StatusTone.Error,
                MessageSeverity.Hint => StatusTone.Hint,
                _ => StatusTone.Info,
            });
            overlay.Render((float)dt, picker.IsOpen ? pickerView() : null);
        };

        foreach (var mouse in input.Mice)
        {
            mouse.MouseMove += (_, pos) => pointer.Move(pos.X, pos.Y, window.Size.X, window.Size.Y);
            mouse.MouseDown += (_, _) => pointer.Down = true;
            mouse.MouseUp += (_, _) => pointer.Down = false;
        }
        foreach (var kb in input.Keyboards)
        {
            // Watched, not taken (as upstream): the app's own keys still work, and the vibescape sees them too.
            kb.KeyChar += (_, c) => pointer.Typed(c);
            kb.KeyUp += (_, _, _) => pointer.KeyDown = false;
        }

        foreach (var kb in input.Keyboards)
            kb.KeyDown += (_, key, _) =>
            {
                pointer.KeyDown = true;
                if (picker.IsOpen)
                {
                    switch (key)
                    {
                        case Key.Escape or Key.Tab: picker.Close(); break;
                        case Key.Up: picker.Move(-1); break;
                        case Key.Down: picker.Move(1); break;
                        case Key.Enter or Key.KeypadEnter: Choose(picker.Selected); break;
                    }
                    return;
                }
                switch (key)
                {
                    case Key.Escape: window.Close(); break;
                    case Key.F12: OpenFolder(log.Directory, log); break;
                    case Key.Tab: OpenPicker(); break;
                    case Key.Right: Switch(catalog.Step(host.Current.Id, 1)); break;
                    case Key.Left: Switch(catalog.Step(host.Current.Id, -1)); break;
                    case >= Key.Number0 and <= Key.Number9 when catalog.ForShortcut((char)('0' + (key - Key.Number0))) is { } picked:
                        Switch(picked);
                        break;
                    case Key.D:
                        audio.Use(SourceToggle.Next(audio.Source, sources.Demo, sources.Loopback));
                        announceUntil = elapsed + 4.0;
                        Remember();
                        break;
                }
            };

        window.Run();
        marker.End(log);
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

    private static void ShowError(string message)
    {
        if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, message, "Vibresia", 0x10 /* MB_ICONERROR */);
        else Console.Error.WriteLine(message);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
