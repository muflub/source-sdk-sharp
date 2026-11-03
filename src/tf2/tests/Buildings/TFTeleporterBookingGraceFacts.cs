using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The touch reservation grace (tf_obj_teleporter.cpp:546-569, :622-632):
/// stepping off the entrance holds its booking for two seconds — players can
/// push the rider off the pad — the booked rider stepping back closes the
/// window, and only a rider actually standing on the pad is sent.
/// </summary>
public class TFTeleporterBookingGraceFacts
{
    [Fact]
    public void SteppingOffKeepsTheBookingForTwoSeconds()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);   // the pair to READY
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        rig.Clock.Now += 1f;

        entrance.OnEndTouch(rider);

        // :563-568: the EndTouch arm stamps the grace and keeps the booking.
        Assert.Equal(rider, entrance.Reserved);
    }

    [Fact]
    public void AWalkedOffRiderIsNotSentAndReturnsToTheBooking()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        entrance.OnEndTouch(rider);                        // pushed off
        Assert.Equal(rider, entrance.Reserved);            // :563-568 — kept

        // Stock sends only from the touch callback — a standing rider that is
        // not on the pad rides nobody.
        TeleporterHarness.Tick(rig, entrance, exit, 0.5f);
        Assert.Equal(TFTeleporterState.Ready, entrance.State);
        Assert.False(rig.Engine.Called("Entities.Teleport"));

        entrance.OnTouch(rider);                           // back on the pad
        TeleporterHarness.Tick(rig, entrance, exit, 11f);  // the send and the trip

        EngineCall trip = Assert.Single(rig.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(rider, trip.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void TheBookingFallsToTheNextFriendlyWhenTheGraceRunsOut()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle first = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(first);
        entrance.OnEndTouch(first);
        TeleporterHarness.Tick(rig, entrance, exit, 2.1f); // past the 2 s (:626)

        EntityHandle second = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(second);

        // :622-627: the grace is expired, so the touch rebooks.
        Assert.Equal(second, entrance.Reserved);
    }

    [Fact]
    public void ABookedRiderBackOnThePadReopensItsOwnWindow()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle first = TeleporterHarness.AStandingRider(rig);
        EntityHandle second = TeleporterHarness.AStandingRider(rig);

        entrance.OnTouch(first);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(10f, 0f, 0f));   // squared 100, above 25
        entrance.OnEndTouch(first);                        // grace: t0 + 2
        TeleporterHarness.Tick(rig, entrance, exit, 1f);   // one second of the window
        entrance.OnTouch(first);                           // back on — StartTouch zeroes it (:550-553)

        TeleporterHarness.Tick(rig, entrance, exit, 1.5f); // past the original t0 + 2, still moving
        entrance.OnTouch(second);

        // The return closed the window before it could expire, so the booking
        // is not the second friendly's for the taking (:625-626).
        Assert.Equal(first, entrance.Reserved);
    }
}
