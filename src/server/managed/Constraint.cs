namespace SourceSharp.Abi;

/// <summary>
/// The kinds of constraint the shell can build. Must match
/// the parameter blocks PhysicsConstraints builds (shell_constraints.cpp's
/// SourceSharpConstraintKind_t until plan_physics_bones_native_zero 2.4).
///
/// The first four take a frame of reference that is simply a point and an axis.
/// The last three do not: each has to finish the job by reading where its two
/// objects currently ARE — a slide projects its travel limits onto its axis, a
/// pulley measures the rope already strung, a ragdoll joint is expressed
/// relative to both ends as they stand. That is why those three are given a
/// frame and left to measure it rather than handed finished numbers.
/// </summary>
public enum ConstraintKind
{
    /// <summary>Welds two objects together in their current relative pose.</summary>
    Fixed = 0,

    /// <summary>Lets one turn about an axis. A door, a lever, a wheel.</summary>
    Hinge = 1,

    /// <summary>Lets one turn freely about a point. A pendulum.</summary>
    BallSocket = 2,

    /// <summary>Keeps two points within a distance of each other. A rope.</summary>
    Length = 3,

    /// <summary>Lets one slide along a line. A lift, a drawer, a sliding door.</summary>
    Sliding = 4,

    /// <summary>Two objects on one rope over two pulleys. A counterweight.</summary>
    Pulley = 5,

    /// <summary>A joint with per-axis angle limits and friction. A limb.</summary>
    Ragdoll = 6,
}

/// <summary>
/// A relationship between two entities.
///
/// This is the first thing in the managed API that is not an entity, and it is
/// worth being clear why it cannot be one. A constraint has two ends, is owned
/// by neither of them, and stops meaning anything once either goes away. A
/// class holding one handle cannot express that, and a field on one of the two
/// ends would quietly make that end the owner.
///
/// So it is a handle into a native table. The solver stays in VPhysics, which
/// is right: game code decides that a door is hinged, not how a hinge
/// integrates.
///
/// <para>
/// Lifetime: destroyed explicitly, or dropped wholesale at level change. There
/// is deliberately no watching of the far end — VPhysics already severs a
/// constraint whose object is destroyed, which is why stock Source destroys the
/// constraint with the entity that made it and never watches the other side.
/// </para>
/// </summary>
public sealed class Constraint
{
    private Constraint(IEngineConstraints engine, PhysicsId id, ConstraintKind kind)
    {
        _engine = engine;
        Id = id;
        Kind = kind;
    }

    /// <summary>
    /// The native id, with the serial that makes a stale one answer nothing
    /// rather than answer for whatever took its slot. See <see cref="PhysicsId"/>
    /// for why this is an id and not an index.
    /// </summary>
    public PhysicsId Id { get; private set; }

    public ConstraintKind Kind { get; }

    /// <summary>
    /// The side's constraint table, handed over by whoever made this joint —
    /// the entity that created it, from its own composition. Kept per instance
    /// so two game instances in one process each talk to their own engine.
    /// </summary>
    private readonly IEngineConstraints _engine;

    /// <summary>
    /// Whether the constraint still exists. Asks native rather than trusting a
    /// local flag, because a level change drops the whole table.
    /// </summary>
    public bool IsValid => !Id.IsNone && _engine.IsValid(Id.Value);

    /// <summary>
    /// Wraps a constraint the SOLVER still holds: the reload spelling of
    /// <see cref="Make"/>.
    ///
    /// <para>
    /// A hot reload rebuilds the managed entity but leaves the native table
    /// standing, so the joint an entity made before the reload is still
    /// holding — and the rebuilt entity must be able to break it. Stock
    /// re-wires the same pointers for the same reason: CPhysMagnet's
    /// m_MagnettedEntities constraint pointers are saved fields re-found in
    /// OnRestore (physobj.cpp:1494-1512), and the phys_* constraints hold
    /// DEFINE_PHYSPTR m_pConstraint rebuilt in Activate(). The managed
    /// spelling is a <c>[SaveField]</c> leaf carrying the <see cref="Id"/> and
    /// this call putting the wrapper back around it.
    /// </para>
    ///
    /// <para>
    /// It makes no call. A stale id — one the level change already dropped —
    /// is not detected here: <see cref="IsValid"/> asks native on every read,
    /// so an adopted-and-gone joint answers false exactly as a made-then-
    /// dropped one would, and <see cref="Destroy"/> on it is the ordinary
    /// already-destroyed case.
    /// </para>
    /// </summary>
    public static Constraint Adopt(IEngineConstraints engine, PhysicsId id, ConstraintKind kind)
        => new(engine, id, kind);

