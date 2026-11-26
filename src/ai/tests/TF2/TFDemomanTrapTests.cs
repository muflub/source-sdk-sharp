using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFDemomanTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Demoman's sticky trap and the Trapper (plan_bots_teams_tf2 P7; .scratch/p7-research/demoman.md §4.3 "Sticky trap",
/// A3 Trapper): laid on the defending role in a lull at the bot's own flag, spread over spots, stopped by an enemy in sight
/// or a hit; the Trapper defends whatever role is dealt, lays more, watches the trap, and looks at a sticky before his
/// in-view launcher detonates it.
/// </summary>
public sealed class TFDemomanTrapTests
{
    /// <summary>Near its home flag: 450 u off it toward the enemy's end (the field's flag stands 256 u from the red wall).</summary>
    private static readonly AiVector Lay = new(TFField.RedFlag.X + 450f, TFField.RedFlag.Y, 0f);

    /// <summary>The Scottish Resistance's kit: in view, fourteen out, armed in 1.6 s.</summary>
    private static TFHud TrapperKit()
        => new([Item(TFLoadoutSlot.Primary, 19, "tf_weapon_grenadelauncher", Pipes),
                Item(TFLoadoutSlot.Secondary, 130, "tf_weapon_pipebomblauncher", Stickies, new("set_detonate_mode", 1f), new("add_max_pipebombs", 6f),
                     new("sticky_arm_time", 0.8f)),
                Item(TFLoadoutSlot.Melee, 1, "tf_weapon_bottle", Bottle)], [], []);

    /// <summary>A Demoman who defends: the last slot of a team of three.</summary>
    private static TFField Defender(TFHud? hud = null) => new(slot: 3, mates: [1, 2], weapons: Weapons) { Hud = hud ?? StockKit() };

    /// <summary>A seed whose draw of the Demoman's archetypes is <paramref name="name"/>.</summary>
    internal static ulong SeedDrawing(string name)
    {
        TFArchetypeTable table = TFDemomanClass.Archetypes();
        for (ulong seed = 1; seed < 5000; seed++)
        {
            if (table.Draw(seed)?.Name == name)
                return seed;
        }

        throw new InvalidOperationException($"no seed draws {name}");
    }

    /// <summary>
    /// Decides in a lull by the flag, the clip falling by one after each tap (and full after a reload), the stickies
    /// where aimed; how many stickies were laid in <paramref name="decides"/>.
    /// </summary>
    private static (int Laid, IntentCommand Last) LayFor(TFField f, int decides, AiVector at) => LayForAt(f, decides, at, TFField.Ctf());

    /// <summary><see cref="LayFor"/> under <paramref name="objectives"/>.</summary>
    private static (int Laid, IntentCommand Last) LayForAt(TFField f, int decides, AiVector at, ObjectiveKnowledge objectives)
    {
        int clip = 8, laid = 0;
        IntentCommand last = default;
        for (int n = 0; n < decides; n++)
        {
            last = f.Step(Self(at, Stickies, clip), objectives);
            if (last.Action.Kind == ActionKind.Fire && f.Decider.Doing.Verdict == "laying a trap")
            {
                laid++;
                clip--;
            }
            else if (last.Action.Kind == ActionKind.Reload)
            {
                clip = 8;
            }
        }

        return (laid, last);
    }

    /// <summary>A defending Demoman in a lull near its flag lays a sticky there: the class's branch, a tap aimed at a point.</summary>
    [Fact]
    public void ADefenderLaysATrapAtItsFlag()
    {
        TFField f = Defender();
        IntentCommand[] decides = [.. Enumerable.Range(0, 6).Select(_ => f.Step(Self(Lay, Stickies, 8), TFField.Ctf()))];
        Assert.Equal((TFDecider.ClassBranch, "laying a trap"), (f.Branch, f.Decider.Doing.Verdict));
        Assert.Contains(decides, i => (i.Action.Kind, i.Look.Kind) == (ActionKind.Fire, LookKind.Point));
    }

    /// <summary>
    /// The trap's first sticky waits for the view (review finding 7): the look at the spot is a place, which the applier's
    /// hands do not wait for, so the first decide aims and holds its fire; the tap comes after the view's settle (0.3 s).
    /// </summary>
    [Fact]
    public void TheFirstTrapStickyWaitsForTheView()
    {
        TFField f = Defender();
        IntentCommand[] decides = [.. Enumerable.Range(0, 8).Select(_ => f.Step(Self(Lay, Stickies, 8), TFField.Ctf()))];
        int first = Array.FindIndex(decides, i => i.Action.Kind == ActionKind.Fire);
        Assert.Equal(LookKind.Point, decides[0].Look.Kind);
        Assert.True(first * DecideSchedule.Period * 0.015f >= TFDemomanClass.LobSettleSeconds, $"fired at decide {first}");
    }

