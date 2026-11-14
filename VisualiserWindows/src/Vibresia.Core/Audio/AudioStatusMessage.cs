namespace Vibresia.Core.Audio;

public enum MessageSeverity { Info, Hint, Error }

/// <summary>What the screen says about audio, decided in one testable place.</summary>
public static class AudioStatusMessage
{
    /// <param name="onDemo">The demo tone is the current source.</param>
    /// <param name="canSwitch">There is a second source for D to switch to.</param>
    /// <param name="announce">A source has just started: say which.</param>
    public static (string? Text, MessageSeverity Severity) For(
        AudioStatus status, string? failure, string? sourceName, bool onDemo, bool canSwitch, bool announce) => status switch
    {
        AudioStatus.Failed => ($"Can't hear this PC's sound: {failure}" + (canSwitch ? " Press D for the demo tone." : ""), MessageSeverity.Error),
        AudioStatus.NothingPlaying when onDemo => ("The demo tone has gone quiet. Press D to restart it.", MessageSeverity.Error),
        AudioStatus.NothingPlaying => ("Nothing playing. Play some music" + (canSwitch ? ", or press D for the demo tone." : "."), MessageSeverity.Hint),
        _ when announce && sourceName is not null => ($"Listening to {sourceName}" + (canSwitch ? " (D switches)" : ""), MessageSeverity.Info),
        _ => (null, MessageSeverity.Info),
    };
}

/// <summary>Which source D switches to: between this PC's sound and the demo tone, by identity.</summary>
public static class SourceToggle
{
    public static IAudioSource Next(IAudioSource? current, IAudioSource demo, IAudioSource? loopback) =>
        loopback is not null && ReferenceEquals(current, demo) ? loopback : demo;
}
