using Silk.NET.Input;

namespace Vibresia.Render;

/// <summary>What a key press asks the app to do.</summary>
public enum AppCommand { None, Quit, CloseMenu, OpenLogFolder, Next, Previous, Pick, ToggleDemo, ToggleFullScreen }

/// <summary>
/// The app's keys, decided in one testable place. As on the Mac, 1–9 and 0
/// pick the first ten vibescapes in list order. F11 switches full screen
/// (Windows' key for what the Mac does with Ctrl+Cmd+F). Port additions,
/// kept from earlier builds: ←/→ step through the list, D switches between
/// this PC's sound and the demo tone, F12 opens the log folder, and Esc
/// quits — except that with a drop-down open it closes the drop-down, as a
/// click outside it does. Keys are watched, not taken: the vibescape sees them too.
/// </summary>
public static class KeyCommands
{
    /// <returns>The command, and for <see cref="AppCommand.Pick"/> the shortcut character.</returns>
    public static (AppCommand Command, char Shortcut) For(Key key, bool menuOpen) => key switch
    {
        Key.Escape when menuOpen => (AppCommand.CloseMenu, '\0'),
        Key.Escape => (AppCommand.Quit, '\0'),
        Key.F11 => (AppCommand.ToggleFullScreen, '\0'),
        Key.F12 => (AppCommand.OpenLogFolder, '\0'),
        Key.Right => (AppCommand.Next, '\0'),
        Key.Left => (AppCommand.Previous, '\0'),
        Key.D => (AppCommand.ToggleDemo, '\0'),
        >= Key.Number0 and <= Key.Number9 => (AppCommand.Pick, (char)('0' + (key - Key.Number0))),
        _ => (AppCommand.None, '\0'),
    };
}
