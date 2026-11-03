//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The sniper rifle's convar, stock declares under a CLIENT_DLL arm of the
// shared weapon (tf_weapon_sniperrifle.cpp:48-50), so only the client declares
// it. TFConVarsTests holds the row to the pristine declaration and its side.

/// <summary>The fully charged rifle's bell, off until asked for (the client charge arms, tf_weapon_sniperrifle.cpp:391, :1936).</summary>
[ConVar("tf_sniper_fullcharge_bell", "0", Flags = ConVarFlags.Archive)]
public sealed class TFSniperFullChargeBellConVar : BoolConVar { }
