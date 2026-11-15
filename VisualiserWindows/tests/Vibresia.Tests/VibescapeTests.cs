using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using Vibresia.Core;
using Vibresia.Core.Audio;
using Vibresia.Core.Vibescapes;
using Vibresia.Render;

namespace Vibresia.Tests;

public class VibescapeIdTests
{
    [Theory]
    [InlineData("builtin:kaleidoscope", false, "kaleidoscope")]
    [InlineData("custom:my-thing", true, "my-thing")]
    public void RoundTrips(string text, bool custom, string slug)
    {
        var id = VibescapeId.Parse(text);
        Assert.Equal(new VibescapeId(custom, slug), id);
        Assert.Equal(text, id!.Value.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kaleidoscope")]
    [InlineData("builtin:")]
    [InlineData("custom:")]
    public void RejectsMalformed(string? text) => Assert.Null(VibescapeId.Parse(text));
}

public class CatalogTests
{
    private static CustomVibescape Custom(string id, bool enabled = true) => new(id, id + " name", "someone", "vec3 render(vec2 p, float t){return vec3(1.0);}", enabled);

    /// <summary>Every upstream shader built-in is either listed or deliberately excluded, so a new one upstream can't go missing unnoticed.</summary>
    [Fact]
    public void ListsEveryUpstreamShaderBuiltinExceptTheExcludedOnes()
    {
        var listed = VibescapeCatalog.Builtins.Select(b => b.Slug).ToHashSet();
        Assert.Subset(ShaderSources.BuiltinOrder.ToHashSet(), listed);
        Assert.Equal(ShaderSources.BuiltinOrder.ToHashSet(), listed.Union(VibescapeCatalog.Excluded).ToHashSet());
        Assert.Empty(listed.Intersect(VibescapeCatalog.Excluded));
    }

    [Fact]
    public void BuiltinsComeInTheMacsFreshInstallOrderWithCustomsAfter()
    {
        var c = new VibescapeCatalog([Custom("mine")]);
        Assert.Equal("builtin:ribbons", c.Entries[0].Id.ToString());
        Assert.Equal("builtin:vortex", c.Entries[1].Id.ToString());
        Assert.Equal("builtin:danceFloor", c.Entries[^2].Id.ToString());
        Assert.Equal(VibescapeId.Custom("mine"), c.Entries[^1].Id);
        Assert.Equal("MINE NAME", c.Entries[^1].Title);
    }

    [Fact]
    public void DisabledCustomsAreNotListed()
    {
        var c = new VibescapeCatalog([Custom("off", enabled: false), Custom("on")]);
        Assert.Null(c.Find(VibescapeId.Custom("off")));
        Assert.NotNull(c.Find(VibescapeId.Custom("on")));
    }

    [Fact]
    public void StepWrapsAtBothEnds()
    {
        var c = new VibescapeCatalog([]);
        Assert.Equal(c.Entries[^1].Id, c.Step(c.Entries[0].Id, -1).Id);
        Assert.Equal(c.Entries[0].Id, c.Step(c.Entries[^1].Id, 1).Id);
        Assert.Equal(c.Entries[1].Id, c.Step(VibescapeId.Custom("gone"), 1).Id);
    }

    [Fact]
    public void NumberKeysPickTheFirstTenAsOnTheMac()
    {
        var c = new VibescapeCatalog([]);
        Assert.Equal(c.Entries[0].Id, c.ForShortcut('1')!.Id);
        Assert.Equal(c.Entries[9].Id, c.ForShortcut('0')!.Id);
        Assert.Null(c.ForShortcut('x'));
    }

    [Fact]
    public void ThePixelArtBuiltinsAreUngradedAndCustomsAreGraded()
    {
        var c = new VibescapeCatalog([Custom("mine")]);
        Assert.False(c.Find(VibescapeId.Builtin("nyan"))!.Graded);
        Assert.True(c.Find(VibescapeId.Builtin("kaleidoscope"))!.Graded);
        Assert.True(c.Find(VibescapeId.Custom("mine"))!.Graded);
    }
}

public sealed class CustomLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vibresia-custom-" + Guid.NewGuid().ToString("N"));
    private string Folder => Path.Combine(_root, "Vibescapes");
    private string LogText => File.ReadAllText(Path.Combine(_root, "logs", Log.FileName));

    private const string Body = "vec3 render(vec2 p, float t) { return vec3(uBass); }";

    private void Put(string file, string json)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, file), json);
    }

    private static string Json(string id, string name = "N", string? extra = null) =>
        $$"""{"id":"{{id}}","name":"{{name}}","author":"A","shader":{{System.Text.Json.JsonSerializer.Serialize(Body)}}{{(extra is null ? "" : "," + extra)}}}""";

    private IReadOnlyList<CustomVibescape> Load()
    {
        using var log = new Log(Path.Combine(_root, "logs"));
        return CustomVibescapeLoader.LoadAll(Folder, log);
    }

    [Fact]
    public void LoadsAnUpstreamFileIgnoringShareAndUnknownFields()
    {
        Put("glow.json", Json("glow", "Glow", "\"enabled\":true,\"modified\":\"2026-01-02T03:04:05Z\",\"shareID\":\"x\",\"shareEditKey\":\"secret\",\"somethingNew\":[1,2]"));
        var v = Assert.Single(Load());
        Assert.Equal(new CustomVibescape("glow", "Glow", "A", Body, true) { FileName = "glow.json" }, v);
        Assert.DoesNotContain("secret", LogText);
    }

    [Fact]
    public void EnabledDefaultsToTrueAndFalseIsKept()
    {
        Put("a.json", Json("a"));
        Put("b.json", Json("b", extra: "\"enabled\":false"));
        var all = Load();
        Assert.True(all.Single(v => v.Id == "a").Enabled);
        Assert.False(all.Single(v => v.Id == "b").Enabled);
    }

    [Fact]
    public void FirstFileByNameWinsADuplicateId()
    {
        Put("b.json", Json("same", "Second"));
        Put("a.json", Json("same", "First"));
        Assert.Equal("First", Assert.Single(Load()).Name);
        Assert.Contains("skipped b.json: id 'same' is already used", LogText);
    }

    [Fact]
    public void BadFilesAreSkippedAndLoggedWhileTheRestLoad()
    {
        Put("1-malformed.json", "{ not json");
        Put("2-noshader.json", """{"id":"x","name":"X"}""");
        Put("3-huge.json", Json("huge", extra: $"\"pad\":\"{new string('a', 300 * 1024)}\""));
        Put("4-null.json", "null");
        Put("5-wrongtype.json", """{"id":"w","name":"W","shader":"s","enabled":"yes"}""");
        Put("6-badmodified.json", """{"id":"m","name":"M","shader":"s","modified":42}""");
        Put("7-badshare.json", """{"id":"k","name":"K","shader":"s","shareEditKey":{"x":1}}""");
        Put("8-emptyid.json", """{"id":"","name":"E","shader":"s"}""");
        Put("good.json", Json("good"));
        Assert.Equal("good", Assert.Single(Load()).Id);
        var log = LogText;
        Assert.Contains("skipped 1-malformed.json: not valid JSON", log);
        Assert.Contains("skipped 2-noshader.json: missing shader", log);
        Assert.Contains("skipped 3-huge.json: 300 KB is over the 256 KB limit", log);
        Assert.Contains("skipped 4-null.json", log);
        Assert.Contains("skipped 5-wrongtype.json: not valid JSON", log);
        Assert.Contains("skipped 6-badmodified.json: not valid JSON", log);
        Assert.Contains("skipped 7-badshare.json: shareEditKey isn't a string", log);
        Assert.Contains("skipped 8-emptyid.json: empty id", log);
    }

    [Fact]
    public void AnIdThatDoesNotMatchTheFilenameStillLoads()
    {
        Put("copied.json", Json("original"));
        Assert.Equal("original", Assert.Single(Load()).Id);
        Assert.Contains("doesn't match its filename", LogText);
    }

    [Fact]
    public void OnlyJsonFilesAreReadAndTheFolderIsCreated()
    {
        Assert.Empty(Load());
        Assert.True(Directory.Exists(Folder));
        Put("notes.txt", Json("txt"));
        Put("UPPER.JSON", Json("upper"));
        Assert.Equal("upper", Assert.Single(Load()).Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

public class ShaderCheckTests
{
    [Theory]
    [InlineData("", "nothing in the box")]
    [InlineData("#version 330 core\nvec3 render(vec2 p, float t){return vec3(1.);}", "#version")]
    [InlineData("void main() { fragColor = vec4(1.); }", "render, not main")]
    [InlineData("vec3 render(vec2 p, float t){ fragColor = vec4(1.); return vec3(1.); }", "fragColor")]
    [InlineData("vec3 render(vec2 p){return vec3(1.);}", "not in the shape")]
    [InlineData("vec3 draw(vec2 p, float t){return vec3(1.);}", "needs a function called render")]
    public void ShapeProblemsAreCaughtBeforeCompiling(string body, string expected) =>
        Assert.Contains(expected, ShaderCheck.CheckShape(body)!.Message);

    [Fact]
    public void AGoodShapePasses() =>
        Assert.Null(ShaderCheck.CheckShape("// fragColor in a comment is fine\nvec3 render(vec2 p,\n float t) { return vec3(0.5); }"));

    [Theory]
    [InlineData("0:57(12): error: `nope' undeclared", 57)]
    [InlineData("0(57) : error C1008: undefined variable \"nope\"", 57)]
    [InlineData("ERROR: 0:57: 'nope' : undeclared identifier", 57)]
    [InlineData("error: something with no position", null)]
    public void ReadsTheLineFromEachDriversFormat(string log, int? line) =>
        Assert.Equal(line, ShaderCheck.DriverLine(log));

    [Fact]
    public void LinesAreCountedFromTheUsersFirstLine()
    {
        Assert.Equal(3, ShaderCheck.UserLine(53, 50));
        Assert.Equal(1, ShaderCheck.UserLine(10, 50)); // inside the prelude
    }

    [Fact]
    public void AMisspelledUniformGetsADidYouMean()
    {
        var p = ShaderCheck.Explain("ERROR: 0:52: 'uBas' : undeclared identifier", 50);
        Assert.Equal(2, p.Line);
        Assert.Contains("The closest is uBass", p.Message);
        Assert.Equal("Line 2 needs a fix", p.Title);
    }

    [Fact]
    public void AnUnrecognisedErrorShowsTheDriversFirstLine()
    {
        var p = ShaderCheck.Explain("0:60(1): error: something odd\nmore", 50);
        Assert.Contains("something odd", p.ForScreen());
        Assert.Equal(10, p.Line);
    }
}

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vibresia-settings-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_dir, Settings.FileName);
    private Log NewLog() => new(Path.Combine(_dir, "logs"));

    [Fact]
    public void MissingFileMeansDefaults()
    {
        using var log = NewLog();
        var s = Settings.Load(PathName, log);
        Assert.Equal(VibescapeCatalog.Default, s.Vibescape);
        Assert.Equal(AudioChoice.Loopback, s.Audio);
    }

    [Fact]
    public void RoundTrips()
    {
        using var log = NewLog();
        var s = Settings.Load(PathName, log);
        s.Vibescape = VibescapeId.Custom("mine");
        s.Audio = AudioChoice.Demo;
        s.Save(log);
        var back = Settings.Load(PathName, log);
        Assert.Equal(VibescapeId.Custom("mine"), back.Vibescape);
        Assert.Equal(AudioChoice.Demo, back.Audio);
        Assert.Equal(1, (int)JsonNode.Parse(File.ReadAllText(PathName))!["version"]!);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[1,2]")]
    [InlineData("""{"vibescape": 7, "audio": null}""")]
    public void CorruptMeansDefaults(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathName, text);
        using var log = NewLog();
        var s = Settings.Load(PathName, log);
        Assert.Equal(VibescapeCatalog.Default, s.Vibescape);
        Assert.Equal(AudioChoice.Loopback, s.Audio);
    }

    [Fact]
    public void TheMicrophoneAndKeepAwakeRoundTripAndKeepAwakeIsOffByDefault()
    {
        using var log = NewLog();
        var s = Settings.Load(PathName, log);
        Assert.False(s.KeepDisplayAwake);
        s.Audio = AudioChoice.Microphone;
        s.KeepDisplayAwake = true;
        s.Save(log);
        var back = Settings.Load(PathName, log);
        Assert.Equal(AudioChoice.Microphone, back.Audio);
        Assert.True(back.KeepDisplayAwake);
        Assert.Equal("microphone", (string)JsonNode.Parse(File.ReadAllText(PathName))!["audio"]!);
    }

    [Fact]
    public void AKeepAwakeOfTheWrongTypeMeansOff()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathName, """{"version":1,"keepDisplayAwake":"yes"}""");
        using var log = NewLog();
        Assert.False(Settings.Load(PathName, log).KeepDisplayAwake);
    }

    [Fact]
    public void ThePaletteRoundTripsByNameAndStartsOnNeon()
    {
        using var log = NewLog();
        var s = Settings.Load(PathName, log);
        Assert.Equal(0, s.PaletteIndex);
        s.PaletteIndex = 3;
        s.Save(log);
        Assert.Equal(Palette.All[3].Name, (string)JsonNode.Parse(File.ReadAllText(PathName))!["palette"]!);
        Assert.Equal(3, Settings.Load(PathName, log).PaletteIndex);
    }

    [Theory]
    [InlineData("\"PLAID\"")]
    [InlineData("2")]
    public void AnUnknownPaletteMeansNeon(string value)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathName, $$"""{"version":1,"palette":{{value}}}""");
        using var log = NewLog();
        Assert.Equal(0, Settings.Load(PathName, log).PaletteIndex);
    }

    [Fact]
    public void UnknownKeysSurviveASave()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathName, """{"version":1,"vibescape":"builtin:warp","future":{"x":1}}""");
        using var log = NewLog();
        var s = Settings.Load(PathName, log);
        Assert.Equal(VibescapeId.Builtin("warp"), s.Vibescape);
        s.Save(log);
        Assert.Equal(1, (int)JsonNode.Parse(File.ReadAllText(PathName))!["future"]!["x"]!);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

