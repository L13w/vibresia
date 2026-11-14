using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vibresia.Core.Audio;

namespace Vibresia.Audio.Windows;

/// <summary>
/// Captures, through WASAPI, either whatever the PC is playing through its
/// default output device (loopback: the Mac's system audio) or the default
/// microphone. Restartable: the pipeline stops and starts it again when
/// Windows switches the default device (headphones plugged in), which would
/// otherwise leave capture listening to a device nothing goes through.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CaptureSource : IAudioSource
{
    private readonly MMDeviceEnumerator _devices = new();
    private readonly bool _loopback;
    private MMDevice? _device;

    /// <summary>What the PC is playing.</summary>
    public static CaptureSource Loopback() => new(loopback: true);

    /// <summary>The default recording device.</summary>
    public static CaptureSource Microphone() => new(loopback: false);

    private DataFlow Flow => _loopback ? DataFlow.Render : DataFlow.Capture;
    // Recording's "Default Device" is the Console role; playback follows Multimedia.
    private Role DeviceRole => _loopback ? Role.Multimedia : Role.Console;

    /// <summary>
    /// The device's volume slider and mute, logged beside the measured
    /// loudness. For loopback it varies by driver whether capture is taken
    /// before or after this volume (on the owner's PC, before: the level was
    /// the same at 4% and 72%); for a microphone it is the recording level.
    /// </summary>
    public string? LevelInfo
    {
        get
        {
            try
            {
                var v = _device?.AudioEndpointVolume;
                return v is null ? null : $"Windows {(_loopback ? "volume" : "mic level")} {v.MasterVolumeLevelScalar * 100:F0}%{(v.Mute ? ", muted" : "")}";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null; // the device went away; the next start picks up the new one
            }
        }
    }
    private readonly MMDeviceNotificationClient _notifications;
    private WasapiRecorder? _capture;
    private AudioRingBuffer? _ring;
    private SampleEncoding _encoding;
    private int _channels;
    private float[] _mono = new float[4096];
    private bool _stopping;

    public string Name { get; private set; }
    public string FormatInfo { get; private set; } = "not started";

    public event Action<string>? Failed;
    public event Action? DeviceChanged;

    private CaptureSource(bool loopback)
    {
        _loopback = loopback;
        Name = loopback ? "PC sound" : "Microphone";
        // Raised on a COM thread; the pipeline only sets a flag and acts on the render thread.
        _notifications = _devices.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Flow == Flow && e.Role == DeviceRole) DeviceChanged?.Invoke();
        };
    }

    public void Start(AudioRingBuffer ring)
    {
        var device = _devices.GetDefaultAudioEndpoint(Flow, DeviceRole);
        if (!ReferenceEquals(_device, device)) _device?.Dispose();
        _device = device;
        Name = $"{(_loopback ? "PC sound" : "Microphone")} ({device.FriendlyName})";
        // Ask for 32-bit float at the device's own rate and channel count (shared
        // mode converts sample formats, never rates), so the format is known
        // before recording starts rather than read off the recorder afterwards.
        WaveFormat mix;
        using (var client = device.CreateAudioClient()) mix = client.MixFormat;
        var requested = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
        var builder = new WasapiRecorderBuilder().WithDevice(device).WithFormat(requested);
        if (_loopback) builder = builder.WithLoopbackCapture();
        var capture = builder.Build();
        var format = capture.WaveFormat ?? requested;
        _encoding = EncodingOf(format)
            ?? throw new NotSupportedException($"unsupported capture format {format.Encoding} {format.BitsPerSample}-bit");
        _channels = format.Channels;
        FormatInfo = $"device mix {mix.Encoding} {mix.BitsPerSample}-bit {mix.Channels} ch; capturing {_encoding} {format.Channels} ch";
        ring.Reset(format.SampleRate);
        _ring = ring;
        _stopping = false;
        capture.DataAvailable += OnData;
        capture.RecordingStopped += OnStopped;
        _capture = capture;
        capture.StartRecording();
    }

    private static SampleEncoding? EncodingOf(WaveFormat f)
    {
        var isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat
                      || (f is WaveFormatExtensible x && x.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        return (isFloat, f.BitsPerSample) switch
        {
            (true, 32) => SampleEncoding.Float32,
            (false, 16) => SampleEncoding.Pcm16,
            (false, 24) => SampleEncoding.Pcm24,
            (false, 32) => SampleEncoding.Pcm32,
            _ => null,
        };
    }

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var frames = buffer.Length / (SampleConverter.BytesPerSample(_encoding) * _channels);
        if (_mono.Length < frames) _mono = new float[frames];
        // WASAPI's Silent flag means "treat this packet as silence", whatever bytes it holds.
        if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(_mono, 0, frames);
        else SampleConverter.ToMono(buffer, _encoding, _channels, _mono);
        _ring?.Write(_mono.AsSpan(0, frames));
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping) return;
        Failed?.Invoke(e.Exception is { } ex
            ? $"capture from {Name} stopped: {ex.Message}"
            : $"capture from {Name} stopped unexpectedly");
    }

    public void Stop()
    {
        if (_capture is not { } c) return;
        _stopping = true;
        c.DataAvailable -= OnData;
        c.RecordingStopped -= OnStopped;
        c.StopRecording();
        c.Dispose();
        _capture = null;
    }

    public void Dispose()
    {
        Stop();
        _device?.Dispose();
        _notifications.Dispose();
        _devices.Dispose();
    }
}
