using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The Descent room kits (plan_descent_mine_levels.md §3, E1) and their door plugs.</summary>
public sealed class DescentRoomKitTests
{
    [Fact]
    public void The_normal_kit_is_the_plans_1024_cell_with_a_256_by_192_door_and_a_32_wall()
    {
        Assert.Equal(new RoomKit("normal", 1024, 256, 192, 32), RoomKit.Normal);
    }

    [Theory]
    [InlineData("boss2048", 2048)]
    [InlineData("boss3072", 3072)]
    [InlineData("boss4096", 4096)]
    public void A_boss_kit_keeps_the_normal_door_and_wall_on_its_larger_cell(string name, int cell)
    {
        RoomKit kit = new[] { RoomKit.Boss2048, RoomKit.Boss3072, RoomKit.Boss4096 }.Single(k => k.Name == name);

        Assert.Equal((cell, 256, 192, 32), (kit.Cell, kit.DoorWidth, kit.DoorHeight, kit.WallDepth));
    }

    [Theory]
    [InlineData(CellFace.East)]
    [InlineData(CellFace.West)]
    [InlineData(CellFace.North)]
    [InlineData(CellFace.South)]
    public void A_plug_box_is_the_opening_on_the_floor_one_wall_deep_on_the_cell_face(CellFace face)
    {
        RoomKit kit = RoomKit.Normal;
        var (mins, maxs) = kit.PlugBox(face);
        Point size = maxs - mins;
        bool alongY = face is CellFace.East or CellFace.West;

        Assert.Equal(kit.DoorWidth, alongY ? size.Y : size.X);
        Assert.Equal(kit.WallDepth, alongY ? size.X : size.Y);
        Assert.Equal((kit.WallDepth, kit.WallDepth + kit.DoorHeight), (mins.Z, maxs.Z));
        float outer = face switch
        {
            CellFace.East => maxs.X,
            CellFace.West => kit.Cell - mins.X,
            CellFace.North => maxs.Y,
            _ => kit.Cell - mins.Y,
        };
        Assert.Equal(kit.Cell, outer);
    }

    [Fact]
    public void The_pinned_tools_refuse_the_plans_kit_and_take_its_full_height_door_variant()
    {
        Assert.False(RoomKit.Normal.ToolsAccept);
        Assert.True(RoomKit.Normal.WithFullHeightDoors().ToolsAccept);
        Assert.Equal(960, RoomKit.Normal.WithFullHeightDoors().DoorHeight);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("boss2048")]
    [InlineData("boss3072")]
    [InlineData("boss4096")]
    public void Every_kit_fits_its_cell_in_the_tools_navigation_voxel_limit(string name)
    {
        RoomKit kit = new[] { RoomKit.Normal, RoomKit.Boss2048, RoomKit.Boss3072, RoomKit.Boss4096 }.Single(k => k.Name == name);

        Assert.InRange(kit.Cell / kit.NavVoxel, 1, RoomKit.MaxNavVoxelsPerCell);
        Assert.Equal(0, kit.Cell % kit.NavVoxel);
        Assert.Equal(0, kit.DoorHeight % kit.NavVoxel);
    }
}
