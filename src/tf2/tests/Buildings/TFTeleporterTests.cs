using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>The level 1 pair: the match, the booking, the trip, the recharge.</summary>
public class TFTeleporterTests
{
    private static (BuildingsRig Rig, TFTeleporter Entrance, TFTeleporter Exit, EntityHandle Builder) APair()
        => TeleporterHarness.Pair();

    private static void TickBoth(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit, float seconds)
        => TeleporterHarness.Tick(rig, entrance, exit, seconds);

    private static EntityHandle AStandingRider(BuildingsRig rig)
        => TeleporterHarness.AStandingRider(rig);

    [Fact]
    public void ThePairBecomesReadyTogether()
    {
        var (rig, entrance, exit, _) = APair();

        // Stock's OnGoActive stands the end in IDLE at once
        // (tf_obj_teleporter.cpp:431) — no fade-out on the clock.
        Assert.Equal(TFTeleporterState.Idle, entrance.State);
        TickBoth(rig, entrance, exit, 1f);

        Assert.Equal(TFTeleporterState.Ready, entrance.State);
        Assert.Equal(TFTeleporterState.Ready, exit.State);
    }

    [Fact]
    public void TheEntranceBooksAStandingFriendlyAndSends()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        EntityHandle rider = AStandingRider(rig);

        entrance.OnTouch(rider);
        Assert.Equal(rider, entrance.Reserved);

        TickBoth(rig, entrance, exit, 0.2f);   // the send's delay, and the exit told

        Assert.Equal(TFTeleporterState.Recharging, entrance.State);
        Assert.Equal(TFTeleporterState.Receiving, exit.State);
        Assert.Equal(rider, exit.Teleportee);
    }

    [Fact]
    public void TheArrivalWaitsTheSendDelayAndTheFade()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        EntityHandle rider = AStandingRider(rig);
        entrance.OnTouch(rider);

        // Stock's chain (tf_obj_teleporter.cpp:127, :203): 0.1 s of SENDING,
        // then the exit's 0.25 fade-out — the arrival at send + 0.35.
        TickBoth(rig, entrance, exit, 0.3f);
        Assert.False(rig.Engine.Called("Entities.Teleport"));

        TickBoth(rig, entrance, exit, 0.1f);
        EngineCall trip = Assert.Single(rig.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(rider, trip.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void TheTripEndsWithTheTravellerOnTheExitsDeck()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        EntityHandle rider = AStandingRider(rig);
        entrance.OnTouch(rider);

        TickBoth(rig, entrance, exit, 11f);   // the fades, the arrival, and the recharge running out

        EngineCall trip = Assert.Single(rig.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(rider, trip.ArgAs<EntityHandle>(0));
        Vector landed = trip.ArgAs<Vector>(1);
        Assert.Equal(0f, landed.X);
        Assert.Equal(0f, landed.Y);
        Assert.Equal(TFTeleporter.ArrivalHeight, landed.Z);
        Assert.True(trip.ArgAs<bool>(3));   // the arrival lands with no velocity
    }

    [Fact]
    public void ThePairIsReadyAgainWhenTheRechargeRunsOut()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        EntityHandle rider = AStandingRider(rig);
        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 11f);

        TickBoth(rig, entrance, exit, 3f);

        Assert.Equal(TFTeleporterState.Ready, entrance.State);
        Assert.Equal(TFTeleporterState.Ready, exit.State);
    }

    [Fact]
    public void TheSendWaitsForThePlayerToStandStill()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        EntityHandle rider = AStandingRider(rig);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(10f, 0f, 0f));   // squared speed 100, above 25
        entrance.OnTouch(rider);

        TickBoth(rig, entrance, exit, 0.1f);
        Assert.Equal(TFTeleporterState.Ready, entrance.State);

        rig.Engine.Returns("Entities.GetVelocity", Vector.Zero);
        TickBoth(rig, entrance, exit, 0.1f);
        Assert.Equal(TFTeleporterState.Sending, entrance.State);
    }

    [Fact]
    public void TheEntranceIgnoresEnemiesAndTheExitBooksNobody()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        (EntityHandle enemy, _) = rig.Players.Add(TFTeam.Blue);

        entrance.OnTouch(enemy);
        Assert.Equal(EntityHandle.None, entrance.Reserved);

        EntityHandle mate = AStandingRider(rig);
        exit.OnTouch(mate);
        Assert.Equal(EntityHandle.None, exit.Reserved);
    }

    [Fact]
    public void TheEntranceBooksWhateverTheStateAndSendsWhenReady()
    {
        var (rig, entrance, exit, _) = APair();
        EntityHandle rider = AStandingRider(rig);

        entrance.OnTouch(rider);   // still Building — the booking does not wait
        Assert.Equal(rider, entrance.Reserved);

        TickBoth(rig, entrance, exit, 0.3f);   // to Ready, the send, its delay, and the exit told

        Assert.Equal(TFTeleporterState.Receiving, exit.State);
        Assert.Equal(rider, exit.Teleportee);
    }

    [Fact]
    public void AReservationThatCannotRideIsReplaced()
    {
        var (rig, entrance, exit, _) = APair();
        EntityHandle first = AStandingRider(rig);
        entrance.OnTouch(first);
        rig.Players.All[first].Alive = false;

        EntityHandle second = AStandingRider(rig);
        entrance.OnTouch(second);

        // The rebooking arm (tf_obj_teleporter.cpp:622-627).
        Assert.Equal(second, entrance.Reserved);
    }

    [Fact]
    public void ABrokenPairNeverSends()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        EntityHandle rider = AStandingRider(rig);
        entrance.OnTouch(rider);

        exit.DestroyObject();
        TickBoth(rig, entrance, exit, 1f);

        Assert.False(rig.Engine.Called("Entities.Teleport"));
    }
}
