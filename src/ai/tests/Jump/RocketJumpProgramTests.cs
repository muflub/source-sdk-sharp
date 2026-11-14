using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Jump;
using TF2.Shared.Movement;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The rocket jump as the applier runs it (plan_bots_teams_tf2_part_g G.5 step 2): TF2's program
/// flying the open hall's plan (<see cref="JumpPlannerTests.OpenPlan"/>) for a bot whose player is
/// the simulator (<see cref="JumpLive"/>), its view moved by the real motion layer. What a flight
/// should do is asked of the simulator for the same recipe, never written down.
/// </summary>
public class RocketJumpProgramTests
{
    // An expert's hand, a middling one, and one slower than the easy preset's (160 ms, 300 degrees a second, 20 % of overshoot).
    private static readonly MotionParams Quick = new(100f, 900f, 5f, 60f), Normal = new(250f, 600f, 10f, 150f), Slow = new(400f, 300f, 20f, 250f);

    /// <summary>A hand the envelope allows and no preset has: a reaction of 1.5 s.</summary>
    private static readonly MotionParams Stunned = new(1500f, 300f, 40f, 400f);

    private static JumpPlan Plan => JumpPlannerTests.OpenPlan;

    private static AiVector Goal => Plan.Goal;

    /// <summary>A bot standing west of the plan's takeoff, <paramref name="aside"/> to the north of its line, looking east.</summary>
    private static JumpLive Bot(MotionParams motion, float x = 40f, float aside = 0f, int rowCommands = JumpLive.RowCommands, int approachCommands = RocketJumpProgram.ApproachCommands,
                                ulong seed = 42)
        => new(JumpPlannerTests.Hall(), new JumpStart(new Vector(x, Plan.Recipe.Takeoff.Y + aside, VoxelMoveWorld.DistEpsilon), Vector.Zero, Vector.Zero, Ducked: false, OnGround: true),
               motion, rowCommands, approachCommands, seed: seed);

    /// <summary>The bot walks to <paramref name="from"/>, is told to jump, and goes on until the program is over: the first command the program had.</summary>
    private static int Fly(JumpLive live, JumpPlan? plan = null, float from = 170f, ActionIntent held = default)
    {
        JumpPlan kept = live.Keep(plan ?? Plan);
        live.WalkPast(from, Goal);
        return live.Jump(kept.Id, Goal, held: held);
    }

    private const int Attack = (int)InputButtons.Attack, JumpKey = (int)InputButtons.Jump, Duck = (int)InputButtons.Duck;

    private static Vector View(in UserCmd c) => new(c.Pitch, c.Yaw, 0f);

