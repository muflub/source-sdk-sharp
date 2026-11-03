//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The player convar stock's TF client declares (c_tf_player.cpp:211), declared
// by the TF2 client library. The server reads it per player as a client convar,
// as stock's ClientSettingsChanged does through GetClientConVarValue
// (tf_gamerules.cpp:10269); TFConVarsTests holds the row to the pristine
// declaration and its side.

/// <summary>Stock's idle top-up: clip weapons reload alone between shots
/// (c_tf_player.cpp:211, read back by the weapon's post frame,
/// tf_weaponbase.cpp:2454).</summary>
[ConVar("cl_autoreload", "1", Flags = ConVarFlags.UserInfo | ConVarFlags.Archive,
        Help = "When set to 1, clip-using weapons will automatically be reloaded whenever they're not being fired.")]
public sealed class ClAutoReloadConVar : BoolConVar { }
