using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot's team role from the allocator (plan_bots_teams_tf2_part_g G.2.2,
/// TFDecider.Roles.cs over <see cref="RoleDeal"/>): a red team of bots on the synthetic field
/// (<see cref="TFField"/>), each dealing for itself from its scoreboard, its HUD's flags and
/// what it sees of the team's people. The split follows the policy at every size, a person seen
/// defending is one bot fewer defending, a role is not flipped inside the hold time, and the
/// same inputs are the same deal.
///
/// <para>The policy a decider is started with is CTF's (<see cref="TFDecider.CtfRoleSplit"/>): a
/// third defend, the team's last bots, and every other bot but the carrier goes with the carrier.
/// The facts about an attacker who is not dealt the escort start their squad with a policy that
/// has one (<see cref="RoleSplits.ThirdDefend"/>).</para>
/// </summary>
public sealed class TFRoleTests
{
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    /// <summary>A red team's bots, in slots 2, 4, 6, ..., each told of the others (and of <paramref name="person"/>, if any) by its scoreboard's team column.</summary>
    private sealed class Squad
    {
        public readonly TFField[] Bots;
        private readonly ActorId? _person;
        private bool _told;

        public Squad(int bots, ActorId? person = null, RoleSplit? split = null)
        {
            Bots = [.. Enumerable.Range(0, bots).Select(i => new TFField(slot: 2 + 2 * i, seed: (ulong)(40 + i)))];
            _person = person;
            if (split is not null)
            {
                foreach (TFField f in Bots)
                    f.Decider.StartRoles(split);
            }
        }

        /// <summary>One decide of every bot at mid field, the person (if any) in everyone's sight at <paramref name="personAt"/>.</summary>
        public void Step(ObjectiveKnowledge hud, AiVector? personAt = null)
        {
            foreach (TFField f in Bots)
            {
                var tick = new Tick(f.Tick + DecideSchedule.Period);
                AiEvent[] events = _told ? [] :
                [
                    .. Bots.Where(o => o != f).Select(o => AiEvent.ActorTeam(f.Bot, tick, o.Bot, TFField.Red, 0)),
                    .. _person is { } p ? new[] { AiEvent.ActorTeam(f.Bot, tick, p, TFField.Red, 0) } : [],
                ];
                ActorFacts[] seen = _person is { } who && personAt is { } at
                    ? [new ActorFacts(who, at, AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Red)]
                    : [];
                f.StepHearing(TFField.Self(Mid), hud, events, seen);
                // The role is worked out when it is asked for, as the tree asks at each decide.
                _ = f.Decider.Dealt;
            }

            _told = true;
        }

        public int Count(TeamRole role) => Bots.Count(f => f.Decider.Dealt == role);

        public TeamRole[] Roles => [.. Bots.Select(f => f.Decider.Dealt)];
    }

    private static readonly ActorId Person = new(30, 0x4000_0009);

