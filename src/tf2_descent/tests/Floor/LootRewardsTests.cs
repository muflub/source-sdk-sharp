using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Host.Contracts;

using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Loot;

using Xunit;

using static TF2.Descent.Tests.Floor.LootRig;

namespace TF2.Descent.Tests.Floor;

/// <summary>A level as the rewards read it: its depth and seed, nothing else.</summary>
internal sealed class RigLevel(int depth, ulong seed) : ILevelData
{
    public LevelData? Current { get; set; } = new("tf2_descent-" + depth + "-s" + seed, depth, seed, "map name", [], [], [], [], null, null,
        ImmutableDictionary<LevelPiece, PieceSource>.Empty, []);

    public TF2.Descent.Ai.Level.HordeLevel? Horde => null;
}

/// <summary>A floor at z 0 everywhere, unless a fact digs a pit.</summary>
internal sealed class RigGround : ILootGround
{
    public bool Pit { get; set; }

    public bool TryGround(Vector at, out Vector ground)
    {
        ground = new Vector(at.X, at.Y, 2f);
        return !Pit;
    }
}

/// <summary>
/// Kill to reward (plan_descent_logic HO10): each seat's roll is the rules'
/// own, its item asked of the ledger and put on that hero's seat, the kill's
/// Australium split among the party from the killer on, the drop laid on the
/// floor under the robot; an answer that comes after the level ended drops
/// nothing and throws nothing.
/// </summary>
public sealed class LootRewardsTests
{
    private static TF2.Descent.Rules.TF2DescentRules R => LootRig.Rules;

    private const int Depth = 3;
    private const ulong Seed = 0x5EED_0003;

    private sealed record Kit(LootRig Rig, RigLevel Level, RigGround Ground, LootRewards Rewards);

    private static Kit Make(IItemLedger? ledger = null)
    {
        var rig = new LootRig(ledger: ledger);
        var level = new RigLevel(Depth, Seed);
        var ground = new RigGround();
        return new Kit(rig, level, ground, new LootRewards(R, level, rig.Party, rig.Ledger, ground, rig.Floor, rig.Log));
    }

    /// <summary>The first kill numbers from <paramref name="from"/> whose seats' rolls satisfy <paramref name="want"/>, for <paramref name="seats"/> heroes.</summary>
    private static uint Find(Func<IReadOnlyList<KillLoot>, bool> want, int seats = 2, uint from = 0, string robot = "soldier")
    {
        for (uint k = from; k < from + 20_000; k++)
            if (want(R.Loot.PerSeat(Seed, k, robot, Depth, seats, R.Salt)))
                return k;
        throw new InvalidOperationException("no kill in 20,000 rolls satisfies the fact's wish");
    }

    [Fact]
    public async Task Each_hero_near_gets_his_own_roll_on_his_own_seat()
    {
        var k = Make();
        uint kill = Find(r => r[0].Item && r[1].Item && r[0].Tier != r[1].Tier);

        var reward = await k.Rewards.Reward(kill, "soldier", InRoom(0), SlotA, ownTable: false);

        var drop = Assert.Single(reward!.Drops);
        var expected = R.Loot.PerSeat(Seed, kill, "soldier", Depth, 2, R.Salt);
        Assert.Equal(expected, reward.Rolls);
        Assert.Equal(expected[0].Tier, drop.SeatOf(SlotA)!.Item!.Item.Tier);
        Assert.Equal(expected[1].Tier, drop.SeatOf(SlotB)!.Item!.Item.Tier);
        Assert.Equal(A, drop.SeatOf(SlotA)!.Item!.ForHero);
        Assert.Equal(B, drop.SeatOf(SlotB)!.Item!.ForHero);
    }

    [Fact]
    public async Task The_killer_rolls_seat_0_whoever_else_is_near()
    {
        var k = Make();
        uint kill = Find(r => r[0].Item && r[0].Australium == 0 && !r[1].Item && r[1].Potion is null);

        var reward = await k.Rewards.Reward(kill, "soldier", InRoom(0), SlotB, ownTable: false);

        var drop = Assert.Single(reward!.Drops);
        Assert.Equal(B, reward.Members[0].Hero);
        Assert.NotNull(drop.SeatOf(SlotB)!.Item);
        Assert.Null(drop.SeatOf(SlotA));
    }

    [Fact]
    public async Task The_kills_Australium_is_split_among_the_heroes_near_with_the_remainder_from_the_killer()
    {
        var k = Make();
        k.Rig.Party.Members.Add(new LootMember(SlotC, C, new Vector(110, 110, 0)));
        uint kill = Find(r => r[0].Australium % 3 != 0, seats: 3);
        long total = R.Loot.PerSeat(Seed, kill, "soldier", Depth, 3, R.Salt)[0].Australium;

        var reward = await k.Rewards.Reward(kill, "soldier", InRoom(0), SlotB, ownTable: false);

        Assert.Equal(LootRules.Split(total, 3), reward!.Shares);
        Assert.Equal(total, reward.Shares.Sum());
        var drop = reward.Drops[0];
        // Members are the killer (B) first, then A, then C: the remainder's first unit is B's.
        Assert.Equal(reward.Shares[0], drop.SeatOf(SlotB)!.Australium);
        Assert.Equal(reward.Shares[1], drop.SeatOf(SlotA)?.Australium ?? 0);
        Assert.Equal(reward.Shares[2], drop.SeatOf(SlotC)?.Australium ?? 0);
        Assert.True(reward.Shares[0] > reward.Shares[2], "the stimulus: the split leaves a remainder");
    }

