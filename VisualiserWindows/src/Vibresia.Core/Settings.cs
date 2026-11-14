using System.Text.Json;
using System.Text.Json.Nodes;
using Vibresia.Core.Vibescapes;

namespace Vibresia.Core;

/// <summary>Which audio source to start with.</summary>
public enum AudioChoice { Loopback, Demo }

/// <summary>
/// settings.json in the roaming app-data folder: the vibescape to start on
/// and the audio source (data-model.md). A missing or unreadable file means
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
            s.Audio = ReadString(root, "audio") == "demo" ? AudioChoice.Demo : AudioChoice.Loopback;
            log.Info("settings", $"loaded: vibescape {s.Vibescape}, audio {s.Audio}");
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
        _root["audio"] = Audio == AudioChoice.Demo ? "demo" : "loopback";
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
