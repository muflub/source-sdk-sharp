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

// Stock TF declares default_fov 75 beside fov_desired (clientmode_tf.cpp:113)
// where the core declares 90. SetUpViews moves the viewmodel's FOV by
// default_fov - fov (view.cpp:724-729), so under TF's fov_desired 75 the
// core's number drew the viewmodel at 39, not 54. The client walk reads this
// row; TFServerConVars's TFDefaultFovDefault is the server half's same row,
// and both sides of the replicated pair must answer 75 or the engine warns
// about the mismatch on a listen server.

/// <summary>TF's default field of view.</summary>
[ConVarDefault("default_fov", "75")]
public sealed class TFDefaultFovDefault { }
