using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Vibresia.Core;
using Vibresia.Core.Audio;
using Vibresia.Render;

namespace Vibresia.App;

public static partial class Program
{
    // The skeleton shows one upstream vibescape. Kaleidoscope uses only the
    // shared prelude uniforms, so it needs nothing the port doesn't have yet.
    private const string StartVibescape = "kaleidoscope";

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
                log.Error("shader", $"{StartVibescape}: {e.Stage}: {e.DriverLog}");
                ShowError($"The '{StartVibescape}' vibescape failed to compile on this graphics driver.\n\nThe log is in {log.Directory} — please send it.");
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

        using var audio = new AudioPipeline(log);
        var sources = new AudioSources();
        audio.Use(sources.Preferred);

        var host = new VibescapeHost(window, gl, log, StartVibescape, (u, dt) => audio.Tick(u, dt));
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
        var announceUntil = 4.0; // seconds of "listening to …" after a source starts
        var elapsed = 0.0;
        host.Overlay += dt =>
        {
            elapsed += dt;
            var (text, tone) = StatusText(audio, sources, elapsed < announceUntil);
            overlay.Show(text, tone);
            overlay.Render((float)dt);
        };

        foreach (var kb in input.Keyboards)
            kb.KeyDown += (_, key, _) =>
            {
                if (key == Key.Escape) window.Close();
                else if (key == Key.F12) OpenFolder(log);
                else if (key == Key.D)
                {
                    audio.Use(sources.Toggle(audio.SourceName));
                    announceUntil = elapsed + 4.0;
                }
            };

        window.Run();
    }

    private static (string? Text, StatusTone Tone) StatusText(AudioPipeline audio, AudioSources sources, bool announce)
    {
        var other = sources.HasLoopback ? " Press D to switch to the demo tone." : "";
        return audio.Status switch
        {
            AudioStatus.Failed => ($"Can't hear this PC's sound: {audio.FailureMessage}{other}", StatusTone.Error),
            AudioStatus.NothingPlaying when audio.SourceName == sources.Demo.Name =>
                ("The demo tone has gone quiet; press D to restart it.", StatusTone.Error),
            AudioStatus.NothingPlaying => ($"Nothing playing. Play some music, or press D for the demo tone.", StatusTone.Hint),
            _ when announce => ($"Listening to {audio.SourceName}" + (sources.HasLoopback ? " (D switches)" : ""), StatusTone.Info),
            _ => (null, StatusTone.Info),
        };
    }

    private static void OpenFolder(Log log)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{log.Directory}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            log.Warn("ui", $"couldn't open the log folder: {e.Message}");
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
