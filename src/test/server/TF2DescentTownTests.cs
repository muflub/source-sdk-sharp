using System.Globalization;
using System.Text;
using SourceSharp.MapGen;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// tf2_descent_town as the generator writes it (plan_host_sdk §2.10): the way down,
/// the spawns, determinism, and every material and model it names present in
/// the content the tf2_descent mod mounts.
/// </summary>
public sealed class TF2DescentTownTests
{
    private static VmfMap Town() => new TF2DescentTown().Build();

    [Fact]
    public void There_is_one_way_down_and_it_follows_the_entity_contract()
    {
        var transition = Assert.Single(Town().Entities, e => e.ClassName == TF2DescentTown.TransitionClass);

        string Key(string k) => transition.KeyValues.Single(kv => kv.Key == k).Value;
        Assert.Equal(TF2DescentTown.TransitionName, Key("targetname"));
        Assert.Equal("down", Key("direction"));
        Assert.Equal(TF2DescentTown.FirstLevel, Key("map"));
        Assert.Equal("0", Key("StartDisabled"));
    }

    [Theory]
    [InlineData("func_button", "OnPressed")]
    [InlineData("trigger_multiple", "OnStartTouch")]
    public void The_cage_button_and_the_shaft_both_fire_the_transition(string className, string output)
    {
        var source = Assert.Single(Town().Entities, e => e.ClassName == className);

        Assert.Contains(source.Connections, c => c.Output == output && c.Target == TF2DescentTown.TransitionName && c.Input == "Transition");
        Assert.NotEmpty(source.Solids);
    }

    [Fact]
    public void Sixteen_spawns_in_the_square_and_eight_named_for_arrivals()
    {
        var spawns = Town().Entities.Where(e => e.ClassName == "info_player_deathmatch").ToArray();

        Assert.Equal(TF2DescentTown.ArrivalSpawns, spawns.Count(s => s.Name == TF2DescentTown.ArrivalName));
        Assert.Equal(TF2DescentTown.SquareSpawns, spawns.Count(s => s.Name.Length == 0));
    }

    [Fact]
    public void The_worldspawn_says_its_entities_are_the_mod_contracts()
    {
        var world = Town().World.ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal("mod", world["ssmap_entities"]);
        Assert.Equal("1", world["ssmap_entities_version"]);
        Assert.Equal(TownPalette.TF2Mining.Sky, world["skyname"]);
    }

    [Fact]
    public void Two_builds_write_the_same_bytes()
    {
        Assert.Equal(Town().Write(), Town().Write());
    }

    //-------------------------------------------------------------------------
    // Placement: props sit where the preview showed they belong.
    //-------------------------------------------------------------------------

