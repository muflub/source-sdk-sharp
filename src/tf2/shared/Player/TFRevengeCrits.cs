//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Player;

/// <summary>
/// The shared purse of revenge crits — stock's
/// <c>CTFPlayerShared::m_iRevengeCrits</c> with its setter's clamp
/// (<c>SetRevengeCrits</c>, tf_player_shared.cpp:14472-14488). The grants are
/// the server's extinguishes: the Manmelter's vacuum
/// (<c>CTFFlareGun_Revenge::ChargePostFrame</c>, tf_weapon_flaregun.cpp:549-556),
/// the Diamondback's backstabs and the sapper's removal — so the purse rides
/// the player's state (<see cref="TFPlayerNetState.RevengeCrits"/>) next to
/// the condition its count bears.
/// </summary>
public static class TFRevengeCrits
{
    /// <summary>The setter's clamp (tf_player_shared.cpp:14474).</summary>
    public const int MaxCrits = 35;

    /// <summary>The count a grant or a spend leaves, stock's clamp.</summary>
    public static int Clamped(int crits) => Math.Clamp(crits, 0, MaxCrits);
}
