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
/// A volume a mapper draws to say WHICH entities travel with the player to the
/// next map. It does nothing at all on its own; something else asks about it.
///
/// <para>
/// The asking is done by <c>InTransitionVolume</c> in triggers.cpp, and it is
/// worth being exact about how, because it is what this class has to keep true.
/// It looks the volume up by TARGETNAME — the mapper gives it the same name as
/// the landmark — confirms the classname with <c>FClassnameIs</c>, and then
/// tests the player's bounds against the brushes with a trace. Every one of
/// those is a question about a string and a shape, so a managed shell answers
/// them exactly as the native class did: the entity factory sets the classname,
/// the base shell takes the targetname, and the "model" key gives it the same
/// brushes with the same SOLID_BSP collision.
/// </para>
///
/// <para>
/// What is NOT true here is that anybody asks. Both callers are the save/restore
/// transition path — <c>CChangeLevel::ChangeLevelNow</c> and
/// <c>BuildAdjacentMapList</c> — and this is a deathmatch build.
/// <c>ChangeLevelNow</c> begins "Don't work in deathmatch" and returns before it
/// reaches the volume test, and there are no singleplayer saves for the other to
/// be building an adjacency list during. So a trigger_transition here is a named
/// invisible volume and nothing more, in C# exactly as it was in C++. It is
/// carried rather than dropped because it costs one file and a map that has one
/// should not fail to load.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.TriggerTransition, ScriptedEntityKind.Brush)]
public sealed class TriggerTransition : ScriptedEntity
{
    /// <summary>
    /// Not a <see cref="TriggerVolume"/>, deliberately. Stock derives this from
    /// CPointEntity rather than CBaseTrigger — nothing touches it, it has no
    /// filter, no wait, no enable and none of the six touch outputs — and giving
    /// it a trigger's machinery would put inputs and outputs in front of a
    /// mapper that stock never offered and that nothing would ever fire.
    /// </summary>
    public override void OnSpawn()
    {
        // SOLID_BSP with FSOLID_NOT_SOLID and no FSOLID_TRIGGER, which is stock's
        // Spawn line for line. The BSP part matters more than it looks:
        // TestEntityTriggerIntersection_Accurate opens by asserting the volume is
        // SOLID_BSP and then traces against its collision, so a volume made
        // shapeless would be a debug stop rather than a quiet no-op.
        SetSolidity(BrushSolidity.None);
        SetVisible(false);
    }
}
