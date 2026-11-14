namespace Vibresia.Core.Audio;

/// <summary>
/// What the screen says about audio, decided in one testable place. As on
/// the Mac, silence says nothing (the CRT fading and the picture settling
/// say it) and which source is running is the header's job, so the only
/// words are for a source that failed, and for having no source at all
/// (a remembered microphone, which is never started without being asked for).
/// </summary>
public static class AudioStatusMessage
{
    /// <param name="onDemo">The demo tone is the source that failed or is chosen.</param>
    public static (string? Text, MessageSeverity Severity) For(AudioStatus status, string? failure, bool onDemo) => status switch
    {
        AudioStatus.Failed => ($"Can't hear anything: {failure?.TrimEnd('.')}. Pick another source from the menu at the top right" +
                               (onDemo ? "." : ", or press D for the demo tone."), MessageSeverity.Error),
        AudioStatus.None => ("Choose what Vibresia listens to from the menu at the top right.", MessageSeverity.Hint),
        _ => (null, MessageSeverity.Info),
    };
}

/// <summary>
/// Which source D switches to (a port addition; the Mac has no such key):
/// from the demo tone to this PC's sound, from anything else to the demo
/// tone — and never to system audio on a build that can't capture it.
/// </summary>
public static class SourceToggle
{
    public static AudioChoice Next(AudioChoice current, bool hasLoopback) =>
        current == AudioChoice.Demo && hasLoopback ? AudioChoice.Loopback : AudioChoice.Demo;
}
