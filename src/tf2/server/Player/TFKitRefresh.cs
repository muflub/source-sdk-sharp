//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// A living player's kit settled to what their item sources answer now
/// (<see cref="ITFItemSource"/>), where they stand: for the code that changes
/// what a player carries between spawns and lockers — an item equipped or
/// taken off mid-life.
/// </summary>
public interface ITFKitRefresh
{
    /// <summary>
    /// Re-reads the player's items and changes only what differs: a weapon
    /// whose item is no longer the kit's leaves, a weapon the kit names and
    /// the hands lack arrives, the worn items are rebuilt when they differ.
    /// The health, the ammunition pools, the clips of the weapons kept and
    /// the player's place are not touched. False when nothing changed —
    /// every weapon and wearable entity is the one it was.
    /// </summary>
    bool Refresh(EntityHandle player);
}

/// <summary>
/// The server's <see cref="ITFKitRefresh"/>: the weapon and wearable halves
/// of <c>CTFPlayer::ManageRegularWeapons</c> (tf_player.cpp:4496)
/// without the regeneration around them.
///
/// <para>
/// Stock reaches that walk only through <c>InitClass</c> — a spawn or
/// <c>Regenerate</c> — which resets every pool (<c>RemoveAllAmmo</c> and the
/// fill, :4498-4515) and the health (<c>SetHealth( GetMaxHealth() )</c>,
/// :4162) on the way; none of that is here. What is kept is the walk's own
/// rule for a carried weapon (<c>ValidateWeapons</c>, :5158-5200, over
/// <c>ItemsMatch</c>, :4361): one whose item is no longer the loadout's is
/// removed and the loadout's is given in its place, as a new weapon with the
/// give's own clip; one whose item still matches is not touched. A weapon
/// the last kit did not hand out (the builder a placement gave, a weapon
/// picked off the floor) is not the kit's to take, as the locker's
/// <see cref="TFPlayerResupply.GiveKit"/> leaves it.
/// </para>
///
/// <para>
/// The hands follow the change: when the weapon that left was the one held,
/// the weapon now in its loadout slot is drawn (stock switches to the next
/// best before the removal, :5173-5176, and has no later arm under
/// <c>m_bRegenerating</c>; the slot's successor is the answer a mid-life
/// equip wants). The kit's attribute manager is rebuilt by the same read
/// (<see cref="TFLoadoutProvider.Weapons"/>), so the new items' attributes
/// are the player's from this call on. A maximum that fell under the pool
/// or the health is left for the next clamp that asks.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFKitRefresh)])]
public sealed class TFKitRefresh(ITFPlayerClasses classes, TFLoadoutProvider loadout, Arsenal arsenal,
                                 TFPlayerCarriers carriers, IEnginePlayers players,
                                 IHooks<ITFWeaponEquipHook> equipHooks) : ITFKitRefresh
{
    public bool Refresh(EntityHandle player)
    {
        TFClass cls = classes.ClassOf(player);
        if (cls is <= TFClass.Undefined or >= TFClass.CountAll)
            return false;

        int index = player.EntryIndex;

        // What the last kit handed out, and which of it is in the hands.
        var before = new Dictionary<string, LoadoutItem>(loadout.KitOf(index), StringComparer.Ordinal);
        string? heldBefore = null;
        foreach (var (name, _, active) in arsenal.Holdings(player))
        {
            if (active)
                heldBefore = name;
        }

        // The sources' answer now: the kit's rows, the item behind each and
        // the manager their attributes provide to.
        List<string> kit = [.. loadout.Weapons(player)];
        var after = loadout.KitOf(index);

        // ValidateWeapons: the last kit's weapon whose item is no longer the
        // kit's goes.
        bool changed = false;
        bool handsEmptied = false;
        foreach (var (name, _, active) in arsenal.Holdings(player).ToArray())
        {
            if (!before.TryGetValue(name, out var was))
                continue;
            if (after.TryGetValue(name, out var now) && was.Item.SameAs(now.Item))
                continue;

            if (arsenal.TakeWeapon(player, name))
            {
                changed = true;
                handsEmptied |= active;
            }
        }

        // The loadout walk's give: what the kit names and the hands lack.
        foreach (string name in kit)
        {
            if (Holds(player, name))
                continue;
            if (arsenal.GiveWeapon(player, name))
                changed = true;
        }

        // The slot's successor takes the hands the removal emptied.
        if (handsEmptied && heldBefore is not null)
        {
            TFLoadoutSlot slot = before[heldBefore].Slot;
            string? successor = null;
            foreach (var (name, entry) in after)
            {
                if (entry.Slot == slot)
                    successor = name;
            }

            successor ??= kit.Count > 0 ? kit[0] : null;
            if (successor is not null)
                arsenal.Switch(player, successor);
        }

        // ValidateWearables and the wearable half of the loadout walk
        // (:4525, :4565-4660): the worn items, rebuilt only when they differ.
        if (carriers.Part<TFWearables>(index) is { } wearables
            && wearables.Settle(player, cls, (TFTeam)players.GetTeam(player),
                                TFWearables.Worn(loadout.Loadout(index, cls))))
            changed = true;

        // Stock's equip arm runs inside Weapon_Equip (:20590-20598): a pass
        // that equipped anything ends by asking the chain, as the kit's does.
        if (changed)
            equipHooks.Each(nameof(ITFWeaponEquipHook.OnEquipped), h => h.OnEquipped(player));

        return changed;
    }

    private bool Holds(EntityHandle player, string weapon)
    {
        foreach (var (name, _, _) in arsenal.Holdings(player))
        {
            if (name == weapon)
                return true;
        }

        return false;
    }
}
