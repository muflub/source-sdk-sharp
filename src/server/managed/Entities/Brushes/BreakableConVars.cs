//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>func_breakdmg_bullet (props.cpp): what a func_breakable multiplies bullet damage by, before its propdata.</summary>
[ConVar("func_breakdmg_bullet", "0.5")]
public sealed class FuncBreakDmgBulletConVar : FloatConVar { }

/// <summary>func_breakdmg_club: the same for a club.</summary>
[ConVar("func_breakdmg_club", "1.5")]
public sealed class FuncBreakDmgClubConVar : FloatConVar { }

/// <summary>func_breakdmg_explosive: the same for a blast.</summary>
[ConVar("func_breakdmg_explosive", "1.25")]
public sealed class FuncBreakDmgExplosiveConVar : FloatConVar { }

/// <summary>func_break_max_pieces (func_break.cpp): the most gibs one func_breakable throws. Replicated: the client caps its own by it.</summary>
[ConVar("func_break_max_pieces", "15", Flags = ConVarFlags.Archive | ConVarFlags.Replicated)]
public sealed class FuncBreakMaxPiecesConVar : IntConVar { }

/// <summary>func_break_reduction_factor: what PerformanceMode's reduced gibs scales the count by.</summary>
[ConVar("func_break_reduction_factor", ".5")]
public sealed class FuncBreakReductionFactorConVar : FloatConVar { }

/// <summary>breakable_multiplayer (props.cpp): in multiplayer a broken prop's pieces are the clients' to make.</summary>
[ConVar("breakable_multiplayer", "1")]
public sealed class BreakableMultiplayerConVar : BoolConVar { }

/// <summary>breakable_disable_gib_limit (props.cpp): PerformanceMode does not trim gibs.</summary>
[ConVar("breakable_disable_gib_limit", "0")]
public sealed class BreakableDisableGibLimitConVar : BoolConVar { }
