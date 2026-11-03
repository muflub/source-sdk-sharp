//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared;

// TF2's player convars that the movement rules (stage1 1.8) read, declared in
// SHARED code as stock declares them (tf_player_shared.cpp:113,118, outside
// any #if). TFConVarsTests holds each to the pristine declaration.

[ConVar("tf_scout_air_dash_count", "1", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFScoutAirDashCountConVar : IntConVar { }

[ConVar("tf_spy_max_cloaked_speed", "999", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated)]
public sealed class TFSpyMaxCloakedSpeedConVar : FloatConVar { }
