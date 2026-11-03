//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared;

// The medigun and healing convars that stock declares in shared code
// (tf_player_shared.cpp:120,121, outside any #if), so both DLLs declare them.
// TFConVarsTests holds each row to the pristine declaration and its side.

/// <summary>Stock reads it as a factor: <c>GetMaxBuffedHealth</c> multiplies the buffing max health by it.</summary>
[ConVar("tf_max_health_boost", "1.5", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated,
        Help = "Max health factor that players can be boosted to by healers.", Min = 1.0f)]
public sealed class TFMaxHealthBoostConVar : FloatConVar { }

[ConVar("tf_invuln_time", "1.0", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated,
        Help = "Time it takes for invulnerability to wear off.")]
public sealed class TFInvulnTimeConVar : FloatConVar { }

// ---- shared/tf/tf_weapon_medigun.cpp (outside any #if) ----------------------

/// <summary>The meter's build: seconds of healing to a full charge, read by the server's
/// charge arm and the client's predicted replay alike (<c>tf_weapon_medigun.cpp:1272</c>).</summary>
[ConVar("weapon_medigun_charge_rate", "40", Flags = ConVarFlags.Cheat | ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly,
        Help = "Amount of time healing it takes to fully charge the medigun.")]
public sealed class WeaponMedigunChargeRateConVar : FloatConVar { }
