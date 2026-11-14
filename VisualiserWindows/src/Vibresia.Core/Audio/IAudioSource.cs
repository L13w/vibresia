namespace Vibresia.Core.Audio;

/// <summary>
/// Something that writes mono samples into the ring buffer from its own
/// thread: the PC's playback (loopback capture) or the demo tone. Events are
/// raised on the source's thread; the pipeline acts on them on the render thread.
/// </summary>
public interface IAudioSource : IDisposable
{
    /// <summary>For the log and the screen: "Speakers (Realtek) loopback", "Demo tone".</summary>
    string Name { get; }

    /// <summary>For the log: sample format and channels, so a format problem can be told from silence.</summary>
    string FormatInfo { get; }

    /// <summary>
    /// For the log: the output volume setting the captured sound may have
    /// passed through ("Windows volume 35%"), so a quiet signal can be told
    /// apart from a turned-down one. Null when it doesn't apply.
    /// </summary>
    string? LevelInfo => null;

    /// <summary>Resets the ring to the source's sample rate, then starts writing to it.</summary>
    void Start(AudioRingBuffer ring);

    void Stop();

    /// <summary>The source stopped and won't recover by itself (device gone, driver error).</summary>
    event Action<string>? Failed;

    /// <summary>Windows' default playback device changed: the pipeline restarts capture on the new one.</summary>
    event Action? DeviceChanged;
}

/// <summary>How captured bytes encode a sample.</summary>
public enum SampleEncoding { Float32, Pcm16, Pcm24, Pcm32 }

/// <summary>Interleaved device bytes → mono floats, averaging the channels.</summary>
public static class SampleConverter
{
    public static int BytesPerSample(SampleEncoding e) => e switch
    {
        SampleEncoding.Float32 or SampleEncoding.Pcm32 => 4,
        SampleEncoding.Pcm16 => 2,
        SampleEncoding.Pcm24 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    /// <returns>Mono samples written to <paramref name="mono"/> (one per whole frame in <paramref name="bytes"/>).</returns>
    public static int ToMono(ReadOnlySpan<byte> bytes, SampleEncoding encoding, int channels, Span<float> mono)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        var bps = BytesPerSample(encoding);
        var frame = bps * channels;
        var frames = Math.Min(bytes.Length / frame, mono.Length);
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
                sum += Read(bytes.Slice(f * frame + c * bps, bps), encoding);
            mono[f] = sum / channels;
        }
        return frames;
    }

    private static float Read(ReadOnlySpan<byte> b, SampleEncoding e) => e switch
    {
        SampleEncoding.Float32 => BitConverter.ToSingle(b),
        SampleEncoding.Pcm16 => BitConverter.ToInt16(b) / 32768f,
        SampleEncoding.Pcm24 => ((b[0] | (b[1] << 8) | (b[2] << 16)) << 8 >> 8) / 8388608f,
        SampleEncoding.Pcm32 => BitConverter.ToInt32(b) / 2147483648f,
        _ => 0f,
    };
}
