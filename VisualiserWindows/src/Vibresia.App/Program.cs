using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Vibresia.Core;
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
                            $"monitor {window.Monitor?.Name} {window.Monitor?.Bounds.Size}; framebuffer {window.FramebufferSize}");

        var host = new VibescapeHost(window, gl, log, StartVibescape, SyntheticPulse.Fill);
        var firstFrameLogged = false;
        window.Render += _ =>
        {
            if (firstFrameLogged || host.FramesDrawn == 0) return;
            firstFrameLogged = true;
            log.Info("startup", $"first frame after {started.ElapsedMilliseconds} ms");
        };

        var input = window.CreateInput();
        foreach (var kb in input.Keyboards)
            kb.KeyDown += (_, key, _) =>
            {
                if (key == Key.Escape) window.Close();
                else if (key == Key.F12) OpenFolder(log);
            };

        window.Run();
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
