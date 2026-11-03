//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Server.Combat;

// The damage layer's convars that stock declares in the game DLL only: a
// GAME_DLL arm of tf_gamerules.cpp (:672, :3871) or a server/ file
// (tf_player.cpp). TFConVarsTests holds each to the pristine declaration.

/// <summary><c>tf_damage_range</c> (<c>server/tf/tf_player.cpp:197</c>): the range variance's half-width as a fraction of the damage.</summary>
[ConVar("tf_damage_range", "0.5", Flags = ConVarFlags.DevelopmentOnly)]
public sealed class TFDamageRangeConVar : FloatConVar { }

/// <summary><c>tf_debug_damage</c> (<c>server/tf/tf_player.cpp:8791</c>): the modify rules print each hit's arithmetic.</summary>
[ConVar("tf_debug_damage", "0", Flags = ConVarFlags.Cheat)]
public sealed class TFDebugDamageConVar : BoolConVar { }

/// <summary><c>tf_weapon_criticals_distance_falloff</c> (<c>tf_gamerules.cpp:733</c>).</summary>
[ConVar("tf_weapon_criticals_distance_falloff", "0", Flags = ConVarFlags.Cheat,
        Help = "Critical weapon damage will take distance into account.")]
public sealed class TFWeaponCriticalsDistanceFalloffConVar : BoolConVar { }

/// <summary><c>tf_weapon_minicrits_distance_falloff</c> (<c>tf_gamerules.cpp:734</c>).</summary>
[ConVar("tf_weapon_minicrits_distance_falloff", "0", Flags = ConVarFlags.Cheat,
        Help = "Mini-crit weapon damage will take distance into account.")]
public sealed class TFWeaponMinicritsDistanceFalloffConVar : BoolConVar { }

/// <summary><c>tf_fall_damage_disablespread</c> (<c>tf_gamerules.cpp:13366</c>): no 0.8-1.2 random factor on fall damage.</summary>
[ConVar("tf_fall_damage_disablespread", "0")]
public sealed class TFFallDamageDisableSpreadConVar : BoolConVar { }

/// <summary>The cloaked spy's damage scale (<c>tf_gamerules.cpp:690</c>, a GAME_DLL arm; no help in stock).</summary>
[ConVar("tf_stealth_damage_reduction", "0.8", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFStealthDamageReductionConVar : FloatConVar { }
