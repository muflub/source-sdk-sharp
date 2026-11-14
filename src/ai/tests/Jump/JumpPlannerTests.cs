using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using TF2.Shared.Movement;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// Where a forward jump's recipe is found (plan_bots_teams_tf2_part_g G.5 step 6): the planner over
/// a bot's route, on hand-built halls. Nothing here is a number of the blast's: what a fact needs of
/// a flight (how high it rises, where it comes down) it reads from the plan the simulator made in
/// the open hall, so the facts follow the physics when they change.
/// </summary>
public class JumpPlannerTests
{
    /// <summary>A hall along x on 16-unit columns, its floor at 0: 3,520 by 320 unless told otherwise.</summary>
    internal const int HallLength = 220, HallWidth = 20;

    internal const float HallY = HallWidth * 8f;

    internal static VoxelColumns Hall(float ceiling = 4096f, int length = HallLength, Func<int, int, bool>? solid = null, int width = HallWidth)
        => VoxelColumns.Of(0f, 0f, 16f, length, width, (x, y) => solid?.Invoke(x, y) == true ? [] : [new FreeSpan(0f, ceiling)]);

    internal static JumpPlanner Planner(VoxelColumns columns, JumpPlanSettings? settings = null, float? walkSpeed = null, JumpSearchSettings? search = null)
    {
        JumpPilotLimits limits = Limits();
        var flier = new JumpFlier(Sim(columns), limits);
        NavGraph graph = GraphOf(columns);
        return new JumpPlanner(graph, new JumpSearch(flier, new NoJumpWalk(), search ?? new JumpSearchSettings()), flier, Flier(JumpEnvelope.OpenAir()),
                               new NavObstacleState(graph.ObstacleCount) { OpenDoors = true }, walkSpeed ?? limits.RunSpeed, settings ?? new JumpPlanSettings());
    }

    internal static AiVector At(float x, float y = HallY) => new(x, y, 0f);

    internal static readonly AiVector East = new(1f, 0f, 0f);

    /// <summary>A bot at the hall's west end heading east for its far end.</summary>
    internal static JumpStretchAsk Along(float from = 160f, float to = HallLength * 16f - 120f) => new(At(from), East, At(to), new Tick(100));

    /// <summary>The open hall's plan, made once: what the other facts size their halls by.</summary>
    internal static readonly Lazy<(JumpPlanned Planned, JumpPlanner Planner)> Open = new(() =>
    {
        JumpPlanner planner = Planner(Hall());
        return (planner.Plan(Along()), planner);
    });

    internal static JumpPlan OpenPlan => Open.Value.Planned.Plan ?? throw new InvalidOperationException($"the open hall has no plan: {Open.Value.Planned.Refusal}");

    [Fact]
    public void ALongStraightLevelRunHasAJumpAheadOfTheBotAlongItsWay()
    {
        JumpPlan plan = OpenPlan;
        var settings = new JumpPlanSettings();
        // The takeoff is the lead ahead of the bot, the landing further along the same line, no further than the hall goes.
        Assert.Equal(160f + settings.Lead, plan.Recipe.Takeoff.X, 0);
        Assert.True(MathF.Abs(plan.Recipe.Takeoff.Y - HallY) < 4f && MathF.Abs(plan.Recipe.Landing.Y - HallY) < 12f, $"from {plan.Recipe.Takeoff} to {plan.Recipe.Landing}");
        float jump = plan.Recipe.Landing.X - plan.Recipe.Takeoff.X;
        Assert.InRange(jump, settings.MinJump, Along().Goal.X - plan.Recipe.Takeoff.X - settings.EndMargin + 16f);
        Assert.True(jump <= Open.Value.Planner.Envelope.Range * settings.ReachShare + 0.01f, $"a jump of {jump} with a range of {Open.Value.Planner.Envelope.Range}");
        Assert.Equal(At(HallLength * 16f - 120f), plan.Goal);
    }

    [Fact]
    public void APlansRecipeFlownAgainIsThePlan()
    {
        JumpPlan plan = OpenPlan;
        JumpOutcome again = Flier(Hall()).Fly(plan.Recipe, plan.Recipe.Start(), new JumpSearchSettings().MaxCommands);
        Assert.True(again.Flew && again.OnGround, "the plan's recipe did not fly");
        Assert.Equal((plan.Seconds, plan.AirSeconds, plan.Health, plan.Touch, plan.Apex), (again.Seconds, again.AirSeconds, again.BlastDamage, again.Touch, again.Apex));
        Assert.True(Flat(again.Rest, plan.Recipe.Landing) <= new JumpPlanSettings().Tolerance, $"it rests {Flat(again.Rest, plan.Recipe.Landing)} from its landing");
    }

