using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vibresia.Core.Vibescapes;

/// <summary>
/// A user's vibescape in upstream's on-disk format (VisualiserMac
/// Render/Vibescapes.swift, <c>CustomVibescape</c>). Only the fields v1 uses
/// are read; share fields and anything unknown are ignored, and the file is
/// never written, so a file copied from a Mac keeps everything it had.
/// </summary>
public sealed record CustomVibescape(string Id, string Name, string Author, string Shader, bool Enabled)
{
    public string FileName { get; init; } = "";
}

/// <summary>
/// Loads every <c>*.json</c> in the custom folder, the way upstream's
/// CustomVibescapeStore.loadAll does: sorted by filename, and the first file
/// to claim an id wins. Unlike upstream, a file that is skipped is logged
/// with why, because these files are untrusted input and a silent skip looks
/// like the app ignoring them.
/// </summary>
public static class CustomVibescapeLoader
{
    /// <summary>Largest file read (security-model.md): bigger is skipped unread.</summary>
    public const long MaxFileBytes = 256 * 1024;

    /// <summary>%APPDATA%\Vibresia\Vibescapes on Windows; the XDG equivalent elsewhere.</summary>
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create), "Vibresia", "Vibescapes");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };

    private sealed class Wire
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("shader")] public string? Shader { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    }

    /// <summary>Reads the folder, creating it if it doesn't exist yet so the user can find it.</summary>
    public static IReadOnlyList<CustomVibescape> LoadAll(string folder, Log log)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn("custom", $"couldn't create {folder}: {e.Message}");
            return [];
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(folder)
                .Where(f => Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn("custom", $"couldn't list {folder}: {e.Message}");
            return [];
        }

        var loaded = new List<CustomVibescape>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var (vibescape, problem) = Read(path);
            if (vibescape is null)
            {
                log.Warn("custom", $"skipped {name}: {problem}");
                continue;
            }
            if (!seen.Add(vibescape.Id))
            {
                log.Warn("custom", $"skipped {name}: id '{vibescape.Id}' is already used by an earlier file");
                continue;
            }
            if (vibescape.Id != Path.GetFileNameWithoutExtension(path))
                log.Info("custom", $"{name} has id '{vibescape.Id}', which doesn't match its filename (loaded anyway)");
            loaded.Add(vibescape with { FileName = name });
        }
        log.Info("custom", $"loaded {loaded.Count} custom vibescape(s) from {folder}" +
                           (loaded.Count(v => !v.Enabled) is var off and > 0 ? $" ({off} disabled)" : ""));
        return loaded;
    }

    /// <returns>The vibescape, or null and the reason it can't be used.</returns>
    public static (CustomVibescape? Vibescape, string? Problem) Read(string path)
    {
        try
        {
            var size = new FileInfo(path).Length;
            if (size > MaxFileBytes)
                return (null, $"{size / 1024} KB is over the {MaxFileBytes / 1024} KB limit");
            var wire = JsonSerializer.Deserialize<Wire>(File.ReadAllBytes(path), Options);
            if (wire is null) return (null, "the file is JSON null");
            var missing = new[] { ("id", wire.Id), ("name", wire.Name), ("shader", wire.Shader) }
                .Where(f => f.Item2 is null).Select(f => f.Item1).ToList();
            if (missing.Count > 0) return (null, $"missing {string.Join(", ", missing)}");
            if (wire.Id!.Length == 0) return (null, "empty id");
            return (new CustomVibescape(wire.Id, wire.Name!, wire.Author ?? "", wire.Shader!, wire.Enabled ?? true), null);
        }
        catch (JsonException e)
        {
            return (null, $"not valid JSON ({e.Message})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, $"couldn't read it ({e.Message})");
        }
    }
}
