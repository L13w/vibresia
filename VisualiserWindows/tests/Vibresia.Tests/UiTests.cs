using Silk.NET.Input;
using Vibresia.Core;
using Vibresia.Core.Audio;
using Vibresia.Core.Ui;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.Tests;

public class PaletteTests
{
    [Fact]
    public void TheFivePalettesAreUpstreamsInPickerOrder()
    {
        Assert.Equal(["NEON", "EMBER", "TOXIC", "ABYSS", "MONO"], Palette.All.Select(p => p.Name));
        Assert.Same(Palette.Neon, Palette.All[0]);
        // Spot-checked against Palette.swift.
        var abyss = Palette.All[3];
        Assert.Equal((0.00f, 0.02f, 0.11f), abyss.Shadow);
        Assert.Equal((0.56f, 0.86f, 1.00f), abyss.Highlight);
        Assert.Equal((1.05f, 0.78f, 0.32f), (abyss.Saturation, abyss.Duotone, abyss.Lift));
        Assert.Equal((0f, 0f, 0.08f), (Palette.All[4].Saturation, Palette.All[4].Duotone, Palette.All[4].Lift));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void AnIndexOutOfRangeIsNeonAsUpstream(int index) => Assert.Same(Palette.Neon, Palette.At(index));
}

public class LayoutTierTests
{
    [Theory]
    [InlineData(1400, LayoutTier.Wide, true, true)]
    [InlineData(1100, LayoutTier.Wide, true, true)]
    [InlineData(1099, LayoutTier.Compact, true, true)]
    [InlineData(880, LayoutTier.Compact, true, true)]
    [InlineData(879, LayoutTier.Narrow, false, true)]
    [InlineData(620, LayoutTier.Narrow, false, true)]
    [InlineData(619, LayoutTier.Tiny, false, false)]
    public void TiersFollowUpstreamsWidths(double points, LayoutTier tier, bool text, bool pills)
    {
        Assert.Equal(tier, Layout.ForWidth(points));
        Assert.Equal(text, tier.ShowsPillText());
        Assert.Equal(pills, tier.ShowsPills());
    }
}

public class ChromeStateTests
{
    private const double Frame = 1 / 60.0;

    private static void Run(ChromeState c, double seconds, bool inside, bool menu = false)
    {
        for (var t = 0.0; t < seconds; t += Frame) c.Advance(Frame, () => inside, menu);
    }

    [Fact]
    public void StartsShownAndHidesHalfASecondAfterThePointerLeaves()
    {
        var c = new ChromeState();
        Assert.True(c.Visible);
        Assert.Equal(1f, c.Shown);
        Run(c, 0.45, inside: false);
        Assert.True(c.PointerInside); // not yet: away counts from the first poll that found it gone
        Run(c, 0.4, inside: false);
        Assert.False(c.PointerInside);
        Assert.False(c.Visible);
        Assert.True(c.Shown is > 0 and < 1, $"mid-slide {c.Shown}");
        Run(c, ChromeState.SlideSeconds, inside: false);
        Assert.Equal(0f, c.Shown);
    }

    [Fact]
    public void AnOpenDropDownKeepsItUp()
    {
        var c = new ChromeState();
        Run(c, 2, inside: false, menu: true);
        Assert.False(c.PointerInside);
        Assert.True(c.Visible);
        Assert.Equal(1f, c.Shown);
    }

    [Fact]
    public void ComingBackShowsItAtTheNextPollAndEasesIn()
    {
        var c = new ChromeState();
        Run(c, 2, inside: false);
        Run(c, ChromeState.PollSeconds + Frame, inside: true);
        Assert.True(c.Visible);
        Run(c, ChromeState.SlideSeconds, inside: true);
        Assert.Equal(1f, c.Shown);
    }

