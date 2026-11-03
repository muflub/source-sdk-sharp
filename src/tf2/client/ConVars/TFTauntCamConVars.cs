//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The taunt camera's convar, stock's client declares beside the player that
// reads it (c_tf_player.cpp:5533): TurnOnTauntCam takes its distance from it
// when the player carries none yet (:5548).

/// <summary>How far behind the eye the taunt camera sits (c_tf_player.cpp:5533).</summary>
[ConVar("tf_tauntcam_dist", "150", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFTauntcamDistConVar : FloatConVar { }
