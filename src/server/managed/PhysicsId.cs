namespace SourceSharp.Abi;

/// <summary>
/// An id for one of the things VPhysics owns and this port refers to by name:
/// a physics object, a constraint, a spring, a motion controller, a shadow
/// controller (<c>plan_entity.md</c> §3.8, step 3b).
///
/// <para>
/// <b>Why it is not an index.</b> Every one of these used to be the index PLUS
/// ONE into a native vector that only ever grew, with retired rows blanked and
/// never reused. That is safe exactly as long as nothing reuses a row, and it
/// costs one dead row per constraint the level ever built. The moment a row IS
/// reused — which the object table must, because VPhysics destroys objects
/// without telling anybody — a map holding an id past the death of the thing it
/// named gets the row's NEW occupant back. A <c>phys_thruster</c> pushing
/// somebody else's crate, with no error anywhere.
/// </para>
///
/// <para>
/// So an id carries the generation the slot was in when the id was made, and a
/// stale id resolves to nothing. It is the same construction, and the same
/// remedy, as <see cref="EntityHandle"/>'s serial one level up.
/// </para>
///
/// <para>
/// <b>Zero is not a valid id, and that is by construction rather than by
/// decree.</b> The low field holds the slot index PLUS ONE, so the smallest
/// value a real id can carry is 1. This matters because the entity side has the
/// opposite shape and it has already caught two lanes out: a default
/// <see cref="EntityHandle"/> is entry 0, which is a REAL slot — worldspawn —
/// and the invalid one is a named sentinel. Here <see cref="None"/> is a word
/// the native allocator cannot produce.
/// </para>
///
/// <para>
/// <b>The kind is not in the id.</b> One native table holds all five kinds and
/// every lookup names the kind it expects, so a spring id handed to a
/// constraint call resolves to nothing rather than into another table's live
/// row. Managed code keeps the kinds apart by type instead — a
/// <see cref="Constraint"/> holds its own id and never hands it anywhere else.
/// </para>
/// </summary>
public readonly struct PhysicsId : System.IEquatable<PhysicsId>
{
    /// <summary>
    /// How the word is split, mirrored from <c>SS_PHYSID_INDEX_BITS</c> in
    /// <c>sourcesharp/game/server/physics_shell.h</c> and pinned to it by
    /// <c>PhysicsIdTests</c> — a stand-in for native drifts silently, so the
    /// agreement is asserted rather than assumed.
    /// </summary>
    public const int IndexBits = 12;

    /// <inheritdoc cref="IndexBits"/>
    public const int SerialBits = 20;

    /// <summary>The low field's mask. <c>SS_PHYSID_INDEX_MASK</c>.</summary>
    public const uint IndexMask = (1u << IndexBits) - 1u;

    /// <summary>The high field's mask. <c>SS_PHYSID_SERIAL_MASK</c>.</summary>
    public const uint SerialMask = (1u << SerialBits) - 1u;

    /// <summary>
    /// How many slots the native table has: the largest index+1 the low field
    /// can hold. <c>SS_PHYSID_MAX_SLOTS</c>.
    /// </summary>
    public const int MaxSlots = (int)IndexMask;

    /// <summary>
    /// No id. Zero — and see the type's own remarks for why zero is safe here
    /// where it is a trap on the entity side.
    /// </summary>
    public static PhysicsId None => default;

    public PhysicsId(uint value) => Value = value;

    /// <summary>The word native issued, as it is passed back over the ABI.</summary>
    public uint Value { get; }

    /// <summary>
    /// The slot, or -1 when this is not an id native issued. Never a valid
    /// subscript for <see cref="None"/>.
    /// </summary>
    public int Index => (Value & IndexMask) == 0 ? -1 : (int)((Value & IndexMask) - 1u);

    /// <summary>
    /// The slot's generation when this id was made. The whole of what separates
    /// a live id from a stale one naming the same slot.
    /// </summary>
    public uint Serial => (Value >> IndexBits) & SerialMask;

    /// <summary>Whether this names nothing at all.</summary>
    public bool IsNone => Value == 0u;

    /// <summary>
    /// Builds an id from its two fields. For tests and for diagnostics; nothing
    /// in the game makes an id, because only the native table may allocate one.
    /// Answers <see cref="None"/> for a slot the table does not have.
    /// </summary>
    public static PhysicsId Make(int index, uint serial)
        => index < 0 || index >= MaxSlots
            ? None
            : new PhysicsId(((serial & SerialMask) << IndexBits) | (uint)(index + 1));

    public bool Equals(PhysicsId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is PhysicsId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(PhysicsId a, PhysicsId b) => a.Equals(b);

    public static bool operator !=(PhysicsId a, PhysicsId b) => !a.Equals(b);

    /// <summary>
    /// Slot and generation rather than the raw word, because the raw word is
    /// unreadable and the two fields are what a log is ever asking about.
    /// </summary>
    public override string ToString()
        => IsNone ? "none" : $"#{Index}:{Serial}";
}
