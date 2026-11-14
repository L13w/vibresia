namespace Vibresia.Core.Vibescapes;

/// <summary>
/// The vibescapes the app lists: the built-ins this port can draw, in
/// upstream's order, followed by the user's enabled custom vibescapes.
/// </summary>
public sealed class VibescapeCatalog
{
    public const string BuiltinAuthor = "Minith Labs";

    /// <summary>The fresh-install vibescape (the Mac's default, Auto Scan, isn't in this port).</summary>
    public static VibescapeId Default { get; } = VibescapeId.Builtin("kaleidoscope");

    /// <summary>
    /// Built-ins this port draws, with upstream's display names
    /// (AppState.swift Vibescape.displayName), in the order a fresh Mac
    /// install lists them (AppState.defaultVibescapeOrder) less the ones the
    /// port can't draw.
    /// </summary>
    public static IReadOnlyList<(string Slug, string Title)> Builtins { get; } =
    [
        ("ribbons", "RIBBONS"),
        ("vortex", "VORTEX"),
        ("silk", "SILK FLIGHT"),
        ("flux", "FLUX"),
        ("spectrum", "SPECTRUM"),
        ("kaleidoscope", "KALEIDOSCOPE"),
        ("waveform", "NEON WAVE"),
        ("warp", "WARP"),
        ("nyan", "NYAN CAT"),
        ("hamster", "HAMSTER DANCE"),
        ("badger", "BADGER BADGER"),
        ("llama", "WHOOPING LLAMA"),
        ("danceFloor", "DANCE FLOOR"),
    ];

    /// <summary>The keys that pick a vibescape by its place in the list, as on the Mac (AppState.shortcutKeys).</summary>
    public const string ShortcutKeys = "1234567890";

    /// <summary>The entry a shortcut key picks, or null if the list is shorter than that.</summary>
    public VibescapeEntry? ForShortcut(char key) =>
        ShortcutKeys.IndexOf(key) is var i and >= 0 && i < Entries.Count ? Entries[i] : null;

    /// <summary>
    /// Upstream shader built-ins deliberately not listed. The Band needs
    /// instrument detection and per-player state the port doesn't have.
    /// </summary>
    public static IReadOnlySet<string> Excluded { get; } = new HashSet<string>(StringComparer.Ordinal) { "band" };

    public IReadOnlyList<VibescapeEntry> Entries { get; }

    public VibescapeCatalog(IEnumerable<CustomVibescape> customs)
    {
        var list = Builtins.Select(b => new VibescapeEntry(VibescapeId.Builtin(b.Slug), b.Title, BuiltinAuthor)).ToList();
        // Names show in capitals, as upstream lists them.
        list.AddRange(customs.Where(c => c.Enabled)
            .Select(c => new VibescapeEntry(VibescapeId.Custom(c.Id), c.Name.ToUpperInvariant(), c.Author, c.Shader)));
        Entries = list;
    }

    public int IndexOf(VibescapeId id)
    {
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Id == id) return i;
        return -1;
    }

    public VibescapeEntry? Find(VibescapeId id) => IndexOf(id) is var i and >= 0 ? Entries[i] : null;

    /// <summary>The entry <paramref name="step"/> places after <paramref name="from"/>, wrapping at both ends. An id no longer listed counts as the first.</summary>
    public VibescapeEntry Step(VibescapeId from, int step)
    {
        var i = Math.Max(0, IndexOf(from));
        var n = Entries.Count;
        return Entries[((i + step) % n + n) % n];
    }
}
