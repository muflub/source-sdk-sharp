//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Vgui;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The timed item's charge reads (tf_hud_itemeffectmeter.cpp): the bar off
/// the held weapon's own networked field (m_flItemChargeMeter on the carried
/// blob, tf_player_shared.cpp:7832 — the replayed prediction while it is
/// fresh, as the medic meter reads its BehaviourMeter), present when the
/// held item's <c>item_meter_charge_rate</c> is set (stock's meter stands up
/// for the item that carries one), and the label as the item's
/// <c>meter_label</c> string attribute (SetLabelText's plain read, :607-628).
/// A buff item rides the same bar through its <c>GetProgress</c>: the local
/// player's rage meter over 100 (tf_weapon_buff_item.cpp:444-451), so its
/// <c>set_buff_type</c> keeps the meter present and reads the carrier
/// (<see cref="TFRageMeter"/>).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFItemMeter)])]
public sealed class TFHudItemMeter(IEngineHud screen, ILocalArsenal arsenal,
                                   IClientStandalones standalones, IWeaponDrawStates states,
                                   ITFWeaponOwners owners, AttributeHooks hooks, ITFPlayerData data) : ITFItemMeter
{
    public bool Present
        => arsenal.TryGetActive(out string name, out _, out _)
           && (hooks.Float(0f, TFItemMeter.ChargeRateAttribute, ItemOf(name)) > 0f
               || BuffTypeOf(name) > 0);

    public float Charge
        => arsenal.TryGetActive(out string name, out _, out _) && BuffTypeOf(name) > 0
            // The buff item's bar is the player's rage meter over full, stock's
            // GetProgress (tf_weapon_buff_item.cpp:444-451).
            ? Math.Clamp((data.Networked(screen.LocalPlayerIndex)?.RageMeter ?? 0f)
                         / TFRageMeter.FullMeter, 0f, 1f)
            : Carried() is { } weapon
                ? Math.Clamp((states.PredictedStateOf(weapon) ?? weapon.State).ItemMeter, 0f, 1f)
                : 0f;

    public string Label
        => arsenal.TryGetActive(out string name, out _, out _)
            ? hooks.String("", TFItemMeter.LabelAttribute, ItemOf(name)) ?? ""
            : "";

    /// <summary>The local player's carried weapon — the one entity the core
    /// holds per armed player, as the medic meter's read.</summary>
    private ClientSsWeapon? Carried()
        => standalones.Live().OfType<ClientSsWeapon>()
            .FirstOrDefault(w => w.State.OwnerIndex == screen.LocalPlayerIndex);

    /// <summary>The held weapon's item, by the active row's name.</summary>
    private IAttributeProvider? ItemOf(string weaponName)
        => owners.ItemOf(screen.LocalPlayerIndex, weaponName);

    /// <summary>The held item's buff (<c>set_buff_type</c>), 0 when none.</summary>
    private int BuffTypeOf(string weaponName) => hooks.Int(0, TFRageMeter.BuffTypeAttribute, ItemOf(weaponName));
}
