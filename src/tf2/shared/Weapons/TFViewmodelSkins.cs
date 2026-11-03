//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The team arm of stock's <c>CTFViewModel::GetSkin</c>
/// (tf_viewmodel.cpp:362-398), shared by both sides' rules: the server's
/// <see cref="TF.Server.Weapons.TFViewmodelTeamSkinRule"/> dresses the
/// networked state with it and the client's
/// <see cref="TF.Client.Weapons.TFClientViewmodelTeamSkinRule"/> predicts the
/// same byte in the mirror, which is why the two must not drift.
/// </summary>
public static class TFViewmodelSkins
{
    /// <summary>
    /// The item's own skin for <paramref name="team"/> first
    /// (<c>CEconItemView::GetSkin</c>, the viewmodel arm) and — when the item
    /// names none — the weapon script's <c>HasTeamSkins_Viewmodel</c> answer,
    /// RED 0 / BLU 1. A team that is neither and a weapon with neither leave
    /// <paramref name="chosen"/> standing, as stock's switch falls through to
    /// the base class's skin.
    /// </summary>
    public static int? ForTeam(TFWeaponTypes types, TFWeaponScripts scripts, int team,
                               WeaponType type, int? chosen)
    {
        if (team != (int)TFTeam.Red && team != (int)TFTeam.Blue)
            return chosen;

        // See if the item wants to override the skin (:374-380).
        if (types.BaseItemFor(type.Name)?.ViewmodelSkin((TFTeam)team) is { } itemSkin)
            return itemSkin;

        // Else the script's team-skins flag: RED 0, BLU 1 (:386-396).
        if (scripts.Get(type.Name) is { HasTeamSkinsViewmodel: true })
            return team - (int)TFTeam.Red;

        return chosen;
    }
}
