namespace Vibresia.Core.Vibescapes;

/// <summary>One vibescape as the View All panel shows it: switched on or off, and whether it is the user's own.</summary>
public sealed record VibescapeListing(VibescapeEntry Entry, bool On)
{
    public bool Yours => Entry.Id.IsCustom;
}

/// <summary>
/// The vibescapes the app knows, as the Mac arranges them
/// (AppState.allVibescapes): every built-in this port can draw and every
/// custom vibescape, in the order the user dragged them into (a fresh install
/// gets the Mac's order, with custom vibescapes after it), each switched on or
/// off. The drop-down, the shortcut keys and ←/→ use only the switched-on ones.
/// </summary>
public sealed class VibescapeCatalog
{
    public const string BuiltinAuthor = "Minith Labs";

    /// <summary>
    /// The fresh-install vibescape (the Mac's default, Auto Scan, isn't in
    /// this port), and the one that can't be switched off, so the list is
    /// never empty: the Mac keeps Tunnel on for the same reason, and Tunnel
    /// isn't in this port either.
    /// </summary>
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

    /// <summary>
    /// The keys handed out down the switched-on list, as on the Mac
    /// (AppState.shortcutKeys): the number row, then punctuation nothing else
    /// uses, then the shifted number row. A vibescape past the end has no key.
    /// </summary>
    public const string ShortcutKeys = "1234567890-[];'\\,./`!@#$%^&*()";

    /// <summary>
    /// Upstream shader built-ins deliberately not listed. The Band needs
    /// instrument detection and per-player state the port doesn't have.
    /// </summary>
    public static IReadOnlySet<string> Excluded { get; } = new HashSet<string>(StringComparer.Ordinal) { "band" };

    /// <summary>Every vibescape, switched on or off, in the user's order.</summary>
    public IReadOnlyList<VibescapeListing> All { get; }

    /// <summary>The switched-on vibescapes, in order: the drop-down's rows, the keys, and what ←/→ step through.</summary>
    public IReadOnlyList<VibescapeEntry> Entries { get; }

    /// <summary>Whether the user has arranged the list differently from how it ships (the panel then offers Reset Order).</summary>
    public bool HasCustomOrder { get; }

    private readonly IReadOnlyList<VibescapeId> _shippedOrder;

    /// <param name="order">The user's arrangement (settings.json <c>order</c>), or null for the shipped one. Ids it doesn't name go after it; ids that no longer exist are skipped.</param>
    /// <param name="switches">Vibescapes switched on or off by the user (settings.json <c>switches</c>). Anything not named is on, except a custom vibescape whose own file says <c>enabled: false</c>.</param>
    public VibescapeCatalog(IEnumerable<CustomVibescape> customs, IReadOnlyList<VibescapeId>? order = null,
        IReadOnlyDictionary<VibescapeId, bool>? switches = null)
    {
        var shipped = Builtins.Select(b => (Entry: new VibescapeEntry(VibescapeId.Builtin(b.Slug), b.Title, BuiltinAuthor), FileOn: true)).ToList();
        // Names show in capitals, as upstream lists them.
        shipped.AddRange(customs.Select(c =>
            (new VibescapeEntry(VibescapeId.Custom(c.Id), c.Name.ToUpperInvariant(), c.Author, c.Shader), c.Enabled)));
        _shippedOrder = shipped.Select(s => s.Entry.Id).ToList();
        _fileOff = shipped.Where(s => !s.FileOn).Select(s => s.Entry.Id).ToHashSet();

        var byId = shipped.ToDictionary(s => s.Entry.Id);
        var arranged = new List<(VibescapeEntry Entry, bool FileOn)>();
        var used = new HashSet<VibescapeId>();
        foreach (var id in order ?? [])
            if (byId.TryGetValue(id, out var s) && used.Add(id)) arranged.Add(s);
        foreach (var s in shipped)
            if (used.Add(s.Entry.Id)) arranged.Add(s);

        All = arranged.Select(s => new VibescapeListing(s.Entry,
            s.Entry.Id == Default || (switches is not null && switches.TryGetValue(s.Entry.Id, out var on) ? on : s.FileOn))).ToList();
        Entries = All.Where(l => l.On).Select(l => l.Entry).ToList();
        HasCustomOrder = !All.Select(l => l.Entry.Id).SequenceEqual(_shippedOrder);
    }

    /// <summary>The switched-on vibescape a shortcut key picks, or null.</summary>
    public VibescapeEntry? ForShortcut(char key) =>
        ShortcutKeys.IndexOf(key) is var i and >= 0 && i < Entries.Count ? Entries[i] : null;

    /// <summary>The key that picks <paramref name="id"/>, or null when it is switched off or past the end of the keys.</summary>
    public char? ShortcutFor(VibescapeId id) =>
        IndexOf(id) is var i and >= 0 && i < ShortcutKeys.Length ? ShortcutKeys[i] : null;

    /// <summary>Where <paramref name="id"/> sits among the switched-on vibescapes, or -1.</summary>
    public int IndexOf(VibescapeId id)
    {
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Id == id) return i;
        return -1;
    }

    /// <summary>Any vibescape the app knows, switched on or not (one switched off while on screen stays on screen, as on the Mac).</summary>
    public VibescapeEntry? Find(VibescapeId id) => All.FirstOrDefault(l => l.Entry.Id == id)?.Entry;

    public bool IsOn(VibescapeId id) => All.Any(l => l.Entry.Id == id && l.On);

    /// <summary>The switched-on entry <paramref name="step"/> places after <paramref name="from"/>, wrapping at both ends. An id not in that list counts as the first.</summary>
    public VibescapeEntry Step(VibescapeId from, int step)
    {
        var i = Math.Max(0, IndexOf(from));
        var n = Entries.Count;
        return Entries[((i + step) % n + n) % n];
    }

    /// <summary>
    /// The order after dragging <paramref name="id"/> to position
    /// <paramref name="destination"/> in <see cref="All"/> (AppState.moveVibescape):
    /// the whole list moves, switched-on or not, so switching one off and on
    /// again doesn't shuffle it. Null means the result is the shipped order,
    /// which is then forgotten rather than pinned, so a later change to the
    /// shipped order still reaches someone who never really rearranged anything.
    /// </summary>
    public IReadOnlyList<VibescapeId>? OrderAfterMove(VibescapeId id, int destination)
    {
        var order = All.Select(l => l.Entry.Id).ToList();
        var from = order.IndexOf(id);
        if (from < 0) return CurrentOrder();
        order.RemoveAt(from);
        order.Insert(Math.Clamp(destination, 0, order.Count), id);
        return order.SequenceEqual(_shippedOrder) ? null : order;
    }

    /// <summary>The order as stored: null when it is the shipped one.</summary>
    public IReadOnlyList<VibescapeId>? CurrentOrder() => HasCustomOrder ? All.Select(l => l.Entry.Id).ToList() : null;

    /// <summary>
    /// The switches after setting <paramref name="id"/> on or off. The
    /// default vibescape can't be switched off. A switch that matches what the
    /// vibescape would be anyway is dropped, so settings.json only names real choices.
    /// </summary>
    public Dictionary<VibescapeId, bool> SwitchesWith(IReadOnlyDictionary<VibescapeId, bool>? switches, VibescapeId id, bool on)
    {
        var result = switches is null ? [] : new Dictionary<VibescapeId, bool>(switches);
        if (id == Default) return result;
        // Without a switch a built-in is on, and a custom one is whatever its file says.
        if (on == (!_fileOff.Contains(id))) result.Remove(id);
        else result[id] = on;
        return result;
    }

    private readonly HashSet<VibescapeId> _fileOff;
}