    /// <summary>
    /// Welds two entities together as they currently stand. An end left unset
    /// is the world, which pins the other one in place.
    /// </summary>
    public static Constraint? Fixed(IEngineConstraints engine, EntityHandle a, EntityHandle b,
                                    float forceLimit = 0f, float torqueLimit = 0f)
        => Make(engine, a, b, ConstraintKind.Fixed, stackalloc float[] { forceLimit, torqueLimit });

    /// <summary>
    /// A hinge about an axis through <paramref name="axisPoint"/>.
    ///
    /// Friction is in the engine's units, where a usable value is a small
    /// number and the shell scales it — the same scaling stock Source applies,
    /// so a mapper's number means here what it means in Hammer.
    ///
    /// The two ends colliding with each other is the caller's to stop, in
    /// <see cref="EntityPairCollisions"/> (PhysHinge does, for the family's
    /// "do not collide" flag; plan_physics_bones_native_zero 2.1).
    /// </summary>
    public static Constraint? Hinge(IEngineConstraints engine, EntityHandle a, EntityHandle b,
                                    Vector axisPoint, Vector axisDirection,
                                    float friction = 0f,
                                    float forceLimit = 0f, float torqueLimit = 0f,
                                    bool assumeWorldGeometry = false)
        => Make(engine, a, b, ConstraintKind.Hinge, stackalloc float[]
        {
            forceLimit, torqueLimit,
            axisPoint.X, axisPoint.Y, axisPoint.Z,
            axisDirection.X, axisDirection.Y, axisDirection.Z,
            friction,
            assumeWorldGeometry ? 1f : 0f,
        });

    /// <summary>Free rotation about a single world point.</summary>
    public static Constraint? BallSocket(IEngineConstraints engine, EntityHandle a, EntityHandle b, Vector anchor,
                                         float forceLimit = 0f, float torqueLimit = 0f)
        => Make(engine, a, b, ConstraintKind.BallSocket, stackalloc float[]
        {
            forceLimit, torqueLimit,
            anchor.X, anchor.Y, anchor.Z,
        });

    /// <summary>
    /// Holds two world points within a distance of each other — a rope, or a
    /// rigid link when the minimum equals the total.
    /// </summary>
    public static Constraint? Length(IEngineConstraints engine, EntityHandle a, EntityHandle b,
                                     Vector pointOnA, Vector pointOnB,
                                     float minLength = 0f, float extraLength = 0f,
                                     float forceLimit = 0f, float torqueLimit = 0f)
        => Make(engine, a, b, ConstraintKind.Length, stackalloc float[]
        {
            forceLimit, torqueLimit,
            pointOnA.X, pointOnA.Y, pointOnA.Z,
            pointOnB.X, pointOnB.Y, pointOnB.Z,
            minLength, extraLength,
        });

    /// <summary>
    /// A slide along the line from <paramref name="axisStart"/> to
    /// <paramref name="axisEnd"/> — a lift, a drawer, a sliding door.
    ///
    /// With <paramref name="limitToEnds"/> the travel stops at those two points.
    /// The stops are worked out natively by projecting both onto the axis and
    /// measuring from where the attached object currently stands, which is also
    /// what lets an object that spawned past its own limits stay put rather than
    /// being shoved to the nearest end.
    ///
    /// The axis is snapped to the nearest world axis, as stock does: a slide a
    /// fraction off true drifts sideways over its whole travel.
    /// </summary>
    public static Constraint? Sliding(IEngineConstraints engine, EntityHandle a, EntityHandle b,
                                      Vector axisStart, Vector axisEnd,
                                      float friction = 0f, bool limitToEnds = false,
                                      float forceLimit = 0f, float torqueLimit = 0f)
        => Make(engine, a, b, ConstraintKind.Sliding, stackalloc float[]
        {
            forceLimit, torqueLimit,
            axisStart.X, axisStart.Y, axisStart.Z,
            axisEnd.X, axisEnd.Y, axisEnd.Z,
            friction, limitToEnds ? 1f : 0f,
        });

