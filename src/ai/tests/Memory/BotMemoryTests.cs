using System.Reflection;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Memory;

/// <summary>
/// A bot's memory (plan_hl2mp_ai §4.5, §5.1, §5.2): only perceptions move it.
/// An unperceived shot or a reload out of sight changes nothing; a heard or
/// seen one does; a shot heard and seen at once is one shot; confidence and
/// habits decay by their half-lives; investigations rank by loudness and
/// recency; items expect their respawns; the opponents' habits never touch a
/// file.
/// </summary>
public sealed class BotMemoryTests
{
    private static readonly ActorId Bot = new(3, 1);
    private static readonly ActorId Them = new(2, 0x4000_0001);
    private static readonly WeaponFacts Smg1 = AmmoEstimateTests.Smg1;
    private static readonly WeaponFacts Magnum = new(4, "weapon_357", 6, 0.75f, 3.7f, false, false, 0f);

    private static MemoryWorld World()
    {
        var items = System.Collections.Immutable.ImmutableArray.Create(new ItemSpawn(0, "item_healthkit", ItemKind.Health, new AiVector(900, 0, 0), 30f));
        return new MemoryWorld(new MapKnowledge("m", items, [], new WeaponKnowledge([Smg1, Magnum])), null, 0.015f, id => id.ToString());
    }

    private static BotMemory Memory(HabitStore? habits = null)
        => new(Bot, 8, new MemoryParams(4f, 120f), World(), habits ?? new HabitStore());

