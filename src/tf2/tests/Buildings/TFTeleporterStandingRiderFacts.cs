using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using TF2.Server.Buildings;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The standing rider of another client (stock's <c>TeleporterTouch</c>,
/// tf_obj_teleporter.cpp:617-650): every processed command touches the pad the
/// rider stands on — the mover's own ground check, what stock starts touch from —
/// and the pad asks the rider on each touch, so one at a rest rides and one still
/// moving waits until it stops.
/// </summary>
public class TFTeleporterStandingRiderFacts
{
    /// <summary>The dispatcher the server runs over each command of this player.</summary>
    private static PlayerCommandTouches PadOf(ServerPlayerMoves moves, TFTeleporter pad, EntityHandle handle)
        => new(h => h == handle ? pad : null, moves);

    [Fact]
    public void AStandingRiderIsSentByTheCommandsOwnTouch()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);              // the pair to READY
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);   // stands at rest on the pad

        var moves = new ServerPlayerMoves();
        moves.For(rider).RecordTouch(entrance.Handle);                // the mover's ground check hits the pad
        PlayerCommandTouches touches = PadOf(moves, entrance, entrance.Handle);

        touches.ProcessImpacts(rider);                                // one processed command of the rider
        TeleporterHarness.Tick(rig, entrance, exit, 11f);             // the send and the trip

        EngineCall trip = Assert.Single(rig.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(rider, trip.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void ARunningRiderWaitsForAStopOnThePad()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(6f, 0f, 0f));   // :642, 36 is past the 25

        var moves = new ServerPlayerMoves();
        ServerPlayerMove record = moves.For(rider);
        record.RecordTouch(entrance.Handle);
        PlayerCommandTouches touches = PadOf(moves, entrance, entrance.Handle);

        touches.ProcessImpacts(rider);
        TeleporterHarness.Tick(rig, entrance, exit, 0.5f);
        Assert.Equal(rider, entrance.Reserved);                       // booked, :629-632
        Assert.False(rig.Engine.Called("Entities.Teleport"));         // :642, too fast to ride yet

        record.ClearTouches();                                        // the next command touches again
        record.RecordTouch(entrance.Handle);
        rig.Engine.Returns("Entities.GetVelocity", Vector.Zero);      // :550-553, back to a rest on the pad
        touches.ProcessImpacts(rider);
        TeleporterHarness.Tick(rig, entrance, exit, 11f);             // the send and the trip

        EngineCall trip = Assert.Single(rig.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(rider, trip.ArgAs<EntityHandle>(0));
    }
}
