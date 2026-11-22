using SourceSharp.MapGen.Descent.Themes;

namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The boss arenas (tag <c>boss_arena</c>; plan §3.2, §7, brief p13), on the normal kit as rooms larger
/// than one cell (M24), each opening only where its boss row joins it (<see cref="RoomBuilder.WithSockets"/>).
/// Each lights itself (<see cref="RoomBuilder.WithOwnLamps"/>) with its theme's arena lamps, and tells the
/// server where its boss stands and faces, the Tank's track and the robots' mouths (<see cref="ArenaData"/>),
/// and names the brushes the encounter switches (<see cref="BossNames"/>).
/// <list type="bullet">
/// <item>Level 5, the Powder Magazine, 2x2: a blasted chamber, its corners rounded, stone pillars for
/// cover from the cannonballs, a ring of ledges at the double jump's 96 (S2) along the north wall and
/// round the north-west curve, shelves at the rocket jump's 224 (B1), red warning lamps high on the walls
/// and one white work light over the middle; in at its west socket's south cell, out at its east socket's
/// north cell.</item>
/// <item>Level 10, the Bore, 3x3: the cavern the robots bored up into, its roof on four outer pillars and a
/// central one (each a named brush the Tank drills, its rubble a named brush that falls), the Tank's
/// track a spiral of about 10,500 from the robots' lift at the east wall in to the central pillar past the
/// four (<see cref="BoreTrack"/>), its treads worn into the floor, steel catwalks at 384 (B2) over the
/// track along the north and south walls, rock steps at 96 and 144 (S2, S3), cover along each leg, the
/// robots' light towers; in and out at the middle of its west and east walls.</item>
/// <item>Level 15, the Pit, 4x4: a lake of black slime round a stone platform, the platform's outer
/// sections named brushes the encounter breaks away (phases 2 to 4) and the slime staged hazards under
/// them, islands, pillar tops at 96 and 144 (S2, S3), ledges at 224 and 384 (B1, B2), torches on the
/// platform only and the slime's faint glow; its one socket west: it is the bottom of the shaft.</item>
/// </list>
/// </summary>
public static class ArenaShape
{
    /// <summary>Each boss depth's arena: its footprint, its sockets (wall and cell edge) and height.</summary>
    public static IReadOnlyDictionary<int, (int Cells, (CellFace Face, int Offset)[] Sockets, int Height)> Of { get; } =
        new Dictionary<int, (int, (CellFace, int)[], int)>
        {
            // 768, not 1,024 (brief p16): Sir Nukesalot, at the largest scale the nav's hulls hold, read small against a
            // 1,024-high hall; 768 still holds the rocket air (576) over the floor and the air over the shelves at 224.
            [5] = (2, [(CellFace.West, 0), (CellFace.East, 1)], 768),
            [10] = (3, [(CellFace.West, 1), (CellFace.East, 1)], 1280),
            [15] = (4, [(CellFace.West, 1)], 1280),
        };

    /// <summary>The MvM Tank's movement hull (stock <c>boss_tank.mdl</c>'s header: -115.3 -79.1 -16.8 to 117.3 79.1 176.2): the disc its footprint turns in, and its height.</summary>
    public const float TankRadius = 141.5f, TankHeight = 177;

