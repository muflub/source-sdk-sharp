//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Player;

using TF2.Client.Player;

namespace TF2.Client.Weapons;

/// <summary>
/// The client's half of <c>CTFViewModel::GetSkin</c>
/// (tf_viewmodel.cpp:362-398): the same answer the server's
/// <see cref="TF.Server.Weapons.TFViewmodelTeamSkinRule"/> dressed the
/// networked state with, asked by the mirror's predicted deploy so the
/// replayed switch writes the byte the server's run wrote instead of
/// mispredicting it for a round trip. The team is the player resource's
/// column for the player asked about, 0 until it arrives — the same
/// word the scoreboard reads.
/// </summary>
[SideService(Side.Client)]
public sealed class TFClientViewmodelTeamSkinRule(TFWeaponTypes types, TFWeaponScripts scripts,
                                                  TFClientPlayerResources resources)
    : IViewmodelSkinRule, IOrderedHook
{
    /// <summary>
    /// The rule's order: the weapons band, the server rule's slot +1 — the
    /// census holds one order per participant across both sides, and the
    /// client chain is its own registry, so the mirror rule stands beside the
    /// server's, not on it.
    /// </summary>
    public const int Order = 1_000_000 - 1_000 + 223;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public int? Skin(int playerIndex, WeaponType type, int? chosen)
    {
        int team = resources.Live is { } live && playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? live.State.Team[playerIndex]
            : 0;

        return TFViewmodelSkins.ForTeam(types, scripts, team, type, chosen);
    }
}
