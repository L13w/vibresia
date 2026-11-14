using System.Diagnostics;

namespace Vibresia.Core.Audio;

/// <summary>
/// A synthetic track — a sweeping pad over a 124 bpm kick, upstream's
/// DemoTone recipe — so the visuals can be checked with nothing playing.
/// Unlike upstream's it is silent: samples go straight into the ring buffer,
/// never to the speakers, so it can be left on without anyone hearing it.
/// </summary>
public sealed class DemoTone : IAudioSource
{
    public const double SampleRate = 48000;
    public const double Bpm = 124;

    private readonly DemoSynth _synth = new();
    private Thread? _thread;
    private volatile bool _running;

    public string Name => "Demo tone";

    public event Action<string>? Failed { add { } remove { } }
    public event Action? DeviceChanged { add { } remove { } }

    public void Start(AudioRingBuffer ring)
    {
        if (_running) return;
        ring.Reset(SampleRate);
        _running = true;
        _thread = new Thread(() => Run(ring)) { IsBackground = true, Name = "Vibresia demo tone" };
        _thread.Start();
    }

    // Writes in real time, 10 ms at a time, so the analyser sees the same
    // flow it would from a device.
    private void Run(AudioRingBuffer ring)
    {
        var clock = Stopwatch.StartNew();
        long produced = 0;
        var chunk = new float[(int)(SampleRate / 100)];
        while (_running)
        {
            var due = (long)(clock.Elapsed.TotalSeconds * SampleRate);
            while (produced + chunk.Length <= due)
            {
                _synth.Render(chunk);
                ring.Write(chunk);
                produced += chunk.Length;
            }
            Thread.Sleep(5);
        }
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(500);
        _thread = null;
    }

    public void Dispose() => Stop();
}

/// <summary>The demo track's sample generator, separate so tests can render it without a thread.</summary>
public sealed class DemoSynth
{
    private double _phasePad, _phaseSub, _kickPhase, _kickEnv, _nextKick, _t;

    public void Render(Span<float> output)
    {
        const double twoPi = 2 * Math.PI, sr = DemoTone.SampleRate, interval = 60.0 / DemoTone.Bpm;
        for (var i = 0; i < output.Length; i++)
        {
            var t = _t + i / sr;
            var modFreq = 110.0 + 220.0 * Math.Sin(t * twoPi * 0.18);
            _phasePad += twoPi * modFreq / sr;
            if (_phasePad > twoPi) _phasePad -= twoPi;
            var pad = Math.Sin(_phasePad) * 0.18;

            if (_nextKick == 0) _nextKick = t + 0.5;
            while (t >= _nextKick && t - _nextKick < interval * 1.5)
            {
                _kickEnv = 1.0;
                _kickPhase = 0;
                _nextKick += interval;
            }
            var pitch = 110.0 * Math.Exp(-_kickPhase * 14.0);
            _phaseSub += twoPi * pitch / sr;
            if (_phaseSub > twoPi) _phaseSub -= twoPi;
            var kick = Math.Sin(_phaseSub) * _kickEnv;
            _kickPhase += 1.0 / sr;
            _kickEnv *= Math.Exp(-_kickPhase * 6.0);
            if (_kickEnv < 0.0001) _kickEnv = 0;

            output[i] = (float)(pad + kick);
        }
        _t += output.Length / sr;
    }
}
