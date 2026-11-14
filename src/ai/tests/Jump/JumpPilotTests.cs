using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The jump's pilot (plan_bots_teams_tf2_part_g G.5 step 2): a recipe turned into commands a tick
/// at a time, flown here in the simulator on an open field. No fact names a height or a distance
/// the blast gives: where a flight goes is asked of the simulator in the fact, and the pilot is
/// held to what it does with that.
/// </summary>
public class JumpPilotTests
{
    /// <summary>A crouched jump from standing with the rocket behind and under: up and along.</summary>
    private static JumpRecipe Standing(float along = 3000f, float pitch = 60f, float yawOffset = 180f, int shotDelay = 2, bool ducked = true, float runUp = 0f)
        => new(OnTheField(), OnTheField() + new Vector(along, 0f, 0f), runUp, pitch, yawOffset, shotDelay, ducked);

    /// <summary>The same jump with its landing set <paramref name="share"/> of the way the unsteered flight goes: short of it, so the air can get there.</summary>
    private static (JumpRecipe Recipe, JumpFlight Unsteered) ShortOfTheUnsteered(JumpFlier flier, float share = 0.7f, float runUp = 0f)
    {
        JumpRecipe far = Standing(runUp: runUp);
        JumpFlight unsteered = Unsteered(flier, far, Pilot(flier, far));
        float range = unsteered.To.X - far.Takeoff.X;
        Assert.True(unsteered.Landed && unsteered.Blasts == 1 && range > 100f, $"the control: the unsteered flight is a blast jump that goes somewhere: {range} along");
        return (far with { Landing = far.Takeoff + new Vector(range * share, 0f, 0f) }, unsteered);
    }

    [Fact]
    public void FedToTheSimulatorItFliesABlastJump()
    {
        JumpFlier flier = Flier(Field());
        JumpOutcome jump = flier.Fly(Standing(), Standing().Start(), 700);
        Assert.True(jump.Flew);
        Assert.Equal(1, jump.Rockets);
        Assert.True(jump.BlastDamage > 0f);
        // The control: the same recipe with the rocket sent into the sky is a crouched hop.
        JumpOutcome hop = flier.Fly(Standing(pitch: -80f), Standing(pitch: -80f).Start(), 700);
        Assert.True(hop.Flew && hop.BlastDamage == 0f && hop.Apex > 40f, $"the control: a hop of {hop.Apex}");
        Assert.True(jump.Apex > 2f * hop.Apex, $"the jump rose {jump.Apex}, the hop {hop.Apex}");
    }

    [Fact]
    public void ASteeredFlightLandsNearerItsTargetThanTheSameFlightUnsteered()
    {
        JumpFlier flier = Flier(Field());
        (JumpRecipe recipe, JumpFlight unsteered) = ShortOfTheUnsteered(flier);
        Piloted steered = Pilot(flier, recipe);
        // The same approach, jump and shot: only the air's moves differ.
        Assert.Equal(unsteered.Impacts[0].At, Unsteered(flier, recipe, steered).Impacts[0].At);
        float left = Flat(unsteered.To, recipe.Landing), steeredLeft = Flat(steered.Outcome.Touch, recipe.Landing);
        Assert.True(left > 30f, $"the control: unsteered it lands {left} from the target");
        Assert.True(steeredLeft < 4f, $"steered it lands {steeredLeft} from the target, unsteered {left}");
    }

    [Fact]
    public void ASteeredFlightComesDownStoppedOverItsTarget()
    {
        JumpFlier flier = Flier(Field());
        (JumpRecipe recipe, JumpFlight unsteered) = ShortOfTheUnsteered(flier);
        Piloted steered = Pilot(flier, recipe);
        JumpPoint before = steered.Track[steered.Touch - 1];
        float speed = MathF.Sqrt(before.Velocity.X * before.Velocity.X + before.Velocity.Y * before.Velocity.Y);
        JumpPoint plain = unsteered.Track[unsteered.Landing - 1];
        Assert.True(MathF.Sqrt(plain.Velocity.X * plain.Velocity.X + plain.Velocity.Y * plain.Velocity.Y) > 50f, "the control: unsteered it comes down moving");
        Assert.True(speed < 5f, $"it came down moving at {speed} over the ground");
        Assert.True(Flat(steered.Outcome.Rest, recipe.Landing) < 4f, $"it stopped {Flat(steered.Outcome.Rest, recipe.Landing)} from the target");
    }

    [Fact]
    public void ARunAtTheJumpIsSteeredToItsTargetToo()
    {
        JumpFlier flier = Flier(Field());
        (JumpRecipe recipe, JumpFlight unsteered) = ShortOfTheUnsteered(flier, runUp: 160f);
        Assert.True(Flat(unsteered.To, recipe.Landing) > 30f, "the control: unsteered it lands off the target");
        Assert.True(Flat(Pilot(flier, recipe).Outcome.Touch, recipe.Landing) < 4f);
    }

