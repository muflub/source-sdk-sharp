using System.Text.Json.Nodes;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Rules.Skills;
using Xunit;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// The Host.Contracts 1.2 members of the Descent rules module (plan_sdk_2_8): a kill's Australium
/// pile divided on pickup, a repair by the repairer's sheet, a numbered breakable's roll for each
/// hero, and two stacks merged. Each is asked through the contract, as the host asks it.
/// </summary>
public sealed class Contract12RulesFacts
{
    private static readonly TF2DescentRules R = ItemFixtures.NewRules();

    private static IGameRules Contract => R;

    private static CharacterSheet Sheet(string cls = "soldier", string name = "ann", int level = 1, int scavenger = 0)
    {
        var hero = R.Heroes.New(cls, name) with { Level = level };
        if (scavenger > 0)
            hero = hero with { SkillRanks = hero.SkillRanks.SetItem("scavenger", scavenger) };
        return R.SheetCodec.Write(hero);
    }

    private static RolledItem Rolled(ItemInstance item) => R.ItemRules.Codec.ToRolled(item);

    private static ItemInstance Potion(int count, string baseId = "health_small") =>
        ItemFixtures.Item(baseId, 0, 1) with { Count = count, Durability = 0, MaxDurability = 0 };

    //-------------------------------------------------------------------------
    // The Australium pile
    //-------------------------------------------------------------------------

    [Fact]
    public void A_pile_is_split_among_the_members_the_remainder_one_apiece_from_the_killer()
    {
        var shares = Contract.AustraliumShares(10, [Sheet(name: "a"), Sheet(name: "b"), Sheet(name: "c")]);

        Assert.Equal([4L, 3L, 3L], shares);
    }

    [Fact]
    public void A_lone_killer_takes_the_whole_pile()
    {
        Assert.Equal([17L], Contract.AustraliumShares(17, [Sheet()]));
    }

    [Fact]
    public void A_scavengers_found_share_is_added_to_his_split_alone()
    {
        // Scavenger 5: +20 % Australium found, new Australium beyond the split (LootRules.FoundShare).
        var shares = Contract.AustraliumShares(100, [Sheet(name: "a"), Sheet(name: "b", scavenger: 5)]);

        Assert.Equal([50L, LootRules.FoundShare(50, 20)], shares);
        Assert.True(shares[1] > 50, "the stimulus: the found share raises the split");
    }

    [Fact]
    public void A_found_share_never_passes_the_whole_pile()
    {
        // The host refuses a share above the pile (rules_bad_shares): a lone Scavenger keeps the whole roll.
        var shares = Contract.AustraliumShares(40, [Sheet(scavenger: 5)]);

        Assert.Equal([40L], shares);
    }

    [Fact]
    public void Every_pile_gives_one_share_a_member_each_between_nothing_and_the_pile()
    {
        int checkedPiles = 0;
        foreach (long total in new long[] { 1, 2, 3, 7, 50, 999 })
            for (int n = 1; n <= 4; n++)
            {
                var sheets = Enumerable.Range(0, n).Select(i => Sheet(name: "m" + i, scavenger: i % 2 == 0 ? 0 : 5)).ToList();
                var shares = Contract.AustraliumShares(total, sheets);
                Assert.Equal(n, shares.Count);
                Assert.All(shares, s => Assert.InRange(s, 0, total));
                Assert.True(shares.Sum() >= total, $"{total} over {n}: the split pays the whole pile");
                checkedPiles++;
            }
        Assert.Equal(24, checkedPiles);
    }

    //-------------------------------------------------------------------------
    // The waived town price
    //-------------------------------------------------------------------------

    [Fact]
    public void A_hero_whose_perks_total_the_waiver_repairs_in_town_for_nothing()
    {
        // No shipped perk grants the waiver yet: a copy of the content whose Scavenger grants it stands in.
        var content = new ContentLoader().Load(ItemFixtures.Planted("skills", t =>
            ItemFixtures.Row(t, "scavenger")["effect"]![0]!["stats"]!.AsArray()
                .Add(new JsonObject { ["stat"] = Repairs.TownFreeStat, ["amount"] = new JsonObject { ["perRank"] = 0.2 } }))).OrThrow();
        var heroes = new HeroRules(content);
        var skills = new SkillRules(content, heroes);
        var repairs = new Repairs(content, new ItemRules(content, heroes).Codec);
        var worn = Rolled(ItemFixtures.Item("shovel", 0, 1) with { Durability = 10 });
        var plain = heroes.New("soldier", "ann");
        var perked = plain with { SkillRanks = plain.SkillRanks.SetItem("scavenger", 5) };

        bool free = Repairs.TownFree(skills.Stats(perked, SkillBonuses.None));
        var paid = repairs.Repair(worn, RepairKind.Town, null, 1, Repairs.TownFree(skills.Stats(plain, SkillBonuses.None)), 1);
        var waived = repairs.Repair(worn, RepairKind.Town, null, 1, free, 1);

        Assert.True(free);
        Assert.True(paid.Cost > 0, "the stimulus: the repair has a price to waive");
        Assert.Equal(0, waived.Cost);
        Assert.Equal(paid.Produced[0].Instance, waived.Produced[0].Instance);
    }

