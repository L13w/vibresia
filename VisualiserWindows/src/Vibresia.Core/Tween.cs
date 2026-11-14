namespace Vibresia.Core;

/// <summary>The two SwiftUI animation curves the Mac's interface uses.</summary>
public enum Easing { InOut, Out }

/// <summary>
/// A SwiftUI-style animation: from where it is to a new target over a fixed
/// time. Retargeting mid-flight starts the new animation from the current
/// value, as SwiftUI does, so nothing jumps.
/// </summary>
public sealed class Tween(double seconds, Easing easing, double initial = 0)
{
    private double _from = initial, _to = initial, _t = 1;

    public double Value { get; private set; } = initial;

    /// <summary>Whether it has reached its target.</summary>
    public bool Settled => _t >= 1;

    public void Target(double to)
    {
        if (to == _to) return;
        _from = Value;
        _to = to;
        _t = 0;
    }

    public void Snap(double to)
    {
        if (_t < 1) { _to = to; return; } // let a running animation finish, towards the latest value
        _from = _to = Value = to;
    }

    public void Advance(double dt)
    {
        _t = Math.Min(1, _t + dt / seconds);
        var k = easing == Easing.InOut
            ? (_t < 0.5 ? 2 * _t * _t : 1 - Math.Pow(-2 * _t + 2, 2) / 2)
            : 1 - (1 - _t) * (1 - _t);
        Value = _from + (_to - _from) * k;
    }
}