public class BuiltinStateTests
{
    private static FrameUniforms Frame(float beat = 0, float strength = 0, float idle = 0, float active = 1, double bpm = 0)
    {
        var u = new FrameUniforms { Beat = beat, BeatStrength = strength, Idle = idle, Active = active, Bpm = bpm };
        return u;
    }

    [Fact]
    public void SpectrumPeaksHoldTheHighestPairThenFallSlowly()
    {
        var s = new BuiltinState();
        var u = Frame();
        u.Bands[0] = 0.2f; u.Bands[1] = 0.8f;
        s.Advance("spectrum", u, 0.1);
        Assert.Equal(0.8f, s.Peaks[0], 4);
        u.Bands[1] = 0;
        s.Advance("spectrum", u, 1.0);
        Assert.Equal(0.8f - 0.35f, s.Peaks[0], 4);
    }

    [Fact]
    public void StateOnlyAdvancesForTheBuiltinShowing()
    {
        var s = new BuiltinState();
        var u = Frame(idle: 0, bpm: 137);
        u.Bands[0] = 1;
        s.Advance("kaleidoscope", u, 0.05);
        Assert.Equal(0f, s.Peaks[0]);
        Assert.Equal(0, s.SpriteClock);
    }

    [Fact]
    public void BadgerOverlayShowsOnABigBeatEveryThirdIsTheOtherKindAndItExpires()
    {
        var s = new BuiltinState();
        var kinds = new List<float>();
        for (var i = 0; i < 3; i++)
        {
            s.Advance("badger", Frame(beat: 1, strength: 0.9f), 0.01);
            kinds.Add(s.EventKind);
            s.Advance("badger", Frame(), 1.7); // past the overlay's 1.6 s
            Assert.Equal(0, s.EventKind);
        }
        Assert.Equal([1f, 1f, 2f], kinds);
    }