    [Fact]
    public async Task Hosted_no_Australium_goes_on_the_floor()
    {
        var held = new HeldLedger { Kind = LedgerKind.Hosted, Drops = kill => DropResult.Nothing };
        var k = Make(held.Proxy);
        uint kill = Find(r => r[0].Australium > 0 && !r[0].Item && !r[1].Item);

        var reward = await k.Rewards.Reward(kill, "soldier", InRoom(0), SlotA, ownTable: false);

        Assert.All(reward!.Shares, s => Assert.Equal(0, s));
        Assert.Empty(reward.Drops);
    }

    [Fact]
    public async Task The_drop_lies_on_the_floor_under_the_robot()
    {
        var k = Make();
        uint kill = Find(r => r[0].Item);

        var reward = await k.Rewards.Reward(kill, "soldier", new Vector(300, 200, 40), SlotA, ownTable: false);

        Assert.Equal(new Vector(300, 200, 2), reward!.Drops[0].Position);
        Assert.Equal(reward.Drops[0].Position, reward.Drops[0].Entity!.Position);
    }

    [Fact]
    public async Task A_robot_that_died_over_a_pit_drops_at_the_first_heros_feet()
    {
        var k = Make();
        k.Ground.Pit = true;
        uint kill = Find(r => r[0].Item);

        var reward = await k.Rewards.Reward(kill, "soldier", new Vector(300, 200, -900), SlotA, ownTable: false);

        Assert.Equal(k.Rig.Party.Members.Single(m => m.Player == SlotA).Origin, reward!.Drops[0].Position);
    }

    [Fact]
    public async Task Nobody_near_drops_nothing()
    {
        var k = Make();
        k.Rig.Party.Members.Clear();

        var reward = await k.Rewards.Reward(0, "soldier", InRoom(0), SlotA, ownTable: false);

        Assert.Null(reward);
        Assert.Empty(k.Rig.Floor.Drops);
    }

    [Fact]
    public async Task A_level_change_before_the_ledger_answers_drops_nothing_and_throws_nothing()
    {
        var held = new HeldLedger();
        var k = Make(held.Proxy);
        uint kill = Find(r => r[0].Item);

        var reward = k.Rewards.Reward(kill, "soldier", InRoom(0), SlotA, ownTable: false);
        Assert.False(reward.IsCompleted);
        k.Rig.NextLevel();
        foreach (var (drop, answer) in held.KillDrops)
            answer.SetResult(new DropResult(true, null, [k.Rig.Item(drop.Hero)]));

        Assert.Null(await reward);
        Assert.Empty(k.Rig.Floor.Drops);
        Assert.Equal(64, k.Rig.Floor.Free);
    }

    [Fact]
    public async Task A_ledger_that_fails_is_logged_and_nothing_is_thrown()
    {
        var held = new HeldLedger();
        var k = Make(held.Proxy);
        uint kill = Find(r => r[0].Item);

        var reward = k.Rewards.Reward(kill, "soldier", InRoom(0), SlotA, ownTable: false);
        held.KillDrops[0].Answer.SetException(new InvalidOperationException("the host fell over"));

        Assert.Null(await reward);
        Assert.Contains(k.Rig.Log.Lines, l => l.Warning && l.Text.Contains("the host fell over", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_champion_adds_its_own_table_one_drop_a_round()
    {
        var k = Make();
        string champion = R.Content.Champions.Rows[0].Id;
        string key = LootKey.Of("soldier", [champion]).ToString();

        var reward = await k.Rewards.Reward(0, key, InRoom(0), SlotA, ownTable: true);

        // Each hero gets the champion's items (items.json's per affix) on top of his own roll.
        int table = R.Content.Loot.Champion.Items;
        Assert.True(reward!.Drops.Count >= table, $"{reward.Drops.Count} drops for a champion's table of {table}");
        Assert.All(reward.Drops.Skip(1), d => Assert.NotEqual(reward.Drops[0].Position, d.Position));
        Assert.Contains(reward.Drops.SelectMany(d => d.Seats), s => s.Item?.Item.Context.Source.StartsWith("champion:", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_dev_drop_with_a_tier_gives_every_seat_an_item_of_it()
    {
        var k = Make();

        var reward = await k.Rewards.Reward(900_000, "soldier", InRoom(0), SlotA, ownTable: false, forceTier: 2);

        var drop = Assert.Single(reward!.Drops);
        Assert.Equal(2, drop.SeatOf(SlotA)!.Item!.Item.Tier);
        Assert.Equal(2, drop.SeatOf(SlotB)!.Item!.Item.Tier);
    }
}
