using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay metrics</c>'s definitions (plan_hl2mp_ai §3.4.4, Phase 5b
/// step 2) on synthetic streams: a known flick yields its known peak, a slow
/// turn is no flick, a known reaction and a known acquisition their known
/// delays, a known strafe its interval; a person and a bot by the same
/// definitions, told apart by the BotAdded record.
/// </summary>
public sealed class MetricsTests
{
    private static readonly ActorId Person = new(2, 0x4000_0123), Bot = new(3, 7);

    private sealed class Stream
    {
        private readonly MemoryStream _bytes = new();
        public readonly AiRecorder Recorder;

        public Stream() => Recorder = new AiRecorder(_bytes, Recordings.Header);

        public void Cmd(ActorId a, int tick, float yaw, float side = 0, int buttons = 0, float pitch = 0)
            => Recorder.UserCmd(new AiUserCmd(a, new Tick(tick), tick, tick, new AiAngles(pitch, yaw, 0), 0, side, 0, buttons, 0, 0, 0, 0, 0, 0));

        public void Frame(ActorId a, int tick, AiVector origin)
            => Recorder.Frame(new AiActorFrame(a, new Tick(tick), origin, origin + new AiVector(0, 0, 64), default));

        public IReadOnlyList<ActorMetrics> Metrics() => AiMetrics.Read(new MemoryStream(_bytes.ToArray()));
    }

    private static ActorMetrics Of(IReadOnlyList<ActorMetrics> all, ActorId a) => all.Single(m => m.Actor == a);

    [Fact]
    public void AKnownFlickYieldsItsKnownPeak()
    {
        var s = new Stream();
        // Still, then 60° in ten ticks peaking at 8° a tick (533 °/s), then still.
        float[] steps = [2, 4, 6, 8, 8, 8, 8, 8, 6, 2];
        float yaw = 0;
        int t = 0;
        for (; t < 20; t++)
            s.Cmd(Person, t, yaw);
        foreach (float step in steps)
            s.Cmd(Person, t++, yaw += step);
        for (int i = 0; i < 20; i++)
            s.Cmd(Person, t++, yaw);

        ActorMetrics m = Of(s.Metrics(), Person);
        double peak = Assert.Single(m.FlickPeaksDegPerSec);
        Assert.Equal(8 / 0.015, peak, 1);
        Assert.Equal(8 / 0.015, AiMetrics.Quantile(m.AngularSpeedDegPerSec, 1), 1);
        Assert.False(m.Bot);
    }

    [Fact]
    public void ASlowTurnIsNoFlick()
    {
        var s = new Stream();
        for (int t = 0; t < 100; t++)
            s.Cmd(Person, t, t * 1.2f);   // 80 °/s for 1.5 s: moving, but no flick
        ActorMetrics m = Of(s.Metrics(), Person);
        Assert.Empty(m.FlickPeaksDegPerSec);
        Assert.Equal(80, AiMetrics.Quantile(m.AngularSpeedDegPerSec, 0.5), 1);
    }

    [Fact]
    public void AYawThatCrossesTheSeamTurnsTheShortWay()
        => Assert.Equal(4, AiMetrics.Delta(new AiAngles(0, 178, 0), new AiAngles(0, -178, 0)).Yaw, 3);

    /// <summary>A target 500 units along +x; the actor's view turns from 90° toward it 4.5° a tick from the gain at tick 100.</summary>
    private static Stream Engagement(ActorId actor, bool bot, Action<Stream>? extra = null)
    {
        var s = new Stream();
        ActorId target = actor == Person ? Bot : Person;
        if (bot)
            s.Recorder.BotAdded(actor, 1, []);
        for (int t = 90; t < 160; t++)
        {
            s.Frame(actor, t, new AiVector(0, 0, 0));
            s.Frame(target, t, new AiVector(500, 0, 0));
            float yaw = t < 100 ? 90 : Math.Max(0, 90 - 4.5f * (t - 100));
            s.Cmd(actor, t, yaw, buttons: t == 130 ? 1 : 0);
        }

        AiEvent gained = AiEvent.SightGained(actor, new Tick(100), target, new AiVector(500, 0, 0), default, 1f);
        if (bot)
            s.Recorder.Event(gained);
        else
            s.Recorder.Observed(gained);
        extra?.Invoke(s);
        return s;
    }

