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
/// A slide along a line: a lift, a drawer, a sliding door.
/// </summary>
[ScriptedEntity(EntityNames.PhysSlideconstraint)]
public sealed class PhysSlideConstraint : PhysicsConstraintEntity
{
    public PhysSlideConstraint(IEngineConstraints constraints, IEngineSound sound, EntityPairCollisions pairs)
        : base(constraints, sound, pairs)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PhysSlideConstraintVars"/>.</summary>
    [ScriptVars] public PhysSlideConstraintVars PhysSlideConstraintVars = new();

    /// <summary>
    /// phys_slideconstraint's own use of bit 2, and nothing else's. Every
    /// phys_ constraint spends this bit on something different -- it is the
    /// length constraint's "rigid" and the ragdoll's "free movement" -- which
    /// is why it is declared per entity while the family bits live in
    /// <see cref="ConstraintSpawnFlag"/>.
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>
        /// The travel stops at the two ends. Without it the slide is a rail
        /// with no stops and the object runs off either end of it.
        /// </summary>
        LimitEnds = 0b0000_0010,
    }

    internal static bool LimitsEndsFor(SpawnFlag spawnFlags) => spawnFlags.HasFlag(SpawnFlag.LimitEnds);

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
    {
        // The world goes FIRST when only one end is named, as stock's
        // GetConstraintObjects arranges it and for its stated reason: the first
        // object is the REFERENCE, and everything the second one gets is
        // measured against it. A slide built the other way round measures its
        // travel limits from the world's origin and points its motor at the
        // world, so the map's drawer sits in limits it never asked for and
        // SetVelocity drives something that cannot move. Measured: a drum on a
        // world-anchored slide did not shift a unit under a motor, and moved
        // its full travel and stopped at both ends once the ends were ordered
        // this way.
        //
        // Safe here for the same reason it was safe for the hinge and is NOT
        // safe for the pulley: a slide's parameters are a LINE, which reads the
        // same from either end. A pulley names one pulley per object, so
        // swapping its objects alone would run the rope backwards.
        if (Entities.IsValid(a) && !Entities.IsValid(b))
            (a, b) = (b, a);

        return Constraint.Sliding(Constraints, a, b, Origin, PhysSlideConstraintVars.AxisEnd, PhysSlideConstraintVars.Friction,
                                  LimitsEndsFor((SpawnFlag)SpawnFlags), ForceLimitKg, TorqueLimitKg);
    }

    /// <summary>
    /// Drives the slide along its axis. The value is units per second, a
    /// negative one runs it back, and zero lets it coast.
    ///
    /// The load scale is handed over rather than applied here, because what it
    /// multiplies is the average mass of whichever ends can actually move — a
    /// lift hauling something heavy has to work harder than one hauling
    /// nothing, and only the engine can weigh either. Zero standing for one is
    /// native's substitution too, so a map that never set the key behaves here
    /// as it does in stock.
    /// </summary>
    [ScriptedEvent("SetVelocity")]
    private void OnSetVelocity(InputData input)
        => Joint?.SetLinearMotor(input.Number, PhysSlideConstraintVars.SystemLoadScale);
}

/// <summary>
/// The map keys <see cref="PhysSlideConstraint"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysSlideConstraintVars
{
    /// <summary>
    /// The far end of the travel, as a POINT — Hammer draws the line from the
    /// entity to here, and the direction is the difference. Which end is which
    /// does not matter: native projects both onto the axis and swaps them if
    /// they came out backwards.
    /// </summary>
    [VarName("slideaxis")]
    public Vector AxisEnd { get; set; }

    [VarName("slidefriction")]
    public float Friction { get; set; }

    /// <summary>
    /// Scales the FORCE the motor spends, never its speed. Most maps leave it
    /// at zero, which native reads as one — see <see cref="OnSetVelocity"/>.
    /// </summary>
    public float SystemLoadScale { get; set; }
}
