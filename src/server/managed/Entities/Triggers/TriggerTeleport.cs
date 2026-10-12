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
/// Moves whoever walks in to somewhere else in the map.
/// </summary>
[ScriptedEntity(EntityNames.TriggerTeleport, ScriptedEntityKind.Brush)]
public sealed class TriggerTeleport : TriggerVolume
{
    public TriggerTeleport(IGameClock clock)
        : base(clock)
    {
    }

    /// <summary>
    /// trigger_teleport's own spawnflag. It used to sit in
    /// <see cref="TriggerSpawnflags"/>, which is the SHARED set every trigger
    /// reads to decide what counts as touching it -- and 32 is not a member of
    /// that set at all, so keeping it there offered every other trigger a bit
    /// with no meaning and a gap where a shared one would be.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None           = 0b0000_0000,

        PreserveAngles = 0b0010_0000,
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="TriggerTeleportVars"/>.</summary>
    [ScriptVars] public TriggerTeleportVars TriggerTeleportVars = new();

    protected override void Fire(EntityHandle toucher)
    {
        if (TriggerTeleportVars.Target.Length == 0)
            return;

        var destination = Entities.FindByName(TriggerTeleportVars.Target);

        // A map naming a destination that does not exist is a common mistake,
        // and teleporting to nowhere would drop the player out of the world.
        if (!Entities.IsValid(destination))
        {
            Log.Warning($"[SourceSharp] trigger_teleport has no destination named '{TriggerTeleportVars.Target}'\n");
            return;
        }

        // CTriggerTeleport::Touch (triggers.cpp:2385-2451; A35). A landmark keeps
        // the toucher's offset from it; with none, the toucher takes the
        // destination's facing unless the map asked to keep its own. The
        // velocity is never touched: stock passes NULL ("BUGBUG"), so momentum
        // carries through the teleport.
        Vector offset = Vector.Zero;
        bool landmark = false;
        if (TriggerTeleportVars.Landmark.Length > 0)
        {
            EntityHandle mark = Entities.FindByName(TriggerTeleportVars.Landmark);
            if (Entities.IsValid(mark))
            {
                offset = Entities.GetOrigin(toucher) - Entities.GetOrigin(mark);
                landmark = true;
            }
        }

        Vector? angles = !landmark && !Flags.HasFlag(SpawnFlag.PreserveAngles)
            ? Entities.GetAngles(destination)
            : null;
        Entities.Teleport(toucher, Entities.GetOrigin(destination) + offset, angles, zeroVelocity: false);

        OnTrigger.Fire();
    }
}

/// <summary>
/// The map keys <see cref="TriggerTeleport"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerTeleportVars
{
    /// <summary>Name of the entity to arrive at. Usually an info_teleport_destination.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// A landmark: the toucher keeps its offset from it, arriving at the same
    /// offset from the destination (m_iLandmark).
    /// </summary>
    public string Landmark { get; set; } = string.Empty;
}
