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
/// Points in space that DO something to the world, rather than watching it or
/// marking it.
///
/// The distinction from <c>Markers.cs</c> is the whole reason this file exists:
/// an info_target is a coordinate other entities refer to, and moves nothing. A
/// point_teleport picks something up and puts it somewhere else. From
/// <c>Sensors.cs</c> the split is the other way round — a sensor reads and
/// reports, and never reaches into what it is reading.
/// </summary>
/// <summary>
/// Puts a named entity at a fixed place, on demand.
///
/// Distinct from trigger_teleport in who decides: a trigger fires when somebody
/// walks into it and moves whoever that was. This one is fired by map logic and
/// moves an entity the MAP names, which is how a map resets a prop somebody has
/// pushed into a corner.
/// </summary>
[ScriptedEntity(EntityNames.PointTeleport)]
public sealed class PointTeleport : ScriptedEntity
{
    public PointTeleport(IEngineNetworking net)
    {
        Net = net;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;
    /// <summary>What a map may set on this entity. See <see cref="PointTeleportVars"/>.</summary>
    [ScriptVars] public PointTeleportVars Vars = new();

    /// <summary>
    /// Put the target back where the MAP drew it, rather than bringing it here.
    ///
    /// The two readings of this entity, and a mapper picks between them with one
    /// flag: without it, this is a destination; with it, this is a reset button
    /// and the destination is wherever the target started.
    /// </summary>
    private const int ToSpawnPositionFlag = 1;

    private Vector _origin;
    private Vector _angles;

    /// <summary>
    /// Captured at spawn, which is legitimate HERE and would not be in stock.
    ///
    /// Stock does this in Activate, because a CBaseEntity spawns in hierarchical
    /// order and the entity it names may not exist yet. A logical entity on this
    /// mod's parallel hierarchy spawns after the whole CBaseEntity world does —
    /// see docs/divergences.md — so the target is already there, and there is
    /// nothing left for a second pass to wait for.
    ///
    /// The capture must happen now rather than at the first Teleport either way:
    /// the position wanted is where the target STARTED, and by the time somebody
    /// fires the input it has usually been moved. That is the entire point of
    /// the flag.
    /// </summary>
    public override void OnSpawn()
    {
        _origin = Origin;
        _angles = Entities.GetAngles(Handle);

        if ((SpawnFlags & ToSpawnPositionFlag) == 0)
            return;

        var target = Entities.FindByName(Vars.Target);

        if (!Entities.IsValid(target))
        {
            // Stock deletes itself here. This does not: a point_teleport that
            // cannot find its target is a broken map, and an entity that has
            // quietly removed itself is much harder to find in the debugger
            // than one sitting there with a warning beside it.
            Log.Warning($"[SourceSharp] point_teleport '{Name}': no entity named '{Vars.Target}' to read a start position from\n");
            return;
        }

        if (!MayTeleport(target))
        {
            Log.Warning($"[SourceSharp] point_teleport '{Name}': '{Vars.Target}' is parented and cannot be teleported\n");
            return;
        }

        _origin = Entities.GetOrigin(target);
        _angles = Entities.GetAngles(target);
    }

    [ScriptedEvent("Teleport")]
    private void OnTeleport()
    {
        if (Vars.Target.Length == 0)
            return;

        var target = Entities.FindByName(Vars.Target);

        if (!Entities.IsValid(target))
        {
            // Stock returns silently here. This says so, and the reason is a
            // bug this exact silence hid: ss_sandbox's own point_teleport named
            // an entity that did not exist, the input reported that it had been
            // ACCEPTED, and the station demonstrated nothing for as long as
            // nobody fired it by hand. A teleport with nothing to move is
            // always a mistake in the map.
            Log.Warning($"[SourceSharp] point_teleport '{Name}': no entity named '{Vars.Target}' to move\n");
            return;
        }

        // Something carried by something else has its position decided by its
        // parent every frame, so moving it directly is undone before anybody
        // sees it. Refusing and saying so beats a teleport that silently does
        // nothing.
        if (!MayTeleport(target))
        {
            Log.Warning($"[SourceSharp] point_teleport '{Name}': '{Vars.Target}' is parented and cannot be teleported\n");
            return;
        }

        // CPointTeleport::InputTeleport: pTarget->Teleport( origin, angles,
        // NULL ), so the target keeps its velocity (pointteleport.cpp:145; A35).
        Entities.Teleport(target, _origin, _angles, zeroVelocity: false);
    }

    /// <summary>
    /// Whether an entity can be picked up and put down elsewhere.
    ///
    /// Stock spares passengers in a vehicle, whose own behaviour handles the
    /// move. There are no vehicles in this build — they left the server with
    /// the rest of the singleplayer subsystems — so the exception has nothing
    /// to except and the rule is simply "not if it is parented".
    /// </summary>
    private bool MayTeleport(EntityHandle target)
        => (Net.EntityGetState(target, out _) & EntityState.Parented) == 0;
}

/// <summary>
/// The map keys <see cref="PointTeleport"/> declares.
/// </summary>
[ScriptVars]
public partial class PointTeleportVars
{
    /// <summary>Name of the entity to move.</summary>
    public string Target { get; set; } = string.Empty;
}
