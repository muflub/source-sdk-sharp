//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// item_teamflag_return_icon: the marker over a dropped flag (stock's
/// <c>CCaptureFlagReturnIcon</c>, shared/tf/entity_capture_flag.cpp:2648-2677).
/// The flag's <see cref="ItemTeamFlag"/> creates and destroys it as the flag
/// drops and returns; it carries no state of its own — stock's
/// <c>DT_CaptureFlagReturnIcon</c> send table is empty.
///
/// <para>
/// The ring art the client draws from the flag's return progress
/// (<c>DrawReturnProgressBar</c>) is not ported: the countdown shows in the
/// HUD's flag row, which reads the objective resource (docs/divergences.md).
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Effect)]
public sealed class ItemTeamFlagReturnIcon(IEngineEntityRender render) : ScriptedEntity
{
    public const string MapClass = "item_teamflag_return_icon";

    /// <summary>
    /// <c>cl_flag_return_height</c>'s default (entity_capture_flag.cpp:48):
    /// how far above the flag it hangs. Stock's convar is not declared — the
    /// height is a constant of the effect (docs/divergences.md).
    /// </summary>
    public const float Height = 82f;

    /// <summary>Spawn (entity_capture_flag.cpp:2665-2677): the ±50 collision bounds and FL_EDICT_ALWAYS.</summary>
    public override void OnSpawn()
    {
        // Spawn's UTIL_SetSize( ±8 ) is subsumed by the ±50 collision bounds
        // the same lines set — the wider pair is the one a trace reads.
        Entities.SetBounds(Handle, new Vector(-50f, -50f, -50f), new Vector(50f, 50f, 50f));
        render.SetTransmitAlways(Handle, true);
    }
}
