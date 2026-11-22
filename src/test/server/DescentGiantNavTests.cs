using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Shapes;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A giant crosses every room (ask M8, brief p7): the level's navigation
/// keeps no solid's top, so a box standing on the floor is stopped by any
/// solid above it, a rail two units high as a wall (<see cref="FlatHullWalk"/>).
/// On depth 1 seed 1 the x1.75 and x1.90 classes reached 15 of the 18 rooms,
/// cut off behind the wide drift's floor rails.
/// </summary>
public sealed class DescentGiantNavTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    /// <summary>The themed depths: Act I and Act II (the gates' order).</summary>
    public static TheoryData<int> ThemedDepths => [1, 2, 3, 4, 6, 7, 8, 9];

    /// <summary>
    /// The families whose way between doors climbs stairs: a pool's walkways stand 64 over its
    /// floor, up a flight from each door. This walk never raises its box, so no room of stairs
    /// passes it. The game's navigation stands a hull on the highest step under its box, and over
    /// it every class reaches every room of depth 8 and 9 seed 1, pools included
    /// (HordeNavDataTests in the Descent tests).
    /// </summary>
    private static readonly string[] Climbs = ["pool"];

    [Theory]
    [MemberData(nameof(ThemedDepths))]
    public void A_96_by_160_box_on_the_floor_walks_from_every_door_of_every_room_to_every_other(int depth)
    {
        var rooms = libs.Dressed(depth, 0).Select(r => (r.Dressed, Cuts: new FlatHullWalk(r.Dressed, DressLint.Models(r.Theme)).Cuts())).ToArray();
        var cuts = rooms.Where(r => !Climbs.Contains(RoomDresser.Family(r.Dressed.Name))).SelectMany(r => r.Cuts.Select(c => $"{r.Dressed.Name}: {c}")).ToArray();
        // An exemption that no longer fails is stale.
        var stale = rooms.Where(r => Climbs.Contains(RoomDresser.Family(r.Dressed.Name)) && r.Cuts.Count == 0).Select(r => r.Dressed.Name).ToArray();

        Assert.True(cuts.Length == 0, $"depth {depth}: {cuts.Length} cuts:\n{string.Join("\n", cuts)}");
        Assert.True(stale.Length == 0, $"depth {depth}: {string.Join(", ", stale)} climb no stairs a giant cannot: take the family out of {nameof(Climbs)}");
    }

    [Fact]
    public void A_rail_two_units_high_across_a_corridor_cuts_it()
    {
        var (open, cut) = Corridor();

        Assert.Empty(new FlatHullWalk(open, new Dictionary<string, PropModel>()).Cuts());
        Assert.NotEmpty(new FlatHullWalk(cut, new Dictionary<string, PropModel>()).Cuts());
    }

    [Fact]
    public void A_post_leaving_less_than_the_box_either_side_cuts_a_corridor()
    {
        Room room = Builder().WithWidth(256).Post(512, 512, 32).Build();

        Assert.Empty(new FlatHullWalk(Builder().WithWidth(256).Build(), new Dictionary<string, PropModel>()).Cuts());
        Assert.NotEmpty(new FlatHullWalk(room, new Dictionary<string, PropModel>()).Cuts());
    }

    [Fact]
    public void A_strip_across_a_corridor_is_drawn_but_leaves_it_open()
    {
        RoomBuilder b = Builder();
        Room room = b.WithStrips([Brush.Box(new Point(500, 16, b.Floor), new Point(508, 1008, b.Floor + 2), b.Theme.Block)]).Build();

        Assert.Contains(room.Entities, e => e.ClassName == "func_brush" && e.Key("Solidity") == "1" && e.Brushes.Count == 1);
        Assert.Empty(new FlatHullWalk(room, new Dictionary<string, PropModel>()).Cuts());
    }

    private static RoomBuilder Builder() =>
        new RoomBuilder("drift_t", RoomKit.Normal, RoomTheme.Grey).WithHeight(256).WithDoors(CellFace.West, CellFace.East).WithoutDoorFrames();

    private static (Room Open, Room Cut) Corridor()
    {
        Room open = Builder().Build();
        RoomBuilder b = Builder();
        Room cut = b.WithBox(new Point(500, 16, b.Floor), new Point(508, 1008, b.Floor + 2)).Build();
        return (open, cut);
    }
}
