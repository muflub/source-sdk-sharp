using System.Reflection;
using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// The HL2MP trees (plan_hl2mp_ai §9 Phase 5 step 3, §5.4, §8.2's ammo and
/// Headhunter facts): push on an empty clip, hold on a full one, the bot's own
/// reloads, surviving to a health kit, Headhunter's leader, and Discipline 0
/// never trusting the estimate.
/// </summary>
public sealed class Hl2mpTreeTests
{
    private static readonly AiVector Near = new(150f, 0f, 0f);

    private static SkillProfile Disciplined(string preset, int discipline)
    {
        SkillProfile p = Duel.Preset(preset);
        return p.With(p.IndexOf("discipline"), discipline);
    }

    /// <summary>An opponent heard and seen to spend all 45 of an SMG1's rounds, close by: the bot pushes.</summary>
    [Fact]
    public void AnOpponentSeenToEmptyItsClipIsPushed()
    {
        var d = new Duel(Disciplined("hard", 10));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.SeeAt(Duel.Opponent, Near));
        IntentCommand i = d.Step(d.Self(), [(Duel.Opponent, Near)], d.Shots(Duel.Opponent, 45));

        Assert.Equal(("Fight", "push"), (d.Decider.Doing.Branch, d.Decider.Doing.Verdict));
        Assert.Equal((MoveKind.Follow, Near), (i.Move.Kind, i.Move.Corner0));
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
        Assert.True(i.Reason.AmmoTerm >= 0.9f, $"the reason carries the ammo term: {i.Reason.AmmoTerm}");
    }

    [Fact]
    public void AnOpponentWithAFullClipIsHeldAgainst()
    {
        var d = new Duel(Disciplined("hard", 10));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.SeeAt(Duel.Opponent, Near));
        IntentCommand i = d.Step(d.Self(), [(Duel.Opponent, Near)], d.Shots(Duel.Opponent, 3));

        Assert.Equal(("Fight", "hold"), (d.Decider.Doing.Branch, d.Decider.Doing.Verdict));
        Assert.NotEqual(MoveKind.Follow, i.Move.Kind);
    }

    /// <summary>§5.4: at Discipline 0 the estimate is ignored, so an empty clip is no push.</summary>
    [Fact]
    public void AtDisciplineZeroAnEmptyClipIsNoPush()
    {
        var d = new Duel(Disciplined("hard", 0));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.SeeAt(Duel.Opponent, Near));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.Shots(Duel.Opponent, 45));

        Assert.NotEqual("push", d.Decider.Doing.Verdict);
    }

    /// <summary>The bot's own reload in sight of an opponent: it backs away from it and does not fire.</summary>
    [Fact]
    public void ABotReloadingInSightBacksAwayAndHoldsFire()
    {
        var d = new Duel(Duel.Preset("hard"));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.SeeAt(Duel.Opponent, Near));
        IntentCommand i = d.Step(d.Self(clip: 0, extra: SelfFlags.Reloading), [(Duel.Opponent, Near)]);

        Assert.Equal("retreat (own reload)", d.Decider.Doing.Verdict);
        Assert.Equal(MoveKind.Strafe, i.Move.Kind);
        Assert.True(i.Move.Direction.X < 0f, $"away from the opponent at +x: {i.Move.Direction}");
        Assert.Equal(ActionKind.None, i.Action.Kind);
    }

    [Fact]
    public void AnEmptyClipInAFightIsReloaded()
    {
        var d = new Duel(Duel.Preset("hard"));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.SeeAt(Duel.Opponent, Near));
        IntentCommand i = d.Step(d.Self(clip: 0), [(Duel.Opponent, Near)]);

        Assert.Equal((ActionKind.Reload, true), (i.Action.Kind, i.Action.OneShotSeq > 0));
    }

    /// <summary>Under Nerve's retreat health with an opponent in sight, the bot goes for the health it knows.</summary>
    [Fact]
    public void AHurtBotUnderFireGoesForTheHealthKit()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.SeeAt(Duel.Opponent, Near));
        IntentCommand i = d.Step(d.Self(health: 10), [(Duel.Opponent, Near)]);

        Assert.Equal("Survive", d.Decider.Doing.Branch);
        Assert.Equal((MoveKind.Retreat, Duel.Kit), (i.Move.Kind, i.Move.Corner0));
    }

    private static ActorId Target(int headhunter, bool sighted)
    {
        SkillProfile p = Duel.Preset("normal");
        var d = new Duel(p.With(p.IndexOf("headhunter"), headhunter));
        var leaderAt = new AiVector(1200f, 0f, 0f);
        var lowAt = new AiVector(300f, 100f, 0f);
        (ActorId, AiVector)[] view = sighted ? [(Duel.Opponent, leaderAt), (Duel.Other, lowAt)] : [];
        AiEvent[] seen = [.. d.SeeAt(Duel.Opponent, leaderAt), .. d.SeeAt(Duel.Other, lowAt), d.Row(Duel.Opponent, 9, 1), d.Row(Duel.Other, 0, 3),
                          AiEvent.ScoreboardRow(Duel.Bot, new Tick(6), Duel.Bot, 1, 0, 2)];
        d.Step(d.Self(), [(Duel.Opponent, leaderAt), (Duel.Other, lowAt)], seen);
        if (!sighted)
        {
            d.Step(d.Self(), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, leaderAt, AiVector.Zero),
                   AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Other, lowAt, AiVector.Zero));
            d.Step(d.Self(), []);
        }

        d.Step(d.Self(), view);
        return d.Decider.Doing.Target;
    }

    /// <summary>§8.2: with Headhunter at 10 a bot picks the scoreboard's leader over a closer low scorer; at 0 the closer one.</summary>
    [Fact]
    public void HeadhunterTenPicksTheLeaderAndZeroTheCloserOne()
    {
        Assert.Equal(Duel.Opponent, Target(10, sighted: true));
        Assert.Equal(Duel.Other, Target(0, sighted: true));
    }

    /// <summary>§8.2: with nobody in sight a Headhunter hunts the leader's believed place.</summary>
    [Fact]
    public void WithNobodyInSightAHeadhunterHuntsTheLeader()
    {
        Assert.Equal(Duel.Opponent, Target(10, sighted: false));
        Assert.Equal(Duel.Other, Target(0, sighted: false));
    }

    [Fact]
    public void TheScoreboardTermIsInTheReason()
    {
        SkillProfile p = Duel.Preset("normal");
        var d = new Duel(p.With(p.IndexOf("headhunter"), 10));
        var at = new AiVector(400f, 0f, 0f);
        IntentCommand i = d.Step(d.Self(), [(Duel.Opponent, at)], [.. d.SeeAt(Duel.Opponent, at), d.Row(Duel.Opponent, 5, 1), d.Row(Duel.Other, 0, 3)]);
        Assert.Equal(1f, i.Reason.ScoreboardTerm);
        Assert.Equal(1f, d.Decider.Doing.ScoreboardTerm);
    }

    [Theory]
    [InlineData(1, 4, 1f)]
    [InlineData(4, 4, 0f)]
    [InlineData(2, 3, 0.5f)]
    [InlineData(0, 4, 0f)]
    public void TheRankBonusFallsLinearlyFromTheLeader(int rank, int rows, float bonus)
        => Assert.Equal(bonus, Hl2mpDecider.RankBonus(rank, rows));

    /// <summary>The trees read only the core (§4.4): no method of the Trees namespace calls into the engine, the server or the ABI.</summary>
    [Fact]
    public void TheTreesCallNothingButTheCoreAndTheFramework()
    {
        Assembly binding = typeof(Hl2mpDecider).Assembly;
        IReadOnlyCollection<(string Caller, string Callee)> calls = SourceSharp.Tests.IlCallCensus.Calls(
            m => m.DeclaringType?.Assembly is { } a && a != typeof(Brain).Assembly && a != binding && !a.GetName().Name!.StartsWith("System", StringComparison.Ordinal)
                 && a.GetName().Name != "mscorlib",
            binding);
        string[] fromTrees = [.. calls.Where(c => c.Caller.StartsWith("SourceSharp.Ai.Hl2mp.Trees.", StringComparison.Ordinal)).Select(c => $"{c.Caller} -> {c.Callee}")];
        Assert.Empty(fromTrees);
        Assert.Contains(calls, c => !c.Caller.StartsWith("SourceSharp.Ai.Hl2mp.Trees.", StringComparison.Ordinal));   // the control: the census sees the binding's engine calls
    }

    /// <summary>
    /// A weapon the bot found empty in its hands is not asked for again while
    /// it stays empty: the HUD shows it so. Before, the decider took every
    /// weapon not in hand for loaded, so a bot whose SMG1 ran dry (the game
    /// switching it to the pistol) asked for the SMG1 back every second, and
    /// the game switched it away again (live: Barney flipping pistol and SMG1
    /// for minutes in the live gate's bots step).
    /// </summary>
    [Fact]
    public void AWeaponFoundEmptyIsNotAskedForAgain()
    {
        var d = new Duel(Duel.Preset("normal"));
        int held = (1 << Duel.Pistol) | (1 << Duel.Smg1);
        d.Step(d.Self(clip: 0, reserve: 0, weapon: Duel.Smg1), [], AiEvent.Holdings(Duel.Bot, new Tick(d.Tick + 6), held));
        int asked = 0;
        for (int i = 0; i < 60; i++)
        {
            IntentCommand c = d.Step(d.Self(clip: 18, reserve: 50, weapon: Duel.Pistol), []);
            if (c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == Hl2mpDecider.SelectByKind + Duel.Smg1)
                asked++;
        }

        Assert.Equal(0, asked);
    }

    /// <summary>The control: a loaded better weapon in the HUD is asked for.</summary>
    [Fact]
    public void ALoadedBetterWeaponIsAskedFor()
    {
        var d = new Duel(Duel.Preset("normal"));
        int held = (1 << Duel.Pistol) | (1 << Duel.Smg1);
        d.Step(d.Self(clip: 18, reserve: 50, weapon: Duel.Pistol), [], AiEvent.Holdings(Duel.Bot, new Tick(d.Tick + 6), held));
        bool asked = false;
        for (int i = 0; i < 60 && !asked; i++)
        {
            IntentCommand c = d.Step(d.Self(clip: 18, reserve: 50, weapon: Duel.Pistol), []);
            asked = c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == Hl2mpDecider.SelectByKind + Duel.Smg1;
        }

        Assert.True(asked, "the SMG1 was never asked for");
    }

    /// <summary>§9 Phase 5's gate: a steady brain step with the trees allocates nothing over 1,000 decides.</summary>
    [Fact]
    public void AThousandSteadyBrainStepsAllocateNothing()
    {
        var d = new Duel(Duel.Preset("hard"));
        d.Step(d.Self(), [(Duel.Opponent, Near)], d.SeeAt(Duel.Opponent, Near));
        for (int i = 0; i < 50; i++)
            d.Step(d.Self(), [(Duel.Opponent, Near)]);

        (ActorId, AiVector)[] view = [(Duel.Opponent, Near)];
        SelfState self = d.Self();
        long allocated = SourceSharp.Ai.Tests.Core.SteadyAllocation.Measure(() => d.Step(self, view), 1000);
        Assert.True(allocated == 0, $"{allocated} bytes over 1,000 steps (the least of the windows)");
    }
}