    [Fact]
    public void ASmallBeatDoesNotTriggerTheOverlay()
    {
        var s = new BuiltinState();
        s.Advance("badger", Frame(beat: 1, strength: 0.2f), 0.01);
        Assert.Equal(0, s.EventKind);
    }

    [Fact]
    public void HamsterClockFollowsTheTempoAndStopsInSilence()
    {
        var s = new BuiltinState();
        s.Advance("hamster", Frame(bpm: 137 * 2), 0.1); // clamped to 1.5x
        Assert.Equal(0.15, s.SpriteClock, 6);
        s.Advance("hamster", Frame(idle: 1, bpm: 137), 0.1);
        Assert.Equal(0.15, s.SpriteClock, 6);
        s.Advance("hamster", Frame(active: 0.5f), 0.5); // dt capped at 0.1, no tempo = 1x
        Assert.Equal(0.2, s.SpriteClock, 6);
    }
}

public class AudioHealthTests
{
    [Fact]
    public void SummarisesGateTimeAndResets()
    {
        var h = new AudioHealth(periodSeconds: 1);
        Assert.Null(h.Add(0.002f, 0, 0, 0, 0.25));
        Assert.Null(h.Add(0.006f, 0.5f, 0.5f, 120, 0.25));
        Assert.Null(h.Add(0.02f, 1, 1, 120, 0.25));
        var s = h.Add(0.02f, 1, 1, 0, 0.25);
        Assert.Equal("1 s: rms avg 0.0120 max 0.0200; gate closed 25% partly 25%; activity avg 0.62; bpm 120", s);
        Assert.Null(h.Add(0, 0, 0, 0, 0.5));
        Assert.EndsWith("no tempo", h.Add(0, 0, 0, 0, 0.5));
    }
}

