//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>spec_freeze_time</c> (player.cpp:92, the non-CSTRIKE arm): how long the
/// freeze cam holds its frame. Replicated: the client freezes the view for it.
/// </summary>
[ConVar("spec_freeze_time", "4.0", Help = "Time spend frozen in observer freeze cam.",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated)]
public sealed class SpecFreezeTimeConVar : FloatConVar { }

/// <summary>
/// <c>spec_freeze_traveltime</c> (player.cpp:93): how long the freeze cam
/// takes to fly to its frame of the killer.
/// </summary>
[ConVar("spec_freeze_traveltime", "0.4", Help = "Time taken to zoom in to frame a target in observer freeze cam.",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated, Min = 0.01f)]
public sealed class SpecFreezeTravelTimeConVar : FloatConVar { }