    private static (Point Min, Point Max) Bounds(VmfSolid solid)
    {
        var points = solid.Sides.SelectMany(s => new[] { s.P0, s.P1, s.P2 }).ToArray();
        return (new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
                new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
    }

    private static Point Origin(VmfEntity e)
    {
        var v = e.KeyValues.Single(kv => kv.Key == "origin").Value.Split(' ').Select(float.Parse).ToArray();
        return new(v[0], v[1], v[2]);
    }

    private static string Key(VmfEntity e, string key) => e.KeyValues.Single(kv => kv.Key == key).Value;

    private static VmfEntity[] PropsOf(VmfMap map, string model) =>
        map.Entities.Where(e => e.KeyValues.Any(kv => kv.Key == "model" && kv.Value == model)).ToArray();

    [Fact]
    public void The_rail_spur_is_one_line_of_track_along_the_street_with_the_carts_on_it()
    {
        // The track and cart models run along their own Y; placed at yaw 0
        // the spur was four cross-wise rail stubs with gaps between them.
        var town = Town();
        var tracks = PropsOf(town, TownPalette.TF2Mining.Track).OrderBy(t => Origin(t).X).ToArray();
        var carts = PropsOf(town, TownPalette.TF2Mining.OreCart);

        Assert.Equal(4, tracks.Length);
        Assert.All(tracks.Concat(carts), p => Assert.Equal("0 90 0", Key(p, "angles")));
        Assert.All(tracks.Concat(carts), p => Assert.Equal(TF2DescentTown.SpurY, Origin(p).Y));
        for (int i = 1; i < tracks.Length; i++)
            Assert.Equal(TF2DescentTown.TrackLength, Origin(tracks[i]).X - Origin(tracks[i - 1]).X);
        Assert.All(carts, c => Assert.InRange(Origin(c).X, Origin(tracks[0]).X - TF2DescentTown.TrackLength / 2f, Origin(tracks[^1]).X + TF2DescentTown.TrackLength / 2f));
    }

    [Fact]
    public void No_arrival_spawn_stands_in_the_rail_spur()
    {
        // A cart turned along the spur is 48 wide; a player hull is 48 wide.
        // The spawn row once ran through a cart that stood across the spur.
        const float cartHalfWidth = 24f, hullHalfWidth = 24f;
        var arrivals = Town().Entities.Where(e => e.ClassName == "info_player_deathmatch" && e.Name == TF2DescentTown.ArrivalName);

        Assert.All(arrivals, a => Assert.True(Math.Abs(Origin(a).Y - TF2DescentTown.SpurY) >= cartHalfWidth + hullHalfWidth,
            $"arrival spawn at {Origin(a)} overlaps the spur at y {TF2DescentTown.SpurY}"));
    }

    [Fact]
    public void Every_lantern_hangs_beside_something_taller_than_it()
    {
        // A lantern hangs off a wall, a post or a beam, or under a roof: some
        // brush within 48 units of it reaches above it and starts no more than
        // 64 above it (so the sky, high over everything, does not count). The
        // winch house's once floated over its roof.
        var town = Town();
        var brushes = town.WorldSolids.Select(Bounds).ToArray();

        foreach (var light in town.Entities.Where(e => e.ClassName == "light"))
        {
            var at = Origin(light);
            bool hung = brushes.Any(b =>
                Math.Max(Math.Max(b.Min.X - at.X, at.X - b.Max.X), 0f) <= 48f
                && Math.Max(Math.Max(b.Min.Y - at.Y, at.Y - b.Max.Y), 0f) <= 48f
                && b.Max.Z > at.Z
                && b.Min.Z < at.Z + 64f);
            Assert.True(hung, $"the lantern at {at} hangs from nothing");
        }
    }

    [Fact]
    public void The_headframe_stands_over_the_shaft_under_open_sky()
    {
        var town = Town();
        var headframe = Assert.Single(PropsOf(town, TownPalette.TF2Mining.Headframe));
        var at = Origin(headframe);
        var sky = town.WorldSolids.Where(s => s.Sides.All(side => side.Material == "tools/toolsskybox")).Select(Bounds).ToArray();

        Assert.InRange(at.X, TF2DescentTown.ShaftX - TF2DescentTown.ShaftHalf, TF2DescentTown.ShaftX + TF2DescentTown.ShaftHalf);
        Assert.InRange(at.Y, -TF2DescentTown.ShaftHalf, TF2DescentTown.ShaftHalf);
        // Nothing of the sky's seal hangs low enough over it to cut its top off.
        Assert.All(sky.Where(b => b.Min.X <= at.X && b.Max.X >= at.X && b.Min.Y <= at.Y && b.Max.Y >= at.Y),
            b => Assert.True(b.Min.Z > at.Z + TF2DescentTown.HeadframeHeight, $"the sky at z {b.Min.Z} cuts the headframe"));
    }

    [Fact]
    public void The_cliffs_texture_is_scaled_up_and_every_other_face_keeps_the_default()
    {
        var town = Town();
        var cliffSides = town.WorldSolids.SelectMany(s => s.Sides).Where(side => side.Material == TownPalette.TF2Mining.Cliff).ToArray();
        var otherSides = town.WorldSolids.SelectMany(s => s.Sides).Where(side => side.Material != TownPalette.TF2Mining.Cliff);

        Assert.NotEmpty(cliffSides);
        Assert.All(cliffSides, side => Assert.EndsWith("] 0.75", side.UAxis));
        Assert.All(otherSides, side => Assert.EndsWith("] 0.25", side.UAxis));
    }

    [Theory]
    [InlineData(VmfMap.DefaultTextureScale, "0.25")]
    [InlineData(0.75f, "0.75")]
    [InlineData(1f, "1")]
    public void A_box_writes_its_texture_scale_on_both_axes_of_every_face(float scale, string written)
    {
        var box = VmfMap.Box(new(0f, 0f, 0f), new(64f, 64f, 64f), "dev/dev_measuregeneric01", textureScale: scale);

        Assert.Equal(6, box.Sides.Count);
        Assert.All(box.Sides, side => Assert.EndsWith("] " + written, side.UAxis));
        Assert.All(box.Sides, side => Assert.EndsWith("] " + written, side.VAxis));
    }

    [Fact]
    public void The_six_shops_differ_in_planking_and_height()
    {
        // Six identical grey shells read as a set, not a town.
        var town = new TF2DescentTown();
        var styles = TF2DescentTown.Shops.Select(s => town.StyleOf(s.Name)).ToArray();

        Assert.True(styles.Select(s => s.Wall).Distinct().Count() >= 4, "fewer than four wall materials across six shops");
        Assert.True(styles.Select(s => s.Height).Distinct().Count() >= 3, "fewer than three wall heights across six shops");
        Assert.Contains(styles, s => s.FalseFront);
        Assert.Contains(styles, s => s.Chimney != TF2DescentTown.ShopChimney.None);
    }

    [Fact]
    public void A_name_that_is_not_a_shop_has_no_style()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TF2DescentTown().StyleOf("bank"));
    }

    [Fact]
    public void A_false_front_rises_above_its_roof_on_the_street_side()
    {
        var generator = new TF2DescentTown();
        var town = generator.Build();
        var brushes = town.WorldSolids.Select(Bounds).ToArray();

        foreach (var (name, x, side) in TF2DescentTown.Shops)
        {
            var (height, _, falseFront, _) = generator.StyleOf(name);
            float face = TF2DescentTown.ShopFront * side;
            bool found = brushes.Any(b => b.Min.X <= x - 200f && b.Max.X >= x + 200f
                && b.Min.Z == height && b.Max.Z >= height + 16f + TF2DescentTown.FalseFrontRise
                && (side > 0 ? b.Max.Y == face : b.Min.Y == face));
            Assert.Equal(falseFront, found);
        }
    }

    [Fact]
    public void Every_shop_faces_the_street_with_a_door_a_window_and_an_awning()
    {
        var town = Town();
        var palette = TownPalette.TF2Mining;

        foreach (var (name, x, side) in TF2DescentTown.Shops)
        {
            float face = TF2DescentTown.ShopFront * side;
            // Modelled facing +X, each turns a quarter to face the street: 270
            // on the north side (facing -Y), 90 on the south (facing +Y).
            string facing = side > 0 ? "0 270 0" : "0 90 0";
            foreach (var model in new[] { palette.DoorFrame, palette.Window, palette.Awning })
            {
                Assert.Contains(PropsOf(town, model), p => Math.Abs(Origin(p).X - x) < 224f
                    && Math.Abs(Origin(p).Y - face) <= 4f
                    && Key(p, "angles") == facing);
            }
        }
    }

    [Fact]
    public void Every_porch_barrel_stands_on_its_porch_clear_of_the_wall()
    {
        // The barrel model's origin is its centre: 23 across, 30 up. Placed by
        // its foot it once sank into the porch and two thirds into the wall.
        const float radius = 23f, halfHeight = 30f;

        foreach (var barrel in PropsOf(Town(), TownPalette.TF2Mining.Barrel))
        {
            var at = Origin(barrel);
            Assert.Equal(TF2DescentTown.PorchTop + halfHeight, at.Z);
            Assert.True(Math.Abs(at.Y) + radius <= TF2DescentTown.ShopFront, $"the barrel at {at} is in the wall");
            Assert.True(Math.Abs(at.Y) >= TF2DescentTown.ShopFront - TF2DescentTown.PorchDepth, $"the barrel at {at} is off its porch");
        }
    }

    [Fact]
    public void Player_clip_keeps_blast_jumpers_off_the_lower_cliffs_tops()
    {
        // Above the lower cliffs is sky, not a ceiling, so a jump that clears a
        // lip would land on a cliff top. A clip curtain inside each lower cliff
        // runs from below its top to the sky.
        var town = Town();
        var clips = town.WorldSolids.Where(s => s.Sides.All(side => side.Material == "tools/toolsplayerclip")).Select(Bounds).ToArray();
        var cliffs = town.WorldSolids.Where(s => s.Sides.Any(side => side.Material == TownPalette.TF2Mining.Cliff)).Select(Bounds).ToArray();
        float sky = town.WorldSolids.Where(s => s.Sides.All(side => side.Material == "tools/toolsskybox")).Select(Bounds).Max(b => b.Max.Z) - 16f;
        var lower = cliffs.Where(c => c.Max.Z < sky).ToArray();

        Assert.Equal(3, lower.Length);
        foreach (var cliff in lower)
        {
            // Along the cliff's long axis, nearly its whole length (less the
            // corners the neighbouring cliffs' clips cover), against its face.
            bool alongX = cliff.Max.X - cliff.Min.X > cliff.Max.Y - cliff.Min.Y;
            float length = alongX ? cliff.Max.X - cliff.Min.X : cliff.Max.Y - cliff.Min.Y;
            Assert.Contains(clips, c => c.Min.Z <= cliff.Max.Z - 64f && c.Max.Z >= sky
                && (alongX ? c.Max.X - c.Min.X : c.Max.Y - c.Min.Y) >= length - 160f
                && (alongX ? c.Max.Y >= cliff.Min.Y - 8f && c.Min.Y <= cliff.Max.Y + 8f
                           : c.Max.X >= cliff.Min.X - 8f && c.Min.X <= cliff.Max.X + 8f));
        }
    }

    [Fact]
    public void Player_clip_fills_the_space_behind_every_rock()
    {
        // A rock's mesh is not a box: between it and its cliff are pockets
        // the preview's fuzzer wedged players in, and behind a corner rock a
        // one-position trap. Clip fills each corner to the sky and runs from
        // each long rock back to its cliff.
        var town = Town();
        var clips = town.WorldSolids.Where(s => s.Sides.All(side => side.Material == "tools/toolsplayerclip")).Select(Bounds).ToArray();
        float sky = town.WorldSolids.Where(s => s.Sides.All(side => side.Material == "tools/toolsskybox")).Select(Bounds).Max(b => b.Min.Z);

        var corners = PropsOf(town, TownPalette.TF2Mining.RockTall);
        Assert.Equal(4, corners.Length);
        Assert.All(corners, r =>
        {
            var at = Origin(r);
            Assert.Contains(clips, c => c.Min.X <= at.X && c.Max.X >= at.X && c.Min.Y <= at.Y && c.Max.Y >= at.Y
                && c.Min.Z <= 0f && c.Max.Z >= sky);
        });

        var long_ = PropsOf(town, TownPalette.TF2Mining.RockLong);
        Assert.Equal(4, long_.Length);
        Assert.All(long_, r =>
        {
            var at = Origin(r);
            Assert.Contains(clips, c => c.Min.X <= at.X - 200f && c.Max.X >= at.X + 200f
                && c.Min.Y <= at.Y && c.Max.Y >= at.Y && Math.Max(Math.Abs(c.Min.Y), Math.Abs(c.Max.Y)) >= 1024f
                && c.Min.Z <= 0f && c.Max.Z >= 235f);
        });
    }

    [Fact]
    public void No_dressing_stands_in_the_street_or_on_a_spawn()
    {
        var town = Town();
        var palette = TownPalette.TF2Mining;
        var dressing = new[]
        {
            palette.RockTall, palette.RockLong, palette.Scree, palette.Shrub, palette.Crate, palette.SmallCrate,
            palette.OilDrum, palette.WoodPile, palette.Pole, palette.DangerSign, palette.Barrel,
        };
        var spots = town.Entities.Where(e => e.ClassName is "info_player_deathmatch" or "info_teleport_destination").Select(Origin).ToArray();
        const float streetHalfWidth = 192f, streetEnd = TF2DescentTown.ShaftX - TF2DescentTown.ShaftHalf - 128f;

        foreach (var prop in dressing.SelectMany(m => PropsOf(town, m)))
        {
            var at = Origin(prop);
            Assert.False(Math.Abs(at.Y) < streetHalfWidth && at.X < streetEnd, $"{Key(prop, "model")} at {at} stands in the street");
            Assert.All(spots, spot => Assert.True(Math.Abs(spot.X - at.X) >= 64f || Math.Abs(spot.Y - at.Y) >= 64f,
                $"{Key(prop, "model")} at {at} stands on the spawn at {spot}"));
        }
    }

    [Fact]
    public void The_shade_is_lit_at_least_half_as_bright_as_the_sun()
    {
        // Most of the street is in a building's or the cliff's shadow. At six
        // to one the map preview's spawn views were half black.
        var sun = Assert.Single(Town().Entities, e => e.ClassName == "light_environment");
        float Brightness(string key) => float.Parse(Key(sun, key).Split(' ')[3], CultureInfo.InvariantCulture);

        Assert.InRange(Brightness("_light") / Brightness("_ambient"), 1f, 2f);
    }

    //-------------------------------------------------------------------------
    // The markers the server stands its stalls and the portal pads at.
    //-------------------------------------------------------------------------

    private static VmfEntity Target(VmfMap map, string name) =>
        Assert.Single(map.Entities, e => e.ClassName == "info_target" && e.Name == name);

    /// <summary>
    /// What stands in a footprint above the floor: a world solid taller than
    /// the porches (the ground, street, boardwalks and porches are what a
    /// stall or a pad stands on), or a prop whose origin is in it.
    /// </summary>
    private static string[] InTheWay(VmfMap map, float x, float y, float half)
    {
        bool Inside(float px, float py) => Math.Abs(px - x) < half && Math.Abs(py - y) < half;
        var walls = map.WorldSolids.Select(Bounds)
            .Where(b => b.Max.Z > TF2DescentTown.PorchTop && b.Min.Z < 82f
                && b.Min.X < x + half && b.Max.X > x - half && b.Min.Y < y + half && b.Max.Y > y - half)
            .Select(b => $"solid {b.Min}..{b.Max}");
        var props = map.Entities.Where(e => e.ClassName.StartsWith("prop_", StringComparison.Ordinal) && Inside(Origin(e).X, Origin(e).Y))
            .Select(e => $"{Key(e, "model")} at {Origin(e)}");
        return [.. walls, .. props];
    }

    [Fact]
    public void The_engineers_armour_stall_and_bench_stand_either_side_of_the_smithys_porch()
    {
        var town = Town();
        var smithy = Origin(Target(town, "shop_" + TF2DescentTown.EngineerShop));
        var engineer = Origin(Target(town, TF2DescentTown.EngineerMarker));
        var bench = Origin(Target(town, TF2DescentTown.BenchMarker));

        Assert.Equal(new Point(smithy.X - TF2DescentTown.EngineerBeside, smithy.Y, smithy.Z), engineer);
        Assert.Equal(new Point(smithy.X + TF2DescentTown.EngineerBeside, smithy.Y, smithy.Z), bench);
        // The bench is 64 across, the Engineer a player's 48; with a margin
        // either way, nothing but floor under either.
        Assert.Empty(InTheWay(town, engineer.X, engineer.Y, 40f));
        Assert.Empty(InTheWay(town, bench.X, bench.Y, 40f));
    }

    [Fact]
    public void Each_shop_marker_names_one_place()
    {
        var names = Town().Entities.Where(e => e.ClassName == "info_target").Select(e => e.Name).ToArray();

        Assert.Equal(TF2DescentTown.Shops.Count + 2, names.Count(n => n.StartsWith("shop_", StringComparison.Ordinal)));
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void The_portal_pad_row_faces_the_square_and_has_room_for_every_pad()
    {
        var town = Town();
        var marker = Target(town, TF2DescentTown.PortalMarker);
        var at = Origin(marker);
        var square = town.Entities.Where(e => e.ClassName == "info_player_deathmatch" && e.Name.Length == 0).Select(Origin).ToArray();
        var spots = town.Entities.Where(e => e.ClassName is "info_player_deathmatch" or "info_teleport_destination").Select(Origin).ToArray();

        // Facing west, back at the square's spawns, which face it.
        Assert.Equal("0 180 0", Key(marker, "angles"));
        Assert.All(square, s => Assert.True(s.X < at.X, $"square spawn at {s} is not west of the pads"));

        // The server's row: 0, then one side, then the other, a spacing
        // further each time; facing west it runs along y. Each pad is 48
        // across; with a player's half width round it, nothing but floor in
        // it, and no spawn so close a player arrives on a pad.
        const float padHalf = 24f, hullHalf = 24f;
        for (int i = 0; i < TF2DescentTown.PortalPads; i++)
        {
            int step = (i + 1) / 2 * (i % 2 == 1 ? 1 : -1);
            float y = at.Y - step * TF2DescentTown.PortalSpacing;
            Assert.Empty(InTheWay(town, at.X, y, padHalf + hullHalf));
            Assert.All(spots, s => Assert.True(Math.Abs(s.X - at.X) >= padHalf + hullHalf || Math.Abs(s.Y - y) >= padHalf + hullHalf,
                $"the spawn at {s} stands on pad {i} at ({at.X}, {y})"));
        }
    }

    [Fact]
    public void In_the_way_sees_a_wall_and_a_prop_but_not_the_floor()
    {
        var town = Town();
        float x = -800f, front = TF2DescentTown.ShopFront;

        // The general store's street wall; its porch barrel; the street.
        Assert.Contains(InTheWay(town, x, front + 8f, 16f), s => s.StartsWith("solid", StringComparison.Ordinal));
        Assert.Contains(InTheWay(town, x + 128f, front - 24f, 8f), s => s.Contains(TownPalette.TF2Mining.Barrel, StringComparison.Ordinal));
        Assert.Empty(InTheWay(town, x, 0f, 64f));
    }

    [Fact]
    public void A_player_on_the_shaft_floor_is_sent_back_up_to_the_arrival_ring()
    {
        // The transition trigger catches a falling player first; the floor
        // below it is only reached when the hop did not happen, and then the
        // teleport returns the player instead of stranding them.
        var town = Town();
        var floor = Assert.Single(town.Entities, e => e.ClassName == "trigger_teleport");
        var transition = Assert.Single(town.Entities, e => e.ClassName == "trigger_multiple");
        var destination = Assert.Single(town.Entities, e => e.ClassName == "info_teleport_destination");

        Assert.Equal(Key(destination, "targetname"), Key(floor, "target"));
        var floorBox = Bounds(Assert.Single(floor.Solids));
        var transitionBox = Bounds(Assert.Single(transition.Solids));
        Assert.Equal(-TF2DescentTown.ShaftDepth, floorBox.Min.Z);
        Assert.True(floorBox.Max.Z <= transitionBox.Min.Z, "the floor teleport reaches up into the transition trigger");
        Assert.Equal(transitionBox.Min.X, floorBox.Min.X);
        Assert.Equal(transitionBox.Max.X, floorBox.Max.X);

        var at = Origin(destination);
        Assert.True(at.Z > 0f, "the teleport destination is not above ground");
        Assert.True(Math.Abs(at.Y - TF2DescentTown.SpurY) >= 48f, $"the return point {at} is on the rail spur");
        Assert.All(town.Entities.Where(e => e.Name == TF2DescentTown.ArrivalName),
            s => Assert.True(Math.Abs(Origin(s).X - at.X) >= 48f || Math.Abs(Origin(s).Y - at.Y) >= 48f, $"the return point {at} lands on the spawn at {Origin(s)}"));
    }

    //-------------------------------------------------------------------------
    // Every palette entry exists in TF2's VPKs, read from the tree's content
    // link (tools/setup: bin/<config>/content -> the local TF2 install).
    //-------------------------------------------------------------------------

    /// <summary>Every file path a VPK v1/v2 directory lists.</summary>
    private static HashSet<string> VpkFiles(string dirVpk)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var r = new BinaryReader(File.OpenRead(dirVpk));
        r.ReadUInt32();                 // signature
        uint version = r.ReadUInt32();
        r.ReadUInt32();                 // tree size
        if (version == 2)
            r.ReadBytes(16);

        string Z()
        {
            var b = new List<byte>();
            for (byte c; (c = r.ReadByte()) != 0;)
                b.Add(c);
            return Encoding.Latin1.GetString(b.ToArray());
        }

        for (string ext; (ext = Z()).Length > 0;)
        for (string dir; (dir = Z()).Length > 0;)
        for (string name; (name = Z()).Length > 0;)
        {
            r.ReadUInt32();             // crc
            ushort preload = r.ReadUInt16();
            r.ReadBytes(2 + 4 + 4 + 2); // archive, offset, length, terminator
            r.ReadBytes(preload);
            files.Add($"{dir}/{name}.{ext}");
        }

        return files;
    }

    [NotOnCiFact("needs a Team Fortress 2 install for its content, which a runner has not")]
    public void Every_material_and_model_the_town_names_is_in_the_mounted_tf2_content()
    {
        string tf = Path.Combine(RepoLayout.Repo("bin", "debug", "content"), "tf");
        Assert.True(Directory.Exists(tf), $"no {tf}: make setup links bin/<config>/content to a Team Fortress 2 install (TF2_DIR)");

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vpk in new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" })
            files.UnionWith(VpkFiles(Path.Combine(tf, vpk)));

        Assert.True(files.Count > 10_000, $"the VPKs listed {files.Count} files: the reader saw nothing");
        var palette = TownPalette.TF2Mining;
        var missing = palette.Materials.Select(m => $"materials/{m}.vmt")
            .Concat(palette.Models)
            .Concat(new[] { "bk", "dn", "ft", "lf", "rt", "up" }.Select(f => $"materials/skybox/{palette.Sky}{f}.vmt"))
            .Where(f => !files.Contains(f))
            .ToArray();

        Assert.Empty(missing);
    }

    [NotOnCiFact("needs a Team Fortress 2 install for its content, which a runner has not")]
    public void Every_solid_static_prop_has_a_collision_model()
    {
        // The engine makes a solid static prop with no .phy non-solid and logs
        // a map error at load; the ore cart, track and headframe have none.
        string tf = Path.Combine(RepoLayout.Repo("bin", "debug", "content"), "tf");
        var files = VpkFiles(Path.Combine(tf, "tf2_misc_dir.vpk"));

        var solidWithout = Town().Entities
            .Where(e => e.ClassName == "prop_static" && Key(e, "solid") != "0")
            .Select(e => Key(e, "model"))
            .Distinct()
            .Where(m => !files.Contains(Path.ChangeExtension(m, ".phy")))
            .ToArray();

        Assert.Empty(solidWithout);
        Assert.All(TownPalette.TF2Mining.WithoutCollision, m => Assert.DoesNotContain(Path.ChangeExtension(m, ".phy"), files));
    }

    [Fact]
    public void Every_ore_cart_is_wrapped_in_player_clip()
    {
        var town = Town();
        var clips = town.WorldSolids.Where(s => s.Sides.All(side => side.Material == "tools/toolsplayerclip")).Select(Bounds).ToArray();

        foreach (var cart in PropsOf(town, TownPalette.TF2Mining.OreCart))
        {
            var at = Origin(cart);
            Assert.Contains(clips, c => c.Min.X <= at.X - 40f && c.Max.X >= at.X + 40f
                && c.Min.Y <= at.Y - 20f && c.Max.Y >= at.Y + 20f
                && c.Min.Z <= at.Z && c.Max.Z >= at.Z + 100f);
        }
    }

    [NotOnCiFact("needs a Team Fortress 2 install for its content, which a runner has not")]
    public void The_content_check_sees_a_missing_entry()
    {
        // Fail-first arm: a palette entry that is not in TF2 must be reported
        // by the same lookup.
        string tf = Path.Combine(RepoLayout.Repo("bin", "debug", "content"), "tf");
        var files = VpkFiles(Path.Combine(tf, "tf2_misc_dir.vpk"));

        Assert.DoesNotContain("models/props_mining/no_such_model.mdl", files);
        Assert.Contains(TownPalette.TF2Mining.WaterTower, files);
    }
}
