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
    public PhysHinge(IEngineConstraints constraints, IEngineSound sound, EntityPairCollisions pairs,
                     MassCenterOverrides massCenters)
        : base(constraints, sound, pairs)
    {
        _massCenters = massCenters;
    }

    /// <summary>m_hinge.worldPosition and worldAxisDirection, taken at Spawn (CPhysHinge::Spawn).</summary>
    [SaveField] private Vector _worldPosition, _worldAxis;

    /// <summary>
    /// SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY as Spawn leaves it: removed from a
    /// hinge with both ends named, which joins two objects and no world.
    /// </summary>
    [SaveField] private bool _assumeWorld;

    /// <summary>Where a world hinge leaves its object's mass centre (stock's PhysSetMassCenterOverride).</summary>
    private readonly MassCenterOverrides _massCenters;

    /// <summary>
    /// The bits this entity shares with the rest of its family. See
    /// <see cref="ConstraintSpawnFlag"/>.
    /// </summary>
    private ConstraintSpawnFlag FamilyFlags => SpawnFlagsAs<ConstraintSpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysHingeVars"/>.</summary>
    [ScriptVars] public PhysHingeVars PhysHingeVars = new();

    /// <summary>
    /// CPhysHinge::Spawn's world hinge: the one named end's mass centre is
    /// moved onto the hinge's axis when that end is made.
    /// </summary>
    public override void OnSpawn()
    {
        base.OnSpawn();

        // Where it stands and which way it points now, not when it is built.
        _worldPosition = Origin;
        Vector direction = PhysHingeVars.HingeAxis - _worldPosition;
        _worldAxis = direction.LengthSquared > 0f ? SnapToAxis(direction.Normalized) : Vector.Zero;

        _assumeWorld = FamilyFlags.HasFlag(ConstraintSpawnFlag.AssumeWorldGeometry);
        if (!_assumeWorld)
            return;

        if (Vars.Attach1.Length == 0)
            _massCenters.Add(MassCenterOverride.SnapToAxis(Vars.Attach2, _worldPosition, _worldAxis));
        else if (Vars.Attach2.Length == 0)
            _massCenters.Add(MassCenterOverride.SnapToAxis(Vars.Attach1, _worldPosition, _worldAxis));
        else
            _assumeWorld = false;
    }

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
    {
        if (_worldAxis.LengthSquared <= 0f)
        {
            Log.Warning($"[SourceSharp] phys_hinge '{Name}' has no axis\n");
            return null;
        }

        return Constraint.Hinge(Constraints, a, b, _worldPosition, _worldAxis, PhysHingeVars.Friction,
                                ForceLimitKg, TorqueLimitKg, _assumeWorld);
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
    /// Drives the hinge at degrees per second, the torque scaled by the load
    /// on the joint so that a heavy door and a light one open at the same rate.
    /// </summary>
    [ScriptedEvent("SetAngularVelocity")]
    private void OnSetAngularVelocity(InputData input)
        => Joint?.SetHingeVelocity(input.Number, PhysHingeVars.SystemLoadScale);

    /// <summary>InputSetHingeFriction: the friction the next joint is made with.</summary>
    [ScriptedEvent("SetHingeFriction")]
    private void OnSetHingeFriction(InputData input)
    {
        PhysHingeVars.Friction = input.Number;
        Log.Msg($"Setting hinge friction to {input.Number.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}\n");
    }
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

    [VarName("systemloadscale")]
    public float SystemLoadScale { get; set; }
}