    [Fact]
    public void ABriefExitDoesNotHideIt()
    {
        var c = new ChromeState();
        Run(c, 0.3, inside: false);
        Run(c, 0.2, inside: true);
        Run(c, 0.3, inside: false);
        Assert.True(c.Visible);
    }
}

public class MeterBarsTests
{
    [Fact]
    public void EachBarIsTheLouderOfItsBandAndTheNextScaledAndCapped()
    {
        var bands = new float[FrameUniforms.BandCount]; // 96 bands → one band per step
        bands[10] = 0.5f;
        bands[40] = 0.95f;
        var h = MeterBars.Heights(bands);
        Assert.Equal(MeterBars.Count, h.Length);
        Assert.Equal(0.55f, h[10], 4);
        Assert.Equal(0.55f, h[9], 4);  // its neighbour above reaches into it
        Assert.Equal(0f, h[11]);
        Assert.Equal(1f, h[40]);       // 0.95 × 1.1 is capped at the panel
        Assert.All(MeterBars.Heights([]), v => Assert.Equal(0f, v));
    }

    [Fact]
    public void ThirdsAndTheBeatTint()
    {
        Assert.Equal(0, MeterBars.Third(0));
        Assert.Equal(0, MeterBars.Third(20));
        Assert.Equal(1, MeterBars.Third(21));
        Assert.Equal(1, MeterBars.Third(41));
        Assert.Equal(2, MeterBars.Third(42));
        Assert.Equal(0f, MeterBars.BeatTint(0.05f));
        Assert.Equal(0.18f, MeterBars.BeatTint(1f), 4);
    }
}

public class SourcePillTests
{
    [Theory]
    [InlineData(AudioStatus.Playing, AudioChoice.Loopback, SourceState.Live, "SYSTEM AUDIO")]
    [InlineData(AudioStatus.NothingPlaying, AudioChoice.Loopback, SourceState.Live, "SYSTEM AUDIO")]
    [InlineData(AudioStatus.Playing, AudioChoice.Microphone, SourceState.Live, "MICROPHONE")]
    [InlineData(AudioStatus.Playing, AudioChoice.Demo, SourceState.Demo, "DEMO TONE")]
    [InlineData(AudioStatus.Failed, AudioChoice.Loopback, SourceState.Error, "SYSTEM AUDIO FAILED")]
    [InlineData(AudioStatus.Failed, AudioChoice.Microphone, SourceState.Error, "MIC FAILED")]
    public void StateAndTitleFollowWhatIsFlowing(AudioStatus status, AudioChoice running, SourceState state, string title)
    {
        Assert.Equal(state, SourcePill.State(status, running));
        Assert.Equal(title, SourcePill.Title(state, running));
    }

    [Fact]
    public void ARememberedMicrophoneThatWasNotStartedIsIdle()
    {
        var state = SourcePill.State(AudioStatus.None, null);
        Assert.Equal(SourceState.Idle, state);
        Assert.Equal("IDLE", SourcePill.Title(state, AudioChoice.Microphone));
    }

    [Fact]
    public void TheMenuIsUpstreamsInOrder()
    {
        Assert.Equal([AudioChoice.Loopback, AudioChoice.Microphone, AudioChoice.Demo], SourcePill.Choices);
        Assert.Equal(["Share System Audio", "Use Microphone", "Use Demo Tone"], SourcePill.Choices.Select(SourcePill.MenuTitle));
        Assert.Equal("Spotify, YouTube, anything playing", SourcePill.Subtitle(AudioChoice.Loopback));
    }
}

public class KeyCommandsTests
{
    [Theory]
    [InlineData(Key.Escape, false, AppCommand.Quit)]
    [InlineData(Key.Escape, true, AppCommand.CloseMenu)]
    [InlineData(Key.F11, false, AppCommand.ToggleFullScreen)]
    [InlineData(Key.F12, false, AppCommand.OpenLogFolder)]
    [InlineData(Key.Right, false, AppCommand.Next)]
    [InlineData(Key.Left, true, AppCommand.Previous)]
    [InlineData(Key.D, false, AppCommand.ToggleDemo)]
    [InlineData(Key.Tab, false, AppCommand.None)]
    [InlineData(Key.A, false, AppCommand.None)]
    public void KeysMapToCommands(Key key, bool menuOpen, AppCommand expected) =>
        Assert.Equal(expected, KeyCommands.For(key, menuOpen));

