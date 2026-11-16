using Vibresia.Core.Ui;
using Vibresia.Core;
using Vibresia.Core.Audio;

namespace Vibresia.Tests;

/// <summary>Feeds a signal into the analyser in real-time-sized slices, one per 60 fps frame.</summary>
internal sealed class AnalyserRig
{
    public const double Rate = 48000, Fps = 60;
    public readonly AudioRingBuffer Ring = new(sampleRate: Rate);
    public readonly AudioAnalyzer Analyzer;
    public readonly MotionClocks Clocks = new();
    public int Beats;
    private double _t;

    public AnalyserRig() => Analyzer = new AudioAnalyzer(Ring);

    public void Run(Func<double, float> signal, double seconds, Action? afterFrame = null)
    {
        var perFrame = (int)(Rate / Fps);
        var buf = new float[perFrame];
        for (var f = 0; f < seconds * Fps; f++)
        {
            for (var i = 0; i < perFrame; i++) buf[i] = signal(_t + i / Rate);
            _t += perFrame / Rate;
            Ring.Write(buf);
            Analyzer.Tick();
            if (Analyzer.Beat) Beats++;
            Clocks.Advance(new AudioReading(Analyzer.Rms, Analyzer.Bass, Analyzer.Mid, Analyzer.Treble, Analyzer.Level,
                Analyzer.Beat, Analyzer.BeatStrength, Analyzer.Bpm, SourceIdle: false), 1 / Fps);
            afterFrame?.Invoke();
        }
    }

    public static float Sine(double t, double hz, double amp = 0.3) => (float)(amp * Math.Sin(2 * Math.PI * hz * t));

    /// <summary>A kick drum: a 100 ms decaying 60 Hz burst on every beat, over a quiet pad so the gate stays open.</summary>
    public static float Kicks(double t, double bpm)
    {
        var phase = t % (60 / bpm);
        var kick = phase < 0.1 ? Math.Sin(2 * Math.PI * 60 * phase) * Math.Exp(-phase * 30) * 0.8 : 0;
        return (float)(kick + 0.02 * Math.Sin(2 * Math.PI * 440 * t));
    }
}

public class AudioAnalyzerTests
{
    [Fact]
    public void SilenceStaysStillAndNeverBeats()
    {
        var rig = new AnalyserRig();
        rig.Run(_ => 0f, 5);
        Assert.Equal(0, rig.Beats);
        Assert.Equal(0f, rig.Analyzer.Bass);
        Assert.True(rig.Clocks.Activity < 0.01, $"activity {rig.Clocks.Activity}");
        Assert.Equal(0, rig.Clocks.BeatClock, 6); // nothing moves in silence
    }

    [Fact]
    public void LowToneIsBass()
    {
        var rig = new AnalyserRig();
        rig.Run(t => AnalyserRig.Sine(t, 60), 2);
        var a = rig.Analyzer;
        Assert.True(a.Bass > 3 * a.Mid && a.Bass > 3 * a.Treble, $"bass {a.Bass} mid {a.Mid} treble {a.Treble}");
    }

    [Fact]
    public void HighToneIsTreble()
    {
        var rig = new AnalyserRig();
        rig.Run(t => AnalyserRig.Sine(t, 5000), 2);
        var a = rig.Analyzer;
        Assert.True(a.Treble > 3 * a.Bass && a.Treble > 3 * a.Mid, $"bass {a.Bass} mid {a.Mid} treble {a.Treble}");
    }

    [Fact]
    public void ActivityRisesWithSound()
    {
        var rig = new AnalyserRig();
        rig.Run(t => AnalyserRig.Sine(t, 220), 2);
        Assert.True(rig.Clocks.Activity > 0.95, $"activity {rig.Clocks.Activity}");
    }

    [Fact]
    public void ClickTrackAt120BpmIsTrackedAndBeatsLand()
    {
        var rig = new AnalyserRig();
        rig.Run(t => AnalyserRig.Kicks(t, 120), 10);
        var beatsBefore = rig.Beats;
        rig.Run(t => AnalyserRig.Kicks(t, 120), 10);
        var perSecond = (rig.Beats - beatsBefore) / 10.0;

        Assert.InRange(rig.Analyzer.Bpm, 114, 126);
        Assert.InRange(perSecond, 1.8, 2.2);
        // The beat clock advances at the detected tempo: about 2 beats a second.
        var before = rig.Clocks.BeatClock;
        rig.Run(t => AnalyserRig.Kicks(t, 120), 5);
        Assert.InRange((rig.Clocks.BeatClock - before) / 5, 1.8, 2.2);
    }