    [Fact]
    public void A_hero_without_the_waiver_pays_the_engineers_price_in_town()
    {
        var worn = Rolled(ItemFixtures.Item("shovel", 0, 1) with { Durability = 10 });

        Assert.False(R.TownFree(R.Heroes.New("soldier", "ann")));
        Assert.Equal(R.Repair(worn).Cost, Contract.Repair(worn, RepairKind.Town, null, Sheet(level: 40, scavenger: 5), 1).Cost);
    }

    //-------------------------------------------------------------------------
    // Numbered breakables
    //-------------------------------------------------------------------------

    private static (bool Australium, string Items) Kind(BreakableRoll r) =>
        (r.Australium > 0, string.Join(",", r.Items.Select(i => R.Content.ItemBases[i.BaseType].Kind == ItemKinds.Potion ? "potion" : "gear")));

    [Fact]
    public void Every_hero_agrees_on_what_a_breakable_gives()
    {
        int differ = 0;
        for (uint seq = 0; seq < 120; seq++)
        {
            var a = Contract.BreakableDrops(42, seq, 2, Sheet("soldier", "ann", 3));
            var b = Contract.BreakableDrops(42, seq, 2, Sheet("medic", "bob", 9));
            Assert.Equal(Kind(a), Kind(b));
            if (a.Australium != b.Australium || a.Items.Select(i => i.Seed).SequenceEqual(b.Items.Select(i => i.Seed)) is false)
                differ++;
        }
        Assert.True(differ > 0, "each hero's share is his own: some breakable rolled two heroes different shares");
    }

    [Fact]
    public void Breakables_give_nothing_australium_and_items_and_every_item_replays()
    {
        var rolls = Enumerable.Range(0, 200).Select(seq => Contract.BreakableDrops(42, (uint)seq, 2, Sheet())).ToList();

        Assert.Contains(rolls, r => r.Australium == 0 && r.Items.Count == 0);
        Assert.Contains(rolls, r => r.Australium > 0 && r.Items.Count == 0);
        Assert.Contains(rolls, r => r.Australium == 0 && r.Items.Count == 1);
        Assert.All(rolls.SelectMany(r => r.Items), i => Assert.True(R.Replays(i)));
    }

    [Fact]
    public void A_breakable_rolls_its_outcome_from_the_instance_its_number_and_the_depth()
    {
        var hosted = Enumerable.Range(0, 200).Select(seq => R.Furniture.Breaks.Hosted(42, (uint)seq, 2)).ToList();

        Assert.Equal(hosted, Enumerable.Range(0, 200).Select(seq => R.Furniture.Breaks.Hosted(42, (uint)seq, 2)));
        Assert.NotEqual(hosted, Enumerable.Range(0, 200).Select(seq => R.Furniture.Breaks.Hosted(43, (uint)seq, 2)));
        Assert.Contains(hosted, o => TF2.Descent.Rules.Furniture.BreakableSettings.Bad(o));
    }

    [Fact]
    public void A_hubs_depth_0_rolls_its_breakables_as_depth_1()
    {
        // A hub reports depth 0 to the host while its level reads 1: the levels table's depth keys both.
        var at0 = Enumerable.Range(0, 100).Select(seq => R.Furniture.Breaks.Hosted(42, (uint)seq, 0)).ToList();

        Assert.Equal(Enumerable.Range(0, 100).Select(seq => R.Furniture.Breaks.Hosted(42, (uint)seq, 1)), at0);
        Assert.NotEqual(Enumerable.Range(0, 100).Select(seq => R.Furniture.Breaks.Hosted(42, (uint)seq, 2)), at0);
    }

    [Fact]
    public void A_bad_outcome_is_the_empty_roll()
    {
        int bad = 0;
        for (uint seq = 0; seq < 200; seq++)
        {
            if (!TF2.Descent.Rules.Furniture.BreakableSettings.Bad(R.Furniture.Breaks.Hosted(42, seq, 2)))
                continue;
            var roll = Contract.BreakableDrops(42, seq, 2, Sheet());
            Assert.Equal((0L, 0), (roll.Australium, roll.Items.Count));
            bad++;
        }
        Assert.True(bad > 0, "no breakable rolled a bad outcome");
    }

