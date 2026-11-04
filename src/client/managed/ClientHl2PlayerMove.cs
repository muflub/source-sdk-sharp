//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The predicting client's HL2MP player as the replay's movement loop asks
/// it (<see cref="IHl2PlayerMove"/>): <c>C_HL2MP_Player</c>'s speeds, read
/// from the replicated convars, and its <c>CanSprint</c>.
/// </summary>
public sealed class ClientHl2PlayerMove(Hl2WalkSpeedConVar walkSpeed, Hl2NormSpeedConVar normSpeed,
                                        Hl2SprintSpeedConVar sprintSpeed, SvInfiniteAuxPowerConVar infiniteAuxPower)
    : IHl2PlayerMove
{
    public float WalkSpeed => walkSpeed.Value;

    public float NormSpeed => normSpeed.Value;

    public float SprintSpeed => sprintSpeed.Value;

    public bool InfiniteAuxPower => infiniteAuxPower.Value;

    /// <summary>
    /// <c>C_HL2MP_Player::CanSprint</c> (c_hl2mp_player.cpp:781): no duck at
    /// all and not under water. Stock's client neither knows the sprint is
    /// disabled nor reads the global, so neither is asked here.
    /// </summary>
    public bool CanSprint(bool ducked, bool ducking, int waterLevel, bool sprintDisabled)
        => !ducked && !ducking && waterLevel != GameMovement.WlEyes;
}
