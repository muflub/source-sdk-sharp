using SourceSharp.Abi;
using SourceSharp.Ai.Tests.TF2;
using SourceSharp.Ai.TF2.Jump;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The whole of the rocket jump as the game runs it, with the simulator for the game
/// (plan_bots_teams_tf2_part_g G.5 steps 2 and 6): TF2's tree decides every sixth tick from what
/// the bot's own HUD would show, the real applier builds a command a tick, the bot's player is the
/// simulator (<see cref="JumpLive"/>), and the desk's worker is given a turn each decide. Nothing
/// is told to jump: the tree asks the book about the stretch ahead, the worker plans it, a later
/// decide begins it, and the program flies it.
/// </summary>
public class JumpEndToEndTests
{
    // The medium preset's hand (its reaction, turn, overshoot and lag as the game's curves give them).
    private static readonly MotionParams Medium = new(100f, 525f, 16.2f, 174f);

    private sealed record Crossing(JumpLive Live, JumpDesk Desk, TFField Field)
    {
        /// <summary>The commands it took the bot to be at <paramref name="x"/> or past it.</summary>
        public int TicksTo(float x)
        {
            int at = Live.Track.FindIndex(p => p.Origin.X >= x);
            Assert.True(at >= 0, $"the bot never came to {x}: it is at {Live.Now.Origin}");
            return at + 1;
        }
    }

    /// <summary>
    /// A Soldier bot walking the open hall from its west end for the flag at its east end: until it is past
    /// <paramref name="x"/>, or with none given until the first jump it began has ended. How long a hall a jump
    /// crosses is the simulator's to say, so nothing here is a length of it.
    /// </summary>
    private static Crossing Cross(bool jumps, float? x = null, int health = 200)
    {
        VoxelColumns hall = JumpPlannerTests.Hall();
        var desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally());
        desk.Open(JumpPlannerTests.Planner(hall));
        var field = new TFField(preset: "medium", jumps: jumps ? desk.Book : null);
        var live = new JumpLive(hall, new JumpStart(new Vector(60f, JumpPlannerTests.HallY, VoxelMoveWorld.DistEpsilon), Vector.Zero, Vector.Zero, Ducked: false, OnGround: true),
                                Medium, desk: desk);
        ObjectiveKnowledge objectives = TFField.Ctf(blueAt: JumpPlannerTests.Along().Goal);
        float most = JumpPlannerTests.Along().Goal.X - 64f;
        bool Done() => x is { } to ? live.Now.Origin.X >= to : live.Flown.Count > 0;
        for (int ticks = 0; !Done() && live.Now.Origin.X < most && ticks < 4000;)
        {
            JumpPoint now = live.Now;
            SelfState self = TFField.Self(JumpLive.A(now.Origin), health: health, velocity: JumpLive.A(now.Velocity)) with
            {
                Flags = now.OnGround ? SelfFlags.Alive | SelfFlags.Grounded : SelfFlags.Alive,
            };
            IntentCommand intent = field.Step(self, objectives);
            live.Submit(intent);
            for (int i = 0; i < DecideSchedule.Period; i++, ticks++)
                live.Tick(field.Tick + i);
            desk.Work();
        }

        Assert.True(Done(), x is null ? $"the bot began no jump that ended: it is at {live.Now.Origin}" : $"the bot stopped at {live.Now.Origin}");
        return new Crossing(live, desk, field);
    }

    /// <summary>The bot left to itself until its first jump has ended, and one that may not jump walked to where that one came down.</summary>
    private static readonly Lazy<Crossing> Jumped = new(() => Cross(jumps: true));

    private static float Touch => Jumped.Value.Live.Flown[0].Touch.X;

    private static readonly Lazy<Crossing> Walked = new(() => Cross(jumps: false, x: Touch));

    [Fact]
    public void LeftToItselfTheBotAsksPlansBeginsAndFliesAJump()
    {
        Crossing c = Jumped.Value;
        Assert.Equal(1, c.Field.Decider.RocketJumps);
        Assert.Equal(1L, c.Live.Applier.AbilityPrograms);
        JumpFlown first = Assert.Single(c.Live.Flown);
        Assert.Equal((JumpEnd.Landed, true), (first.End, first.Felt));
        // The recipe flown is the book's plan for the stretch, its takeoff where the bot launched.
        JumpPlan plan = c.Field.Decider.LastJump!;
        Assert.Same(c.Desk.Book.Plan(plan.Id), plan);
        Assert.Equal(plan.Recipe with { Takeoff = first.Recipe.Takeoff }, first.Recipe);
        Assert.True(first.Recipe.Takeoff.X >= plan.Recipe.Takeoff.X - 2f && first.Recipe.Takeoff.X <= plan.Recipe.Takeoff.X + plan.Slide + 2f,
                    $"it launched at {first.Recipe.Takeoff.X}, the plan's takeoff is {plan.Recipe.Takeoff.X} with {plan.Slide} of slide");
    }

    [Fact]
    public void TheBotThatJumpsIsWhereItComesDownSoonerThanTheOneThatWalksByMostOfWhatThePlanPromised()
    {
        Crossing jumped = Jumped.Value, walked = Walked.Value;
        Assert.Equal(0, walked.Field.Decider.RocketJumps);
        JumpPlan plan = jumped.Field.Decider.LastJump!;
        float promised = plan.WalkSeconds - plan.Seconds, saved = (walked.TicksTo(Touch) - jumped.TicksTo(Touch)) * TickSeconds;
        Assert.True(promised >= new JumpPlanSettings().MinSavedSeconds, $"the control: the plan promised {promised} s");
        // The plan's seconds are the simulator's own pilot's from the recipe's start; the bot's hand turned its view sooner and ran some of the way backwards.
        Assert.True(saved >= promised * MostOfIt, $"{jumped.TicksTo(Touch) * TickSeconds:F2} s with the jump, {walked.TicksTo(Touch) * TickSeconds:F2} s on foot: {saved:F2} s saved of the {promised:F2} s the plan promised");
    }

    /// <summary>The share of a plan's promised saving a bot's own jump must make.</summary>
    private const float MostOfIt = 0.85f;

    [Fact]
    public void TheJumpItFlewIsCountedAndLandsWhereTheSimulatorSaysThatRecipeDoesFromThatLaunch()
    {
        Crossing c = Jumped.Value;
        JumpTallied tallied = c.Desk.Tally.Last()[0];
        Assert.Equal(JumpEnd.Landed, tallied.Flown.End);
        Assert.True(tallied.Gap is <= 24f, $"the flight came down {tallied.Gap} from the simulator's own for the same recipe and launch");
        Assert.Equal(1, c.Desk.Tally.Counts.LandedNear);
    }

    [Fact]
    public void ABotWithoutTheHealthWalksTheWholeWay()
    {
        Crossing c = Cross(jumps: true, x: Touch, health: (int)global::SourceSharp.Ai.TF2.Trees.TFDecider.RocketJumpHealthFloor + 5);
        Assert.Equal((0, 0L), (c.Field.Decider.RocketJumps, c.Live.Applier.AbilityPrograms));
        Assert.Equal(Walked.Value.TicksTo(Touch), c.TicksTo(Touch));
    }
}
