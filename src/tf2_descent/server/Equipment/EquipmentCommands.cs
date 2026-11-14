using System.Globalization;
using System.Text;

using SourceSharp.Abi;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Gear;

/// <summary>What the equipment commands print: a hero's gear slot by slot, and an operation's answer.</summary>
[SideService(Side.Server)]
public sealed class GearReport(HeroEquipment equipment, HeroWear wear)
{
    /// <summary>
    /// Every equipment slot of the hero, in inventory.json's order: the item (its ledger id,
    /// base, rarity and item level, its durability as the ledger holds it with the progress
    /// toward the next point, and the wear not yet written, <see cref="HeroWear"/>), its state and why, the TF item and attributes handed to
    /// TF2 for a weapon, and what it contributes to the sheet (a state other than Active
    /// counting for nothing).
    /// </summary>
    public string Gear(HeroGear gear)
    {
        var c = CultureInfo.InvariantCulture;
        var rules = equipment.Rules;
        var sb = new StringBuilder();
        sb.Append(c, $"[TF2Descent/Equipment] {gear.PlayerIndex} {gear.Hero}: {gear.Sheet.ClassId} level {gear.Sheet.Level}, " +
                     $"{gear.Equipped.Count} equipped, resolved in {gear.Result.Passes} passes\n");
        foreach (var slot in rules.Content.InventorySettings.Equipment)
        {
            if (gear.In(slot.Id) is not { } held)
            {
                sb.Append(c, $"  {slot.Id}: empty\n");
                continue;
            }

            var item = held.Item;
            var state = gear.Result.State(slot.Id);
            string rarity = item.Rarity >= 0 && item.Rarity < rules.Content.Rarities.Count ? rules.Content.Rarities.Rows[item.Rarity].Id : "?";
            sb.Append(c, $"  {slot.Id}: #{held.Id} {item.BaseId} {rarity} ilvl {item.ItemLevel} dur {item.Durability}/{item.MaxDurability} " +
                         $"{state.State}{(state.Reason == TF2.Descent.Rules.Items.InactiveReason.None ? "" : " (" + state.Reason + ")")}" +
                         $"{(state.FiresAsStock ? " fires as Stock" : "")}\n");
            var (uses, hits) = wear.Pending(gear.Hero, held.Id);
            sb.Append(c, $"    wear: {item.WearUses / 100.0:0.##} toward the next point, pending {uses} uses {hits} hits\n");

            if (equipment.TFItemOf(gear, slot.Id) is { } tf)
            {
                string attributes = tf.Attributes.Count == 0 ? "none"
                    : string.Join(", ", tf.Attributes.Select(a => $"{a.Definition.Name} ({a.Definition.AttributeClass}) {a.AsFloat.ToString("0.###", c)}"));
                sb.Append(c, $"    tf: {tf.Definition.Name} (defindex {tf.Defindex}, quality {tf.Quality}): {attributes}\n");
            }
            else if (rules.Content.ItemBases[item.BaseId].TFItem is null)
            {
                sb.Append(c, $"    tf: none (no TF item)\n");
            }

            var mine = gear.Result.Contributions.Where(x => x.Source.Kind == SourceKind.Item && x.Source.Id == slot.Id).ToList();
            string contributes = mine.Count == 0 ? "nothing"
                : string.Join(", ", mine.Select(x => $"{x.Stat} {x.Amount.ToString("+0.##;-0.##", c)}"))
                  + (mine[0].Source.Counts ? "" : " (counts for nothing)");
            sb.Append(c, $"    contributes: {contributes}\n");
        }
        return sb.ToString();
    }
}

/// <summary>
/// <c>tf2_descent_equip &lt;slot&gt; &lt;backpack index&gt; [equipment slot]</c>: equips a
/// backpack item through the item ledger (dev; cheats). The equipment slot is the item's
/// base's (the first free trinket slot for a trinket) unless named. The rules' refusal is printed.
/// </summary>
[ConCommand("tf2_descent_equip",
            Help = "tf2_descent_equip <player slot> <backpack index> [equipment slot]: equip a backpack item through the item ledger.",
            Flags = ConVarFlags.Cheat)]
public sealed class EquipCommand(HeroEquipment equipment, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(0, int.MaxValue)] int index, string equipmentSlot = "")
    {
        if (equipment.HeroOf(slot) is not { } hero || equipment.Ledger.Find(hero) is not { } h)
        {
            log.Warning($"[TF2Descent/Equipment] tf2_descent_equip: player {slot} is no hero whose items can be equipped\n");
            return;
        }
        if (!h.Inventory.Backpack.TryGetValue(index, out var held))
        {
            log.Warning($"[TF2Descent/Equipment] tf2_descent_equip {index} for {hero} refused: no_item\n");
            return;
        }

        string target = equipmentSlot.Length > 0 ? equipmentSlot : SlotFor(h, equipment.Rules.Content.ItemBases[held.Item.BaseId].Slot);
        _ = Report(hero, index, target, equipment.Equip(slot, index, target));
    }

    /// <summary>The equipment slot that accepts a base's slot: the first free one, else the first.</summary>
    private string SlotFor(LedgerHero h, string accepts)
    {
        var slots = equipment.Rules.Content.InventorySettings.Equipment.Where(e => e.Accepts == accepts).ToList();
        if (slots.Count == 0)
            return accepts;
        return (slots.FirstOrDefault(e => !h.Inventory.Equipped.ContainsKey(e.Id)) ?? slots[0]).Id;
    }

    private async Task Report(string hero, int index, string target, Task<LedgerResult> equip)
    {
        var r = await equip;
        if (r.Ok)
            log.Msg($"[TF2Descent/Equipment] {hero} equipped backpack {index} in {target}\n");
        else
            log.Warning($"[TF2Descent/Equipment] tf2_descent_equip {index} in {target} for {hero} refused: {r.Refusal}\n");
    }
}

/// <summary><c>tf2_descent_unequip &lt;slot&gt; &lt;equipment slot&gt;</c>: takes an item off through the item ledger (dev; cheats).</summary>
[ConCommand("tf2_descent_unequip",
            Help = "tf2_descent_unequip <player slot> <equipment slot>: take an equipped item off into the backpack through the item ledger.",
            Flags = ConVarFlags.Cheat)]
public sealed class UnequipCommand(HeroEquipment equipment, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string equipmentSlot)
    {
        if (equipment.HeroOf(slot) is not { } hero)
        {
            log.Warning($"[TF2Descent/Equipment] tf2_descent_unequip: player {slot} is no hero whose items can be equipped\n");
            return;
        }
        _ = Report(hero, equipmentSlot, equipment.Unequip(slot, equipmentSlot));
    }

    private async Task Report(string hero, string equipmentSlot, Task<LedgerResult> unequip)
    {
        var r = await unequip;
        if (r.Ok)
            log.Msg($"[TF2Descent/Equipment] {hero} took off {equipmentSlot}\n");
        else
            log.Warning($"[TF2Descent/Equipment] tf2_descent_unequip {equipmentSlot} for {hero} refused: {r.Refusal}\n");
    }
}

/// <summary><c>tf2_descent_gear &lt;slot&gt;</c>: the hero's equipment slot by slot (dev; cheats; <see cref="GearReport"/>).</summary>
[ConCommand("tf2_descent_gear",
            Help = "tf2_descent_gear <player slot>: print each equipment slot: item, state, the TF attributes handed to TF2, the contributions.",
            Flags = ConVarFlags.Cheat)]
public sealed class GearCommand(HeroEquipment equipment, GearReport report, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (equipment.Of(slot) is not { } gear)
        {
            log.Warning($"[TF2Descent/Equipment] tf2_descent_gear: player {slot} is no hero whose items can be equipped\n");
            return;
        }
        log.Msg(report.Gear(gear));
    }
}

