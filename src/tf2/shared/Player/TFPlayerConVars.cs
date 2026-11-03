//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Player;

// ---- shared/tf/tf_gamerules.cpp ---------------------------------------------

/// <summary><c>CTFGameRules::GetClassLimit</c>'s limit outside tournament and highlander mode (0: none).</summary>
[ConVar("tf_classlimit", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Limit on how many players can be any class (i.e. tf_class_limit 2 would limit 2 players per class).\n",
        Min = 0)]
public sealed class TFClassLimitConVar : IntConVar { }