    [Fact]
    public void ItNeverTurnsTheViewFasterThanItIsTold()
    {
        foreach (float turn in (float[])[150f, 600f])
        {
            JumpFlier flier = Flier(Field(), turn);
            JumpRecipe recipe = Standing();
            Piloted flown = Pilot(flier, recipe);
            Assert.True(flown.Outcome.Flew && flown.Touch < flown.Sent.Count - 5, "the control: the whole jump, and commands after its landing");
            float most = 0f, total = 0f;
            Vector view = recipe.Start().View;
            foreach (JumpCommand c in flown.Sent)
            {
                float turned = JumpPilot.ViewGap(view, c.View);
                most = MathF.Max(most, turned);
                total += turned;
                view = c.View;
            }

            float allowed = turn * TickSeconds;
            Assert.True(total > 300f, $"the control: the view goes round to the shot's and back: {total} degrees");
            Assert.InRange(most, allowed * 0.99f, allowed + 1e-3f);
        }
    }

    [Fact]
    public void TheRocketLeavesAlongTheRecipesView()
    {
        JumpRecipe recipe = Standing(pitch: 55f, yawOffset: 160f);
        Piloted flown = Pilot(Flier(Field()), recipe);
        Assert.Equal(new Vector(55f, 160f, 0f), recipe.ShotView);
        Assert.True(flown.Trigger > 10, "the control: the view had to be turned there first");
        Assert.True(JumpPilot.ViewGap(flown.Sent[flown.Trigger].View, recipe.ShotView) <= JumpPilot.AimDegrees);
        Assert.Equal(1, flown.Outcome.Rockets);
    }

    [Fact]
    public void TheTriggerComesTheRecipesDelayAfterTheJump()
    {
        JumpFlier flier = Flier(Field());
        foreach (int delay in (int[])[-3, 0, 4])
        {
            Piloted flown = Pilot(flier, Standing(shotDelay: delay));
            Assert.Equal(delay, flown.Trigger - flown.Jump);
            Assert.Equal(JumpPilot.TriggerCommands, flown.Sent.Count(c => (c.Buttons & InAttack) != 0));
            Assert.Equal(1, flown.Sent.Count(c => (c.Buttons & InJump) != 0));
        }
    }

    [Fact]
    public void ACrouchedRecipeHoldsTheDuckFromTheJumpToTheLanding()
    {
        Piloted flown = Pilot(Flier(Field()), Standing(ducked: true));
        Assert.True(flown.Jump > 0 && flown.Touch > flown.Jump + 20, "the control: an approach, then a flight");
        for (int i = 0; i < flown.Sent.Count; i++)
            Assert.Equal(i >= flown.Jump && i <= flown.Touch, (flown.Sent[i].Buttons & InDuck) != 0);
    }

    [Fact]
    public void AnUncrouchedRecipeNeverDucks()
    {
        Piloted flown = Pilot(Flier(Field()), Standing(ducked: false));
        Assert.True(flown.Outcome.Flew);
        Assert.DoesNotContain(flown.Sent, c => (c.Buttons & InDuck) != 0);
        Assert.DoesNotContain(flown.Track, p => p.Ducking);
    }

    [Fact]
    public void AStandingRecipeJumpsFromWhereItStands()
    {
        Piloted flown = Pilot(Flier(Field()), Standing());
        Assert.True(flown.Jump > 10, "the control: commands went by before the jump");
        Assert.All(flown.Sent.Take(flown.Jump + 1), c => Assert.Equal((0f, 0f), (c.ForwardMove, c.SideMove)));
        Assert.Equal(OnTheField().X, flown.Track[flown.Jump - 1].Origin.X, 3);
    }

    [Fact]
    public void ARunUpRecipeJumpsOnTheTakeoffsLineAtARun()
    {
        JumpRecipe recipe = Standing(runUp: 200f);
        Assert.Equal(recipe.Takeoff.X - 200f, recipe.Start().Origin.X, 3);
        Piloted flown = Pilot(Flier(Field()), recipe);
        JumpPoint at = flown.Track[flown.Jump - 1];
        // Within half a command's travel of the line, and at most of his run speed (he runs backwards: the view is behind).
        Assert.InRange(at.Origin.X, recipe.Takeoff.X - at.Velocity.X * TickSeconds * 0.5f - JumpPilot.LineSlack, recipe.Takeoff.X + at.Velocity.X * TickSeconds);
        Assert.True(at.Velocity.X > 0.8f * RunSpeed, $"at the line he ran at {at.Velocity.X}");
        // The mod slows a backward move and not a sideways one, so a run with the view turning bends a little.
        Assert.InRange(at.Origin.Y, recipe.Takeoff.Y - 8f, recipe.Takeoff.Y + 8f);
    }

