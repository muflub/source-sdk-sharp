using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Level;

namespace TF2.Descent.Server.Loot;

/// <summary>What one kill's loot came to: the kill's Australium pile (null for none), the drops put on the floor (several when a hero got more than one item).</summary>
public sealed record LootReward(uint Sequence, IReadOnlyList<LootMember> Members, IReadOnlyList<KillLoot> Rolls,
                                AustraliumPile? Pile, IReadOnlyList<FloorDrop> Drops);

/// <summary>
/// Kill to reward (plan_descent_logic HO10; plan_tf2_rpg 2c, 5e, R4): on
/// every robot death (<see cref="IRobotKillHook"/>) the party near the kill
/// is paid its loot, put on the floor where the robot fell, a seat a hero.
///
/// <list type="bullet">
/// <item><b>Who.</b> The heroes within the party range of the kill
/// (<see cref="ILootParty.Near"/>: the XP's range; the killer first when he
/// is one of them), at most the rules' seats. Seat <c>s</c> is the
/// <c>s</c>-th of them.</item>
/// <item><b>The rolls.</b> Each seat's roll is the rules' own
/// (<see cref="LootRules.Roll"/> with <c>killSeq = </c><see cref="LootRules.KillSeq"/>(kill, seat),
/// the level's seed and depth, the robot's loot key with its affixes, the
/// rules' salt): exactly the <c>KillRoll</c> the host replays. An item is
/// asked of the ledger (<see cref="IItemLedger.KillDrop"/>: hosted, the
/// hero's reserve answers and the reveal goes out after); a potion likewise
/// (<see cref="IItemLedger.PotionDrop"/>). A champion or the boss adds its own
/// table for every member (<see cref="IItemLedger.BossDrop"/>).</item>
/// <item><b>Australium.</b> Seat 0's roll carries the kill's (plus the
/// boss's own unhosted, <see cref="LootRules.BossAustralium"/>): ONE pile on
/// the drop (the user's ruling L24, SDK 2.8), asked of the ledger
/// (<see cref="IItemLedger.KillAustralium"/>: hosted the host replays seat 0's
/// roll under the first member's lease). Every seat shows it; whoever of the
/// killer's party takes it first takes it for all, each member credited his
/// share (the rules' <c>AustraliumShares</c>: the split from the killer on and
/// each one's found share) at once.</item>
/// <item><b>Where.</b> On the floor under the robot (<see cref="ILootGround"/>);
/// a robot that died over a pit (no floor within reach below) drops at the
/// feet of the first member, so nothing lies where no one can reach it. A
/// hero with more than one item (a boss's table) gets one drop a round, each
/// a little to the side of the last.</item>
/// <item><b>When.</b> The ledger answers by task. The drop is placed when
/// every answer is in, on the game thread; a level that ends before then
/// places nothing (<see cref="LootFloor.Level"/>), and a failure is logged,
/// never thrown.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class LootRewards(TF2DescentRules rules, ILevelData level, ILootParty party, IItemLedger ledger, ILootGround ground,
                                LootFloor floor, ILog log)
    : IRobotKillHook, IOrderedHook
{
    /// <summary>
    /// In TF2 Descent's hero block of the kill chain, after the experience
    /// (<see cref="HeroExperience.Order"/>, First + 300): the kill's XP, the
    /// level-up with its refill and the bestiary's count are done before the
    /// loot is asked for, so a slow or failing ledger delays or loses nothing
    /// of the hero's sheet, and the roll's context reads the hero as the kill
    /// left him.
    /// </summary>
    public const int Order = ModStages.First + 310;

    /// <summary>How far each further round of a hero's items lies from the first.</summary>
    public const float RoundSpacing = 20f;

    public int HookOrder => Order;

    public void OnRobotKilled(in RobotKill kill) =>
        _ = Reward(kill.Sequence, LootKey.Of(kill.Template, kill.Affixes).ToString(), kill.Position, kill.Killer, kill.Boss || !kill.Affixes.IsEmpty);

    /// <summary>The depth the level's rolls are made at: the level's, 1 off a level (a dev drop in town).</summary>
    public int Depth => level.Current is { Depth: > 0 } l ? l.Depth : 1;

    /// <summary>The instance seed the level's rolls replay from: the level's (Booting's, hosted); 0 off a level.</summary>
    public ulong Seed => level.Current?.Seed ?? 0;

    /// <summary>
    /// Pays kill <paramref name="sequence"/> of the robot <paramref name="robotKey"/>
    /// (<see cref="LootKey"/>) that died at <paramref name="at"/>; answers what
    /// went on the floor once the ledger has answered (null when nobody was near,
    /// or the level ended first).
    /// </summary>
    /// <param name="forceTier">A dev drop's tier: every seat's roll an item of it (its Australium as rolled); -1 for the rules' rolls.</param>
    public async Task<LootReward?> Reward(uint sequence, string robotKey, Vector at, int killer, bool ownTable, int forceTier = -1)
    {
        var members = party.Near(at, killer);
        if (members.Count == 0)
            return null;

        int levelAt = floor.Level;
        try
        {
            int depth = Depth;
            int size = members.Count;
            var place = ground.TryGround(at, out var g) ? g : members[0].Origin;
            var rolls = new KillLoot[size];
            for (int s = 0; s < size; s++)
            {
                rolls[s] = rules.Loot.Roll(Seed, rules.Loot.KillSeq(sequence, s), robotKey, depth, size, rules.Salt);
                if (forceTier >= 0)
                    rolls[s] = rolls[s] with { Outcome = LootOutcome.Item, Tier = forceTier, Potion = null };
            }

            // The kill's Australium, one pile (L24): hosted the host replays seat 0's roll and its pile is the roll's
            // alone (KillRoll has no boss table); unhosted the game's roll and the boss's own.
            bool hosted = ledger.Kind == LedgerKind.Hosted;
            long total = rolls[0].Australium + (ownTable && !hosted ? rules.Loot.BossAustralium(LootKey.Parse(robotKey).Template) : 0);
            var pileAsked = total > 0
                ? ledger.KillAustralium(new KillPile(members[0].Hero, rules.Loot.KillSeq(sequence, 0), robotKey, size, total, place))
                : null;

            var asked = new List<(int Seat, Task<DropResult> Task)>();
            for (int s = 0; s < size; s++)
            {
                var m = members[s];
                var others = members.Where(o => o.Hero != m.Hero).Select(o => o.Hero).ToList();
                uint killSeq = rules.Loot.KillSeq(sequence, s);
                if (rolls[s].Item)
                    asked.Add((s, ledger.KillDrop(new KillDrop(m.Hero, rolls[s].Tier, killSeq, robotKey, depth, place, others))));
                else if (rolls[s].Potion is { } potion)
                    asked.Add((s, ledger.PotionDrop(m.Hero, potion, killSeq, depth, place)));
            }

            if (ownTable)
                asked.Add((-1, ledger.BossDrop(new BossDrop([.. members.Select(m => m.Hero)], sequence, robotKey, depth, place))));

            var items = new List<FloorItem>[size];
            for (int s = 0; s < size; s++)
                items[s] = [];
            foreach (var (seat, task) in asked)
            {
                var answer = await task;
                if (floor.Level != levelAt)
                    return null;
                if (!answer.Ok)
                {
                    log.Warning($"[TF2Descent/Loot] kill {sequence}: the ledger refused a drop: {answer.Refusal}\n");
                    continue;
                }

                foreach (var item in answer.Items)
                {
                    int to = seat >= 0 ? seat : members.ToList().FindIndex(m => m.Hero == item.ForHero);
                    if (to >= 0)
                        items[to].Add(item);
                }
            }

            AustraliumPile? pile = null;
            if (pileAsked is not null)
            {
                var answer = await pileAsked;
                if (floor.Level != levelAt)
                    return null;
                if (answer.Ok)
                    pile = answer.Pile;
                else
                    log.Warning($"[TF2Descent/Loot] kill {sequence}: the ledger refused the Australium pile: {answer.Refusal}\n");
            }

            int rounds = Math.Max(1, items.Max(i => i.Count));
            var drops = new List<FloorDrop>();
            for (int r = 0; r < rounds; r++)
            {
                var shareList = new List<LootShare>();
                for (int s = 0; s < size; s++)
                    shareList.Add(new LootShare(members[s].Hero, members[s].Player, r < items[s].Count ? items[s][r] : null, 0));
                if (floor.Place(place + new Vector(r * RoundSpacing, 0f, 0f), shareList, r == 0 ? pile : null) is { } drop)
                    drops.Add(drop);
            }

            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Loot] kill {sequence} ({robotKey}, depth {depth}): {size} heroes, rolls [{string.Join(", ", rolls.Select(Say))}], " +
                $"Australium {total}{(pile is { } p ? $" in one pile of {p.Australium} (#{p.Id})" : total > 0 ? " (no pile)" : "")}, {drops.Count} drops\n"));
            return new LootReward(sequence, members, rolls, pile, drops);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Warning($"[TF2Descent/Loot] kill {sequence}: no loot: {e.GetType().Name}: {e.Message}\n");
            return null;
        }
    }

    private static string Say(KillLoot l) => l.Outcome switch
    {
        LootOutcome.Item => "item tier " + l.Tier.ToString(CultureInfo.InvariantCulture),
        LootOutcome.Potion => "potion " + l.Potion,
        _ => "nothing",
    };
}
