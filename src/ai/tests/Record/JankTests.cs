using SourceSharp.Ai.Replay;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay jank</c>'s parts (plan_hl2mp_ai §3.4.5) over hand-built
/// recordings with a known answer: each part, the case it must count and the
/// case that looks like it and must not.
/// </summary>
public sealed class JankTests
{
    private const float Tick = AiMetrics.TickSeconds;
    private static readonly ActorId Bot = new(3, 7), Other = new(4, 8), Person = new(2, 0x4000_0123);

    // A tree of two branches: Fight (node 1) over "hold" (2) and "chase" (3), Roam (4) over "roam" (5).
    private static readonly AiDecisionTree Tree = new(["root", "Fight", "hold", "chase", "Roam", "roam"], [0, 1, 1, 1, 4, 4]);
    private const int Hold = 2, Chase = 3, Roam = 5;

    private const int Pistol = 1;   // reach 1024, cadence 0.3 s
    private const int Shotgun = 2;  // reach 400

    private sealed class Rec
    {
        private readonly MemoryStream _bytes = new();
        public readonly AiRecorder R;

        public Rec(ActorId? bot = null, bool tree = true)
        {
            R = new AiRecorder(_bytes, Recordings.Header);
            var weapons = new WeaponKnowledge([new WeaponFacts(Pistol, "weapon_pistol", 18, 0.3f, 1.5f, true, false, 0f),
                                               new WeaponFacts(Shotgun, "weapon_shotgun", 6, 0.75f, 1.5f, true, true, 0f)]);
            R.World(new AiRecordedWorld(new MapKnowledge("ss_test", [], [], weapons), Tick, "", 0));
            if (bot is { } b)
                R.BotAdded(b, 1, []);
            if (tree)
                R.DecisionTree(Tree.Names, Tree.Branches);
        }

        /// <summary>One tick of an actor: its frame at x and its command.</summary>
        public void At(ActorId a, int t, float x, float forward = 0, float side = 0, float yaw = 0, int buttons = 0, float y = 0)
        {
            var at = new AiVector(x, y, 0);
            R.Frame(new AiActorFrame(a, new Tick(t), at, at + new AiVector(0, 0, 64), default));
            R.UserCmd(new AiUserCmd(a, new Tick(t), t, t, new AiAngles(0, yaw, 0), forward, side, 0, buttons, 0, 0, 0, 0, 0, 0));
        }

        public void Decide(int t, int node)
            => R.Intent(new IntentCommand(Bot, new Tick(t), t, new Tick(t + IntentCommand.LifetimeTicks), default, default, default, new DecisionReason(node, 0, 0, 0)));

        public void Holding(int t, ActorId bot, int weapon)
        {
            var snap = new SnapshotRing(1, 8, 4).BeginWrite(new Tick(t));
            snap.Add(bot, new SelfState(default, default, default, 100, 0, weapon, 1, 1, SelfFlags.Alive));
            R.Snapshot(snap);
        }

        public void Sees(ActorId a, int t, ActorId other, bool observed = false)
        {
            AiEvent e = AiEvent.SightGained(a, new Tick(t), other, default, default, 1f);
            if (observed)
                R.Observed(e);
            else
                R.Event(e);
        }

        public JankReport Report(AiDecisionTree? fallback = null)
            => AiJank.Read(new MemoryStream(_bytes.ToArray()), JankTool.WeaponRange, fallback);

        public JankActor Of(ActorId a) => Report().Actors.Single(x => x.Actor == a);
    }

    private static double Seconds(int ticks) => ticks * (double)Tick;

    // ---- stuck -------------------------------------------------------------

