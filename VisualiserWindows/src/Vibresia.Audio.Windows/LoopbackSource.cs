using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vibresia.Core.Audio;

namespace Vibresia.Audio.Windows;

/// <summary>
/// Captures whatever the PC is playing through its default output device
/// (WASAPI loopback). Restartable: the pipeline stops and starts it again
/// when Windows switches the default device (headphones plugged in), which
/// would otherwise leave capture listening to a device nothing plays through.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LoopbackSource : IAudioSource
{
    private readonly MMDeviceEnumerator _devices = new();
    private readonly MMDeviceNotificationClient _notifications;
    private WasapiRecorder? _capture;
    private AudioRingBuffer? _ring;
    private SampleEncoding _encoding;
    private int _channels;
    private float[] _mono = new float[4096];
    private bool _stopping;

    public string Name { get; private set; } = "PC sound";
    public string FormatInfo { get; private set; } = "not started";

    public event Action<string>? Failed;
    public event Action? DeviceChanged;

    public LoopbackSource()
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
        var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        Name = $"PC sound ({device.FriendlyName})";
        // Ask for 32-bit float at the device's own rate and channel count (shared
        // mode converts sample formats, never rates), so the format is known
        // before recording starts rather than read off the recorder afterwards.
        WaveFormat mix;
        using (var client = device.CreateAudioClient()) mix = client.MixFormat;
        var requested = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
        var capture = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().WithFormat(requested).Build();
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
        _notifications.Dispose();
        _devices.Dispose();
    }
}
