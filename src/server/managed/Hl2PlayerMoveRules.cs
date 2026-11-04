//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The server's HL2MP player as the movement loop asks it
/// (<see cref="IHl2PlayerMove"/>): <c>CHL2_Player</c>'s speeds, read from
/// the convars each command, and its <c>CanSprint</c>.
/// </summary>
public sealed class Hl2PlayerMoveRules(Hl2WalkSpeedConVar walkSpeed, Hl2NormSpeedConVar normSpeed,
                                       Hl2SprintSpeedConVar sprintSpeed, SvInfiniteAuxPowerConVar infiniteAuxPower,
                                       GlobalStateTable globals) : IHl2PlayerMove
{
    /// <summary>The global that takes the sprint module away.</summary>
    public const string SuitNoSprint = "suit_no_sprint";

    public float WalkSpeed => walkSpeed.Value;

    public float NormSpeed => normSpeed.Value;

    public float SprintSpeed => sprintSpeed.Value;

    public bool InfiniteAuxPower => infiniteAuxPower.Value;

    /// <summary>
    /// <c>CHL2_Player::CanSprint</c> (hl2_player.cpp:1229): enabled, not a
    /// finished duck, not under water, and not without the sprint module.
    /// </summary>
    public bool CanSprint(bool ducked, bool ducking, int waterLevel, bool sprintDisabled)
        => !sprintDisabled
           && !(ducked && !ducking)
           && waterLevel != GameMovement.WlEyes
           && globals.GetState(globals.IndexOf(SuitNoSprint)) != GlobalState.On;
}