    [Fact]
    public void ABotFliesAPlanThroughTheApplierAndMakesItsLanding()
    {
        JumpLive live = Bot(Normal);
        Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.Landed, true, true), (flown.End, flown.Launched, flown.Felt));
        // It comes to rest on the plan's landing, as the search asked of the pilot alone.
        for (int i = 0; i < JumpFlier.SettleCommands; i++)
            live.Command(Goal with { X = live.Now.Origin.X });
        Assert.True(Flat(live.Now.Origin, Plan.Recipe.Landing) <= new JumpPlanSettings().Tolerance * 2f, $"it rests at {live.Now.Origin}, the landing is {Plan.Recipe.Landing}");
        Assert.Equal(1, live.Simulator.Rockets);
        Assert.Equal((1L, 0L), (live.Program.Begun, live.Program.Unknown));
    }

    [Fact]
    public void TheViewLooksBackForTheShotAndIsOnTheWayAgainByTheLanding()
    {
        JumpLive live = Bot(Normal);
        int first = Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        var way = new Vector(0f, flown.Recipe.TravelYaw, 0f);
        int shot = live.Sent.FindIndex(first, c => (c.Buttons & Attack) != 0);
        Assert.True(JumpPilot.ViewGap(View(live.Sent[shot]), way) > 150f, "the control: the shot was fired looking back");
        Assert.True(JumpPilot.ViewGap(View(live.Sent[^1]), way) < 20f, $"it landed looking {JumpPilot.ViewGap(View(live.Sent[^1]), way)} degrees off its way");
    }

    [Fact]
    public void ThePilotIsGivenTheBotsOwnTurnRateAndItsTolerance()
    {
        JumpLive live = Bot(Slow);
        JumpPlan kept = live.Keep(Plan);
        MotionParams motion = MotionEnvelope.Clamp(Slow);
        var run = Assert.IsType<RocketJumpProgram.Run>(live.Program.Begin(kept.Id, new AbilityBody(default, default, default, true), motion, TickSeconds));
        Assert.Equal((motion.TurnPeakDegPerSec, TickSeconds, RunSpeed), (run.Limits.TurnDegreesASecond, run.Limits.TickSeconds, run.Limits.RunSpeed));
        Assert.Equal(RocketJumpProgram.Tolerance(motion), run.ToleranceDegrees);
        Assert.Equal(kept.Recipe, run.Recipe);
    }

    [Fact]
    public void ABotOffTheGroundCallsTheJumpOff()
    {
        // Dropped from over the floor and told to jump while it falls.
        var live = new JumpLive(JumpPlannerTests.Hall(), new JumpStart(new Vector(170f, Plan.Recipe.Takeoff.Y, 300f), Vector.Zero, Vector.Zero, Ducked: false, OnGround: false), Normal);
        JumpPlan kept = live.Keep(Plan);
        live.Jump(kept.Id, Goal);
        Assert.Equal((JumpEnd.Astray, false), (Assert.Single(live.Flown).End, live.Flown[0].Launched));
    }

    [Fact]
    public void AJumpWhoseBlastNeverCarriedItIsCountedAsFizzled()
    {
        // The same plan with the shot straight up: the rocket leaves and the blast is nowhere near.
        JumpLive live = Bot(Normal);
        Fly(live, Hop);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.Fizzled, true, false, 1), (flown.End, flown.Launched, flown.Felt, live.Simulator.Rockets));
    }

    /// <summary>The plan with the shot straight up: the bot hops on its legs and the rocket's blast is nowhere near.</summary>
    private static JumpPlan Hop => Plan with { Recipe = Plan.Recipe with { Pitch = -85f } };

    [Fact]
    public void AHopThatStaysUpLongEnoughButRisesNoHigherThanLegsDoIsFizzled()
    {
        // A plan whose time in the air any hop outlasts: only how high it rose tells the hop from the flight.
        JumpLive live = Bot(Normal);
        Fly(live, Hop with { AirSeconds = 0.05f });
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.True(flown.AirCommands * flown.TickSeconds > 0.05f * 4f && flown.Rise > 0f, $"the control: it hopped, {flown.AirCommands} commands in the air and {flown.Rise} up");
        Assert.True(flown.Rise < Plan.Apex * RocketJumpProgram.FlownShare, $"the control: the hop rose {flown.Rise}, the plan's flight {Plan.Apex}");
        Assert.Equal(JumpEnd.Fizzled, flown.End);
    }

    [Fact]
    public void AHopBackDownBeforeHalfThePlansTimeInTheAirIsFizzled()
    {
        // A plan that rises no higher than a step and stays up far longer than legs can: only the time in the air tells.
        JumpLive live = Bot(Normal);
        Fly(live, Hop with { Apex = 1f, AirSeconds = 30f });
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.True(flown.Rise > 1f, $"the control: the hop rose {flown.Rise}");
        Assert.Equal(JumpEnd.Fizzled, flown.End);
    }

    [Fact]
    public void AFlightIsCountedWithTheCommandAfterTheTriggerItsBlastWasFeltOnAndTheLeapItMade()
    {
        JumpLive live = Bot(Normal);
        int first = Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.True(flown.Felt && flown.FeltAfterShot > 0, $"the control: the blast was felt ({flown.Felt}), {flown.FeltAfterShot} commands after the trigger");
        // The command the program felt it on read the body the command before it left: the leap is that body's velocity against the one before.
        int shot = live.Sent.FindIndex(first, c => (c.Buttons & Attack) != 0), felt = shot + flown.FeltAfterShot;
        Vector leap = live.Track[felt - 1].Velocity - live.Track[felt - 2].Velocity;
        Assert.True((leap - flown.Leap).Length < 0.01f, $"the record's leap {flown.Leap}, the track's {leap}");
        // A blast under the bot and behind it lifts it.
        Assert.True(flown.Leap.Z > JumpPilot.BlastFeltSpeed * 0.5f, $"the leap {flown.Leap}");
    }

    [Fact]
    public void AJumpWithNoBlastFeltSaysSo()
    {
        JumpLive live = Bot(Normal);
        Fly(live, Hop);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((false, -1, Vector.Zero), (flown.Felt, flown.FeltAfterShot, flown.Leap));
    }

    [Fact]
    public void AFlightIsCountedWithHowHighItRose()
    {
        JumpLive live = Bot(Normal);
        Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        // The simulator's own flight of the plan rises the plan's apex; the applier's, launched a little late, as near as makes no difference.
        Assert.Equal(Plan.Apex, flown.Rise, 0);
    }

    /// <summary>
    /// Live, the first run's third jump (a recipe 50 degrees down, the trigger three commands before the jump) came
    /// down on its plan's touch after its plan's time in the air and was counted as fizzled: its blast reaches the
    /// body with the jump's own command, where the pilot looks for no blast. The same recipe here.
    /// </summary>
    [Fact]
    public void AFlightWhoseBlastThePilotNeverFeltIsStillALanding()
    {
        JumpPlan steep = JumpPlannerTests.Planner(JumpPlannerTests.Hall(), new JumpPlanSettings { ReachShare = 0.9f }).Plan(JumpPlannerTests.Along()).Plan
                         ?? throw new InvalidOperationException("no plan at 0.9 of the range");
        JumpLive live = Bot(Normal);
        Fly(live, steep);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal(JumpEnd.Landed, flown.End);
        Assert.True(flown.AirCommands * flown.TickSeconds >= steep.AirSeconds * 0.9f, $"{flown.AirCommands * flown.TickSeconds} s in the air, the plan's is {steep.AirSeconds}");
        // With today's blast the pilot does not feel this one (flown.Felt is false), which is what made the fact red under
        // the old rule; that is the physics' doing and is not held here.
    }

    [Fact]
    public void AFlightThroughTheApplierComesDownWhereTheSimulatorSaysThePilotAloneWould()
    {
        JumpLive live = Bot(Normal);
        Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        // The same recipe (its takeoff where the bot launched) from the same launch state, the pilot alone in a simulator of its own.
        JumpOutcome alone = Flier(JumpPlannerTests.Hall(), MotionEnvelope.Clamp(Normal).TurnPeakDegPerSec).Fly(flown.Recipe, flown.Launch, 700);
        Assert.True(alone.Flew, "the control: the pilot alone flies it");
        Assert.True(Flat(flown.Touch, alone.Touch) <= 24f, $"the applier's flight touched at {flown.Touch}, the pilot's own at {alone.Touch}");
        Assert.Equal(alone.AirSeconds, flown.AirCommands * flown.TickSeconds, 1);
    }

    [Fact]
    public void FromTheLaunchOnTheAppliersCommandsAreThePilots()
    {
        JumpLive live = Bot(Normal);
        int first = Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        JumpPilotLimits limits = JumpPilotLimits.Of(TFViewVectors.Params, RunSpeed, TickSeconds, MotionEnvelope.Clamp(Normal).TurnPeakDegPerSec);
        JumpPilotState state = default;
        int launch = first + flown.ApproachCommands, compared = 0, jumps = 0, shots = 0, steered = 0;
        for (int k = launch; k < live.Sent.Count; k++)
        {
            var body = new JumpBody(live.Track[k - 1].Origin, live.Track[k - 1].Velocity, View(live.Sent[k - 1]), live.Track[k - 1].OnGround);
            JumpPilotStep step = JumpPilot.Step(flown.Recipe, state, body, limits);
            state = step.Next;
            if (state.Phase == JumpPhase.Landed)
                break;
            UserCmd sent = live.Sent[k];
            Assert.Equal(step.Command.Buttons, sent.Buttons);
            float yaw = sent.Yaw * (MathF.PI / 180f), fx = MathF.Cos(yaw), fy = MathF.Sin(yaw);
            var wish = new Vector(sent.ForwardMove * fx + sent.SideMove * fy, sent.ForwardMove * fy - sent.SideMove * fx, 0f);
            Assert.True((wish - step.Wish).Length < 0.05f, $"command {k - launch}: the applier moves {wish}, the pilot {step.Wish}");
            compared++;
            jumps += (sent.Buttons & JumpKey) != 0 ? 1 : 0;
            shots += (sent.Buttons & Attack) != 0 ? 1 : 0;
            steered += !body.OnGround && step.Wish.Length > 1f ? 1 : 0;
        }

        // The stimulus reached the subject: the jump, the shot and a steered flight were among them.
        Assert.Equal((1, JumpPilot.TriggerCommands), (jumps, shots));
        Assert.True(compared > 60 && steered > 20, $"{compared} commands compared, {steered} steered in the air");
    }

    /// <summary>The open hall's plan made worth any wait: what a hand does for its view is then its own, whatever the jump saves today.</summary>
    private static JumpPlan Patient => Plan with { HoldSeconds = 60f };

    private static (int Shot, JumpFlown Flown, JumpLive Live) ShotOf(MotionParams motion)
    {
        Assert.True(Plan.HoldRun > 0f, "the control: the open hall's plan allows a wait");
        JumpLive live = Bot(motion);
        int first = Fly(live, Patient, from: 60f);
        int shot = live.Sent.FindIndex(first, c => (c.Buttons & Attack) != 0);
        return (shot - first, Assert.Single(live.Flown), live);
    }

    [Fact]
    public void AViewThatLagsDelaysTheShotAndTheTakeoff()
    {
        (int quick, JumpFlown quickFlown, _) = ShotOf(Quick);
        (int slow, JumpFlown slowFlown, _) = ShotOf(Slow);
        // The quick hand's view is round before the plan's takeoff and it launches there; the slow one's comes round later, and it launches further on.
        Assert.True(quick >= 0 && slow > quick + 10, $"the quick hand fired on its command {quick}, the slow one on {slow}");
        Assert.True(quickFlown.Slid < 8f && slowFlown.Slid > quickFlown.Slid + 16f && slowFlown.Slid <= Plan.Slide, $"slid {quickFlown.Slid} and {slowFlown.Slid}, of {Plan.Slide}");
        Assert.Equal(slowFlown.Slid - quickFlown.Slid, slowFlown.Recipe.Takeoff.X - quickFlown.Recipe.Takeoff.X, 0);
        Assert.Equal((JumpEnd.Landed, JumpEnd.Landed), (quickFlown.End, slowFlown.End));
    }

    /// <summary>The motion of one of the game's presets, as the game's own curves give it.</summary>
    private static MotionParams Preset(string name)
    {
        Assert.True(SkillPresets.TryGet(name, out SkillProfile? profile));
        return MotionEnvelope.Clamp(MotionCurves.For(profile!, Skills.Curves.Game().For(profile!)));
    }

    /// <summary>Where a bot is when a plan made for it comes back: the planner's lead before the takeoff, less the few steps it took meanwhile.</summary>
    private static float Fresh => Plan.Recipe.Takeoff.X - new JumpPlanSettings().Lead + 10f;

    private const int Seeds = 40;

    /// <summary>A command's run, units.</summary>
    private static float Stride => RunSpeed * TickSeconds;

    /// <summary>Where a bot whose view may be late is stopped for it: two hold runs before the slide's end.</summary>
    private static float StandsFrom => Plan.Recipe.Takeoff.X + Plan.Slide - 2f * Plan.HoldRun;

    /// <summary>The turn from the way ahead to the plan's shot, degrees.</summary>
    private static float Turn => JumpPilot.ViewGap(new Vector(0f, Plan.Recipe.TravelYaw, 0f), Plan.Recipe.ShotView);

    [Theory]
    [InlineData("easy")]
    [InlineData("normal")]
    [InlineData("hard")]
    [InlineData("expert")]
    public void APresetsHandWhoseLikelyTurnTheFreshPlanIsWorthGetsItsJumpAwayAndBeatsTheWalk(string preset)
    {
        MotionParams motion = Preset(preset);
        // The wait this hand is likely to have where it would stop, against what the plan's jump from a standstill saves: well inside it, the jump must
        // be made; well outside it, the bot must not stand at all; near the edge, either. Nothing here is a number of the blast's.
        float wait = (RocketJumpProgram.TurnCommandsLikely(motion, Turn, TickSeconds) - MathF.Max(0f, StandsFrom - Fresh) / Stride) * TickSeconds;
        const float edge = 0.15f;
        bool worth = Plan.HoldRun > 0f && wait <= Plan.HoldSeconds - edge, notWorth = Plan.HoldRun > 0f && wait > Plan.HoldSeconds + edge;
        var seconds = new List<float>();
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            JumpLive live = Bot(motion, seed: seed);
            int first = Fly(live, from: Fresh);
            JumpFlown flown = Assert.Single(live.Flown);
            string what = $"{preset}, seed {seed}: {flown.End}, launched {flown.Slid} past the takeoff of the {Plan.Slide} allowed, having stood {flown.Stood} commands (a likely wait of {wait} s, the jump saves {Plan.HoldSeconds} s)";
            // Whatever the plan is worth: a jump is made inside the slide, or called off with nothing pressed.
            Assert.True(flown.End == JumpEnd.Landed ? flown.Slid <= Plan.Slide : flown.End == JumpEnd.ViewLate && live.Sent.Skip(first).All(c => (c.Buttons & (Attack | JumpKey | Duck)) == 0), what);
            Assert.True(!worth || flown.End == JumpEnd.Landed, what);
            Assert.True(!notWorth || flown.Stood == 0, what);
            if (flown.End == JumpEnd.Landed)
                seconds.Add((live.Sent.Count - first) * TickSeconds - (flown.Touch.X - live.Track[first - 1].Origin.X) / RunSpeed);
        }

        // From where it was told to jump to where it came down, against the same ground at a run: sooner, in all but the few whose turn was longest.
        seconds.Sort();
        Assert.True(seconds.Count == 0 || seconds[seconds.Count * 9 / 10] < 0f, $"{preset}: nine in ten of its {seconds.Count} jumps were {-seconds[seconds.Count * 9 / 10]} s sooner than the run, or more");
        Assert.True(!worth || seconds.Count == Seeds);
    }

    [Fact]
    public void OnTodaysPlanSomePresetsHandIsOneTheFreshPlanIsWorth()
    {
        // The control of the theory above: it is not every preset that falls outside what the plan is worth.
        int worth = SkillPresets.Names.Count(preset => Plan.HoldRun > 0f
            && (RocketJumpProgram.TurnCommandsLikely(Preset(preset), Turn, TickSeconds) - MathF.Max(0f, StandsFrom - Fresh) / Stride) * TickSeconds <= Plan.HoldSeconds - 0.15f);
        Assert.True(worth > 0, $"no preset's likely turn is worth the fresh plan's {Plan.HoldSeconds} s");
    }

    /// <summary>The slowest the bot went over the ground during an approach of <paramref name="commands"/> commands from <paramref name="first"/>, and where.</summary>
    private static (float Speed, float X) Slowest(JumpLive live, int first, int commands)
    {
        JumpPoint slowest = live.Track.Skip(first).Take(commands).MinBy(p => p.Velocity.Length);
        return (slowest.Velocity.Length, slowest.Origin.X);
    }

    [Fact]
    public void AHandNotRoundByTheSlidesEndStandsForItsViewAndStillJumps()
    {
        // The easy preset's hand on a fresh plan (before the hold, the jump this hand most often called off), the plan worth any wait.
        Assert.True(Plan.HoldRun > 0f, "the control: the open hall's plan allows a wait");
        JumpLive live = Bot(Preset("easy"));
        int first = Fly(live, Patient, from: Fresh);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal(JumpEnd.Landed, flown.End);
        Assert.True(flown.Stood > 0, "the control: it stood");
        Assert.True(Slowest(live, first, flown.ApproachCommands).Speed < 1f, $"it never came to a stand: its slowest was {Slowest(live, first, flown.ApproachCommands).Speed}");
        Assert.InRange(flown.Slid, 0f, Plan.Slide);
        // The control: a plan that allows no wait calls the same hand's jump off, from the same place.
        JumpLive without = Bot(Preset("easy"));
        Fly(without, Plan with { HoldRun = 0f, HoldSeconds = 0f }, from: Fresh);
        Assert.Equal(JumpEnd.ViewLate, Assert.Single(without.Flown).End);
    }

    [Fact]
    public void ABotThatStoodRunsThePlansHoldRunAndIsAtItsSpeedAgainWhenItJumps()
    {
        JumpLive live = Bot(Preset("easy"));
        int first = Fly(live, Patient, from: Fresh);
        JumpFlown flown = Assert.Single(live.Flown);
        (float speed, float stoodAt) = Slowest(live, first, flown.ApproachCommands);
        Assert.True(speed < 1f, "the control: it stood");
        Assert.True(flown.Launch.Origin.X - stoodAt >= Plan.HoldRun - 1f, $"it ran {flown.Launch.Origin.X - stoodAt} from where it stood, the plan's run is {Plan.HoldRun}");
        // Its speed at the launch is the one the simulator's own flight of the plan has at its jump.
        Piloted alone = Pilot(Flier(JumpPlannerTests.Hall()), Plan.Recipe);
        float planned = alone.Track[alone.Jump - 1].Velocity.Length;
        Assert.True(planned > RunSpeed * 0.5f, $"the control: the plan's flight jumps at {planned}");
        Assert.Equal(planned, flown.Launch.Velocity.Length, 0);
    }

    [Fact]
    public void AHandThatWillBeRoundInTimeNeverStands()
    {
        // Told to jump where the normal preset's turn, at its longest, is over a few steps before a run from there passes the slide's end.
        MotionParams motion = Preset("normal");
        float from = Plan.Recipe.Takeoff.X + Plan.Slide - Stride * (RocketJumpProgram.TurnCommandsAtMost(motion, Turn, TickSeconds) + 8);
        Assert.True(from > 60f, $"the control: the hall has the room, the jump is begun at {from}");
        JumpLive live = Bot(motion, x: from - 40f);
        int first = Fly(live, from: from);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.Landed, 0), (flown.End, flown.Stood));
        // The control: its view was still turning where a late view is stood for, and it ran on through.
        int inside = Enumerable.Range(first, flown.ApproachCommands).Count(k => live.Track[k].Origin.X >= StandsFrom && JumpPilot.ViewGap(View(live.Sent[k]), Plan.Recipe.ShotView) > 10f);
        Assert.True(inside > 3, $"the control: {inside} commands of its approach were where a late view is stood for, the view still turning");
        Assert.True(Slowest(live, first + 20, flown.ApproachCommands - 20).Speed > RunSpeed * 0.8f, "it slowed");
    }

    [Fact]
    public void ABotLikelyToWaitLongerThanTheJumpSavesCallsItOffAsItWouldStop()
    {
        // The easy preset's hand on a fresh plan whose jump from a standstill saves half the wait that hand is likely to have where it would stop.
        MotionParams motion = Preset("easy");
        float wait = (RocketJumpProgram.TurnCommandsLikely(motion, Turn, TickSeconds) - MathF.Max(0f, StandsFrom - Fresh) / Stride) * TickSeconds;
        Assert.True(wait > 0.2f, $"the control: the hand is likely to wait {wait} s");
        JumpLive live = Bot(motion);
        int first = Fly(live, Plan with { HoldSeconds = wait * 0.5f }, from: Fresh);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.ViewLate, 0), (flown.End, flown.Stood));
        Assert.True(Slowest(live, first, flown.ApproachCommands).Speed > RunSpeed * 0.8f, "it slowed before it called the jump off");
        Assert.True(flown.Touch.X < Plan.Recipe.Takeoff.X + Plan.Slide - Plan.HoldRun, $"it called the jump off at {flown.Touch.X}, past where it would have stood");
        Assert.All(live.Sent.Skip(first), c => Assert.Equal(0, c.Buttons & (Attack | JumpKey | Duck)));
        // The control: the same hand with a plan worth twice that wait stands for its view, and jumps.
        JumpLive patient = Bot(motion);
        Fly(patient, Plan with { HoldSeconds = wait * 2f }, from: Fresh);
        JumpFlown waited = Assert.Single(patient.Flown);
        Assert.True(waited.End == JumpEnd.Landed && waited.Stood > 0, $"{waited.End} having stood {waited.Stood}");
    }

    [Fact]
    public void ABotWithNoRoomLeftToStopAndRunInCallsTheJumpOffAtOnce()
    {
        // Told to jump less than the hold's run before the slide's end: wherever it stopped, the run at the jump would end past it.
        float late = Plan.Recipe.Takeoff.X + Plan.Slide - Plan.HoldRun + 8f;
        JumpLive live = Bot(Normal);
        int first = Fly(live, Patient, from: late);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.ViewLate, false, 0), (flown.End, flown.Launched, flown.Stood));
        Assert.All(live.Sent.Skip(first), c => Assert.Equal(0, c.Buttons & (Attack | JumpKey | Duck)));
        // The control: told two hold runs sooner, the same hand has the room, stands in it and jumps (the plan worth any wait).
        JumpLive sooner = Bot(Normal);
        Fly(sooner, Patient, from: late - 2f * Plan.HoldRun - 16f);
        JumpFlown made = Assert.Single(sooner.Flown);
        Assert.True(made.End == JumpEnd.Landed && made.Stood > 0, $"{made.End} having stood {made.Stood}");
    }

    [Fact]
    public void AViewStillNotRoundWhenNoTurnTakesLongerIsCalledOff()
    {
        // The program alone, told of a bot that stands where a late view is stood for and whose view never moves.
        JumpLive live = Bot(Normal);
        JumpPlan kept = live.Keep(Plan with { HoldSeconds = 60f });
        MotionParams motion = MotionEnvelope.Clamp(Normal);
        var at = new AiVector(Plan.Recipe.Takeoff.X + Plan.Slide - 2f * Plan.HoldRun + 4f, Plan.Recipe.Takeoff.Y, Plan.Recipe.Takeoff.Z);
        var body = new AbilityBody(at, default, new AiAngles(0f, Plan.Recipe.TravelYaw, 0f), true);
        IAbilityRun run = live.Program.Begin(kept.Id, body, motion, TickSeconds) ?? throw new InvalidOperationException("the plan is not in the book");
        int commands = 0;
        while (commands < 1000 && !run.Step(body).Over)
            commands++;
        JumpFlown flown = Assert.Single(live.Flown);
        int most = RocketJumpProgram.TurnCommandsAtMost(motion, JumpPilot.ViewGap(new Vector(0f, Plan.Recipe.TravelYaw, 0f), Plan.Recipe.ShotView), TickSeconds);
        Assert.Equal((JumpEnd.ViewLate, false), (flown.End, flown.Launched));
        Assert.True(most < RocketJumpProgram.ApproachCommands, $"the control: the turn's {most} commands are fewer than an approach's");
        // It stood while a run from there would have passed the slide's end before the view was surely round, and gave up on the command no turn outlasts.
        Assert.True(flown.Stood > 0, "the control: it stood");
        Assert.Equal(most - 1, commands);
    }

    public static TheoryData<float, float, float, float> Hands()
    {
        var hands = new TheoryData<float, float, float, float>();
        foreach (string preset in SkillPresets.Names)
        {
            MotionParams m = Preset(preset);
            hands.Add(m.ReactionMs, m.TurnPeakDegPerSec, m.FlickOvershootPct, m.TrackingLagMs);
        }

        // The envelope's corners that turn longest: the slowest turn with the most overshoot, and with the slowest reaction.
        hands.Add(MotionEnvelope.MinReactionMs, MotionEnvelope.MinPeakDegPerSec, MotionEnvelope.MaxOvershootPct, MotionEnvelope.MaxTrackingLagMs);
        hands.Add(Slow.ReactionMs, Slow.TurnPeakDegPerSec, Slow.FlickOvershootPct, Slow.TrackingLagMs);
        return hands;
    }

    /// <summary>The commands each of <see cref="Seeds"/> seeds' views took to come to rest on the shot's line, least first, and the turn's degrees.</summary>
    private static (int[] Commands, float Degrees) Turns(MotionParams motion)
    {
        var commands = new int[Seeds];
        float degrees = 0f;
        for (int seed = 1; seed <= Seeds; seed++)
        {
            // A plan that is launched wherever the view comes round, begun on its takeoff's line: the approach is the turn.
            JumpLive live = Bot(motion, seed: (ulong)seed);
            int first = Fly(live, Plan with { Slide = 100_000f, HoldRun = 0f, HoldSeconds = 0f }, from: Plan.Recipe.Takeoff.X + 2f);
            JumpFlown flown = Assert.Single(live.Flown);
            Assert.True(flown.Launched, $"seed {seed}: {flown.End}");
            commands[seed - 1] = flown.ApproachCommands;
            degrees = JumpPilot.ViewGap(View(live.Sent[first - 1]), Plan.Recipe.ShotView);
        }

        Array.Sort(commands);
        return (commands, degrees);
    }

    [Theory]
    [MemberData(nameof(Hands))]
    public void NoHandsTurnTakesLongerThanTheProgramSaysItCan(float reactionMs, float peak, float overshoot, float lag)
    {
        MotionParams motion = MotionEnvelope.Clamp(new MotionParams(reactionMs, peak, overshoot, lag));
        (int[] commands, float degrees) = Turns(motion);
        int most = RocketJumpProgram.TurnCommandsAtMost(motion, degrees, TickSeconds);
        Assert.True(commands[^1] <= most, $"a turn of {degrees} degrees took {commands[^1]} commands, of the {most} it may");
        // And it says no more than a turn can take: the longest of these is most of it.
        Assert.True(commands[^1] >= most * 0.8f, $"the longest turn took {commands[^1]} commands, of the {most} it may");
    }

    [Theory]
    [MemberData(nameof(Hands))]
    public void AHandsLikelyTurnIsAboutItsMiddleOne(float reactionMs, float peak, float overshoot, float lag)
    {
        MotionParams motion = MotionEnvelope.Clamp(new MotionParams(reactionMs, peak, overshoot, lag));
        (int[] commands, float degrees) = Turns(motion);
        int likely = RocketJumpProgram.TurnCommandsLikely(motion, degrees, TickSeconds);
        Assert.InRange(commands[Seeds / 2], likely * 0.85f, likely * 1.15f);
        Assert.True(likely <= RocketJumpProgram.TurnCommandsAtMost(motion, degrees, TickSeconds));
    }

    [Theory]
    [InlineData(100f, 900f, 5f, 60f)]
    [InlineData(250f, 600f, 10f, 150f)]
    [InlineData(400f, 300f, 20f, 250f)]
    [InlineData(160f, 300f, 40f, 400f)]
    public void TheTriggerIsOnlyEverPressedWithTheViewOnTheShotsLine(float reactionMs, float peak, float overshoot, float lag)
    {
        var motion = new MotionParams(reactionMs, peak, overshoot, lag);
        JumpLive live = Bot(motion);
        int first = Fly(live, Patient, from: 60f);
        JumpFlown flown = Assert.Single(live.Flown);
        float tolerance = RocketJumpProgram.Tolerance(MotionEnvelope.Clamp(motion));
        int pressed = 0;
        for (int k = first; k < live.Sent.Count; k++)
        {
            if ((live.Sent[k].Buttons & Attack) == 0)
                continue;
            pressed++;
            // The view the program saw when it pressed, and the one the command went out with.
            Assert.True(JumpPilot.ViewGap(View(live.Sent[k - 1]), flown.Recipe.ShotView) <= tolerance, $"pressed with the view {JumpPilot.ViewGap(View(live.Sent[k - 1]), flown.Recipe.ShotView)} off");
            Assert.True(JumpPilot.ViewGap(View(live.Sent[k]), flown.Recipe.ShotView) <= tolerance * 1.5f, $"fired with the view {JumpPilot.ViewGap(View(live.Sent[k]), flown.Recipe.ShotView)} off");
        }

        Assert.True(pressed > 0, $"the control: the jump was made ({flown.End})");
    }

    [Fact]
    public void TheToleranceIsTighterTheBetterTheBotTurns()
    {
        Assert.Equal(RocketJumpProgram.LooseDegrees, RocketJumpProgram.Tolerance(new MotionParams(200f, MotionEnvelope.MinPeakDegPerSec, 10f, 100f)));
        Assert.Equal(RocketJumpProgram.TightDegrees, RocketJumpProgram.Tolerance(new MotionParams(200f, MotionEnvelope.MaxPeakDegPerSec, 10f, 100f)));
        float middle = RocketJumpProgram.Tolerance(new MotionParams(200f, 600f, 10f, 100f));
        Assert.True(middle < RocketJumpProgram.LooseDegrees && middle > RocketJumpProgram.TightDegrees);
        // No looser than what a recipe's trials are perturbed by.
        Assert.True(RocketJumpProgram.LooseDegrees <= new JumpSearchSettings().ViewJitter);
    }

    [Fact]
    public void ABotWhoseViewIsNotRoundInTheRoomThePlanAllowsCallsTheJumpOffAndPressesNothing()
    {
        // No slide and no wait, and told to jump a few steps before the takeoff: no hand is round by then, and it runs on past the takeoff.
        JumpLive live = Bot(Normal);
        int first = Fly(live, Plan with { Slide = 0f, HoldRun = 0f, HoldSeconds = 0f }, from: Plan.Recipe.Takeoff.X - 80f);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.ViewLate, false, 0), (flown.End, flown.Launched, flown.Stood));
        Assert.True(flown.Touch.X > Plan.Recipe.Takeoff.X && flown.Touch.X < Plan.Recipe.Takeoff.X + 16f, $"it was called off at {flown.Touch.X}, the takeoff is at {Plan.Recipe.Takeoff.X}");
        Assert.All(live.Sent.Skip(first), c => Assert.Equal(0, c.Buttons & (Attack | JumpKey | Duck)));
        Assert.Equal(0, live.Simulator.Rockets);
        // The control: with the plan's own slide, and a wait, the same bot from the same place makes the jump.
        JumpLive again = Bot(Normal);
        Fly(again, Patient, from: Plan.Recipe.Takeoff.X - 80f);
        Assert.Equal(JumpEnd.Landed, Assert.Single(again.Flown).End);
    }

    [Fact]
    public void AHandTooSlowForThePlansRoomCallsTheJumpOffRatherThanFireSideways()
    {
        // A reaction of 1.5 s and a plan that allows no wait: the bot is past the takeoff and the slide before its view has left the way ahead.
        JumpLive live = Bot(Stunned);
        int first = Fly(live, Plan with { HoldRun = 0f, HoldSeconds = 0f });
        Assert.Equal(JumpEnd.ViewLate, Assert.Single(live.Flown).End);
        Assert.All(live.Sent.Skip(first), c => Assert.Equal(0, c.Buttons & (Attack | JumpKey | Duck)));
        Assert.Equal(0, live.Simulator.Rockets);
    }

    [Fact]
    public void AnApproachThatTakesMoreCommandsThanItMayIsCalledOff()
    {
        // All the room in the world to slide, and fewer commands than a slow hand's reaction alone takes.
        JumpLive live = Bot(Slow, approachCommands: 40);
        int first = Fly(live, Plan with { Slide = 100_000f });
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.ViewLate, false), (flown.End, flown.Launched));
        Assert.InRange(live.Sent.Count - first, 40, 42);
        // The control: given the commands, the same bot with the same plan makes the jump.
        JumpLive again = Bot(Slow);
        Fly(again, Plan with { Slide = 100_000f });
        Assert.Equal(JumpEnd.Landed, Assert.Single(again.Flown).End);
    }

    [Fact]
    public void ABotOffThePlansLineCallsTheJumpOff()
    {
        JumpLive live = Bot(Normal, aside: RocketJumpProgram.AsideMax + 12f);
        int first = Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.Astray, false), (flown.End, flown.Launched));
        Assert.All(live.Sent.Skip(first), c => Assert.Equal(0, c.Buttons & (Attack | JumpKey)));
    }

    [Fact]
    public void ABotALittleOffThePlansLineIsBroughtBackOntoItBeforeTheLaunch()
    {
        JumpLive live = Bot(Normal, aside: 30f);
        Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal(JumpEnd.Landed, flown.End);
        float off = MathF.Abs(flown.Launch.Origin.Y - Plan.Recipe.Takeoff.Y);
        Assert.True(off < 12f, $"it launched {off} off the line, from 30");
    }

    [Fact]
    public void WhileTheJumpRunsTheIntentsFireIsNotPressed()
    {
        JumpLive live = Bot(Normal);
        int first = Fly(live, held: new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0));
        Assert.Equal(JumpEnd.Landed, Assert.Single(live.Flown).End);
        // Of the program's commands (all but the last, which said it was over) the trigger was down for the pilot's own and no more.
        Assert.Equal(JumpPilot.TriggerCommands, live.Sent.Skip(first).Take(live.Sent.Count - first - 1).Count(c => (c.Buttons & Attack) != 0));
        Assert.True(live.Sent.Count - first > 100, "the control: the program ran through a flight");
        // The control: the same intent fires once the jump is over.
        Assert.NotEqual(0, live.Sent[^1].Buttons & Attack);
    }

    [Fact]
    public void DeathEndsAJumpAndItIsCountedSo()
    {
        JumpLive live = Bot(Normal);
        JumpPlan kept = live.Keep(Plan);
        live.WalkPast(170f, Goal);
        live.Command(Goal, new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(JumpLive.Row, kept.Id), OneShotSeq: 100_000));
        for (int i = 0; i < 20; i++)
            live.Command(Goal);
        Assert.True(live.Applier.ProgramRunning(JumpLive.Bot), "the control: the jump is under way");
        live.Alive = false;
        live.Command(Goal);
        Assert.Equal(JumpEnd.Died, Assert.Single(live.Flown).End);
        Assert.False(live.Applier.ProgramRunning(JumpLive.Bot));
    }

    [Fact]
    public void AJumpThatOutlastsItsRowsCommandsIsEndedAndCountedSo()
    {
        // The row allows fewer commands than the flight takes.
        JumpLive live = Bot(Normal, rowCommands: 90);
        Fly(live);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.TimedOut, true), (flown.End, flown.Launched));
    }

    [Fact]
    public void AOneShotThatNamesNoPlanTheBookHoldsBeginsNothing()
    {
        JumpLive live = Bot(Normal);
        live.WalkPast(170f, Goal);
        int first = live.Jump(4242, Goal);
        Assert.Empty(live.Flown);
        Assert.Equal((0L, 1L, 1L), (live.Program.Begun, live.Program.Unknown, live.Applier.AbilityProgramsRefused));
        Assert.Equal(first, live.Sent.Count);
    }

    [Fact]
    public void ThePilotLooksToTheShotUntilTheTriggerIsLetGoThenTheWayItGoes()
    {
        var recipe = new JumpRecipe(new Vector(0f, 0f, 0f), new Vector(1000f, 0f, 0f), 160f, 40f, 180f, 2, true);
        var way = new Vector(0f, 0f, 0f);
        Assert.Equal(recipe.ShotView, JumpPilot.LooksTo(recipe, default));
        int shotOver = 2 + JumpPilot.TriggerCommands;
        Assert.Equal(recipe.ShotView, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, shotOver - 1, true, false, default)));
        Assert.Equal(way, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Launch, shotOver, true, false, default)));
        Assert.Equal(way, JumpPilot.LooksTo(recipe, new JumpPilotState(JumpPhase.Landed, 3, true, true, default)));
    }
}