    [Fact]
    public void NonFiniteSamplesNeverReachTheUniforms()
    {
        var rig = new AnalyserRig();
        var n = 0;
        rig.Run(t => ++n % 97 == 0 ? float.NaN : n % 89 == 0 ? float.PositiveInfinity : AnalyserRig.Sine(t, 100), 3);
        var a = rig.Analyzer;
        Assert.All(a.Bands.ToArray(), b => Assert.True(float.IsFinite(b) && b is >= 0 and <= 1, $"band {b}"));
        Assert.True(float.IsFinite(a.Bass) && float.IsFinite(a.Level) && float.IsFinite(a.Rms));
        Assert.True(a.Bass > 0, "the real signal around the bad samples still gets through");
    }

    /// <summary>The audible demo tone renders at the speaker's rate; the analyser must still hear 124 bpm.</summary>
    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void DemoKeepsItsTempoAtAnySampleRate(int rate)
    {
        var ring = new AudioRingBuffer(sampleRate: rate);
        var analyzer = new AudioAnalyzer(ring);
        var synth = new DemoSynth(rate);
        var frame = new float[rate / 60];
        for (var f = 0; f < 15 * 60; f++)
        {
            synth.Render(frame);
            ring.Write(frame);
            analyzer.Tick();
        }
        Assert.InRange(analyzer.Bpm, DemoTone.Bpm * 0.95, DemoTone.Bpm * 1.05);
    }

    [Fact]
    public void DemoToneDrivesBeatsAndTempo()
    {
        var rig = new AnalyserRig();
        var synth = new DemoSynth();
        var one = new float[1];
        // DemoSynth is stateful and sample-ordered, so pull it one sample at a time in signal order.
        rig.Run(_ => { synth.Render(one); return one[0]; }, 15);
        Assert.True(rig.Beats > 10, $"beats {rig.Beats}");
        Assert.InRange(rig.Analyzer.Bpm, DemoTone.Bpm * 0.95, DemoTone.Bpm * 1.05);
        Assert.True(rig.Clocks.Activity > 0.95);
    }
}

public class MotionClockTests
{
    [Fact]
    public void ClocksNeverRunBackwardsThroughHiccupsBeatsAndSilence()
    {
        var clocks = new MotionClocks();
        var rng = new Random(7);
        double lastBeat = 0, lastScroll = 0, lastDance = 0, lastWall = 0;
        for (var i = 0; i < 36000; i++)
        {
            var silent = i % 3000 > 2500;
            var snap = new AudioReading(
                Rms: silent ? 0 : (float)rng.NextDouble() * 0.2f, Bass: (float)rng.NextDouble(), Mid: 0.3f, Treble: 0.2f,
                Level: (float)rng.NextDouble(), Beat: rng.Next(20) == 0, BeatStrength: 0.5f,
                Bpm: rng.Next(4) == 0 ? 0 : 60 + rng.Next(140), SourceIdle: i % 7000 > 6900);
            var dt = i % 500 == 0 ? 0.5 : i % 333 == 0 ? 0 : 1 / 60.0 * (0.5 + rng.NextDouble());
            clocks.Advance(snap, dt);
            Assert.True(clocks.BeatClock >= lastBeat, $"beat clock ran back at frame {i}");
            Assert.True(clocks.ScrollClock >= lastScroll && clocks.DanceClock >= lastDance && clocks.WallClock >= lastWall);
            (lastBeat, lastScroll, lastDance, lastWall) = (clocks.BeatClock, clocks.ScrollClock, clocks.DanceClock, clocks.WallClock);
        }
    }

    /// <summary>
    /// Upstream's contract: a detected beat measures the clock's distance from
    /// the nearest whole beat and works 35% of it in smoothly (no step larger
    /// than ±45% of the normal advance). Measured against a control clock fed
    /// the same input without the beat, since the phase also moves on its own.
    /// </summary>
    [Fact]
    public void ABeatPullsTheClockByAThirdOfItsErrorWithoutStepping()
    {
        var withBeat = new MotionClocks();
        var control = new MotionClocks();
        var loud = new AudioReading(0.1f, 0.5f, 0.3f, 0.2f, 0.4f, false, 0, 120, false);
        for (var i = 0; i < 70; i++) { withBeat.Advance(loud, 1 / 60.0); control.Advance(loud, 1 / 60.0); }
        var error = Math.Round(withBeat.BeatClock, MidpointRounding.AwayFromZero) - withBeat.BeatClock;
        Assert.True(Math.Abs(error) > 0.02, $"setup must leave a measurable phase error, got {error}");

        withBeat.Advance(loud with { Beat = true }, 1 / 60.0);
        control.Advance(loud, 1 / 60.0);
        for (var i = 0; i < 60; i++)
        {
            var b = withBeat.BeatClock;
            var c = control.BeatClock;
            withBeat.Advance(loud, 1 / 60.0);
            control.Advance(loud, 1 / 60.0);
            var normal = control.BeatClock - c;
            var step = withBeat.BeatClock - b;
            Assert.InRange(step, normal * 0.55 - 1e-9, normal * 1.45 + 1e-9);
        }
        Assert.Equal(error * 0.35, withBeat.BeatClock - control.BeatClock, 3);
    }
}