    /// <summary>An own point the enemy may capture, where the red bot stands guard (no flag: the trap's only objective).</summary>
    private static ObjectiveKnowledge OwnPoint() => new([new Objective(20, ObjectiveKind.Capture, TFField.Red, 1 << TFField.Blue, ObjectiveState.Open, TFField.RedFlag)]);

    /// <summary>
    /// A defender standing by its flag, inside the sticky's blast of it, backs off along its own line to lay (live,
    /// ctf_2fort: the defenders went for a place behind the flag that lay in a wall, and laid nothing all round): its goal
    /// is out of the blast and near where it stands.
    /// </summary>
    [Fact]
    public void ADefenderAtItsFlagBacksOffAlongItsOwnLineToLay()
    {
        TFField f = Defender();
        AiVector at = TFField.RedFlag + new AiVector(50f, 0f, 0f);
        f.Step(Self(at, Stickies, 8), TFField.Ctf());
        Assert.Equal("to lay a trap", f.Decider.Doing.Verdict);
        AiVector goal = f.Decider.Doing.Goal;
        Assert.True(goal.DistanceTo(TFField.RedFlag) > TFDemomanClass.SelfSafe && goal.DistanceTo(at) < 400f, $"goal {goal}");
    }

    /// <summary>An attacking Hybrid lays no trap, though an own point the enemy may take stands by it.</summary>
    [Fact]
    public void AnAttackerLaysNoTrap()
    {
        var f = new TFField(weapons: Weapons) { Hud = StockKit() };
        IntentCommand[] decides = [.. Enumerable.Range(0, 20).Select(_ => f.Step(Self(Lay, Stickies, 8), OwnPoint()))];
        Assert.Equal(TFTeamRole.Attack, f.Decider.Role);
        Assert.DoesNotContain(decides, i => i.Action.Kind == ActionKind.Fire);
    }

    /// <summary>The control: a defender by that point lays its trap there.</summary>
    [Fact]
    public void ADefenderLaysATrapAtAnOwnPoint()
    {
        TFField f = Defender();
        IntentCommand[] decides = [.. Enumerable.Range(0, 6).Select(_ => f.Step(Self(Lay, Stickies, 8), OwnPoint()))];
        Assert.Equal("laying a trap", f.Decider.Doing.Verdict);
        Assert.Contains(decides, i => i.Action.Kind == ActionKind.Fire);
    }

    /// <summary>A Hybrid's trap holds four stickies: laid one to a spot, then no more.</summary>
    [Fact]
    public void AHybridsTrapHoldsFour()
    {
        Assert.Equal(TFDemomanClass.HybridTrapStickies, LayFor(Defender(), 60, Lay).Laid);
    }

    /// <summary>With an enemy in sight, even far off, no trap is laid.</summary>
    [Fact]
    public void NoTrapWithAnEnemyInSight()
    {
        TFField f = Defender();
        IntentCommand[] decides = [.. Enumerable.Range(0, 10).Select(_ => f.StepDrawn(Self(Lay, Stickies, 8), TFField.Ctf(),
                                                                                         Drawn(Enemy, Lay + new AiVector(1500f, 0f, 0f), TFField.Blue)))];
        Assert.DoesNotContain("laying a trap", f.Decider.Doing.Verdict);
        Assert.DoesNotContain(decides, i => i.Look.Kind == LookKind.Point && i.Action.Kind == ActionKind.Fire);
    }

    /// <summary>Within a second of a hit no trap is laid; after it, it is.</summary>
    [Fact]
    public void NoTrapWithinASecondOfAHit()
    {
        TFField f = Defender();
        f.Step(Self(Lay, Stickies, 8, health: 175), TFField.Ctf());
        IntentCommand hit = f.Step(Self(Lay, Stickies, 8, health: 150), TFField.Ctf());
        Assert.NotEqual("laying a trap", f.Decider.Doing.Verdict);
        int laidAt = -1;
        for (int n = 1; n < 80 && laidAt < 0; n++)
        {
            f.Step(Self(Lay, Stickies, 8, health: 150), TFField.Ctf());
            if (f.Decider.Doing.Verdict == "laying a trap")
                laidAt = n;
        }

        Assert.True(laidAt * DecideSchedule.Period * 0.015f >= TFDemomanClass.TrapHurtSeconds, $"laid {laidAt} decides after the hit");
    }

    /// <summary>The trap's first spots stand at least its spread apart, so one blast cannot clear them.</summary>
    [Fact]
    public void TheTrapsSpotsAreSpread()
    {
        AiVector[] spots = [.. Enumerable.Range(0, 7).Select(i => TFDemomanClass.TrapSpotAt(TFField.RedFlag, new AiVector(1f, 0f, 0f), i))];
        for (int a = 0; a < spots.Length; a++)
        {
            for (int b = a + 1; b < spots.Length; b++)
                Assert.True(spots[a].DistanceTo(spots[b]) >= TFDemomanClass.TrapSpread - 0.01f, $"spots {a} and {b} {spots[a].DistanceTo(spots[b])} u apart");
        }
    }