    [Fact]
    public void ItSetsOffOnceTheTurnLeftFitsTheRunLeftAndIsOnTheLineWithTheViewThere()
    {
        // 100 units to run and a view that turns 90 degrees a second: two seconds of turning, well under half a second of running.
        JumpRecipe recipe = Standing(runUp: 100f);
        Piloted flown = Pilot(Flier(Field(), turn: 90f), recipe);
        int setOff = flown.Sent.FindIndex(c => c.ForwardMove != 0f || c.SideMove != 0f);
        Assert.True(setOff > 60 && setOff < flown.Jump, $"it set off at command {setOff} and jumped at {flown.Jump}");
        Assert.True(JumpPilot.ViewGap(flown.Sent[setOff].View, recipe.ShotView) > 10f, "the control: it set off with the view still turning");
        JumpPoint at = flown.Track[flown.Jump - 1];
        Assert.True(at.Velocity.X > 0.5f * RunSpeed, $"it did not wait on the line: it crossed it at {at.Velocity.X}");
        Assert.True(JumpPilot.ViewGap(flown.Sent[flown.Jump].View, recipe.ShotView) <= JumpPilot.AimDegrees);
    }

    [Fact]
    public void OnTheLineWithTheViewNotYetThereItWaits()
    {
        // A recipe with a run-up, begun on the line itself with the view level: nothing to run, a turn to make.
        JumpRecipe recipe = Standing(runUp: 100f);
        JumpStart onLine = recipe.Start() with { Origin = recipe.Takeoff };
        Piloted flown = Pilot(Flier(Field(), turn: 90f), recipe, onLine);
        Assert.True(flown.Jump > 60, $"the jump came at command {flown.Jump}");
        Assert.All(flown.Sent.Take(flown.Jump), c => Assert.Equal((0f, 0f, 0), (c.ForwardMove, c.SideMove, c.Buttons)));
        Assert.True(JumpPilot.ViewGap(flown.Sent[flown.Jump].View, recipe.ShotView) <= JumpPilot.AimDegrees);
        Assert.True(flown.Outcome.Flew);
    }

    [Fact]
    public void AfterTheLandingNothingIsHeld()
    {
        Piloted flown = Pilot(Flier(Field()), Standing());
        Assert.True(flown.Sent.Count > flown.Touch + 1, "the control: commands after the landing");
        Assert.All(flown.Sent.Skip(flown.Touch + 1), c => Assert.Equal((0, 0f, 0f), (c.Buttons, c.ForwardMove, c.SideMove)));
    }

