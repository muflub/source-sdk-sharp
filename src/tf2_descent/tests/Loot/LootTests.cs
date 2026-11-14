using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Loot;
using Xunit;

namespace TF2.Descent.Tests.Loot;

/// <summary>
/// Loot (plan_descent_logic RU4 and X1; plan_tf2_rpg 2c, R4, D11, D13): a kill is rolled once
/// per party seat through the kill sequence, each seat's roll its own draw that replays; the
/// Australium is the kill's, rolled once and split without loss; the tables' distributions
/// hold over fixed seeds within the stated tolerances; bosses and champions roll their own
/// tables per player; containers roll through the furniture hook per player.
/// </summary>
public sealed class LootTests
{
    private static readonly TF2DescentRules Rules = new();
    private static LootRules L => Rules.Loot;

    /// <summary>Kills 0..n−1 of one template at one depth, seat <paramref name="seat"/>.</summary>
    private static List<KillLoot> Kills(string key, int depth, int n, int seat = 0, ulong seed = 11, int party = 1) =>
        [.. Enumerable.Range(0, n).Select(k => L.Roll(seed, L.KillSeq((uint)k, seat), key, depth, party))];

    private static double Share(IEnumerable<KillLoot> rolls, Func<KillLoot, bool> which)
    {
        var list = rolls.ToList();
        return (double)list.Count(which) / list.Count;
    }

    //-------------------------------------------------------------------------
    // Seats (R4, X1)
    //-------------------------------------------------------------------------

    [Fact]
    public void A_seats_kill_sequence_is_the_kill_times_the_seats_plus_the_seat()
    {
        Assert.Equal(4, L.Seats);
        Assert.Equal(0u, L.KillSeq(0, 0));
        Assert.Equal(4u * 7 + 3, L.KillSeq(7, 3));
        Assert.Equal((7u, 3), L.Decode(L.KillSeq(7, 3)));
    }

