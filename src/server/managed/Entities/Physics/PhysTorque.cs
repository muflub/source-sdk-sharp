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
/// A steady twist about an axis, with no target speed — it just keeps pushing.
///
/// The difference from a motor is worth stating: a motor decides how much torque
/// to apply to REACH a speed and stops pushing once it is there. A torque
/// applies the same amount forever, so whatever it is attached to accelerates
/// until something else stops it.
/// </summary>
[ScriptedEntity(EntityNames.PhysTorque)]
public sealed class PhysTorque : PhysicsForceEntity
{
    public PhysTorque(IEngineMotionControllers motion)
        : base(motion)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PhysTorqueVars"/>.</summary>
    [ScriptVars] public PhysTorqueVars PhysTorqueVars = new();

    private const int StartActiveFlag = 1;

    /// <summary>
    /// The twist multiplier the map ramps, carried for the same reason the
    /// thruster's is: the amnesia shows only when something re-issues the
    /// force, and then at the wrong magnitude (stock's m_scale,
    /// phys_controller.cpp).
    /// </summary>
    [SaveField] private float _scale = 1f;

    /// <summary>Timed shut-off owed (stock's m_flStopTime shape).</summary>
    [SaveField] private bool _stopping;

    /// <summary>
    /// The twist axis in the attached object's own frame, taken once at
    /// activation (stock's SetupForces: VectorIRotate( m_axis, matrix )), so
    /// the twist turns with the object and a scale scales it as stock's
    /// ScaleConstantForce does.
    /// </summary>
    [SaveField] private Vector _localAxis;

    protected override MotionControllerKind Kind => MotionControllerKind.ConstantForce;

    protected override void OnAttached()
    {
        // The object's frame as vphysics has it: x forward, y LEFT, z up.
        var basis = Angles.Vectors(Entities.GetAngles(Attached));
        Vector axis = TwistAxis(PhysTorqueVars.Axis, Origin);
        _localAxis = new Vector(axis.Dot(basis.Forward), -axis.Dot(basis.Right), axis.Dot(basis.Up));

        // stock's integrator is an ACCELERATION (CPhysForce::Spawn).
        Controller?.SetForceIsAcceleration(true);

        Apply();
        Controller?.SetActive((SpawnFlags & StartActiveFlag) != 0);

        if ((SpawnFlags & StartActiveFlag) != 0)
            StartTimer();
    }

    public override void OnThink()
    {
        base.OnThink();

        if (!_stopping)
            return;

        // The timed shutoff. A separate flag from the attach think, because both
        // arrive here and only one of them means "time is up".
        _stopping = false;
        Controller?.SetActive(false);
        StopThinking();
    }

    [ScriptedEvent("Activate")]
    private void OnActivateInput()
    {
        Controller?.SetActive(true);
        StartTimer();
    }

    [ScriptedEvent("Deactivate")]
    private void OnDeactivate()
    {
        _stopping = false;
        Controller?.SetActive(false);
        StopThinking();
    }

    /// <summary>Multiplies the force. A map ramps a torque up this way.</summary>
    [ScriptedEvent("scale")]
    private void OnScale(float scale)
    {
        _scale = scale;
        Apply();
    }

    private void Apply()
        => Controller?.SetForce(Vector.Zero, _localAxis * (PhysTorqueVars.Force * _scale), localSpace: true);

    /// <summary>
    /// The axis the torque twists about: CPhysTorque::Spawn reads the "axis" key
    /// as a point and makes it a direction from the entity, snapped to a world
    /// axis when it nearly is one (phys_controller.cpp:437-447; A26).
    /// </summary>
    internal static Vector TwistAxis(Vector axisKey, Vector origin)
        => PhysMotor.SnapToAxis((axisKey - origin).Normalized);

    private void StartTimer()
    {
        if (PhysTorqueVars.ForceTime <= 0f)
            return;

        _stopping = true;
        SetNextThink(PhysTorqueVars.ForceTime);
    }
}

/// <summary>
/// The map keys <see cref="PhysTorque"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysTorqueVars
{
    public float Force { get; set; }

    /// <summary>How long to push for, in seconds. Zero is forever.</summary>
    public float ForceTime { get; set; }

    /// <summary>A POINT, as Hammer writes it and stock reads it: <see cref="PhysTorque.TwistAxis"/>.</summary>
    public Vector Axis { get; set; }
}