/// <summary>
/// The app makes no network calls (scope). Checked on the built assemblies
/// themselves: none of them references an HTTP or socket assembly or type.
/// </summary>
public class NoNetworkTests
{
    private static readonly string[] Forbidden = ["System.Net.Http", "System.Net.Sockets", "System.Net.Requests", "System.Net.WebClient", "System.Net.WebSockets"];

    [Fact]
    public void NoAppAssemblyReferencesNetworking()
    {
        var src = Path.Combine(Paths.Windows, "src");
        var dlls = Directory.GetFiles(src, "Vibresia*.dll", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        foreach (var name in new[] { "Vibresia.Core.dll", "Vibresia.Render.dll", "Vibresia.dll" /* the app */, "Vibresia.Audio.Windows.dll" })
            Assert.Contains(dlls, d => Path.GetFileName(d) == name);

        foreach (var dll in dlls)
        {
            using var pe = new PEReader(File.OpenRead(dll));
            var md = pe.GetMetadataReader();
            var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name));
            var types = md.TypeReferences.Select(h => md.GetString(md.GetTypeReference(h).Namespace));
            foreach (var bad in Forbidden)
            {
                Assert.DoesNotContain(bad, refs);
                Assert.DoesNotContain(types, ns => ns.StartsWith(bad, StringComparison.Ordinal));
            }
        }
    }
}

