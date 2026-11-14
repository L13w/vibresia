namespace Vibresia.Core.Vibescapes;

/// <summary>
/// Names something the app can draw: a built-in (by its upstream slug, e.g.
/// "kaleidoscope") or a custom vibescape (by its JSON id). The string form
/// is what settings.json stores; the prefix keeps a custom vibescape from ever
/// colliding with a built-in that shares its slug.
/// </summary>
public readonly record struct VibescapeId(bool IsCustom, string Slug)
{
    private const string BuiltinPrefix = "builtin:";
    private const string CustomPrefix = "custom:";

    public static VibescapeId Builtin(string slug) => new(false, slug);
    public static VibescapeId Custom(string id) => new(true, id);

    public override string ToString() => (IsCustom ? CustomPrefix : BuiltinPrefix) + Slug;

    /// <returns>Null for anything that isn't a well-formed id (an empty slug, or no known prefix).</returns>
    public static VibescapeId? Parse(string? text)
    {
        if (text is null) return null;
        if (text.StartsWith(BuiltinPrefix, StringComparison.Ordinal) && text.Length > BuiltinPrefix.Length)
            return Builtin(text[BuiltinPrefix.Length..]);
        if (text.StartsWith(CustomPrefix, StringComparison.Ordinal) && text.Length > CustomPrefix.Length)
            return Custom(text[CustomPrefix.Length..]);
        return null;
    }
}

/// <summary>One row in the vibescape list, built-in or custom.</summary>
/// <param name="CustomBody">The GLSL body of a custom vibescape; null for a built-in.</param>
public sealed record VibescapeEntry(VibescapeId Id, string Title, string Author, string? CustomBody = null)
{
    /// <summary>Whether the palette grade is applied to this vibescape's frame (upstream skips the pixel-art ones).</summary>
    public bool Graded => Id.IsCustom || !Palette.UngradedBuiltins.Contains(Id.Slug);
}