    [Fact]
    public void A_breakable_rolls_the_same_for_the_same_instance_number_depth_and_hero()
    {
        var once = Contract.BreakableDrops(42, 9, 2, Sheet());
        var twice = Contract.BreakableDrops(42, 9, 2, Sheet());

        Assert.Equal(once.Australium, twice.Australium);
        Assert.Equal(once.Items.Select(i => Convert.ToHexString(i.Instance)), twice.Items.Select(i => Convert.ToHexString(i.Instance)));
    }

    //-------------------------------------------------------------------------
    // Merge
    //-------------------------------------------------------------------------

    [Fact]
    public void A_merge_that_fits_consumes_the_source_and_raises_the_target()
    {
        // Two potions the roller made, so the merged stack is asked to replay as the host asks a carried item.
        var ctx = new ItemRollContext("soldier", 1, 1, 1, 0, R.Content.ItemSettings.ForcedBasePrefix + "health_small", "test");
        var source = R.ItemRules.Roller.Roll(ctx, 5);
        var into = R.ItemRules.Roller.Roll(ctx, 6) with { Count = 2 };
        Assert.True(R.Replays(Rolled(into)), "the stimulus: the target replays before the merge");

        var change = Contract.Merge(Rolled(source), Rolled(into));

        Assert.True(change.Ok, change.Refusal);
        Assert.Equal([0], change.Consumed);
        var target = Assert.Single(change.Produced);
        Assert.Equal(into.Count + source.Count, target.Count);
        Assert.Equal(target.Count, R.ItemRules.Codec.Instance(target).Count);
        Assert.Equal(into.Seed, target.Seed);
        Assert.True(R.Replays(target));
        Assert.Equal(0, change.Cost);
    }

    [Fact]
    public void A_merge_past_the_stack_fills_the_target_and_leaves_the_rest_in_the_source()
    {
        // health_small stacks to 4: 3 into 2 makes 4 and leaves 1.
        var change = Contract.Merge(Rolled(Potion(3)), Rolled(Potion(2)));

        Assert.True(change.Ok, change.Refusal);
        Assert.Empty(change.Consumed);
        Assert.Equal([4, 1], change.Produced.Select(p => p.Count));
        Assert.Equal([4, 1], change.Produced.Select(p => R.ItemRules.Codec.Instance(p).Count));
    }

    [Fact]
    public void A_merge_keeps_the_count()
    {
        for (int a = 1; a <= 4; a++)
            for (int b = 1; b < 4; b++)
            {
                var change = Contract.Merge(Rolled(Potion(a)), Rolled(Potion(b)));
                Assert.True(change.Ok, $"{a} into {b}: {change.Refusal}");
                Assert.Equal(a + b, change.Produced.Sum(p => p.Count));
                Assert.All(change.Produced, p => Assert.InRange(p.Count, 1, 4));
            }
    }

    [Fact]
    public void A_merge_of_two_kinds_or_into_a_full_stack_or_of_gear_is_refused()
    {
        Assert.Equal(InventoryRefusals.NotStackable, Contract.Merge(Rolled(Potion(1)), Rolled(Potion(1, "health_medium"))).Refusal);
        Assert.Equal(InventoryRefusals.NotStackable, Contract.Merge(Rolled(Potion(1)), Rolled(Potion(4))).Refusal);
        Assert.Equal(InventoryRefusals.NotStackable,
            Contract.Merge(Rolled(ItemFixtures.Item("shovel", 0, 1)), Rolled(ItemFixtures.Item("shovel", 0, 1))).Refusal);
    }

    [Fact]
    public void A_merge_is_the_inventorys_own_stack()
    {
        var hero = new HeroInventory
        {
            Backpack = System.Collections.Immutable.ImmutableSortedDictionary<int, Held>.Empty
                .Add(0, new Held(1, Potion(3))).Add(1, new Held(2, Potion(2))),
        };
        var stacked = R.Inventory.Stack(hero, new StashState(), R.Heroes.New("soldier", "ann"), 0, 1);

        var change = Contract.Merge(Rolled(Potion(3)), Rolled(Potion(2)));

        Assert.True(stacked.Ok, stacked.Refusal);
        Assert.Equal([stacked.Hero.Backpack[1].Item.Count, stacked.Hero.Backpack[0].Item.Count], change.Produced.Select(p => p.Count));
    }
}
