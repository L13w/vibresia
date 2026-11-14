namespace Vibresia.Core.Ui;

/// <summary>Tells the frame something opens from the frames it stays open (the vibescape list rescans the custom folder only as it opens).</summary>
public sealed class OpenEdge
{
    private bool _wasOpen;

    /// <returns>True only on the call where <paramref name="open"/> turns true.</returns>
    public bool Update(bool open)
    {
        var opened = open && !_wasOpen;
        _wasOpen = open;
        return opened;
    }
}
