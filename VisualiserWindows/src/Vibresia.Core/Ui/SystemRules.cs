namespace Vibresia.Core.Ui;

/// <summary>The credits' two web pages: the only addresses the app ever hands to the browser.</summary>
public static class CreditsLinks
{
    public const string ProductSite = "https://vibresia.com";
    public const string MakerSite = "https://minithlabs.com";

    /// <summary>A custom vibescape's author or URL is untrusted text and is never opened; only these two are.</summary>
    public static bool IsAllowed(string url) => url is ProductSite or MakerSite;
}

/// <summary>Float above other apps (the Mac's alwaysOnTop), which the Mac suspends while full screen.</summary>
public static class WindowFloat
{
    public static bool Wanted(bool floatOnTop, bool fullScreen) => floatOnTop && !fullScreen;
}

/// <summary>
/// Start at login's Windows startup entry: the quoted path of the exe. Each
/// launch points a switched-on entry at the running copy, so a new build in
/// a new folder takes over without the setting being switched off and on.
/// </summary>
public static class StartupEntry
{
    public static string CommandFor(string exePath) => $"\"{exePath}\"";

    /// <summary>Whether an entry's command means Start at login is on (anything but blank).</summary>
    public static bool IsSet(string? command) => command is not null && command.Trim().Length > 0;

    /// <summary>The command to write so a switched-on entry opens <paramref name="exePath"/>, or null when it is off or already does.</summary>
    public static string? Repointed(string? command, string exePath)
    {
        if (!IsSet(command)) return null;
        var wanted = CommandFor(exePath);
        return string.Equals(command!.Trim(), wanted, StringComparison.OrdinalIgnoreCase) ? null : wanted;
    }
}

/// <summary>
/// What the app does as the chrome opens things: reads the custom folder
/// again as the vibescape list or the View All panel opens (so a file copied
/// in shows up without a restart), and reads Start at login back from
/// Windows as the credits open (it may have changed in Task Manager).
/// </summary>
public sealed class ChromeOpenings
{
    private readonly OpenEdge _list = new(), _panel = new(), _credits = new();

    public (bool Rescan, bool ReadStartAtLogin) Update(bool listOpen, bool panelOpen, bool creditsOpen)
    {
        var list = _list.Update(listOpen);
        var panel = _panel.Update(panelOpen);
        return (list || panel, _credits.Update(creditsOpen));
    }
}
