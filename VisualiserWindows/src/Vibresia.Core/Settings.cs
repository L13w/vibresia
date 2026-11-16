using System.Text.Json;
using System.Text.Json.Nodes;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Core;

/// <summary>
/// Which audio source was last chosen, in the Mac's picker order (system
/// audio, microphone, demo tone). The microphone is remembered but, as on the
/// Mac, never started at launch without being asked for: it can pick up a room.
/// </summary>
public enum AudioChoice { Loopback, Microphone, Demo }

/// <summary>
/// settings.json in the roaming app-data folder: the vibescape to start on,
/// the audio source, the palette, whether to keep the display awake, the CRT effect, the vibescape list's order and switches, and floating above other windows (data-model.md). A missing or unreadable file means
/// defaults. Keys this version doesn't know are kept when the file is
/// rewritten, so a later version's settings survive a run of this one.
/// </summary>
public sealed class Settings
{
    public const int Version = 1;
    public const string FileName = "settings.json";

    private readonly JsonObject _root;

    public string FilePath { get; }
    public VibescapeId Vibescape { get; set; } = VibescapeCatalog.Default;
    public AudioChoice Audio { get; set; } = AudioChoice.Loopback;

    /// <summary>
    /// The palette last picked, as an index into <see cref="Palette.All"/>. Stored by
    /// name so reordering the palettes can't swap one for another; an unknown
    /// name means NEON. The Mac doesn't remember it; remembering keeps the look someone chose across a restart.
    /// </summary>
    public int PaletteIndex { get; set; }

    /// <summary>
    /// Draw the Mac's CRT overlay (scanlines and vignette) while there is sound.
    /// On by default, as on the Mac; off gives the brighter look of the Mac's
    /// exported videos. The Mac has no switch for it.
    /// </summary>
    public bool CrtEffect { get; set; } = true;

    /// <summary>The user's arrangement of the vibescape list (the View All panel), or null for the shipped order.</summary>
    public IReadOnlyList<VibescapeId>? Order { get; set; }

    /// <summary>Vibescapes the user switched on or off in the View All panel; one not named keeps its default.</summary>
    public IReadOnlyDictionary<VibescapeId, bool> Switches { get; set; } = new Dictionary<VibescapeId, bool>();

    /// <summary>Keep the window above other apps' windows (the credits popover's "Float above other apps"). Off by default, as on the Mac.</summary>
    public bool FloatOnTop { get; set; }

    /// <summary>Ask Windows not to dim or turn off the display while running. Off by default, as the Mac doesn't ask.</summary>
    public bool KeepDisplayAwake { get; set; }

    private Settings(string path, JsonObject root)
    {
        FilePath = path;
        _root = root;
    }

    /// <summary>%APPDATA%\Vibresia\settings.json on Windows; the XDG equivalent elsewhere.</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create), "Vibresia", FileName);

    public static Settings Load(string path, Log log)
    {
        if (!File.Exists(path))
        {
            log.Info("settings", $"no {FileName} yet; using defaults");
            return new Settings(path, new JsonObject());
        }
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
                throw new JsonException("the top level isn't an object");
            var s = new Settings(path, root);
            if (VibescapeId.Parse(ReadString(root, "vibescape")) is { } id) s.Vibescape = id;
            s.Audio = ReadString(root, "audio") switch
            {
                "demo" => AudioChoice.Demo,
                "microphone" => AudioChoice.Microphone,
                _ => AudioChoice.Loopback,
            };
            var paletteName = ReadString(root, "palette");
            s.PaletteIndex = Math.Max(0, Palette.All.ToList().FindIndex(p => p.Name == paletteName));
            s.KeepDisplayAwake = root["keepDisplayAwake"] is JsonValue k && k.TryGetValue<bool>(out var awake) && awake;
            s.Order = root["order"] is JsonArray order
                ? order.Select(n => n is JsonValue v && v.TryGetValue<string>(out var t) ? VibescapeId.Parse(t) : null)
                    .OfType<VibescapeId>().ToList()
                : null;
            if (root["switches"] is JsonObject switches)
                s.Switches = switches
                    .Select(kv => (Id: VibescapeId.Parse(kv.Key), On: kv.Value is JsonValue v && v.TryGetValue<bool>(out var b) ? b : (bool?)null))
                    .Where(x => x.Id is not null && x.On is not null)
                    .GroupBy(x => x.Id!.Value)
                    .ToDictionary(g => g.Key, g => g.Last().On!.Value);
            s.FloatOnTop = root["floatOnTop"] is JsonValue f && f.TryGetValue<bool>(out var floats) && floats;
            s.CrtEffect = root["crtEffect"] is not JsonValue c || !c.TryGetValue<bool>(out var crt) || crt;
            log.Info("settings", $"loaded: vibescape {s.Vibescape}, audio {s.Audio}, palette {Palette.At(s.PaletteIndex).Name}, keep display awake {s.KeepDisplayAwake}, CRT effect {s.CrtEffect}, float on top {s.FloatOnTop}, {(s.Order is null ? "shipped order" : "own order")}, {s.Switches.Count(kv => !kv.Value)} switched off");
            return s;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warn("settings", $"couldn't read {path} ({e.Message}); using defaults");
            return new Settings(path, new JsonObject());
        }
    }

    private static string? ReadString(JsonObject root, string key) =>
        root[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Writes the file. A failure is logged, never thrown: losing a preference must not stop the app.</summary>
    public void Save(Log log)
    {
        _root["version"] = Version;
        _root["vibescape"] = Vibescape.ToString();
        _root["audio"] = Audio switch
        {
            AudioChoice.Demo => "demo",
            AudioChoice.Microphone => "microphone",
            _ => "loopback",
        };
        _root["palette"] = Palette.At(PaletteIndex).Name;
        _root["keepDisplayAwake"] = KeepDisplayAwake;
        _root["crtEffect"] = CrtEffect;
        _root["floatOnTop"] = FloatOnTop;
        if (Order is null) _root.Remove("order");
        else _root["order"] = new JsonArray(Order.Select(id => (JsonNode?)JsonValue.Create(id.ToString())).ToArray());
        var switches = new JsonObject();
        foreach (var (id, on) in Switches.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal)) switches[id.ToString()] = on;
        _root["switches"] = switches;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn("settings", $"couldn't save {FilePath}: {e.Message}");
        }
    }
}
