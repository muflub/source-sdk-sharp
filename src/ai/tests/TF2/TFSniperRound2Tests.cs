using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Shared;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Sniper's round 2 leftovers (round2.md §5 "Sniper", the round 1 code review's C7, C9 and C10): the attacker of a hit,
/// not the bot's target, says whether the hit came from range; a far target is scoped only standing still; the SMG in hand
/// is filled in a lull before the rifle comes back.
/// </summary>
public sealed class TFSniperRound2Tests
{
    private static readonly ActorId Soldier = new(5, 0x4000_0001), Unseen = new(9, 0x4000_0001);
    private static readonly AiVector Start = new(1500f, 256f, 0f);

    private static TFField Field() => new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Marksman), weapons: TFSniperPlayTests.Weapons);

    private static long Counter(TFField f, string name) => f.Decider.ClassCounters().Single(kv => kv.Key == name).Value;

    private static bool IsZoomPress(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) && row.Name == TFSniperClass.Zoom;

    /// <summary>Walks the bot to its spot with nobody in sight, standing it where it walks; true once it holds the line.</summary>
    private static AiVector ToTheSpot(TFField f)
    {
        AiVector at = Start;
        for (int n = 0; n < 12; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), []);
            if (f.Decider.Doing.Verdict == "holding a sight line")
                return at;
            if (f.Decider.Doing.HasGoal)
                at = f.Decider.Doing.Goal;
        }

        throw new InvalidOperationException("never held a spot");
    }

    /// <summary>
    /// Held at its spot and scoped on a Soldier it sees, the Sniper is hit by someone it does not see (a far Sniper): it leaves the
    /// line, though its own target is in sight (the code review's C7: the rule tested the target, not the attacker).
    /// </summary>
    [Fact]
    public void HitByAnUnseenShooterWhileItsTargetIsInSightItLeaves()
    {
        TFField f = Field();
        AiVector at = ToTheSpot(f);
        var soldier = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1400f, 0f, 0f), TFClass.Soldier);
        for (int n = 0; n < 4; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
            f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), [], soldier);
        }

        long before = Counter(f, "sniper.relocations");
        f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
        f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(),
                      [AiEvent.Damaged(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Unseen, 50f, new AiVector(-1f, 0f, 0f), 0)], soldier);
        Assert.Equal(before + 1, Counter(f, "sniper.relocations"));
    }

    /// <summary>Held at its spot and hit by the Soldier it sees and is shooting at, it holds the line: a hit from in front is no reason to move.</summary>
    [Fact]
    public void HitByTheSoldierItSeesItHoldsTheLine()
    {
        TFField f = Field();
        AiVector at = ToTheSpot(f);
        var soldier = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1400f, 0f, 0f), TFClass.Soldier);
        for (int n = 0; n < 4; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
            f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), [], soldier);
        }

        long before = Counter(f, "sniper.relocations");
        f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
        f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(),
                      [AiEvent.Damaged(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Soldier, 50f, new AiVector(1f, 0f, 0f), 0)], soldier);
        Assert.Equal(before, Counter(f, "sniper.relocations"));
    }

    /// <summary>
    /// A far enemy in sight while the bot walks (hurt, to a pack): no scope press on any decide that moves; with full health
    /// the same enemy is fought standing still, and scoped (the code review's C9: the scope came up on the walk, a crawl at 80 u/s).
    /// </summary>
    [Fact]
    public void AFarTargetIsScopedOnlyStandingStill()
    {
        (int walking, int walkingPresses, _) = Run(health: 30);
        (_, _, int holdingPresses) = Run(health: 125);
        Assert.True(walking > 0 && holdingPresses > 0, $"walking {walking} holding presses {holdingPresses}");
        Assert.Equal(0, walkingPresses);
    }

    private static (int Walking, int WalkingPresses, int HoldingPresses) Run(int health)
    {
        TFField f = Field();
        int walkingWithTarget = 0, walkingPresses = 0, holdingPresses = 0;
        AiVector at = Start;
        for (int n = 0; n < 40; n++)
        {
            var enemy = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1600f, 0f, 0f), TFClass.Soldier);
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            SelfState me = TFField.Self(at, health: health, clip: -1, reserve: 25, weapon: TFSniperPlayTests.Rifle, playerClass: TFClass.Sniper);
            IntentCommand i = f.StepHearing(me, TFField.Ctf(), [], enemy);
            bool press = IsZoomPress(f, i);
            if (i.Move.Kind == MoveKind.Hold)
                holdingPresses += press ? 1 : 0;
            else
                (walkingWithTarget, walkingPresses) = (walkingWithTarget + 1, walkingPresses + (press ? 1 : 0));
        }

        return (walkingWithTarget, walkingPresses, holdingPresses);
    }

    /// <summary>In a lull with the SMG in hand and its clip part spent, the Sniper reloads it before taking the rifle back (the code review's C10).</summary>
    [Fact]
    public void InALullThePartSpentSmgIsReloadedFirst()
    {
        TFField f = Field();
        SelfState me = TFField.Self(Start, health: 125, clip: 10, reserve: 50, weapon: TFSniperPlayTests.Smg, playerClass: TFClass.Sniper);
        var seen = new List<ActionKind>();
        for (int n = 0; n < 6; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            seen.Add(f.StepHearing(me, TFField.Ctf(), []).Action.Kind);
        }

        Assert.Contains(ActionKind.Reload, seen);
        Assert.DoesNotContain(ActionKind.SelectWeapon, seen);
    }

    /// <summary>The control: with the SMG full, the lull takes the rifle back.</summary>
    [Fact]
    public void InALullAFullSmgGivesWayToTheRifle()
    {
        TFField f = Field();
        SelfState me = TFField.Self(Start, health: 125, clip: 25, reserve: 50, weapon: TFSniperPlayTests.Smg, playerClass: TFClass.Sniper);
        var seen = new List<ActionKind>();
        for (int n = 0; n < 6; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            seen.Add(f.StepHearing(me, TFField.Ctf(), []).Action.Kind);
        }

        Assert.DoesNotContain(ActionKind.Reload, seen);
        Assert.Contains(ActionKind.SelectWeapon, seen);
    }
}
