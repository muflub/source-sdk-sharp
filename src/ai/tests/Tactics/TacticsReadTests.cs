using SourceSharp.Ai.Tests.Record;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary><c>ai-replay tactics</c> (plan_hl2mp_ai §5.4, §5.5) over a hand-built recording with a known answer.</summary>
public sealed class TacticsReadTests
{
    private static readonly ActorId Bot = new(3, 7), Other = new(4, 8);
    private static readonly AiDecisionTree Tree = new(["root", "Ambush", "ambush", "Roam", "roam"], [0, 1, 1, 3, 3]);
    private const int Ambush = 2, Roam = 4;

    private static readonly ItemSpawn Kit = new(0, "item_healthkit", ItemKind.Health, new AiVector(100, 0, 0), 30f);
    private static readonly ItemSpawn Far = new(1, "item_ammo_smg1", ItemKind.Ammo, new AiVector(0, 40, 0), 30f);

    private sealed class Rec
    {
        private readonly MemoryStream _bytes = new();
        private readonly AiRecorder _r;

        public Rec(ActorId bot)
        {
            _r = new AiRecorder(_bytes, Recordings.Header);
            _r.World(new AiRecordedWorld(new MapKnowledge("ss_test", [Kit, Far], [], new WeaponKnowledge([])), AiMetrics.TickSeconds, "", 0));
            _r.BotAdded(bot, 1, []);
            _r.DecisionTree(Tree.Names, Tree.Branches);
        }

        public void Frames(ActorId a, int from, int to, float x)
        {
            for (int t = from; t < to; t++)
                _r.Frame(new AiActorFrame(a, new Tick(t), new AiVector(x, 0, 0), default, default));
        }

        public void Decide(int t, int node, bool prefire)
        {
            LookIntent look = prefire ? new LookIntent(LookKind.CornerCheck, default, ActorId.None, AimPoint.Body, 0f, 0) : default;
            ActionIntent action = prefire ? new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0) : default;
            _r.Intent(new IntentCommand(Bot, new Tick(t), t, new Tick(t + IntentCommand.LifetimeTicks), default, look, action, new DecisionReason(node, 0, 0, 0)));
        }

        public void Event(in AiEvent e) => _r.Event(e);

        public TacticsReport Read(params AiVector[] chokes) => AiTactics.Read(new MemoryStream(_bytes.ToArray()), chokes, _ => 0);
    }

    /// <summary>Standing still under the ambush leaf is holding, and it is measured from the pickups worth holding: not the ammo beside it.</summary>
    [Fact]
    public void AHoldIsMeasuredFromTheGoodPickups()
    {
        var r = new Rec(Bot);
        r.Decide(0, Ambush, false);
        r.Frames(Bot, 0, 200, 0f);
        TacticsActor a = r.Read(new AiVector(0, 300, 0)).Bots.Single();
        Assert.Equal(200 * AiMetrics.TickSeconds, a.HoldSeconds, 3);
        Assert.Equal(100.0, a.HoldNearestPickup, 3);
        Assert.Equal(300.0, a.HoldNearestChoke, 3);
    }

    /// <summary>Walking under the leaf, or standing under another, is no hold.</summary>
    [Fact]
    public void WalkingOrRoamingIsNoHold()
    {
        var r = new Rec(Bot);
        r.Decide(0, Roam, false);
        r.Frames(Bot, 0, 200, 0f);
        Assert.Equal(0.0, r.Read().Bots.Single().HoldSeconds);
    }

    /// <summary>A burst is a run of intents firing at a corner; a shot under one is its, and one after is not.</summary>
    [Fact]
    public void AShotUnderAPrefireIsCountedAndOneAfterIsNot()
    {
        var r = new Rec(Bot);
        r.Decide(0, Roam, false);
        r.Decide(10, Roam, true);
        r.Decide(30, Roam, false);
        r.Frames(Bot, 0, 60, 0f);
        r.Event(AiEvent.ShotFired(Bot, new Tick(15), 1, 1));
        r.Event(AiEvent.ShotFired(Bot, new Tick(40), 1, 1));
        TacticsActor a = r.Read().Bots.Single();
        Assert.Equal((1, 1, 2), (a.Bursts, a.PrefireShots, a.Shots));
        Assert.Equal(new[] { 10 }, a.BurstTicks);
    }

    /// <summary>Someone dying at a pickup's spawn was killed picking up, wherever the killer stood.</summary>
    [Fact]
    public void AKillAtASpawnIsKilledPickingUp()
    {
        var r = new Rec(Bot);
        r.Decide(0, Roam, false);
        r.Frames(Bot, 0, 20, 0f);
        r.Frames(Other, 0, 20, 90f);
        r.Event(AiEvent.KillFeed(Bot, new Tick(10), Other, Bot));
        TacticsReport t = r.Read();
        Assert.Equal((1, 1), (t.Kills, t.KilledAtPickups));
    }
}
