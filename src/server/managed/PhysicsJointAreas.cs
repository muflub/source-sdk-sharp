// The three physics tables. Three interfaces and not one, because VPhysics keeps
// three tables and a mod that pretended otherwise would leak one kind while
// counting another.
//
// MANAGED SINCE plan_physics_bones_native_zero 2.4. These were engine areas
// (shared/interop, one shim slot per member, the tables in
// shell_constraints.cpp and shell_motioncontrollers.cpp). The tables are the
// server's own now (PhysicsJointTable, PhysicsConstraints,
// PhysicsMotionControllers), which call vphysics through its vtables; the
// names and the members are unchanged, so no entity that asks for one noticed.

namespace SourceSharp.Abi;

/// <summary>
/// Constraints: a joint between two entities, owned by neither.
///
/// <para>
/// Handles rather than entities, and that is the whole reason this is its own
/// table. A constraint outliving what it joined is invisible from every other
/// angle, so the count is here beside the lifetime calls rather than somewhere a
/// leak could hide from. Zero means a constraint could not be made. See
/// <see cref="Constraint"/> for the wrapper callers should actually use.
/// </para>
/// </summary>
public interface IEngineConstraints
{
    /// <summary>How many constraints are live. For reporting, not for logic.</summary>
    int Count();

    /// <summary>
    /// Creates a constraint of <paramref name="kind"/> (<see cref="ConstraintKind"/>)
    /// between two entities and answers its id, or 0. <paramref name="parameters"/>
    /// is the flat block <see cref="Constraint"/>'s factories build; a short
    /// one is refused rather than read off the end. An end that names no
    /// entity is the world.
    /// </summary>
    uint Create(EntityHandle a, EntityHandle b, int kind, ReadOnlySpan<float> parameters);

    /// <summary>Tears one down. Safe to call twice: a stale id answers nothing.</summary>
    void Destroy(uint constraint);

    /// <summary>Whether the id still names a live constraint.</summary>
    bool IsValid(uint constraint);

    /// <summary>
    /// CPhysConstraint::Deactivate: the joint stops holding, both ends lose
    /// FVPHYSICS_CONSTRAINT_STATIC, and a world hinge's object is no longer
    /// hinged. The constraint stays, to be activated again.
    /// </summary>
    void Deactivate(uint constraint);

    /// <summary>CPhysConstraint::InputTurnOn's half: the joint holds again, and both ends wake.</summary>
    void Activate(uint constraint);

    /// <summary>
    /// Drives a joint along its axis rather than about one — a winch, a lift, a
    /// powered slide. Speed is units per second. The load scale multiplies the
    /// FORCE the motor will spend, which is the average mass of the ends that
    /// can move, so it cannot be folded into the speed by the caller.
    /// </summary>
    void SetLinearMotor(uint constraint, float unitsPerSecond, float systemLoadScale);

    /// <summary>
    /// CPhysHinge::InputSetVelocity: drives a hinge at degrees per second with
    /// the torque scaled by the average inertia of the ends that can move, as
    /// stock spends it, and wakes them.
    /// </summary>
    void SetHingeVelocity(uint constraint, float degreesPerSecond, float systemLoadScale);
}

/// <summary>
/// Springs. A separate table from the constraints because VPhysics keeps a
/// separate table, and a mod that pretended otherwise would leak one kind while
/// counting the other.
/// </summary>
public interface IEngineSprings
{
    /// <summary>How many springs are live, for reporting through debug-api.</summary>
    int Count();

    /// <summary>
    /// Creates a spring between two entities' objects and answers its id, or 0.
    /// The endpoints are world positions, matched to the ends by which is
    /// nearer (stock's PointIsNearer); an end that names no entity is the world.
    /// </summary>
    uint Create(EntityHandle a, EntityHandle b, Vector start, Vector end, float constant, float damping, float relativeDamping, float naturalLength, bool onlyStretch);

    /// <summary>Tears one down. Safe to call twice.</summary>
    void Destroy(uint spring);

    /// <summary>Whether the id still names a live spring.</summary>
    bool IsValid(uint spring);

