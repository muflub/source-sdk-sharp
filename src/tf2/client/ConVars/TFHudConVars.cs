//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client;

// The hud_ convars stock's TF client declares (tf_hud_account.cpp), declared
// by the TF2 client library since the combat text: names, defaults and flags
// as stock wrote them (TFConVarsTests holds the row). The batching family is
// not declared — our text does not batch yet, and a convar with no reader is
// a dead switch.

/// <summary>Stock's combat-text switch (tf_hud_account.cpp:79): off hides
/// the floating text.</summary>
[ConVar("hud_combattext", "1", Flags = ConVarFlags.UserInfo | ConVarFlags.Archive | ConVarFlags.ArchiveXbox)]
public sealed class HudCombatTextConVar : BoolConVar { }

/// <summary>Stock's server-time switch (tf_hud_objectivestatus.cpp:42): on
/// shows the clock's <c>mp_timelimit</c> band
/// (tf_time_panel.cpp:741-808).</summary>
[ConVar("tf_hud_show_servertimelimit", "0", Flags = ConVarFlags.Archive,
        Help = "Display time left before the current map ends.")]
public sealed class TFHudShowServerTimeLimitConVar : BoolConVar { }

/// <summary>Stock's notification duration (tf_hud_notification_panel.cpp:29):
/// how long a notification panel shows before it fades.</summary>
[ConVar("tf_hud_notification_duration", "3.0",
    Help = "How long to display hud notification panels before fading them")]
public sealed class TFHudNotificationDurationConVar : FloatConVar { }
