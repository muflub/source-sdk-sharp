//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Server;

// The medigun and healing convars that stock declares under a GAME_DLL arm of
// shared code (tf_player_shared.cpp:136), so only the server declares them.
// TFConVarsTests holds the row to the pristine declaration and its side.

/// <summary>The help text keeps stock's typo ("is takes") byte for byte: TFConVarsTests compares it to the pristine source.</summary>
[ConVar("tf_boost_drain_time", "15.0", Flags = ConVarFlags.DevelopmentOnly,
        Help = "Time is takes for a full health boost to drain away from a player.", Min = 0.1f)]
public sealed class TFBoostDrainTimeConVar : FloatConVar { }