    /// <summary>The enemy's flag at mid field in the hands of the squad's first bot (an attacker: the last bots defend).</summary>
    private static ObjectiveKnowledge Carried(Squad s) => TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: s.Bots[0].Bot.Slot);

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void TheSplitFollowsThePolicyAtEveryTeamSize(int size)
    {
        var squad = new Squad(size);
        squad.Step(TFField.Ctf());
        RoleCounts policy = TFDecider.CtfRoleSplit(size);
        Assert.Equal((policy.Defend, size - policy.Defend), (squad.Count(TeamRole.Defend), squad.Count(TeamRole.Attack)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void WhileATeammateCarriesTheEscortsAreThePolicys(int size)
    {
        var squad = new Squad(size, split: RoleSplits.ThirdDefend);
        squad.Step(Carried(squad));
        RoleCounts policy = RoleSplits.ThirdDefend(size);
        Assert.Equal((policy.Defend, Math.Min(policy.Escort, size - policy.Defend - 1)), (squad.Count(TeamRole.Defend), squad.Count(TeamRole.Escort)));
    }

    /// <summary>Under CTF's policy, while a teammate carries, every bot but the defenders and the carrier is dealt the escort.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void UnderTheCtfPolicyEveryBotButTheDefendersAndTheCarrierEscorts(int size)
    {
        var squad = new Squad(size);
        squad.Step(Carried(squad));
        int defend = TFDecider.CtfRoleSplit(size).Defend;
        Assert.Equal((defend, size - defend - 1, 1), (squad.Count(TeamRole.Defend), squad.Count(TeamRole.Escort), squad.Count(TeamRole.Attack)));
        Assert.Equal(TeamRole.Attack, squad.Bots[0].Decider.Dealt);
    }

    /// <summary>The policy is the decider's input (the constructor's start call): half defending deals half.</summary>
    [Fact]
    public void ThePolicyTheBotWasStartedWithIsTheOneItDealsBy()
    {
        var squad = new Squad(6, split: n => new RoleCounts(n / 2, 0));
        squad.Step(TFField.Ctf());
        Assert.Equal(3, squad.Count(TeamRole.Defend));
    }

    [Fact]
    public void APersonSeenByTheTeamsFlagIsOneBotFewerDefending()
    {
        var away = new Squad(5, Person);
        away.Step(TFField.Ctf(), personAt: Mid + new AiVector(900f, 0f, 0f));
        var home = new Squad(5, Person);
        home.Step(TFField.Ctf(), personAt: TFField.RedFlag + new AiVector(120f, 0f, 0f));
        Assert.Equal((2, 1), (away.Count(TeamRole.Defend), home.Count(TeamRole.Defend)));
    }

    /// <summary>A person nobody has seen is taken to attack: the bots fill the policy's defenders themselves.</summary>
    [Fact]
    public void APersonNobodyHasSeenIsTakenToAttack()
    {
        var squad = new Squad(5, Person);
        squad.Step(TFField.Ctf());
        Assert.Equal(TFDecider.CtfRoleSplit(6).Defend, squad.Count(TeamRole.Defend));
    }

    [Fact]
    public void APersonSeenWithTheCarrierIsOneBotFewerEscorting()
    {
        var alone = new Squad(5, Person, RoleSplits.ThirdDefend);
        alone.Step(Carried(alone), personAt: TFField.BlueFlag);
        var together = new Squad(5, Person, RoleSplits.ThirdDefend);
        together.Step(Carried(together), personAt: Mid + new AiVector(200f, 0f, 0f));
        Assert.Equal((2, 1), (alone.Count(TeamRole.Escort), together.Count(TeamRole.Escort)));
    }

    /// <summary>
    /// The flag taken and returned every second for a minute: in every decide a teammate carries, every bot
    /// but the defenders and the carrier is its escort (nobody waits a hold out to go with a carrier), and no
    /// bot is taken off the escort more than once a hold time.
    /// </summary>
    [Fact]
    public void AStormOfFlagEventsTakesNoBotOffTheEscortMoreThanOnceAHoldTime()
    {
        var squad = new Squad(6);
        int perSecond = (int)(1f / (DecideSchedule.Period * 0.015f));
        const int Seconds = 60;
        int[] left = new int[squad.Bots.Length];
        int missed = 0;
        for (int i = 0; i < Seconds * perSecond; i++)
        {
            bool carried = i / perSecond % 2 == 1;
            TeamRole[] was = squad.Roles;
            squad.Step(carried ? Carried(squad) : TFField.Ctf());
            TeamRole[] now = squad.Roles;
            for (int b = 0; b < now.Length; b++)
                left[b] += was[b] == TeamRole.Escort && now[b] != TeamRole.Escort ? 1 : 0;
            missed += carried ? now[1..4].Count(r => r != TeamRole.Escort) : 0;
        }

        Assert.Equal(0, missed);
        Assert.InRange(left.Max(), 1, (int)(Seconds / RoleHold.HoldSeconds));
    }

    /// <summary>
    /// A carrier falls and another teammate has the flag two seconds later: the bots let go of the escort a
    /// moment ago go with the new carrier at once, as every attacker did before the roles were dealt. Held to
    /// the hold time, their Escort score would be a not-the-escort's 0.09 for the eight seconds left.
    /// </summary>
    [Fact]
    public void ANewCarrierSoonAfterTheLastIsGoneWithAtOnce()
    {
        var squad = new Squad(6);
        int perSecond = (int)(1f / (DecideSchedule.Period * 0.015f));
        ObjectiveKnowledge first = Carried(squad), second = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: squad.Bots[1].Bot.Slot);
        for (int i = 0; i < (int)((RoleHold.HoldSeconds + 2f) * perSecond); i++)
            squad.Step(first);
        for (int i = 0; i < 2 * perSecond; i++)
            squad.Step(TFField.Ctf(blue: ObjectiveState.Dropped, blueAt: Mid));
        TeamRole[] between = squad.Roles;
        squad.Step(second);
        Assert.Equal([TeamRole.Attack, TeamRole.Attack, TeamRole.Attack, TeamRole.Attack, TeamRole.Defend, TeamRole.Defend], between);
        Assert.Equal([TeamRole.Escort, TeamRole.Attack, TeamRole.Escort, TeamRole.Escort, TeamRole.Defend, TeamRole.Defend], squad.Roles);
        Assert.Equal(TFDecider.AttackerEscort, squad.Bots[2].Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort));
    }

    /// <summary>With no event and nobody changing what they do, the deal is not worked out at every decide.</summary>
    [Fact]
    public void WithNoEventTheDealIsWorkedOutOnTheClockOnly()
    {
        var squad = new Squad(4);
        int decides = (int)(40f / (DecideSchedule.Period * 0.015f));
        for (int i = 0; i < decides; i++)
            squad.Step(TFField.Ctf());
        Assert.InRange(squad.Bots[0].Decider.RoleDeals, 2, (int)(40f / TFDecider.RedealSeconds) + 1);
    }

    [Fact]
    public void TheSameInputsAreTheSameDeal()
    {
        Squad a = new(7, Person), b = new(7, Person);
        for (int i = 0; i < 30; i++)
        {
            ObjectiveKnowledge hud = i % 9 < 4 ? TFField.Ctf() : Carried(a);
            AiVector person = i < 15 ? TFField.RedFlag : Mid;
            a.Step(hud, person);
            b.Step(hud, person);
            Assert.Equal(a.Roles, b.Roles);
        }

        Assert.True(a.Roles.Distinct().Count() >= 2, "the control: the deal has more than one role in it");
    }

    /// <summary>
    /// Until its scoreboard shows it a teammate there is nothing to share out, and the bot starts as
    /// the policy's team of one: under CTF's an attacker, whatever its slot (every third slot
    /// started as a defender, the lopsidedness db31c9b3d took out of the teams).
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    public void ABotThatKnowsOfNoTeammateStartsAnAttackerWhateverItsSlot(int slot)
    {
        var f = new TFField(slot: slot);
        f.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal((false, TeamRole.Attack, TFTeamRole.Attack, TFDecider.Fetch), (f.Decider.RolesDealt, f.Decider.Dealt, f.Decider.Role, f.Branch));
    }

    /// <summary>The start is the policy's, not a rule of its own: started with a policy whose team of one defends, a lone bot defends.</summary>
    [Fact]
    public void TheStartRoleIsThePolicysTeamOfOne()
    {
        var f = new TFField(slot: 2);
        f.Decider.StartRoles(n => new RoleCounts(1, 0));
        f.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal((false, TeamRole.Defend, TFDecider.Defend), (f.Decider.RolesDealt, f.Decider.Dealt, f.Branch));
    }

    /// <summary>An escort reads as an attacker to the tree's utilities, which know two roles.</summary>
    [Fact]
    public void AnEscortReadsAsAnAttackerToTheUtilities()
    {
        var squad = new Squad(6);
        squad.Step(Carried(squad));
        TFField escort = squad.Bots.First(f => f.Decider.Dealt == TeamRole.Escort);
        Assert.Equal(TFTeamRole.Attack, escort.Decider.RoleRead);
    }

    /// <summary>
    /// Once roles are dealt, going with the carrier is the escort's job: its Escort score stands exactly as the
    /// tree gave it (the role neither raises nor lowers it), and an attacker's falls under roaming's 0.1.
    /// </summary>
    [Fact]
    public void OnlyTheDealtEscortsScoreOfGoingWithTheCarrierStandsUp()
    {
        var squad = new Squad(6, split: RoleSplits.ThirdDefend);
        squad.Step(Carried(squad));
        TFField escort = squad.Bots.First(f => f.Decider.Dealt == TeamRole.Escort);
        TFField attacker = squad.Bots.First(f => f.Decider.Dealt == TeamRole.Attack && f != squad.Bots[0]);
        Assert.Equal((TFDecider.AttackerEscort, true),
                     (escort.Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort), attacker.Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort) < 0.1f));
    }

    /// <summary>A defender's score of going with the carrier is cut to <see cref="TFDecider.NotTheEscort"/> of it, under roaming's 0.1: it is the one bot the CTF policy keeps home.</summary>
    [Fact]
    public void ADefendersScoreOfGoingWithTheCarrierIsCut()
    {
        var squad = new Squad(6);
        squad.Step(Carried(squad));
        TFField defender = squad.Bots[^1];
        Assert.Equal((TeamRole.Defend, TFDecider.DefenderEscort * TFDecider.NotTheEscort),
                     (defender.Decider.Dealt, defender.Decider.Weigh(TFDecider.Escort, TFDecider.DefenderEscort)));
    }

    /// <summary>The decider's own role, which the tree's utilities read, is the allocator's: in a team of six the last two bots defend and the rest attack.</summary>
    [Fact]
    public void TheDecidersRoleIsTheAllocatorsAnswer()
    {
        var squad = new Squad(6);
        squad.Step(TFField.Ctf());
        Assert.Equal([TFTeamRole.Attack, TFTeamRole.Attack, TFTeamRole.Attack, TFTeamRole.Attack, TFTeamRole.Defend, TFTeamRole.Defend],
                     squad.Bots.Select(f => f.Decider.Role));
    }

    /// <summary>
    /// The role the tree reads is the allocator's, people and all: with a person (the host, slot 1) seen by the
    /// team's flag, one bot defends where two would, and the bot let off goes for the enemy's flag.
    /// </summary>
    [Fact]
    public void APersonSeenDefendingLetsABotOffInTheTreesRole()
    {
        var host = new ActorId(1, 0x4000_0001);
        Squad away = new(5, host), home = new(5, host);
        away.Step(TFField.Ctf(), personAt: Mid + new AiVector(900f, 0f, 0f));
        home.Step(TFField.Ctf(), personAt: TFField.RedFlag + new AiVector(120f, 0f, 0f));
        Assert.Equal((2, 1), (away.Bots.Count(f => f.Decider.Role == TFTeamRole.Defend), home.Bots.Count(f => f.Decider.Role == TFTeamRole.Defend)));
        Assert.Equal((TFDecider.Defend, TFDecider.Fetch), (away.Bots[3].Branch, home.Bots[3].Branch));
    }

    /// <summary>
    /// A team of six under CTF's policy whose first bot carries the flag: every other bot but the two
    /// defenders goes with it, as before the allocator, and the defenders stay by their flag.
    /// </summary>
    [Fact]
    public void EveryBotButTheDefendersGoesWithTheCarrier()
    {
        var squad = new Squad(6);
        for (int i = 0; i < 3; i++)
            squad.Step(Carried(squad));
        string[] took = [.. squad.Bots.Select(f => f.Branch)];
        Assert.Equal([TFDecider.Deliver, TFDecider.Escort, TFDecider.Escort, TFDecider.Escort, TFDecider.Defend, TFDecider.Defend], took);
    }

    /// <summary>
    /// Under a policy with two escorts in a team of six, only the two dealt the escort go with the carrier:
    /// the attacker left over does not (it roams on toward the enemy), and the defenders stay by their flag.
    /// </summary>
    [Fact]
    public void OnlyTheDealtEscortsGoWithTheCarrier()
    {
        var squad = new Squad(6, split: RoleSplits.ThirdDefend);
        for (int i = 0; i < 3; i++)
            squad.Step(Carried(squad));
        string[] took = [.. squad.Bots.Select(f => f.Branch)];
        Assert.Equal([TFDecider.Deliver, TFDecider.Escort, TFDecider.Escort, TFDecider.Roam, TFDecider.Defend, TFDecider.Defend], took);
    }

    /// <summary>A bot alone goes for the flag; the same slot told it is the last of a team of three is dealt the defence and holds its own.</summary>
    [Fact]
    public void ABotDealtTheDefencePlaysIt()
    {
        TFField alone = new(slot: 5), last = new(slot: 5, mates: [2, 4]);
        alone.Step(TFField.Self(Mid), TFField.Ctf());
        last.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal((TFDecider.Fetch, false, TFDecider.Defend, true), (alone.Branch, alone.Decider.RolesDealt, last.Branch, last.Decider.RolesDealt));
    }

    // ------------------------------------------------------------------ what the scoreboard read gave (db31c9b3d), through the allocator

    /// <summary>A match's bots, each told of every player's team by its scoreboard and stood at mid field for one decide; the roles the trees read, by slot.</summary>
    private static Dictionary<int, TFTeamRole> Match(int[] red, int[] blue, params ActorId[] people)
    {
        var roles = new Dictionary<int, TFTeamRole>();
        foreach ((int[] slots, int team) in new[] { (red, TFField.Red), (blue, TFField.Blue) })
        {
            foreach (int slot in slots.Where(s => people.All(p => p.Slot != s)))
            {
                var f = new TFField(slot: slot);
                foreach ((int[] others, int theirs) in new[] { (red, TFField.Red), (blue, TFField.Blue) })
                {
                    foreach (int other in others.Where(o => o != slot))
                        f.Memory.Apply(AiEvent.ActorTeam(f.Bot, new Tick(1), people.FirstOrDefault(p => p.Slot == other, new ActorId(other, 1)), theirs, 0));
                }

                f.Step(TFField.Self(Mid, team: team), TFField.Ctf());
                roles[slot] = f.Decider.Role;
            }
        }

        return roles;
    }

    private static int[] Defenders(Dictionary<int, TFTeamRole> roles, int[] team) => [.. team.Where(s => roles.TryGetValue(s, out TFTeamRole r) && r == TFTeamRole.Defend).Order()];

    /// <summary>
    /// At 2, 4, 6 and 8 a side, whatever slots the teams hold, each team's defenders are exactly the CTF
    /// policy's count, and they are the team's last slots.
    /// </summary>
    [Theory]
    [InlineData(new[] { 3, 4 }, new[] { 2, 5 })]
    [InlineData(new[] { 3, 5, 6, 9 }, new[] { 2, 4, 7, 8 })]
    [InlineData(new[] { 2, 3, 6, 9, 12, 13 }, new[] { 4, 5, 7, 8, 10, 11 })]
    [InlineData(new[] { 2, 3, 6, 9, 12, 15, 16, 17 }, new[] { 4, 5, 7, 8, 10, 11, 13, 14 })]
    public void EachTeamsDefendersAreThePolicysCountAndItsLastSlots(int[] red, int[] blue)
    {
        Dictionary<int, TFTeamRole> roles = Match(red, blue);
        int defend = TFDecider.CtfRoleSplit(red.Length).Defend;
        Assert.Equal(red.Order().TakeLast(defend), Defenders(roles, red));
        Assert.Equal(blue.Order().TakeLast(defend), Defenders(roles, blue));
    }

    /// <summary>
    /// Live at 4v4 on ctf_2fort red held slots 3, 5, 6 and 9 and blue 2, 4, 7 and 8: a defender every third
    /// slot gave red three and blue none. Each team has one, its last slot.
    /// </summary>
    [Fact]
    public void BothTeamsOfAFourASideHaveOneDefenderEach()
    {
        int[] red = [3, 5, 6, 9], blue = [2, 4, 7, 8];
        Dictionary<int, TFTeamRole> roles = Match(red, blue);
        Assert.Equal([9], Defenders(roles, red));
        Assert.Equal([8], Defenders(roles, blue));
    }

    /// <summary>
    /// A listen server's host (slot 1, a person) on a team of bots is never the one counted on to defend:
    /// the team's defender is its last bot, though no bot has seen the host anywhere.
    /// </summary>
    [Fact]
    public void AListenHostIsNeverTheDefender()
    {
        int[] red = [1, 3, 5, 7], blue = [2, 4, 6, 8];
        Dictionary<int, TFTeamRole> roles = Match(red, blue, new ActorId(1, 0x4000_0001));
        Assert.Equal([7], Defenders(roles, red));
        Assert.Equal([8], Defenders(roles, blue));
    }

    /// <summary>Nor is a person in the team's last slot counted on: the last bot defends until someone is seen to.</summary>
    [Fact]
    public void APersonInTheLastSlotIsNotCountedOnToDefend()
    {
        int[] red = [3, 5, 7, 9], blue = [2, 4, 6, 8];
        Dictionary<int, TFTeamRole> roles = Match(red, blue, new ActorId(9, 0x4000_0001));
        Assert.Equal([7], Defenders(roles, red));
    }

    /// <summary>The carrier's "Help!", as the voice tap tells it to <paramref name="f"/>.</summary>
    private static void HearHelp(TFField f, ActorId from, AiVector at)
        => f.Memory.Apply(AiEvent.Callout(f.Bot, new Tick(f.Tick + 1), from, new SayIntent(CalloutKind.Help, at, 0, 0, 0, 0, false)));

    /// <summary>
    /// The voice menu, in (G.2.4): the carrier's "Help!" makes going with it worth the escort's score to an
    /// attacker who was not dealt the escort, for its Teamwork's followSeconds, and then no longer.
    /// </summary>
    [Fact]
    public void TheCarriersCallForHelpRaisesEscortForAWhile()
    {
        var squad = new Squad(6, split: RoleSplits.ThirdDefend);
        squad.Step(Carried(squad));
        TFField carrier = squad.Bots[0];
        TFField attacker = squad.Bots.First(f => f.Decider.Dealt == TeamRole.Attack && f != carrier);
        float before = attacker.Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort);
        HearHelp(attacker, carrier.Bot, Mid);
        squad.Step(Carried(squad));
        float called = attacker.Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort);
        for (int i = 0; i < (int)(attacker.Params.FollowSeconds / (DecideSchedule.Period * 0.015f)) + 2; i++)
            squad.Step(Carried(squad));
        float after = attacker.Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort);
        Assert.Equal((true, TFDecider.AttackerEscort, true), (before < 0.1f, called, after < 0.1f));
    }

    /// <summary>A teammate's "Help!" from the far end of the map, not the carrier's and not from beside it, raises no escort.</summary>
    [Fact]
    public void ACallForHelpFromSomeoneAwayFromTheCarrierRaisesNoEscort()
    {
        var squad = new Squad(6, split: RoleSplits.ThirdDefend);
        squad.Step(Carried(squad));
        TFField attacker = squad.Bots.First(f => f.Decider.Dealt == TeamRole.Attack && f != squad.Bots[0]);
        HearHelp(attacker, squad.Bots[^1].Bot, TFField.RedFlag);
        squad.Step(Carried(squad));
        Assert.True(attacker.Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort) < 0.1f);
    }

    /// <summary>A bot not yet dealt a role weighs the Escort branch as the tree scored it: it goes with the carrier as it always did.</summary>
    [Fact]
    public void ABotNotDealtARoleWeighsEscortAsTheTreeScoredIt()
    {
        var f = new TFField(slot: 2);
        f.Step(TFField.Self(Mid), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: 4));
        Assert.Equal(TFDecider.AttackerEscort, f.Decider.Weigh(TFDecider.Escort, TFDecider.AttackerEscort));
    }
}
