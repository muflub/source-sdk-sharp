//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The spy convar stock's client declares under its own file
// (c_tf_player.cpp:1702), so only the client declares it.

/// <summary>
/// The invisibility cap the cloak proxies' effective level applies to anyone
/// the local view does not see as an enemy (<c>GetEffectiveInvisibilityLevel</c>,
/// c_tf_player.cpp:6854-6862).
/// </summary>
[ConVar("tf_teammate_max_invis", "0.95", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFTeammateMaxInvisConVar : FloatConVar { }
