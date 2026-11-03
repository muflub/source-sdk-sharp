//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared;

// The flag's shared constants (tf_shareddefs.h), named as TFSharedDefs.cs
// names its enums: the stock prefix dropped, the rest PascalCase.
// TFSharedDefsTests holds each to the pristine header.

/// <summary>
/// <c>ETFFlagType</c>: the game an <c>item_teamflag</c> is played in (its
/// <c>GameType</c> key). The milestone mode plays <see cref="Ctf"/>.
/// </summary>
public enum TFFlagType
{
    Ctf = 0,
    AttackDefend,
    TerritoryControl,
    Invade,
    ResourceControl,
    RobotDestruction,
    PlayerDestruction,
}

/// <summary>
/// <c>TF_FLAGINFO_*</c>: where a flag is. Stock writes these as bits
/// (<c>STOLEN 1&lt;&lt;0</c>, <c>DROPPED 1&lt;&lt;1</c>) but only ever holds one.
/// </summary>
public enum TFFlagStatus
{
    Home = 0,
    Stolen = 1 << 0,
    Dropped = 1 << 1,
}

/// <summary><c>ETFFlagEventTypes</c>: the <c>teamplay_flag_event</c> event's <c>eventtype</c>.</summary>
public enum TFFlagEvent
{
    Pickup = 1,
    Capture,
    Defend,
    Dropped,
    Returned,
    Count,
}