    [Fact]
    public void APlanBeatsTheWalkOverTheSameGroundAsTheSimulatorHasIt()
    {
        JumpPlan plan = OpenPlan;
        var settings = new JumpPlanSettings();
        // The walk: from the recipe's start to where the flight comes down, at the class's run.
        Assert.Equal((plan.Recipe.RunUp + Flat(plan.Touch, plan.Recipe.Takeoff)) / RunSpeed, plan.WalkSeconds, 3);
        Assert.True(plan.Seconds <= plan.WalkSeconds * settings.PaysShare, $"{plan.Seconds} s against a walk of {plan.WalkSeconds} s");
        Assert.True(plan.WalkSeconds - plan.Seconds >= settings.MinSavedSeconds);
    }

    [Fact]
    public void AJumpThatTakesMoreThanItsShareOfTheWalkIsNoJump()
    {
        // The same hall and the same simulator; only the walk the flight is set beside is quicker, and no seconds need be saved.
        JumpPlan plan = OpenPlan;
        var settings = new JumpPlanSettings { MinSavedSeconds = 0f };
        float ground = plan.Recipe.RunUp + Flat(plan.Touch, plan.Recipe.Takeoff);
        // A class the jump takes 0.9 of the walk of: over the share. And the control, 0.7 of it: under.
        JumpPlanned over = Planner(Hall(), settings, walkSpeed: ground / (plan.Seconds / 0.9f)).Plan(Along());
        Assert.Equal((null, JumpRefusal.NotFaster), (over.Plan, over.Refusal));
        Assert.True(settings.PaysShare is > 0.7f and < 0.9f, "the control: the share lies between the two");
        Assert.NotNull(Planner(Hall(), settings, walkSpeed: ground / (plan.Seconds / 0.7f)).Plan(Along()).Plan);
    }

    [Fact]
    public void AJumpThatSavesLessThanItMustHasNoJump()
    {
        JumpPlan plan = OpenPlan;
        float saved = plan.WalkSeconds - plan.Seconds;
        JumpPlanned planned = Planner(Hall(), new JumpPlanSettings { MinSavedSeconds = saved + 0.5f }).Plan(Along());
        Assert.Equal((null, JumpRefusal.NotFaster), (planned.Plan, planned.Refusal));
    }

    /// <summary>The ceiling a crouched body's head is <paramref name="air"/> under at the top of a flight that rises <paramref name="apex"/>.</summary>
    private static float CeilingOver(float apex, float air) => apex + TFViewVectors.Params.DuckHullMax.Z + air;

    [Fact]
    public void AFlightThatWouldComeNearerACeilingThanItsHeadroomHasNoJump()
    {
        // The control: a hall with a ceiling well over the open hall's flight has a jump, with the default headroom.
        VoxelColumns hall = Hall(CeilingOver(OpenPlan.Apex, 250f));
        JumpPlan? roomy = Planner(hall).Plan(Along()).Plan;
        Assert.True(roomy is not null, "the control: no jump under the high ceiling");
        float air = CeilingOver(OpenPlan.Apex, 250f) - CeilingOver(roomy!.Apex, 0f);
        Assert.True(air > new JumpPlanSettings().Headroom, $"the control: {air} u over its head");

        // The same hall, asked to keep a little more air than there is over that flight's top.
        JumpPlanned low = Planner(hall, new JumpPlanSettings { Headroom = air + 8f }).Plan(Along());
        Assert.Equal((null, JumpRefusal.Ceiling), (low.Plan, low.Refusal));
    }

    [Fact]
    public void UnderACeilingTheFlightWouldMeetThereIsNoJump()
    {
        // A slab a standing body walks under with room to spare, far under where the flight goes.
        float slab = TFViewVectors.Params.HullMax.Z + 46f;
        Assert.True(slab < CeilingOver(OpenPlan.Apex, 0f) - 64f, $"the control: the open flight rises {OpenPlan.Apex}, well past a slab at {slab}");
        Assert.Null(Planner(Hall(slab)).Plan(Along()).Plan);
    }

