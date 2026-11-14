using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Server;

using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Loot;
using TF2.Descent.Tests.Floor;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Horde;

using Xunit;

using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The plan's proving line for HO13: each boss can be killed and drops its table. Each boss dies
/// through the real kill chain (<see cref="RobotKills"/> telling <see cref="HeroExperience"/> and
/// <see cref="LootRewards"/>) with two heroes near, over the in-memory ledger and the floor: each
/// hero's seat holds the boss's own table (bosses' rows of loot.json, rolled by
/// <c>BossDrops</c> for that hero at the ledger's seed), its items and potions, and the XP is the
/// rules' for the boss.
/// </summary>
public sealed class BossLootTests
{
    private const ulong Seed = 0xB055_0001;
    private const int HeroA = 3, HeroB = 4;

    private sealed class Rosters(RobotRoster roster) : IKillRoster
    {
        public RobotRoster? Roster => roster;
    }

    private sealed class Checkpoints : IHeroLevelCheckpoint
    {
        public bool Checkpoint(int entIndex) => true;
    }

    private sealed class Chain(params IRobotKillHook[] hooks) : IHooks<IRobotKillHook>
    {
        private readonly IRobotKillHook[] _sorted = [.. hooks.OrderBy(h => ((IOrderedHook)h).HookOrder)];
        public IReadOnlyList<IRobotKillHook> All => _sorted;
        public void Each(string what, Action<IRobotKillHook> call) { foreach (var h in _sorted) call(h); }
    }

    [Theory]
    [InlineData("sir_nukesalot")]
    [InlineData("major_crits")]
    [InlineData("captain_punch")]
    public async Task A_boss_killed_drops_its_own_table_for_each_hero_and_pays_its_xp(string boss)
    {
        int depth = TheRules.Bosses.Of(boss)!.Depth;
        var level = Row(4, markers: 3, depth: depth, boss: new HordeBoss(boss, 2, new PackMarker(Centre(2), 0)));
        var horde = new ServerHordeRig(level);
        var heroes = new HeroRig();
        var rules = heroes.Rules;
        var origins = new Dictionary<EntityHandle, Vector>();
        heroes.Engine.ReturnsEachCall("Entities.GetOrigin", () => origins.GetValueOrDefault(heroes.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
        heroes.Engine.Returns("Players.IsAlive", true);
        heroes.Players.Steam[HeroA] = HeroRig.SteamId + HeroA;
        heroes.Players.Steam[HeroB] = HeroRig.SteamId + HeroB;
        var a = heroes.Join(HeroA)!;
        heroes.Join(HeroB);
        var keys = heroes.Keys();
        string nameA = keys.Of(HeroA)!, nameB = keys.Of(HeroB)!;

        var ledger = new InMemoryItemLedger(rules, new NoLedgerHooks(), new StartingKit(rules));
        var floorRig = new LootRig(ledger: ledger);
        var experience = new HeroExperience(heroes.Heroes, heroes.Sheets, heroes.Engine, heroes.Engine, new Party.OneParty(), new ClientMessages(heroes.Engine),
                                            new Checkpoints(), heroes.Log);
        var party = new HeroLootParty(heroes.Heroes, keys, heroes.Engine, heroes.Engine, rules, new Party.OneParty());
        var rewards = new LootRewards(rules, new RigLevel(depth, Seed), party, ledger, new RigGround(), floorRig.Floor, floorRig.Log);
        var roster = horde.Roster;
        var kills = new RobotKills(new Rosters(roster), horde.Registry, horde.Engine, new Chain(rewards, experience), horde.Log);
        await ledger.Load(nameA);
        await ledger.Load(nameB);
        var ledgerSheets = new Dictionary<string, Rules.Hero.HeroSheet> { [nameA] = ledger.Find(nameA)!.Sheet, [nameB] = ledger.Find(nameB)!.Sheet };
        long xpBefore = a.Sheet!.Xp;

        int row = Enumerable.Range(0, roster.Count).Single(roster.IsBoss);
        var at = roster.Position(row);
        var where = new Vector(at.X, at.Y, at.Z);
        origins[HeroRig.PlayerOf(HeroA)] = where + new Vector(30, 0, 0);
        origins[HeroRig.PlayerOf(HeroB)] = where + new Vector(0, 30, 0);
        uint sequence = kills.Told;
        Assert.True(roster.Hurt(row, roster.Life(row)));
        Assert.Equal(1, kills.Sweep());
        var drops = floorRig.Floor.Drops.ToList();

        var table = rules.Content.Loot.Boss(boss)!;
        var tableRow = rules.LevelTable(depth);
        string key = LootKey.Of(boss, []).ToString();
        foreach (var (hero, slot) in new[] { (nameA, HeroA), (nameB, HeroB) })
        {
            var sheet = ledgerSheets[hero];
            var ctx = new ItemRollContext(sheet.ClassId, sheet.Level, depth, tableRow.ItemLevel, -1, tableRow.LootTable, "boss:" + key);
            var own = rules.BossDrops(ctx, key, ledger.KillSeed(sequence, hero)).Select(rules.ItemRules.Codec.Instance).Select(Bytes).ToList();
            Assert.Equal(table.Items + table.Potions, own.Count);
            var got = drops.Select(d => d.SeatOf(slot)?.Item?.Item).OfType<ItemInstance>().Select(Bytes).ToList();
            Assert.All(own, item => Assert.Contains(item, got));   // the boss's own table, each item in this hero's seats
        }

        long xp = rules.Experience.KillXp(roster.Stats(row), 1);
        Assert.True(xp > 0);
        Assert.Equal(xpBefore + xp, a.Sheet!.Xp);
    }

    private static string Bytes(ItemInstance item) => Convert.ToBase64String(ItemCodec.Encode(item));
}