    /// <summary>
    /// Two objects hung on one rope passing over two pulleys — a counterweight,
    /// a block and tackle.
    ///
    /// The offsets are in each object's OWN space, so zero is its centre, which
    /// is what a map means when it names a target rather than an attachment
    /// point. The rope's length is measured from where the objects currently
    /// hang plus <paramref name="extraRope"/>; giving a length instead would
    /// yank both of them on the first tick if it disagreed.
    ///
    /// <paramref name="gearRatio"/> multiplies the second span, so 2 reels in
    /// twice what it pays out. <paramref name="rigid"/> makes the rope resist
    /// being shortened as well as lengthened.
    /// </summary>
    public static Constraint? Pulley(IEngineConstraints engine, EntityHandle a, EntityHandle b,
                                     Vector pulleyA, Vector pulleyB,
                                     Vector offsetOnA = default, Vector offsetOnB = default,
                                     float extraRope = 0f, float gearRatio = 1f,
                                     bool rigid = false,
                                     float forceLimit = 0f, float torqueLimit = 0f)
        => Make(engine, a, b, ConstraintKind.Pulley, stackalloc float[]
        {
            forceLimit, torqueLimit,
            pulleyA.X, pulleyA.Y, pulleyA.Z,
            pulleyB.X, pulleyB.Y, pulleyB.Z,
            offsetOnA.X, offsetOnA.Y, offsetOnA.Z,
            offsetOnB.X, offsetOnB.Y, offsetOnB.Z,
            extraRope, gearRatio, rigid ? 1f : 0f,
        });

    /// <summary>
    /// A joint with a limited swing about each of its own three axes, and
    /// friction on each — a limb, a jaw, a hinged panel that must not fold flat.
    ///
    /// The limits are degrees about the JOINT's axes, not the world's, which is
    /// what <paramref name="jointOrigin"/> and <paramref name="jointAngles"/>
    /// define. Native expresses that frame relative to both ends as they
    /// currently stand, which is why the joint is given rather than computed
    /// here.
    ///
    /// With <paramref name="limitAnglesOnly"/> the two ends may also drift apart;
    /// without it the joint holds them together as well.
    ///
    /// The friction numbers are in different units from
    /// <see cref="Hinge"/>'s. That is stock's inconsistency, kept so a mapper's
    /// number means the same here as there.
    /// </summary>
    public static Constraint? Ragdoll(IEngineConstraints engine, EntityHandle a, EntityHandle b,
                                      Vector jointOrigin, Vector jointAngles,
                                      Vector minAngles = default, Vector maxAngles = default,
                                      Vector friction = default,
                                      bool limitAnglesOnly = false, bool startActive = true,
                                      float forceLimit = 0f, float torqueLimit = 0f)
        => Make(engine, a, b, ConstraintKind.Ragdoll, stackalloc float[]
        {
            forceLimit, torqueLimit,
            jointOrigin.X, jointOrigin.Y, jointOrigin.Z,
            jointAngles.X, jointAngles.Y, jointAngles.Z,
            minAngles.X, maxAngles.X,
            minAngles.Y, maxAngles.Y,
            minAngles.Z, maxAngles.Z,
            friction.X, friction.Y, friction.Z,
            limitAnglesOnly ? 1f : 0f, startActive ? 1f : 0f,
        });

    /// <summary>
    /// CPhysHinge::InputSetVelocity: see <see cref="IEngineConstraints.SetHingeVelocity"/>.
    /// Ignored by every other kind: a motor needs an axis to turn about.
    /// </summary>
    public void SetHingeVelocity(float degreesPerSecond, float systemLoadScale)
        => _engine.SetHingeVelocity(Id.Value, degreesPerSecond, systemLoadScale);

    /// <summary>
    /// Drives a joint ALONG its axis instead of about one — a winch, a lift, a
    /// powered slide. Speed is units per second, and a negative one runs it back.
    ///
    /// <paramref name="systemLoadScale"/> scales only the FORCE the motor will
    /// spend, which is why it is a separate number rather than folded into the
    /// speed: what it multiplies is the average mass of whichever ends can
    /// actually move, and only the engine can weigh those.
    /// </summary>
    public void SetLinearMotor(float unitsPerSecond, float systemLoadScale = 1f)
        => _engine.SetLinearMotor(Id.Value, unitsPerSecond, systemLoadScale);

    /// <summary>Stops driving along the axis, leaving the joint free to coast.</summary>
    public void StopLinearMotor() => SetLinearMotor(0f);

    /// <summary>
    /// Tears the constraint down. Safe to call twice, and safe after a level
    /// change has already dropped it.
    /// </summary>
    public void Destroy()
    {
        if (Id.IsNone)
            return;

        _engine.Destroy(Id.Value);

        // Dropped here as well as freed natively. The native side has already
        // advanced the slot's serial, so this id is dead either way -- but a
        // second Destroy would then spend a crossing asking about a slot that
        // may since have been given to somebody else's constraint.
        Id = PhysicsId.None;
    }

    private static Constraint? Make(IEngineConstraints engine, EntityHandle a, EntityHandle b,
                                    ConstraintKind kind, ReadOnlySpan<float> parameters)
    {
        var id = new PhysicsId(engine.Create(a, b, (int)kind, parameters));
        return id.IsNone ? null : new Constraint(engine, id, kind);
    }
}
