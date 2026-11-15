using Vibresia.Core.Vibescapes;

namespace Vibresia.Core.Ui;

/// <summary>
/// The user's vibescape list as the app keeps it: the custom vibescapes on
/// disk, arranged and switched by what settings.json says (the View All
/// panel's order and switches). Every change goes into the settings; the
/// caller saves them.
/// </summary>
public sealed class VibescapeList
{
    private readonly Settings _settings;
    private IReadOnlyList<CustomVibescape> _customs;

    public VibescapeCatalog Catalog { get; private set; }

    public VibescapeList(Settings settings, IReadOnlyList<CustomVibescape> customs)
    {
        _settings = settings;
        _customs = customs;
        Catalog = Build();
    }

    /// <summary>The custom vibescapes were read again (a file may have been added, changed or removed).</summary>
    public void Rescan(IReadOnlyList<CustomVibescape> customs)
    {
        _customs = customs;
        Catalog = Build();
    }

    /// <summary>
    /// Picking a switched-off vibescape (from the View All panel) switches it
    /// back on, as on the Mac, so it isn't chosen yet missing from the list.
    /// </summary>
    /// <returns>Whether the settings changed.</returns>
    public bool SwitchOnToPick(VibescapeId id) => !Catalog.IsOn(id) && Switch(id, true);

    /// <returns>Whether the settings changed.</returns>
    public bool Switch(VibescapeId id, bool on) => Apply(_settings.Order, Catalog.SwitchesWith(_settings.Switches, id, on));

    /// <returns>Whether the settings changed.</returns>
    public bool Move(VibescapeId id, int destination) => Apply(Catalog.OrderAfterMove(id, destination), _settings.Switches);

    /// <returns>Whether the settings changed.</returns>
    public bool ResetOrder() => Apply(null, _settings.Switches);

    private bool Apply(IReadOnlyList<VibescapeId>? order, IReadOnlyDictionary<VibescapeId, bool> switches)
    {
        var changed = !SameOrder(order, _settings.Order) || !SameSwitches(switches, _settings.Switches);
        _settings.Order = order;
        _settings.Switches = switches;
        Catalog = Build();
        return changed;
    }

    private VibescapeCatalog Build() => new(_customs, _settings.Order, _settings.Switches);

    private static bool SameOrder(IReadOnlyList<VibescapeId>? a, IReadOnlyList<VibescapeId>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);

    private static bool SameSwitches(IReadOnlyDictionary<VibescapeId, bool> a, IReadOnlyDictionary<VibescapeId, bool> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
}
