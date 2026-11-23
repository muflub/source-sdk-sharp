using SourceSharp.Ai.Replay;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <see cref="NavCellsTool"/>'s reading of a room: its teleports sorted by where they land, by the
/// room's own file. The room is the loader's two-cell floor (x 0 to 128, z 16 to 144).
/// </summary>
public sealed class NavCellsToolTests
{
    private static RoomEntity Teleport(string target) => new("trigger_teleport", null, target, default, new AiVector(16, 16, 16), new AiVector(48, 48, 80));

    [Fact]
    public void ATeleportLandingInTheRoomIsATeleportBack()
    {
        RoomTeleports t = NavCellsTool.Teleports(HullNavFixtures.Floor().Open(),
            [Teleport("start"), new RoomEntity("info_teleport_destination", "start", null, new AiVector(100, 32, 16), null, null)]);
        Assert.Equal((1, 0), (t.Resets.Count, t.Exits.Count));
    }

    [Fact]
    public void ATeleportLandingOutsideTheRoomIsATeleportOut()
    {
        RoomTeleports t = NavCellsTool.Teleports(HullNavFixtures.Floor().Open(),
            [Teleport("next"), new RoomEntity("info_teleport_destination", "next", null, new AiVector(5000, 32, 16), null, null)]);
        Assert.Equal((0, 1), (t.Resets.Count, t.Exits.Count));
    }

    [Fact]
    public void ATeleportsVolumeIsItsBrushesWhenTheyAreGiven()
    {
        HurtVolume[] two = [new(new AiVector(16, 16, 16), new AiVector(24, 24, 32)), new(new AiVector(40, 40, 16), new AiVector(48, 48, 32))];
        RoomTeleports t = NavCellsTool.Teleports(HullNavFixtures.Floor().Open(),
            [Teleport("start"), new RoomEntity("info_teleport_destination", "start", null, new AiVector(100, 32, 16), null, null)], [two, []]);
        Assert.Equal(two, t.Resets);
    }

    [Fact]
    public void ADestinationInTheRoomIsAWayIn()
    {
        RoomTeleports t = NavCellsTool.Teleports(HullNavFixtures.Floor().Open(),
            [new RoomEntity("info_teleport_destination", "a", null, new AiVector(100, 32, 16), null, null),
             new RoomEntity("info_teleport_destination", "b", null, new AiVector(5000, 32, 16), null, null)]);
        Assert.Equal([new AiVector(100, 32, 16)], t.WaysIn);
    }

    [Fact]
    public void ACellStandingInAVolumeIsInsideIt()
    {
        var volume = new HurtVolume(new AiVector(0, 0, 0), new AiVector(32, 32, 64));
        var cell = new NavCell(16, 16, 32, 32, 10f, ClearanceClasses.Stand, NavCellFlags.None, 1f);
        Assert.True(NavCellsTool.Inside(cell, [volume]));
        Assert.False(NavCellsTool.Inside(cell with { FloorZ = 64f }, [volume]));
    }
}
