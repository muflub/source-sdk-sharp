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
/// A hinge: one object turns about an axis. Doors, levers, wheels.
/// </summary>
[ScriptedEntity(EntityNames.PhysHinge)]
public sealed class PhysHinge : PhysicsConstraintEntity
{
    public PhysHinge(IEngineConstraints constraints, IEngineSound sound, EntityPairCollisions pairs)
        : base(constraints, sound)
    {
        _pairs = pairs;
    }

    /// <summary>Where the family's "do not collide" pair is recorded (stock's PhysDisableEntityCollisions).</summary>
    private readonly EntityPairCollisions _pairs;

    /// <summary>
    /// The bits this entity shares with the rest of its family. See
    /// <see cref="ConstraintSpawnFlag"/>.
    /// </summary>
    private ConstraintSpawnFlag FamilyFlags => SpawnFlagsAs<ConstraintSpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysHingeVars"/>.</summary>
    [ScriptVars] public PhysHingeVars PhysHingeVars = new();

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
    {
        Vector direction = PhysHingeVars.HingeAxis - Origin;

        if (direction.LengthSquared <= 0f)
        {
            Log.Warning($"[SourceSharp] phys_hinge '{Name}' has no axis\n");
            return null;
        }

        // The family-wide checkbox, not a hinge one -- see ConstraintSpawnFlag.
        // Almost every hinge wants it, because a hinge usually joins two things
        // that overlap: dm_lockdown's fan sweeps through its own housing, and
        // without it the fan is stopped by that housing within a turn of being
        // powered up.
        bool noCollide = FamilyFlags.HasFlag(ConstraintSpawnFlag.DisableCollision);
        Constraint? hinge = Constraint.Hinge(Constraints, a, b, Origin, SnapToAxis(direction.Normalized),
                                             PhysHingeVars.Friction, ForceLimitKg, TorqueLimitKg);

        // The pair the solver refuses is managed's (plan_physics_bones_native_zero
        // 2.1): recorded once the joint exists, as native's create did.
        if (hinge is not null && noCollide)
            _pairs.SetEnabled(a, b, false);

        return hinge;
    }

    /// <summary>
    /// Turns a nearly-axial direction into an exactly axial one.
    ///
    /// Stock Source does this and it is not cosmetic: a mapper places the axis
    /// by eye, and a door hinge a thousandth off vertical drifts sideways over
    /// time instead of just turning. The threshold is the engine's.
    /// </summary>
    private static Vector SnapToAxis(Vector direction, float epsilon = 0.002f)
    {
        float projection = 1f - epsilon;

        if (MathF.Abs(direction.X) > projection) return new Vector(MathF.Sign(direction.X), 0f, 0f);
        if (MathF.Abs(direction.Y) > projection) return new Vector(0f, MathF.Sign(direction.Y), 0f);
        if (MathF.Abs(direction.Z) > projection) return new Vector(0f, 0f, MathF.Sign(direction.Z));

        return direction;
    }

    /// <summary>
    /// Drives the hinge. The value is degrees per second; zero lets it swing
    /// free again.
    ///
    /// Stock scales the torque by the load on the joint so that a heavy door
    /// and a light one open at the same rate. This does not, and the difference
    /// shows on anything unusually heavy — worth knowing before blaming the
    /// map.
    /// </summary>
    [ScriptedEvent("SetAngularVelocity")]
    private void OnSetAngularVelocity(InputData input)
        => Joint?.SetMotor(input.Number, MotorTorque);

    private const float MotorTorque = 1000f;
}

/// <summary>
/// The map keys <see cref="PhysHinge"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysHingeVars
{
    /// <summary>
    /// A POINT, not a direction, despite the name — Hammer draws the axis as a
    /// line from the entity to here, and the direction is the difference. A map
    /// that meant a direction would put the hinge at the origin of the world.
    /// </summary>
    public Vector HingeAxis { get; set; }

    [VarName("hingefriction")]
    public float Friction { get; set; }
}
