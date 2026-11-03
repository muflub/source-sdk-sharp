//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The spawn's choice of held weapon, stock <c>CTFPlayer::Spawn</c>'s walk
/// under <c>m_bRegenerating == false</c> (tf_player.cpp:4700-4726): after the
/// kit, the primary loadout row's weapon is taken when it selects
/// (<c>CanBeSelected</c>, then <c>Weapon_Switch</c>); else the secondary's —
/// but stock's second arm reads <c>pWeapon->CanBeSelected()</c> unnegated
/// (:4721), so a selectable secondary is stepped over and the melee taken:
/// the spy, whose revolver selects, spawns on the knife. The row's weapon is
/// the item's <c>item_class</c> as the class's own (<see
/// cref="TFItemClassTranslation.Translate"/>, the kit's rule), and the
/// questions go to the carry through <see cref="ITFCarriedWeapons"/> — the
/// given weapon's own answer, as stock asks the entity it holds.
///
/// <para>
/// The arms before and after the walk are the remembered-weapon furniture
/// (<c>m_bRememberActiveWeapon</c>, :4703-4710; the <c>m_iLastWeaponSlot</c>
/// seed, :4729-4742): convar state this build does not keep, so the walk is
/// the fallback chain alone. The resupply keeps the inventory and its guard
/// skips the switch (see <c>TFPlayerResupply.GiveKit</c>), so this runs on
/// the fresh-kit spawn only, as stock's <c>m_bRegenerating</c> gate reads.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFSpawnWeaponWalk(TFPlayerCarriers carriers, TFLoadoutProvider loadout, TFClassTable classes,
                                      ITFCarriedWeapons carried) : IPlayerSpawnHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.SpawnWeaponWalk;

    public void OnSpawning(EntityHandle player, float now)
    {
    }

    public void OnSpawned(EntityHandle player, float now)
    {
        TFClass cls = carriers.ClassOf(player);
        if (cls is <= TFClass.Undefined or >= TFClass.CountAll)
            return;

        // The stock arms verbatim, the second's unnegated CanBeSelected
        // included (:4718-4721): it is why a selectable secondary — the spy's
        // revolver — is stepped over and the melee taken.
        string? primary = Row(player, cls, TFLoadoutSlot.Primary);
        if (primary is null || !carried.Selectable(player, primary) || !carried.SwitchTo(player, primary))
        {
            string? secondary = Row(player, cls, TFLoadoutSlot.Secondary);
            if (secondary is null || carried.Selectable(player, secondary) || !carried.SwitchTo(player, secondary))
            {
                string? melee = Row(player, cls, TFLoadoutSlot.Melee);
                if (melee is not null)
                    carried.SwitchTo(player, melee);
            }
        }
    }

    /// <summary>The loadout row's weapon, as the class's own — the name the kit granted and the carry holds.</summary>
    private string? Row(EntityHandle player, TFClass cls, TFLoadoutSlot slot)
    {
        foreach (var entry in loadout.Loadout(player.EntryIndex, cls))
        {
            if (entry.Slot != slot)
                continue;

            return entry.Item.Definition.ItemClass is { Length: > 0 } itemClass
                ? TFItemClassTranslation.Translate(itemClass, classes[cls])
                : null;
        }

        return null;
    }
}
