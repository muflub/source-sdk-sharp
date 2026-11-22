//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>Client half of <c>spec_freeze_time</c> (c_baseplayer.cpp:112): how long the freeze frame holds.</summary>
[ConVar("spec_freeze_time", "4.0", Help = "Time spend frozen in observer freeze cam.",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated)]
public sealed class ClSpecFreezeTimeConVar : FloatConVar { }

/// <summary>Client half of <c>spec_freeze_traveltime</c> (c_baseplayer.cpp:113): the camera's flight to its frame.</summary>
[ConVar("spec_freeze_traveltime", "0.4", Help = "Time taken to zoom in to frame a target in observer freeze cam.",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated, Min = 0.01f)]
public sealed class ClSpecFreezeTravelTimeConVar : FloatConVar { }

/// <summary><c>spec_freeze_distance_min</c> (c_baseplayer.cpp:114): the nearest the freeze cam frames its target from.</summary>
[ConVar("spec_freeze_distance_min", "96", Help = "Minimum random distance from the target to stop when framing them in observer freeze cam.",
        Flags = ConVarFlags.Cheat)]
public sealed class SpecFreezeDistanceMinConVar : FloatConVar { }

/// <summary><c>spec_freeze_distance_max</c> (c_baseplayer.cpp:115): the farthest.</summary>
[ConVar("spec_freeze_distance_max", "200", Help = "Maximum random distance from the target to stop when framing them in observer freeze cam.",
        Flags = ConVarFlags.Cheat)]
public sealed class SpecFreezeDistanceMaxConVar : FloatConVar { }

/// <summary>CViewRender::FreezeFrame: hold the captured picture <paramref name="freezeTime"/> seconds; 0 ends it.</summary>
public interface IViewFreezeFrame
{
    void ViewFreezeFrame(float freezeTime, float curTime);
}
