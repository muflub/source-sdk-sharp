namespace SourceSharp.Ai;

/// <summary>
/// An actor: a player slot and the serial of the life that holds it. The
/// serial makes an intent for a bot that has since been removed and replaced
/// in the same slot unmistakable (plan_hl2mp_ai §2.4: the applier checks it).
/// <c>default</c> is <see cref="None"/>: no actor has serial 0.
/// </summary>
public readonly record struct ActorId(int Slot, int Serial)
{
    public static ActorId None => default;

    public bool IsValid => Serial != 0;

    public override string ToString() => IsValid ? $"actor {Slot}#{Serial}" : "no actor";
}

/// <summary>
/// A cell of the nav graph. Stored one above the index, so <c>default</c> is
/// <see cref="None"/> and cell 0 has to be named on purpose.
/// </summary>
public readonly record struct CellId
{
    private readonly int _indexPlusOne;

    public CellId(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        _indexPlusOne = index + 1;
    }

    public static CellId None => default;

    public bool IsValid => _indexPlusOne != 0;

    /// <summary>The index, or -1 for <see cref="None"/>.</summary>
    public int Index => _indexPlusOne - 1;

    public override string ToString() => IsValid ? $"cell {Index}" : "no cell";
}

/// <summary>A server tick: the unit every AI input and output is keyed by.</summary>
public readonly record struct Tick(int Value) : IComparable<Tick>
{
    public int CompareTo(Tick other) => Value.CompareTo(other.Value);

    public static Tick operator +(Tick t, int ticks) => new(t.Value + ticks);

    public static int operator -(Tick a, Tick b) => a.Value - b.Value;

    public static bool operator <(Tick a, Tick b) => a.Value < b.Value;

    public static bool operator >(Tick a, Tick b) => a.Value > b.Value;

    public static bool operator <=(Tick a, Tick b) => a.Value <= b.Value;

    public static bool operator >=(Tick a, Tick b) => a.Value >= b.Value;

    public override string ToString() => $"tick {Value}";
}