    private static PerceivedView View(int tick, bool sees, int weapon = 3)
    {
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(tick), new SelfState(default, default, default, 100, 0, 3, 45, 90, SelfFlags.Alive));
        if (sees)
            v.AddSighted(new ActorFacts(Them, new AiVector(500, 0, 0), default, weapon, ActorActivity.None, 1f));
        return v;
    }

    /// <summary>Seen with an SMG1, then lost: the estimate is the whole clip.</summary>
    private static BotMemory SeenThenLost()
    {
        BotMemory m = Memory();
        m.Step(new Tick(1), View(1, true), [AiEvent.SightGained(Bot, new Tick(1), Them, new AiVector(500, 0, 0), default, 1f)]);
        m.Step(new Tick(2), View(2, false), [AiEvent.SightLost(Bot, new Tick(2), Them, new AiVector(500, 0, 0), default)]);
        return m;
    }

    [Fact]
    public void AShotTheBotNeitherHeardNorSawChangesNoEstimate()
    {
        BotMemory m = SeenThenLost();
        (int, int) before = (m.Board.Actor(2).Ammo.Min, m.Board.Actor(2).Ammo.Max);
        m.Step(new Tick(3), View(3, false), []);
        Assert.Equal(before, (m.Board.Actor(2).Ammo.Min, m.Board.Actor(2).Ammo.Max));

        // The control: the same shot, heard, moves it.
        m.Step(new Tick(4), View(4, false), [AiEvent.SoundHeard(Bot, new Tick(4), SoundKind.Gunfire, new AiVector(500, 0, 0), 1f, Them, 3)]);
        Assert.Equal(44, m.Board.Actor(2).Ammo.Max);
    }

    [Fact]
    public void ShotsHeardReduceTheEstimateByTheNumberHeard()
    {
        BotMemory m = SeenThenLost();
        for (int i = 0; i < 17; i++)
            m.Apply(AiEvent.SoundHeard(Bot, new Tick(10 + i), SoundKind.Gunfire, new AiVector(500, 0, 0), 1f, Them, 3));
        Assert.Equal(28, m.Board.Actor(2).Ammo.Max);
    }

    [Fact]
    public void AShotHeardAndSeenAtOnceIsOneShot()
    {
        BotMemory m = SeenThenLost();
        m.Apply(AiEvent.SeenFire(Bot, new Tick(10), Them, 3));
        m.Apply(AiEvent.SoundHeard(Bot, new Tick(10), SoundKind.Gunfire, new AiVector(500, 0, 0), 1f, Them, 3));
        Assert.Equal(44, m.Board.Actor(2).Ammo.Max);
    }

    [Fact]
    public void AMagnumReloadOutOfSightChangesNothing()
    {
        BotMemory m = Memory();
        m.Step(new Tick(1), View(1, true, weapon: 4), [AiEvent.SightGained(Bot, new Tick(1), Them, new AiVector(500, 0, 0), default, 1f)]);
        m.Step(new Tick(2), View(2, false), [AiEvent.SightLost(Bot, new Tick(2), Them, new AiVector(500, 0, 0), default)]);
        for (int i = 0; i < 6; i++)
            m.Apply(AiEvent.SoundHeard(Bot, new Tick(3 + i), SoundKind.Gunfire, new AiVector(500, 0, 0), 1f, Them, 4));
        Assert.Equal((0, 0), (m.Board.Actor(2).Ammo.Min, m.Board.Actor(2).Ammo.Max));
        // The 357 reloads out of sight: nothing reaches the bot (no sound is emitted, no gesture is seen), so no event.
        m.Step(new Tick(20), View(20, false), []);
        Assert.False(m.Board.Actor(2).Ammo.ReloadInProgress);
        Assert.True(m.Board.Actor(2).Ammo.PEmpty > 0.9f);
    }

    [Fact]
    public void ASightedSmg1ReloadResetsTheEstimateAtItsEnd()
    {
        BotMemory m = SeenThenLost();
        for (int i = 0; i < 40; i++)
            m.Apply(AiEvent.SoundHeard(Bot, new Tick(10 + i), SoundKind.Gunfire, new AiVector(500, 0, 0), 1f, Them, 3));
        m.Apply(AiEvent.SeenReload(Bot, new Tick(60), Them, ReloadPhase.Start, 3));
        m.Advance(new Tick(60 + VisionParams.Ticks(1.5f, 0.015f)));
        Assert.Equal((45, 45), (m.Board.Actor(2).Ammo.Min, m.Board.Actor(2).Ammo.Max));
        Assert.Equal(40f / 45f, m.HabitsOf(Them).ReloadStyle, 3);
    }

    [Fact]
    public void ConfidenceHalvesEveryMemoryHalfLife()
    {
        BotMemory m = SeenThenLost();
        m.Advance(new Tick(2 + (int)(4f / 0.015f)));
        Assert.Equal(0.5f, m.Board.Actor(2).Confidence, 2);
        Assert.Equal(0.25f, Blackboard.Decayed(1f, 8f, 4f), 4);
    }

    [Fact]
    public void HabitCountersHalveEveryHabitHalfLife()
    {
        var h = new OpponentHabits("x", 4);
        for (int i = 0; i < 8; i++)
            h.Visit(new CellId(1));
        h.LostSight(afterHurt: true);
        h.Decay(120f, 120f);
        Assert.Equal(4, h.Route[1]);
        Assert.Equal(0.5f, h.SightLosses, 3);
    }

    [Fact]
    public void ALossOfSightJustAfterBeingHurtIsARetreat()
    {
        BotMemory m = Memory();
        m.Step(new Tick(1), View(1, true), [AiEvent.SightGained(Bot, new Tick(1), Them, new AiVector(500, 0, 0), default, 1f)]);
        m.Apply(AiEvent.DealtDamage(Bot, new Tick(5), Them, 20, 80));
        m.Apply(AiEvent.SightLost(Bot, new Tick(20), Them, new AiVector(500, 0, 0), default));
        Assert.Equal(1f, m.HabitsOf(Them).RetreatTendency);
        Assert.Equal(80, m.Board.Actor(2).HealthEstimate);
    }

    [Fact]
    public void InvestigationsKeepTheLoudestAndNewest()
    {
        var b = new Blackboard(4);
        for (int i = 0; i < 8; i++)
            b.Investigate(new Investigation(new AiVector(i, 0, 0), 0.1f, new Tick(1), SoundKind.Footstep, ActorId.None), new Tick(1), 0.015f);
        b.Investigate(new Investigation(new AiVector(99, 0, 0), 1f, new Tick(100), SoundKind.Gunfire, ActorId.None), new Tick(100), 0.015f);
        Assert.Equal(8, b.InvestigationCount);
        Assert.True(b.TryBestInvestigation(new Tick(100), 0.015f, out Investigation best));
        Assert.Equal(99f, best.Position.X);
    }

    [Fact]
    public void AnItemSeenGoneIsExpectedBackAfterTheGamesInterval()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.ItemSeen(Bot, new Tick(100), 0, false));
        ItemBelief b = m.Board.Items[0];
        Assert.Equal(false, b.Present);
        Assert.Equal(new Tick(100 + VisionParams.Ticks(30f, 0.015f)), b.ExpectedBack);
    }

    [Fact]
    public void AnOpponentMetAfterTheBotsDeathStartsUnknown()
    {
        BotMemory m = SeenThenLost();
        m.Apply(AiEvent.SoundHeard(Bot, new Tick(5), SoundKind.Gunfire, new AiVector(500, 0, 0), 1f, Them, 3));
        m.Apply(AiEvent.Killed(Bot, new Tick(6), Them));
        Assert.False(m.Board.Actor(2).Ammo.Known);
        Assert.True(m.Board.Actor(2).Known, "who it knew stays (the map tier)");
    }

    [Fact]
    public void HabitsOutliveALifeInTheBotsStore()
    {
        var store = new HabitStore();
        BotMemory first = Memory(store);
        first.HabitsOf(Them).LostSight(afterHurt: false);
        BotMemory second = Memory(store);
        Assert.True(second.TryHabits(Them, out OpponentHabits? h) && h!.SightLosses == 1f);
    }

    [Fact]
    public void NoMemoryTypeTouchesAFile()
    {
        // Opponent memory is in memory only (ruling 2026-11-01): no core type calls System.IO's file or directory APIs.
        Assembly core = typeof(BotMemory).Assembly;
        string[] callers = IlCallCensus.Callers(m => m.DeclaringType?.Namespace == "System.IO" && m.DeclaringType.Name is "File" or "Directory" or "FileStream" or "FileInfo", core);
        Assert.Empty(callers);
        Assert.NotEmpty(IlCallCensus.Callers(m => m.DeclaringType == typeof(OpponentHabits), core));
    }

    // Live, dm_lockdown (2026-11-02): a bot's memory listed itself and its
    // scoreboard-only rivals with a decaying confidence.
    [Fact]
    public void TheBotIsNotAnActorItRemembersAndARowIsNoPlace()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.ScoreboardRow(Bot, new Tick(5), Bot, 2, 1, 1));
        m.Apply(AiEvent.ScoreboardRow(Bot, new Tick(5), Them, 3, 0, 1));
        m.Advance(new Tick(50));
        Assert.False(m.Board.Actor(Bot.Slot).Known);
        Assert.True(m.Board.Actor(Them.Slot).Known, "the control: the rival's row is remembered");
        Assert.Equal(0f, m.Board.Actor(Them.Slot).Confidence);
    }
}
