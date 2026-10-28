namespace SourceSharp.Abi;

/// <summary>
/// The kinds of motion controller native can build. Must match
/// MotionState's kinds in PhysicsMotionControllers (shell_motioncontrollers.cpp's
/// SourceSharpMotionKind_t until plan_physics_bones_native_zero 2.4).
/// </summary>
/// <summary>
/// Which halves of a decomposed force to keep. A thruster can be told to shove
/// without turning, or to turn without shoving, and both fall out of the same
/// decomposition.
/// </summary>
[Flags]
public enum ForceComponents
{
    None = 0,
    Push = 1,
    Twist = 2,
    Both = Push | Twist,
}

public enum MotionControllerKind
{
    /// <summary>A steady push and twist. Thrusters and torque entities.</summary>
    ConstantForce = 0,

    /// <summary>Drives something to a target angular speed about an axis.</summary>
    Motor = 1,

    /// <summary>Rights something that has been knocked over.</summary>
    KeepUpright = 2,

    /// <summary>
    /// A medium that everything inside it moves through — water, wind, a jet of
    /// air, a slow-motion field. Unlike the other three this belongs to a
    /// VOLUME rather than to one object, so it starts with nothing attached and
    /// gains and loses objects as they come and go.
    /// </summary>
    Medium = 3,

    /// <summary>
    /// A controller whose Simulate is a managed object's own
    /// (<see cref="SourceSharp.Server.IMotionSimulate"/>): the physcannon's grab
    /// controller, stock's CGrabController::Simulate. Made only through
    /// <c>PhysicsMotionControllers.CreateCustom</c>, never by kind number.
    /// </summary>
    Custom = 4,
}

/// <summary>
/// A force applied every physics tick, attached to one entity's physics object.
///
/// Like <see cref="Constraint"/> this is a handle into a native table rather
/// than an entity, but for a different reason. A constraint cannot be an entity
/// because it has two ends; a motion controller cannot be managed at all,
/// because it is asked for a force several times per frame and every one of
/// those would be a call across the boundary for a number that changed once.
///
/// So the split is: C# decides WHAT force, native applies it. That is the same
/// line the migration plan draws for movement, drawn here for the same reason.
/// </summary>
public sealed class MotionController
{
    private MotionController(IEngineMotionControllers engine, PhysicsId id, MotionControllerKind kind)
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

    public MotionControllerKind Kind { get; }

    /// <summary>
    /// The side's motion-controller table, handed over by whoever made this
    /// controller — the entity that created it, from its own composition. Kept
    /// per instance so two game instances in one process each talk to their
    /// own engine.
    /// </summary>
    private readonly IEngineMotionControllers _engine;

    /// <summary>
    /// Attaches a controller to an entity. Null if the entity has no physics
    /// object, which is the usual reason: a brush or a logical entity has
    /// nothing for a force to act on.
    /// </summary>
    public static MotionController? Attach(IEngineMotionControllers engine, EntityHandle entity,
                                           MotionControllerKind kind)
    {
        var id = new PhysicsId(engine.Create(entity, (int)kind));
        return id.IsNone ? null : new MotionController(engine, id, kind);
    }

    /// <summary>
    /// A controller with nothing attached yet, for a volume that will attach and
    /// detach other entities' objects as they enter and leave it.
    ///
    /// The named spelling of <c>Attach(engine, EntityHandle.None, kind)</c>, which is the
    /// one handle native reads as a request for an empty controller rather than
    /// as an entity it failed to find. Any other handle that no longer resolves
    /// is a warning and a null result, because an entity that died between the
    /// decision and the call is a bug and this is the only thing that tells the
    /// two apart.
    /// </summary>
    public static MotionController? Unattached(IEngineMotionControllers engine, MotionControllerKind kind)
    {
        var id = new PhysicsId(engine.Create(EntityHandle.None, (int)kind));
        return id.IsNone ? null : new MotionController(engine, id, kind);
    }

    /// <summary>
    /// Wraps a controller the SOLVER still holds: the reload spelling of
    /// <see cref="Attach"/>.
    ///
    /// A hot reload rebuilds the managed entity but leaves the native
    /// controller table standing (PhysicsMotionControllers carries it and clears it only
    /// at LevelShutdown), so the controller a thruster attached before the
    /// reload is still applying its force — and the rebuilt entity must be
    /// able to drive and destroy it. Stock's controllers ride the same gap:
    /// m_motor is a saved field (phys_controller.cpp's DATADESC) whose
    /// pointer is re-wired in Activate(). The managed spelling is a
    /// <c>[SaveField]</c> leaf carrying the <see cref="Id"/> and this call
    /// putting the wrapper back around it.
    ///
    /// It makes no call, so a stale id is not detected here — but neither is
    /// any harm done: every setter addresses the id verbatim, and a stale or
    /// NONE id is what a controller whose table was dropped between the save
    /// and the restore correctly looks like.
    /// </summary>
    public static MotionController Adopt(IEngineMotionControllers engine, PhysicsId id,
                                         MotionControllerKind kind)
        => new(engine, id, kind);

    /// <summary>
    /// Adds an entity's physics object to the set this controller acts on.
    /// Harmless if it is already there.
    /// </summary>
    public void AttachObject(EntityHandle entity) => _engine.Attach(Id.Value, entity);

    /// <summary>
    /// Takes one back off. Harmless if it was never attached, so a caller that
    /// missed a leave event is simply correct on the next one rather than stuck.
    /// </summary>
    public void DetachObject(EntityHandle entity) => _engine.Detach(Id.Value, entity);

