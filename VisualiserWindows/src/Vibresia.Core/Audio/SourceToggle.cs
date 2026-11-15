namespace Vibresia.Core.Audio;

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
