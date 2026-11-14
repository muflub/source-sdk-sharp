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
/// Stock's <c>CTFWeaponBase::GetWorldModel</c> (tf_weaponbase.cpp:681-701) as
/// an <see cref="IWorldModelRule"/>: the HOLDER's item names the model the
/// weapon entity wears — its <c>model_world</c>
/// (<c>CEconItemView::GetWorldDisplayModel</c> over
/// <c>CEconItemDefinition::GetWorldDisplayModel</c>,
/// econ_item_view.cpp:1034) first, else its player display model for the
/// holder's class (<c>GetPlayerDisplayModel( iClass, iTeam )</c> over
/// <c>GetPlayerDisplayModel( iClass )</c>, econ_item_view.cpp:924-970:
/// <c>model_player_per_class</c>, else <c>model_player</c>). This is why an
/// unlock that shares a weapon class — the Gloves of Running Urgently on
/// <c>tf_weapon_fists</c> — wears its own shape in everyone's viewmodel
/// attachment and third person while the row stays the base item's.
///
/// <para>
/// A holder with no item for the row, or an item that names neither, answers
/// no opinion: stock's fall-through is <c>BaseClass::GetWorldModel()</c>, the
/// row's own. The style and team-visual arms of
/// <c>GetPlayerDisplayModel</c> wait for the styles' player models being
/// parsed (the wearables port keeps the same shape,
/// <see cref="TF2.Server.Player.TFWearables"/>). Registered on the SERVER: the
/// model is the entity's networked state, not predicted input.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFItemWorldModelRule(ITFWeaponOwners owners) : IWorldModelRule, IOrderedHook
{
    /// <summary>The rule's order: the weapons band, after both skin rules (+222, +223).</summary>
    public const int Order = 1_000_000 - 1_000 + 224;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public string? Model(int playerIndex, WeaponType type, string? chosen)
    {
        if (owners.ItemOf(playerIndex, type.Name) is not AttributeContainer { Item: { } item })
            return chosen;

        if (!string.IsNullOrEmpty(item.ModelWorld))
            return item.ModelWorld;

        // The base display model is the per-class walk's fall-through for
        // every class (GetBasePlayerDisplayModel, econ_item_view.cpp:969).
        string? player = item.PlayerModel(owners.ClassOf(playerIndex));
        if (string.IsNullOrEmpty(player))
            player = item.ModelPlayer;

        return !string.IsNullOrEmpty(player) ? player : chosen;
    }
}
