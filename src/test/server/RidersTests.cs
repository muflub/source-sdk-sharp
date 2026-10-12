using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The belt's carry: whoever stands on a stationary conveyor is handed its
/// surface velocity through base velocity. The geometry itself — who counts as
/// standing on a box — is pinned in <see cref="EffectEntityTests"/>; what is
/// pinned here is the pass around it: who gets carried and who is skipped. A
/// MOVING brush's riders are not this file's: they are in the mover's push
/// list (<see cref="PhysicsPushedEntities"/>).
/// </summary>
public class RidersTests
{
    /// <summary>
    /// A box everyone in these tests wears: 32 across, 16 tall. The fake
    /// engine answers one stub for every entity, which is fine — a rider the
    /// same shape as the mover is still a rider.
    /// </summary>
    private static readonly (Vector Mins, Vector Maxs) Box =
        (new Vector(-16f, -16f, -8f), new Vector(16f, 16f, 8f));

    private static RecordingEngine Fake(MoveType riderMoveType, Vector riderOrigin)
    {
        var fake = new RecordingEngine { Strict = false };

        // The sphere query reports one rider, handle 7 — which is why the
        // mover in these tests is handle 42: distinct from its rider except
        // where a test wants the mover to find itself.
        fake.FillsSphere(Rider);
        fake.Returns("Networking.EntityGetState.moveType", riderMoveType);
        fake.Returns("Entities.GetBounds", Box);
        fake.Returns("Entities.GetOrigin", riderOrigin);

        return fake;
    }

    private static readonly EntityHandle Mover = new(42u);
    private static readonly EntityHandle Rider = new(7u);

    [Fact]
    public void SomebodyOnTheBeltRidesTheVelocityChannel()
    {
        // The belt never moves, so it has no step to give — its carry is the
        // original one, base velocity renewed per tick, and the impulse a
        // rider keeps on stepping off the end is how a belt has always
        // thrown things.
        var fake = Fake(MoveType.Walk, new Vector(0f, 0f, 16f));

        Riders.Ride(fake, fake, fake, Mover, Vector.Zero, new Vector(100f, 0f, 0f), "test 'belt'", new RecordingLog());

        Assert.True(fake.Called("Entities.AddBaseVelocity"), "the rider is carried");
        var carried = fake.Last("Entities.AddBaseVelocity")!.Value.ArgAs<Vector>(1);
        Assert.True(carried.X == 100f, "at the belt's own speed");
        Assert.True(!fake.Called("Entities.SetOrigin"), "and nobody is teleported by a floor that is not moving");
    }

    [Fact]
    public void APhysicsCrateIsNotCarried()
    {
        // Same place, wrong movetype: base velocity means nothing to the
        // solver, so handing a crate one would be a new behaviour wearing a
        // port's clothing. Stock's conveyor leaves crates behind too.
        var fake = Fake(MoveType.VPhysics, new Vector(0f, 0f, 16f));

        Riders.Ride(fake, fake, fake, Mover, Vector.Zero, new Vector(100f, 0f, 0f), "test 'belt'", new RecordingLog());

        Assert.True(!fake.Called("Entities.AddBaseVelocity") && !fake.Called("Entities.SetOrigin"),
                    "a crate is left where it is, on either channel");
    }

    [Fact]
    public void SomebodyBesideTheMoverIsNot()
    {
        // Feet on the floor next to it, not on the top face.
        var fake = Fake(MoveType.Walk, new Vector(64f, 0f, 8f));

        Riders.Ride(fake, fake, fake, Mover, Vector.Zero, new Vector(100f, 0f, 0f), "test 'belt'", new RecordingLog());

        Assert.True(!fake.Called("Entities.AddBaseVelocity") && !fake.Called("Entities.SetOrigin"),
                    "standing beside it is not riding");
    }

    [Fact]
    public void TheMoverDoesNotCarryItself()
    {
        // The sphere query reports the rider's handle, and this mover IS that
        // handle — the one entity every mover always finds is itself.
        var fake = Fake(MoveType.Walk, new Vector(0f, 0f, 16f));

        Riders.Ride(fake, fake, fake, Rider, Vector.Zero, new Vector(100f, 0f, 0f), "test 'belt'", new RecordingLog());

        Assert.True(!fake.Called("Entities.AddBaseVelocity") && !fake.Called("Entities.SetOrigin"),
                    "a mover is not its own rider");
    }

}
