//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary><c>sv_unlag</c> (player_lagcompensation.cpp:33).</summary>
[ConVar("sv_unlag", "1", Flags = ConVarFlags.DevelopmentOnly, Help = "Enables player lag compensation")]
public sealed class SvUnlagConVar : BoolConVar { }

/// <summary><c>sv_maxunlag</c> (player_lagcompensation.cpp:34), bounded to [0, 1].</summary>
[ConVar("sv_maxunlag", "1.0", Flags = ConVarFlags.DevelopmentOnly, Min = 0f, Max = 1f,
        Help = "Maximum lag compensation in seconds")]
public sealed class SvMaxUnlagConVar : FloatConVar { }

/// <summary><c>sv_lagflushbonecache</c> (player_lagcompensation.cpp:35).</summary>
[ConVar("sv_lagflushbonecache", "1", Flags = ConVarFlags.DevelopmentOnly,
        Help = "Flushes entity bone cache on lag compensation")]
public sealed class SvLagFlushBoneCacheConVar : BoolConVar { }

/// <summary><c>sv_lagcompensation_teleport_dist</c> (player_lagcompensation.cpp:28).</summary>
[ConVar("sv_lagcompensation_teleport_dist", "64", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Cheat,
        Help = "How far a player got moved by game code before we can't lag compensate their position back")]
public sealed class SvLagCompensationTeleportDistConVar : FloatConVar { }

/// <summary>The lag compensation's settings, read from its four convars as they stand.</summary>
public sealed class LagCompensationConVars(SvUnlagConVar unlag, SvMaxUnlagConVar maxUnlag,
                                           SvLagFlushBoneCacheConVar flushBoneCache,
                                           SvLagCompensationTeleportDistConVar teleportDistance)
    : ILagCompensationSettings
{
    public bool Unlag => unlag.Value;

    public float MaxUnlag => maxUnlag.Value;

    public bool FlushBoneCache => flushBoneCache.Value;

    public float TeleportDistance => teleportDistance.Value;
}
