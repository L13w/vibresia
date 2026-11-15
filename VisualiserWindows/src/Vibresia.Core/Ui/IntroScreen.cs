using Vibresia.Core.Audio;

namespace Vibresia.Core.Ui;

/// <summary>
/// The Mac's intro (IntroView, AudioEngine.showIntro): "FEED IT SOME SOUND."
/// and a button per audio source, over the dimmed picture. It shows whenever
/// nothing is listening: at launch until a remembered source starts (never
/// the microphone, which waits to be asked for), and again when a source
/// fails. The words are the Mac's, with Windows in place of macOS.
/// </summary>
public static class IntroScreen
{
    public const string Title = "FEED IT SOME SOUND.";
    public const string Subtitle = "Pick any audio source. Vibresia reacts to bass, mids, treble, and beats in real time.";

    /// <summary>
    /// Where the Mac explains its system-audio permission prompt. Windows asks
    /// nothing for system audio, but it can block the microphone for desktop apps.
    /// </summary>
    public const string Footnote =
        "Windows can block the microphone for desktop apps. If it hears nothing, allow it in Settings > Privacy & security > Microphone, then pick it again.";

    /// <summary>The buttons, in the Mac's order; the first is drawn as the main one.</summary>
    public static IReadOnlyList<(AudioChoice Choice, string Label, string Detail)> Buttons { get; } =
    [
        (AudioChoice.Loopback, "Share System Audio", "Spotify, YouTube, games, anything playing on your PC."),
        (AudioChoice.Microphone, "Use Microphone", "Plays whatever the mic hears. Good for ambient testing."),
        (AudioChoice.Demo, "Use Demo Tone", "Generated kick + pad sweep. No permissions needed."),
    ];

    public static bool Shows(AudioStatus status) => status is AudioStatus.Failed or AudioStatus.None;

    /// <summary>
    /// Why the intro came back, or null when nothing failed. The Mac shows
    /// the intro again with no reason (the pill that carries it is hidden
    /// with the header); the port says what went wrong.
    /// </summary>
    public static string? Problem(AudioStatus status, AudioChoice chosen, string? failure)
    {
        if (status != AudioStatus.Failed) return null;
        var what = chosen switch
        {
            AudioChoice.Microphone => "the microphone",
            AudioChoice.Demo => "the demo tone",
            _ => "system audio",
        };
        var why = failure?.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(why) ? $"Couldn't start {what}." : $"Couldn't start {what}: {why}.";
    }
}
