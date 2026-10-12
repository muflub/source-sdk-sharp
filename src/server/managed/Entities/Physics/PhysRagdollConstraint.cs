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
/// A joint that may swing only so far about each of its own three axes, with
/// friction on each: a limb, a jaw, a panel that must not fold flat.
/// </summary>
[ScriptedEntity(EntityNames.PhysRagdollconstraint)]
public sealed class PhysRagdollConstraint : PhysicsConstraintEntity
{
    public PhysRagdollConstraint(IEngineConstraints constraints, IEngineSound sound)
        : base(constraints, sound)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PhysRagdollConstraintVars"/>.</summary>
    [ScriptVars] public PhysRagdollConstraintVars PhysRagdollConstraintVars = new();

    /// <summary>
    /// phys_ragdollconstraint's own use of bit 2, and nothing else's -- the
    /// same bit is "rigid" on a length and a pulley constraint and "limit ends"
    /// on a slide. The bits this entity shares with the rest of the family are
    /// in <see cref="ConstraintSpawnFlag"/>, and this one is deliberately not
    /// among them.
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>
        /// Without it the joint holds its two ends together as well as limiting
        /// how they turn; with it they may drift apart and only the angles are
        /// enforced.
        /// </summary>
        FreeMovement = 0b0000_0010,
    }

    internal static bool LimitAnglesOnlyFor(SpawnFlag spawnFlags)
        => spawnFlags.HasFlag(SpawnFlag.FreeMovement);

    /// <summary>
    /// Reads the FAMILY's start-inactive bit, which is why it takes the other
    /// enum. Honoured here and nowhere else: the ragdoll is the only kind whose
    /// parameters carry an active flag of their own, and it is the one that
    /// gates the angular limits.
    /// </summary>
    internal static bool StartsActiveFor(ConstraintSpawnFlag spawnFlags)
        => !spawnFlags.HasFlag(ConstraintSpawnFlag.StartInactive);

    // Regrouped per axis rather than per bound, because that is how the joint
    // reads them: a transposition between these three would limit the wrong
    // axis and look like a joint that was simply built crooked.
    internal Vector MinAngles => new(PhysRagdollConstraintVars.XMin, PhysRagdollConstraintVars.YMin, PhysRagdollConstraintVars.ZMin);
    internal Vector MaxAngles => new(PhysRagdollConstraintVars.XMax, PhysRagdollConstraintVars.YMax, PhysRagdollConstraintVars.ZMax);
    internal Vector Friction => new(PhysRagdollConstraintVars.XFriction, PhysRagdollConstraintVars.YFriction, PhysRagdollConstraintVars.ZFriction);

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
    {
        // The limits are degrees about the JOINT's axes, so the joint's own
        // frame has to go with them — this entity's position and the angles the
        // mapper turned it to. Native re-expresses that frame relative to both
        // ends as they currently stand, which is why it is handed over whole
        // rather than resolved here.
        return Constraint.Ragdoll(Constraints, a, b, Origin, Entities.GetAngles(Handle),
                                  MinAngles, MaxAngles, Friction,
                                  limitAnglesOnly: LimitAnglesOnlyFor((SpawnFlag)SpawnFlags),
                                  startActive: StartsActiveFor((ConstraintSpawnFlag)SpawnFlags),
                                  forceLimit: ForceLimitKg, torqueLimit: TorqueLimitKg);
    }
}

/// <summary>
/// The map keys <see cref="PhysRagdollConstraint"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysRagdollConstraintVars
{
    // Six limits and three frictions, one pair and one number per axis. The map
    // names them singly because Hammer has no vector field for a pair of angles.
    public float XMin { get; set; }

    public float XMax { get; set; }

    public float YMin { get; set; }

    public float YMax { get; set; }

    public float ZMin { get; set; }

    public float ZMax { get; set; }

    public float XFriction { get; set; }

    public float YFriction { get; set; }

    public float ZFriction { get; set; }
}