public class PointerInputTests
{
    [Fact]
    public void NobodyThereUntilThePointerMoves()
    {
        var p = new PointerInput();
        var u = new FrameUniforms();
        p.Fill(u, 1 / 60.0);
        Assert.Equal(0, u.MouseHere);
        p.Move(0, 0, 1920, 1080);
        p.Fill(u, 1 / 60.0);
        Assert.Equal(1, u.MouseHere);
    }

    /// <summary>Same coordinates as screenP(): x across from the middle, y up, both over the height.</summary>
    [Fact]
    public void ThePointerIsInScreenPCoordinatesOnceItSettles()
    {
        var p = new PointerInput();
        var u = new FrameUniforms();
        p.Move(1920, 0, 1920, 1080); // top-right corner
        for (var i = 0; i < 300; i++) p.Fill(u, 1 / 60.0);
        Assert.Equal(960f / 1080, u.MouseX, 3);
        Assert.Equal(0.5f, u.MouseY, 3);
    }

    [Fact]
    public void ThePointerEasesWithUpstreamsTimeConstant()
    {
        var p = new PointerInput();
        var u = new FrameUniforms();
        p.Move(2160, 540, 2160, 1080); // right edge: target x = (2160 - 1080) / 1080 = 1
        p.Fill(u, 0.1);
        Assert.Equal(1 - Math.Exp(-0.1 / 0.15), u.MouseX, 4);
        p.Fill(u, 5); // a long frame is capped at 0.1 s, so it can't jump
        Assert.True(u.MouseX < 0.99f);
    }

    [Fact]
    public void KeysAndButtons()
    {
        var p = new PointerInput { Down = true, KeyDown = true };
        p.Typed(' ');
        var u = new FrameUniforms();
        p.Fill(u, 0.01);
        Assert.Equal((1f, 32f, 1f), (u.MouseDown, u.Key, u.KeyDown));
    }
}