    /// <summary>How far the dressing keeps from the Tank's disc along its track.</summary>
    public const float TrackMargin = 16;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "arena");
        yield return Magazine(kit, theme, rng);
        yield return Bore(kit, theme, rng);
        yield return Pit(kit, theme, rng);
    }

    /// <summary>An arena's builder for <paramref name="depth"/>: its footprint, sockets and height.</summary>
    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, int depth) =>
        new RoomBuilder(name, kit, theme).WithFootprint(Of[depth].Cells).WithSockets(Of[depth].Sockets).WithHeight(Of[depth].Height)
            .WithEntry(new RoomEntry(["boss_arena"], MinDepth: depth, MaxDepth: depth, Jump: true)).WithMapLabel("Arena").WithOwnLamps();

    /// <summary>The theme's arena lamp of <paramref name="mount"/>, or null (the grey measuring set has none).</summary>
    private static LampKind? ArenaLamp(RoomTheme theme, LampMount mount) => theme.ArenaLamps.FirstOrDefault(k => k.Mount == mount);

    /// <summary>A socket's middle on its wall's inner face, on the floor.</summary>
    private static Point DoorPoint(RoomBuilder b, CellFace face, int offset)
    {
        float c = b.Extent, mid = b.Kit.Cell / 2f + offset * b.Kit.Cell, t = b.Kit.WallDepth;
        return face switch
        {
            CellFace.West => new(t, mid, b.Floor),
            CellFace.East => new(c - t, mid, b.Floor),
            CellFace.North => new(mid, c - t, b.Floor),
            _ => new(mid, t, b.Floor),
        };
    }

    /// <summary>The yaw from (x, y) toward (tx, ty), whole degrees.</summary>
    private static int Toward(float x, float y, float tx, float ty) => ((int)MathF.Round(MathF.Atan2(ty - y, tx - x) * 180 / MathF.PI) + 360) % 360;

    /// <summary>A dim fill light on a grid over the body, a fill's role (no fixture): the dark's floor of light, so every walked floor is lit by the lint's rule.</summary>
    private static void FillGrid(RoomBuilder b, float pitch, float above, Lamp lamp)
    {
        Rect r = b.Body;
        int nx = Math.Max(1, (int)MathF.Ceiling(r.Width / pitch)), ny = Math.Max(1, (int)MathF.Ceiling(r.Depth / pitch));
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                b.WithFill(new Point(MathF.Round(r.X0 + r.Width * (i + 0.5f) / nx), MathF.Round(r.Y0 + r.Depth * (j + 0.5f) / ny), b.Floor + above), lamp);
    }

    //-------------------------------------------------------------------------
    // Level 5: the Powder Magazine.
    //-------------------------------------------------------------------------

    /// <summary>The radius the Magazine's north-west and south-east corners are blasted round to.</summary>
    public const float MagazineRound = 640;

    /// <summary>How far under the Magazine's roof its warm fill hangs.</summary>
    public const float MagazineFillUnderRoof = 96;

    private static Room Magazine(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        var b = New("arena_a", kit, theme, 5);
        Rect body = b.Body;
        float m = b.Extent / 2, f = b.Floor, small = kit.DoorLow - 96;
        // Blasted round: the two corners without a door near them deep, the others short of the sockets.
        b.WithBrushes(b.RoundCorner(body, Corner.NorthWest, MagazineRound, 6)).WithBrushes(b.RoundCorner(body, Corner.SouthEast, MagazineRound, 6))
            .WithBrushes(b.RoundCorner(body, Corner.SouthWest, small, 4)).WithBrushes(b.RoundCorner(body, Corner.NorthEast, small, 4));

        // The boss in the middle, facing the way in (the west socket's south cell).
        Point entry = DoorPoint(b, CellFace.West, 0);
        b.WithBossSpot(new Point(m, m, f), Toward(m, m, entry.X, entry.Y));

        // Stone pillars on a ring for cover from his cannonballs, turned by the seed.
        float turn = rng.Pick<float>([0, 15, 30]);
        for (int k = 0; k < 6; k++)
        {
            float a = (turn + 60 * k) * MathF.PI / 180;
            b.Pillar(MathF.Round(m + 520 * MathF.Cos(a)), MathF.Round(m + 520 * MathF.Sin(a)), 72, 10);
        }

        // The ring ledge at the double jump's height (S2): along the north wall from the north-west curve, its
        // stairs at the east end for everyone, and round the curve itself, a bench of the rock's ring.
        int s2 = JumpTable.Of(JumpTier.S2).Ledge(0.5f, kit.NavVoxel);
        float nwX = body.X0 + MagazineRound, nwY = body.Y1 - MagazineRound;
        b.Mezzanine(CellFace.North, nwX - 8, 1300, depth: 160);
        b.WithBrushes(Brush.RingWall(nwX, nwY, MagazineRound - 160, MagazineRound + 8, 24, f, f + s2, theme.Block, keep: i => i is >= 6 and < 12));
        // And round the south-east curve, with a reward on it and no stair: a Scout's.
        float seX = body.X1 - MagazineRound, seY = body.Y0 + MagazineRound;
        b.WithBrushes(Brush.RingWall(seX, seY, MagazineRound - 160, MagazineRound + 8, 24, f, f + s2, theme.Block, keep: i => i is >= 18 and < 24));
        float seA = 315 * MathF.PI / 180, seR = MagazineRound - 80;
        b.Mark(MarkerKind.Container, MathF.Round(seX + seR * MathF.Cos(seA)), MathF.Round(seY + seR * MathF.Sin(seA)), above: s2);

        // Shelves at the rocket jump's height (B1) on the west and east walls, between the sockets and the curves.
        b.RocketShelf(CellFace.West, 1100, 1340, depth: 128, t: 0.5f).RocketShelf(CellFace.East, 700, 940, depth: 128, t: 0.5f);

        // Packs (the boss spot is the first), powder barrels on barrel markers.
        b.Pack(m - 300, m - 150).Pack(m + 250, m + 250).Pack(m - 150, m + 330)
            .Mark(MarkerKind.Barrel, m + rng.Pick<float>([-160, 160]), m - 760).Mark(MarkerKind.Barrel, m + 760, m + rng.Pick<float>([-120, 120]));

        // The robots' mouths: his adds come in from the far side, then from the curves.
        b.WithMouth(new Point(body.X1 - 240, body.Y1 - 300, f), 225, 1)
            .WithMouth(new Point(nwX - 260, nwY + 260, f), 315, 2)
            .WithMouth(new Point(seX + 260, seY - 260, f), 135, 3);

        // Red warning lamps high on the flat walls, over every ledge's air; one white work light over the middle.
        if (ArenaLamp(theme, LampMount.Wall) is { } red)
        {
            float z = f + 352;
            foreach (float y in (float[])[760, 1240])
                b.WithLampSite(red, new Point(body.X0, y, z), 0);
            foreach (float x in (float[])[900, 1450])
                b.WithLampSite(red, new Point(x, body.Y1, z), 270);
            foreach (float y in (float[])[820, 1180])
                b.WithLampSite(red, new Point(body.X1, y, z), 180);
            foreach (float x in (float[])[600, 1150])
                b.WithLampSite(red, new Point(x, body.Y0, z), 90);
        }

        if (ArenaLamp(theme, LampMount.Pendant) is { } white)
            b.WithLampSite(white, new Point(m + 1, m + 1, f), 0);
        // A dim warm fill under the roof: the store's dark between its lamps is brown, not black.
        FillGrid(b, 1024, b.CeilingUnderside - f - MagazineFillUnderRoof, new Lamp("255 196 160", 40, 640, 1600));
        return b.Build();
    }

    //-------------------------------------------------------------------------
    // Level 10: the Bore.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The Tank's track, relative to the arena's middle, in order (the server closes it back to the first
    /// point, a way the Tank never drives: the final station brings the roof down). A spiral winding in
    /// counter-clockwise (brief p15: the first, which came back up its own first leg's line and turned the
    /// other way round the middle, read as a squared loop): out of the robots' lift by the east wall it runs
    /// north up the east side to the north-east pillar's station on the north side, west along the north to
    /// the north-west's, south down the west past the south-west's, east along the south to the south-east's,
    /// then north again inside its first leg, west inside the north one and south to the central pillar's
    /// station on its west side: five legs of 2,140, 1,900, 1,520, 1,820 and 3,340, 10,720 in all (the
    /// economy model assumes about 10,700). Every point keeps the Tank's disc (<see cref="TankRadius"/>) 34
    /// or more off the pillars and walls.
    /// </summary>
    public static IReadOnlyList<TrackPoint> BoreTrack { get; } =
    [
        new(new(1120, -300, 0)), new(new(1120, 1100, 0)), new(new(380, 1100, 0), Station: true),
        new(new(-1180, 1100, 0)), new(new(-1180, 760, 0), Station: true),
        new(new(-1180, -760, 0), Station: true), new(new(-1180, -1100, 0)), new(new(300, -1100, 0), Station: true),
        new(new(640, -1100, 0)), new(new(640, 420, 0)), new(new(-420, 420, 0)), new(new(-420, 0, 0), Final: true),
    ];

    /// <summary>The roof pillars relative to the middle, in the order of their stations along the track (north-east, north-west, south-west, south-east, then the central one), and each one's radius.</summary>
    public static IReadOnlyList<(float X, float Y, float Radius)> BorePillars { get; } =
        [(380, 760, 144), (-860, 760, 144), (-860, -760, 144), (300, -760, 144), (0, 0, 224)];

    /// <summary>Where the robots' lift stands, relative to the middle, the Tank's first point inside it; its front (toward +y, the lift turned 90) open onto the first leg.</summary>
    public static (float X, float Y) BoreLift { get; } = (1120, -300);

    private static Room Bore(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        var b = New("arena_b", kit, theme, 10);
        Rect body = b.Body;
        float m = b.Extent / 2, f = b.Floor;
        b.RoundCorners(body, kit.DoorLow - 96, 4);
        Point P(float x, float y) => new(m + x, m + y, f);

        // The boss at the middle's west side, before the central pillar, facing the way in.
        Point entry = DoorPoint(b, CellFace.West, 1);
        b.WithBossSpot(P(-600, 0), Toward(m - 600, m, entry.X, entry.Y));

        // The roof pillars, each a named brush the encounter fells, and the rubble it leaves (off the track and off
        // every pillar's box: the server's graph keeps one obstacle per nav leaf, brief p15). A pillar casts no shadow:
        // the floor under it, lit as if it stood, shows no dark disc when it falls (brief p15).
        for (int n = 1; n <= BorePillars.Count; n++)
        {
            var (x, y, r) = BorePillars[n - 1];
            b.WithNamedBrush(BossNames.Pillar(n), [Brush.Prism(m + x, m + y, r, 12, f, b.CeilingUnderside, theme.Block, startDegrees: 15)], castsShadow: false);
            b.WithNamedBrush(BossNames.Rubble(n), BoreRubble[n - 1].Select(h => Parts.Rock(m + h.X, m + h.Y, h.Half, h.Half, f, f + rng.Pick<float>([48, 64]), theme.Block, rng)),
                startDisabled: true);
        }

        // The track: its points, its treads worn in the floor (strips, never solid), and the Tank's hull kept clear along it.
        var track = BoreTrack.Select(p => p with { At = new Point(m + p.At.X, m + p.At.Y, f) }).ToArray();
        b.WithTrack(track);
        var treads = new List<Brush>();
        Surface worn = theme.ArenaSurfaces.Count > 1 ? theme.ArenaSurfaces[1] : theme.Floor;
        for (int i = 1; i < track.Length; i++)
        {
            Point a = track[i - 1].At, c = track[i].At;
            float reach = TankRadius + TrackMargin;
            b.WithKeepClear(new Point(Math.Min(a.X, c.X) - reach, Math.Min(a.Y, c.Y) - reach, f), new Point(Math.Max(a.X, c.X) + reach, Math.Max(a.Y, c.Y) + reach, f + TankHeight + 15));
            treads.AddRange(Treads(a, c, f, worn));
        }

        // No tread inside the robots' lift, which stands over the track's start (its floor is the lift's).
        var (lx, ly) = BoreLift;
        var liftFloor = new Rect(m + lx - 230, m + ly - 340, m + lx + 230, m + ly + 300);
        b.WithStrips(treads.SelectMany(t => Rects.Subtract(new Rect(t.Mins.X, t.Mins.Y, t.Maxs.X, t.Maxs.Y), [liftFloor])
            .Select(r => Brush.Box(new Point(r.X0, r.Y0, t.Mins.Z), new Point(r.X1, r.Y1, t.Maxs.Z), worn))));

        // Steel catwalks at the crouched rocket jump's height (B2) over the track along the north and south walls, each with a reward.
        int b2 = JumpTable.Of(JumpTier.B2).Ledge(0.5f, kit.NavVoxel);
        Surface deck = theme.ArenaSurfaces.Count > 0 ? theme.ArenaSurfaces[0] : theme.Block;
        Catwalk(b, CellFace.North, m - 700, m + 300, 512, b2, deck);
        Catwalk(b, CellFace.South, m - 700, m + 300, 512, b2, deck);

        // Rock steps for the Scout: a climbing row in the west band (96 then 144, S3) and one at 96 between the first leg
        // and the fifth, which run up the east side side by side, north of the lift (S2): the row is that lane's cover.
        b.SteppingPillars(m - 860, m - 400, Compass.North, JumpTier.S3, count: 3, radius: 56);
        b.SteppingPillars(m + 880, m + 120, Compass.North, JumpTier.S2, count: 3, radius: 56);

        // Cover along each leg, on its inner side: low lumps of rock a player crouches behind.
        foreach (var (x0, y0, x1, y1) in BoreCover)
            b.WithBrush(Parts.Rock(m + (x0 + x1) / 2, m + (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2, f, f + JumpTable.Of(JumpTier.T1).LedgeMax, theme.Block, rng, sides: 8));

        // Packs (the boss spot is the first), a dispenser by the way in, a barrel by the robots' end.
        b.Pack(m - 640, m - 260).Pack(m - 200, m + 700).Pack(m - 640, m + 300)
            .Mark(MarkerKind.Dispenser, m - 1380, m + 360).Mark(MarkerKind.Barrel, m + 1000, m - 880);

        // The robots' end: their lift over the bore (the Tank drives out of its front, leg 1's mouth) and two planked
        // tunnel mouths in the east wall (legs 2 and 3).
        if (theme.SetPieces.Count >= 2)
        {
            PropModel lift = theme.SetPieces[0], planks = theme.SetPieces[1];
            b.WithSetPiece(lift, new Point(m + lx, m + ly, f - lift.Mins.Z), 90);
            // The planks across each mouth stand in its portal (Portal), half way into it.
            b.WithSetPiece(planks, new Point(body.X1 - 72, m + 900, f - planks.Mins.Z), 180);
            b.WithSetPiece(planks, new Point(body.X1 - 72, m - 900, f - planks.Mins.Z), 180);
            // A cold work light before the lift's open front, toward the arena: the frame stood in the dark and read near
            // black in game (brief p15); lit from inside, only its inner faces took it.
            b.WithFill(new Point(m + lx - 280, m + ly + 160, f + 220), new Lamp("206 218 240", 110, 256, 900));
        }

        // Legs 2 and 3 come out of tunnel mouths in the east wall: a rock portal round a black back, planks across it.
        Portal(b, m + 900);
        Portal(b, m - 900);
        b.WithMouth(P(lx, ly), 90, 1).WithMouth(P(1300, 900), 180, 2).WithMouth(P(1300, -900), 180, 3);

        // The robots' light towers round the arena, beyond the track, aimed across it; a dim cold fill so no floor is black.
        if (ArenaLamp(theme, LampMount.Floor) is { } tower)
        {
            // Past the catwalks' ends (nothing stands within a take-off's 128 of a ledge only a jump reaches).
            foreach (float x in (float[])[-950, 600])
            {
                b.WithLampSite(tower, P(x, 1380), 270);
                b.WithLampSite(tower, P(x, -1380), 90);
            }

            foreach (float y in (float[])[-560, 560])
                b.WithLampSite(tower, P(-1420, y), 0);
            // One by the east wall north of the lift (the lift fills the wall's south half between the mouths).
            b.WithLampSite(tower, P(1400, 420), 180);
            // Between the outer legs and the inner ones, aimed in across the inner legs at the central pillar.
            b.WithLampSite(tower, P(-40, 760), 270);
            b.WithLampSite(tower, P(-40, -760), 90);
        }

        FillGrid(b, 768, 640, new Lamp("150 164 200", 10, 640, 1600));
        return b.Build();
    }

    /// <summary>
    /// A tunnel mouth in the east wall at <paramref name="y"/>: a buttress of rock standing out of the wall floor to roof
    /// round an opening 320 wide and 240 high, its back the coal's black, as if the tunnel ran on into the dark.
    /// </summary>
    private static void Portal(RoomBuilder b, float y)
    {
        Rect body = b.Body;
        float f = b.Floor, x0 = body.X1 - 128, x1 = body.X1, half = 160, jamb = 64, top = f + 240, roof = b.CeilingUnderside;
        Surface back = b.Theme.ArenaSurfaces.Count > 1 ? b.Theme.ArenaSurfaces[1] : b.Theme.Wall;
        b.WithBox(new Point(x0, y - half - jamb, f), new Point(x1, y - half, roof), b.Theme.Wall)
            .WithBox(new Point(x0, y + half, f), new Point(x1, y + half + jamb, roof), b.Theme.Wall)
            .WithBox(new Point(x0, y - half, top), new Point(x1, y + half, roof), b.Theme.Wall)
            .WithBox(new Point(x1 - 8, y - half, f), new Point(x1, y + half, top), back);
    }

    /// <summary>The Bore's cover, relative to its middle: the box of each low lump of rock beside a leg on its inner side.</summary>
    public static IReadOnlyList<(float X0, float Y0, float X1, float Y1)> BoreCover { get; } =
    [
        (400, -240, 464, -40), (400, 140, 464, 320),          // the fifth leg, west of it
        (-360, 840, -160, 904), (-1000, 380, -936, 560),      // north leg (south of it), west leg (east of it)
        (-1000, -560, -936, -380), (-360, -904, -160, -840),  // west leg, south leg (north of it)
    ];

    /// <summary>
    /// The rubble each pillar leaves when it falls, relative to the middle: heaps (a centre and a half width) a
    /// step and a crouch-jump high on a side of it the track does not run, new cover by where it stood; each
    /// keeps 158 (the Tank's disc and the margin) off the track, and all of one pillar's heaps lie on one side
    /// of its box, 32 or more off it, so no nav leaf lies inside both (brief p15); a pillar's heaps overlap,
    /// one heap of rubble with no way between its lumps.
    /// </summary>
    public static IReadOnlyList<(float X, float Y, float Half)[]> BoreRubble { get; } =
    [
        [(150, 770, 56), (140, 870, 48)],
        [(-620, 780, 64), (-610, 890, 56)],
        [(-620, -640, 64), (-610, -520, 60)],
        [(300, -520, 60), (190, -500, 56)],
        [(312, -40, 64), (316, 70, 56)],
    ];

    /// <summary>Two treads 40 wide either side of a straight leg's line, a unit proud of the floor, run past each end to meet the next leg.</summary>
    private static IEnumerable<Brush> Treads(Point a, Point c, float f, Surface surface)
    {
        const float off = 60, half = 20, run = 80;
        bool alongX = Math.Abs(c.X - a.X) >= Math.Abs(c.Y - a.Y);
        if (Math.Abs(c.X - a.X) > 0.5f && Math.Abs(c.Y - a.Y) > 0.5f)
            yield break; // only straight legs
        foreach (float side in (float[])[-off, off])
        {
            if (alongX)
                yield return Brush.Box(new Point(Math.Min(a.X, c.X) - run, a.Y + side - half, f), new Point(Math.Max(a.X, c.X) + run, a.Y + side + half, f + 1), surface);
            else
                yield return Brush.Box(new Point(a.X + side - half, Math.Min(a.Y, c.Y) - run, f), new Point(a.X + side + half, Math.Max(a.Y, c.Y) + run, f + 1), surface);
        }
    }

    /// <summary>A catwalk (B2) along <paramref name="wall"/> from <paramref name="from"/> to <paramref name="to"/>, <paramref name="depth"/> out, its deck at <paramref name="h"/>, its edge in the theme's trim, a container on it.</summary>
    private static void Catwalk(RoomBuilder b, CellFace wall, float from, float to, float depth, int h, Surface deck)
    {
        b.Holds(JumpTier.B2);
        var (mins, maxs) = JumpFeatures.WallBounds(b, wall, from, to, 0, depth, h - JumpFeatures.Slab, h);
        b.WithBrush(Brush.Box(mins, maxs, b.Theme.TrimOrBlock, deck));
        b.WithMarker(MarkerKind.Container, new Point(MathF.Round((mins.X + maxs.X) / 2), MathF.Round(wall == CellFace.North ? maxs.Y - 64 : mins.Y + 64), b.Floor + h));
    }

    /// <summary>The Pit's rim glow off the slime by the walls: faint, short, green.</summary>
    public static Lamp ShoreGlow { get; } = new("70 130 50", 20, 128, 448);

    /// <summary>How far apart the rim glows stand, and how far in from the wall.</summary>
    public const float ShorePitch = 512, ShoreInset = 72;

    /// <summary>Glows (fills, no fixture) along the walls round <paramref name="lake"/>, <see cref="ShoreInset"/> in from each, every <see cref="ShorePitch"/>, at <paramref name="z"/>.</summary>
    private static void ShoreGlows(RoomBuilder b, Rect lake, float z, Lamp glow)
    {
        Rect r = b.Body;
        float x0 = Math.Max(lake.X0, r.X0) + ShoreInset, x1 = Math.Min(lake.X1, r.X1) - ShoreInset;
        float y0 = Math.Max(lake.Y0, r.Y0) + ShoreInset, y1 = Math.Min(lake.Y1, r.Y1) - ShoreInset;
        int nx = Math.Max(1, (int)MathF.Round((x1 - x0) / ShorePitch)), ny = Math.Max(1, (int)MathF.Round((y1 - y0) / ShorePitch));
        for (int i = 0; i <= nx; i++)
        {
            float x = MathF.Round(x0 + (x1 - x0) * i / nx);
            b.WithFill(new Point(x, y0, z), glow).WithFill(new Point(x, y1, z), glow);
        }

        for (int j = 1; j < ny; j++)
        {
            float y = MathF.Round(y0 + (y1 - y0) * j / ny);
            b.WithFill(new Point(x1, y, z), glow);
        }
    }

    //-------------------------------------------------------------------------
    // Level 15: the Pit.
    //-------------------------------------------------------------------------

    /// <summary>The Pit's platform, relative to the middle: its core (world, it never breaks) and its sections by the phase that breaks each away (2: east, 3: north, 4: south; the west stays, toward the way in).</summary>
    public static (Rect Core, IReadOnlyList<(int Phase, Rect Section)> Sections, Rect West) PitPlatform { get; } =
        (new Rect(-224, -224, 224, 224),
         [(2, new Rect(224, -512, 512, 512)), (3, new Rect(-512, 224, 224, 512)), (4, new Rect(-512, -512, 224, -224))],
         new Rect(-512, -224, -224, 224));

    /// <summary>The Pit's islands, relative to the middle, a step up out of the slime.</summary>
    public static IReadOnlyList<Rect> PitIslands { get; } =
        [new(-1120, 480, -800, 800), new(800, -800, 1120, -480), new(-1120, -1120, -800, -800), new(800, 800, 1120, 1120)];

    /// <summary>The slime's depth over the lake floor.</summary>
    public const int SlimeDepth = 12;

    private static Room Pit(RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        var b = New("arena_c", kit, theme, 15);
        Rect body = b.Body;
        float m = b.Extent / 2, f = b.Floor, step = 16;
        b.RoundCorners(body, kit.DoorLow - 96, 4);
        Rect R(Rect r) => new(m + r.X0, m + r.Y0, m + r.X1, m + r.Y1);

        // The platform: the core and the west a step up, the sections named brushes the encounter breaks away.
        var (core, sections, west) = PitPlatform;
        b.Block(R(core), step).Block(R(west), step);
        foreach (var (phase, section) in sections)
        {
            Rect s = R(section);
            b.WithNamedBrush(BossNames.Platform(phase), [Brush.Box(new Point(s.X0, s.Y0, f), new Point(s.X1, s.Y1, f + step), theme.Block)]);
        }

        // The islands, a step up, and the shore by the way in.
        foreach (Rect island in PitIslands)
            b.Block(R(island), step);
        var shore = new Rect(body.X0, body.Y0, body.X0 + 448, body.Y1);

        // The slime: over the lake floor beyond the shore, under the platform and the islands too (they stand in it).
        Rect lake = new(shore.X1, body.Y0, body.X1, body.Y1);
        b.WithWater(lake.Grow(-8), f, f + SlimeDepth, theme.Water ?? PoolShape.WaterMaterial);

        // The slime is harmless until the first break; then the lake is on, and each section's floor as it goes.
        List<Rect> dry = [R(core), R(west), .. sections.Select(s => R(s.Section)), .. PitIslands.Select(R)];
        foreach (Rect r in Rects.Subtract(lake, dry))
            b.WithStagedHazard(2, "slime", new Point(r.X0, r.Y0, f), new Point(r.X1, r.Y1, f + 32));
        foreach (var (phase, section) in sections)
        {
            Rect s = R(section);
            b.WithStagedHazard(phase, "slime", new Point(s.X0, s.Y0, f), new Point(s.X1, s.Y1, f + 32));
        }

        // The boss on the platform's middle, facing the way in.
        Point entry = DoorPoint(b, CellFace.West, 1);
        b.WithBossSpot(new Point(m, m, f + step), Toward(m, m, entry.X, entry.Y));

        // Height is safety: pillar tops in the slime for the Scout (96, then 144), ledges for the blast jumpers (224, 384).
        b.SteppingPillars(m - 1500, m + 1000, Compass.East, JumpTier.S3, count: 3, radius: 64);
        b.SteppingPillars(m + 1500, m - 1000, Compass.West, JumpTier.S2, count: 3, radius: 64);
        b.Shelf(JumpTier.B1, CellFace.North, m - 400, m + 400, depth: 160, t: 0.5f)
            .Shelf(JumpTier.B2, CellFace.South, m - 400, m + 400, depth: 160, t: 0.5f)
            .Shelf(JumpTier.B2, CellFace.East, m - 300, m + 300, depth: 160, t: 0.5f);

        // Packs (the boss spot is the first), a shrine on an island, barrels in the slime's shallows.
        foreach (var (x, y) in ((float, float)[])[(960, 960), (-960, -960), (960, -640)])
            b.WithMarker(MarkerKind.PackSpawn, new Point(m + x, m + y, f + step), radius: 96);
        b.Mark(MarkerKind.Shrine, m - 960, m + 640, above: step).Mark(MarkerKind.Barrel, m + 640, m + 1500);

        // The robots' mouths round the lake, one more each phase.
        b.WithMouth(new Point(body.X1 - 200, m, f), 180, 1).WithMouth(new Point(m, body.Y1 - 200, f), 270, 2)
            .WithMouth(new Point(m, body.Y0 + 200, f), 90, 3).WithMouth(new Point(body.X1 - 300, body.Y1 - 300, f), 225, 4);

        // Torches on the platform only, on what never breaks away: the core's corners and the west's, 96 in from every edge a
        // section drops from (brief p15: at 32 in, platform 3's stood at its edge and floated once it dropped).
        if (ArenaLamp(theme, LampMount.Floor) is { } torch)
            foreach (var (x, y) in ((float, float)[])[(-432, -128), (-432, 128), (-128, -128), (-128, 128), (128, -128), (128, 128)])
                b.WithLampSite(torch, new Point(m + x, m + y, f + step), Toward(m + x, m + y, m, m));

        // The slime's own faint glow over the lake, so a walked floor is never black by the lint's rule.
        FillGrid(b, 1024, 96, new Lamp("60 110 40", 9, 700, 1500));
        // And a low glow along its rim by the walls, so the lake reads as a lake from the platform: its edge and the wet
        // foot of the rock lit green where it meets the slime (brief p15: only the torch-lit platform showed).
        ShoreGlows(b, lake, f + SlimeDepth + 24, ShoreGlow);
        return b.Build();
    }
}
