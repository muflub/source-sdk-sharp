using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Inventory;

/// <summary>
/// The property of plan_tf2_rpg 2d and 2k: no sequence of inventory operations duplicates or
/// loses an item. Over 10,000 seeded sequences of pickups, drops, moves, equips, belt and stash
/// moves, stacks, sales and buy-backs, rank-ups and respecs mid-sequence, salvage and recipes,
/// every item ever minted is in exactly one place (held, in the stash, on the floor, kept for
/// buy-back) or accounted gone (consumed by a recipe, merged into another stack, kept by a
/// vendor past buy-back), and the metal held is the metal that went in less the metal consumed.
/// </summary>
public sealed class InventoryPropertyTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static InventoryRules Inv => R.Inventory;

    private sealed class World
    {
        public HeroInventory Hero = new() { Australium = 1000 };
        public StashState Stash = new();
        public HeroSheet Sheet = Soldier(R, brawn: 60, aim: 60);
        public int StorageRank;
        public readonly List<Held> Floor = [];
        public readonly HashSet<long> Minted = [], Consumed = [], Merged = [], VendorKept = [];
        public long NextId;
        public long Units;
        public readonly Dictionary<string, int> Succeeded = [];
    }

    private static int Units(ItemInstance i) => R.Content.ItemBases[i.BaseId].Kind == ItemKinds.Metal ? R.Content.ItemBases[i.BaseId].Metal * i.Count : 0;

    private static Held Mint(World w, ItemInstance item)
    {
        var held = new Held(++w.NextId, item);
        w.Minted.Add(held.Id);
        w.Units += Units(item);
        return held;
    }

    private static int StashCap(World w) => Inv.StashSlots([w.Sheet with { SkillRanks = w.Sheet.SkillRanks.SetItem("storage_plan", w.StorageRank) }]);

    private static T Pick<T>(SeededRandom r, IReadOnlyList<T> list) => list[r.Below(list.Count)];

    private static void Note(World w, string op, InventoryResult result)
    {
        if (!result.Ok) return;
        w.Hero = result.Hero;
        w.Stash = result.Stash;
        w.Succeeded[op] = w.Succeeded.GetValueOrDefault(op) + 1;
    }

    private static void Step(World w, SeededRandom r)
    {
        var backpack = w.Hero.Backpack.Keys.ToList();
        int cap = Inv.BackpackSlots(w.Sheet);
        switch (r.Below(15))
        {
            case 0:
            {
                string[] bases = ["shovel", "rocket_launcher", "cap", "dog_tags", "health_small", "dossier", "repair_kit", "repair_kit", "repair_kit"];
                var item = r.Below(3) == 0
                    ? R.ItemRules.Roller.Roll(Ctx(0, 1, table: "base:scrap", source: "craft"), r.Next()) with { Count = 1 + r.Below(60) }
                    : R.ItemRules.Roller.Roll(Ctx(r.Below(3), 1 + r.Below(30), table: "base:" + Pick(r, bases), source: "craft"), r.Next());
                if (item.MaxDurability > 0)
                    item = item with { Durability = r.Below(item.MaxDurability + 1) };   // worn, broken or whole
                w.Floor.Add(Mint(w, item));
                break;
            }
            case 1 when w.Floor.Count > 0:
            {
                var item = Pick(r, w.Floor);
                var result = Inv.Pickup(w.Hero, w.Stash, w.Sheet, item);
                Note(w, "pickup", result);
                if (result.Ok) w.Floor.Remove(item);
                break;
            }
            case 2 when backpack.Count > 0:
            {
                var result = Inv.Drop(w.Hero, w.Stash, Pick(r, backpack));
                Note(w, "drop", result);
                w.Floor.AddRange(result.Left);
                break;
            }
            case 3 when backpack.Count > 0:
                Note(w, "move", Inv.Move(w.Hero, w.Stash, w.Sheet, Pick(r, backpack), r.Below(cap + 3)));
                break;
            case 4 when backpack.Count > 0:
            {
                int from = Pick(r, backpack);
                var b = R.Content.ItemBases[w.Hero.Backpack[from].Item.BaseId];
                var slots = R.Content.InventorySettings.Equipment.Where(e => e.Accepts == b.Slot).Select(e => e.Id).ToList();
                if (slots.Count > 0) Note(w, "equip", Inv.Equip(w.Hero, w.Stash, w.Sheet, from, Pick(r, slots)));
                break;
            }
            case 5 when w.Hero.Equipped.Count > 0:
                Note(w, "unequip", Inv.Unequip(w.Hero, w.Stash, w.Sheet, Pick(r, w.Hero.Equipped.Keys.ToList())));
                break;
            case 6:
                if (r.Below(2) == 0 && backpack.Count > 0) Note(w, "belt", Inv.ToBelt(w.Hero, w.Stash, Pick(r, backpack), r.Below(4)));
                else if (w.Hero.Belt.Count > 0) Note(w, "unbelt", Inv.FromBelt(w.Hero, w.Stash, w.Sheet, Pick(r, w.Hero.Belt.Keys.ToList())));
                break;
            case 7:
                if (r.Below(2) == 0 && backpack.Count > 0) Note(w, "deposit", Inv.Deposit(w.Hero, w.Stash, StashCap(w), Pick(r, backpack)));
                else if (w.Stash.Slots.Count > 0) Note(w, "withdraw", Inv.Withdraw(w.Hero, w.Stash, w.Sheet, Pick(r, w.Stash.Slots.Keys.ToList())));
                break;
            case 8 when backpack.Count > 1:
            {
                var result = Inv.Stack(w.Hero, w.Stash, w.Sheet, Pick(r, backpack), Pick(r, backpack));
                Note(w, "stack", result);
                foreach (var h in result.Left) w.Merged.Add(h.Id);
                break;
            }
            case 9:
                if (r.Below(2) == 0 && backpack.Count > 0)
                {
                    var result = Inv.Sell(w.Hero, w.Stash, Pick(r, backpack), 1);
                    Note(w, "sell", result);
                    foreach (var h in result.Left)
                    {
                        w.VendorKept.Add(h.Id);
                        w.Units -= Units(h.Item);
                    }
                }
                else if (w.Hero.BuyBack.Count > 0) Note(w, "buyback", Inv.BuyBack(w.Hero, w.Stash, w.Sheet, r.Below(w.Hero.BuyBack.Count), 1));
                break;
            case 10:
            {
                int rank = w.Sheet.SkillRanks.GetValueOrDefault("deep_pockets");
                int next = r.Below(3) == 0 ? 0 : Math.Min(9, rank + 1 + r.Below(3));   // a respec, or ranks bought
                w.Sheet = w.Sheet with { SkillRanks = w.Sheet.SkillRanks.SetItem("deep_pockets", next) };
                w.StorageRank = r.Below(3) == 0 ? 0 : Math.Min(5, w.StorageRank + 1);
                w.Succeeded["rank"] = w.Succeeded.GetValueOrDefault("rank") + 1;
                break;
            }
            case 11 when backpack.Count > 0:
            {
                int slot = Pick(r, backpack);
                Apply(w, "salvage", [slot], R.Salvage(R.ItemRules.Codec.ToRolled(w.Hero.Backpack[slot].Item), Ctx(0, 10), r.Next()));
                break;
            }
            case 12 when backpack.Count > 0:
            {
                int slot = Pick(r, backpack);
                Apply(w, "metal_up", [slot], R.Craft("metal_up", [R.ItemRules.Codec.ToRolled(w.Hero.Backpack[slot].Item)], Ctx(0, 10), r.Next()));
                break;
            }
            case 13 when backpack.Count > 0:
            {
                var metal = backpack.Where(s => R.Content.ItemBases[w.Hero.Backpack[s].Item.BaseId].Kind == ItemKinds.Metal).ToList();
                Apply(w, "craft", metal, R.Craft("craft_item:shovel", [.. metal.Select(s => R.ItemRules.Codec.ToRolled(w.Hero.Backpack[s].Item))], Ctx(0, 10), r.Next()));
                break;
            }
            case 14 when backpack.Count > 1:
            {
                var kits = backpack.Where(s => w.Hero.Backpack[s].Item.BaseId == "repair_kit").ToList();
                int a = kits.Count > 0 && r.Below(4) != 0 ? Pick(r, kits) : Pick(r, backpack), b = Pick(r, backpack);
                if (a != b)
                    Apply(w, "field_repair", [a, b], R.Craft("field_repair",
                        [R.ItemRules.Codec.ToRolled(w.Hero.Backpack[a].Item), R.ItemRules.Codec.ToRolled(w.Hero.Backpack[b].Item)], Ctx(0, 10), r.Next()));
                break;
            }
        }
    }

    private static void Apply(World w, string op, IReadOnlyList<int> inputs, SourceSharp.Host.Contracts.ItemChange change)
    {
        if (!change.Ok) return;
        List<long> ids = [.. Enumerable.Range(1, change.Produced.Count).Select(k => w.NextId + k)];
        var result = Inv.Apply(w.Hero, w.Stash, w.Sheet, inputs, change, ids);
        if (!result.Ok) return;
        foreach (var id in ids) w.Minted.Add(id);
        w.NextId += ids.Count;
        foreach (var h in result.Left)
        {
            w.Consumed.Add(h.Id);
            w.Units -= Units(h.Item);
        }
        w.Units += change.Produced.Sum(p => Units(R.ItemRules.Codec.Instance(p)));
        Note(w, op, result);
    }

    private static void Check(World w, ulong seed, int step)
    {
        var live = w.Hero.Items.Concat(w.Hero.BuyBack).Concat(w.Stash.Slots.Values).Concat(w.Floor).ToList();
        var ids = live.Select(h => h.Id).ToList();
        if (ids.Count != ids.Distinct().Count())
            Assert.Fail($"seed {seed} step {step}: an item is in two places");
        var accounted = ids.Concat(w.Consumed).Concat(w.Merged).Concat(w.VendorKept).ToList();
        if (accounted.Count != accounted.Distinct().Count() || !w.Minted.SetEquals(accounted))
            Assert.Fail($"seed {seed} step {step}: {w.Minted.Except(accounted).Count()} lost, {accounted.Count - accounted.Distinct().Count()} counted twice");
        long units = live.Sum(h => (long)Units(h.Item));
        if (units != w.Units)
            Assert.Fail($"seed {seed} step {step}: {units} units of metal held, {w.Units} expected");
    }

    [Fact]
    public void No_sequence_of_operations_duplicates_or_loses_an_item_over_10k_seeded_sequences()
    {
        var totals = new Dictionary<string, int>();
        long minted = 0;
        for (ulong seed = 0; seed < 10_000; seed++)
        {
            var w = new World();
            var r = SeededRandom.For(seed, "inventory property");
            for (int step = 0; step < 60; step++)
            {
                Step(w, r);
                Check(w, seed, step);
            }
            minted += w.Minted.Count;
            foreach (var (op, n) in w.Succeeded) totals[op] = totals.GetValueOrDefault(op) + n;
        }

        // The stimulus: every kind of operation happened, many times.
        string[] kinds = ["pickup", "drop", "move", "equip", "unequip", "belt", "unbelt", "deposit", "withdraw", "stack", "sell", "buyback", "rank", "salvage", "metal_up", "craft", "field_repair"];
        Assert.All(kinds, k => Assert.True(totals.GetValueOrDefault(k) >= 50, $"{k}: {totals.GetValueOrDefault(k)}"));
        Assert.True(minted > 50_000, $"{minted} items minted");
    }
}
