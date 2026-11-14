namespace Vibresia.Core.Vibescapes;

/// <summary>
/// The vibescape list's keyboard state: whether it is open and which row is
/// highlighted. Kept apart from drawing so the key behaviour can be tested.
/// </summary>
public sealed class PickerState
{
    public bool IsOpen { get; private set; }
    public int Selected { get; private set; }
    public int Count { get; private set; }

    /// <summary>Set when the highlight moved by keyboard, so the list scrolls to it once.</summary>
    public bool ScrollToSelected { get; set; }

    /// <param name="current">The row to highlight first (the vibescape showing now); out of range means the first row.</param>
    public void Open(int count, int current)
    {
        Count = count;
        Selected = current >= 0 && current < count ? current : 0;
        IsOpen = count > 0;
        ScrollToSelected = true;
    }

    public void Close() => IsOpen = false;

    /// <summary>Moves the highlight, wrapping at both ends.</summary>
    public void Move(int step)
    {
        if (!IsOpen || Count == 0) return;
        Selected = ((Selected + step) % Count + Count) % Count;
        ScrollToSelected = true;
    }
}
