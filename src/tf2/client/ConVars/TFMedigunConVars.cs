//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The medigun convar that stock declares under a CLIENT_DLL arm of shared code
// (tf_weapon_medigun.cpp:82), so only the client declares it. The server reads
// it per player as a client convar, as stock's ItemPostFrame does through
// GetClientConVarValue; TFConVarsTests holds the row to the pristine
// declaration and its side.

/// <summary>Stock's toggle attack: with it on, holding is not needed to keep the beam on its target.</summary>
[ConVar("tf_medigun_autoheal", "0", Flags = ConVarFlags.Archive | ConVarFlags.UserInfo,
        Help = "Setting this to 1 will cause the Medigun's primary attack to be a toggle instead of needing to be held down.")]
public sealed class TFMedigunAutoHealConVar : BoolConVar { }