public class AudioPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vibresia-audio-" + Guid.NewGuid().ToString("N"));

    /// <summary>A source the test drives by hand: writes what it is told, fails when told.</summary>
    private sealed class FakeSource : IAudioSource
    {
        public AudioRingBuffer? Ring;
        public int Starts;
        public string Name => "fake";
        public string FormatInfo => "fake format";
        public string? LevelInfo => "fake volume 42%";
        public event Action<string>? Failed;
        public event Action? DeviceChanged;
        public void Start(AudioRingBuffer ring) { Ring = ring; ring.Reset(48000); Starts++; }
        public int Stops, Disposals;
        public void Stop() => Stops++;
        public void Dispose() => Disposals++;
        public void Feed(Func<double, float> signal, int frames, Action tick)
        {
            var buf = new float[800];
            for (var f = 0; f < frames; f++)
            {
                for (var i = 0; i < buf.Length; i++) buf[i] = signal(i / 48000.0);
                Ring!.Write(buf);
                tick();
            }
        }
        public void Fail(string m) => Failed?.Invoke(m);
        public void ChangeDevice() => DeviceChanged?.Invoke();
    }

    [Fact]
    public void SilenceBecomesANothingPlayingHintAndClearsWhenSoundReturns()
    {
        using var log = new Log(_dir);
        using var pipe = new AudioPipeline(log);
        var src = new FakeSource();
        var u = new FrameUniforms();
        pipe.Use(src);
        void Tick() => pipe.Tick(u, 1 / 60.0);

        src.Feed(t => AnalyserRig.Sine(t, 200), 60, Tick);
        Assert.Equal(AudioStatus.Playing, pipe.Status);

        // Loopback delivers nothing at all while nothing plays: just tick with no writes.
        for (var i = 0; i < 4 * 60; i++) Tick();
        Assert.Equal(AudioStatus.Playing, pipe.Status); // under the threshold: no hint yet
        for (var i = 0; i < 2 * 60; i++) Tick();
        Assert.Equal(AudioStatus.NothingPlaying, pipe.Status);
        Assert.Equal(0f, u.Idle); // a running source is not "idle": vibescapes settle via uActive
        // And they do settle: no data must not mean "the last window of music, forever".
        Assert.True(u.Active < 0.01f, $"active {u.Active}");
        Assert.True(u.Bass < 0.01f && u.Level < 0.01f, $"bass {u.Bass} level {u.Level}");
        Assert.All(u.Bands, b => Assert.True(b < 0.01f, $"band {b}"));
        var frozen = (u.Time, u.Beats, u.Scroll);
        for (var i = 0; i < 30; i++) Tick();
        Assert.Equal(frozen, (u.Time, u.Beats, u.Scroll)); // stillness in silence

        src.Feed(t => AnalyserRig.Sine(t, 200), 10, Tick);
        Assert.Equal(AudioStatus.Playing, pipe.Status);
        Assert.Contains("nothing audible", Paths.ReadShared(log.FilePath));
    }

    [Fact]
    public void ExactZerosCountAsSilenceToo()
    {
        using var log = new Log(_dir);
        using var pipe = new AudioPipeline(log);
        var src = new FakeSource();
        var u = new FrameUniforms();
        pipe.Use(src);
        src.Feed(_ => 0f, 6 * 60, () => pipe.Tick(u, 1 / 60.0));
        Assert.Equal(AudioStatus.NothingPlaying, pipe.Status);
    }

    [Fact]
    public void StoppingLeavesNoSourceAndRestingVisuals()
    {
        using var log = new Log(_dir);
        using var pipe = new AudioPipeline(log);
        var src = new FakeSource();
        var u = new FrameUniforms();
        pipe.Use(src);
        src.Feed(t => AnalyserRig.Sine(t, 200), 30, () => pipe.Tick(u, 1 / 60.0));
        pipe.Stop();
        Assert.Equal(AudioStatus.None, pipe.Status);
        Assert.Null(pipe.Source);
        Assert.Equal(1, src.Stops);
        for (var i = 0; i < 60; i++) pipe.Tick(u, 1 / 60.0);
        Assert.Equal(1f, u.Idle);
    }

    [Fact]
    public void ACaptureFailureIsShownLoggedAndStillsTheVisuals()
    {
        using var log = new Log(_dir);
        using var pipe = new AudioPipeline(log);
        var src = new FakeSource();
        var u = new FrameUniforms();
        pipe.Use(src);
        src.Feed(t => AnalyserRig.Sine(t, 200), 60, () => pipe.Tick(u, 1 / 60.0));
        Assert.True(u.Active > 0.9f);

        src.Fail("device unplugged");
        for (var i = 0; i < 120; i++) pipe.Tick(u, 1 / 60.0);

        Assert.Equal(AudioStatus.Failed, pipe.Status);
        Assert.Equal("device unplugged", pipe.FailureMessage);
        Assert.Equal(1f, u.Idle);
        Assert.True(u.Active < 0.01f, $"active {u.Active}");
        Assert.Contains("ERROR [audio] device unplugged", Paths.ReadShared(log.FilePath));
    }

    [Fact]
    public void AFailureFromThePreviousSourceDoesNotLandOnTheNextOne()
    {
        using var log = new Log(_dir);
        using var pipe = new AudioPipeline(log);
        var old = new FakeSource();
        var next = new FakeSource();
        var u = new FrameUniforms();
        pipe.Use(old);
        old.Fail("old device gone"); // raised on the audio thread, not yet acted on
        old.ChangeDevice();
        pipe.Use(next);
        next.Feed(t => AnalyserRig.Sine(t, 200), 10, () => pipe.Tick(u, 1 / 60.0));
        Assert.Equal(AudioStatus.Playing, pipe.Status);
        Assert.Null(pipe.FailureMessage);
        Assert.Equal(1, next.Starts);
    }

    [Fact]
    public void StartLogLineCarriesTheSourceFormat()
    {
        using (var log = new Log(_dir))
        using (var pipe = new AudioPipeline(log)) pipe.Use(new FakeSource());
        Assert.Contains("started fake at 48000 Hz; fake format", Paths.ReadShared(Path.Combine(_dir, Log.FileName)));
    }

    /// <summary>The app's AudioSources owns and disposes sources; a pipeline disposing them too disposed loopback twice on exit.</summary>
    [Fact]
    public void DisposingThePipelineStopsTheSourceButLeavesItToItsOwner()
    {
        using var log = new Log(_dir);
        var src = new FakeSource();
        using (var pipe = new AudioPipeline(log)) pipe.Use(src);
        Assert.Equal(1, src.Stops);
        Assert.Equal(0, src.Disposals);
    }

    /// <summary>Once a minute the log says how loud it was and how often the gate held the picture down.</summary>
    [Fact]
    public void LogsAnAudioHealthLineEachMinute()
    {
        using (var log = new Log(_dir))
        using (var pipe = new AudioPipeline(log))
        {
            var src = new FakeSource();
            pipe.Use(src);
            var u = new FrameUniforms();
            src.Feed(t => AnalyserRig.Sine(t, 200), 61 * 60, () => pipe.Tick(u, 1 / 60.0));
        }
        var text = Paths.ReadShared(Path.Combine(_dir, Log.FileName));
        Assert.Matches(@"\[audio\] fake, Playing: 60 s: rms avg 0\.\d+ max 0\.\d+; gate closed \d+% partly \d+%; activity avg", text);
        Assert.Matches(@"\[audio\] fake, Playing: 60 s: .*; fake volume 42%", text); // the volume the sound may have passed through
    }

    [Fact]
    public void DefaultDeviceChangeRestartsCapture()
    {
        using var log = new Log(_dir);
        using var pipe = new AudioPipeline(log);
        var src = new FakeSource();
        pipe.Use(src);
        src.ChangeDevice();
        pipe.Tick(new FrameUniforms(), 1 / 60.0);
        Assert.Equal(2, src.Starts);
        Assert.Equal(AudioStatus.Playing, pipe.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

public class SampleConverterTests
{
    [Fact]
    public void AveragesStereoFloat()
    {
        var bytes = new[] { 0.5f, -0.5f, 1f, 0f }.SelectMany(BitConverter.GetBytes).ToArray();
        var mono = new float[2];
        Assert.Equal(2, SampleConverter.ToMono(bytes, SampleEncoding.Float32, 2, mono));
        Assert.Equal(new[] { 0f, 0.5f }, mono);
    }

    [Fact]
    public void DecodesPcm16And24IncludingNegatives()
    {
        var mono = new float[1];
        SampleConverter.ToMono(BitConverter.GetBytes((short)-16384), SampleEncoding.Pcm16, 1, mono);
        Assert.Equal(-0.5f, mono[0], 4);
        SampleConverter.ToMono(new byte[] { 0x00, 0x00, 0xC0 }, SampleEncoding.Pcm24, 1, mono); // -0x400000
        Assert.Equal(-0.5f, mono[0], 4);
    }

    [Fact]
    public void IgnoresATrailingPartialFrame()
    {
        var mono = new float[4];
        Assert.Equal(1, SampleConverter.ToMono(new byte[6], SampleEncoding.Pcm16, 2, mono));
    }
}

public class RealFftTests
{
    [Fact]
    public void MatchesVdspScaleForAPureTone()
    {
        const int n = 1024, bin = 37;
        var fft = new RealFft(n);
        var x = Enumerable.Range(0, n).Select(i => (float)(0.5 * Math.Cos(2 * Math.PI * bin * i / n))).ToArray();
        var mags = new float[n / 2];
        fft.Magnitudes(x, mags);
        // Textbook |X[k]| = N·A/2; vDSP's real FFT doubles it → N·A.
        Assert.Equal(n * 0.5f, mags[bin], 1);
        Assert.True(mags.Where((_, k) => k != bin).All(m => m < 0.01f));
    }
}

public class IntroScreenTests
{
    /// <summary>As on the Mac: the intro is up whenever nothing is listening, and down while a source runs, sound or not.</summary>
    [Theory]
    [InlineData(AudioStatus.None, true)]
    [InlineData(AudioStatus.Failed, true)]
    [InlineData(AudioStatus.Playing, false)]
    [InlineData(AudioStatus.NothingPlaying, false)]
    public void ShowsOnlyWhenNothingIsListening(AudioStatus status, bool shows) =>
        Assert.Equal(shows, IntroScreen.Shows(status));

    [Fact]
    public void AFailureSaysWhichSourceAndWhyWithOneFullStop()
    {
        Assert.Equal("Couldn't start the microphone: device gone.", IntroScreen.Problem(AudioStatus.Failed, AudioChoice.Microphone, "device gone."));
        Assert.Equal("Couldn't start system audio: x.", IntroScreen.Problem(AudioStatus.Failed, AudioChoice.Loopback, "x"));
        Assert.Equal("Couldn't start the demo tone.", IntroScreen.Problem(AudioStatus.Failed, AudioChoice.Demo, null));
    }

    [Theory]
    [InlineData(AudioStatus.None)]
    [InlineData(AudioStatus.Playing)]
    public void NoFailureNoProblem(AudioStatus status) =>
        Assert.Null(IntroScreen.Problem(status, AudioChoice.Loopback, "stale message"));

    [Fact]
    public void OneButtonPerSourceInTheMacsOrder() =>
        Assert.Equal(SourcePill.Choices, IntroScreen.Buttons.Select(b => b.Choice));

    [Fact]
    public void TheWordsStayInsideTheInterfaceFontsGlyphs() =>
        Assert.All(new[] { IntroScreen.Title, IntroScreen.Subtitle, IntroScreen.Footnote }
                .Concat(IntroScreen.Buttons.SelectMany(b => new[] { b.Label, b.Detail })),
            text => Assert.All(text, ch => Assert.InRange(ch, ' ', '\u017F')));
}

public class SourceToggleTests
{
    [Fact]
    public void SwitchesBetweenSystemAudioAndTheDemo()
    {
        Assert.Equal(AudioChoice.Loopback, SourceToggle.Next(AudioChoice.Demo, hasLoopback: true));
        Assert.Equal(AudioChoice.Demo, SourceToggle.Next(AudioChoice.Loopback, hasLoopback: true));
        Assert.Equal(AudioChoice.Demo, SourceToggle.Next(AudioChoice.Microphone, hasLoopback: true));
    }

    [Fact]
    public void WithoutLoopbackItStaysOnTheDemo()
    {
        Assert.Equal(AudioChoice.Demo, SourceToggle.Next(AudioChoice.Demo, hasLoopback: false));
        Assert.Equal(AudioChoice.Demo, SourceToggle.Next(AudioChoice.Loopback, hasLoopback: false));
    }
}