    /// <summary><c>IPhysicsSpring::SetSpringConstant</c>.</summary>
    void SetConstant(uint spring, float constant);

    /// <summary><c>IPhysicsSpring::SetSpringDamping</c>.</summary>
    void SetDamping(uint spring, float damping);

    /// <summary>
    /// <c>IPhysicsSpring::SetSpringLength</c>: the length at which it pulls with
    /// no force, so setting it shorter winches something in.
    /// </summary>
    void SetLength(uint spring, float length);
}

/// <summary>
/// Motion controllers: a force applied every physics tick rather than once.
///
/// <para>
/// The thing a one-shot impulse cannot express — holding something upright,
/// driving it toward a speed, damping it in a medium. vphysics asks managed's
/// IMotionEvent for the force each tick; the parameters set here are what it
/// answers from. See <see cref="MotionController"/> for the wrapper to use.
/// </para>
/// </summary>
public interface IEngineMotionControllers
{
    /// <summary>
    /// Adds an object to a controller that is already running. A volume's
    /// controller starts with nothing attached and gains objects as they enter.
    /// </summary>
    void Attach(uint motion, EntityHandle entity);

    /// <summary>How many motion controllers are live. For reporting, not logic.</summary>
    int Count();

    /// <summary>
    /// A controller of <paramref name="kind"/> (<see cref="MotionControllerKind"/>)
    /// attached to the entity's object, or with nothing attached when the
    /// entity is <see cref="EntityHandle.None"/>. Answers its id, or 0.
    /// </summary>
    uint Create(EntityHandle entity, int kind);

    /// <summary>Detaches and frees it. Safe to call twice.</summary>
    void Destroy(uint motion);

    /// <summary>
    /// Takes an object back off. Detaching something that was never attached
    /// does nothing, so a caller that missed a leave is simply correct on the
    /// next one.
    /// </summary>
    void Detach(uint motion, EntityHandle entity);

    /// <summary>
    /// Moves a medium's speed limit to a new value over a period. A duration of
    /// zero sets it outright; re-aiming a ramp in flight starts from where it
    /// had reached rather than from where it began.
    /// </summary>
    void RampLinearLimit(uint motion, float targetLimit, float duration);

    /// <summary>Turns a constant force on or off, keeping what it was set to.</summary>
    void SetActive(uint motion, bool active);

    /// <summary>A steady push and twist, in the object's own frame or the world's.</summary>
    void SetForce(uint motion, Vector linear, Vector angular, bool localSpace);

    /// <summary>
    /// A force applied at a world point, split into push and twist by the
    /// object's mass distribution; <paramref name="components"/> keeps either
    /// half (<see cref="ForceComponents"/>). Answers the push and twist it
    /// set (zero for no such controller or no object), which a caller keeps to
    /// scale later as stock's ScaleConstantForce scales m_linearSave.
    /// </summary>
    (Vector Linear, Vector Angular) SetForceAtPoint(uint motion, Vector force, Vector worldPoint, int components, bool localSpace);

    /// <summary>
    /// Whether a constant force's numbers are read as a force or as an
    /// acceleration. Do not scale by the mass instead — the mass changes with
    /// the object, and a magnitude fixed once is then wrong.
    /// </summary>
    void SetForceIsAcceleration(uint motion, bool acceleration);

    /// <summary>
    /// Sets everything a medium does to the objects inside it, in one call
    /// because the nine numbers are read together every tick.
    /// </summary>
    void SetMedium(uint motion, float gravityScale, float airDensity, float linearLimit, float linearLimitDelta, float linearScale, float angularLimit, float angularScale, float linearForce, Vector linearForceAngles);

    /// <summary>Target speed about an axis, and how hard the motor is willing to work.</summary>
    void SetMotor(uint motion, Vector axis, float targetSpeed, float inertiaFactor, float torqueAcceleration);

    /// <summary>Which way is up, and how hard to insist on it.</summary>
    void SetUpright(uint motion, Vector goalAxis, float angularLimit, bool dampAllRotation, bool active);
}
