//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared;

// TF2's spy convars that Stage 2.12's cloak, disguise and knife read, declared
// in SHARED code as stock declares them (tf_player_shared.cpp:115-116, 166-168).
// TFConVarsTests holds each to the pristine declaration.

[ConVar("tf_spy_invis_time", "1.0", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated, Help = "Transition time in and out of spy invisibility", Min = 0.1f, Max = 5.0f)]
public sealed class TFSpyInvisTimeConVar : FloatConVar { }

[ConVar("tf_spy_invis_unstealth_time", "2.0", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated, Help = "Transition time in and out of spy invisibility", Min = 0.1f, Max = 5.0f)]
public sealed class TFSpyInvisUnstealthTimeConVar : FloatConVar { }

[ConVar("tf_spy_cloak_consume_rate", "10.0", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated, Help = "cloak to use per second while cloaked, from 100 max )")]
public sealed class TFSpyCloakConsumeRateConVar : FloatConVar { }

[ConVar("tf_spy_cloak_regen_rate", "3.3", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated, Help = "cloak to regen per second, up to 100 max")]
public sealed class TFSpyCloakRegenRateConVar : FloatConVar { }

[ConVar("tf_spy_cloak_no_attack_time", "2.0", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated, Help = "time after uncloaking that the spy is prohibited from attacking")]
public sealed class TFSpyCloakNoAttackTimeConVar : FloatConVar { }
