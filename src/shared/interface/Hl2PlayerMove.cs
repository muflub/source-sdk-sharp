//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// What HL2MP's player answers the movement loop: the side's half of
/// <c>CHL2_Player</c> / <c>C_HL2MP_Player</c>'s <c>HandleSpeedChanges</c>
/// and <c>ReduceTimers</c>, which CHL2GameMovement's <c>CheckParameters</c>
/// and <c>ReduceTimers</c> call so the speeds and the suit's power are
/// predicted (hl_gamemovement.cpp). A loop handed none moves at the
/// player's own max speed with no suit, as base <c>CGameMovement</c>.
/// </summary>
public interface IHl2PlayerMove
{
    /// <summary><c>hl2_walkspeed</c>: +walk's speed (stock 150).</summary>
    float WalkSpeed { get; }

    /// <summary><c>hl2_normspeed</c>: the run (stock 190).</summary>
    float NormSpeed { get; }

    /// <summary><c>hl2_sprintspeed</c>: +speed's, while the suit has power (stock 320).</summary>
    float SprintSpeed { get; }

    /// <summary><c>sv_infinite_aux_power</c>: the suit never drains (a cheat).</summary>
    bool InfiniteAuxPower { get; }

    /// <summary>
    /// <c>CanSprint</c>, which the two sides answer differently in stock: the
    /// server's (<c>CHL2_Player</c>) also asks whether sprint is enabled (a
    /// +USE carry turns it off) and whether the <c>suit_no_sprint</c> global
    /// is on, and refuses only a duck that has finished; the client's
    /// (<c>C_HL2MP_Player</c>) refuses any duck.
    /// </summary>
    bool CanSprint(bool ducked, bool ducking, int waterLevel, bool sprintDisabled);
}
