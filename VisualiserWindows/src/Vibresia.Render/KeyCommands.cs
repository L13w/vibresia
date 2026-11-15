using Silk.NET.Input;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Render;

/// <summary>What a key press asks the app to do.</summary>
public enum AppCommand { None, Quit, CloseMenu, OpenLogFolder, Next, Previous, Pick, ToggleDemo, ToggleFullScreen }

/// <summary>
/// The app's keys, decided in one testable place. As on the Mac, the
/// characters in <see cref="VibescapeCatalog.ShortcutKeys"/> pick vibescapes by
/// their place in the switched-on list; they go by the character typed (the
/// Mac's key equivalents do too), so punctuation follows the keyboard layout.
/// F11 switches full screen (Windows' key for what the Mac does with
/// Ctrl+Cmd+F). Port additions, kept from earlier builds: ←/→ step through
/// the list, D switches between this PC's sound and the demo tone, F12 opens
/// the log folder, and Esc quits — except that with a drop-down, the credits
/// or the View All panel open it closes that, as a click outside it does.
/// Keys are watched, not taken: the vibescape sees them too.
/// </summary>
public static class KeyCommands
{
    /// <param name="menuOpen">A drop-down, the credits or the View All panel is open.</param>
    public static AppCommand For(Key key, bool menuOpen) => key switch
    {
        Key.Escape when menuOpen => AppCommand.CloseMenu,
        Key.Escape => AppCommand.Quit,
        Key.F11 => AppCommand.ToggleFullScreen,
        Key.F12 => AppCommand.OpenLogFolder,
        Key.Right => AppCommand.Next,
        Key.Left => AppCommand.Previous,
        Key.D => AppCommand.ToggleDemo,
        _ => AppCommand.None,
    };

    /// <summary>A typed character: <see cref="AppCommand.Pick"/> for a shortcut key, otherwise nothing.</summary>
    public static AppCommand ForCharacter(char typed) =>
        VibescapeCatalog.ShortcutKeys.Contains(typed) ? AppCommand.Pick : AppCommand.None;
}
