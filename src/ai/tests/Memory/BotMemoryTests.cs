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

    /// <summary>The HUD's nemesis mark (plan_bots_teams_tf2_part_g G.2.5) is on someone for as long as the HUD shows it, and on nobody else.</summary>
    [Fact]
    public void TheHudsNemesisMarkIsKeptUntilTheHudTakesItOff()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.ScoreboardRow(Bot, new Tick(1), new ActorId(4, 9), 0, 0, 2));
        m.Apply(AiEvent.Nemesis(Bot, new Tick(5), Them, true));
        Assert.Equal((true, false), (m.Board.Actor(Them.Slot).Nemesis, m.Board.Actor(4).Nemesis));
        m.Apply(AiEvent.Nemesis(Bot, new Tick(9), Them, false));
        Assert.False(m.Board.Actor(Them.Slot).Nemesis);
    }

    /// <summary>The applier's arrival is kept as when and where the bot got to (cf's Descent proposal 5).</summary>
    [Fact]
    public void AnArrivalIsKeptAsWhenAndWhere()
    {
        BotMemory m = Memory();
        Assert.Equal(default, m.LastArrived);
        m.Apply(AiEvent.Arrived(Bot, new Tick(7), new AiVector(10, 20, 30)));
        Assert.Equal((new Tick(7), new AiVector(10, 20, 30)), m.LastArrived);
    }

    /// <summary>A teammate's hurt is kept with who hurt it, where, and when; the world hurts with no attacker.</summary>
    [Fact]
    public void AnAllysHurtIsKeptWithItsAttacker()
    {
        BotMemory m = Memory();
        var ally = new ActorId(4, 9);
        m.Apply(AiEvent.AllyDamaged(Bot, new Tick(5), ally, new AiVector(1, 2, 3), 25f, Them));
        Assert.Equal((ally, Them, new Tick(5), new AiVector(1, 2, 3)), m.LastAllyHurt);
        m.Apply(AiEvent.AllyDamaged(Bot, new Tick(6), ally, new AiVector(4, 5, 6), 10f, ActorId.None));
        Assert.Equal((ally, ActorId.None, new Tick(6), new AiVector(4, 5, 6)), m.LastAllyHurt);
    }

    /// <summary>A teammate's death is counted (a morale's input), kept with its killer, and marks the ally dead on the board.</summary>
    [Fact]
    public void AnAllysDeathIsCountedAndMarksItDead()
    {
        BotMemory m = Memory();
        var ally = new ActorId(4, 9);
        m.Apply(AiEvent.ScoreboardRow(Bot, new Tick(1), ally, 0, 0, 2));
        Assert.False(m.Board.Actor(ally.Slot).Dead);
        m.Apply(AiEvent.AllyDied(Bot, new Tick(8), ally, new AiVector(7, 8, 9), Them));
        Assert.Equal((1, ally, Them, new Tick(8)), (m.AlliesLost, m.LastAllyDied.Ally, m.LastAllyDied.Killer, m.LastAllyDied.When));
        Assert.True(m.Board.Actor(ally.Slot).Dead);
        m.Apply(AiEvent.AllyDied(Bot, new Tick(12), new ActorId(5, 3), new AiVector(0, 0, 0), ActorId.None));
        Assert.Equal(2, m.AlliesLost);
    }

    /// <summary>A downed hero is marked so until it gets up, and a death or respawn ends it.</summary>
    [Fact]
    public void ADownedHeroIsMarkedUntilItGetsUpOrDies()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.HeroDowned(Bot, new Tick(3), Them, new AiVector(1, 1, 1), down: true));
        Assert.True(m.Board.Actor(Them.Slot).Downed);
        m.Apply(AiEvent.HeroDowned(Bot, new Tick(4), Them, new AiVector(1, 1, 1), down: false));
        Assert.False(m.Board.Actor(Them.Slot).Downed);
        m.Apply(AiEvent.HeroDowned(Bot, new Tick(5), Them, new AiVector(1, 1, 1), down: true));
        m.Apply(AiEvent.KillFeed(Bot, new Tick(6), Them, ActorId.None));
        Assert.Equal((false, true), (m.Board.Actor(Them.Slot).Downed, m.Board.Actor(Them.Slot).Dead));
    }

    [Fact]
    public void AnothersHitIsAnAttack()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.Damaged(Bot, new Tick(40), Them, 20f, new AiVector(1, 0, 0), 0));
        Assert.Equal((new Tick(40), new Tick(40)), (m.Board.LastDamageTick, m.Board.LastAttackTick));
    }

    /// <summary>The bot's own blast (plan_bots_teams_tf2_part_g G.13: a rocket jump) is damage on its HUD and nobody's attack.</summary>
    [Fact]
    public void TheBotsOwnBlastIsDamageAndNoAttack()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.Damaged(Bot, new Tick(40), Them, 20f, new AiVector(1, 0, 0), 0));
        m.Apply(AiEvent.Damaged(Bot, new Tick(90), Bot, 45f, default, 0));
        Assert.Equal((new Tick(90), new Tick(40)), (m.Board.LastDamageTick, m.Board.LastAttackTick));
    }

    /// <summary>A hurt with no attacker (a fall, the water) is damage and nobody's attack.</summary>
    [Fact]
    public void TheWorldsHurtIsDamageAndNoAttack()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.Damaged(Bot, new Tick(40), Them, 20f, new AiVector(1, 0, 0), 0));
        m.Apply(AiEvent.Damaged(Bot, new Tick(90), ActorId.None, 12f, default, 0));
        Assert.Equal((new Tick(90), new Tick(40)), (m.Board.LastDamageTick, m.Board.LastAttackTick));
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
