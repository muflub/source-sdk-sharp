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
/// Two objects hung on one rope over two pulleys: a counterweight, a lift with
/// a sandbag on the other end.
/// </summary>
[ScriptedEntity(EntityNames.PhysPulleyconstraint)]
public sealed class PhysPulleyConstraint : PhysicsConstraintEntity
{
    public PhysPulleyConstraint(IEngineConstraints constraints, IEngineSound sound)
        : base(constraints, sound)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PhysPulleyConstraintVars"/>.</summary>
    [ScriptVars] public PhysPulleyConstraintVars PhysPulleyConstraintVars = new();

    /// <summary>
    /// phys_pulleyconstraint's own use of bit 2, and nothing else's. The family
    /// bits live in <see cref="ConstraintSpawnFlag"/>; the same bit is the
    /// length constraint's "rigid", the slide's "limit ends" and the ragdoll's
    /// "free movement", so the type is what keeps them apart.
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>
        /// A rigid rope resists being shortened as well as lengthened, which is
        /// the difference between a chain hoist and a rope one.
        /// </summary>
        Rigid = 0b0000_0010,
    }

    internal static bool RigidFor(SpawnFlag spawnFlags) => spawnFlags.HasFlag(SpawnFlag.Rigid);

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
    {
        // The gear ratio goes over UNCHANGED, zero and all. Native measures the
        // rope as extraRope + firstSpan + secondSpan * ratio and only THEN
        // substitutes one for a zero ratio, so an unset key gives a rope that
        // ignores the second span's length. That is stock's own order and its
        // own quirk, and a map built against it hangs its counterweight where
        // the mapper left it; correcting the ratio first would move it.
        //
        // The offsets stay at the objects' centres because the two ends are
        // named entities, and a name is all this class resolves. Stock can put
        // them elsewhere, but only through an info_constraint_anchor, which no
        // managed constraint reads yet.
        return Constraint.Pulley(Constraints, a, b, Origin, PhysPulleyConstraintVars.Position2,
                                 offsetOnA: default, offsetOnB: default,
                                 extraRope: PhysPulleyConstraintVars.AddLength, gearRatio: PhysPulleyConstraintVars.GearRatio,
                                 rigid: RigidFor((SpawnFlag)SpawnFlags),
                                 forceLimit: ForceLimitKg, torqueLimit: TorqueLimitKg);
    }
}

/// <summary>
/// The map keys <see cref="PhysPulleyConstraint"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysPulleyConstraintVars
{
    /// <summary>The second pulley. The entity's own origin is the first.</summary>
    public Vector Position2 { get; set; }

    /// <summary>Slack, added to the rope already strung between the objects.</summary>
    public float AddLength { get; set; }

    /// <summary>
    /// How much the second span reels in for what the first pays out, so 2 is a
    /// block and tackle. Left at zero when a map does not say, and that zero is
    /// carried through deliberately — see <see cref="Build"/>.
    /// </summary>
    public float GearRatio { get; set; }
}
