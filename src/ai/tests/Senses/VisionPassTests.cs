using SourceSharp.Ai.Hl2mp.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// The vision pass (plan_hl2mp_ai §4.1) over a fake engine: range, cone, PVS,
/// the trace budget and recognition, and what blocks sight. Each negative
/// names its stage; the positive control beside it shows the same opponent
/// is seen once the one thing in the way is gone.
/// </summary>
public sealed class VisionPassTests
{
    private const float Tick = 0.015f;
    private static readonly VisionParams Eyes = new(6000f, 90f, 0.3f, 0.5f);

    private sealed class Rig
    {
        public readonly SenseWorld World = new();
        public readonly VisionPass Pass;
        public readonly SenseWorld.Player Bot;
        public readonly SenseWorld.Player Them;
        public readonly ActorId BotId = new(1, 1);
        public readonly List<AiEvent> Events = new();
        private int _tick;

        public Rig(AiVector them)
        {
            Bot = World.Add(1, new AiVector(0, 0, 0));
            Them = World.Add(2, them);
            Pass = new VisionPass(World.Engine(), World.Entities(), World.Traces());
            Pass.Add(BotId);
        }

        public ActorId ThemId => ActorDirectory.HumanActor(2, Them.Handle);

        public SensedBot Sensed => new(BotId, Bot.Handle, Eyes);

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                Pass.Run(new Tick(++_tick), Tick, [Sensed], World.Living(Sensed), Events);
        }

        public bool Sees => Pass.SightOf(1)!.Sees(2);

