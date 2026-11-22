using System.Globalization;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A joined doorway's light (brief p9). The faces inside a doorway were in the plug when the room was
/// lit: a brush frame takes a floor of light of its theme's (<see cref="BrushFrame.MinLight"/>), near
/// its walls' rather than Act I's everywhere (the arches of levels 7 and 9 glowed), and the let-down
/// floor under the plug is covered by a slab that takes the frame's light (it drew as a black strip).
/// </summary>
public sealed class DescentDoorwayLightTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static Room Chamber(RoomTheme theme) =>
        new RoomBuilder("c", DescentRooms.KitFor(theme == ActOne.Collar ? 1 : 9), theme).WithHeight(512).WithDoors(CellFace.West).Build();

    private static float MinLight(RoomEntity e) => float.Parse(e.Key("_minlight")!, CultureInfo.InvariantCulture);

    private static RoomEntity Joined(Room room) =>
        Assert.Single(room.Entities, e => e.ClassName == "func_brush" && e.Key("room_needs") == "joined_west");

    [Fact]
    public void A_model_framed_doorway_takes_a_lit_slab_over_its_dip()
    {
        Room room = Chamber(ActOne.Collar);
        RoomEntity slab = Joined(room);
        Brush b = Assert.Single(slab.Brushes);

        Assert.Equal("1", slab.Key("Solidity"));
        Assert.Equal(BrushFrame.DefaultMinLight, MinLight(slab));
        Assert.Equal((room.Kit.Sill - RoomBuilder.DoorwayDip, room.Kit.Sill + 1f), (b.Mins.Z, b.Maxs.Z));
        Assert.Equal((RoomBuilder.DoorwayDipShort, (float)room.Kit.WallDepth), (b.Mins.X, b.Maxs.X));
        Assert.Equal(((float)room.Kit.DoorLow, (float)room.Kit.DoorHigh), (b.Mins.Y, b.Maxs.Y));
        Assert.DoesNotContain(RoomLint.Check(room), f => f.Rule == RoomLint.FrameClear);
    }

    [Fact]
    public void A_brush_frame_covers_its_doorways_dip()
    {
        Room room = Chamber(ActTwo.Chapel);

        Assert.Contains(Joined(room).Brushes, b => b.Mins.Z == room.Kit.Sill - RoomBuilder.DoorwayDip && b.Maxs.Z == room.Kit.Sill + 1
                                                   && b.Mins.X == RoomBuilder.DoorwayDipShort && b.Maxs.X == room.Kit.WallDepth
                                                   && b.Mins.Y == room.Kit.DoorLow && b.Maxs.Y == room.Kit.DoorHigh);
        Assert.DoesNotContain(RoomLint.Check(room), f => f.Rule == RoomLint.FrameClear);
    }

    [Fact]
    public void An_act_two_frame_takes_a_light_of_its_own_below_act_ones_and_level_nines_the_least()
    {
        float[] lights = [.. ActTwo.All.Select(t => MinLight(Joined(Chamber(t))))];

        Assert.All(lights, l => Assert.True(l < BrushFrame.DefaultMinLight));
        Assert.Equal(lights.Min(), lights[^1]);
        Assert.Equal(4, lights.Distinct().Count());
    }
}
