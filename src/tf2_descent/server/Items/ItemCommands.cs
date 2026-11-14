using System.Globalization;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Server.Items;

/// <summary>What the item commands print: a hero's inventory with each item's view, and an operation's answer.</summary>
[SideService(Side.Server)]
public sealed class ItemReport(IGameRules rules)
{
    private readonly ItemViews _views = (rules as TF2DescentRules ?? throw new ArgumentException("the item report needs TF2.Descent's own rules", nameof(rules))).ItemRules.Views;

    /// <summary>One item: its ledger id and the view its owner may have (the bytes a client would be sent).</summary>
    public string Item(long id, ItemInstance item) =>
        $"#{id.ToString(CultureInfo.InvariantCulture)} {Encoding.UTF8.GetString(ItemViews.Encode(_views.View(item, ItemViewer.Owner)))}";

    /// <summary>Every item the hero holds, place by place.</summary>
    public string Inventory(LedgerKind kind, LedgerHero h)
    {
        var sb = new StringBuilder();
        var inv = h.Inventory;
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Items] {h.Hero} ({kind}): {h.Sheet.ClassId} level {h.Sheet.Level}, Australium {inv.Australium}, " +
            $"backpack {inv.Backpack.Count}, equipped {inv.Equipped.Count}, belt {inv.Belt.Count}, stash {h.Stash.Slots.Count}\n");
        foreach (var (slot, held) in inv.Backpack)
            sb.Append(CultureInfo.InvariantCulture, $"  backpack {slot}: {Item(held.Id, held.Item)}\n");
        foreach (var (slot, held) in inv.Equipped)
            sb.Append(CultureInfo.InvariantCulture, $"  equipped {slot}: {Item(held.Id, held.Item)}\n");
        foreach (var (slot, held) in inv.Belt)
            sb.Append(CultureInfo.InvariantCulture, $"  belt {slot}: {Item(held.Id, held.Item)}\n");
        foreach (var (slot, held) in h.Stash.Slots)
            sb.Append(CultureInfo.InvariantCulture, $"  stash {slot}: {Item(held.Id, held.Item)}\n");
        return sb.ToString();
    }
}

/// <summary>
/// <c>tf2_descent_give &lt;slot&gt; &lt;base&gt; [rarity] [item level]</c>: mints an item of a base
/// through the item ledger into the player's backpack (dev; cheats). An unknown base is refused.
/// </summary>
[ConCommand("tf2_descent_give",
            Help = "tf2_descent_give <player slot> <item base> [rarity 0-4] [item level]: mint an item into the backpack through the item ledger.",
            Flags = ConVarFlags.Cheat)]
public sealed class GiveItemCommand(IItemLedger ledger, HeroKeys keys, ItemReport report, ILog log)
{
    public void Execute([ArgRange(1, int.MaxValue)] int slot, string baseId, [ArgRange(0, 4)] int rarity = 0, [ArgRange(1, 30)] int itemLevel = 1)
    {
        if (keys.Of(slot) is not { } hero)
        {
            log.Warning($"[TF2Descent/Items] tf2_descent_give: no hero in slot {slot}\n");
            return;
        }
        _ = Report(hero, baseId, ledger.Give(hero, baseId, rarity, itemLevel));
    }

    private async Task Report(string hero, string baseId, Task<LedgerResult> give)
    {
        var r = await give;
        if (!r.Ok)
        {
            log.Warning($"[TF2Descent/Items] tf2_descent_give {baseId} to {hero} refused: {r.Refusal}\n");
            return;
        }
        foreach (var added in r.Change?.Added ?? [])
            log.Msg($"[TF2Descent/Items] gave {hero} backpack {added.Slot}: {report.Item(added.Id, added.Item)}\n");
    }
}

/// <summary><c>tf2_descent_inv &lt;slot&gt;</c>: prints the player's inventory with each item's view (dev).</summary>
[ConCommand("tf2_descent_inv",
            Help = "tf2_descent_inv <player slot>: print the player's items (backpack, equipped, belt, stash) as the item ledger holds them.",
            Flags = ConVarFlags.Cheat)]
public sealed class InventoryCommand(IItemLedger ledger, HeroKeys keys, ItemReport report, ILog log)
{
    public void Execute([ArgRange(1, int.MaxValue)] int slot)
    {
        if (keys.Of(slot) is not { } hero)
        {
            log.Warning($"[TF2Descent/Items] tf2_descent_inv: no hero in slot {slot}\n");
            return;
        }
        _ = Report(ledger.Load(hero));
    }

    private async Task Report(Task<LedgerResult> load)
    {
        var r = await load;
        if (r.Hero is { } h)
            log.Msg(report.Inventory(ledger.Kind, h));
        else
            log.Warning($"[TF2Descent/Items] tf2_descent_inv: {r.Refusal}\n");
    }
}

/// <summary><c>tf2_descent_drop &lt;slot&gt; &lt;backpack index&gt;</c>: drops a backpack item on the floor through the item ledger (dev).</summary>
[ConCommand("tf2_descent_drop",
            Help = "tf2_descent_drop <player slot> <backpack index>: drop a backpack item onto the floor through the item ledger.",
            Flags = ConVarFlags.Cheat)]
public sealed class DropItemCommand(IItemLedger ledger, HeroKeys keys, ItemReport report, ILog log)
{
    public void Execute([ArgRange(1, int.MaxValue)] int slot, [ArgRange(0, int.MaxValue)] int index)
    {
        if (keys.Of(slot) is not { } hero)
        {
            log.Warning($"[TF2Descent/Items] tf2_descent_drop: no hero in slot {slot}\n");
            return;
        }
        // The floor's entities are WO2's: until then a dev drop lies at the origin.
        _ = Report(hero, index, ledger.Drop(hero, index, Vector.Zero));
    }

    private async Task Report(string hero, int index, Task<LedgerResult> drop)
    {
        var r = await drop;
        if (!r.Ok)
        {
            log.Warning($"[TF2Descent/Items] tf2_descent_drop {index} for {hero} refused: {r.Refusal}\n");
            return;
        }
        foreach (var removed in r.Change?.Removed ?? [])
            log.Msg($"[TF2Descent/Items] {hero} dropped backpack {index}: {report.Item(removed.Id, removed.Item)}\n");
    }
}
