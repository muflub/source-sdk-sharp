//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Weapons;

/// <summary>
/// Stock's <c>CTFViewModel::GetSkin</c> (tf_viewmodel.cpp:362-398) as an
/// <see cref="IViewmodelSkinRule"/>: the item's own skin for the OWNER'S TEAM
/// (<c>CEconItemView::GetSkin</c> team, viewmodel arm) first, and — when the
/// item has none — the weapon script's <c>HasTeamSkins_Viewmodel</c> answer,
/// RED 0 / BLU 1.
///
/// <para>
/// This is the bug the chain exists for: the live game drew a BLU medic's
/// <c>c_medic_arms</c> in the RED sleeve. The medigun's item names no skins
/// (no styles block) and its script has <c>HasTeamSkins_Viewmodel</c>, so
/// stock's second arm decides: the team's skin of the model, which is
/// red-grey at 0 and blue-grey at 1. A team that is neither answers no
/// opinion, as stock's switch falls through to the base class's skin.
/// Registered on the SERVER: the skin is the viewmodel's networked state, and
/// the attachment's m_nSkin is written beside it.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFViewmodelTeamSkinRule(TFWeaponTypes types, TFWeaponScripts scripts, IEnginePlayers players)
    : IViewmodelSkinRule, IOrderedHook
{
    /// <summary>The rule's order: the weapons band, after the hands model rule (+221).</summary>
    public const int Order = 1_000_000 - 1_000 + 222;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public int? Skin(int playerIndex, WeaponType type, int? chosen)
    {
        int team = players.GetTeam(players.GetHandle(playerIndex));
        return TFViewmodelSkins.ForTeam(types, scripts, team, type, chosen);
    }
}
