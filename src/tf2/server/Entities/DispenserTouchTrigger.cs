//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities;

/// <summary>
/// dispenser_touch_trigger: the brush volume a payload map may name in the
/// cart dispenser's <c>touch_trigger</c> key (stock's
/// <c>CDispenserTouchTrigger</c>, tf_obj_dispenser.cpp:108). The trigger
/// itself fires nothing — it is the dispenser's serve list: a dispenser with
/// one named serves only the players inside the brush, where a dispenser with
/// none serves its radius sphere (stock creates a brush of its own at the
/// radius for that, and the crossings fill the same list).
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class DispenserTouchTrigger(IGameClock clock) : TriggerVolume(clock)
{
    public const string MapClass = "dispenser_touch_trigger";

    /// <summary>Everyone inside the brush: the list the dispenser serves (stock's m_hTouchingEntities, fed by the crossings).</summary>
    public IReadOnlyCollection<EntityHandle> TouchingEntities => Inside;

    // The volume fires nothing itself; holding the touching list is its whole job.
    protected override void Fire(EntityHandle toucher) { }
}