public sealed class SessionMarkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vibresia-marker-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ACleanExitLeavesNothingAndACrashIsNoticedNextTime()
    {
        using var log = new Log(Path.Combine(_dir, "logs"));
        var path = Path.Combine(_dir, SessionMarker.FileName);
        var first = new SessionMarker(path);
        Assert.False(first.Begin(log));
        first.End(log);
        Assert.False(new SessionMarker(path).Begin(log)); // previous run was clean
        // ...and this run "crashes": no End.
        Assert.True(new SessionMarker(path).Begin(log));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

/// <summary>Timings and thresholds from VisualiserMac ContentView.swift (CRTOverlay, BeatFlash) and AudioEngine.swift (isSounding, BeatFlashLevel).</summary>
public class MacOverlayTests
{
    private static FrameUniforms Frame(float rms = 0, float beat = 0) => new() { Rms = rms, BeatStrength = beat };

    [Fact]
    public void SoundingHasUpstreamsHysteresis()
    {
        var m = new MacOverlay();
        m.Advance(Frame(rms: 0.005f), true, false, 0.016);
        Assert.False(m.IsSounding);                 // under 0.008 to start
        m.Advance(Frame(rms: 0.009f), true, false, 0.016);
        Assert.True(m.IsSounding);
        m.Advance(Frame(rms: 0.005f), true, false, 0.016);
        Assert.True(m.IsSounding);                  // stays on above 0.003
        m.Advance(Frame(rms: 0.002f), true, false, 0.016);
        Assert.False(m.IsSounding);
        m.Advance(Frame(rms: 0.5f), false, false, 0.016);
        Assert.False(m.IsSounding);                 // no live source, no sound
    }

    [Fact]
    public void TheCrtFadesInOverHalfASecondEasedInAndOut()
    {
        var m = new MacOverlay();
        m.Advance(Frame(rms: 0.02f), true, false, 0.25);
        Assert.Equal(0.5f, m.CrtOpacity, 3);        // easeInOut is halfway at half time
        m.Advance(Frame(rms: 0.02f), true, false, 0.25);
        Assert.Equal(1f, m.CrtOpacity, 3);
        m.Advance(Frame(rms: 0), true, false, 0.125);
        Assert.Equal(1 - 2 * 0.25 * 0.25, m.CrtOpacity, 3);
    }

    [Theory]
    [InlineData(0.039f, 0.0)]
    [InlineData(0.051f, 0.06)]
    [InlineData(0.5f, 0.5)]
    public void FlashStrengthIsSteppedToTwoHundredths(float raw, double expected)
    {
        var m = new MacOverlay();
        m.Advance(Frame(beat: raw), true, false, 0.016);
        Assert.Equal(expected, m.FlashStrength, 6);
    }

    [Fact]
    public void FlashFadesInOnAppearingThenFollowsTheStrengthSquared()
    {
        var m = new MacOverlay();
        m.Advance(Frame(beat: 0.5f), true, false, 0.09);   // half of the 0.18 s easeOut
        Assert.Equal(Math.Pow(0.5 * 0.75, 2), m.FlashOpacity, 3);
        m.Advance(Frame(beat: 0.5f), true, false, 0.2);
        Assert.Equal(0.25, m.FlashOpacity, 3);
        m.Advance(Frame(beat: 0.3f), true, false, 0.016);  // visible to visible: no animation
        Assert.Equal(0.09, m.FlashOpacity, 3);
        Assert.Equal(0.18f, m.FlashCentreAlpha, 4);         // min(0.4, 0.3 × 0.6)
    }

    [Fact]
    public void LessMotionMeansNoFlash()
    {
        var m = new MacOverlay();
        for (var i = 0; i < 30; i++) m.Advance(Frame(beat: 0.8f), true, reduceMotion: true, 0.016);
        Assert.Equal(0f, m.FlashOpacity);
    }
}

public class RenderScaleTests
{
    [Theory]
    [InlineData(3840, 2160, 1.5f, 2560, 1440)]
    [InlineData(3840, 2160, 2f, 1920, 1080)]
    [InlineData(1920, 1080, 1f, 1920, 1080)]
    [InlineData(1920, 1080, 0.5f, 1920, 1080)]      // never drawn bigger than the screen
    [InlineData(1920, 1080, float.NaN, 1920, 1080)]
    public void DrawsAtOneXOfTheDisplayScaling(int w, int h, float scale, int ew, int eh) =>
        Assert.Equal((ew, eh), RenderScale.SceneSize(w, h, scale));
}