    [Fact]
    public void TheWishIsTheCommandsMovesInTheWorldsFrame()
    {
        // In the air, moving east, the landing behind and to the north: the wish has both parts, and the view is askew.
        var recipe = new JumpRecipe(OnTheField(), OnTheField() + new Vector(0f, 300f, 0f), 0f, 60f, 180f, 0, true);
        var body = new JumpBody(OnTheField() + new Vector(100f, 0f, 400f), new Vector(200f, 0f, 100f), new Vector(10f, 40f, 0f), OnGround: false);
        var state = new JumpPilotState(JumpPhase.Launch, 30, Airborne: true, Felt: true, new Vector(200f, 0f, 112f));
        JumpPilotStep step = JumpPilot.Step(recipe, state, body, Limits());
        Assert.True(step.Wish.X < -1f && step.Wish.Y > 1f && step.Wish.Z == 0f, $"the control: a wish west and north: {step.Wish}");
        float yaw = step.Command.View.Y * (MathF.PI / 180f);
        var forward = new Vector(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        var right = new Vector(MathF.Sin(yaw), -MathF.Cos(yaw), 0f);
        Vector made = forward * step.Command.ForwardMove + right * step.Command.SideMove;
        Assert.True((made - step.Wish).Length < 1e-2f, $"the moves make {made}, the wish is {step.Wish}");
    }

    [Fact]
    public void AFlightWhoseBlastNeverComesIsSteeredOnceItHasHadTimeTo()
    {
        // The rocket goes into the sky: a hop, and a landing a little to the north of where the hop comes down.
        JumpFlier flier = Flier(Field());
        var recipe = new JumpRecipe(OnTheField(), OnTheField() + new Vector(0f, 12f, 0f), 0f, -80f, 0f, 0, true);
        Piloted flown = Pilot(flier, recipe);
        JumpFlight unsteered = Unsteered(flier, recipe, flown);
        Assert.True(unsteered.Blasts == 0 && unsteered.Landed && Flat(unsteered.To, recipe.Takeoff) < 0.5f, "the control: unsteered it comes down where it stood");
        Assert.True(unsteered.Landing - unsteered.Takeoff > JumpPilot.BlastWaitCommands + 10, "the control: the hop outlasts the wait for a blast");
        Assert.All(flown.Sent.Skip(flown.Jump + 1).Take(JumpPilot.BlastWaitCommands), c => Assert.Equal((0f, 0f), (c.ForwardMove, c.SideMove)));
        Assert.True(Flat(flown.Outcome.Touch, recipe.Landing) < 6f, $"it came down {Flat(flown.Outcome.Touch, recipe.Landing)} from the landing");
    }

    [Fact]
    public void ACommandAllocatesNothing()
    {
        JumpRecipe recipe = Standing();
        JumpPilotLimits limits = Limits();
        var body = new JumpBody(OnTheField() + new Vector(100f, 20f, 400f), new Vector(200f, 30f, 100f), new Vector(10f, 40f, 0f), OnGround: false);
        var flying = new JumpPilotState(JumpPhase.Launch, 30, Airborne: true, Felt: true, new Vector(200f, 30f, 112f));
        var ground = new JumpBody(recipe.Start().Origin, Vector.Zero, recipe.Start().View, OnGround: true);
        float moved = 0f;

        // A warm-up, then the quietest of three windows: a command that allocates does so in every window, and a
        // loaded box's tiering (a hot method compiled again mid-measure) lands in one.
        long allocated = long.MaxValue;
        for (int window = -1; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
            {
                moved += MathF.Abs(JumpPilot.Step(recipe, flying, body, limits).Command.SideMove);
                moved += JumpPilot.Step(recipe, default, ground, limits).Command.View.X;
                moved += JumpPilot.Step(recipe, flying with { Phase = JumpPhase.Landed }, body, limits).Command.View.X;
            }

            if (window >= 0)
                allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0L, allocated);
        Assert.True(moved > 1000f, "the control: the commands steered and turned");
    }

    [Fact]
    public void TheSameInputsGiveTheSameFlightBitForBit()
    {
        (JumpRecipe recipe, _) = ShortOfTheUnsteered(Flier(Field()));
        Piloted first = Pilot(Flier(Field()), recipe), again = Pilot(Flier(Field()), recipe);
        Assert.Equal(first.Sent, again.Sent);
        Assert.Equal(first.Track, again.Track);
        Assert.Equal(first.Outcome, again.Outcome);
    }

    [Fact]
    public void TheOutcomeIsTheFlightTheSimulatorFliesFromTheSameCommands()
    {
        JumpFlier flier = Flier(Field());
        (JumpRecipe recipe, _) = ShortOfTheUnsteered(flier);
        Piloted flown = Pilot(flier, recipe);
        JumpFlight replayed = flier.Sim.Fly(recipe.Start(), [.. flown.Sent]);
        Assert.True(replayed.Landed && flown.Outcome.Flew);
        Assert.Equal((replayed.To, replayed.AirSeconds, replayed.Apex, replayed.Rockets), (flown.Outcome.Touch, flown.Outcome.AirSeconds, flown.Outcome.Apex, flown.Outcome.Rockets));
        Assert.Equal(replayed.BlastDamage, flown.Outcome.BlastDamage, 3);
        Assert.Equal((replayed.Landing + 1) * TickSeconds, flown.Outcome.Seconds, 4);
        Assert.Equal(replayed.Track[^1].Origin, flown.Outcome.Rest);
    }

    [Fact]
    public void ARunThatNeverComesDownSaysSo()
    {
        JumpOutcome cut = Flier(Field()).Fly(Standing(), Standing().Start(), 60);
        Assert.Equal((false, 60, false), (cut.Flew, cut.Commands, cut.OnGround));
        Assert.True(cut.Rest.Z > 10f, $"where the run ended: {cut.Rest}");
    }

    [Fact]
    public void AnAngleIsWrappedIntoHalfATurnEitherWay()
    {
        Assert.Equal((170f, -170f, 180f, 180f, 0f), (JumpPilot.WrapDegrees(170f), JumpPilot.WrapDegrees(190f), JumpPilot.WrapDegrees(180f),
                                                    JumpPilot.WrapDegrees(-180f), JumpPilot.WrapDegrees(720f)));
        // The short way round: from 170 to -170 is 20 degrees, through 180.
        Assert.Equal(20f, JumpPilot.ViewGap(new Vector(0f, 170f, 0f), new Vector(0f, -170f, 0f)), 4);
        Assert.Equal(new Vector(0f, 175f, 0f), JumpPilot.Turn(new Vector(0f, 170f, 0f), new Vector(0f, -170f, 0f), 5f));
    }
}
