namespace Vibresia.Core.Audio;

/// <summary>
/// Mono samples from whichever source is running, written on the audio
/// thread and read on the render thread. A port of upstream's
/// AudioRingBuffer, with a lock (upstream relies on benign races) and with
/// non-finite samples replaced by silence on the way in — one NaN from a
/// driver would otherwise poison the analyser's gain for good.
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly object _gate = new();
    private readonly float[] _storage;
    private readonly int _mask;
    private long _written;
    private double _sampleRate;

    public AudioRingBuffer(double seconds = 2.0, double sampleRate = 48000)
    {
        var cap = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, (int)(seconds * sampleRate * 2)));
        _storage = new float[cap];
        _mask = cap - 1;
        _sampleRate = sampleRate;
    }

    public int Capacity => _storage.Length;

    public double SampleRate
    {
        get { lock (_gate) return _sampleRate; }
    }

    public long TotalWritten
    {
        get { lock (_gate) return _written; }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                _storage[(int)((_written + i) & _mask)] = float.IsFinite(s) ? s : 0f;
            }
            _written += samples.Length;
        }
    }

    /// <summary>The newest <paramref name="dest"/>.Length samples. False until that many have been written.</summary>
    public bool ReadLatest(Span<float> dest)
    {
        lock (_gate)
        {
            if (_written < dest.Length || dest.Length > _storage.Length) return false;
            CopyLocked(_written - dest.Length, dest);
            return true;
        }
    }

    /// <summary>Samples [start, start + dest.Length) in write order, if still held.</summary>
    public bool Copy(long start, Span<float> dest)
    {
        lock (_gate)
        {
            if (start < 0 || start + dest.Length > _written || _written - start > _storage.Length) return false;
            CopyLocked(start, dest);
            return true;
        }
    }

    /// <summary>A new source: forget the old samples and adopt its rate.</summary>
    public void Reset(double sampleRate)
    {
        lock (_gate)
        {
            Array.Clear(_storage);
            _written = 0;
            _sampleRate = sampleRate;
        }
    }

    private void CopyLocked(long start, Span<float> dest)
    {
        for (var i = 0; i < dest.Length; i++)
            dest[i] = _storage[(int)((start + i) & _mask)];
    }
}