    /// <summary>The Trapper defends whatever role is dealt: an attacker's slot lays the trap.</summary>
    [Fact]
    public void TheTrapperDefendsWhateverRoleIsDealt()
    {
        var f = new TFField(seed: SeedDrawing(TFDemomanClass.Trapper), weapons: Weapons) { Hud = TrapperKit() };
        Assert.True(LayFor(f, 20, Lay).Laid > 0);
        Assert.Equal(TFDemomanClass.Trapper, f.Decider.Archetype);
    }

    /// <summary>The Trapper's trap holds more than the Hybrid's (twelve of his fourteen) and, full, he watches it, looking at it.</summary>
    [Fact]
    public void TheTrapperLaysMoreAndWatchesIt()
    {
        var f = new TFField(slot: 3, mates: [1, 2], seed: SeedDrawing(TFDemomanClass.Trapper), weapons: Weapons) { Hud = TrapperKit() };
        (int laid, IntentCommand last) = LayFor(f, 120, Lay);
        Assert.Equal(12, laid);
        Assert.Equal(("watching a trap", LookKind.Point), (f.Decider.Doing.Verdict, last.Look.Kind));
    }

    /// <summary>The own point, moved 1,500 u up the field (the enemy took the first: the next is in play).</summary>
    private static ObjectiveKnowledge MovedPoint() => new([new Objective(21, ObjectiveKind.Capture, TFField.Red, 1 << TFField.Blue, ObjectiveState.Open,
                                                                         TFField.RedFlag + new AiVector(1500f, 0f, 0f))]);

    /// <summary>
    /// The Trapper's objective moved after his full trap (12 of 14 out at the first point): at the next he lays a trap again,
    /// the launcher's cap recycling the old trap's oldest, not two stickies and then nothing (review finding 3).
    /// </summary>
    [Fact]
    public void ATrapperLaysAgainWhenTheObjectiveMoves()
    {
        var f = new TFField(slot: 3, mates: [1, 2], seed: SeedDrawing(TFDemomanClass.Trapper), weapons: Weapons) { Hud = TrapperKit() };
        Assert.Equal(12, LayForAt(f, 120, Lay, OwnPoint()).Laid);
        AiVector there = Lay + new AiVector(1500f, 0f, 0f);
        Assert.True(LayForAt(f, 120, there, MovedPoint()).Laid >= 6, "laid too few at the moved point");
    }

    /// <summary>With stickies of his trap out and an enemy in sight 700 u off, beyond it, the Trapper takes out his pipes.</summary>
    [Fact]
    public void ATrapperWithStickiesOutFightsWithPipes()
    {
        var f = new TFField(slot: 3, mates: [1, 2], seed: SeedDrawing(TFDemomanClass.Trapper), weapons: Weapons) { Hud = TrapperKit() };
        Assert.True(LayForAt(f, 20, Lay, OwnPoint()).Laid > 0);
        // Within the hands' second of spacing after the last lay.
        IntentCommand[] decides = [.. Enumerable.Range(0, 15).Select(_ => f.StepDrawn(Self(Lay, Stickies, 6), OwnPoint(),
                                                                                         Drawn(Enemy, Lay + new AiVector(700f, 0f, 0f), TFField.Blue)))];
        Assert.Contains(decides, i => (i.Action.Kind, i.Action.WeaponSlot) == (ActionKind.SelectWeapon, EngineBotSelf.ByKind + Pipes));
    }

    /// <summary>
    /// The in-view launcher: with an enemy by an armed sticky and the bot looking away, the view goes to the sticky first and
    /// nothing is detonated; looking at it, the detonation goes.
    /// </summary>
    [Fact]
    public void AnInViewLauncherLooksBeforeItDetonates()
    {
        TFField Laid(float yaw, out IntentCommand last)
        {
            var f = new TFField(weapons: Weapons) { Hud = TrapperKit() };
            AiVector landing = Mid + new AiVector(300f, 0f, 0f);
            ActorFacts enemy = Drawn(Enemy, landing, TFField.Blue);
            f.StepDrawn(Self(Mid, Stickies, 8), null, enemy);
            last = default;
            for (int n = 0; n < 25; n++)
                last = f.StepSeeing(Self(Mid, Stickies, 7) with { Eye = new AiAngles(0f, yaw, 0f) }, null, [Sticky(77, landing, TFField.Red)], null, enemy);
            return f;
        }

        TFField away = Laid(180f, out IntentCommand turned);
        Assert.Equal(0, away.Decider.ClassCounters().Single(p => p.Key == TFDemomanClass.Detonations).Value);
        Assert.Equal(LookKind.Point, turned.Look.Kind);
        TFField facing = Laid(0f, out _);
        Assert.Equal(1, facing.Decider.ClassCounters().Single(p => p.Key == TFDemomanClass.Detonations).Value);
    }
}
