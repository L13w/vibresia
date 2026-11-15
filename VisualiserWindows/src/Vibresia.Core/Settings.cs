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
/// the audio source, the palette, whether to keep the display awake, and the CRT effect (data-model.md). A missing or unreadable file means
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
    /// name means NEON. The Mac doesn't remember it (an owner-chosen difference).
    /// </summary>
    public int PaletteIndex { get; set; }

    /// <summary>
    /// Draw the Mac's CRT overlay (scanlines and vignette) while there is sound.
    /// On by default, as on the Mac; off gives the brighter look of the Mac's
    /// exported videos. The Mac has no switch for it (an owner-chosen difference).
    /// </summary>
    public bool CrtEffect { get; set; } = true;

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
            s.CrtEffect = root["crtEffect"] is not JsonValue c || !c.TryGetValue<bool>(out var crt) || crt;
            log.Info("settings", $"loaded: vibescape {s.Vibescape}, audio {s.Audio}, palette {Palette.At(s.PaletteIndex).Name}, keep display awake {s.KeepDisplayAwake}, CRT effect {s.CrtEffect}");
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
