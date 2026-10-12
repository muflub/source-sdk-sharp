//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A rocket bolted to something: a steady push in the direction the entity
/// faces, applied at the point where it sits.
///
/// The offset is the whole character of it. A thruster through the centre of
/// mass shoves; one mounted off to the side shoves AND spins, and a map uses
/// that difference deliberately. Working out the split needs the object's mass
/// distribution, so it happens natively.
/// </summary>
[ScriptedEntity(EntityNames.PhysThruster)]
public sealed class PhysThruster : PhysicsForceEntity
{
    public PhysThruster(IEngineEntityPhysics physics, IEngineMotionControllers motion)
        : base(motion)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysThrusterVars"/>.</summary>
    [ScriptVars] public PhysThrusterVars PhysThrusterVars = new();

    /// <summary>
    /// phys_thruster's own spawnflags, from physics.cpp's SF_THRUST_*.
    ///
    /// <para>
    /// These six are not independent settings so much as three pairs of questions:
    /// when to start, what part of the push to keep, and whose frame the direction
    /// is in. The enum is what stops <see cref="SpawnFlag.ApplyForce"/> and
    /// <see cref="SpawnFlag.ApplyTorque"/> being read as a single "apply" bit -- a
    /// thruster with neither pushes nothing at all, which is a legal thing for a
    /// map to ask for and looks exactly like a broken entity.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        StartActive = 0b0000_0001,
        ApplyForce = 0b0000_0010,
        ApplyTorque = 0b0000_0100,
        LocalOrientation = 0b0000_1000,
        MassIndependent = 0b0001_0000,
        IgnorePosition = 0b0010_0000,
    }

    /// <summary>
    /// Whether the force is on: stock's <c>m_pController != NULL</c>. Stock
    /// builds the controller at ForceOn and destroys it at ForceOff; here it
    /// is made once at attach and switched, so "on" is its own bit.
    /// </summary>
    [SaveField] private bool _on;

    /// <summary>Timed shut-off owed (stock's m_flStopTime shape).</summary>
    [SaveField] private bool _stopping;

    /// <summary>
    /// The push and twist SetupForces answered at the last ForceOn: stock's
    /// <c>m_linearSave</c> / <c>m_angularSave</c> (CConstantForceController,
    /// saved in its DATADESC). A <c>scale</c> input scales these, as
    /// ScaleConstantForce does; it never re-decomposes at the object's
    /// current pose.
    /// </summary>
    [SaveField] private Vector _linearSave;

    /// <inheritdoc cref="_linearSave"/>
    [SaveField] private Vector _angularSave;

    /// <summary>
    /// stock's m_localOrigin: where the thruster sits in the attached object's
    /// frame, taken once at activation (CPhysThruster::OnActivate). Zero with
    /// SF_THRUST_IGNORE_POS, which is the attached object's own origin.
    /// </summary>
    [SaveField] private Vector _localOrigin;

    /// <summary>
    /// The thrust's direction in the attached object's frame at activation:
    /// stock's local angles (SetLocalAngles( MatrixAngles( thrusterToAttached )))
    /// under SF_THRUST_LOCAL_ORIENTATION.
    /// </summary>
    [SaveField] private Vector _localDirection;

    protected override MotionControllerKind Kind => MotionControllerKind.ConstantForce;

    protected override void OnAttached()
    {
        // CPhysThruster::OnActivate: the relationship to the attached object,
        // fixed here, so it may move before the force is switched on.
        var basis = Angles.Vectors(Entities.GetAngles(Attached));
        _localOrigin = Flags.HasFlag(SpawnFlag.IgnorePosition)
            ? Vector.Zero
            : PhysicsCannister.ToLocal(Origin - Entities.GetOrigin(Attached), basis);
        _localDirection = PhysicsCannister.ToLocal(Angles.Forward(SpawnAngles), basis);

        // stock's integrator is an ACCELERATION (CPhysForce::Spawn): the
        // velocity offset a force makes is already divided by the mass.
        Controller?.SetForceIsAcceleration(true);
        Controller?.SetActive(false);

        if (Flags.HasFlag(SpawnFlag.StartActive))
            ForceOn();
    }

    public override void OnThink()
    {
        base.OnThink();

        if (!_stopping)
            return;

        _stopping = false;
        ForceOff();
    }

    [ScriptedEvent("Activate")]
    private void OnActivateInput() => ForceOn();

    [ScriptedEvent("Deactivate")]
    private void OnDeactivate() => ForceOff();

    /// <summary>
    /// CPhysForce::ScaleForce: on first if it is off, then the activation's
    /// answer times the scale (ScaleConstantForce). The scale is not kept: the
    /// next ForceOn sets the unscaled answer again, as stock's SetConstantForce
    /// does.
    /// </summary>
    [ScriptedEvent("scale")]
    private void OnScale(float scale)
    {
        if (!_on)
            ForceOn();

        Controller?.SetForce(_linearSave * scale, _angularSave * scale, Flags.HasFlag(SpawnFlag.LocalOrientation));
    }

    /// <summary>
    /// CPhysForce::ForceOn: nothing if already on; otherwise ActivateForce
    /// (SetupForces at the attached object's pose now) and the timed
    /// shut-off.
    /// </summary>
    private void ForceOn()
    {
        if (_on || Controller is null)
            return;

        ActivateForce();
        _on = true;
        StartTimer();
    }

    /// <summary>CPhysForce::ForceOff: the force stops, the think goes, and the object is woken.</summary>
    private void ForceOff()
    {
        if (!_on)
            return;

        _on = false;
        _stopping = false;
        Controller?.SetActive(false);
        StopThinking();

        if (Entities.IsValid(Attached))
            Physics.Wake(Attached);
    }

    /// <summary>
    /// stock's ActivateForce and CPhysThruster::SetupForces, from the
    /// activation frame above and the attached object's pose now: the push and
    /// twist a force at the thruster's point makes (CalculateVelocityOffset),
    /// in the object's own frame with SF_THRUST_LOCAL_ORIENTATION
    /// (SIM_LOCAL_ACCELERATION, so it turns with the object) and in the
    /// world's without. Computed once per ForceOn, as stock's is, and kept for
    /// the scale input: the controller then answers it every tick.
    /// </summary>
    private void ActivateForce()
    {
        if (Controller is null)
            return;

        var basis = Angles.Vectors(Entities.GetAngles(Attached));
        bool localSpace = Flags.HasFlag(SpawnFlag.LocalOrientation);

        Vector direction = localSpace ? PhysicsCannister.Rotate(_localDirection, basis) : Angles.Forward(SpawnAngles);
        Vector thrust = direction * PhysThrusterVars.Force;

        // The flag says the force is an acceleration rather than a force, so it
        // is scaled by mass to make heavy and light objects move alike.
        if (Flags.HasFlag(SpawnFlag.MassIndependent))
            thrust = thrust * Physics.GetMass(Attached);

        Vector point = Entities.GetOrigin(Attached) + PhysicsCannister.Rotate(_localOrigin, basis);

        var components = ForceComponents.None;
        if (Flags.HasFlag(SpawnFlag.ApplyForce)) components |= ForceComponents.Push;
        if (Flags.HasFlag(SpawnFlag.ApplyTorque)) components |= ForceComponents.Twist;

        (_linearSave, _angularSave) = Controller.SetForceAtPoint(thrust, point, components, localSpace);
        Controller.SetActive(true);
    }

    private void StartTimer()
    {
        if (PhysThrusterVars.ForceTime <= 0f)
            return;

        _stopping = true;
        SetNextThink(PhysThrusterVars.ForceTime);
    }
}

/// <summary>
/// The map keys <see cref="PhysThruster"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysThrusterVars
{
    public float Force { get; set; }

    /// <summary>How long to push for. Zero is forever.</summary>
    public float ForceTime { get; set; }
}
