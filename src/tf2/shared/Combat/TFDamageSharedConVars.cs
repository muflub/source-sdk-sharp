//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Combat;

// The damage layer's convar that stock declares in shared code outside any
// #if (tf_player_shared.cpp:170), so both DLLs declare it. TFConVarsTests
// holds it to the pristine declaration.

/// <summary>
/// <c>tf_damage_disablespread</c>: no random spread on player damage, the band's
/// floor plus the spread instead (<c>ApplyOnDamageModifyRules</c>). On by default.
/// </summary>
[ConVar("tf_damage_disablespread", "1", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Toggles the random damage spread applied to all player damage.")]
public sealed class TFDamageDisableSpreadConVar : BoolConVar { }