    [Fact]
    public void AJumpLandsShortOfTheWallItsStraightEndsAt()
    {
        // Whether the shorter jump pays is other facts' business: here any jump no slower than the walk is a plan.
        var settings = new JumpPlanSettings { PaysShare = 1f, MinSavedSeconds = 0f };
        // A wall across the hall, the goal just before it, where the straight has room for a jump midway between the shortest there is and the open hall's.
        float open = OpenPlan.Recipe.Landing.X - OpenPlan.Recipe.Takeoff.X, jump = (settings.MinJump + open) * 0.5f;
        Assert.True(open > settings.MinJump + 64f, $"the control: the open hall's jump of {open} is longer than the shortest there is");
        float wall = MathF.Floor((Along().Origin.X + settings.Lead + jump + settings.EndMargin + 40f) / 16f) * 16f;
        VoxelColumns columns = Hall(solid: (x, _) => x * 16f >= wall);
        JumpPlanned planned = Planner(columns, settings).Plan(Along(to: wall - 40f));
        Assert.True(planned.Plan is not null, $"the control: a jump before the wall, {planned.Refusal}");
        Assert.True(planned.Plan!.Recipe.Landing.X <= wall - 40f - settings.EndMargin + 16f, $"it lands at {planned.Plan.Recipe.Landing.X} with the wall at {wall}");
        Assert.True(planned.Plan.Touch.X < wall - 24f, $"the simulator comes down at {planned.Plan.Touch.X}");
        // It is the wall that ends it: the open hall's jump from the same takeoff lands further on, and past where this one may.
        Assert.True(OpenPlan.Recipe.Landing.X > wall - 40f - settings.EndMargin + 16f, $"the control: the open hall's jump lands at {OpenPlan.Recipe.Landing.X}");
        Assert.Equal(OpenPlan.Recipe.Takeoff.X, planned.Plan.Recipe.Takeoff.X, 0);
    }

    [Fact]
    public void TowardAWallTooNearForALeadAJumpAndRoomToStopThereIsNoJump()
    {
        var settings = new JumpPlanSettings();
        float wall = 160f + settings.Lead + settings.MinJump + settings.EndMargin;
        VoxelColumns columns = Hall(solid: (x, _) => x * 16f >= wall);
        JumpPlanned planned = Planner(columns).Plan(Along(to: wall - 40f));
        Assert.Equal((null, JumpRefusal.Short, 0), (planned.Plan, planned.Refusal, planned.Flights));
    }

    /// <summary>
    /// G.13's finding: the goal is near in a straight line, through a wall, and the way to it is long. A hall 640
    /// wide folded in two by a wall along its middle, open at the east end: the bot at the west end of the south
    /// half, the goal at the west end of the north half.
    /// </summary>
    private static VoxelColumns Folded() => Hall(solid: (x, y) => y is 19 or 20 && x < HallLength - 12, width: 40);

    [Fact]
    public void TheStraightIsMeasuredAlongTheRouteNotToTheGoal()
    {
        var south = new AiVector(160f, 200f, 0f);
        var north = new AiVector(200f, 500f, 0f);
        Assert.True(south.DistanceTo(north) < new JumpPlanSettings().MinJump, "the control: the goal is close by, through the wall");
        JumpPlanned planned = Planner(Folded()).Plan(new JumpStretchAsk(south, East, north, new Tick(100)));
        Assert.True(planned.Plan is not null, $"no jump along the south half: {planned.Refusal}");
        // East along the south half, away from where the goal lies, and on the bot's side of the wall.
        JumpRecipe r = planned.Plan!.Recipe;
        Assert.True(r.Landing.X > r.Takeoff.X + 500f && r.Landing.Y < 19 * 16f - 24f && planned.Plan.Touch.Y < 19 * 16f - 24f, $"from {r.Takeoff} to {r.Landing}");
    }

    [Fact]
    public void ABotNotHeadingTheWayItsRouteGoesIsTurningAndHasNoJump()
    {
        JumpPlanned planned = Planner(Hall()).Plan(Along() with { Heading = new AiVector(0f, 1f, 0f) });
        Assert.Equal((null, JumpRefusal.Turning, 0), (planned.Plan, planned.Refusal, planned.Flights));
    }

    [Fact]
    public void AGoalThereIsNoWayToHasNoJump()
    {
        // The hall cut in two by a wall with no way round.
        VoxelColumns columns = Hall(solid: (x, _) => x is 100 or 101);
        JumpPlanned planned = Planner(columns).Plan(Along());
        Assert.Equal((null, JumpRefusal.NoRoute, 0), (planned.Plan, planned.Refusal, planned.Flights));
    }

