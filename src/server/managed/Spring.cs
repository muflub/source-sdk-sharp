namespace SourceSharp.Abi;

/// <summary>
/// A force pulling two entities towards a separation, without insisting on it.
///
/// The same shape as a <see cref="Constraint"/> — two ends, owned by neither,
/// meaningless once either goes away — and deliberately a separate type,
/// because it is not a constraint. A constraint says where an object MAY be. A
/// spring says only what force pulls it somewhere, and forbids nothing: it can
/// be stretched to any length, it passes through whatever is in the way, and it
/// occupies no space at all. Stretch it far enough and the force is unbounded.
///
/// <para>
/// The separate native table is not tidiness either. VPhysics keeps springs in
/// their own list with their own destroy call, so a handle that resolved into
/// the wrong table would hand a spring pointer to DestroyConstraint.
/// </para>
/// </summary>
public sealed class Spring
{
    private Spring(IEngineSprings engine, PhysicsId id)
    {
        _engine = engine;
        Id = id;
    }

    /// <summary>
    /// The side's spring table, handed over by whoever made this spring — the
    /// entity that created it, from its own composition. Kept per instance so
    /// two game instances in one process each talk to their own engine.
    /// </summary>
    private readonly IEngineSprings _engine;

    /// <summary>
    /// The native id, with the serial that makes a stale one answer nothing
    /// rather than answer for whatever took its slot. See <see cref="PhysicsId"/>
    /// for why this is an id and not an index.
    /// </summary>
    public PhysicsId Id { get; private set; }

    /// <summary>
    /// Whether it still exists. Asks native rather than trusting a local flag,
    /// because a level change drops the whole table.
    /// </summary>
    public bool IsValid => !Id.IsNone && _engine.IsValid(Id.Value);

    /// <summary>
    /// Puts a wrapper back around a spring that already exists natively, by
    /// its saved id: the reload spelling of <see cref="Make"/>.
    ///
    /// <para>
    /// A cs_sv_reload swaps the managed assembly but leaves the native spring
    /// table standing, so the spring an entity made before the reload is still
    /// pulling, and the rebuilt entity must be able to retune and destroy it.
    /// Stock carries the pointer for the same reason (phys_spring's
    /// DEFINE_PHYSPTR m_pSpring, physobj.cpp:91). The managed spelling is
    /// a <c>[SaveField]</c> leaf carrying the <see cref="Id"/> and this call.
    /// </para>
    ///
    /// <para>
    /// It makes no call. A stale id is not detected here: <see cref="IsValid"/>
    /// asks native on every read, so an adopted-and-gone spring answers false
    /// exactly as a made-then-dropped one would.
    /// </para>
    /// </summary>
    public static Spring Adopt(IEngineSprings engine, PhysicsId id) => new(engine, id);

    /// <summary>
    /// Joins two entities. An end left unset is the world, which anchors the
    /// other one to a fixed point rather than to something that moves.
    ///
    /// The endpoints are world positions, one per end, and which belongs to
    /// which is worked out natively — it needs each object's centre, and the
    /// world's has no entity to ask. Getting it backwards is not subtly wrong:
    /// each object would be pulled towards the other's anchor.
    /// </summary>
    /// <param name="constant">
    /// Stiffness. Higher pulls harder for the same stretch, and high enough
    /// makes the simulation ring rather than settle.
    /// </param>
    /// <param name="naturalLength">
    /// The separation at which the spring pulls with no force at all. Shortening
    /// it below the current distance is how a map winches something in.
    /// </param>
    /// <param name="onlyStretch">
    /// Pull but never push: the spring goes slack when compressed, which is what
    /// makes it a rope rather than a strut.
    /// </param>
    public static Spring? Make(IEngineSprings engine, EntityHandle a, EntityHandle b,
                               Vector start, Vector end,
                               float constant, float damping, float relativeDamping,
                               float naturalLength, bool onlyStretch = false)
    {
        var id = new PhysicsId(engine.Create(a, b, start, end, constant, damping,
                                             relativeDamping, naturalLength, onlyStretch));

        return id.IsNone ? null : new Spring(engine, id);
    }

    public void SetConstant(float constant) => _engine.SetConstant(Id.Value, constant);

    public void SetDamping(float damping) => _engine.SetDamping(Id.Value, damping);

    /// <summary>Sets the rest separation — see <see cref="Make"/>.</summary>
    public void SetLength(float length) => _engine.SetLength(Id.Value, length);

    /// <summary>
    /// Tears it down. Safe to call twice, and safe after a level change has
    /// already dropped it.
    /// </summary>
    public void Destroy()
    {
        if (Id.IsNone)
            return;

        _engine.Destroy(Id.Value);
        Id = PhysicsId.None;
    }
}
