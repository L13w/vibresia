using Vibresia.Core;
using Vibresia.Core.Audio;

namespace Vibresia.App;

/// <summary>
/// The sources the header's source menu offers, in the Mac's order: this
/// PC's playback, the microphone, and the demo tone. On Windows the demo
/// tone plays aloud, as on the Mac; the Linux build (no WASAPI) has only the
/// silent demo tone. Created once and reused — sources are restartable — and
/// released on exit, so the device enumerators and notification
/// subscriptions don't outlive the app whichever source was last in use.
/// </summary>
public sealed class AudioSources : IDisposable
{
    public IAudioSource Demo { get; }
    public IAudioSource? Loopback { get; }
    public IAudioSource? Microphone { get; }
    public bool HasLoopback => Loopback is not null;

    public AudioSources()
    {
#if WINDOWS
        Demo = new Vibresia.Audio.Windows.AudibleDemoTone();
        Loopback = Vibresia.Audio.Windows.CaptureSource.Loopback();
        Microphone = Vibresia.Audio.Windows.CaptureSource.Microphone();
#else
        Demo = new DemoTone();
#endif
    }

    /// <summary>The source for a choice, or null where this build can't capture it.</summary>
    public IAudioSource? For(AudioChoice choice) => choice switch
    {
        AudioChoice.Loopback => Loopback,
        AudioChoice.Microphone => Microphone,
        _ => Demo,
    };

    /// <summary>Which choice a running source is, or null for none.</summary>
    public AudioChoice? ChoiceOf(IAudioSource? source) =>
        source is null ? null
        : ReferenceEquals(source, Demo) ? AudioChoice.Demo
        : ReferenceEquals(source, Microphone) ? AudioChoice.Microphone
        : AudioChoice.Loopback;

    public void Dispose()
    {
        Demo.Dispose();
        Loopback?.Dispose();
        Microphone?.Dispose();
    }
}
