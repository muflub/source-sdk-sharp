//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared;

// The control-point convars stock declares in shared code that the TF2 1.3
// set does not carry (stage 2.4: the objectives read them). Shared source, so
// both sides declare them and a replicated pair keeps one default.
// TFConVarsTests holds each row to the pristine declaration.

// ---- shared/tf/tf_gamerules.cpp ---------------------------------------------

[ConVar("tf_caplinear", "1", Flags = ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly,
        Help = "If set to 1, teams must capture control points linearly.")]
public sealed class TFCapLinearConVar : BoolConVar { }
