using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Tests.Jump;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Soldier's rocket jump as the tree decides it (plan_bots_teams_tf2_part_g G.5 step 6): when a
/// jump is allowed, what is asked of the level's book of jumps, and that the jump begun is the
/// book's. On the synthetic field (<see cref="TFField"/>) with a book that answers what the fact
/// says (<see cref="TFJumpAdvice"/>); and, where the fact is about the recipe itself, with the real
/// desk and planner over a hall (<see cref="JumpPlannerTests"/>).
/// </summary>
public sealed class TFJumpRuleTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Start = new(300f, 256f, 0f), East = new(240f, 0f, 0f);
    private const float Floor = TFDecider.RocketJumpHealthFloor;

    /// <summary>A Soldier at the west end running east toward the enemy's flag, 3,500 u away.</summary>
    private static SelfState Running(int health = 200, int clip = 4, AiVector? velocity = null, SelfFlags extra = SelfFlags.None, TFClass playerClass = TFClass.Soldier)
        => TFField.Self(Start, health: health, clip: clip, velocity: velocity ?? East, playerClass: playerClass, extra: extra);

    /// <summary>The field with a book of jumps, the bot one that never glances: what a glance does to a jump is its own facts'.</summary>
    private static TFField Field(SourceSharp.Ai.TF2.Jump.IJumpAdvisor? jumps, int slot = 2) => new(slot: slot, tune: p => p with { GlanceRate = 0f }, jumps: jumps);

    private static List<IntentCommand> Decides(TFField f, Func<SelfState> self, int decides, ObjectiveKnowledge? objectives = null)
        => [.. Enumerable.Range(0, decides).Select(_ => f.Step(self(), objectives ?? TFField.Ctf())).ToList()];

    private static int Jumps(TFField f, Func<SelfState> self, int decides = 20, ObjectiveKnowledge? objectives = null)
        => Decides(f, self, decides, objectives).Count(i => f.BeginsJump(i));

    [Fact]
    public void ASoldierOnItsWayBeginsTheJumpTheBookAdvisesAndTheOneShotCarriesItsNumber()
    {
        var advice = new TFJumpAdvice(TFJumpAdvice.Plan(id: 1234));
        var f = Field(advice);
        IntentCommand jump = f.Step(Running(), TFField.Ctf());
        Assert.Equal(ActionKind.Ability, jump.Action.Kind);
        Assert.True(f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(jump.Action.WeaponSlot), out AbilityRow row));
        Assert.Equal((TFAbilities.RocketJump, AbilityRun.Program, 1234), (row.Name, row.Run, AbilityCall.Argument(jump.Action.WeaponSlot)));
        Assert.True(jump.Action.OneShotSeq > 0);
        Assert.Equal((1, 1234, "rocket jump"), (f.Decider.RocketJumps, f.Decider.LastJump?.Id, f.Decider.Doing.Verdict));
        // Once: the decides that follow ask for no other.
        Assert.DoesNotContain(Decides(f, () => Running(), 20), i => f.BeginsJump(i));
    }

    [Fact]
    public void TheStretchAskedAboutIsTheBotsPlaceItsHeadingAndItsGoal()
    {
        var advice = new TFJumpAdvice(null);
        var f = Field(advice);
        f.Step(Running(velocity: new AiVector(120f, -160f, 30f)), TFField.Ctf());
        JumpStretchAsk ask = Assert.Single(advice.Asks);
        Assert.Equal((Start, TFField.BlueFlag, f.Tick), (ask.Origin, ask.Goal, ask.At.Value));
        // The heading is the way it moves over the ground, a unit vector.
        Assert.Equal(0.6f, ask.Heading.X, 3);
        Assert.Equal(-0.8f, ask.Heading.Y, 3);
        Assert.Equal(0f, ask.Heading.Z);
    }

    [Fact]
    public void WithNoJumpAdvisedThereIsNoneAndTheStretchIsAskedAboutEachDecide()
    {
        var advice = new TFJumpAdvice(null);
        Assert.Equal(0, Jumps(Field(advice), () => Running()));
        Assert.Equal(20, advice.Asks.Count);
    }

    [Fact]
    public void ATreeGivenNoBookNeverJumps()
        => Assert.Equal(0, Jumps(Field(null), () => Running()));

    [Fact]
    public void ARocketJumpWaitsItsCooldownBeforeTheNext()
    {
        var f = Field(new TFJumpAdvice());
        int decides = (int)(TFDecider.RocketJumpEverySeconds / 0.015f / DecideSchedule.Period);
        Assert.Equal(1, Jumps(f, () => Running(), decides - 1));
        Assert.Equal(1, Jumps(f, () => Running(), 3));
    }

    [Fact]
    public void NoJumpWhoseBlastWouldTakeTheBotUnderItsHealthFloor()
    {
        // The plan's blast costs 30: with a point less than the floor and that, none; with exactly that, the jump.
        Assert.Equal(0, Jumps(Field(new TFJumpAdvice()), () => Running(health: (int)(Floor + TFJumpAdvice.Cost) - 1)));
        Assert.Equal(1, Jumps(Field(new TFJumpAdvice()), () => Running(health: (int)(Floor + TFJumpAdvice.Cost))));
    }

    [Fact]
    public void ABotUnderItsHealthFloorDoesNotAsk()
    {
        var advice = new TFJumpAdvice();
        Assert.Equal(0, Jumps(Field(advice), () => Running(health: (int)Floor - 1)));
        Assert.Empty(advice.Asks);
    }

    [Fact]
    public void NoJumpWithoutARocketToSpare()
    {
        var advice = new TFJumpAdvice();
        Assert.Equal(0, Jumps(Field(advice), () => Running(clip: 0)));
        Assert.Equal(0, Jumps(Field(advice), () => Running(clip: TFDecider.RocketJumpRockets - 1)));
        Assert.Empty(advice.Asks);
        Assert.Equal(1, Jumps(Field(advice), () => Running(clip: TFDecider.RocketJumpRockets)));
    }

    [Fact]
    public void NoJumpWhileTheLauncherIsBeingReloaded()
    {
        var advice = new TFJumpAdvice();
        Assert.Equal(0, Jumps(Field(advice), () => Running(extra: SelfFlags.Reloading)));
        Assert.Empty(advice.Asks);
    }

    [Fact]
    public void NoJumpOffTheGround()
    {
        var advice = new TFJumpAdvice();
        Assert.Equal(0, Jumps(Field(advice), () => Running() with { Flags = SelfFlags.Alive }));
        Assert.Empty(advice.Asks);
    }

    [Fact]
    public void NoJumpStandingOrAtASlowWalk()
    {
        var advice = new TFJumpAdvice();
        Assert.Equal(0, Jumps(Field(advice), () => Running(velocity: new AiVector(TFDecider.RocketJumpSpeed - 5f, 0f, 0f))));
        Assert.Empty(advice.Asks);
        Assert.Equal(1, Jumps(Field(advice), () => Running(velocity: new AiVector(TFDecider.RocketJumpSpeed + 5f, 0f, 0f))));
    }

    [Fact]
    public void NoJumpWithAnEnemyInSight()
    {
        // A defender chasing the enemy carrying its flag (a run, which outranks the fight) with another enemy in sight.
        var advice = new TFJumpAdvice();
        var f = Field(advice, slot: 3);
        ObjectiveKnowledge carried = TFField.Ctf(red: ObjectiveState.Carried, redAt: new AiVector(3700f, 256f, 0f), redCarrier: 9);
        List<IntentCommand> intents = [.. Enumerable.Range(0, 20).Select(_ => f.Step(Running(), carried, (Enemy, new AiVector(2500f, 256f, 0f), AiVector.Zero, TFField.Blue)))];
        Assert.Equal(MoveKind.Follow, intents[^1].Move.Kind);
        Assert.Equal(0, intents.Count(i => f.BeginsJump(i)));
        Assert.Empty(advice.Asks);
        // The control: the same chase with nobody in sight jumps.
        Assert.Equal(1, Jumps(Field(new TFJumpAdvice(), slot: 3), () => Running(), objectives: carried));
    }

    [Fact]
    public void NoJumpCarryingTheFlag()
    {
        var advice = new TFJumpAdvice();
        var f = Field(advice);
        ObjectiveKnowledge carrying = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: new AiVector(3000f, 256f, 0f), blueCarrier: f.Bot.Slot);
        List<IntentCommand> intents = Decides(f, () => TFField.Self(new AiVector(3000f, 256f, 0f), velocity: new AiVector(-240f, 0f, 0f)), 20, carrying);
        Assert.Equal((MoveKind.Follow, TFField.RedZone), (intents[^1].Move.Kind, intents[^1].Move.Corner0));
        Assert.Equal(0, intents.Count(i => f.BeginsJump(i)));
        Assert.Empty(advice.Asks);
    }

    [Fact]
    public void AClassThatIsNoSoldierDoesNotAsk()
    {
        var advice = new TFJumpAdvice();
        Assert.Equal(0, Jumps(Field(advice), () => Running(playerClass: TFClass.Scout)));
        Assert.Empty(advice.Asks);
    }

    /// <summary>A bot that glances twice a second (so about half its time is spent looking aside), on a run where the book always has a jump.</summary>
    private static (List<IntentCommand> Intents, TFField Field) GlancingRun(int decides = 600)
    {
        var f = new TFField(tune: p => p with { GlanceRate = 2f }, jumps: new TFJumpAdvice());
        return (Decides(f, () => Running(), decides), f);
    }

    [Fact]
    public void NoJumpBeginsInTheMiddleOfAGlance()
    {
        (List<IntentCommand> intents, TFField f) = GlancingRun();
        int[] jumps = [.. Enumerable.Range(0, intents.Count).Where(i => f.BeginsJump(intents[i]))];
        Assert.True(jumps.Length >= 3 && f.Decider.Glances >= 10, $"the control: {jumps.Length} jumps and {f.Decider.Glances} glances on the run");
        // A glance is held 0.4 s: the decide it begins on and the four after. The decides looking aside right before a jump's are
        // whole glances, none cut short by the jump.
        int held = (int)(PathGlance.HoldSeconds / 0.015f) / DecideSchedule.Period + 1;
        Assert.Equal(5, held);
        int waited = 0;
        foreach (int jump in jumps)
        {
            int aside = 0;
            while (jump - 1 - aside >= 0 && intents[jump - 1 - aside].Look.Kind == LookKind.Glance)
                aside++;
            Assert.True(aside % held == 0, $"the jump at decide {jump} began {aside % held} decides into a glance");
            waited += aside > 0 ? 1 : 0;
        }

        // The stimulus reached the rule: some jump did come right after a glance, having waited it out.
        Assert.True(waited >= 1, "no jump on the run was due while a glance was held");
    }

    [Fact]
    public void NoGlanceBeginsWhileAJumpsApproachHasTheBot()
    {
        (List<IntentCommand> intents, TFField f) = GlancingRun();
        int busy = (int)(TFDecider.RocketJumpBusySeconds / (DecideSchedule.Period * 0.015f));
        int[] jumps = [.. Enumerable.Range(0, intents.Count).Where(i => f.BeginsJump(intents[i]))];
        foreach (int jump in jumps)
        {
            for (int i = jump; i < Math.Min(intents.Count, jump + busy); i++)
                Assert.Equal(LookKind.Path, intents[i].Look.Kind);
        }

        // The control: between two jumps it glances as anyone does.
        Assert.Contains(intents.Skip(jumps[0] + busy).Take(jumps[1] - jumps[0] - busy), i => i.Look.Kind == LookKind.Glance);
    }

    /// <summary>The bot as its own HUD has it a jump in: off the ground or on it, an enemy in sight of it.</summary>
    private static IntentCommand InAJump(TFField f, bool grounded)
        => f.Step(Running() with { Flags = grounded ? SelfFlags.Alive | SelfFlags.Grounded : SelfFlags.Alive }, TFField.Ctf(),
                  (Enemy, Start + new AiVector(600f, 0f, 0f), AiVector.Zero, TFField.Blue));

    [Fact]
    public void WhileAJumpHasTheBotItsHandsStayOffTheTrigger()
    {
        var f = Field(new TFJumpAdvice());
        Assert.Equal(ActionKind.Ability, f.Step(Running(), TFField.Ctf()).Action.Kind);
        int approach = (int)(TFDecider.RocketJumpBusySeconds / (DecideSchedule.Period * 0.015f));
        // The approach, on the ground, then three seconds in the air: an enemy in sight all the while, and no shot.
        for (int i = 0; i < approach - 1; i++)
            Assert.Equal(ActionKind.None, InAJump(f, grounded: true).Action.Kind);
        for (int i = 0; i < 33; i++)
            Assert.Equal(ActionKind.None, InAJump(f, grounded: false).Action.Kind);
        // Down again: the next decide but one fires.
        InAJump(f, grounded: true);
        Assert.Equal(ActionKind.Fire, InAJump(f, grounded: true).Action.Kind);
    }

    [Fact]
    public void ABotStillOnTheGroundAfterTheApproachHasItsHandsBack()
    {
        var f = Field(new TFJumpAdvice());
        Assert.Equal(ActionKind.Ability, f.Step(Running(), TFField.Ctf()).Action.Kind);
        int approach = (int)(TFDecider.RocketJumpBusySeconds / (DecideSchedule.Period * 0.015f));
        for (int i = 0; i < approach + 1; i++)
            InAJump(f, grounded: true);
        Assert.Equal(ActionKind.Fire, InAJump(f, grounded: true).Action.Kind);
    }

    [Fact]
    public void ADeathInAJumpGivesTheNextLifeItsHandsAtOnce()
    {
        var f = Field(new TFJumpAdvice());
        Assert.Equal(ActionKind.Ability, f.Step(Running(), TFField.Ctf()).Action.Kind);
        InAJump(f, grounded: false);
        f.Step(Running() with { Flags = SelfFlags.None }, TFField.Ctf());
        // Alive again, in the air of a spawn's drop, an enemy in sight: nothing of the old jump holds its hands, on this decide or the next.
        Assert.Equal(ActionKind.Fire, InAJump(f, grounded: false).Action.Kind);
        Assert.Equal(ActionKind.Fire, InAJump(f, grounded: false).Action.Kind);
    }

    [Fact]
    public void AJumpThatNeverComesDownStillLetsTheHandsGoInTheEnd()
    {
        var f = Field(new TFJumpAdvice());
        Assert.Equal(ActionKind.Ability, f.Step(Running(), TFField.Ctf()).Action.Kind);
        int most = (int)(TFDecider.RocketJumpMostSeconds / (DecideSchedule.Period * 0.015f));
        for (int i = 0; i < most - 1; i++)
            Assert.Equal(ActionKind.None, InAJump(f, grounded: false).Action.Kind);
        // Still off the ground past the longest a jump is taken to last: the hands are back within two decides.
        InAJump(f, grounded: false);
        InAJump(f, grounded: false);
        Assert.Equal(ActionKind.Fire, InAJump(f, grounded: false).Action.Kind);
    }

    // ------------------------------------------------------------------ with the real desk and planner over a hall

    /// <summary>The hall's far end as the enemy's flag: the bot's goal is the open hall's plan's.</summary>
    private static ObjectiveKnowledge HallCtf => TFField.Ctf(blueAt: JumpPlannerTests.Along().Goal);

    private static SelfState InTheHall(float x, int health = 200) => TFField.Self(JumpPlannerTests.At(x), health: health, velocity: East);

    private static JumpDesk Desk(VoxelColumns hall, out JumpPlanner planner, JumpPlanSettings? settings = null, float? walkSpeed = null)
    {
        var desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally());
        planner = JumpPlannerTests.Planner(hall, settings, walkSpeed);
        desk.Open(planner);
        return desk;
    }

    [Fact]
    public void ADecideNeverFliesOrSearchesAndTheRecipeBegunIsTheOneFoundForTheStretch()
    {
        JumpDesk desk = Desk(JumpPlannerTests.Hall(), out JumpPlanner planner);
        var f = Field(desk.Book);
        // The bot asks from where the open hall's plan was asked from, and walks on: nothing is known, nothing is flown.
        Assert.Equal(ActionKind.None, f.Step(InTheHall(160f), HallCtf).Action.Kind);
        Assert.Equal(ActionKind.None, f.Step(InTheHall(182f), HallCtf).Action.Kind);
        Assert.Equal((0L, 1), (planner.Flights, desk.Book.Counts.Waiting));

        // The worker's turn; the next decide begins the jump, and it is the plan the simulator found for that stretch.
        Assert.Equal(1, desk.Work());
        IntentCommand jump = f.Step(InTheHall(204f), HallCtf);
        Assert.Equal(ActionKind.Ability, jump.Action.Kind);
        JumpPlan? begun = desk.Book.Plan(AbilityCall.Argument(jump.Action.WeaponSlot));
        Assert.Equal(JumpPlannerTests.OpenPlan.Recipe, begun?.Recipe);
        Assert.Same(begun, f.Decider.LastJump);
    }

    /// <summary>A bot walking the hall from its west end, deciding every period, the worker working between decides: how many jumps it began.</summary>
    private static int JumpsAlong(JumpDesk desk, int health = 200)
    {
        var f = Field(desk.Book);
        int jumps = 0;
        for (float x = 160f; x < 2400f; x += 240f * DecideSchedule.Period * 0.015f)
        {
            jumps += f.BeginsJump(f.Step(InTheHall(x, health), HallCtf)) ? 1 : 0;
            desk.Work();
        }

        return jumps;
    }

    [Fact]
    public void AlongTheOpenHallTheWalkingBotBeginsAJump()
        => Assert.True(JumpsAlong(Desk(JumpPlannerTests.Hall(), out _)) >= 1);

    [Fact]
    public void UnderACeilingTheFlightWouldMeetTheBotBeginsNone()
    {
        // The slab of JumpPlannerTests: a standing body walks under it, the flight would not fit.
        JumpDesk desk = Desk(JumpPlannerTests.Hall(global::TF2.Shared.Movement.TFViewVectors.Params.HullMax.Z + 46f), out _);
        Assert.Equal(0, JumpsAlong(desk));
        Assert.True(desk.Book.Counts.Refused >= 1 && desk.Book.Counts.Planned == 0, "the control: the stretch was asked about and answered none");
    }

    [Fact]
    public void TowardAWallTheBotBeginsNone()
    {
        // A wall across the hall 700 u on, the goal just before it.
        VoxelColumns hall = JumpPlannerTests.Hall(solid: (x, _) => x * 16f >= 860f);
        JumpDesk desk = Desk(hall, out _);
        var f = Field(desk.Book);
        int jumps = 0;
        for (float x = 160f; x < 760f; x += 21.6f)
        {
            jumps += f.BeginsJump(f.Step(InTheHall(x), TFField.Ctf(blueAt: JumpPlannerTests.At(820f)))) ? 1 : 0;
            desk.Work();
        }

        Assert.Equal(0, jumps);
        Assert.True(desk.Book.Refused(JumpRefusal.Short) >= 1, "the control: the stretch was asked about and was too short");
    }

    [Fact]
    public void WhereTheSimulatorsBestIsNoFasterThanWalkingTheBotBeginsNone()
    {
        JumpDesk desk = Desk(JumpPlannerTests.Hall(), out _, walkSpeed: 20_000f);
        Assert.Equal(0, JumpsAlong(desk));
        Assert.True(desk.Book.Refused(JumpRefusal.NotFaster) >= 1, "the control: the stretch was searched and the walk won");
    }

    [Fact]
    public void WithTheHealthForTheFloorAndNotForTheBlastTheBotBeginsNone()
    {
        // Five points over the floor: the bot asks, a jump is advised, and no blast costs that little.
        JumpDesk desk = Desk(JumpPlannerTests.Hall(), out _);
        Assert.True(JumpPlannerTests.OpenPlan.Health > 5f, "the control: the hall's jump costs more than five points");
        Assert.Equal(0, JumpsAlong(desk, health: (int)Floor + 5));
        Assert.True(desk.Book.Counts.Advised >= 1, "the control: a jump was advised and not taken");
    }
}
