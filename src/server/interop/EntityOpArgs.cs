namespace SourceSharp.Abi;

/// <summary>
/// THE ARGUMENTS OF ONE OP, in managed terms — the far side of
/// <see cref="OpArgs"/>.
///
/// <para>
/// <see cref="OpArgs"/> is the wire layout, all pointers and fixed-width words.
/// This is what the dispatch thunk turns it into: strings decoded, vectors
/// copied out, handles typed. The split is deliberate — the thunk decodes only
/// the fields the op it was given actually carries, so a think, which is by far
/// the most frequent op, decodes nothing at all.
/// </para>
///
/// <para>
/// An ordinary <c>readonly struct</c> and NOT a <c>ref struct</c>, which is the
/// one place these two types differ. <see cref="EntityRef"/> holds a span over
/// memory the shell owns, so escaping it has to be a compile error; everything
/// here has already been copied out of the native call — the strings are
/// managed strings, the vectors are values — so it is safe to hold, and making
/// it unholdable would only stop a test from building one.
/// </para>
/// </summary>
public readonly struct EntityOpArgs
{
    /// <summary>
    /// The other entity: a toucher, an attacker, a blocker, an activator, a
    /// filter's subject. <see cref="EntityHandle.None"/> when the op has none.
    /// </summary>
    public EntityHandle Other { get; init; }

    /// <summary>The classname, for the two ops that arrive before the instance exists.</summary>
    public string ClassName { get; init; }

    /// <summary>A keyvalue's key; an input's name.</summary>
    public string Text1 { get; init; }

    /// <summary>A keyvalue's value; an input's value as text.</summary>
    public string Text2 { get; init; }

    /// <summary>Damage: how much. Input: the value converted to a number.</summary>
    public float Number { get; init; }

    /// <summary>Damage: the DMG_ word. FilterPasses: the damage type.</summary>
    public int Flags { get; init; }

    /// <summary>Damage: where the blow landed. Zero when it had no place.</summary>
    public Vector Position { get; init; }

    /// <summary>Damage: which way it was going. Zero when it had none.</summary>
    public Vector Force { get; init; }

    /// <summary>
    /// FilterPasses: whether the question is "would this damage pass" rather
    /// than the plain one. False for every other op.
    /// </summary>
    public bool IsDamageQuestion { get; init; }

    /// <summary>Everything empty. What an op with no arguments is dispatched with.</summary>
    public static EntityOpArgs None => new()
    {
        Other = EntityHandle.None,
        ClassName = string.Empty,
        Text1 = string.Empty,
        Text2 = string.Empty,
    };

    /// <summary>Just the other entity, which is what most ops carry.</summary>
    public static EntityOpArgs With(EntityHandle other) => None with { Other = other };
}