    [Fact]
    public void AKnownAcquisitionYieldsItsKnownTime()
    {
        // Within 2° once 90 - 4.5k <= 2: k = 20, 300 ms.
        ActorMetrics m = Of(Engagement(Person, bot: false).Metrics(), Person);
        Assert.Equal(1, m.SightsGained);
        Assert.Equal(300, Assert.Single(m.TimeToAcquireMs), 3);
    }

    [Fact]
    public void AKnownReactionYieldsItsKnownDelay()
    {
        // The first turn toward the target is the step into tick 101 (300 °/s): 15 ms.
        ActorMetrics m = Of(Engagement(Person, bot: false).Metrics(), Person);
        Assert.Equal(15, Assert.Single(m.ReactionMs), 3);
    }

    [Fact]
    public void AShotBeforeAnyTurnIsTheReaction()
    {
        var s = new Stream();
        for (int t = 90; t < 160; t++)
        {
            s.Frame(Bot, t, new AiVector(0, 0, 0));
            s.Frame(Person, t, new AiVector(500, 0, 0));
            s.Cmd(Bot, t, 0, buttons: t >= 120 ? 1 : 0);
        }

        s.Recorder.BotAdded(Bot, 1, []);
        s.Recorder.Event(AiEvent.SightGained(Bot, new Tick(100), Person, default, default, 1f));
        ActorMetrics m = Of(s.Metrics(), Bot);
        Assert.Equal(300, Assert.Single(m.ReactionMs), 3);
        Assert.Equal(0, Assert.Single(m.TimeToAcquireMs), 3);   // already on target
        Assert.True(m.Bot);
    }

    [Fact]
    public void ABotIsMeasuredByTheSameDefinitions()
    {
        IReadOnlyList<ActorMetrics> all = Engagement(Bot, bot: true).Metrics();
        ActorMetrics m = Of(all, Bot);
        Assert.Equal((true, 300.0, 15.0), (m.Bot, Math.Round(Assert.Single(m.TimeToAcquireMs)), Math.Round(Assert.Single(m.ReactionMs))));
    }

    [Fact]
    public void ASightLostEndsTheAcquisitionWindow()
    {
        ActorMetrics m = Of(Engagement(Person, bot: false,
                               s => s.Recorder.Observed(AiEvent.SightLost(Person, new Tick(110), Bot, default, default))).Metrics(), Person);
        Assert.Empty(m.TimeToAcquireMs);
        Assert.Equal(1, m.SightsGained);
    }

    [Fact]
    public void AKnownStrafeYieldsItsInterval()
    {
        var s = new Stream();
        float[] sides = [450, 0, -450, 450];   // 30 ticks each, a zero between the first two
        int t = 0;
        foreach (float side in sides)
            for (int i = 0; i < 30; i++)
                s.Cmd(Person, t++, 0, side);
        ActorMetrics m = Of(s.Metrics(), Person);
        Assert.Equal(450, Assert.Single(m.StrafeIntervalMs), 3);   // -450 at tick 60, +450 at 90
    }

    [Fact]
    public void YawReversalsAreCountedPerMinute()
    {
        var s = new Stream();
        for (int t = 0; t < 400; t++)
            s.Cmd(Person, t, (t / 20) % 2 == 0 ? t % 20 : 20 - t % 20);   // a sawtooth, a reversal every 20 ticks
        ActorMetrics m = Of(s.Metrics(), Person);
        Assert.InRange(m.ReversalsPerMinute * m.Minutes, 18, 20);
    }

