using Vibresia.Core.Audio;

namespace Vibresia.App;

/// <summary>
/// The sources the D key switches between: this PC's playback (Windows build
/// only) and the demo tone. Created once and reused — sources are restartable —
/// and released on exit, so loopback's device enumerator and notification
/// subscription don't outlive the app whichever source was last in use.
/// </summary>
public sealed class AudioSources : IDisposable
{
    public DemoTone Demo { get; } = new();
    public IAudioSource? Loopback { get; }
    public bool HasLoopback => Loopback is not null;

    public AudioSources()
    {
#if WINDOWS
        Loopback = new Vibresia.Audio.Windows.LoopbackSource();
#endif
    }

    /// <summary>The PC's sound where there is a way to capture it, else the demo tone.</summary>
    public IAudioSource Preferred => Loopback ?? Demo;

    public void Dispose()
    {
        Demo.Dispose();
        Loopback?.Dispose();
    }
}
