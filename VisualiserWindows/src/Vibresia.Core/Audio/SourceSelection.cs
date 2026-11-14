namespace Vibresia.Core.Audio;

/// <summary>
/// Which audio source is chosen, kept in the settings so the choice is
/// remembered (the Mac's savedSource): what to start at launch, and what a
/// pick from the source menu or the D key changes it to.
/// </summary>
public sealed class SourceSelection(Settings settings, bool hasLoopback)
{
    /// <summary>The source last chosen; it may not be running (a remembered microphone, or a failure).</summary>
    public AudioChoice Chosen => settings.Audio;

    /// <summary>
    /// The source to start at launch, or null for none. A remembered
    /// microphone isn't started (as on the Mac: it can pick up a room, so it
    /// always takes a deliberate choice), and system audio on a build that
    /// can't capture it becomes the demo tone.
    /// </summary>
    public AudioChoice? AtLaunch()
    {
        if (Chosen == AudioChoice.Microphone) return null;
        if (Chosen == AudioChoice.Loopback && !hasLoopback) settings.Audio = AudioChoice.Demo;
        return Chosen;
    }

    /// <summary>The user chose <paramref name="choice"/> (from the source menu); the caller starts it and saves the settings.</summary>
    public AudioChoice Pick(AudioChoice choice) => settings.Audio = choice;

    /// <summary>The D key: between system audio and the demo tone (<see cref="SourceToggle"/>).</summary>
    public AudioChoice ToggleDemo() => Pick(SourceToggle.Next(Chosen, hasLoopback));
}