    [Fact]
    public void A_seat_outside_the_party_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => L.KillSeq(1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => L.KillSeq(1, -1));
    }

    [Fact]
    public void Each_eligible_player_gets_one_roll_of_a_kill_and_never_two()
    {
        var seats = L.PerSeat(5, 9, "soldier", 4, partySize: 3);

        Assert.Equal(3, seats.Count);
        Assert.Equal([L.KillSeq(9, 0), L.KillSeq(9, 1), L.KillSeq(9, 2)],
            Enumerable.Range(0, 3).Select(s => L.KillSeq(9, s)).Distinct());
        Assert.Equal(seats, Enumerable.Range(0, 3).Select(s => L.Roll(5, L.KillSeq(9, s), "soldier", 4, 3)));
    }

    /// <summary>
    /// 40,000 kills of a soldier at depth 8, seats 0 and 1: the chance both get an item equals
    /// the product of each one's chance within 0.4 points (independent draws), and a second
    /// rules instance replays every roll.
    /// </summary>
    [Fact]
    public void Two_players_rolls_of_one_kill_are_independent_draws_that_replay()
    {
        const int n = 40_000;
        var a = Kills("soldier", 8, n, seat: 0);
        var b = Kills("soldier", 8, n, seat: 1);
        double pa = Share(a, x => x.Item), pb = Share(b, x => x.Item);
        double both = (double)Enumerable.Range(0, n).Count(i => a[i].Item && b[i].Item) / n;

        Assert.InRange(pa, 0.05, 0.5);
        Assert.InRange(both, pa * pb - 0.004, pa * pb + 0.004);
        Assert.NotEqual(a.Select(x => x.Item), b.Select(x => x.Item));
        var again = new TF2DescentRules();
        Assert.Equal(a, Enumerable.Range(0, n).Select(k => again.Loot.Roll(11, again.Loot.KillSeq((uint)k, 0), "soldier", 8, 1)));
    }

    [Fact]
    public void The_party_size_does_not_change_a_seats_item_roll()
    {
        var solo = Kills("heavy", 6, 2000, seat: 1, party: 1);
        var four = Kills("heavy", 6, 2000, seat: 1, party: 4);

        Assert.Equal(solo.Select(x => (x.Outcome, x.Tier, x.Potion)), four.Select(x => (x.Outcome, x.Tier, x.Potion)));
    }

    //-------------------------------------------------------------------------
    // The contract
    //-------------------------------------------------------------------------

    [Fact]
    public void KillRoll_answers_the_seats_roll()
    {
        Assert.Equal(0ul, Rules.Salt);
        for (uint k = 0; k < 400; k++)
        {
            var roll = L.Roll(42, k, "pyro", 9, 2);
            var answer = Rules.KillRoll(42, k, "pyro", 9, 2);
            Assert.Equal(LootRules.ToContract(roll), answer);
            Assert.Equal(roll.Item, answer.Drop);
            Assert.Equal(roll.Item ? roll.Tier : -1, answer.Tier);
        }
    }

    [Fact]
    public void Two_modules_with_different_salts_roll_differently()
    {
        var salted = new TF2DescentRules { Salt = 99 };
        var plain = Enumerable.Range(0, 300).Select(k => Rules.KillRoll(42, (uint)k, "soldier", 6, 1)).ToList();

        Assert.NotEqual(plain, Enumerable.Range(0, 300).Select(k => salted.KillRoll(42, (uint)k, "soldier", 6, 1)));
        Assert.Equal(plain, Enumerable.Range(0, 300).Select(k => new TF2DescentRules().KillRoll(42, (uint)k, "soldier", 6, 1)));
    }

    [Fact]
    public void A_potion_is_no_item_to_the_host()
    {
        var potion = Kills("pyro", 2, 4000).First(x => x.Outcome == LootOutcome.Potion);

        Assert.False(LootRules.ToContract(potion).Drop);
        Assert.Equal(-1, LootRules.ToContract(potion).Tier);
        Assert.NotNull(potion.Potion);
    }

    [Fact]
    public void An_unknown_template_is_refused_naming_it()
    {
        var e = Assert.Throws<ArgumentException>(() => L.Roll(1, 0, "laser_bot", 1, 1));
        Assert.Contains("laser_bot", e.Message);
        Assert.Throws<ArgumentException>(() => L.Roll(1, 0, "soldier+not_an_affix", 1, 1));
    }

    //-------------------------------------------------------------------------
    // Distributions (fixed seeds, stated tolerances)
    //-------------------------------------------------------------------------

    /// <summary>
    /// A pyro is the reference robot (quality 1): over 40,000 kills at depth 1 the act 1 table's
    /// 80 / 6 / 14 split of nothing, potion and item holds within 0.6 points each.
    /// </summary>
    [Fact]
    public void A_reference_robots_outcomes_follow_its_tables_weights()
    {
        Assert.Equal(1.0, L.Quality("pyro"), 9);
        var rolls = Kills("pyro", 1, 40_000);

        Assert.InRange(Share(rolls, x => x.Outcome == LootOutcome.Nothing), 0.794, 0.806);
        Assert.InRange(Share(rolls, x => x.Outcome == LootOutcome.Potion), 0.054, 0.066);
        Assert.InRange(Share(rolls, x => x.Outcome == LootOutcome.Item), 0.134, 0.146);
    }

    /// <summary>
    /// The item tiers at depth 15 follow act 3's weights grown fifteen deep: 200,000 kills of a
    /// pyro, each tier's share of the items within 10 % of its expected share (and 0.3 points).
    /// </summary>
    [Fact]
    public void Item_tiers_follow_the_tables_weights_at_the_depth()
    {
        var table = Rules.Content.Loot.Table("act3");
        double total = table.Tiers.Sum(t => t.At(15));
        var items = Kills("pyro", 15, 200_000, seed: 3).Where(x => x.Item).ToList();

        Assert.True(items.Count > 25_000);
        foreach (var t in table.Tiers)
        {
            int index = Rules.Tiers.ToList().IndexOf(Rules.Content.Rarities[t.Rarity].Name);
            double expected = t.At(15) / total;
            double seen = (double)items.Count(x => x.Tier == index) / items.Count;
            Assert.InRange(seen, Math.Min(expected * 0.9, expected - 0.003), Math.Max(expected * 1.1, expected + 0.003));
        }
    }

    [Fact]
    public void Deeper_kills_drop_better_tiers()
    {
        double Mean(int depth) => Kills("pyro", depth, 60_000).Where(x => x.Item).Average(x => x.Tier);

        Assert.True(Mean(15) > Mean(1) + 0.1, $"depth 15 mean tier {Mean(15)} vs depth 1 {Mean(1)}");
    }

    /// <summary>A giant drops an item more often than its ordinary template, and a runt less (2f: loot follows life and temperament); 30,000 kills each.</summary>
    [Fact]
    public void Item_chance_follows_the_templates_quality()
    {
        double Items(string t) => Share(Kills(t, 9, 30_000), x => x.Item);

        Assert.True(L.Quality("giant_soldier") > L.Quality("soldier") && L.Quality("soldier") > L.Quality("runt_soldier"));
        Assert.True(Items("giant_soldier") > Items("soldier") + 0.05);
        Assert.True(Items("soldier") > Items("runt_soldier") + 0.03);
    }

    /// <summary>
    /// Two champion affixes (+50 % item weight each, one more tier roll each) raise the item
    /// chance (14.8 % to 25.8 % by the weights) and the mean tier over the plain template
    /// (30,000 kills each).
    /// </summary>
    [Fact]
    public void Champion_affixes_raise_the_item_chance_and_the_tier()
    {
        var plain = Kills("soldier", 9, 30_000);
        var champion = Kills("soldier+extra_fast+teleporting", 9, 30_000);

        Assert.True(Share(champion, x => x.Item) > Share(plain, x => x.Item) * 1.6);
        Assert.True(champion.Where(x => x.Item).Average(x => x.Tier) > plain.Where(x => x.Item).Average(x => x.Tier) + 0.2);
    }

    [Fact]
    public void Champion_affixes_are_keyed_in_any_order()
    {
        Assert.Equal("soldier+extra_fast+teleporting", LootKey.Of("soldier", ["teleporting", "extra_fast"]).ToString());
        Assert.Equal(L.Roll(3, 5, "soldier+teleporting+extra_fast", 9, 1), L.Roll(3, 5, "soldier+extra_fast+teleporting", 9, 1));
    }

    //-------------------------------------------------------------------------
    // Australium (R4)
    //-------------------------------------------------------------------------

    [Fact]
    public void Only_seat_zero_carries_the_kills_australium()
    {
        Assert.True(Kills("soldier", 5, 2000, seat: 0).Count(x => x.Australium > 0) > 500);
        for (int seat = 1; seat < 4; seat++)
            Assert.All(Kills("soldier", 5, 2000, seat: seat, party: 4), x => Assert.Equal(0, x.Australium));
    }

    /// <summary>40 % of a pyro's kills pay (40,000 kills, within 0.8 points), and the mean grows by the table's 20 % a depth: depth 15 over depth 1 within 8 % of 1.2^14.</summary>
    [Fact]
    public void Australium_pays_at_its_chance_and_grows_by_depth()
    {
        var shallow = Kills("pyro", 1, 40_000);
        var deep = Kills("pyro", 15, 40_000);
        double ratio = deep.Where(x => x.Australium > 0).Average(x => (double)x.Australium)
                       / shallow.Where(x => x.Australium > 0).Average(x => (double)x.Australium);

        Assert.InRange(Share(shallow, x => x.Australium > 0), 0.392, 0.408);
        Assert.InRange(ratio, Math.Pow(1.2, 14) * 0.92, Math.Pow(1.2, 14) * 1.08);
    }

    [Fact]
    public void A_partys_kill_pays_more_australium_in_total()
    {
        long Total(int party) => Enumerable.Range(0, 4000).Sum(k => L.Roll(8, L.KillSeq((uint)k, 0), "heavy", 7, party).Australium);

        Assert.InRange((double)Total(4) / Total(1), 2.4, 2.6);   // +50 % a member past the first
    }

    [Fact]
    public void The_split_conserves_the_total_for_every_party_size_and_amount()
    {
        foreach (int members in new[] { 1, 2, 3, 4 })
            foreach (long total in Enumerable.Range(0, 1000).Select(i => (long)i).Concat([12_345L, 9_999_999_999L]))
            {
                var shares = LootRules.Split(total, members);
                Assert.Equal(members, shares.Count);
                Assert.Equal(total, shares.Sum());
                Assert.True(shares.Max() - shares.Min() <= 1);
            }
    }

    [Fact]
    public void The_remainder_goes_one_apiece_to_the_first_members()
    {
        Assert.Equal([3L, 3L, 2L, 2L], LootRules.Split(10, 4));
        Assert.Equal([4L, 3L, 3L], LootRules.Split(10, 3));
    }

    //-------------------------------------------------------------------------
    // Magic find (not auditable by the host)
    //-------------------------------------------------------------------------

    [Fact]
    public void Magic_find_of_zero_is_the_hosts_roll()
    {
        for (uint k = 0; k < 2000; k++)
            Assert.Equal(L.Roll(6, k, "medic", 4, 2), L.RollWithFind(6, k, "medic", 4, 2, itemDropPct: 0));
    }

    /// <summary>Five ranks of Scavenger (+30 %) raise a pyro's item chance by about 30 % relative (40,000 kills; 1.22 to 1.38).</summary>
    [Fact]
    public void Item_find_raises_the_item_chance_in_proportion()
    {
        double plain = Share(Kills("pyro", 3, 40_000), x => x.Item);
        double found = Share(Enumerable.Range(0, 40_000).Select(k => L.RollWithFind(11, L.KillSeq((uint)k, 0), "pyro", 3, 1, 30)), x => x.Item);

        Assert.InRange(found / plain, 1.22, 1.38);
    }

    [Fact]
    public void Australium_find_raises_a_share_rounding_down()
    {
        Assert.Equal(104, LootRules.FoundShare(100, 4));
        Assert.Equal(10, LootRules.FoundShare(10, 4));   // 0.4 rounds down
        Assert.Equal(120, LootRules.FoundShare(100, 20));
    }

    //-------------------------------------------------------------------------
    // Bosses and champions (D13, tier −1)
    //-------------------------------------------------------------------------

    private static ItemRollContext Ctx(int depth) => L.KillContext("soldier", 12, depth, -1);

    [Theory]
    [InlineData("sir_nukesalot", 5, 2, 2)]
    [InlineData("major_crits", 10, 3, 2)]
    [InlineData("captain_punch", 15, 4, 3)]
    public void Each_boss_rolls_its_own_table_for_a_player(string boss, int depth, int items, int potions)
    {
        var drops = Rules.BossDrops(Ctx(depth), boss, 77);
        var gear = drops.Where(d => Rules.Content.ItemBases[d.BaseType].IsGear).ToList();

        Assert.Equal(items, gear.Count);
        Assert.Equal(potions, drops.Count - gear.Count);
        Assert.All(gear, d => Assert.True(d.Tier >= 1, $"a boss item of tier {d.Tier}"));
        Assert.All(drops, d => Assert.True(Rules.Replays(d)));
        Assert.Equal(drops.Select(d => d.Instance), Rules.BossDrops(Ctx(depth), boss, 77).Select(d => d.Instance));
        Assert.NotEqual(drops.Select(d => d.Seed), Rules.BossDrops(Ctx(depth), boss, 78).Select(d => d.Seed));
    }

    [Fact]
    public void The_deepest_boss_drops_better_than_the_first()
    {
        double Mean(string boss, int depth) =>
            Enumerable.Range(0, 400).SelectMany(s => Rules.BossDrops(Ctx(depth), boss, (ulong)s))
                .Where(d => Rules.Content.ItemBases[d.BaseType].IsGear).Average(d => d.Tier);

        Assert.True(Mean("captain_punch", 15) > Mean("sir_nukesalot", 5) + 0.3);
    }

    [Fact]
    public void A_champion_gives_an_item_an_affix_of_at_least_vintage()
    {
        var drops = Rules.BossDrops(Ctx(8), "giant_pyro+fireproof+multishot", 5);

        Assert.Equal(2, drops.Count);
        Assert.All(drops, d => Assert.True(d.Tier >= 1));
        Assert.All(drops, d => Assert.StartsWith("champion:", d.Context.Source));
    }

    [Fact]
    public void An_ordinary_robot_has_no_table_of_its_own()
    {
        Assert.Empty(Rules.BossDrops(Ctx(8), "soldier", 5));
    }

    [Fact]
    public void A_boss_pays_its_tables_australium()
    {
        Assert.Equal(400, L.BossAustralium("sir_nukesalot"));
        Assert.Equal(3000, L.BossAustralium("captain_punch"));
        Assert.Equal(0, L.BossAustralium("soldier"));
    }

    //-------------------------------------------------------------------------
    // Containers (2l, R4)
    //-------------------------------------------------------------------------

    [Fact]
    public void A_container_rolls_its_own_loot_for_each_player_through_the_furniture_hook()
    {
        var open = Rules.Furniture.Containers.Open(new ContainerState("chest", "locker", null, null), 21, ["ann", "bob"], new HashSet<string>(),
            L.ContainerHook(6));
        var again = Rules.Furniture.Containers.Open(new ContainerState("chest", "locker", null, null), 21, ["ann", "bob"], new HashSet<string>(),
            L.ContainerHook(6));

        Assert.Equal(["ann", "bob"], open.Loot.Keys);
        Assert.Equal(open.Loot.Values, again.Loot.Values);
    }

    /// <summary>The container table pays an item 45 % of the time (20,000 opens, within 1 point) and Australium 50 % (within 1 point).</summary>
    [Fact]
    public void A_containers_rolls_follow_its_table()
    {
        var rolls = Enumerable.Range(0, 20_000)
            .Select(i => L.Container(new LootRequest("container", "c" + i, "ann", (ulong)i * 7919), 4)).ToList();

        Assert.InRange(Share(rolls, x => x.Item), 0.44, 0.46);
        Assert.InRange(Share(rolls, x => x.Australium > 0), 0.49, 0.51);
    }
}
