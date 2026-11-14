using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The jump's reach as the simulator gives it (plan_bots_teams_tf2_part_g G.5 step 4): the facts
/// hold it to the flights it is made of, never to a number, so it follows the blast's physics.
/// </summary>
public class JumpEnvelopeTests
{
    [Fact]
    public void TheRiseIsTheHighestAnyGridFlightRoseAndTheRangeTheFurthestAnyWent()
    {
        var settings = new JumpSearchSettings();
        JumpEnvelope envelope = Envelope(settings);
        // The grids' flights, and the one that measures a second rocket's lift.
        Assert.Equal(settings.Up.Pitches * settings.Up.Delays + settings.Forward.Pitches * settings.Forward.Delays + 1, envelope.Flights);

        // Every grid recipe again, by hand: none rises higher or goes further, and one does each.
        JumpFlier flier = Flier(JumpEnvelope.OpenAir());
        float rise = 0f, range = 0f;
        foreach (JumpGrid grid in (JumpGrid[])[settings.Up, settings.Forward])
        {
            for (float pitch = grid.PitchFrom; pitch <= grid.PitchTo; pitch += grid.PitchStep)
            {
                for (int delay = grid.DelayFrom; delay <= grid.DelayTo; delay += grid.DelayStep)
                {
                    var recipe = new JumpRecipe(JumpEnvelope.Takeoff, JumpEnvelope.Takeoff + new Vector(JumpEnvelope.Far, 0f, 0f), grid.RunUp, pitch, 180f, delay, true);
                    JumpOutcome o = flier.Fly(recipe, recipe.Start(), settings.MaxCommands);
                    if (o.Flew && o.BlastDamage > 0f)
                        (rise, range) = (MathF.Max(rise, o.Apex), MathF.Max(range, o.Touch.X - JumpEnvelope.Takeoff.X));
                }
            }
        }

        Assert.Equal((rise, range), (envelope.Rise, envelope.Range));
    }

    [Fact]
    public void ABlastJumpReachesFarPastAHop()
    {
        JumpEnvelope envelope = Envelope();
        var hop = new JumpRecipe(JumpEnvelope.Takeoff, JumpEnvelope.Takeoff + new Vector(JumpEnvelope.Far, 0f, 0f), 160f, -80f, 180f, 0, true);
        JumpOutcome hopped = Flier(JumpEnvelope.OpenAir()).Fly(hop, hop.Start(), 600);
        Assert.True(hopped.Flew && hopped.BlastDamage == 0f && hopped.Apex > 40f, "the control: a running crouched hop, no blast");
        Assert.True(envelope.Rise > 2f * hopped.Apex, $"the rise {envelope.Rise}, a hop's {hopped.Apex}");
        Assert.True(envelope.Range > 2f * (hopped.Touch.X - JumpEnvelope.Takeoff.X), $"the range {envelope.Range}, a hop's {hopped.Touch.X - JumpEnvelope.Takeoff.X}");
    }

    [Fact]
    public void AFlightWithNoBlastIsNoPartOfIt()
    {
        // One grid cell each family, fired into the sky: hops only, and the envelope is empty.
        var skyward = new JumpSearchSettings { Up = new JumpGrid(0f, -80f, -80f, 10f, 0, 0, 2), Forward = new JumpGrid(160f, -80f, -80f, 10f, 0, 0, 3) };
        JumpEnvelope envelope = Envelope(skyward);
        Assert.Equal((0f, 0f, 3), (envelope.Rise, envelope.Range, envelope.Flights));
    }

    [Fact]
    public void TheOpenAirIsHigherAndLongerThanTheReach()
    {
        // The envelope is measured in a room: a flight that met its ceiling or its far wall would be the room's size, not the jump's.
        JumpEnvelope envelope = Envelope();
        VoxelColumns air = JumpEnvelope.OpenAir();
        Assert.True(envelope.Rise < 0.5f * air.Spans(0, 0)[0].Top, $"the rise {envelope.Rise} in air {air.Spans(0, 0)[0].Top} tall");
        Assert.True(JumpEnvelope.Takeoff.X + envelope.Range < 0.5f * air.Width * air.VoxelSize, $"the range {envelope.Range} in air {air.Width * air.VoxelSize} long");
    }

    [Fact]
    public void TheLiftIsTheRiseABlastUnderABodyAtRestOverAFloorGivesIt()
    {
        // By hand: crouched at rest a little over the open air's floor, the trigger held two commands looking straight down.
        BlastJumpSim open = Sim(JumpEnvelope.OpenAir());
        var start = new JumpStart(JumpEnvelope.Takeoff + new Vector(0f, 0f, JumpEnvelope.LiftHeight), Vector.Zero, new Vector(JumpSearch.MaxPitch, 0f, 0f), true, false, InDuck);
        JumpCommand[] commands = [.. Enumerable.Range(0, 400).Select(i => new JumpCommand(start.View, 0f, 0f, 0f, InDuck | (i < JumpPilot.TriggerCommands ? InAttack : 0)))];
        JumpFlight flight = open.Fly(start, commands);
        Assert.True(flight.Blasts == 1 && flight.Apex > JumpEnvelope.LiftHeight, $"the control: the blast lifts it, {flight.Apex} up");
        Assert.Equal(flight.Apex, Envelope().Lift);

        // The control: the same body high over the floor, out of its blast's reach, is lifted by nothing.
        JumpFlight high = open.Fly(start with { Origin = start.Origin + new Vector(0f, 0f, 1000f) }, commands);
        Assert.Equal(0, high.Blasts);
    }
}