    /// <summary>
    /// Sets the steady force and twist.
    ///
    /// <paramref name="localSpace"/> is the difference between a rocket bolted
    /// to a crate and a wind that always blows north: the first turns with the
    /// crate, the second does not.
    /// </summary>
    public void SetForce(Vector linear, Vector angular, bool localSpace = true)
        => _engine.SetForce(Id.Value, linear, angular, localSpace);

    /// <summary>
    /// Turns a constant force on or off, keeping what it was set to.
    ///
    /// Meaningless for a motor: its off state is a target speed of zero, which
    /// actively brakes rather than letting the thing coast.
    /// </summary>
    public void SetActive(bool active) => _engine.SetActive(Id.Value, active);

    /// <summary>
    /// Drives towards a target angular speed about an axis, in degrees per
    /// second.
    ///
    /// <paramref name="torqueAcceleration"/> is how hard the motor is willing to
    /// work; the ceiling it turns into depends on the object's inertia along the
    /// axis, which native derives because C# has no way to ask for it.
    /// </summary>
    public void SetMotor(Vector axis, float targetSpeed, float inertiaFactor, float torqueAcceleration)
        => _engine.SetMotor(Id.Value, axis, targetSpeed, inertiaFactor, torqueAcceleration);

    /// <summary>
    /// Sets a force applied at a point in the world rather than through the
    /// centre of mass, which is what makes a thruster mounted off-centre spin
    /// the thing it is bolted to.
    ///
    /// The split into push and twist depends on the object's mass
    /// distribution, so vphysics makes it. Answers what was set, for a caller
    /// that scales it later (stock's m_linearSave / m_angularSave).
    /// </summary>
    public (Vector Linear, Vector Angular) SetForceAtPoint(Vector force, Vector worldPoint,
                                ForceComponents components = ForceComponents.Both,
                                bool localSpace = true)
        => _engine.SetForceAtPoint(Id.Value, force, worldPoint, (int)components, localSpace);

    /// <summary>
    /// Keeps the object's up axis pointing along <paramref name="goalAxis"/>.
    ///
    /// The angular limit caps how fast the righting happens. Without one a
    /// barrel knocked over snaps upright, which reads as a bug rather than as a
    /// barrel; with it, it rolls back.
    /// </summary>
    public void SetUpright(Vector goalAxis, float angularLimit, bool dampAllRotation, bool active)
        => _engine.SetUpright(Id.Value, goalAxis, angularLimit, dampAllRotation, active);

    /// <summary>
    /// Whether the numbers given to <see cref="SetForce"/> and
    /// <see cref="SetForceAtPoint"/> are a force or an acceleration.
    ///
    /// The difference is the object's mass: the same vector read as a force
    /// moves a heavy crate slowly and a light one quickly, while read as an
    /// acceleration it moves both alike. Stock's thrust entities are written the
    /// second way.
    ///
    /// Do NOT get the same effect by multiplying by the mass before calling. The
    /// mass changes with the object — one that breaks, one that fills — and a
    /// magnitude scaled once is then wrong; it is the same trap as treating a
    /// force as an impulse.
    /// </summary>
    public void SetForceIsAcceleration(bool acceleration)
        => _engine.SetForceIsAcceleration(Id.Value, acceleration);

    /// <summary>
    /// Sets everything a <see cref="MotionControllerKind.Medium"/> does to the
    /// objects inside it.
    ///
    /// One call rather than nine setters because the nine are one setting: they
    /// are read together every tick and no state exists in which changing one
    /// alone is meaningful. Each default is also the way of switching that part
    /// off — a scale of 1 does not scale and a limit of 0 does not limit — which
    /// is why there are no flags to go with them.
    /// </summary>
    /// <param name="gravityScale">Multiple of the world's gravity. 1 changes nothing.</param>
    /// <param name="airDensity">Drag ADDED on top of whatever the object already has.</param>
    /// <param name="linearLimit">Speed ceiling, in units per second. 0 does not limit.</param>
    /// <param name="linearLimitDelta">
    /// Most speed one tick may remove, which is what keeps a ceiling from
    /// reading as a wall. 0 removes as much as it needs to.
    /// </param>
    /// <param name="linearScale">Speed multiplier. 1 changes nothing.</param>
    /// <param name="angularLimit">Spin ceiling. 0 does not limit.</param>
    /// <param name="angularScale">Spin multiplier. 1 changes nothing.</param>
    /// <param name="linearForce">Strength of a steady push through the medium.</param>
    /// <param name="linearForceAngles">Which way that push goes.</param>
    public void SetMedium(float gravityScale = 1f, float airDensity = 0f,
                          float linearLimit = 0f, float linearLimitDelta = 0f,
                          float linearScale = 1f, float angularLimit = 0f,
                          float angularScale = 1f, float linearForce = 0f,
                          Vector linearForceAngles = default)
        => _engine.SetMedium(Id.Value, gravityScale, airDensity, linearLimit,
            linearLimitDelta, linearScale, angularLimit, angularScale, linearForce,
            linearForceAngles);

    /// <summary>
    /// Moves a medium's speed ceiling to a new value over a period rather than
    /// at once.
    ///
    /// The interpolation itself is native, because the medium reads the value at
    /// the physics tick rate. What it ramps to and over how long is the decision
    /// and is this call. A duration of 0 sets the ceiling outright, and re-aiming
    /// a ramp already in flight continues from where it had reached.
    /// </summary>
    public void RampLinearLimit(float targetLimit, float duration)
        => _engine.RampLinearLimit(Id.Value, targetLimit, duration);

    /// <summary>Detaches and frees it. Safe to call twice.</summary>
    public void Destroy()
    {
        if (Id.IsNone)
            return;

        _engine.Destroy(Id.Value);
        Id = PhysicsId.None;
    }
}
