using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The desk and the tally (plan_bots_teams_tf2_part_g G.5 steps 6 and 7): the planner is touched by
/// the desk's worker alone, so nothing is flown when a brain asks; and every jump a bot made is
/// counted by how it ended and flown again in the simulator from its own launch.
/// </summary>
public class JumpDeskTests
{
    private static JumpDesk Desk(out JumpPlanner planner)
    {
        var desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally());
        planner = JumpPlannerTests.Planner(JumpPlannerTests.Hall());
        desk.Open(planner);
        return desk;
    }

    private static JumpStretchAsk At(float x, int tick = 100) => JumpPlannerTests.Along(from: x) with { At = new Tick(tick) };

    [Fact]
    public void ABrainsAskFliesNothingAndTheWorkersWorkAnswersIt()
    {
        JumpDesk desk = Desk(out JumpPlanner planner);
        int cues = 0;
        desk.Cue = () => cues++;
        // Asked, and asked again a decide later: no plan yet, one cue to the worker, and the simulator has not flown.
        Assert.Null(desk.Book.Advise(At(160f)));
        Assert.Null(desk.Book.Advise(At(182f, 106)));
        Assert.Equal((1, 0L, 0), (cues, planner.Flights, planner.Floods));

        Assert.Equal(1, desk.Work());
        Assert.True(planner.Flights > 0, "the control: the worker's work flew the search");
        JumpPlan? plan = desk.Book.Advise(At(204f, 112));
        Assert.Equal(JumpPlannerTests.OpenPlan.Recipe, plan?.Recipe);
        JumpDeskCounts counts = desk.Counts;
        Assert.Equal((1L, planner.Flights, 1L), (counts.Planned, counts.Flights, counts.Floods));
    }

    [Fact]
    public void WithNoLevelOpenNothingIsPlanned()
    {
        var desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally());
        Assert.False(desk.IsOpen);
        Assert.Null(desk.Book.Advise(At(160f)));
        Assert.Equal(0, desk.Work());
        Assert.Equal(1, desk.Book.Counts.Waiting);
    }

    [Fact]
    public void AnotherLevelsPlannerStartsFromAnEmptyBook()
    {
        JumpDesk desk = Desk(out _);
        desk.Book.Advise(At(160f));
        desk.Work();
        Assert.Equal(1, desk.Book.Counts.Plans);
        desk.Open(JumpPlannerTests.Planner(JumpPlannerTests.Hall()));
        Assert.Equal(0, desk.Book.Counts.Plans);
        desk.Close();
        Assert.False(desk.IsOpen);
    }

    /// <summary>A planner that throws on every stretch west of x = 1,000 and answers the open hall's plan east of it.</summary>
    private sealed class Faulty : IJumpPlanner
    {
        public long Flights => 0;

        public int Floods => 0;

        public JumpPlanned Plan(in JumpStretchAsk ask)
            => ask.Origin.X < 1000f ? throw new InvalidOperationException("no such stretch") : new JumpPlanned(JumpPlannerTests.OpenPlan, JumpRefusal.None, 0);

        public JumpOutcome Predict(in JumpRecipe recipe, in JumpStart start) => throw new InvalidOperationException("no such flight");
    }

    [Fact]
    public void AStretchThePlannerThrowsOnIsAnsweredNoneAndTheWorkGoesOn()
    {
        var desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally());
        desk.Open(new Faulty());
        desk.Book.Advise(At(160f));
        desk.Book.Advise(At(1500f));
        desk.Flew(Flown(JumpEnd.Landed, JumpPlannerTests.OpenPlan.Touch));
        Assert.Equal(3, desk.Work());
        Assert.Equal((2L, 1L, 1, 0), (desk.Counts.Faults, desk.Book.Refused(JumpRefusal.Faulted), desk.Book.Counts.Plans, desk.Book.Counts.Waiting));
        Assert.Contains("no such", desk.LastFault, StringComparison.Ordinal);
        // The stretch it threw on is not asked about again.
        desk.Book.Advise(At(170f, 106));
        Assert.Equal(0, desk.Book.Counts.Waiting);
    }

    private static JumpFlown Flown(JumpEnd end, Vector touch, bool launched = true, bool felt = true)
    {
        JumpPlan plan = JumpPlannerTests.OpenPlan;
        JumpStart launch = plan.Recipe.Start() with { Origin = plan.Recipe.Takeoff, Velocity = new Vector(RunSpeed, 0f, 0f), View = plan.Recipe.ShotView };
        return new JumpFlown(plan, plan.Recipe, launch, launched, 0f, 40, 120, felt, touch, end, TickSeconds) { Stood = 20, Rise = 150f };
    }

    [Fact]
    public void AJumpABotMadeIsFlownAgainInTheSimulatorFromItsOwnLaunch()
    {
        JumpDesk desk = Desk(out _);
        JumpPlan plan = JumpPlannerTests.OpenPlan;
        JumpFlown flown = Flown(JumpEnd.Landed, plan.Touch + new Vector(-20f, 0f, 0f));
        desk.Flew(flown);
        JumpTallied before = Assert.Single(desk.Tally.Last());
        Assert.Equal((null, null), (before.SimTouch, before.Gap));

        Assert.Equal(1, desk.Work());
        JumpTallied after = Assert.Single(desk.Tally.Last());
        JumpOutcome alone = Flier(JumpPlannerTests.Hall()).Fly(flown.Recipe, flown.Launch, new JumpSearchSettings().MaxCommands);
        Assert.True(alone.Flew, "the control: the pilot alone flies it from that launch");
        Assert.Equal((alone.Touch, alone.AirSeconds, true), (after.SimTouch, after.SimAirSeconds, after.SimFlew));
        Assert.Equal(Flat(flown.Touch, alone.Touch), after.Gap);
        Assert.Equal((1L, 1L), (desk.Counts.Predicted, desk.Tally.Counts.Predicted));
    }

    [Fact]
    public void AJumpThatWasNeverLaunchedIsCountedAndNotFlownAgain()
    {
        JumpDesk desk = Desk(out JumpPlanner planner);
        desk.Flew(Flown(JumpEnd.ViewLate, JumpPlannerTests.OpenPlan.Recipe.Takeoff, launched: false));
        Assert.Equal(0, desk.Work());
        Assert.Equal((1L, 1L, 0L), (desk.Tally.Counts.Ended, desk.Tally.Counts.ViewLate, planner.Flights));
    }

    [Fact]
    public void ALandingIsCountedByWhereItCameDownAgainstThePlan()
    {
        var tally = new JumpTally();
        JumpPlan plan = JumpPlannerTests.OpenPlan;
        tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(JumpTally.NearUnits - 4f, 0f, 0f)));
        tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(0f, -(JumpTally.NearUnits - 4f), 0f)));
        tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(-(JumpTally.NearUnits + 40f), 0f, 0f)));
        tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(-300f, 30f, 0f)));
        tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(JumpTally.NearUnits + 40f, 0f, 0f)));
        tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(0f, 200f, 0f)));
        // A little short and far to a side is elsewhere, not short.
        tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(-30f, 200f, 0f)));
        JumpTallyCounts counts = tally.Counts;
        Assert.Equal((7L, 2L, 2L, 3L), (counts.Ended, counts.LandedNear, counts.LandedShort, counts.LandedElsewhere));
    }

    [Fact]
    public void EveryOtherEndingIsCountedUnderItsOwnName()
    {
        var tally = new JumpTally();
        Vector at = JumpPlannerTests.OpenPlan.Recipe.Takeoff;
        foreach (JumpEnd end in new[] { JumpEnd.ViewLate, JumpEnd.Astray, JumpEnd.Astray, JumpEnd.Fizzled, JumpEnd.Died, JumpEnd.Died, JumpEnd.Died, JumpEnd.TimedOut })
            tally.Ended(Flown(end, at, launched: false));
        JumpTallyCounts counts = tally.Counts;
        Assert.Equal((8L, 1L, 2L, 1L, 3L, 1L), (counts.Ended, counts.ViewLate, counts.Astray, counts.Fizzled, counts.Died, counts.TimedOut));
        Assert.Equal((0L, 0L, 0L), (counts.LandedNear, counts.LandedShort, counts.LandedElsewhere));
    }

    [Fact]
    public void AFizzleOnWhichABlastWasFeltIsCountedApartFromOneThatFeltNone()
    {
        var tally = new JumpTally();
        Vector at = JumpPlannerTests.OpenPlan.Recipe.Takeoff;
        tally.Ended(Flown(JumpEnd.Fizzled, at, felt: true));
        tally.Ended(Flown(JumpEnd.Fizzled, at, felt: false));
        tally.Ended(Flown(JumpEnd.Fizzled, at, felt: false));
        // A landing felt its blast too, and is no fizzle.
        tally.Ended(Flown(JumpEnd.Landed, JumpPlannerTests.OpenPlan.Touch, felt: true));
        Assert.Equal((4L, 3L, 1L), (tally.Counts.Ended, tally.Counts.Fizzled, tally.Counts.FizzledFelt));
    }

    [Fact]
    public void TheTallyKeepsItsLastJumpsInFullAndListsThem()
    {
        var tally = new JumpTally();
        JumpPlan plan = JumpPlannerTests.OpenPlan;
        for (int i = 0; i < JumpTally.Kept + 5; i++)
            tally.Ended(Flown(JumpEnd.Landed, plan.Touch + new Vector(i, 0f, 0f)));
        JumpTallied[] last = tally.Last();
        Assert.Equal((JumpTally.Kept, 6, JumpTally.Kept + 5), (last.Length, last[0].Serial, last[^1].Serial));
        using var json = System.Text.Json.JsonDocument.Parse(tally.LastJson());
        System.Text.Json.JsonElement first = json.RootElement[0];
        Assert.Equal((6, "Landed", 5.0), (first.GetProperty("serial").GetInt32(), first.GetProperty("end").GetString(), first.GetProperty("off_plan").GetDouble()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("gap").ValueKind);
        // How long it stood for its view of its approach, and how high it rose beside the plan's apex.
        Assert.Equal((Math.Round(40 * TickSeconds, 3), Math.Round(20 * TickSeconds, 3)), (first.GetProperty("approach_seconds").GetDouble(), first.GetProperty("stood_seconds").GetDouble()));
        Assert.Equal((150.0, Math.Round(plan.Apex, 1)), (first.GetProperty("rise").GetDouble(), Math.Round(first.GetProperty("plan_apex").GetDouble(), 1)));
    }
}