        public bool Gained => Events.Any(e => e.Kind == AiEventKind.SightGained && e.Other == ThemId);
    }

    [Fact]
    public void AnOpponentInTheOpenIsSeenOnceRecognised()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.Run(40);
        Assert.True(r.Gained && r.Sees, "the control: nothing in the way, seen");
        Assert.Equal(1f, r.Pass.SightOf(1)!.FractionOf(2));
    }

    [Fact]
    public void ATargetSeenForLessThanTheRecognitionTimeYieldsNoEvent()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.Run(VisionParams.Ticks(Eyes.RecognizeSeconds, Tick) - 1);
        Assert.Empty(r.Events);
        Assert.False(r.Sees);
        r.Run(1);
        Assert.True(r.Gained, "one tick more recognises it");
    }

    [Fact]
    public void AnOpponentBehindAWallIsNeverSeen()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.World.Occluder(new AiVector(250, -200, -10), new AiVector(266, 200, 300), SenseWorld.Solid);
        r.Run(200);
        Assert.False(r.Gained || r.Sees);
        Assert.Equal(SightStage.Trace, r.Pass.StageOf(1, 2));
    }

    [Fact]
    public void AnOpponentOutsideTheConeIsNotSeenEvenTenUnitsAway()
    {
        var r = new Rig(new AiVector(-10, 0, 0));
        r.Run(200);
        Assert.False(r.Gained || r.Sees);
        Assert.Equal(SightStage.Cone, r.Pass.StageOf(1, 2));
    }

    [Fact]
    public void AnOpponentOutsideThePvsIsNotSeenThoughTheLineIsClear()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.World.Pvs = (_, _, _) => false;
        r.Run(200);
        Assert.False(r.Gained || r.Sees);
        Assert.Equal(SightStage.Pvs, r.Pass.StageOf(1, 2));
    }

    [Fact]
    public void AnOpponentBeyondTheSightRangeIsNotSeen()
    {
        var r = new Rig(new AiVector(6500, 0, 0));
        r.Run(200);
        Assert.False(r.Gained || r.Sees);
        Assert.Equal(SightStage.Range, r.Pass.StageOf(1, 2));
    }

    [Fact]
    public void ATraceBudgetStarvedForAHundredTicksNeverProducesASight()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.Pass.TracesPerTick = 0;
        r.Run(100);
        Assert.False(r.Gained || r.Sees);
        Assert.Equal(0, r.World.Lines);
        Assert.Equal(SightStage.Unserved, r.Pass.StageOf(1, 2));
        r.Pass.TracesPerTick = 32;
        r.Run(40);
        Assert.True(r.Gained, "served again, it is seen");
    }

    [Fact]
    public void AnOpponentBehindAClosedDoorIsNeverSeen()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.World.Occluder(new AiVector(250, -32, 0), new AiVector(262, 32, 128), SenseWorld.Solid, entity: 900);
        r.Run(200);
        Assert.False(r.Gained || r.Sees);
    }

    [Fact]
    public void AnOpponentBehindAStackOfCratesIsNeverSeen()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.World.Occluder(new AiVector(240, -24, 0), new AiVector(280, 24, 40), SenseWorld.Solid | SenseWorld.Moveable, entity: 901);
        r.World.Occluder(new AiVector(240, -24, 40), new AiVector(280, 24, 80), SenseWorld.Solid | SenseWorld.Moveable, entity: 902);
        r.Run(200);
        Assert.False(r.Gained || r.Sees);
    }

    [Fact]
    public void AnOpponentBehindAnotherPlayerIsNeverSeen()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.World.Add(3, new AiVector(100, 0, 0));
        r.Run(200);
        Assert.False(r.Events.Any(e => e.Other == r.ThemId) || r.Sees);
        Assert.True(r.Pass.SightOf(1)!.Sees(3), "the player in front is seen");
    }

    [Fact]
    public void AnOpponentBehindAWindowIsSeen()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.World.Occluder(new AiVector(250, -200, -10), new AiVector(254, 200, 300), SenseWorld.Window);
        r.Run(40);
        Assert.True(r.Gained && r.Sees);
    }

    [Fact]
    public void AnOpponentBehindAGrateIsSeen()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.World.Occluder(new AiVector(250, -200, -10), new AiVector(254, 200, 300), SenseWorld.Grate);
        r.Run(40);
        Assert.True(r.Gained && r.Sees);
    }

    [Fact]
    public void AnOpponentHalfBehindACrateIsSeenInPart()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        // A crate as high as the opponent's chest hides chest, pelvis and
        // shoulders from the bot's eye, not the head.
        r.World.Occluder(new AiVector(440, -40, 0), new AiVector(470, 40, 50), SenseWorld.Solid | SenseWorld.Moveable, entity: 903);
        r.Run(40);
        float f = r.Pass.SightOf(1)!.FractionOf(2);
        Assert.True(r.Sees, "the head is clear");
        Assert.InRange(f, 0.01f, 0.99f);
    }

    [Fact]
    public void SightIsLostAfterTheLostSightTimeBehindAWall()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.Run(40);
        Assert.True(r.Gained);
        r.Them.Origin = new AiVector(500, 600, 0);
        r.World.Occluder(new AiVector(250, 100, -10), new AiVector(266, 1000, 300), SenseWorld.Solid);
        r.Run(VisionParams.Ticks(Eyes.LostSightSeconds, Tick) - 1);
        Assert.False(r.Sees, "not seen behind the wall at once");
        Assert.DoesNotContain(r.Events, e => e.Kind == AiEventKind.SightLost);
        r.Run(1);
        Assert.Contains(r.Events, e => e.Kind == AiEventKind.SightLost && e.Other == r.ThemId);
    }

    /// <summary>
    /// A person sees someone go behind a wall where they went, not where they have got
    /// to by the time they stop expecting them: the place the event carries is the last
    /// one with a clear line, however far they went while it was deciding.
    /// </summary>
    [Fact]
    public void ALostSightSaysWhereTheyWereLastSeen()
    {
        var seen = new AiVector(500, 0, 0);
        var r = new Rig(seen);
        r.Run(40);
        Assert.True(r.Sees);
        r.World.Occluder(new AiVector(100, -300, 0), new AiVector(120, 300, 200), 0x1);
        r.Them.Origin = new AiVector(900, 700, 0);
        r.Run(VisionParams.Ticks(Eyes.LostSightSeconds, Tick) + 4);
        AiEvent lost = r.Events.First(e => e.Kind == AiEventKind.SightLost && e.Other == r.ThemId);
        Assert.Equal(seen, lost.Position);
    }

    [Fact]
    public void ADeadOpponentIsLostAtOnce()
    {
        var r = new Rig(new AiVector(500, 0, 0));
        r.Run(40);
        r.Them.Alive = false;
        r.Run(1);
        Assert.Contains(r.Events, e => e.Kind == AiEventKind.SightLost && e.Other == r.ThemId);
    }

    [Fact]
    public void TheSightMaskIsStocksBlockLosAndNpcs()
    {
        // MASK_BLOCKLOS_AND_NPCS | CONTENTS_IGNORE_NODRAW_OPAQUE (bspflags.h;
        // NextBotVisionInterface.cpp:704): SOLID, MOVEABLE, BLOCKLOS, MONSTER,
        // IGNORE_NODRAW_OPAQUE; not WINDOW, not GRATE.
        Assert.Equal(0x1u | 0x4000u | 0x40u | 0x2000000u | 0x2000u, VisionPass.SightMask);
        Assert.Equal(0u, VisionPass.SightMask & (SenseWorld.Window | SenseWorld.Grate));
    }

    [Fact]
    public void TheBudgetServesWholePairsRoundRobin()
    {
        var b = new TraceBudget();
        long[] keys = [1, 2, 3, 4];
        int[] costs = [5, 5, 5, 5];
        var served = new bool[4];
        Assert.Equal(10, b.Select(keys, costs, 12, served));
        Assert.Equal([true, true, false, false], served);
        b.Select(keys, costs, 12, served);
        Assert.Equal([false, false, true, true], served);
        b.Select(keys, costs, 12, served);
        Assert.Equal([true, true, false, false], served);
    }

    [Fact]
    public void FourBotsAndThreeOthersAreAllServedWithinAFewTicks()
    {
        var w = new SenseWorld();
        var bots = new List<SensedBot>();
        for (int s = 1; s <= 4; s++)
        {
            SenseWorld.Player p = w.Add(s, new AiVector(s * 40, 0, 0), yaw: 90f);
            bots.Add(new SensedBot(new ActorId(s, 1), p.Handle, Eyes));
        }

        for (int s = 5; s <= 7; s++)
            w.Add(s, new AiVector(s * 40, 800, 0));
        var pass = new VisionPass(w.Engine(), w.Entities(), w.Traces());
        bots.ForEach(b => pass.Add(b.Id));
        var events = new List<AiEvent>();
        for (int t = 1; t <= 60; t++)
            pass.Run(new Tick(t), Tick, bots.ToArray(), w.Living([.. bots]), events);
        Assert.All(bots, b => Assert.True(Enumerable.Range(5, 3).All(pass.SightOf(b.Id.Slot)!.Sees), $"bot {b.Id.Slot} sees the three"));
        Assert.True(pass.TracesLastTick <= 32);
    }
}

