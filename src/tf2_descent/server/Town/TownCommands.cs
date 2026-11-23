using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Town;

/// <summary>
/// THE TOWN WINDOWS' VERBS (plan_descent_logic WO8, UI4): what each window's buttons send, each
/// acting for the player who sent it and no other, each checked and carried out by the town
/// (<see cref="TownService"/>) through the ledger by the rules:
/// <list type="bullet">
/// <item><c>tf2_descent_town_buy &lt;ware id&gt;</c>, <c>_sell &lt;backpack slot&gt;</c>, <c>_buyback &lt;index&gt;</c>: a shop.</item>
/// <item><c>tf2_descent_town_identify &lt;backpack slot&gt;</c>: the Spy.</item>
/// <item><c>tf2_descent_town_repair &lt;backpack slot | equipment slot&gt;</c>, <c>_repair_all</c>: the Engineer.</item>
/// <item><c>tf2_descent_town_craft &lt;recipe&gt; [affix]</c>: the bench (<see cref="BenchRecipe"/>'s order).</item>
/// <item><c>tf2_descent_town_crate</c>: the Scout's stall.</item>
/// <item><c>tf2_descent_town_stash_in &lt;backpack slot&gt;</c>, <c>_stash_out &lt;stash slot&gt;</c>: the locker.</item>
/// <item><c>tf2_descent_town_descend &lt;depth&gt;</c>: the depth picker.</item>
/// <item><c>tf2_descent_town_reclaim</c>: the Lost &amp; Found (no window: anywhere in town).</item>
/// <item><c>tf2_descent_town_close &lt;screen&gt;</c>: the client closed that window (Esc, its x): the session ends.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class TownCommands(TownService town, IEngineEntities entities) : IClientCommandHook, IOrderedHook
{
    public const string Prefix = "tf2_descent_town_";
    public const string Buy = Prefix + "buy", Sell = Prefix + "sell", BuyBack = Prefix + "buyback", Identify = Prefix + "identify",
                        Repair = Prefix + "repair", RepairAll = Prefix + "repair_all", Craft = Prefix + "craft", Crate = Prefix + "crate",
                        StashIn = Prefix + "stash_in", StashOut = Prefix + "stash_out", Descend = Prefix + "descend",
                        Reclaim = Prefix + "reclaim", Close = Prefix + "close";

    /// <summary>In TF2 Descent's town block, after the town (First + 1215).</summary>
    public int HookOrder => ModStages.First + 1220;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!command.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        int index = entities.GetIndex(player);
        string first = args.Length > 0 ? args[0] : "";
        switch (command)
        {
            case Buy when Long(first) is long id:
                _ = town.Buy(index, id);
                return true;
            case Sell when Int(first) is int slot:
                _ = town.Sell(index, slot);
                return true;
            case BuyBack when Int(first) is int at:
                _ = town.BuyBack(index, at);
                return true;
            case Identify when Int(first) is int slot:
                _ = town.Identify(index, slot);
                return true;
            case Repair when first.Length > 0:
                _ = town.Repair(index, first);
                return true;
            case RepairAll:
                _ = town.RepairAll(index);
                return true;
            case Craft when Int(first) is int recipe:
                _ = town.Craft(index, recipe, args.Length > 1 && Int(args[1]) is int affix ? affix : 0);
                return true;
            case Crate:
                _ = town.Crate(index);
                return true;
            case StashIn when Int(first) is int slot:
                _ = town.StashIn(index, slot);
                return true;
            case StashOut when Int(first) is int slot:
                _ = town.StashOut(index, slot);
                return true;
            case Descend when Int(first) is int depth:
                town.Descend(index, depth);
                return true;
            case Reclaim:
                _ = town.Reclaim(index);
                return true;
            case Close when first.Length > 0:
                town.ClosedOnClient(index, first);
                return true;
            default:
                return false;
        }
    }

    private static int? Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    private static long? Long(string text) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;
}

/// <summary><c>tf2_descent_vendor &lt;stall&gt; [player]</c>: opens a stall's window for a player (1) without walking to it (dev; cheats); walking away leaves it open.</summary>
[ConCommand("tf2_descent_vendor",
            Help = "tf2_descent_vendor <stall> [player slot]: open a town stall's window (saxton, spy, medic, scout, stash, engineer_repair, engineer, bench, depth) for a player without walking.",
            Flags = ConVarFlags.Cheat)]