    [Fact]
    public void TheTableHasARowPerActorNamingItsKind()
    {
        Stream s = Engagement(Bot, bot: true);
        s.Cmd(Person, 200, 0);
        s.Cmd(Person, 201, 0);
        string table = AiMetrics.Table(s.Metrics());
        string[] rows = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, rows.Length);   // the header and a row per actor, for the metrics and for the hit rate
        Assert.StartsWith("2#1073742115 ", rows[1], StringComparison.Ordinal);
        Assert.Contains(" human ", rows[1], StringComparison.Ordinal);
        Assert.Contains(" bot ", rows[2], StringComparison.Ordinal);
    }
    /// <summary>
    /// A respawn's view is no turn: the command run in the tick a bot
    /// respawns still carries its dead view (seen live: 4,861 °/s from
    /// (31, 114) to the spawn's (0, 180)), so a step counts only while the
    /// actor had a frame the tick before it as well.
    /// </summary>
    [Fact]
    public void ARespawnsSnapIsNoTurn()
    {
        var s = new Stream();
        for (int t = 0; t < 10; t++)
        {
            s.Frame(Bot, t, new AiVector(0, 0, 0));
            s.Cmd(Bot, t, 114);
        }

        // Dead for ticks 10..19 (no frames), respawned at 20 where its first command still looks at 114.
        for (int t = 10; t < 20; t++)
            s.Cmd(Bot, t, 114);
        s.Cmd(Bot, 20, 114);
        s.Frame(Bot, 20, new AiVector(0, 0, 0));
        for (int t = 21; t < 30; t++)
        {
            s.Frame(Bot, t, new AiVector(0, 0, 0));
            s.Cmd(Bot, t, 180);
        }

        ActorMetrics m = Of(s.Metrics(), Bot);
        Assert.True(m.AngularSpeedDegPerSec.Length >= 15, $"the control: {m.AngularSpeedDegPerSec.Length} living steps counted");
        Assert.Equal(0, AiMetrics.Quantile(m.AngularSpeedDegPerSec, 1));
    }

    [Fact]
    public void ABodyThatJumpedWasTeleportedNotTurned()
    {
        var s = new Stream();
        for (int t = 0; t < 20; t++)
        {
            s.Frame(Person, t, new AiVector(t < 10 ? 0 : 2000, 0, 0));
            s.Cmd(Person, t, t < 10 ? 0 : 90);
        }

        Assert.Equal(0, AiMetrics.Quantile(Of(s.Metrics(), Person).AngularSpeedDegPerSec, 1));
    }

    // ---- hit rate (user ruling 2026-11-02) ----------------------------------

    private const int Smg = 3, Pistol = 2;

    /// <summary>A bot at the origin looking along +x at a sighted target <paramref name="range"/> units on, from tick 0 to 100.</summary>
    private static Stream Range(ActorId shooter, ActorId target, float range)
    {
        var s = new Stream();
        for (int t = 0; t < 100; t++)
        {
            s.Frame(shooter, t, new AiVector(0, 0, 0));
            s.Frame(target, t, new AiVector(range, 0, 0));
            s.Cmd(shooter, t, 0);
        }

        s.Recorder.Observed(AiEvent.SightGained(shooter, new Tick(0), target, default, default, 1f));
        return s;
    }

    private static void Shot(Stream s, ActorId a, int tick, int weapon = Smg) => s.Recorder.Observed(AiEvent.ShotFired(a, new Tick(tick), weapon, 1));

    private static void Hit(Stream s, ActorId a, ActorId victim, int tick, int range, int weapon = Smg) => s.Recorder.Observed(AiEvent.HitDealt(a, new Tick(tick), victim, 9f, weapon, range));

    [Fact]
    public void AKnownCountOfShotsAndHitsYieldsItsHitRate()
    {
        Stream s = Range(Person, Bot, 500);
        for (int t = 10; t < 20; t++)
            Shot(s, Person, t);
        for (int t = 10; t < 14; t++)
            Hit(s, Person, Bot, t, 500);
        HitRates h = Of(s.Metrics(), Person).Hits;
        Assert.Equal(new HitLine("overall", 4, 10), h.Overall);
        Assert.Equal(0.4, h.Overall.Rate, 6);
    }

    [Fact]
    public void AShotgunsPelletsAreOneShotAndOneHit()
    {
        Stream s = Range(Person, Bot, 200);
        Shot(s, Person, 10);
        for (int p = 0; p < 7; p++)
            Hit(s, Person, Bot, 10, 200);
        Assert.Equal(new HitLine("overall", 1, 1), Of(s.Metrics(), Person).Hits.Overall);
    }

    [Fact]
    public void TwoVictimsInOneTickAreTwoHits()
    {
        Stream s = Range(Person, Bot, 200);
        Shot(s, Person, 10);
        Hit(s, Person, Bot, 10, 200);
        Hit(s, Person, new ActorId(9, 4), 10, 210);
        Assert.Equal(2, Of(s.Metrics(), Person).Hits.Overall.Hits);
    }

    [Fact]
    public void HitRateIsPerWeaponByTheNameTheWorldGives()
    {
        var bytes = new MemoryStream();
        var r = new AiRecorder(bytes, Recordings.Header);
        var map = new MapKnowledge("dm_x", [], [], new WeaponKnowledge([new WeaponFacts(Smg, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f)]));
        r.World(new AiRecordedWorld(map, 0.015f, "", 0));
        r.UserCmd(new AiUserCmd(Person, new Tick(1), 1, 1, default, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        r.Observed(AiEvent.ShotFired(Person, new Tick(1), Smg, 1));
        r.Observed(AiEvent.ShotFired(Person, new Tick(2), Smg, 1));
        r.Observed(AiEvent.ShotFired(Person, new Tick(3), Pistol, 1));
        r.Observed(AiEvent.HitDealt(Person, new Tick(2), Bot, 9f, Smg, 100));
        HitRates h = Of(AiMetrics.Read(new MemoryStream(bytes.ToArray())), Person).Hits;
        Assert.Equal([new HitLine("weapon 2", 0, 1), new HitLine("weapon_smg1", 1, 2)], h.ByWeapon);
    }

    /// <summary>The bands: under 300, 300 to 800, over 800; 300 and 800 are the middle one's.</summary>
    [Theory]
    [InlineData(299f, 0)]
    [InlineData(300f, 1)]
    [InlineData(800f, 1)]
    [InlineData(801f, 2)]
    public void ARangeFallsInItsBand(float range, int band) => Assert.Equal(band, AiMetrics.RangeBand(range));

    [Fact]
    public void HitRateIsPerRangeBandWithSampleCounts()
    {
        Stream s = Range(Person, Bot, 1000);
        for (int t = 10; t < 15; t++)
            Shot(s, Person, t);
        Hit(s, Person, Bot, 11, 1000);
        Hit(s, Person, Bot, 12, 1000);
        HitRates h = Of(s.Metrics(), Person).Hits;
        Assert.Equal([new HitLine("<300", 0, 0), new HitLine("300-800", 0, 0), new HitLine(">800", 2, 5)], h.ByRange);
        Assert.Equal(0, h.Untargeted);
    }

    /// <summary>A shot at nobody near the crosshair, or at an actor not sighted, counts overall and in no band.</summary>
    [Fact]
    public void AShotWithNoSightedTargetNearTheCrosshairIsInNoBand()
    {
        var s = new Stream();
        for (int t = 0; t < 40; t++)
        {
            s.Frame(Person, t, new AiVector(0, 0, 0));
            s.Frame(Bot, t, new AiVector(500, 0, 0));
            s.Cmd(Person, t, t < 20 ? 90 : 0);
        }

        Shot(s, Person, 5);    // nobody sighted yet
        s.Recorder.Observed(AiEvent.SightGained(Person, new Tick(10), Bot, default, default, 1f));
        Shot(s, Person, 15);   // sighted, 90 degrees off the crosshair
        Shot(s, Person, 25);   // sighted, on the crosshair
        s.Recorder.Observed(AiEvent.SightLost(Person, new Tick(30), Bot, default, default));
        Shot(s, Person, 35);   // lost
        HitRates h = Of(s.Metrics(), Person).Hits;
        Assert.Equal((4, 3, 1), (h.Overall.Shots, h.Untargeted, h.ByRange[1].Shots));
    }

    [Fact]
    public void ABotsHitRateIsByTheSameDefinitions()
    {
        Stream s = Range(Bot, Person, 200);
        s.Recorder.BotAdded(Bot, 1, []);
        Shot(s, Bot, 10);
        Shot(s, Bot, 11);
        Hit(s, Bot, Person, 11, 200);
        ActorMetrics m = Of(s.Metrics(), Bot);
        Assert.True(m.Bot);
        Assert.Equal((new HitLine("overall", 1, 2), new HitLine("<300", 1, 2)), (m.Hits.Overall, m.Hits.ByRange[0]));
    }

    [Fact]
    public void AnotherActorsShotsAreNotCounted()
    {
        Stream s = Range(Person, Bot, 500);
        Shot(s, Bot, 10);
        Hit(s, Bot, Person, 10, 500);
        Assert.Equal(new HitLine("overall", 0, 0), Of(s.Metrics(), Person).Hits.Overall);
    }

    [Fact]
    public void TheTablePrintsTheHitRateLines()
    {
        Stream s = Range(Person, Bot, 500);
        Shot(s, Person, 10);
        Shot(s, Person, 11);
        Hit(s, Person, Bot, 10, 500);
        string table = AiMetrics.Table(s.Metrics());
        Assert.Contains("overall 1/2 50.0%", table, StringComparison.Ordinal);
        Assert.Contains("300-800 1/2 50.0%", table, StringComparison.Ordinal);
        Assert.Contains("no target 0 shots", table, StringComparison.Ordinal);
    }
}