    /// <summary>Shortcuts go by the character typed, as the Mac's key equivalents do, so the number row and punctuation both pick.</summary>
    [Theory]
    [InlineData('1', AppCommand.Pick)]
    [InlineData('0', AppCommand.Pick)]
    [InlineData('-', AppCommand.Pick)]
    [InlineData('\\', AppCommand.Pick)]
    [InlineData(')', AppCommand.Pick)]
    [InlineData('d', AppCommand.None)]
    [InlineData('a', AppCommand.None)]
    public void ShortcutCharactersPick(char typed, AppCommand expected) =>
        Assert.Equal(expected, KeyCommands.ForCharacter(typed));

    [Fact]
    public void TheDigitKeysThemselvesNoLongerPick() =>
        Assert.Equal(AppCommand.None, KeyCommands.For(Key.Number1, false));   // the typed '1' does, once, whatever the layout
}

public class CreditTests
{
    [Theory]
    [InlineData("Minith Labs", null)]
    [InlineData("minith labs ", null)]
    [InlineData("  ", null)]
    [InlineData("Ada ", "Ada")]
    public void OnlySomeoneElsesNameIsCredited(string author, string? expected) =>
        Assert.Equal(expected, MacChrome.Credit(new VibescapeEntry(VibescapeId.Custom("x"), "X", author, "body")));