    [Fact]
    public void AJumpMadeTheSlidePastItsTakeoffStillLandsWhereThePlanDoes()
    {
        JumpPlan plan = OpenPlan;
        Assert.True(plan.Slide > 0f, "the control: the open hall's plan allows a late jump");
        JumpRecipe late = plan.Recipe with { Takeoff = plan.Recipe.Takeoff + new Vector(plan.Slide, 0f, 0f) };
        JumpOutcome flown = Flier(Hall()).Fly(late, late.Start(), new JumpSearchSettings().MaxCommands);
        Assert.True(flown.Flew && flown.OnGround && Flat(flown.Rest, plan.Recipe.Landing) <= new JumpPlanSettings().Tolerance,
                    $"from {plan.Slide} further on it rests {Flat(flown.Rest, plan.Recipe.Landing)} from the landing");
    }

    [Fact]
    public void APlanAllowsNoMoreSlideThanTheSimulatorLandsAndPaysFrom()
    {
        // Asked to allow a jump from so far on that 300 u of it are left: no hop that short pays, and the plan allows less.
        float jump = OpenPlan.Recipe.Landing.X - OpenPlan.Recipe.Takeoff.X, asked = jump - 300f;
        JumpPlan plan = Planner(Hall(), new JumpPlanSettings { Slide = asked }).Plan(Along()).Plan ?? throw new InvalidOperationException("no plan");
        Assert.Equal(OpenPlan.Recipe, plan.Recipe);
        Assert.True(plan.Slide < asked, $"it allows {plan.Slide} of the {asked} asked for");
        if (plan.Slide > 0f)
        {
            JumpRecipe late = plan.Recipe with { Takeoff = plan.Recipe.Takeoff + new Vector(plan.Slide, 0f, 0f) };
            JumpOutcome flown = Flier(Hall()).Fly(late, late.Start(), new JumpSearchSettings().MaxCommands);
            float walk = (late.RunUp + Flat(flown.Touch, late.Takeoff)) / RunSpeed;
            Assert.True(flown.Flew && Flat(flown.Rest, late.Landing) <= new JumpPlanSettings().Tolerance && flown.Seconds <= walk * new JumpPlanSettings().PaysShare,
                        $"from {plan.Slide} on: rests {Flat(flown.Rest, late.Landing)} off, {flown.Seconds} s against {walk} s");
        }
    }

    /// <summary>A plan's jump from a standstill as a bot that waited makes it: <paramref name="run"/> before the slide's end, the view round already.</summary>
    private static (JumpOutcome Flown, JumpRecipe Recipe) FromAStandstill(JumpPlan plan, float run)
    {
        JumpRecipe held = plan.Recipe with { Takeoff = plan.Recipe.Takeoff + new Vector(plan.Slide, 0f, 0f), RunUp = run };
        return (Flier(Hall()).Fly(held, held.Start() with { View = held.ShotView }, new JumpSearchSettings().MaxCommands), held);
    }

    [Fact]
    public void APlanSaysHowLongAJumpFromAStandstillAtItsSlidesEndIsWorthWaitingFor()
    {
        JumpPlan plan = OpenPlan;
        var settings = new JumpPlanSettings();
        Assert.Equal(settings.HoldRun, plan.HoldRun);
        // The simulator's own flight of it: it lands on the plan's landing, and what it saves over the walk from where it stood is the wait.
        (JumpOutcome flown, JumpRecipe held) = FromAStandstill(plan, plan.HoldRun);
        Assert.True(flown.Flew && flown.OnGround && Flat(flown.Rest, plan.Recipe.Landing) <= settings.Tolerance, $"it rests {Flat(flown.Rest, plan.Recipe.Landing)} from the landing");
        float walk = (held.RunUp + Flat(flown.Touch, held.Takeoff)) / RunSpeed;
        Assert.True(plan.HoldSeconds > 0f, "the control: the jump from a standstill saves something");
        Assert.Equal(walk - flown.Seconds, plan.HoldSeconds, 3);
        // The view was round when it set off: it ran at once, and was in the air after no more than the run takes at half its speed.
        Assert.True(flown.Seconds - flown.AirSeconds <= plan.HoldRun / (RunSpeed * 0.5f), $"{flown.Seconds - flown.AirSeconds} s on the ground for a run of {plan.HoldRun}");
    }

    [Fact]
    public void AJumpTheSimulatorDoesNotLandFromAStandstillAllowsNoWait()
    {
        // A run so long it would start in the rock west of the hall: there is nowhere to stand for it, and the simulator flies nothing from there.
        var settings = new JumpPlanSettings { HoldRun = OpenPlan.Recipe.Takeoff.X + OpenPlan.Slide + 200f };
        (JumpOutcome flown, _) = FromAStandstill(OpenPlan, settings.HoldRun);
        Assert.True(!flown.Flew || Flat(flown.Rest, OpenPlan.Recipe.Landing) > settings.Tolerance, "the control: the simulator makes no landing from in the rock");
        JumpPlan plan = Planner(Hall(), settings).Plan(Along()).Plan ?? throw new InvalidOperationException("no plan");
        Assert.Equal((OpenPlan.Recipe, OpenPlan.Slide), (plan.Recipe, plan.Slide));
        Assert.Equal((0f, 0f), (plan.HoldRun, plan.HoldSeconds));
    }

