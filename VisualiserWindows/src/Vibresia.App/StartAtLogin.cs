#if WINDOWS
using Microsoft.Win32;
using Vibresia.Core;
using Vibresia.Core.Ui;

namespace Vibresia.App;

/// <summary>
/// "Start at login" (the Mac's StartupManager, which uses SMAppService):
/// Windows' per-user startup entry, HKCU\…\Run, naming an exe. Windows
/// itself is the record, so the switch shows what is actually set, including
/// an app turned off in Task Manager's Startup apps, which Windows records
/// separately (<see cref="StartupEntry.IsOn"/>). Each launch points a
/// switched-on entry at the copy that is running (<see cref="FollowThisCopy"/>),
/// so a new build unzipped to a new folder takes over without the setting
/// being switched off and on, and a deleted old folder mends itself.
/// </summary>
internal static class StartAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Vibresia";

    /// <summary>Whether Windows will open Vibresia at sign-in. A registry that can't be read counts as off, logged.</summary>
    public static bool IsOn(Log log)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return StartupEntry.IsOn(run?.GetValue(ValueName) as string, approved?.GetValue(ValueName) as byte[]);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            log.Warn("startup", $"couldn't read whether Vibresia opens at sign-in: {e.Message}");
            return false;
        }
    }

    private static string Command => StartupEntry.CommandFor(Environment.ProcessPath!);

    /// <summary>
    /// If Start at login is on but names another copy of Vibresia (an older
    /// build, or a folder since deleted), points it at this one: the copy
    /// opened last is the one that opens at sign-in.
    /// </summary>
    public static void FollowThisCopy(Log log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            var command = key?.GetValue(ValueName) as string;
            if (StartupEntry.Repointed(command, Environment.ProcessPath!) is not { } wanted) return;
            key!.SetValue(ValueName, wanted);
            log.Info("startup", $"Start at login now opens this copy ({Environment.ProcessPath}); it named {command}");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            log.Warn("startup", $"couldn't point Start at login at this copy: {e.Message}");
        }
    }

    public static void Set(bool on, Log log)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (on) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            // Either way, forget a "turned off" left by Task Manager: switching it on here is the newer choice.
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            log.Info("startup", on ? $"Vibresia will open when you sign in ({Environment.ProcessPath})" : "Vibresia won't open when you sign in");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            log.Warn("startup", $"couldn't change whether Vibresia opens at sign-in: {e.Message}");
        }
    }
}
#endif
