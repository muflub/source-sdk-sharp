using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot reacting to what it sees that is not a player (plan_bots_teams_tf2_part_g G.2.3,
/// TFDecider.Things.cs): it gets out of the way of a rocket or a pipe whose blast would reach it,
/// after its reaction time and by its Agility; it backs out of a sentry's range, remembers the
/// sentry for the round and thinks less of the objectives inside its range for a while; and it
/// backs off an invulnerable enemy. Every reach is <see cref="TFKnowledge"/>'s.
/// </summary>
public sealed class TFThingsTests
{
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private const float RocketSpeed = 1100f, Decide = DecideSchedule.Period * 0.015f;
    private const int EnemySlot = 5;

    /// <summary>A bot that dodges every blast it can, as soon as it has reacted (the facts on Agility use the mod's curves instead).</summary>
    private static TFField Nimble(int slot = 2, ulong seed = 7) => new(slot: slot, seed: seed, tune: p => p with { DodgeChance = 1f, DodgeLateMs = 0f });

    private static ThingFacts Rocket(AiVector at, AiVector toward, int team = TFField.Blue, int owner = EnemySlot, int id = 900)
        => new(id, ThingKind.Projectile, (int)TFThingType.Rocket, at, (toward - at).Normalised() * RocketSpeed, team, owner);

    private static ThingFacts Sentry(AiVector at, int team = TFField.Blue, int id = 700, int level = 1)
        => new(id, ThingKind.Building, (int)TFThingType.Sentry, at, default, team, EnemySlot, level);

    /// <summary>One decide with <paramref name="things"/> in sight, and the reflex the bot worked out about them in it.</summary>
    private static (string Why, MoveIntent Move) See(TFField f, ThingFacts[] things, ObjectiveKnowledge? hud = null, SelfState? self = null, params ActorFacts[] sighted)
    {
        f.StepSeeing(self ?? TFField.Self(Mid), hud ?? TFField.Ctf(), things, null, sighted);
        return f.Decider.Reflex;
    }

    /// <summary>A rocket fired at the bot's middle from <paramref name="from"/> east of it, <paramref name="aside"/> to the side of the line, flown for <paramref name="decides"/> decides: the bot's reflex at each.</summary>
    private static List<(string Why, MoveIntent Move)> Flight(TFField f, float from, int decides, float aside = 0f, int team = TFField.Blue, int owner = EnemySlot)
    {
        AiVector body = Mid + new AiVector(0f, aside, 41f);
        AiVector at = body + new AiVector(from, 0f, 0f);
        var seen = new List<(string, MoveIntent)>();
        for (int i = 0; i < decides; i++)
        {
            seen.Add(See(f, [Rocket(at, body, team, owner)]));
            at += new AiVector(-RocketSpeed * Decide, 0f, 0f);
        }

        return seen;
    }

    [Fact]
    public void ARocketComingAtTheBotIsSteppedOutOfAcrossItsPath()
    {
        List<(string Why, MoveIntent Move)> flight = Flight(Nimble(), from: 1500f, decides: 6);
        (string why, MoveIntent move) = flight[^1];
        Assert.Equal((TFDecider.DodgeVerdict, MoveKind.Strafe), (why, move.Kind));
        // Across the flight (which is along x), not along it.
        Assert.True(MathF.Abs(move.Direction.Y) > 0.99f && MathF.Abs(move.Direction.X) < 0.05f, $"stepped {move.Direction}");
    }

    [Fact]
    public void ABlastNearlyOnTheBotIsJumpedAwayFrom()
    {
        // Seen from 1,000 u off, it is 0.36 s away six decides on: too near for a step.
        List<(string Why, MoveIntent Move)> flight = Flight(Nimble(), from: 1000f, decides: 7);
        (string why, MoveIntent move) = flight[^1];
        Assert.Equal((TFDecider.DodgeVerdict, MoveKind.Jump, (byte)1), (why, move.Kind, move.CornerCount));
        Assert.True(MathF.Abs(move.Corner0.Y - Mid.Y) > TFDecider.DodgeJumpUnits * 0.9f, $"jumps toward {move.Corner0} from {Mid}");
    }

    /// <summary>A bot of the hard preset reacts in about 0.16 s and dodges 0.3 s after that: nothing at first sight, a dodge half a second on.</summary>
    [Fact]
    public void NothingIsDoneBeforeTheReactionTime()
    {
        var f = new TFField(tune: p => p with { DodgeChance = 1f });
        List<(string Why, MoveIntent Move)> flight = Flight(f, from: 1500f, decides: 8);
        int first = flight.FindIndex(r => r.Why == TFDecider.DodgeVerdict);
        float after = (f.Params.ReactionMs + f.Params.DodgeLateMs) * 0.001f;
        Assert.True(after > Decide * 2, $"the control: the bot's dodge comes {after} s after first sight");
        Assert.Equal((int)MathF.Ceiling(after / Decide), first);
    }

    [Fact]
    public void TheBotsOwnRocketIsNotDodged()
    {
        TFField f = Nimble();
        Assert.DoesNotContain(Flight(f, from: 1500f, decides: 10, team: TFField.Red, owner: f.Bot.Slot), r => r.Why.Length != 0);
    }

    [Fact]
    public void ATeammatesRocketIsNotDodgedWithFriendlyFireOff()
        => Assert.DoesNotContain(Flight(Nimble(), from: 1500f, decides: 10, team: TFField.Red, owner: 4), r => r.Why.Length != 0);

    [Fact]
    public void ATeammatesRocketIsDodgedWithFriendlyFireOn()
    {
        TFField f = Nimble();
        f.Memory.Apply(AiEvent.FriendlyFireSet(f.Bot, new Tick(1), true));
        Assert.Contains(Flight(f, from: 1500f, decides: 10, team: TFField.Red, owner: 4), r => r.Why == TFDecider.DodgeVerdict);
    }

    /// <summary>A rocket whose path passes wider than its blast and the bot's own width is left alone; one 60 u nearer is dodged. The blast is the mod's.</summary>
    [Theory]
    [InlineData(40f, false)]
    [InlineData(-60f, true)]
    public void OnlyARocketWhosePathComesWithinItsBlastIsDodged(float pastTheBlast, bool dodged)
    {
        float aside = TFKnowledge.BlastRadius(TFThingType.Rocket) + TFKnowledge.BodyHalfWidth + pastTheBlast;
        Assert.Equal(dodged, Flight(Nimble(), from: 1500f, decides: 10, aside: aside).Any(r => r.Why == TFDecider.DodgeVerdict));
    }

    /// <summary>A pipe lying 80 u from the bot is walked straight away from.</summary>
    [Fact]
    public void APipeLyingByTheBotIsWalkedAwayFrom()
    {
        TFField f = Nimble();
        var pipe = new ThingFacts(901, ThingKind.Projectile, (int)TFThingType.Pipe, Mid + new AiVector(80f, 0f, 4f), default, TFField.Blue);
        (string Why, MoveIntent Move) reflex = default;
        for (int i = 0; i < 4; i++)
            reflex = See(f, [pipe]);
        Assert.Equal((TFDecider.DodgeVerdict, MoveKind.Strafe, -1f), (reflex.Why, reflex.Move.Kind, MathF.Round(reflex.Move.Direction.X, 3)));
    }

    /// <summary>A flare, an arrow or a syringe has no blast to get out of the way of.</summary>
    [Theory]
    [InlineData(TFThingType.Flare)]
    [InlineData(TFThingType.Arrow)]
    [InlineData(TFThingType.Syringe)]
    public void AProjectileWithNoBlastIsNotDodged(TFThingType type)
    {
        TFField f = Nimble();
        AiVector body = Mid + new AiVector(0f, 0f, 41f), at = body + new AiVector(900f, 0f, 0f);
        var thing = new ThingFacts(902, ThingKind.Projectile, (int)type, at, new AiVector(-RocketSpeed, 0f, 0f), TFField.Blue);
        for (int i = 0; i < 5; i++)
            Assert.Equal("", See(f, [thing]).Why);
    }

    /// <summary>Forty bots of each Agility, on the mod's curves, each shot at once: few of the clumsy get out of the way and most of the nimble.</summary>
    [Fact]
    public void AClumsyBotDodgesFewRocketsAndANimbleOneMost()
    {
        int Dodgers(int agility) => Enumerable.Range(0, 40).Count(seed =>
        {
            var f = new TFField(seed: (ulong)(100 + seed), profile: TFSkillCurvesTests.Profile("agility", agility), curves: TFSkillCurvesTests.ShippedCurves());
            return Flight(f, from: 1500f, decides: 14).Any(r => r.Why == TFDecider.DodgeVerdict);
        });
        (int clumsy, int nimble) = (Dodgers(0), Dodgers(10));
        Assert.True(clumsy <= 8 && nimble >= 32, $"{clumsy} of 40 dodged at Agility 0, {nimble} at 10");
    }

    /// <summary>Of two bots that will both dodge, on the mod's curves, the clumsy one starts later.</summary>
    [Fact]
    public void AClumsyBotsDodgeComesLater()
    {
        int FirstDodge(int agility)
        {
            var f = new TFField(profile: TFSkillCurvesTests.Profile("agility", agility), curves: TFSkillCurvesTests.ShippedCurves(), tune: p => p with { DodgeChance = 1f });
            return Flight(f, from: 1500f, decides: 14).FindIndex(r => r.Why == TFDecider.DodgeVerdict);
        }

        (int clumsy, int nimble) = (FirstDodge(0), FirstDodge(10));
        Assert.True(nimble >= 0 && clumsy > nimble + 2, $"the first dodge at decide {clumsy} at Agility 0, {nimble} at 10");
    }

    /// <summary>A sentry first seen from inside its range, with nowhere out of range the bot has stood: straight away from it.</summary>
    [Fact]
    public void InSightAndInRangeTheBotBacksOutOfASentrysRange()
    {
        (string why, MoveIntent move) = See(new TFField(), [Sentry(Mid + new AiVector(TFKnowledge.SentryRange - 300f, 0f, 0f))]);
        Assert.Equal((TFDecider.SentryVerdict, MoveKind.Strafe, -1f), (why, move.Kind, MathF.Round(move.Direction.X, 3)));
    }

    /// <summary>Walking toward a sentry in sight: the first decide inside its range (and the margin kept past it), the bot turns back for where it stood at the last decide outside.</summary>
    [Fact]
    public void InRangeTheBotGoesBackToWhereItLastStoodOutOfRange()
    {
        var f = new TFField();
        AiVector sentry = Mid + new AiVector(TFKnowledge.SentryRange + TFDecider.SentryMargin + 60f, 0f, 0f);
        Assert.Equal("", See(f, [Sentry(sentry)]).Why);
        (string why, MoveIntent move) = See(f, [Sentry(sentry)], self: TFField.Self(Mid + new AiVector(120f, 0f, 0f)));
        Assert.Equal((TFDecider.SentryVerdict, MoveKind.Follow, Mid), (why, move.Kind, move.Corner0));
    }

    [Fact]
    public void ASentryFurtherOffThanItsRangeIsNotBackedAwayFrom()
    {
        var f = new TFField();
        Assert.Equal("", See(f, [Sentry(Mid + new AiVector(TFKnowledge.SentryRange + TFDecider.SentryMargin + 50f, 0f, 0f))]).Why);
        Assert.Equal(1, f.Decider.Sentries.Length);
    }

    [Fact]
    public void TheTeamsOwnSentryIsNeitherBackedAwayFromNorRemembered()
    {
        var f = new TFField();
        Assert.Equal("", See(f, [Sentry(Mid + new AiVector(300f, 0f, 0f), team: TFField.Red)]).Why);
        Assert.Equal(0, f.Decider.Sentries.Length);
    }

    [Fact]
    public void ASentryIsRememberedWhereItStoodAfterItLeavesSight()
    {
        var f = new TFField();
        AiVector at = Mid + new AiVector(700f, 100f, 0f);
        See(f, [Sentry(at, level: 2)]);
        for (int i = 0; i < 50; i++)
            See(f, []);
        SeenSentry seen = Assert.Single(f.Decider.Sentries.ToArray());
        Assert.Equal((700, at, 2), (seen.Id, seen.At, seen.Level));
    }

    [Fact]
    public void ASentrySeenAgainIsTheSameOneAndNotNew()
    {
        var f = new TFField();
        AiVector at = Mid + new AiVector(700f, 0f, 0f);
        See(f, [Sentry(at)]);
        Assert.True(f.Decider.SawNewSentry);
        See(f, [Sentry(at)]);
        Assert.Equal((false, 1), (f.Decider.SawNewSentry, f.Decider.Sentries.Length));
    }

    /// <summary>The ninth sentry takes the place of the one seen longest ago.</summary>
    [Fact]
    public void TheMemoryKeepsTheSentriesSeenLast()
    {
        var f = new TFField();
        for (int i = 0; i <= TFDecider.RememberedSentries; i++)
            See(f, [Sentry(TFField.BlueFlag + new AiVector(-40f * i, 0f, 0f), id: 700 + i)]);
        int[] ids = [.. f.Decider.Sentries.ToArray().Select(s => s.Id)];
        Assert.Equal(Enumerable.Range(701, TFDecider.RememberedSentries), ids);
    }

    private static readonly AiVector ByTheirFlag = TFField.BlueFlag + new AiVector(-200f, 0f, 0f);

    [Fact]
    public void TheFlagInsideARememberedSentrysRangeIsWorthAFractionForAWhile()
    {
        var f = new TFField();
        See(f, [Sentry(ByTheirFlag)], self: TFField.Self(ByTheirFlag + new AiVector(-(TFKnowledge.SentryRange + 300f), 0f, 0f)));
        Assert.Equal(0.8f * TFDecider.SentryShy, f.Decider.Weigh(TFDecider.Fetch, 0.8f));
        for (int i = 0; i < (int)(TFDecider.SentryShySeconds / Decide) + 2; i++)
            See(f, []);
        Assert.Equal(0.8f, f.Decider.Weigh(TFDecider.Fetch, 0.8f));
        Assert.Equal(1, f.Decider.Sentries.Length);
    }

    /// <summary>A sentry elsewhere takes nothing off the flag, nor one by the flag off a branch that goes elsewhere.</summary>
    [Fact]
    public void ASentryWeighsOnlyOnWhatLiesInsideItsRange()
    {
        var elsewhere = new TFField();
        See(elsewhere, [Sentry(Mid + new AiVector(0f, 200f, 0f))], self: TFField.Self(Mid + new AiVector(-1400f, 0f, 0f)));
        var byFlag = new TFField();
        See(byFlag, [Sentry(ByTheirFlag)], self: TFField.Self(ByTheirFlag + new AiVector(-(TFKnowledge.SentryRange + 300f), 0f, 0f)));
        Assert.Equal((0.8f, 0.5f), (elsewhere.Decider.Weigh(TFDecider.Fetch, 0.8f), byFlag.Decider.Weigh(TFDecider.Defend, 0.5f)));
    }

    [Fact]
    public void ANewRoundsSetupForgetsTheSentries()
    {
        var f = new TFField();
        See(f, [Sentry(ByTheirFlag)]);
        Assert.Equal(1, f.Decider.Sentries.Length);
        var setup = new ObjectiveKnowledge([.. TFField.Ctf().All.Select(o => o with { Setup = true })]);
        See(f, [], hud: setup);
        Assert.Equal(0, f.Decider.Sentries.Length);
    }

    /// <summary>The reflex is the decide's move: the intent a dodging bot hands over steps across the rocket's path, and its doing record says why.</summary>
    [Fact]
    public void TheDodgeIsTheDecidesMove()
    {
        TFField f = Nimble();
        AiVector body = Mid + new AiVector(0f, 0f, 41f);
        IntentCommand i = default;
        for (int n = 0; n < 6; n++)
            i = f.StepSeeing(TFField.Self(Mid), TFField.Ctf(), [Rocket(body + new AiVector(1500f - RocketSpeed * Decide * n, 0f, 0f), body)]);
        Assert.Equal((MoveKind.Strafe, TFDecider.DodgeVerdict, true), (i.Move.Kind, f.Decider.Doing.Verdict, f.Decider.Dodges > 0));
        Assert.True(MathF.Abs(i.Move.Direction.Y) > 0.99f, $"stepped {i.Move.Direction}");
    }

    /// <summary>Six decides of a rocket flying at a nimble bot whose HUD shows <paramref name="flags"/> beside alive and grounded: the last intent, and the bot.</summary>
    private static (IntentCommand Last, TFField Bot) FlownAt(SelfFlags flags)
    {
        TFField f = Nimble();
        AiVector body = Mid + new AiVector(0f, 0f, 41f);
        IntentCommand i = default;
        for (int n = 0; n < 6; n++)
            i = f.StepSeeing(TFField.Self(Mid, extra: flags), TFField.Ctf(), [Rocket(body + new AiVector(1500f - RocketSpeed * Decide * n, 0f, 0f), body)]);
        return (i, f);
    }

    /// <summary>
    /// A bot the game holds still (TF2's pre-round freeze, <see cref="SelfFlags.Frozen"/> on its HUD) dodges
    /// nothing: the rocket a free bot steps out of leaves a frozen one's move as the tree chose it, with no
    /// reflex worked out and no dodge counted.
    /// </summary>
    [Fact]
    public void AFrozenBotDodgesNothing()
    {
        (IntentCommand free, TFField freeBot) = FlownAt(SelfFlags.None);
        (IntentCommand held, TFField heldBot) = FlownAt(SelfFlags.Frozen);
        Assert.Equal((MoveKind.Strafe, true, TFDecider.DodgeVerdict), (free.Move.Kind, freeBot.Decider.Dodges > 0, freeBot.Decider.Reflex.Why));
        Assert.Equal((MoveKind.Follow, TFField.BlueFlag, 0, ""), (held.Move.Kind, held.Move.Corner0, heldBot.Decider.Dodges, heldBot.Decider.Reflex.Why));
    }

    /// <summary>Nor does a frozen bot back out of a sentry's range: the free bot's retreat is not the frozen one's move.</summary>
    [Fact]
    public void AFrozenBotBacksAwayFromNoSentry()
    {
        ThingFacts[] sentry = [Sentry(Mid + new AiVector(TFKnowledge.SentryRange - 300f, 0f, 0f))];
        TFField free = new(), held = new();
        IntentCommand walked = free.StepSeeing(TFField.Self(Mid), TFField.Ctf(), sentry);
        IntentCommand stood = held.StepSeeing(TFField.Self(Mid, extra: SelfFlags.Frozen), TFField.Ctf(), sentry);
        Assert.Equal((MoveKind.Strafe, 1, TFDecider.SentryVerdict), (walked.Move.Kind, free.Decider.SentryRetreats, free.Decider.Doing.Verdict));
        Assert.Equal((MoveKind.Follow, 0, 0, ""), (stood.Move.Kind, held.Decider.SentryRetreats, held.Decider.SentryHolds, held.Decider.Reflex.Why));
    }

    /// <summary>The control: with no rocket the same bot is on its way to the enemy's flag.</summary>
    [Fact]
    public void WithNothingComingTheBotFollowsItsWay()
    {
        IntentCommand i = Nimble().StepSeeing(TFField.Self(Mid), TFField.Ctf(), []);
        Assert.Equal((MoveKind.Follow, TFField.BlueFlag), (i.Move.Kind, i.Move.Corner0));
    }

    /// <summary>Where a bot stands 100 u past the edge it keeps from a sentry by the enemy's flag: far enough not to be backed away, near enough that its next steps would be in.</summary>
    private static readonly AiVector AtTheEdge = ByTheirFlag + new AiVector(-(TFKnowledge.SentryRange + TFDecider.SentryMargin + 100f), 0f, 0f);

    /// <summary>An attacker whose way to the enemy's flag leads into the range of a sentry it sees stops at the edge and waits there.</summary>
    [Fact]
    public void TheBotStopsAtTheEdgeRatherThanWalkIntoAWatchedSentrysRange()
    {
        var f = new TFField();
        IntentCommand i = f.StepSeeing(TFField.Self(AtTheEdge), TFField.Ctf(), [Sentry(ByTheirFlag)]);
        Assert.Equal((TFDecider.Fetch, MoveKind.Hold, TFDecider.SentryEdgeVerdict, 1), (f.Branch, i.Move.Kind, f.Decider.Doing.Verdict, f.Decider.SentryHolds));
    }

    /// <summary>A defender at the same place, whose way home leads away from the sentry, walks it.</summary>
    [Fact]
    public void AWayThatLeadsAwayFromTheSentryIsWalked()
    {
        var f = TFField.Defender();
        IntentCommand i = f.StepSeeing(TFField.Self(AtTheEdge), TFField.Ctf(), [Sentry(ByTheirFlag)]);
        Assert.Equal((TFDecider.Defend, MoveKind.Follow, 0), (f.Branch, i.Move.Kind, f.Decider.SentryHolds));
    }

    /// <summary>Forty-five seconds after it last saw the sentry the bot walks on, to look again.</summary>
    [Fact]
    public void AfterTheShyTimeTheBotWalksOnToLookAgain()
    {
        var f = new TFField();
        f.StepSeeing(TFField.Self(AtTheEdge), TFField.Ctf(), [Sentry(ByTheirFlag)]);
        IntentCommand i = default;
        for (int n = 0; n < (int)(TFDecider.SentryShySeconds / Decide) + 2; n++)
            i = f.StepSeeing(TFField.Self(AtTheEdge), TFField.Ctf(), []);
        Assert.Equal((MoveKind.Follow, TFField.BlueFlag), (i.Move.Kind, i.Move.Corner0));
    }

    /// <summary>A sentry the bot has only heard called weighs on its scores and stops it nowhere: the caller's place is not the sentry's.</summary>
    [Fact]
    public void ASentryOnlyHeardCalledStopsNobody()
    {
        var f = new TFField();
        f.StepSeeing(TFField.Self(AtTheEdge), TFField.Ctf(), []);
        HearSentryAhead(f, ByTheirFlag);
        IntentCommand i = f.StepSeeing(TFField.Self(AtTheEdge), TFField.Ctf(), []);
        Assert.Equal((MoveKind.Follow, 1, 0), (i.Move.Kind, f.Decider.Sentries.Length, f.Decider.SentryHolds));
    }

    /// <summary>The bot backs off an invulnerable enemy and keeps shooting at it: the reflex takes the move, the hands keep the trigger.</summary>
    [Fact]
    public void BackingOffAnInvulnerableEnemyTheBotKeepsFiring()
    {
        var f = new TFField();
        IntentCommand i = f.StepSeeing(TFField.Self(Mid), TFField.Ctf(), [], null, Drawn(Mid + new AiVector(500f, 0f, 0f), ActorConditions.Invulnerable));
        Assert.Equal((MoveKind.Strafe, -1f, ActionKind.Fire, TFDecider.InvulnerableVerdict),
                     (i.Move.Kind, MathF.Round(i.Move.Direction.X, 3), i.Action.Kind, f.Decider.Doing.Verdict));
    }

    private static readonly ActorId Friend = new(4, 0x4000_0002);

    /// <summary>A teammate's "Sentry ahead!" from <paramref name="at"/>, as the voice tap tells it.</summary>
    private static void HearSentryAhead(TFField f, AiVector at)
    {
        f.Memory.Apply(AiEvent.ActorTeam(f.Bot, new Tick(f.Tick + 1), Friend, TFField.Red, 0));
        f.Memory.Apply(AiEvent.Callout(f.Bot, new Tick(f.Tick + 1), Friend, new SayIntent(CalloutKind.Hazard, at, 0, 0, 0, 0, false)));
    }

    /// <summary>The voice menu, in (G.2.4): a teammate's "Sentry ahead!" marks where they called from as a sighting does, and the flag inside its range is worth a fraction.</summary>
    [Fact]
    public void ATeammatesSentryCallMarksThePlaceAsASightingDoes()
    {
        var f = new TFField();
        See(f, []);
        HearSentryAhead(f, ByTheirFlag);
        See(f, []);
        SeenSentry called = Assert.Single(f.Decider.Sentries.ToArray());
        Assert.True(called.Id < 0 && called.At == ByTheirFlag, $"remembered {called}");
        Assert.Equal(0.8f * TFDecider.SentryShy, f.Decider.Weigh(TFDecider.Fetch, 0.8f));
    }

    /// <summary>The sentry a teammate called, once seen, is the one remembered (its own id, where it stands) and no news of the bot's own.</summary>
    [Fact]
    public void ASentryATeammateCalledIsNoNewsWhenTheBotSeesIt()
    {
        var f = new TFField();
        See(f, []);
        HearSentryAhead(f, ByTheirFlag + new AiVector(-700f, 0f, 0f));
        See(f, []);
        See(f, [Sentry(ByTheirFlag)]);
        SeenSentry seen = Assert.Single(f.Decider.Sentries.ToArray());
        Assert.Equal((700, ByTheirFlag, false), (seen.Id, seen.At, f.Decider.SawNewSentry));
    }

    /// <summary>A call about a sentry the bot already remembers there adds none: it is the same one, seen to be there still.</summary>
    [Fact]
    public void ACallAboutASentryTheBotRemembersAddsNone()
    {
        var f = new TFField();
        See(f, [Sentry(ByTheirFlag)]);
        HearSentryAhead(f, ByTheirFlag + new AiVector(-700f, 0f, 0f));
        See(f, []);
        Assert.Equal(700, Assert.Single(f.Decider.Sentries.ToArray()).Id);
    }

    private static ActorFacts Drawn(AiVector at, ActorConditions conditions, int team = TFField.Blue)
        => new(Enemy, at, AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, team, Conditions: conditions);

    [Fact]
    public void AnInvulnerableEnemyInSightIsBackedAwayFrom()
    {
        (string why, MoveIntent move) = See(new TFField(), [], sighted: Drawn(Mid + new AiVector(500f, 0f, 0f), ActorConditions.Invulnerable));
        Assert.Equal((TFDecider.InvulnerableVerdict, MoveKind.Strafe, -1f), (why, move.Kind, MathF.Round(move.Direction.X, 3)));
    }

    [Theory]
    [InlineData(500f, ActorConditions.None, TFField.Blue)]
    [InlineData(500f, ActorConditions.Burning, TFField.Blue)]
    [InlineData(500f, ActorConditions.Invulnerable, TFField.Red)]
    [InlineData(TFDecider.InvulnerableBackOff + 100f, ActorConditions.Invulnerable, TFField.Blue)]
    public void OnlyAnInvulnerableEnemyNearIsBackedAwayFrom(float distance, ActorConditions conditions, int team)
        => Assert.Equal("", See(new TFField(), [], sighted: Drawn(Mid + new AiVector(distance, 0f, 0f), conditions, team)).Why);

    /// <summary>With the flag in its hands the bot runs home past a sentry and an invulnerable enemy; a blast it still gets out of the way of.</summary>
    [Fact]
    public void TheCarrierBacksAwayFromNothingButABlast()
    {
        TFField f = Nimble();
        ObjectiveKnowledge carrying = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot);
        Assert.Equal("", See(f, [Sentry(Mid + new AiVector(500f, 0f, 0f))], carrying, sighted: Drawn(Mid + new AiVector(400f, 0f, 0f), ActorConditions.Invulnerable)).Why);
        AiVector body = Mid + new AiVector(0f, 0f, 41f);
        string why = "";
        for (int i = 0; i < 4; i++)
            why = See(f, [Rocket(body + new AiVector(900f - 99f * i, 0f, 0f), body)], carrying).Why;
        Assert.Equal(TFDecider.DodgeVerdict, why);
    }

    [Fact]
    public void ABlastComesBeforeASentry()
    {
        TFField f = Nimble();
        AiVector body = Mid + new AiVector(0f, 0f, 41f);
        string why = "";
        for (int i = 0; i < 4; i++)
            why = See(f, [Sentry(Mid + new AiVector(500f, 0f, 0f)), Rocket(body + new AiVector(900f - 99f * i, 0f, 0f), body)]).Why;
        Assert.Equal(TFDecider.DodgeVerdict, why);
    }
}