    [Fact]
    public void BuiltInsAreNeverCredited() =>
        Assert.All(new VibescapeCatalog([]).Entries, e => Assert.Null(MacChrome.Credit(e)));
}

public sealed class SourceSelectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vibresia-source-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_dir, Settings.FileName);
    private Log NewLog() => new(Path.Combine(_dir, "logs"));

    private Settings With(AudioChoice audio, Log log)
    {
        var s = Settings.Load(PathName, log);
        s.Audio = audio;
        return s;
    }

    [Fact]
    public void ARememberedMicrophoneIsNotStartedAtLaunchAndStaysChosen()
    {
        using var log = NewLog();
        var selection = new SourceSelection(With(AudioChoice.Microphone, log), hasLoopback: true);
        Assert.Null(selection.AtLaunch());
        Assert.Equal(AudioChoice.Microphone, selection.Chosen); // the menu still ticks it
        using var pipe = new AudioPipeline(log);
        Assert.Equal(AudioStatus.None, pipe.Status);           // nothing started: the pill reads IDLE
    }

    [Theory]
    [InlineData(AudioChoice.Loopback, true, AudioChoice.Loopback)]
    [InlineData(AudioChoice.Loopback, false, AudioChoice.Demo)]
    [InlineData(AudioChoice.Demo, true, AudioChoice.Demo)]
    public void LaunchStartsTheRememberedSourceOrTheDemoWhereSystemAudioCantBeCaptured(AudioChoice remembered, bool hasLoopback, AudioChoice started)
    {
        using var log = NewLog();
        var selection = new SourceSelection(With(remembered, log), hasLoopback);
        Assert.Equal(started, selection.AtLaunch());
        Assert.Equal(started, selection.Chosen);
    }

    [Fact]
    public void PickingAndTheDKeyChangeWhatIsSaved()
    {
        using var log = NewLog();
        var settings = With(AudioChoice.Loopback, log);
        var selection = new SourceSelection(settings, hasLoopback: true);
        Assert.Equal(AudioChoice.Demo, selection.ToggleDemo());
        settings.Save(log);
        Assert.Equal(AudioChoice.Demo, Settings.Load(PathName, log).Audio);
        Assert.Equal(AudioChoice.Loopback, selection.ToggleDemo());
        Assert.Equal(AudioChoice.Microphone, selection.Pick(AudioChoice.Microphone));
        settings.Save(log);
        Assert.Equal(AudioChoice.Microphone, Settings.Load(PathName, log).Audio);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

public class OpenEdgeTests
{
    [Fact]
    public void FiresOnlyOnTheFrameItOpens()
    {
        var edge = new OpenEdge();
        Assert.False(edge.Update(false));
        Assert.True(edge.Update(true));
        Assert.False(edge.Update(true));
        Assert.False(edge.Update(false));
        Assert.True(edge.Update(true));
    }
}

public class PanelPictureTests
{
    /// <summary>A 16:9 picture in a wider strip keeps its full width and loses top and bottom equally (aspectRatio .fill).</summary>
    [Fact]
    public void FillCropsTheMiddleAndFlipsAGlRender()
    {
        var (uv0, uv1) = MacChrome.FillUv(640, 360, 300, 96, bottomUp: false);
        Assert.Equal(0f, uv0.X);
        Assert.Equal(1f, uv1.X);
        var kept = uv1.Y - uv0.Y;
        Assert.Equal(96f / 300 / (360f / 640), kept, 3);
        Assert.Equal(1 - uv1.Y, uv0.Y, 5);                   // centred
        var (f0, f1) = MacChrome.FillUv(640, 360, 300, 96, bottomUp: true);
        Assert.Equal(uv1.Y, f0.Y, 5);                         // rows run the other way
        Assert.Equal(uv0.Y, f1.Y, 5);

        var (n0, n1) = MacChrome.FillUv(640, 360, 100, 96, bottomUp: false);   // a narrow box crops the sides instead
        Assert.Equal(0f, n0.Y);
        Assert.True(n0.X > 0 && n1.X < 1);
    }
}

public class StillPictureTests
{
    [Fact]
    public void TheStandInMusicIsTheMacsAndAlwaysTheSame()
    {
        var a = StillPicture.Uniforms();
        var b = StillPicture.Uniforms();
        Assert.Equal(a.Bands, b.Bands);                       // seeded: the same vibescape gets the same picture
        Assert.Equal(1f, a.Active);
        Assert.Equal((float)StillPicture.Seconds, a.Time);
        Assert.Equal(3f, a.Beats);                            // a 120 bpm grid: two beats a second
        Assert.Equal(StillPicture.Width, (int)a.ResolutionX);
        Assert.All(a.Bands, v => Assert.InRange(v, 0f, 1f));
        Assert.True(a.Bands[0] > a.Bands[^1], "bass-heavy, as the Mac's synthetic spectrum is");

        var onTheBeat = StillPicture.Uniforms(seconds: 0);
        Assert.Equal(1f, onTheBeat.Beat);                     // the kick lands at 0 and has decayed by 1.5 s
        Assert.Equal(0.62f + 0.28f, onTheBeat.Bass, 4);
        Assert.True(a.Bass < onTheBeat.Bass);
    }
}

public sealed class VibescapeListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vibresia-list-" + Guid.NewGuid().ToString("N"));
    private Settings NewSettings(Log log) => Settings.Load(Path.Combine(_dir, Settings.FileName), log);
    private static CustomVibescape Custom(string id, bool enabled = true) => new(id, id, "", "vec3 render(vec2 p, float t){return vec3(1.0);}", enabled);

    [Fact]
    public void PickingASwitchedOffVibescapeSwitchesItOnOnceAndRecordsIt()
    {
        using var log = new Log(Path.Combine(_dir, "logs"));
        var settings = NewSettings(log);
        var list = new VibescapeList(settings, [Custom("off", enabled: false)]);
        var off = VibescapeId.Custom("off");
        Assert.False(list.Catalog.IsOn(off));
        Assert.True(list.SwitchOnToPick(off));                  // changed: the caller saves
        Assert.True(list.Catalog.IsOn(off));
        Assert.True(settings.Switches[off]);                    // in settings, not the file
        Assert.False(list.SwitchOnToPick(off));                 // already on: nothing to save
        Assert.False(list.SwitchOnToPick(VibescapeId.Builtin("warp")));
    }

    [Fact]
    public void MovingSwitchingAndResettingGoIntoTheSettings()
    {
        using var log = new Log(Path.Combine(_dir, "logs"));
        var settings = NewSettings(log);
        var list = new VibescapeList(settings, [Custom("mine")]);
        Assert.True(list.Move(VibescapeId.Custom("mine"), 0));
        Assert.Equal(VibescapeId.Custom("mine"), settings.Order![0]);
        Assert.Equal(VibescapeId.Custom("mine"), list.Catalog.Entries[0].Id);
        Assert.True(list.Switch(VibescapeId.Builtin("warp"), false));
        Assert.False(list.Catalog.IsOn(VibescapeId.Builtin("warp")));
        Assert.False(list.Switch(VibescapeCatalog.Default, false));   // the fallback stays on: no change
        Assert.True(list.ResetOrder());
        Assert.Null(settings.Order);
        Assert.False(list.ResetOrder());
        Assert.False(settings.Switches[VibescapeId.Builtin("warp")]); // resetting the order keeps the switches
    }

    [Fact]
    public void ARescanKeepsTheArrangementAndDropsWhatIsGone()
    {
        using var log = new Log(Path.Combine(_dir, "logs"));
        var settings = NewSettings(log);
        var list = new VibescapeList(settings, [Custom("a"), Custom("b")]);
        list.Move(VibescapeId.Custom("b"), 0);
        list.Rescan([Custom("b"), Custom("c")]);
        Assert.Equal(VibescapeId.Custom("b"), list.Catalog.All[0].Entry.Id);
        Assert.Null(list.Catalog.Find(VibescapeId.Custom("a")));
        Assert.NotNull(list.Catalog.Find(VibescapeId.Custom("c")));
    }

    /// <summary>
    /// As the Mac's moveVibescape: the dragged one is taken out and put in at
    /// the target's place, so dropped on a card further down it lands after
    /// that card, and dropped on one further up it lands before it.
    /// </summary>
    [Fact]
    public void DropsLandAfterATargetBelowAndBeforeATargetAbove()
    {
        var c = new VibescapeCatalog([]);
        var ids = c.All.Select(l => l.Entry.Id).ToList();
        var down = c.OrderAfterMove(ids[0], 2)!;                 // ribbons onto silk
        Assert.Equal([ids[1], ids[2], ids[0]], down.Take(3));
        var up = c.OrderAfterMove(ids[3], 1)!;                   // flux onto vortex
        Assert.Equal([ids[0], ids[3], ids[1]], up.Take(3));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

public class SystemRulesTests
{
    [Theory]
    [InlineData("https://vibresia.com", true)]
    [InlineData("https://minithlabs.com", true)]
    [InlineData("https://inertialnavigation.com", true)]
    [InlineData("https://vibresia.com/", false)]
    [InlineData("https://example.com", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("", false)]
    public void OnlyTheCreditsPagesAreEverOpened(string url, bool allowed) =>
        Assert.Equal(allowed, CreditsLinks.IsAllowed(url));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]   // suspended in full screen, as on the Mac
    [InlineData(false, false, false)]
    public void FloatingIsSuspendedInFullScreen(bool floatOnTop, bool fullScreen, bool expected) =>
        Assert.Equal(expected, WindowFloat.Wanted(floatOnTop, fullScreen));

    [Fact]
    public void AStartupEntryFollowsTheRunningCopyOnlyWhenItIsOn()
    {
        const string now = @"C:\dev14\Vibresia.exe";
        Assert.Equal("\"" + now + "\"", StartupEntry.CommandFor(now));
        Assert.Equal("\"" + now + "\"", StartupEntry.Repointed("\"C:\\dev13\\Vibresia.exe\"", now)); // an older build: point it here
        Assert.Null(StartupEntry.Repointed("\"c:\\DEV14\\vibresia.exe\" ", now));                    // already this copy (Windows paths ignore case)
        Assert.Null(StartupEntry.Repointed(null, now));                                               // off: leave it off
        Assert.Null(StartupEntry.Repointed("  ", now));
        Assert.True(StartupEntry.IsSet("\"x\""));
        Assert.False(StartupEntry.IsSet(""));
    }

    [Fact]
    public void OpeningTheListOrPanelRescansAndOpeningSettingsReadsStartAtLogin()
    {
        var o = new ChromeOpenings();
        Assert.Equal((false, false), o.Update(false, false, false));
        Assert.Equal((true, false), o.Update(true, false, false));    // the drop-down opens
        Assert.Equal((false, false), o.Update(true, false, false));   // still open: once only
        Assert.Equal((true, false), o.Update(false, true, false));    // the panel opens
        Assert.Equal((false, true), o.Update(false, false, true));    // the Settings drop-down opens
        Assert.Equal((false, false), o.Update(false, false, true));
    }
}
