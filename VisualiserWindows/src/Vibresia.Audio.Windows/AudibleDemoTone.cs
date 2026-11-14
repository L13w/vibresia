using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vibresia.Core.Audio;

namespace Vibresia.Audio.Windows;

/// <summary>
/// The demo tone played aloud through the default output device, as the Mac
/// plays it through AVAudioEngine. What the speakers get is what the
/// visuals get: each block the device asks for is synthesised, written to the
/// ring buffer, and handed to the device (the Mac taps its mixer the same
/// way). The synth runs at the device's own rate, because shared-mode WASAPI
/// adapts sample formats and channels but not rates. With no output device
/// to play on, it falls back to the silent demo tone and says so in the log,
/// so the demo still works on a PC with no speakers.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AudibleDemoTone : IAudioSource
{
    private readonly MMDeviceEnumerator _devices = new();
    private readonly MMDeviceNotificationClient _notifications;
    private readonly DemoTone _silent = new();
    private WasapiPlayer? _player;
    private MMDevice? _device;
    private bool _stopping;

    public string Name { get; private set; } = "Demo tone";
    public string FormatInfo { get; private set; } = "not started";

    public event Action<string>? Failed;
    public event Action? DeviceChanged;

    public AudibleDemoTone()
    {
        // Raised on a COM thread; the pipeline only sets a flag and acts on the render thread.
        _notifications = _devices.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia) DeviceChanged?.Invoke();
        };
    }

    public void Start(AudioRingBuffer ring)
    {
        Stop();
        _stopping = false;
        MMDevice? device = null;
        WasapiPlayer? player = null;
        try
        {
            device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            WaveFormat mix;
            using (var client = device.CreateAudioClient()) mix = client.MixFormat;
            var provider = new SynthProvider(new DemoSynth(mix.SampleRate), mix.Channels, ring);
            player = new WasapiPlayerBuilder().WithDevice(device).WithSharedMode().WithLatency(100).Build();
            player.Init(provider);
            ring.Reset(mix.SampleRate);
            player.PlaybackStopped += OnStopped;
            player.Play();
            _device = device;
            _player = player;
            Name = $"Demo tone (playing on {device.FriendlyName})";
            FormatInfo = $"synthesised at {mix.SampleRate} Hz, {mix.Channels} ch float to the speakers";
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // No speakers (or a device that refuses the stream): the demo is still worth having, silently.
            if (player is not null) player.PlaybackStopped -= OnStopped;
            player?.Dispose();
            device?.Dispose();
            _player = null;
            _device = null;
            _silent.Start(ring);
            Name = "Demo tone (silent: no speakers to play it on)";
            FormatInfo = $"{_silent.FormatInfo}; playback failed: {e.Message}";
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping) return;
        Failed?.Invoke(e.Exception is { } ex ? $"the demo tone stopped playing: {ex.Message}" : "the demo tone stopped playing");
    }

    public void Stop()
    {
        _stopping = true;
        _silent.Stop();
        if (_player is { } p)
        {
            p.PlaybackStopped -= OnStopped;
            p.Dispose(); // stops, blocking, then releases
            _player = null;
        }
        _device?.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        Stop();
        _silent.Dispose();
        _notifications.Dispose();
        _devices.Dispose();
    }

    /// <summary>Synthesises each block the device asks for, keeping a mono copy for the visuals.</summary>
    private sealed class SynthProvider(DemoSynth synth, int channels, AudioRingBuffer ring) : IWaveProvider
    {
        private float[] _mono = new float[4096];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat((int)synth.SampleRate, channels);

        public int Read(Span<byte> buffer)
        {
            var frames = buffer.Length / (4 * channels);
            if (_mono.Length < frames) _mono = new float[frames];
            var mono = _mono.AsSpan(0, frames);
            synth.Render(mono);
            ring.Write(mono);
            var output = MemoryMarshal.Cast<byte, float>(buffer[..(frames * 4 * channels)]);
            for (var f = 0; f < frames; f++)
                for (var c = 0; c < channels; c++)
                    output[f * channels + c] = mono[f];
            return frames * 4 * channels;
        }
    }
}