/// <summary>
/// <c>tf2_descent_identify &lt;slot&gt; &lt;backpack index&gt;</c>: identifies a backpack item
/// through the item ledger (dev; cheats): with a Dossier in the backpack, the rules'
/// <c>identify</c> recipe over the item and the Dossier (the item comes back under a new id,
/// into the first free slot); without one, the Spy's identify for his fee.
/// </summary>
[ConCommand("tf2_descent_identify",
            Help = "tf2_descent_identify <player slot> <backpack index>: identify a backpack item (a Dossier in the backpack, else the Spy's fee).",
            Flags = ConVarFlags.Cheat)]
public sealed class IdentifyCommand(HeroEquipment equipment, ItemReport report, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(0, int.MaxValue)] int index)
    {
        if (equipment.HeroOf(slot) is not { } hero || equipment.Ledger.Find(hero) is not { } h)
        {
            log.Warning($"[TF2Descent/Equipment] tf2_descent_identify: player {slot} is no hero whose items can be equipped\n");
            return;
        }

        var settings = equipment.Rules.Content.EconomySettings;
        int dossier = h.Inventory.Backpack.Where(p => p.Key != index && p.Value.Item.BaseId == settings.Dossier).Select(p => p.Key).DefaultIfEmpty(-1).First();
        var identify = dossier >= 0
            ? equipment.Ledger.Craft(hero, settings.IdentifyRecipe, [index, dossier])
            : equipment.Ledger.Identify(hero, index);
        _ = Report(hero, index, dossier >= 0 ? "dossier" : "spy", identify);
    }

    private async Task Report(string hero, int index, string how, Task<LedgerResult> identify)
    {
        var r = await identify;
        if (!r.Ok)
        {
            log.Warning($"[TF2Descent/Equipment] tf2_descent_identify {index} ({how}) for {hero} refused: {r.Refusal}\n");
            return;
        }
        foreach (var item in (r.Change?.Added ?? []).Concat(r.Change?.Changed ?? []).Where(i => i.Place == ItemPlace.Backpack && i.Item.Identified))
            log.Msg($"[TF2Descent/Items] {hero} identified ({how}) backpack {item.Slot}: {report.Item(item.Id, item.Item)}\n");
    }
}
