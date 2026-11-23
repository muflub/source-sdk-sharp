using System.Globalization;
using Microsoft.Data.Sqlite;
using SourceSharp.Host.Contracts;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The breakable crates and barrels (ruling M34, brief p12): the dresser writes
/// an <c>info_poi</c> the server's manager reads where it would have stood a
/// crate or a drum, never a prop, under a room's cap and the level's, and every
/// gate counts the breakable solid.
/// </summary>
public sealed class DescentBreakableTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private static readonly RoomKit Kit = RoomKit.Normal;
    private static float Floor => Kit.Sill;
    private static float M => Kit.Cell / 2f;

    private static IEnumerable<RoomEntity> BreakablesOf(IEnumerable<Room> rooms) => rooms.SelectMany(Breakables.Of);

    /// <summary>A chamber with doors west and east and a pack spawn north of its centre.</summary>
    private static Room Chamber() =>
        new RoomBuilder("c_a", Kit, ActOne.Collar).WithHeight(512).WithDoors(CellFace.West, CellFace.East)
            .WithMarker(MarkerKind.PackSpawn, new(M, M + 256, Floor), radius: 64).Build();

    private static Room With(Room room, params RoomEntity[] extra) => room with { Entities = [.. room.Entities.Where(e => e.ClassName != "light"), .. extra] };

    private static IEnumerable<string> Rules(Room built, Room dressed) => DressLint.Check(built, dressed, ActOne.Collar).Select(f => f.Rule);

    /// <summary>Depth 1's rooms dressed again with every room's cap at <paramref name="cap"/>.</summary>
    private IReadOnlyList<Room> DressedAt(int depth, int cap) =>
        [.. libs.Dressed(depth, 0).Select(r => RoomDresser.Dress(r.Built, r.Theme, 0, cap))];

    //-------------------------------------------------------------------------
    // What the dresser writes.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_breakables_point_is_its_types_poi_on_the_floor_turned_by_yaw_only()
    {
        var points = BreakablesOf(libs.Rooms(1, 0)).ToArray();

        Assert.NotEmpty(points);
        Assert.All(points, p =>
        {
            Assert.Contains(p.Key("poi_type"), (string[])["descent_mark_breakable_crate", "descent_mark_breakable_barrel"]);
            Assert.Equal(Floor, p.Origin!.Value.Z);
            string[] angles = p.Key("angles")!.Split(' ');
            Assert.Equal("0", angles[0]);
            Assert.Equal("0", angles[2]);
            Assert.InRange(int.Parse(angles[1], CultureInfo.InvariantCulture), 0, 359);
        });
    }

    [Fact]
    public void Depth_one_dresses_both_crates_and_barrels_as_breakables()
    {
        var kinds = BreakablesOf(libs.Rooms(1, 0)).Select(p => Breakables.KindOf(p.Key("poi_type"))).ToHashSet();

        Assert.Equal([BreakableKind.Crate, BreakableKind.Barrel], kinds.Order());
    }

    [Fact]
    public void No_prop_stands_in_a_breakables_box()
    {
        var models = DressLint.Models(DescentRooms.ThemeFor(1));
        var overlaps = libs.Rooms(1, 0).SelectMany(r => Breakables.Of(r).SelectMany(b =>
            r.Entities.Where(e => e.ClassName.StartsWith("prop_", StringComparison.Ordinal) && models.ContainsKey(e.Key("model") ?? ""))
                .Where(e =>
                {
                    PropModel m = models[e.Key("model")!];
                    var (lo, hi) = m.Turned(Breakables.Yaw(e));
                    return Breakables.BoxOf(b).Overlaps(new Box(e.Origin!.Value + lo, e.Origin!.Value + hi));
                })
                .Select(e => $"{r.Name}: {e.Key("model")} at ({e.Origin}) in the {b.Key("poi_type")} at ({b.Origin})"))).ToArray();

        Assert.Empty(overlaps);
    }

    [Fact]
    public void No_room_writes_the_servers_models_as_props()
    {
        string[] server = [Breakables.Crate.Model, Breakables.Barrel.Model];

        Assert.DoesNotContain(libs.Rooms(1, 0).SelectMany(r => r.Entities), e => server.Contains(e.Key("model")));
    }

    [Fact]
    public void A_lanterns_crate_stays_a_prop()
    {
        // Level 3's lanterns stand on powder crates: a broken stand would drop its lamp.
        var stands = libs.Rooms(3, 0).SelectMany(r => r.Entities)
            .Where(e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Lamp) && e.Key("model") == ActOne.PowderCrate.Model).ToArray();

        Assert.NotEmpty(stands);
        Assert.All(stands, e => Assert.Equal("prop_static", e.ClassName));
    }

    [Fact]
    public void A_room_past_its_cap_keeps_the_themes_crates_and_drums()
    {
        var capped = DressedAt(1, 0);
        string[] props = [ActOne.PowderCrate.Model, ActOne.OilDrum.Model];

        Assert.Empty(BreakablesOf(capped));
        Assert.Contains(capped.SelectMany(r => r.Entities), e => e.ClassName == "prop_static" && props.Contains(e.Key("model"))
                                                                 && e.Key(RoomDresser.RoleKey) != nameof(RoomDresser.Role.Lamp));
    }

    [Fact]
    public void No_room_holds_more_breakables_than_its_cap()
    {
        const int cap = 2;
        var free = DressedAt(1, Breakables.RoomCap);
        var capped = DressedAt(1, cap);

        // The stimulus: some room takes more than the cap when let.
        Assert.Contains(free, r => Breakables.Of(r).Count > cap);
        Assert.All(capped, r => Assert.InRange(Breakables.Of(r).Count, 0, cap));
    }

    [Fact]
    public void A_depths_library_holds_its_rooms_to_the_depths_cap()
    {
        int cap = Breakables.CapAt(1);

        Assert.All(libs.Rooms(1, 0), r => Assert.InRange(Breakables.Of(r).Count, 0, cap));
        Assert.Contains(libs.Rooms(1, 0), r => Breakables.Of(r).Count == cap);
    }

    //-------------------------------------------------------------------------
    // The level cap.
    //-------------------------------------------------------------------------

    public static TheoryData<int> Depths => [.. Enumerable.Range(1, 15)];

    [Theory]
    [MemberData(nameof(Depths))]
    public void Every_level_a_depth_lays_holds_no_more_than_the_level_cap(int depth)
    {
        // A level places exactly its depth's rooms (M16), each at most the depth's cap.
        Assert.InRange(Breakables.CapAt(depth) * DepthTable.For(depth).Rooms, 0, Breakables.LevelCap);
    }

    [Fact]
    public void A_synthetic_level_of_depth_ones_richest_room_stays_under_the_level_cap()
    {
        int rooms = DepthTable.For(1).Rooms;
        int richest = libs.Rooms(1, 0).Max(r => Breakables.Of(r).Count);
        int uncapped = DressedAt(1, Breakables.RoomCap).Max(r => Breakables.Of(r).Count);

        // The same rooms at the room cap alone fill a level past the level cap: the depth's cap is what holds it.
        Assert.True(uncapped * rooms > Breakables.LevelCap, $"{rooms} rooms of {uncapped} make {uncapped * rooms}");
        Assert.InRange(richest * rooms, 1, Breakables.LevelCap);
    }

    private static Room Holding(string name, int breakables)
    {
        Room room = new RoomBuilder(name, Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.West, CellFace.East).Build();
        var points = Enumerable.Range(0, breakables).Select(k => Breakables.Poi(BreakableKind.Crate, new(200 + 80 * k, 200, Floor), 0, "Corner"));
        return room with { Entities = [.. room.Entities, .. points] };
    }

    [Fact]
    public void The_manifest_counts_each_rooms_breakables()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(LibraryManifestWriter.Write("l", [Holding("drift_a-ew-l", 3)]));

        Assert.Equal(3, doc.RootElement.GetProperty("rooms").GetProperty("drift_a-ew-l").GetProperty("breakables").GetInt32());
    }

    [Fact]
    public void The_manifest_lists_each_rooms_breakable_types_among_its_markers()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(LibraryManifestWriter.Write("l", [Holding("drift_a-ew-l", 2)]));
        var markers = doc.RootElement.GetProperty("rooms").GetProperty("drift_a-ew-l").GetProperty("markers").EnumerateArray().Select(m => m.GetString());

        Assert.Contains("descent_mark_breakable_crate", markers);
    }

    [Fact]
    public void A_synthetic_level_over_the_level_cap_is_refused()
    {
        string manifest = LibraryManifestWriter.Write("l", [Holding("drift_a-ew-l", 8)]);
        LevelPlan Of(int n) => new(1, n, [.. Enumerable.Range(0, n).Select(c => new Placement("drift_a-ew-l", 0, c, 0, ""))], "", "");

        Assert.Equal(96, DescentLevel.BreakablesIn(Of(12), manifest));
        Assert.Null(DescentLevel.OverLevelCap(Of(12), manifest));
        Assert.Equal(104, DescentLevel.BreakablesIn(Of(13), manifest));
        Assert.Contains("104 breakables", DescentLevel.OverLevelCap(Of(13), manifest));
    }

    //-------------------------------------------------------------------------
    // Solid in every gate.
    //-------------------------------------------------------------------------

    private static RoomEntity CrateAt(float x, float y) => Breakables.Poi(BreakableKind.Crate, new(x, y, Floor), 0, nameof(RoomDresser.Role.Floor));

    [Fact]
    public void The_dress_lint_reads_a_breakable_as_a_solid_piece()
    {
        Room built = Chamber();
        var pieces = DressLint.Pieces(built, With(built, CrateAt(M - 200, M - 200)), DressLint.Models(ActOne.Collar));

        DressLint.Piece crate = Assert.Single(pieces);
        Assert.True(crate.Solid);
        Assert.Equal(new Box(new(M - 221, M - 236, Floor), new(M - 178, M - 163, Floor + 39)), crate.Box);
    }

    [Fact]
    public void A_breakable_in_front_of_a_door_is_refused_as_a_prop_is()
    {
        Room built = Chamber();

        Assert.Empty(Rules(built, With(built, CrateAt(M - 200, M - 200))));
        Assert.Contains(DressLint.DoorClear, Rules(built, With(built, CrateAt(Kit.Cell - Kit.WallDepth - 60, M))));
    }

    /// <summary>The chamber with a pillar whose east face stands at x 294.</summary>
    private static Room ChamberWithPillar() =>
        new RoomBuilder("c_a", Kit, ActOne.Collar).WithHeight(512).WithDoors(CellFace.West, CellFace.East)
            .WithMarker(MarkerKind.PackSpawn, new(M, M + 256, Floor), radius: 64)
            .WithBox(new(200, M - 300, Floor), new(294, M - 100, Floor + 200)).Build();

    [Fact]
    public void A_breakable_where_the_room_compiles_agent_cannot_stand_is_refused()
    {
        Room built = ChamberWithPillar();
        // Two barrels clear of the pillar: the room compile stands a point's agent (32 wide) anywhere in
        // the 16-unit voxel the point is in, so at x 319 (voxel 304 to 320) its box reaches back to 288,
        // into the pillar, and at x 330 (voxel 320 to 336) to 304, clear of it.
        RoomEntity BarrelAt(float x) => Breakables.Poi(BreakableKind.Barrel, new(x, M - 200, Floor), 0, nameof(RoomDresser.Role.Floor));

        Assert.DoesNotContain(DressLint.Breakable, Rules(built, With(built, BarrelAt(330))));
        Assert.Contains(DressLint.Breakable, Rules(built, With(built, BarrelAt(319))));
    }

    [Fact]
    public void A_room_over_the_room_cap_is_refused()
    {
        Room built = Chamber();
        var points = Enumerable.Range(0, Breakables.RoomCap + 1).Select(k => CrateAt(160 + 80 * (k % 5), 300 + 120 * (k / 5))).ToArray();

        Assert.DoesNotContain(DressLint.Breakable, Rules(built, With(built, points[..^1])));
        Assert.Contains(DressLint.Breakable, Rules(built, With(built, points)));
    }

    [Fact]
    public void A_breakable_walls_off_the_giants_flat_walk()
    {
        Room built = Chamber();
        var models = DressLint.Models(ActOne.Collar);
        var (i, j) = FloorPlan.ColumnOf(M - 200, M - 200);

        Assert.True(new FlatHullWalk(built, models).IsFree(i, j));
        Assert.False(new FlatHullWalk(With(built, CrateAt(M - 200, M - 200)), models).IsFree(i, j));
    }

    [Fact]
    public void Marker_floor_leaves_a_breakable_to_the_dress_lint()
    {
        Room built = Chamber();
        // A crate turned side-on against the south wall: a standing player's 48-wide hull about its point
        // reaches into the wall, which marker_floor refuses of a marker a player stands on.
        Room room = With(built, Breakables.Poi(BreakableKind.Crate, new(M - 200, Kit.WallDepth + 21 + 1, Floor), 90, nameof(RoomDresser.Role.Wall)));

        Assert.DoesNotContain(RoomLint.Check(room), f => f.Rule == RoomLint.MarkerFloor);
    }

    //-------------------------------------------------------------------------
    // The server's models.
    //-------------------------------------------------------------------------

    [AssetDbFact]
    public void The_servers_boxes_are_the_asset_databases_meshes()
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = AssetDb.File, Mode = SqliteOpenMode.ReadOnly }.ToString());
        db.Open();
        foreach (var (m, lift) in ((PropModel, float)[])[(Breakables.Crate, 0), (Breakables.Barrel, Breakables.BarrelLift)])
        {
            using var q = db.CreateCommand();
            q.CommandText = "SELECT mesh_min_x, mesh_min_y, mesh_min_z, mesh_max_x, mesh_max_y, mesh_max_z FROM model_view WHERE game = 'tf' AND model = $m";
            q.Parameters.AddWithValue("$m", m.Model);
            using var r = q.ExecuteReader();
            Assert.True(r.Read(), $"{m.Model} is not a TF2 model");
            // The mesh, lifted onto the point, is the box to within a unit (the server's box is the mesh in whole units).
            float[] lo = [r.GetFloat(0), r.GetFloat(1), r.GetFloat(2) + lift], hi = [r.GetFloat(3), r.GetFloat(4), r.GetFloat(5) + lift];
            float[] boxLo = [m.Mins.X, m.Mins.Y, m.Mins.Z], boxHi = [m.Maxs.X, m.Maxs.Y, m.Maxs.Z];
            for (int a = 0; a < 3; a++)
            {
                Assert.InRange(boxLo[a], lo[a] - 1, lo[a] + 1);
                Assert.InRange(boxHi[a], hi[a] - 1, hi[a] + 1);
            }
        }
    }
}
