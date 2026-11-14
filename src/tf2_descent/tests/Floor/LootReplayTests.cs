using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Loot;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Horde;

using Xunit;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// The plan's proving line for HO10: a scripted clear's XP and drops equal a
/// seeded Rules replay. Every robot of a level dies through the real kill
/// chain (<see cref="RobotKills"/> telling <see cref="HeroExperience"/> and
/// <see cref="LootRewards"/> in their orders) with two heroes near, the
/// in-memory ledger and the real floor; then the same kills are replayed in
/// the pure rules alone (<see cref="ExperienceRules.KillXp"/>,
/// <see cref="HeroRules.GainXp"/>, <see cref="LootRules"/>' seat rolls and
/// split, the item roller at the ledger's seeds) and every number and item
/// must agree.
/// </summary>
public sealed class LootReplayTests
{
    private const int Depth = 1;
    private const ulong Seed = 0xC1EA_0001;
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

    [Fact]
    public async Task A_scripted_clear_pays_xp_and_drops_equal_to_a_seeded_rules_replay()
    {
        // The game: a level's roster, two heroes standing where each robot dies, the whole chain.
        var horde = new ServerHordeRig();
        var heroes = new HeroRig();
        var rules = heroes.Rules;
        var origins = new Dictionary<EntityHandle, Vector>();
        heroes.Engine.ReturnsEachCall("Entities.GetOrigin", () => origins.GetValueOrDefault(heroes.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
        heroes.Engine.Returns("Players.IsAlive", true);
        heroes.Players.Steam[HeroA] = HeroRig.SteamId + HeroA;
        heroes.Players.Steam[HeroB] = HeroRig.SteamId + HeroB;
        var a = heroes.Join(HeroA)!;
        var b = heroes.Join(HeroB)!;
        var keys = heroes.Keys();
        string nameA = keys.Of(HeroA)!, nameB = keys.Of(HeroB)!;

        var ledger = new InMemoryItemLedger(rules, new NoLedgerHooks(), new StartingKit(rules));
        var floorRig = new LootRig(ledger: ledger);
        var level = new RigLevel(Depth, Seed);
        var experience = new HeroExperience(heroes.Heroes, heroes.Sheets, heroes.Engine, heroes.Engine, new Party.OneParty(), new ClientMessages(heroes.Engine),
                                            new Checkpoints(), heroes.Log);
        // The two heroes are one party (WO9): a kill by either pays both.
        var parties = new TF2.Descent.Server.Party.InMemoryParties(rules, new Party.PartyChanges().Hooks);
        Assert.True((await parties.Make([nameA, nameB])).Ok);
        var party = new HeroLootParty(heroes.Heroes, keys, heroes.Engine, heroes.Engine, rules,
                                      new TF2.Descent.Server.Party.PartyRoster(parties, new TF2.Descent.Server.Party.EnginePartySlots(heroes.Heroes, keys)));
        var rewards = new LootRewards(rules, level, party, ledger, new RigGround(), floorRig.Floor, floorRig.Log);
        var roster = horde.Roster;
        var kills = new RobotKills(new Rosters(roster), horde.Registry, horde.Engine, new Chain(rewards, experience), horde.Log);

        // The replay's starting point, read before any kill: each hero's sheet, and the ledger's view of each hero.
        var sheets = new Dictionary<string, HeroSheet> { [nameA] = a.Sheet!, [nameB] = b.Sheet! };
        await ledger.Load(nameA);
        await ledger.Load(nameB);
        var ledgerSheets = new Dictionary<string, HeroSheet> { [nameA] = ledger.Find(nameA)!.Sheet, [nameB] = ledger.Find(nameB)!.Sheet };

        var told = new List<(uint Sequence, string Key, int Row)>();
        int dropsBefore = 0;
        var placed = new List<IReadOnlyList<FloorDrop>>();
        for (int row = 0; row < roster.Count; row++)
        {
            if (roster.State(row) is RobotState.Dead or RobotState.Empty)
                continue;
            var at = roster.Position(row);
            var where = new Vector(at.X, at.Y, at.Z);
            origins[HeroRig.PlayerOf(HeroA)] = where + new Vector(30, 0, 0);
            origins[HeroRig.PlayerOf(HeroB)] = where + new Vector(0, 30, 0);
            Assert.True(roster.Hurt(row, roster.Life(row)));
            told.Add((kills.Told, LootKey.Of(roster.Template(row), roster.Affixes(row)).ToString(), row));
            Assert.Equal(1, kills.Sweep());
            placed.Add([.. floorRig.Floor.Drops.Skip(dropsBefore)]);
            dropsBefore = floorRig.Floor.Drops.Count;
        }

        Assert.True(told.Count >= 6, $"the stimulus: only {told.Count} robots died");

        // The replay, from the rules alone.
        var seats = new[] { (Hero: nameA, Slot: HeroA), (Hero: nameB, Slot: HeroB) };
        int items = 0;
        long australium = 0;
        for (int i = 0; i < told.Count; i++)
        {
            var (sequence, key, row) = told[i];
            var stats = roster.Stats(row);
            foreach (var (hero, _) in seats)
            {
                long xp = rules.Experience.KillXp(stats, sheets[hero].Level);
                sheets[hero] = rules.Heroes.GainXp(sheets[hero], xp).Sheet;
            }

            var rolls = rules.Loot.PerSeat(Seed, sequence, key, Depth, 2, rules.Salt);
            bool own = roster.IsBoss(row) || roster.IsChampion(row);
            long total = rolls[0].Australium + (own ? rules.Loot.BossAustralium(LootKey.Parse(key).Template) : 0);
            var shares = LootRules.Split(total, 2);
            for (int s = 0; s < 2; s++)
            {
                var (hero, slot) = seats[s];
                var expected = Expected(rules, ledger, ledgerSheets[hero], hero, sequence, s, rolls[s], key, own);
                var got = placed[i].Select(d => d.SeatOf(slot)?.Item?.Item).OfType<ItemInstance>().ToList();
                Assert.Equal(expected.Select(Bytes), got.Select(Bytes));
                Assert.Equal(shares[s], placed[i].Count == 0 ? 0 : placed[i][0].SeatOf(slot)?.Australium ?? 0);
                items += got.Count;
                australium += shares[s];
            }
        }

        // The game's sheets are the replay's: the same XP and level for each hero.
        Assert.Equal((sheets[nameA].Xp, sheets[nameA].Level), (a.Sheet!.Xp, a.Sheet.Level));
        Assert.Equal((sheets[nameB].Xp, sheets[nameB].Level), (b.Sheet!.Xp, b.Sheet.Level));
        Assert.True(a.Sheet.Xp > 0 && items > 0 && australium > 0, $"the stimulus: xp {a.Sheet.Xp}, {items} items, {australium} Australium");
    }

    /// <summary>The items the rules give hero <paramref name="hero"/>'s seat <paramref name="seat"/> of a kill, in the order the ledger is asked.</summary>
    private static List<ItemInstance> Expected(TF2.Descent.Rules.TF2DescentRules rules, InMemoryItemLedger ledger, HeroSheet sheet, string hero,
                                               uint sequence, int seat, KillLoot roll, string key, bool own)
    {
        var row = rules.LevelTable(Depth);
        uint killSeq = rules.Loot.KillSeq(sequence, seat);
        var list = new List<ItemInstance>();
        if (roll.Item)
        {
            var ctx = new ItemRollContext(sheet.ClassId, sheet.Level, Depth, row.ItemLevel, roll.Tier, row.LootTable, "reserve");
            list.Add(rules.ItemRules.Roller.Roll(ctx, ledger.KillSeed(killSeq, hero)));
        }
        else if (roll.Potion is { } potion)
        {
            var ctx = new ItemRollContext(sheet.ClassId, sheet.Level, Depth, row.ItemLevel, 0,
                rules.Content.ItemSettings.ForcedBasePrefix + potion, "potion");
            list.Add(rules.ItemRules.Roller.Roll(ctx, ledger.PotionSeed(killSeq, hero)));
        }

        if (own)
        {
            var ctx = new ItemRollContext(sheet.ClassId, sheet.Level, Depth, row.ItemLevel, -1, row.LootTable, "boss:" + key);
            list.AddRange(rules.BossDrops(ctx, key, ledger.KillSeed(sequence, hero)).Select(rules.ItemRules.Codec.Instance));
        }

        return list;
    }

    private static string Bytes(ItemInstance item) => Convert.ToBase64String(ItemCodec.Encode(item));
}
