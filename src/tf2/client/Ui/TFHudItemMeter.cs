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

namespace TF2.Client.Ui;

/// <summary>
/// The timed item's charge reads (tf_hud_itemeffectmeter.cpp): the bar off
/// the held weapon's own networked field (m_flItemChargeMeter on the carried
/// blob, tf_player_shared.cpp:7832 — the replayed prediction while it is
/// fresh, as the medic meter reads its BehaviourMeter), present when the
/// held item's <c>item_meter_charge_rate</c> is set (stock's meter stands up
/// for the item that carries one), and the label as the item's
/// <c>meter_label</c> string attribute (SetLabelText's plain read, :607-628).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFItemMeter)])]
public sealed class TFHudItemMeter(IEngineHud screen, ILocalArsenal arsenal,
                                   IClientStandalones standalones, IWeaponDrawStates states,
                                   ITFWeaponOwners owners, AttributeHooks hooks) : ITFItemMeter
{
    public bool Present
        => arsenal.TryGetActive(out string name, out _, out _)
           && hooks.Float(0f, TFItemMeter.ChargeRateAttribute, ItemOf(name)) > 0f;

    public float Charge
        => Carried() is { } weapon
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
}