/// <summary>Item spawns looked at (plan_hl2mp_ai §5.1, items observed): seen there, seen gone, or not seen through a wall.</summary>
public sealed class ItemSightTests
{
    private static readonly VisionParams Eyes = new(6000f, 90f, 0.3f, 0.5f);
    private static readonly ItemSpawn Kit = new(0, "item_healthkit", ItemKind.Health, new AiVector(400, 0, 0), 30f);

    private static List<AiEvent> Look(Action<SenseWorld> setup)
    {
        var w = new SenseWorld();
        SenseWorld.Player bot = w.Add(1, new AiVector(0, 0, 0));
        setup(w);
        var pass = new VisionPass(w.Engine(), w.Entities(), w.Traces());
        var id = new ActorId(1, 1);
        pass.Add(id);
        var sensed = new SensedBot(id, bot.Handle, Eyes);
        var events = new List<AiEvent>();
        pass.Run(new Tick(1), 0.015f, [sensed], w.Living(sensed), new List<AiEvent>());
        pass.RunItems(new Tick(1), [sensed], [Kit], events);
        return events;
    }

    [Fact]
    public void AnItemInViewIsSeenThere()
    {
        AiEvent e = Assert.Single(Look(w => w.Items.Add(("item_healthkit", new AiVector(400, 0, 0), new SourceSharp.Abi.EntityHandle(500u)))));
        Assert.Equal((AiEventKind.ItemSeen, 0, 1), (e.Kind, e.Code, e.Code2));
    }

    [Fact]
    public void ATakenItemIsSeenGone()
        => Assert.Equal(0, Assert.Single(Look(_ => { })).Code2);

    [Fact]
    public void AnItemBehindAWallIsNotSeen()
        => Assert.Empty(Look(w => w.Occluder(new AiVector(200, -100, -10), new AiVector(216, 100, 300), SenseWorld.Solid)));
}
