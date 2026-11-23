using System.Globalization;

using SourceSharp.Abi;

using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Gear;

/// <summary>
/// <c>tf2_descent_repair &lt;player&gt; &lt;equipment slot|all&gt; &lt;town|town_free|field&gt;</c>:
/// repairs what the hero wears through the item ledger by the rules (dev; cheats; the vendor
/// is WO8): <c>town</c> is the Engineer's, to the maximum, the maximum untouched, for its price
/// in Australium from the hero's purse; <c>town_free</c> the same with the price waived;
/// <c>field</c> Diablo 1's steps with a Repair Kit from the backpack (it is used up), at the
/// hero's level, each lowering the maximum. The pending wear is written first. A repaired
/// item has its attributes again: the ledger's change re-settles the kit (<see cref="HeroKitSettle"/>).
/// </summary>
[ConCommand("tf2_descent_repair",
            Help = "tf2_descent_repair <player slot> <equipment slot|all> <town|town_free|field>: repair worn gear through the item ledger by the rules.",
            Flags = ConVarFlags.Cheat)]
public sealed class RepairCommand(HeroEquipment equipment, HeroWear wear, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string item, string how = "town")
    {
        if (equipment.HeroOf(slot) is not { } hero || equipment.Ledger.Find(hero) is not { } h)
        {
            log.Warning($"[TF2Descent/Wear] tf2_descent_repair: player {slot} is no hero whose items can be repaired\n");
            return;
        }
        WornRepair? kind = how switch
        {
            "town" => WornRepair.Town,
            "town_free" => WornRepair.TownUncharged,
            "field" => WornRepair.Field,
            _ => null,
        };
        if (kind is not { } k)
        {
            log.Warning($"[TF2Descent/Wear] tf2_descent_repair: '{how}' is not town, town_free or field\n");
            return;
        }

        wear.FlushHero(hero, "repair");
        var slots = item == "all" ? h.Inventory.Equipped.Keys.ToList() : [item];
        _ = Repair(hero, slots, k, how);
    }

    private async Task Repair(string hero, IReadOnlyList<string> slots, WornRepair kind, string how)
    {
        foreach (var slot in slots)
        {
            var before = equipment.Ledger.Find(hero)?.Inventory.Equipped.GetValueOrDefault(slot)?.Item;
            long australium = equipment.Ledger.Find(hero)?.Inventory.Australium ?? 0;
            var r = await equipment.Ledger.RepairWorn(hero, slot, kind);
            var after = equipment.Ledger.Find(hero)?.Inventory.Equipped.GetValueOrDefault(slot)?.Item;
            if (r.Ok && before is not null && after is not null)
                log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"[TF2Descent/Wear] {hero} {how} repair of {slot} ({after.BaseId}): dur {before.Durability}/{before.MaxDurability} -> " +
                    $"{after.Durability}/{after.MaxDurability}, paid {australium - (equipment.Ledger.Find(hero)?.Inventory.Australium ?? 0)} Australium\n"));
            else
                log.Warning($"[TF2Descent/Wear] tf2_descent_repair {slot} ({how}) for {hero} refused: {r.Refusal}\n");
        }
    }
}

/// <summary>
/// <c>tf2_descent_wear &lt;player&gt; &lt;equipment slot&gt; &lt;durability&gt; [maximum]</c>: sets a
/// worn item's durability (and maximum) through the item ledger (dev; cheats), its pending
/// wear written first and its progress cleared: the live proof's way to wear an item to 0.
/// </summary>
[ConCommand("tf2_descent_wear",
            Help = "tf2_descent_wear <player slot> <equipment slot> <durability> [maximum]: set a worn item's durability through the item ledger.",
            Flags = ConVarFlags.Cheat)]
public sealed class WearCommand(HeroEquipment equipment, HeroWear wear, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string item, [ArgRange(0, 10000)] int durability, int maximum = -1)
    {
        if (equipment.HeroOf(slot) is not { } hero || equipment.Ledger.Find(hero) is not { } h
            || !h.Inventory.Equipped.TryGetValue(item, out var held))
        {
            log.Warning($"[TF2Descent/Wear] tf2_descent_wear: player {slot} wears nothing in '{item}'\n");
            return;
        }
        wear.FlushHero(hero, "dev set");
        _ = Set(hero, item, durability, maximum >= 0 ? maximum : held.Item.MaxDurability);
    }

    private async Task Set(string hero, string item, int durability, int maximum)
    {
        var r = await equipment.Ledger.SetDurability(hero, item, durability, maximum);
        var after = equipment.Ledger.Find(hero)?.Inventory.Equipped.GetValueOrDefault(item)?.Item;
        if (r.Ok && after is not null)
            log.Msg($"[TF2Descent/Wear] {hero} {item} ({after.BaseId}) set to dur {after.Durability}/{after.MaxDurability}\n");
        else
            log.Warning($"[TF2Descent/Wear] tf2_descent_wear {item} for {hero} refused: {r.Refusal}\n");
    }
}
