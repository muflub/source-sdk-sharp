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

    /// <summary>
    /// The serial bit an alias carries (plan_bots_teams_tf2 C.3, the Spy's honesty fix): a body a client draws as someone
    /// it is not (a disguised Spy, to his enemies) is an actor of its own, one per disguise, so nothing a bot remembers of
    /// the real player reaches the disguise. Internal: the memory keeps an alias's row apart, and no brain can ask. The
    /// sign bit, which no other producer of serials sets: a bot's is a small counter, a person's the 0x4000_0000 mark over
    /// its handle, and a recording's anonymised person's 31 bits at most (ActorAnonymiser masks with 0x7FFF_FFFF). Bit 29,
    /// first used here, was set for half of all anonymised people, whose replays then saw them as aliases (review F1).
    /// </summary>
    internal const int AliasSerialBit = int.MinValue;

    /// <summary>Whether this is an alias (<see cref="Alias"/>): the memory's question only.</summary>
    internal bool IsAlias => Serial < 0;

    /// <summary>
    /// Alias number <paramref name="instance"/> (1 up), drawn in slot <paramref name="slot"/>: the binding names one per
    /// disguise of a player, shared by every viewer the disguise fools (the review's "per (Spy, disguise instance)").
    /// </summary>
    public static ActorId Alias(int slot, int instance) => new(slot, AliasSerialBit | (Math.Max(1, instance) & 0x0FFF_FFFF));

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