    [Fact]
    public void APlannerToldOfNoRunFromAStandstillAllowsNoWait()
    {
        JumpPlanned planned = Planner(Hall(), new JumpPlanSettings { HoldRun = 0f }).Plan(Along());
        JumpPlan plan = planned.Plan ?? throw new InvalidOperationException("no plan");
        Assert.Equal((OpenPlan.Recipe, 0f, 0f), (plan.Recipe, plan.HoldRun, plan.HoldSeconds));
        // And flies nothing to find that out: the open hall's own plan took one flight more.
        Assert.Equal(Open.Value.Planned.Flights - 1, planned.Flights);
    }

    [Fact]
    public void ARecipeThatLandsInTooFewOfItsTrialsIsNoJump()
    {
        // The same search, asked to keep only what lands in more trials than there are.
        JumpPlanned planned = Planner(Hall(), search: new JumpSearchSettings { Keep = 1.01f }).Plan(Along());
        Assert.Equal((null, JumpRefusal.Fragile), (planned.Plan, planned.Refusal));
    }

    [Fact]
    public void AStraightEndsWhereTheFloorLeavesTheLevel()
    {
        var settings = new JumpPlanSettings();
        // Stairs up from x = 720, a 12 u step every 64 u: a walk all the way, and no level floor to fly over or land on.
        const float foot = 720f;
        static float Floor(float x) => x < foot ? 0f : 12f * (1 + (int)((x - foot) / 64f));
        VoxelColumns stairs = VoxelColumns.Of(0f, 0f, 16f, HallLength, HallWidth, (x, _) => [new FreeSpan(Floor(x * 16f), 4096f)]);
        Assert.True(foot + 64f - 160f < settings.Lead + settings.MinJump + settings.EndMargin, "the control: the level floor before the stairs is too short for a jump");
        JumpStretchAsk up = Along() with { Goal = new AiVector(3400f, HallY, Floor(3400f)) };
        JumpPlanner planner = Planner(stairs);
        Assert.Equal(JumpRefusal.Turning, planner.Plan(up with { Heading = new AiVector(0f, 1f, 0f) }).Refusal);   // the control: there is a way up them
        JumpPlanned planned = planner.Plan(up);
        Assert.Equal((null, JumpRefusal.Short, 0), (planned.Plan, planned.Refusal, planned.Flights));
    }

    [Fact]
    public void AStraightOffTheGridsAxesIsStillAStraight()
    {
        // A square yard, the bot on a corner of the grid heading for the far corner: its line crosses a grid corner every 16 u.
        VoxelColumns yard = VoxelColumns.Of(0f, 0f, 16f, 150, 150, static (_, _) => [new FreeSpan(0f, 4096f)]);
        float d = MathF.Sqrt(0.5f);
        JumpPlanned planned = Planner(yard).Plan(new JumpStretchAsk(new AiVector(160f, 160f, 0f), new AiVector(d, d, 0f), new AiVector(2240f, 2240f, 0f), new Tick(100)));
        Assert.True(planned.Plan is not null, $"no jump across the yard: {planned.Refusal}");
        Assert.Equal(45f, planned.Plan!.Recipe.TravelYaw, 0);
    }

    [Fact]
    public void APlanSpendsAtMostASearchAndAFewFlightsAndOneFloodAGoal()
    {
        JumpPlanner planner = Planner(Hall());
        JumpPlanned first = planner.Plan(Along());
        long envelope = planner.Envelope.Flights;
        // The search, then the recipe flown again, twice at most for the slide, and once from a standstill.
        Assert.InRange(first.Flights - envelope, 1, new JumpSearchSettings().MaxFlights + 4);
        Assert.Equal((first.Flights, 1), (planner.Flights, planner.Floods));
        // The same goal asked about again from elsewhere floods nothing more; another goal floods once.
        planner.Plan(Along(from: 480f));
        Assert.Equal(1, planner.Floods);
        planner.Plan(Along(to: 2000f));
        Assert.Equal(2, planner.Floods);
    }

    [Fact]
    public void TheSameStretchAskedAgainHasTheSamePlan()
        => Assert.Equal(OpenPlan, Planner(Hall()).Plan(Along()).Plan);
}
