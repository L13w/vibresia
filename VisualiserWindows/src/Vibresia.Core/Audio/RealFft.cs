namespace Vibresia.Core.Audio;

/// <summary>
/// Magnitude spectrum of a real signal, scaled the way Apple's vDSP real FFT
/// (vDSP_fft_zrip) scales it — twice the textbook DFT — because upstream's
/// analysis constants (the gain floor, the tempo tracker's log compression)
/// were tuned against that scale. Radix-2, power-of-two sizes only.
/// </summary>
public sealed class RealFft
{
    private readonly int _n;
    private readonly float[] _re, _im, _cos, _sin;
    private readonly int[] _bitReverse;

    public RealFft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("size must be a power of two", nameof(size));
        _n = size;
        _re = new float[size];
        _im = new float[size];
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            _cos[i] = MathF.Cos(2 * MathF.PI * i / size);
            _sin[i] = -MathF.Sin(2 * MathF.PI * i / size);
        }
        _bitReverse = new int[size];
        var bits = System.Numerics.BitOperations.Log2((uint)size);
        for (var i = 0; i < size; i++)
        {
            int r = 0, x = i;
            for (var b = 0; b < bits; b++) { r = (r << 1) | (x & 1); x >>= 1; }
            _bitReverse[i] = r;
        }
    }

    public int Size => _n;

    /// <summary>
    /// Writes |X[k]| × 2 for k in 0..N/2-1 into <paramref name="magnitudes"/>.
    /// (Bin 0 differs from vDSP's packed DC/Nyquist slot; nothing upstream reads it.)
    /// </summary>
    public void Magnitudes(ReadOnlySpan<float> input, Span<float> magnitudes)
    {
        for (var i = 0; i < _n; i++)
        {
            _re[_bitReverse[i]] = input[i];
            _im[_bitReverse[i]] = 0;
        }
        for (var len = 2; len <= _n; len <<= 1)
        {
            var half = len >> 1;
            var step = _n / len;
            for (var start = 0; start < _n; start += len)
            for (var k = 0; k < half; k++)
            {
                float wr = _cos[k * step], wi = _sin[k * step];
                int a = start + k, b = a + half;
                var tr = _re[b] * wr - _im[b] * wi;
                var ti = _re[b] * wi + _im[b] * wr;
                _re[b] = _re[a] - tr; _im[b] = _im[a] - ti;
                _re[a] += tr; _im[a] += ti;
            }
        }
        for (var k = 0; k < _n / 2; k++)
            magnitudes[k] = 2f * MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
    }

    /// <summary>vDSP_HANN_NORM: 0.8165 × (1 − cos(2πn/N)).</summary>
    public static float[] HannNorm(int n)
    {
        var w = new float[n];
        for (var i = 0; i < n; i++) w[i] = 0.8165f * (1f - MathF.Cos(2 * MathF.PI * i / n));
        return w;
    }
}
