using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A blast link's jump as the program flies it (lane G5c-2b): a plan that starts standing
/// (<see cref="JumpPlan.StartsStanding"/>) is walked onto its recipe's start, stood still, its view
/// brought round, and flown as the simulator flew it from there. What it should do is asked of the
/// simulator for the same recipe from the same start, never written down.
/// </summary>
public class RocketJumpLinkPlanTests
{
    private static readonly MotionParams Normal = new(250f, 600f, 10f, 150f);

    /// <summary>
    /// The open hall's recipe from standing (no run-up) or as found (a run-up), with the simulator's own flight of it
    /// from its start: what <see cref="JumpLinkSet.PlanOf"/> makes for a link.
    /// </summary>
    private static (JumpPlan Plan, JumpOutcome Flight) Standing(bool runUp)
    {
        JumpPlan open = JumpPlannerTests.OpenPlan;
        JumpRecipe recipe = runUp ? open.Recipe : open.Recipe with { RunUp = 0f };
        JumpOutcome flight = Flier(JumpPlannerTests.Hall(), MotionEnvelope.Clamp(Normal).TurnPeakDegPerSec).Fly(recipe, recipe.Start(), 700);
        Assert.True(flight.Flew, "the control: the simulator flies the recipe from its start");
        return (open with { Recipe = recipe, StartsStanding = true, Touch = flight.Touch, Apex = flight.Apex, AirSeconds = flight.AirSeconds, Slide = JumpLinkSet.LinkSlide, HoldRun = 0f }, flight);
    }

    /// <summary>A bot standing <paramref name="behind"/> back from the recipe's start along its way and <paramref name="aside"/> to its north, looking along the way (or along the shot's line already).</summary>
    private static JumpLive Near(JumpPlan plan, float behind, float aside, bool aimed = false)
    {
        JumpStart start = plan.Recipe.Start();
        var at = new Vector(start.Origin.X - behind, start.Origin.Y + aside, VoxelMoveWorld.DistEpsilon);
        return new JumpLive(JumpPlannerTests.Hall(), new JumpStart(at, Vector.Zero, aimed ? plan.Recipe.ShotView : start.View, Ducked: false, OnGround: true), Normal);
    }

    /// <summary>With its view already on the shot's line, a bot walked onto the start still waits for its body to stop: the simulator flew it from standing.</summary>
    [Fact]
    public void AStandingJumpWaitsForTheBodyToStopEvenWithItsViewRound()
    {
        (JumpPlan plan, _) = Standing(runUp: false);
        JumpLive live = Near(plan, behind: 60f, aside: 0f, aimed: true);
        live.Jump(live.Keep(plan).Id, plan.Goal);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.True(flown.Launched && flown.ApproachCommands > 5, $"the control: it walked onto the start first ({flown.ApproachCommands} commands)");
        Assert.True(Speed(flown.Launch.Velocity) <= RocketJumpProgram.StillSpeed, $"launched moving at {flown.Launch.Velocity}");
    }

    private static float Speed(Vector v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    [Fact]
    public void AStandingJumpIsWalkedOntoItsStartStoodStillAndFlownAsTheSimulatorFlewIt()
    {
        (JumpPlan plan, JumpOutcome flight) = Standing(runUp: false);
        JumpLive live = Near(plan, behind: 40f, aside: 20f);
        JumpPlan kept = live.Keep(plan);
        int first = live.Jump(kept.Id, plan.Goal);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal((JumpEnd.Landed, true), (flown.End, flown.Launched));
        // It launched on its start, standing: as near as the placing allows, and still.
        Assert.True(Flat(flown.Launch.Origin, plan.Recipe.Takeoff) <= RocketJumpProgram.PlaceSlack + 1f, $"launched at {flown.Launch.Origin}, the takeoff is {plan.Recipe.Takeoff}");
        Assert.True(Speed(flown.Launch.Velocity) <= RocketJumpProgram.StillSpeed, $"launched at {flown.Launch.Velocity}");
        // The control: it had moved to get there.
        Assert.True(Flat(live.Track[first].Origin, plan.Recipe.Takeoff) > 30f, "the control: it began away from the start");
        Assert.True(Flat(flown.Touch, flight.Touch) <= 24f, $"it came down at {flown.Touch}, the simulator from the start at {flight.Touch}");
    }

    [Fact]
    public void ARunningJumpFromStandingRunsOnlyOnceItsViewIsRound()
    {
        (JumpPlan plan, JumpOutcome flight) = Standing(runUp: true);
        Assert.True(plan.Recipe.RunUp > 0f, "the control: the open hall's recipe runs at its jump");
        JumpLive live = Near(plan, behind: 30f, aside: 0f);
        JumpPlan kept = live.Keep(plan);
        int first = live.Jump(kept.Id, plan.Goal);
        JumpFlown flown = Assert.Single(live.Flown);
        Assert.Equal(JumpEnd.Landed, flown.End);
        JumpStart start = plan.Recipe.Start();
        float tolerance = RocketJumpProgram.Tolerance(MotionEnvelope.Clamp(Normal));
        // The command that sets it running from its start (past the placing's slack, on the way) is sent with its view on the shot's line.
        int run = live.Track.FindIndex(first, p => Flat(p.Origin, start.Origin) > RocketJumpProgram.PlaceSlack * 2f && p.Origin.X > start.Origin.X);
        Assert.True(run > first, "the control: it ran from its start");
        UserCmd sent = live.Sent[run];
        Assert.True(JumpPilot.ViewGap(new Vector(sent.Pitch, sent.Yaw, 0f), plan.Recipe.ShotView) <= tolerance, $"it set off looking {JumpPilot.ViewGap(new Vector(sent.Pitch, sent.Yaw, 0f), plan.Recipe.ShotView)} degrees off the shot");
        Assert.True(Flat(flown.Touch, flight.Touch) <= 24f, $"it came down at {flown.Touch}, the simulator from the start at {flight.Touch}");
    }

    [Fact]
    public void AStandingJumpBegunFarFromItsStartIsCalledOff()
    {
        (JumpPlan plan, _) = Standing(runUp: false);
        JumpLive live = Near(plan, behind: RocketJumpProgram.PlaceReach + 40f, aside: 0f);
        JumpPlan kept = live.Keep(plan);
        live.Jump(kept.Id, plan.Goal);
        Assert.Equal((JumpEnd.Astray, false), (Assert.Single(live.Flown).End, live.Flown[0].Launched));
        // The control: from near, the same plan flies.
        JumpLive near = Near(plan, behind: 40f, aside: 0f);
        near.Jump(near.Keep(plan).Id, plan.Goal);
        Assert.Equal(JumpEnd.Landed, Assert.Single(near.Flown).End);
    }
}