public sealed class VendorCommand(TownService town, ILog log)
{
    private IEnumerable<string> Stalls() => TownStalls.All.Select(s => s.Id).Append("depth");

    public void Execute([Complete(From = nameof(Stalls))] string stall, [ArgRange(1, 64)] int player = 1)
    {
        bool opened = stall == "depth" ? town.OpenDepth(player, reach: false)
            : TownStalls.IndexOf(stall) is var i and >= 0 && town.OpenStall(player, i, reach: false);
        if (!opened)
            log.Warning($"[TF2Descent/Town] tf2_descent_vendor: {(town.InTown ? $"no stall '{stall}' or no hero in slot {player}" : "this map is not the town")}\n");
    }
}

/// <summary><c>tf2_descent_vendors [player]</c>: each stall, where it stands, and for a vendor a player's stock this restock (dev; cheats).</summary>
[ConCommand("tf2_descent_vendors",
            Help = "tf2_descent_vendors [player slot]: list the town's stalls, where each stands, and each vendor's stock for a player (1) this restock.",
            Flags = ConVarFlags.Cheat)]
public sealed class VendorsCommand(TownService town, ITownWorld world, IItemLedger ledger, TF2DescentRules rules, ILog log)
{
    public void Execute([ArgRange(1, 64)] int player = 1)
    {
        if (!town.InTown)
        {
            log.Msg("[TF2Descent/Town] this map is not the town: no stalls\n");
            return;
        }
        _ = Report(player);
    }

    private async Task Report(int player)
    {
        var sb = new StringBuilder();
        long restock = town.Restock;
        sb.Append(CultureInfo.InvariantCulture, $"[TF2Descent/Town] {town.Placed.Count} stalls, restock {restock} (every {rules.Content.EconomySettings.RestockSeconds:0} s)\n");
        string? key = world.HeroOf(player);
        foreach (var (index, entity, at) in town.Placed)
        {
            var stall = TownStalls.All[index];
            sb.Append(CultureInfo.InvariantCulture, $"  {stall.Id} #{entity} at {at.X:0} {at.Y:0} {at.Z:0}: {stall.Screen} {stall.Stance}");
            if (stall.Vendor is { } vendor && key is not null)
            {
                var stock = await ledger.Stock(key, vendor, restock);
                sb.Append(CultureInfo.InvariantCulture, $", stock {stock.Items.Count}: ");
                sb.Append(string.Join(", ", stock.Items.Select(w =>
                    string.Create(CultureInfo.InvariantCulture, $"#{w.Id} {w.Item.BaseId} r{w.Item.Rarity} l{w.Item.ItemLevel} {w.Price}A"))));
            }
            sb.Append('\n');
        }
        log.Msg(sb.ToString());
    }
}

/// <summary><c>tf2_descent_australium &lt;player&gt; &lt;amount&gt;</c>: Australium into a hero's purse through the ledger (dev; cheats). Hosted the ledger refuses it: the host credits none.</summary>
[ConCommand("tf2_descent_australium",
            Help = "tf2_descent_australium <player slot> <amount>: credit Australium to a hero's purse through the item ledger.",
            Flags = ConVarFlags.Cheat)]
public sealed class AustraliumCommand(IItemLedger ledger, HeroKeys keys, ILog log)
{
    public void Execute([ArgRange(1, 64)] int player, [ArgRange(1, 100_000_000)] int amount)
    {
        if (keys.Of(player) is not { } hero)
        {
            log.Warning($"[TF2Descent/Town] tf2_descent_australium: no hero in slot {player}\n");
            return;
        }
        _ = Credit(hero, amount);
    }

    private async Task Credit(string hero, long amount)
    {
        var r = await ledger.CollectAustralium(hero, amount);
        log.Msg(r.Ok
            ? $"[TF2Descent/Town] {hero} Australium {r.Change?.AustraliumBefore} -> {r.Change?.AustraliumAfter}\n"
            : $"[TF2Descent/Town] tf2_descent_australium for {hero} refused: {r.Refusal}\n");
    }
}
