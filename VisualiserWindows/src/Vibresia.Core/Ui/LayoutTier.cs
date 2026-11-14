namespace Vibresia.Core.Ui;

/// <summary>
/// How much of the header there is room for, by the window's width in
/// points (upstream AppState.swift LayoutTier). The pill labels go first,
/// then the pills; what is left at the narrowest is the brand and the picture.
/// </summary>
public enum LayoutTier
{
    Wide,
    Compact,
    Narrow,
    Tiny,
}

public static class Layout
{
    public static LayoutTier ForWidth(double points) => points switch
    {
        >= 1100 => LayoutTier.Wide,
        >= 880 => LayoutTier.Compact,
        >= 620 => LayoutTier.Narrow,
        _ => LayoutTier.Tiny,
    };

    public static bool ShowsPillText(this LayoutTier tier) => tier <= LayoutTier.Compact;
    public static bool ShowsPills(this LayoutTier tier) => tier <= LayoutTier.Narrow;
}
