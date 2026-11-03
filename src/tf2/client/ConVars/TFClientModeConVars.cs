//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The view convar stock's client mode declares (clientmode_tf.cpp:114),
// declared by the TF2 client library. The server reads it per player as a
// client convar and writes the clamped value onto the networked record
// (tf_gamerules.cpp:10275-10279) the view answers unzoomed from; the local
// value is the userinfo the server sees. TFConVarsTests holds the row to the
// pristine declaration and its side.

/// <summary>The base field-of-view the player's view answers with, unzoomed.</summary>
[ConVar("fov_desired", "75", Flags = ConVarFlags.Archive | ConVarFlags.UserInfo,
        Help = "Sets the base field-of-view.", Min = 20f, Max = 90f)]
public sealed class FovDesiredConVar : FloatConVar { }
