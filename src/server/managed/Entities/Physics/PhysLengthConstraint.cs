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
/// Holds two points within a distance of each other — a rope, or a rigid link
/// when the minimum equals the total.
/// </summary>
[ScriptedEntity(EntityNames.PhysLengthconstraint)]
public sealed class PhysLengthConstraint : PhysicsConstraintEntity
{
    public PhysLengthConstraint(IEngineConstraints constraints, IEngineSound sound, EntityPairCollisions pairs)
        : base(constraints, sound, pairs)
    {
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysLengthConstraintVars"/>.</summary>
    [ScriptVars] public PhysLengthConstraintVars PhysLengthConstraintVars = new();

    /// <summary>
    /// phys_lengthconstraint's own use of bit 2, and nothing else's. The family
    /// bits live in <see cref="ConstraintSpawnFlag"/>; this one is the length
    /// constraint's "rigid", and the same bit on a slide means "limit ends" and
    /// on a ragdoll means "free movement". A one-member enum, because the
    /// membership is the point: the type is what stops those three being asked
    /// of each other.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        Rigid = 0b0000_0010,
    }

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
    {
        // A rigid link is a rope that cannot slacken: the minimum equals the
        // total, which native computes from the two points. Passing float.MaxValue
        // would be wrong, so the flag is handled by asking for the distance.
        float minimum = Flags.HasFlag(SpawnFlag.Rigid)
            ? (PhysLengthConstraintVars.AttachPoint - Origin).Length + PhysLengthConstraintVars.AddLength
            : PhysLengthConstraintVars.MinLength;

        return Constraint.Length(Constraints, a, b, Origin, PhysLengthConstraintVars.AttachPoint, minimum, PhysLengthConstraintVars.AddLength,
                                 ForceLimitKg, TorqueLimitKg);
    }
}

/// <summary>
/// The map keys <see cref="PhysLengthConstraint"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysLengthConstraintVars
{
    /// <summary>Where on the second object the link lands. The entity's own origin is the first.</summary>
    public Vector AttachPoint { get; set; }

    public float MinLength { get; set; }

    public float AddLength { get; set; }
}