    /// <summary>Standing, then holding forward on the spot for exactly 200 ticks, then standing: exactly those.</summary>
    [Fact]
    public void AMoveKeyHeldWithNoProgressIsStuckForExactlyThatLong()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 400; t++)
            r.At(Bot, t, 100, forward: t is >= 100 and < 300 ? 450 : 0);
        JankActor a = r.Of(Bot);
        Assert.Equal(Seconds(200), a.StuckSeconds, 6);
        Assert.Equal(Seconds(400), a.AliveSeconds, 6);
    }

    /// <summary>The positive arm's twin: a walk's every tick holds the key and gets somewhere.</summary>
    [Fact]
    public void AWalkIsNotStuck()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 400; t++)
            r.At(Bot, t, t * 2.85f, forward: 450);
        JankActor a = r.Of(Bot);
        Assert.Equal(0, a.StuckSeconds);
        Assert.Equal(0, a.WallBumpSeconds);
        Assert.Equal(Seconds(400), a.AliveSeconds, 6);
    }

    /// <summary>Less than the window is a stumble.</summary>
    [Fact]
    public void AHoldShorterThanTheWindowIsNotStuck()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 200; t++)
            r.At(Bot, t, 100, forward: t is >= 50 and < 50 + AiJank.StuckWindowTicks - 1 ? 450 : 0);
        Assert.Equal(0, r.Of(Bot).StuckSeconds);
    }

    /// <summary>Under 32 units a second is stuck though the body creeps, which a wall bump's speed cannot see.</summary>
    [Fact]
    public void ACreepIsStuckAndNoWallBump()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 200; t++)
            r.At(Bot, t, t * 0.4f, forward: 450);   // 26.7 u/s, 26.4 u a window
        JankActor a = r.Of(Bot);
        Assert.Equal(Seconds(200), a.StuckSeconds, 6);
        Assert.Equal(0, a.WallBumpSeconds);
    }

    private static Rec Jiggle(bool opponentSeen)
    {
        var r = new Rec(Bot);
        if (opponentSeen)
            r.Sees(Bot, 0, Other);
        for (int t = 0; t < 300; t++)
        {
            r.At(Bot, t, (t % 40 < 20 ? t % 40 : 40 - t % 40) * 1f, side: t % 40 < 20 ? 450 : -450);
            r.At(Other, t, 2000);
        }

        return r;
    }

    /// <summary>Strafing left and right in a fight gets nowhere on purpose; the same keys with nobody in sight are stuck.</summary>
    [Fact]
    public void ADodgeInAFightIsNotStuckAndTheSameKeysAloneAre()
    {
        Assert.Equal(0, Jiggle(opponentSeen: true).Of(Bot).StuckSeconds);
        Assert.True(Jiggle(opponentSeen: false).Of(Bot).StuckSeconds > 3, "the same keys with nobody in sight");
    }

    /// <summary>Forty ticks, a death, forty ticks at the same spot: no one window ever held the key throughout.</summary>
    [Fact]
    public void ADeathEndsAStuckWindow()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 100; t++)
        {
            if (t is < 40 or >= 60)
                r.At(Bot, t, 100, forward: 450);
        }

        Assert.Equal(0, r.Of(Bot).StuckSeconds);
    }

    /// <summary>The numbers the definitions are written in, so a fact relative to a constant cannot follow it.</summary>
    [Fact]
    public void TheThresholdsAreTheStatedOnes()
    {
        Assert.Equal(1.0, Seconds(AiJank.StuckWindowTicks), 1);
        Assert.Equal(32f, AiJank.StuckProgress);
        Assert.True(Seconds(AiJank.WallMinTicks) > 0.5 && Seconds(AiJank.WallMinTicks - 1) <= 0.5);
        Assert.Equal(20f, AiJank.WallSpeed);
        Assert.Equal(1.0, Seconds(AiJank.FlipTicks), 1);
        Assert.Equal(400f, AiJank.CloseRange);
        Assert.Equal(1.0, Seconds(AiJank.IdleTicks), 1);
    }

    // ---- wall bump ---------------------------------------------------------

    /// <summary>A walk of a unit a tick with the key held throughout, the body stopped dead for <paramref name="stalled"/> ticks from tick 100.</summary>
    private static Rec Stall(int stalled)
    {
        var r = new Rec(Bot);
        float x = 0;
        for (int t = 0; t < 300; t++)
        {
            if (t is < 100 || t >= 100 + stalled)
                x += 1f;
            r.At(Bot, t, x, forward: 450);
        }

        return r;
    }

    [Fact]
    public void AKeyHeldAtNoSpeedForOverHalfASecondIsAWallBumpWhole()
        => Assert.Equal(Seconds(100), Stall(100).Of(Bot).WallBumpSeconds, 6);

    [Fact]
    public void ABumpOfUnderHalfASecondIsNoWallBump()
    {
        Assert.Equal(0, Stall(AiJank.WallMinTicks - 1).Of(Bot).WallBumpSeconds);
        Assert.Equal(Seconds(AiJank.WallMinTicks), Stall(AiJank.WallMinTicks).Of(Bot).WallBumpSeconds, 6);
    }

    [Fact]
    public void ASlowWalkOverTheSpeedIsNoWallBump()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 200; t++)
            r.At(Bot, t, t * 0.45f, forward: 450);   // 30 u/s
        Assert.Equal(0, r.Of(Bot).WallBumpSeconds);
    }

    // ---- decision flips ----------------------------------------------------

    private static JankActor Decisions(params (int Tick, int Node)[] decisions)
    {
        var r = new Rec(Bot);
        for (int t = 0; t <= decisions[^1].Tick + 1; t++)
            r.At(Bot, t, t * 2.85f, forward: 450);
        foreach ((int t, int n) in decisions)
            r.Decide(t, n);
        return r.Of(Bot);
    }

    [Fact]
    public void ADecisionThatGoesBackWithinASecondIsAFlip()
    {
        JankActor a = Decisions((0, Hold), (100, Roam), (120, Hold));
        Assert.Equal((1, 1), (a.BranchFlips, a.LeafFlips));
    }

    [Fact]
    public void ADecisionThatMovesOnIsNoFlip()
    {
        JankActor a = Decisions((0, Hold), (100, Roam), (120, 0), (130, Roam + 1));
        Assert.Equal((0, 0), (a.BranchFlips, a.LeafFlips));
    }

    [Fact]
    public void ADecisionThatGoesBackAfterASecondIsNoFlip()
    {
        JankActor at = Decisions((0, Hold), (100, Roam), (100 + AiJank.FlipTicks, Hold));
        JankActor past = Decisions((0, Hold), (100, Roam), (101 + AiJank.FlipTicks, Hold));
        Assert.Equal((1, 1), (at.BranchFlips, at.LeafFlips));
        Assert.Equal((0, 0), (past.BranchFlips, past.LeafFlips));
    }

    /// <summary>Two leaves of one branch flip the leaf and not the branch.</summary>
    [Fact]
    public void ALeafFlipInsideABranchIsNoBranchFlip()
    {
        JankActor a = Decisions((0, Hold), (100, Chase), (120, Hold));
        Assert.Equal((0, 1), (a.BranchFlips, a.LeafFlips));
    }

    /// <summary>The same decisions either side of a death are no flip: dying is not changing its mind.</summary>
    [Fact]
    public void ADeathEndsTheSequence()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 200; t++)
        {
            if (t is < 105 or > 115)
                r.At(Bot, t, t * 2.85f, forward: 450);
        }

        r.Decide(0, Hold);
        r.Decide(100, Roam);
        r.Decide(110, 0);       // dead: no frame near it
        r.Decide(120, Hold);
        JankActor a = r.Of(Bot);
        Assert.Equal((0, 0), (a.BranchFlips, a.LeafFlips));
    }

    // ---- in sight, not firing ----------------------------------------------

    private static Rec Duel(float range, int weapon, Action<Rec, int>? each = null, int node = Hold)
    {
        var r = new Rec(Bot);
        r.Holding(0, Bot, weapon);
        r.Decide(0, node);
        r.Sees(Bot, 10, Other);
        for (int t = 0; t < 110; t++)
        {
            each?.Invoke(r, t);
            r.At(Bot, t, 0, yaw: t);
            r.At(Other, t, range, yaw: t);
        }

        return r;
    }

    [Fact]
    public void AnOpponentSeenCloseWithNoShotCountsUnderItsLeaf()
    {
        JankReport report = Duel(300, Pistol).Report();
        JankActor a = report.Actors.Single(x => x.Actor == Bot);
        Assert.Equal(new JankShare(100, 100), a.Close);
        Assert.Equal(default, a.Far);
        JankLeaf leaf = Assert.Single(report.Leaves);
        Assert.Equal(("hold", new JankShare(100, 100)), (leaf.Leaf, leaf.Close));
    }

    /// <summary>From the eye (z 64) to the body's middle (z 32): 398 along is 399.3 away, 399 along 400.3.</summary>
    [Fact]
    public void TheSplitIsAt400Units()
    {
        JankActor near = Duel(398, Pistol).Of(Bot);
        JankActor far = Duel(399, Pistol).Of(Bot);
        Assert.Equal((100, 0), (near.Close.Ticks, near.Far.Ticks));
        Assert.Equal((0, 100), (far.Close.Ticks, far.Far.Ticks));
    }

    /// <summary>A shotgun does not reach 700 units and a pistol does: out of reach is no fault of the bot's.</summary>
    [Fact]
    public void AnOpponentOutOfTheHeldWeaponsReachDoesNotCount()
    {
        Assert.Equal(100, Duel(700, Pistol).Of(Bot).Far.Ticks);
        Assert.Equal(0, Duel(700, Shotgun).Of(Bot).Far.Ticks);
    }

    /// <summary>A shot at tick 50 of a 0.3 s weapon (20 ticks) makes ticks 50..70 firing: the rest are not.</summary>
    [Fact]
    public void AShotCoversTheWeaponsCadence()
    {
        JankActor a = Duel(300, Pistol, (r, t) =>
        {
            if (t == 50)
                r.R.Observed(AiEvent.ShotFired(Bot, new Tick(50), Pistol, 1));
        }).Of(Bot);
        Assert.Equal(new JankShare(100 - 21, 100), a.Close);
    }

    [Fact]
    public void HoldingAttackIsFiring()
    {
        var r = new Rec(Bot);
        r.Holding(0, Bot, Pistol);
        r.Sees(Bot, 0, Other);
        for (int t = 0; t < 100; t++)
        {
            r.At(Bot, t, 0, buttons: 1);
            r.At(Other, t, 300);
        }

        Assert.Equal(new JankShare(0, 100), r.Of(Bot).Close);
    }

    /// <summary>People by the same definition: their Observed sight, the weapon they last fired, no leaf.</summary>
    [Fact]
    public void APersonIsMeasuredByTheSameDefinitionWithNoLeaf()
    {
        var r = new Rec();
        r.Sees(Person, 10, Other, observed: true);
        r.R.Observed(AiEvent.ShotFired(Person, new Tick(0), Shotgun, 1));
        for (int t = 0; t < 110; t++)
        {
            r.At(Person, t, 0, yaw: t);
            r.At(Other, t, 700, yaw: t);
        }

        JankReport report = r.Report();
        JankActor person = report.Actors.Single(a => a.Actor == Person);
        Assert.False(person.Bot);
        Assert.Equal(0, person.Far.Ticks);   // the shotgun they last fired does not reach
        Assert.Empty(report.Leaves);
    }

    [Fact]
    public void ASightLostEndsTheCount()
    {
        JankActor a = Duel(300, Pistol, (r, t) =>
        {
            if (t == 60)
                r.R.Event(AiEvent.SightLost(Bot, new Tick(60), Other, default, default));
        }).Of(Bot);
        Assert.Equal(50, a.Close.Ticks);
    }

    // ---- idle --------------------------------------------------------------

    [Fact]
    public void NoMoveNoShotNoLookIsIdleForExactlyThatLong()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 400; t++)
            r.At(Bot, t, t < 100 ? t * 2.85f : 99 * 2.85f + (t >= 300 ? (t - 299) * 2.85f : 0), forward: t is >= 100 and < 300 ? 0 : 450);
        // The first still tick is tick 101 (the frame before it moved: joined, still); the run is 100..299.
        Assert.Equal(Seconds(200), r.Of(Bot).IdleSeconds, 6);
    }

    /// <summary>A bot standing and shooting is doing something, between its shots too.</summary>
    [Fact]
    public void StandingAndShootingIsNotIdle()
    {
        var r = new Rec(Bot);
        r.Holding(0, Bot, Pistol);
        for (int t = 0; t < 300; t++)
            r.At(Bot, t, 0, buttons: t % 15 == 0 ? 1 : 0);
        Assert.Equal(0, r.Of(Bot).IdleSeconds);
    }

    [Fact]
    public void AStandShorterThanASecondIsNotIdle()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 200; t++)
            r.At(Bot, t, 0, forward: t is >= 50 and < 50 + AiJank.IdleTicks - 1 ? 0 : 450);
        Assert.Equal(0, r.Of(Bot).IdleSeconds);
    }

    /// <summary>A person or bot looking about is doing something; a bot's drift of a degree is not.</summary>
    [Fact]
    public void ALookingAboutIsNotIdleAndADriftIs()
    {
        var look = new Rec(Bot);
        var drift = new Rec(Bot);
        for (int t = 0; t < 200; t++)
        {
            look.At(Bot, t, 0, yaw: t * 0.5f);
            drift.At(Bot, t, 0, yaw: MathF.Sin(t * 0.1f));
        }

        Assert.Equal(0, look.Of(Bot).IdleSeconds);
        Assert.True(drift.Of(Bot).IdleSeconds >= Seconds(198), $"{drift.Of(Bot).IdleSeconds}");
    }

    // ---- reuse, places, the score, the tree --------------------------------

    /// <summary>The strafe interval and the reversals are the metrics table's, not a copy of its code that can drift.</summary>
    [Fact]
    public void StrafeAndReversalsAreTheMetricsTables()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 400; t++)
            r.At(Bot, t, t * 2.85f, forward: 450, side: t % 40 < 20 ? 450 : -450, yaw: t % 30 < 15 ? t % 30 : 30 - t % 30);
        JankActor a = r.Of(Bot);
        Assert.Equal(20 * 15, AiMetrics.Quantile(a.StrafeIntervalMs, 0.5), 3);
        Assert.True(a.ReversalsPerMinute > 100, $"{a.ReversalsPerMinute}");
    }

    [Fact]
    public void TheWorstPlacesAreOrderedAndNameThePlaceAndTheBots()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 1000; t++)
        {
            bool first = t < 300;
            if (t is >= 300 and < 340)
                continue;   // no frames: dead between the two
            r.At(Bot, t, first ? 1000 : 2000, forward: 450);
            r.At(Other, t, first ? 1000 : 9000, forward: first ? 450 : 0);
        }

        JankReport report = r.Report();
        IReadOnlyList<JankSpot> spots = report.Places[AiJank.Stuck];
        Assert.Equal(2, spots.Count);
        Assert.Equal(2000, spots[0].Position.X, 1);
        Assert.Equal(Seconds(660), spots[0].Seconds, 6);
        Assert.Equal([Bot], spots[0].Actors);
        Assert.Equal(1000, spots[1].Position.X, 1);
        Assert.Equal([Bot, Other], spots[1].Actors);
    }

    [Fact]
    public void OnlyTheTenWorstPlacesAreListed()
    {
        var r = new Rec(Bot);
        int t = 0;
        for (int place = 0; place < 12; place++)
        {
            for (int i = 0; i < 80; i++, t++)
                r.At(Bot, t, place * 1000, forward: 450);
        }

        Assert.Equal(AiJank.TopSpots, r.Report().Places[AiJank.Stuck].Count);
    }

    /// <summary>A bot that stands and one that walks freely must score apart, and neither by accident.</summary>
    [Fact]
    public void ABotThatStandsAndABotThatWalksScoreDifferently()
    {
        var stands = new Rec(Bot);
        var walks = new Rec(Bot);
        for (int t = 0; t < 2000; t++)
        {
            stands.At(Bot, t, 100);
            walks.At(Bot, t, t * 2.85f, forward: 450);
        }

        double stand = JankScore.Of(stands.Of(Bot)), walk = JankScore.Of(walks.Of(Bot));
        Assert.True(stand > 40, $"standing scores {stand}");
        Assert.Equal(0, walk);
    }

    /// <summary>The formula as stated: a part at its "plainly wrong" value is worth its weight.</summary>
    [Fact]
    public void APartAtItsFullValueIsWorthItsWeight()
    {
        JankActor six = new(Bot, true, 60, 6, 0, 0, 0, 0, default, default, [], 0, 1);
        Assert.Equal(25, JankScore.Of(six), 6);
        JankActor flips = new(Bot, true, 60, 0, 0, 0, 10, 20, default, default, [], 0, 1);
        Assert.Equal(20, JankScore.Of(flips), 6);
        JankActor sight = new(Bot, true, 60, 0, 0, 0, 0, 0, new JankShare(1, 2), new JankShare(4, 5), [], 0, 1);
        Assert.Equal(25, JankScore.Of(sight), 6);
        Assert.Contains("stuck 25/6 s/min", JankScore.Formula(), StringComparison.Ordinal);
    }

    /// <summary>The bots' total is theirs: a person's seconds are in the people's line.</summary>
    [Fact]
    public void TheTotalsSumBotsAndPeopleApart()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 200; t++)
        {
            r.At(Bot, t, 100, forward: 450);
            r.At(Other, t, 100, forward: 450);
        }

        for (int t = 0; t < 100; t++)
            r.At(Person, t, 100, forward: 450);
        r.R.BotAdded(Other, 2, []);
        JankReport report = r.Report();
        Assert.Equal(2 * Seconds(200), report.Bots.StuckSeconds, 6);
        Assert.Equal(Seconds(100), report.People.StuckSeconds, 6);
    }

    /// <summary>The recording's tree names its leaves whatever this build's says; a recording with none says whose it used.</summary>
    [Fact]
    public void TheRecordingsTreeWinsAndTheFallbackIsSaid()
    {
        AiDecisionTree wrong = new(["a", "b", "c", "d", "e", "f"], [0, 1, 1, 1, 1, 1]);
        JankReport recorded = Duel(300, Pistol).Report(wrong);
        Assert.Equal("the recording's", recorded.TreeSource);
        Assert.Equal("hold", Assert.Single(recorded.Leaves).Leaf);

        var bare = new Rec(Bot, tree: false);
        bare.Decide(0, 2);
        bare.Sees(Bot, 0, Other);
        for (int t = 0; t < 10; t++)
        {
            bare.At(Bot, t, 0, yaw: t);
            bare.At(Other, t, 300);
        }

        JankReport fallback = bare.Report(wrong);
        Assert.Contains("this build's", fallback.TreeSource, StringComparison.Ordinal);
        Assert.Equal("c", Assert.Single(fallback.Leaves).Leaf);
    }

    /// <summary>A live session writes the decider's tree with each bot, and a replay of it reads past the record.</summary>
    [Fact]
    public void ALiveSessionRecordsTheDecidersTree()
    {
        var map = new MapKnowledge("ss_test", [], [], new WeaponKnowledge([]));
        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            var recorder = new AiRecorder(stream, Recordings.Header);
            var session = new AiSession(new InlineTransport(), recorder, 8, 4)
            {
                World = new MemoryWorld(map, null, Tick, id => id.ToString()),
                Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
                Deciders = setup => new SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider(setup),
            };
            SkillPresets.TryGet("normal", out SkillProfile? p);
            session.AddBot(Bot, 1, [.. p!.Levels.Select(l => (byte)l)]);
            bytes = stream.ToArray();
        }

        (AiRecordType _, byte[] payload) = Assert.Single(Recordings.Records(bytes), x => x.Type == AiRecordType.DecisionTree);
        AiDecisionTree tree = AiRecordFormat.ReadDecisionTree(new BinaryReader(new MemoryStream(payload)));
        Assert.Equal(JankTool.ThisBuildsTree(), tree, TreeEquals.Instance);
        Assert.Equal(0, AiReplayer.Replay(new MemoryStream(bytes), new InlineTransport()).RecordedIntentCount);
    }

    private sealed class TreeEquals : IEqualityComparer<AiDecisionTree>
    {
        public static readonly TreeEquals Instance = new();

        public bool Equals(AiDecisionTree? a, AiDecisionTree? b)
            => a is not null && b is not null && a.Names.SequenceEqual(b.Names) && a.Branches.SequenceEqual(b.Branches);

        public int GetHashCode(AiDecisionTree t) => t.Names.Length;
    }

    /// <summary>The tree's shape is read from the tree: a leaf's branch is the root's child above it.</summary>
    [Theory]
    [InlineData("go heal", "Survive")]
    [InlineData("hold range", "Fight")]
    [InlineData("push", "Fight")]
    [InlineData("hunt", "Hunt")]
    [InlineData("roam", "Roam")]
    public void ALeafsBranchIsTheChildOfTheRootAboveIt(string leaf, string branch)
    {
        AiDecisionTree tree = JankTool.ThisBuildsTree();
        int node = Array.IndexOf(tree.Names, leaf);
        Assert.True(node > 0, leaf);
        Assert.Equal(branch, tree.Name(tree.BranchOf(node)));
    }

    /// <summary>The table prints every actor and the line a gate reads.</summary>
    [Fact]
    public void TheTableStatesTheWeightsAndTheHeadline()
    {
        var r = new Rec(Bot);
        for (int t = 0; t < 200; t++)
            r.At(Bot, t, 100, forward: 450);
        string table = AiJank.Table(r.Report());
        Assert.Contains("score = sum of weight", table, StringComparison.Ordinal);
        Assert.Contains("3#7", table, StringComparison.Ordinal);
        Assert.Contains("jank: bots score ", table, StringComparison.Ordinal);
    }
}
