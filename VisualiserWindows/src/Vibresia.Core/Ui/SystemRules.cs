namespace Vibresia.Core.Ui;

/// <summary>The credits' web pages: the only addresses the app ever hands to the browser.</summary>
public static class CreditsLinks
{
    public const string ProductSite = "https://vibresia.com";
    public const string MakerSite = "https://minithlabs.com";
    /// <summary>Whoever made this Windows version.</summary>
    public const string PortSite = "https://inertialnavigation.com";

    /// <summary>A custom vibescape's author or URL is untrusted text and is never opened; only these are.</summary>
    public static bool IsAllowed(string url) => url is ProductSite or MakerSite or PortSite;
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

    /// <summary>
    /// Whether Windows will actually run the entry at sign-in. Turning an app
    /// off in Task Manager's Startup apps (or Settings → Apps → Startup) keeps
    /// its Run entry and records the choice separately, in
    /// Explorer\StartupApproved\Run: a binary value whose first byte is even
    /// when the app is allowed (02) and odd when it was turned off (03),
    /// followed by when that happened. No value there means allowed.
    /// </summary>
    /// <param name="command">The Run entry's command, or null when there is none.</param>
    /// <param name="approval">The StartupApproved value, or null when there is none.</param>
    public static bool IsOn(string? command, byte[]? approval) =>
        IsSet(command) && !(approval is { Length: > 0 } && (approval[0] & 1) == 1);

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
/// Windows as the Settings drop-down opens (it may have changed in Task Manager).
/// </summary>
public sealed class ChromeOpenings
{
    private readonly OpenEdge _list = new(), _panel = new(), _settings = new();

    public (bool Rescan, bool ReadStartAtLogin) Update(bool listOpen, bool panelOpen, bool settingsOpen)
    {
        var list = _list.Update(listOpen);
        var panel = _panel.Update(panelOpen);
        return (list || panel, _settings.Update(settingsOpen));
    }
}
