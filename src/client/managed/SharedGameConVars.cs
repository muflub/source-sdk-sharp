//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The client's half of the ConVars <c>game/shared/gamevars_shared.cpp</c> and
/// <c>game/shared/sceneentity_shared.cpp</c> declared on this side
/// (plan_client_novalve C8). Both files left client.so: none of their code had
/// an importer, and these declarations were the only thing they still did. A
/// replicated ConVar is a contract between the two DLLs, so the client keeps
/// its copy of each name; the server's are SourceSharp.Server's GameVars.cs.
/// ClientSharedConVarOracleTests holds every name, default, help text and flag
/// to the upstream files with the client's arms (no GAME_DLL callback).
/// </summary>
internal static class SharedGameConVarsDoc
{
}

/// <summary>Client half of <c>mp_forcecamera</c>; the range check is the server's.</summary>
[ConVar("mp_forcecamera", "1", Help = "Restricts spectator modes for dead players",
        Flags = ConVarFlags.Replicated)]
public sealed class ClForceCameraConVar : IntConVar { }

/// <summary>Client half of <c>mp_allowspectators</c>.</summary>
[ConVar("mp_allowspectators", "1.0", Help = "toggles whether the server allows spectator mode or not",
        Flags = ConVarFlags.Replicated)]
public sealed class ClAllowSpectatorsConVar : BoolConVar { }

/// <summary>Client half of <c>mp_friendlyfire</c>.</summary>
[ConVar("mp_friendlyfire", "0", Help = "Allows team members to injure other members of their team",
        Flags = ConVarFlags.Replicated | ConVarFlags.Notify)]
public sealed class ClFriendlyFireConVar : BoolConVar { }

/// <summary>Client half of <c>mp_fadetoblack</c>.</summary>
[ConVar("mp_fadetoblack", "0", Help = "fade a player's screen to black when he dies",
        Flags = ConVarFlags.Replicated | ConVarFlags.Notify)]
public sealed class ClFadeToBlackConVar : BoolConVar { }

/// <summary>Client half of <c>sv_hudhint_sound</c>.</summary>
[ConVar("sv_hudhint_sound", "1", Flags = ConVarFlags.Replicated)]
public sealed class ClHudHintSoundConVar : BoolConVar { }

/// <summary>
/// <c>scene_print</c>: replicated; the scene system it reported on is compiled
/// on neither side, so nothing reads it.
/// </summary>
[ConVar("scene_print", "0", Help = "When playing back a scene, print timing and event info to console.",
        Flags = ConVarFlags.Replicated)]
public sealed class ClScenePrintConVar : BoolConVar { }

/// <summary><c>scene_clientflex</c>: replicated; no reader on this side.</summary>
[ConVar("scene_clientflex", "1", Help = "Do client side flex animation.",
        Flags = ConVarFlags.Replicated)]
public sealed class ClSceneClientFlexConVar : BoolConVar { }

#if DEBUG
/// <summary>
/// <c>r_FadeProps</c>, which game/client/cdll_util.cpp declared under
/// <c>_DEBUG</c> (plan_client_novalve C8: that file left client.so with no
/// importer). Its one stock reader was in the same file, so nothing reads it;
/// it stays declared in a Debug build for the ConVar contract.
/// </summary>
[ConVar("r_FadeProps", "1")]
public sealed class ClFadePropsConVar : BoolConVar { }
#endif
