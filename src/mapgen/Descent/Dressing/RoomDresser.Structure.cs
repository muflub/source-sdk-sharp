namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// The dressing pass's structure and its placed pieces (the review of
/// 2026-11-10): pilasters up tall walls, trim bands along them, lamps in
/// pools along the route (hung from a set or stood on a crate), work lamps
/// in large rooms, track on each door's axis with sidings, and clutter in
/// groups.
/// </summary>
public static partial class RoomDresser
{
    private sealed partial class Dresser
    {
        //---------------------------------------------------------------------
        // Walls: straight stretches of flat wall.
        //---------------------------------------------------------------------

        /// <summary>The straight stretches of wall at <paramref name="z"/> above the floor, each its sites in order along it.</summary>
        private List<List<WallSite>> Stretches(float z) =>
            [.. WallSites(z)
                .GroupBy(s => (s.Dx, s.Dy, Line: s.Dx != 0 ? s.Face.X : s.Face.Y))
                .SelectMany(g => Contiguous([.. g.OrderBy(s => s.Dx != 0 ? s.Face.Y : s.Face.X)]))
                .OrderBy(st => st[0].Face.X).ThenBy(st => st[0].Face.Y).ThenBy(st => st[0].Dx).ThenBy(st => st[0].Dy)];

        /// <summary>A box on the wall of <paramref name="s0"/>'s stretch from <paramref name="a0"/> to <paramref name="a1"/> along it, <paramref name="proud"/> off it.</summary>
        private static Box OnWall(WallSite s0, float a0, float a1, float z0, float z1, float proud)
        {
            if (s0.Dx != 0)
            {
                float x = s0.Face.X;
                return s0.Dx > 0 ? new Box(new(x - proud, a0, z0), new(x, a1, z1)) : new Box(new(x, a0, z0), new(x + proud, a1, z1));
            }

            float y = s0.Face.Y;
            return s0.Dy > 0 ? new Box(new(a0, y - proud, z0), new(a1, y, z1)) : new Box(new(a0, y, z0), new(a1, y + proud, z1));
        }

        /// <summary>Where a stretch starts along its wall, and how long it is.</summary>
        private static (float Start, float Span) Extent(List<WallSite> stretch)
        {
            var s0 = stretch[0];
            float first = (s0.Dx != 0 ? s0.Face.Y : s0.Face.X) - RoomColumns.Probe;
            return (first, stretch.Count * RoomLint.Grid);
        }

        /// <summary>Whether the wall of a stretch stands flat at the face's line at <paramref name="z"/>, at <paramref name="along"/>.</summary>
        private bool FlatAt(WallSite s0, float along, float z)
        {
            Point face = s0.Dx != 0 ? new(s0.Face.X, along, z) : new(along, s0.Face.Y, z);
            return solids.Ray(Out(face, s0.Dx, s0.Dy, 8), s0.Dx, s0.Dy) is { } r && Math.Abs(r - 8) < 0.01f;
        }

        /// <summary>The rules a structural piece keeps: the doors, the markers, the ledges' and a jump room's air, the cell, and a solid one the giant's lane.</summary>
        private bool StructureFits(Box box, bool solid)
        {
            float c = room.Extent;
            if (box.Mins.X < 0 || box.Mins.Y < 0 || box.Maxs.X > c || box.Maxs.Y > c || box.Maxs.Z > room.Height)
                return false;
            if (doors.Any(d => d.DistanceTo(box) < DoorClear))
                return false;
            if (markers.Any(m => box.Mins.Z < m.Z + RoomLint.HullHeight && box.Maxs.Z > m.Z - 1 && box.LevelDistanceTo(m.X, m.Y) < MarkerClear))
                return false;
            if (InLedgeAir(box) || InJumpAir(box))
                return false;
            if (solid && box.Mins.Z < sill + FloorPlan.GiantHeight && plan.Covered(box).Any(lane.Contains))
                return false;
            if (placed.Any(p => p.Role != Role.Set && p.Box.Overlaps(box)))
                return false;
            return !solids.Blocked(box.Mins, box.Maxs);
        }

        private void AddStructure(Box box, Surface? surface = null) => AddBrush(box, surface ?? theme.TrimOrBlock);

        /// <summary>
        /// A dressing brush, moved off a larger room's internal cell faces: the tools refuse a face lying
        /// on one (RoomBuilder holds the shapes to it), so a side on one is pushed out a unit and a half.
        /// </summary>
        private void AddBrush(Box box, Surface surface)
        {
            float Off(float v, float by)
            {
                for (int k = 1; k < room.Cells; k++)
                    if (Math.Abs(v - k * kit.Cell) < 0.01f)
                        return v + by;
                return v;
            }

            box = new Box(new(Off(box.Mins.X, -1.5f), Off(box.Mins.Y, -1.5f), box.Mins.Z), new(Off(box.Maxs.X, 1.5f), Off(box.Maxs.Y, 1.5f), box.Maxs.Z));
            brushes.Add(Brush.Box(box.Mins, box.Maxs, surface));
            placed.Add((box, Role.Set));
        }

        //---------------------------------------------------------------------
        // Roof beams, cool fill, hazard stripes (the review of 2026-11-10).
        //---------------------------------------------------------------------

        /// <summary>
        /// Beams across the roof of a hall or shaft (a room at least 768 high,
        /// not a hallway), every <see cref="RoomTheme.RoofBeams"/> across its
        /// body's shorter side, from wall to wall a beam's depth under the
        /// ceiling, where nothing stands (a ledge's air, a jump room's air, a
        /// marker): something overhead in the tall rooms.
        /// </summary>
        private void RoofBeams(int pitch)
        {
            if (room.Height < 768 || IsHallway(room))
                return;
            Rect body = room.Body;
            bool acrossX = body.X1 - body.X0 <= body.Y1 - body.Y0;
            float lo = acrossX ? body.Y0 : body.X0, hi = acrossX ? body.Y1 : body.X1;
            int count = (int)((hi - lo) / pitch);
            if (count < 1)
                return;
            const float depth = 24, width = 24;
            float top = room.Height - kit.WallDepth;
            int added0 = brushes.Count;
            for (int k = 0; k < count; k++)
            {
                float a = lo + (hi - lo - (count - 1) * pitch) / 2 + k * pitch;
                float z1 = top, z0 = top - depth;
                Point mid = acrossX ? new((body.X0 + body.X1) / 2, a, z0 + depth / 2) : new(a, (body.Y0 + body.Y1) / 2, z0 + depth / 2);
                int dx = acrossX ? 1 : 0, dy = acrossX ? 0 : 1;
                if (solids.Ray(mid, dx, dy) is not { } r1 || solids.Ray(mid, -dx, -dy) is not { } r0)
                    continue;
                Box box = acrossX
                    ? new Box(new(mid.X - r0, a - width / 2, z0), new(mid.X + r1, a + width / 2, z1))
                    : new Box(new(a - width / 2, mid.Y - r0, z0), new(a + width / 2, mid.Y + r1, z1));
                if (StructureFits(box, solid: true))
                    AddStructure(box);
            }

            if (brushes.Count > added0)
                solids = new RoomSolids(room, brushes);
        }

        /// <summary>
        /// A dim cold light under the roof every <paramref name="pitch"/> over the
        /// room's floor (<see cref="RoomTheme.CoolFill"/>): the shadow between the
        /// lamps' pools is cool, so the pools read as pools.
        /// </summary>
        private void CoolFill(Lamp light, int pitch)
        {
            float c = room.Extent;
            for (float x = pitch / 2f; x < c; x += pitch)
                for (float y = pitch / 2f; y < c; y += pitch)
                {
                    if (solids.FloorBelow(x, y, room.Height - kit.WallDepth - 1) is not { } floor || solids.CeilingAbove(x, y, floor + 1) is not { } ceil)
                        continue;
                    if (ceil - floor < 96)
                        continue;
                    added.Add(new RoomEntity("light", new Point(x, y, ceil - 24), [.. light.Keys, new(RoleKey, nameof(Role.Fill))], [], []));
                }
        }

        /// <summary>
        /// The theme's hazard stripe (<see cref="RoomTheme.Hazard"/>): one band over
        /// each door's head on the room's side, and one along the top edge of every
        /// raised deck or ledge built of the theme's block, proud of its face.
        /// </summary>
        private void HazardStripes(Surface hazard)
        {
            float c = room.Extent, t = kit.WallDepth;
            int added0 = brushes.Count;
            foreach (var (face, offset) in room.Sockets)
            {
                float lo = kit.DoorLow - 16 + offset * kit.Cell, hi = kit.DoorHigh + 16 + offset * kit.Cell, z0 = kit.Head + 8, z1 = kit.Head + 24;
                if (z1 > room.Height - t)
                    continue;
                Box box = face switch
                {
                    CellFace.East => new Box(new(c - t - 2, lo, z0), new(c - t, hi, z1)),
                    CellFace.West => new Box(new(t, lo, z0), new(t + 2, hi, z1)),
                    CellFace.North => new Box(new(lo, c - t - 2, z0), new(hi, c - t, z1)),
                    _ => new Box(new(lo, t, z0), new(hi, t + 2, z1)),
                };
                if (!solids.Blocked(box.Mins, box.Maxs))
                    AddBrush(box, hazard);
            }

            foreach (Brush b in room.World)
            {
                float top = b.Maxs.Z;
                // A deck or a ledge, not a stair's step (both sides a player's width and more).
                if (!b.IsBox || b.Surface != theme.Block || top < sill + 32 || top > room.Height - t - 64
                    || b.Maxs.X - b.Mins.X < 64 || b.Maxs.Y - b.Mins.Y < 64)
                    continue;
                // A flight's lower step (Brush.Stairs: each step a box from the floor to its tread, the next
                // one taller inside it) is no deck: only the flight's top and real decks take the stripe.
                if (room.World.Any(q => q != b && q.IsBox && q.Surface == theme.Block && q.Maxs.Z > top && q.Mins.Z <= b.Mins.Z + 1
                                        && q.Mins.X >= b.Mins.X - 0.5f && q.Maxs.X <= b.Maxs.X + 0.5f && q.Mins.Y >= b.Mins.Y - 0.5f && q.Maxs.Y <= b.Maxs.Y + 0.5f))
                    continue;
                float z0 = top - 12;
                foreach (var (dx, dy) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                {
                    Box box = (dx, dy) switch
                    {
                        (1, 0) => new Box(new(b.Maxs.X, b.Mins.Y, z0), new(b.Maxs.X + 1, b.Maxs.Y, top)),
                        (-1, 0) => new Box(new(b.Mins.X - 1, b.Mins.Y, z0), new(b.Mins.X, b.Maxs.Y, top)),
                        (0, 1) => new Box(new(b.Mins.X, b.Maxs.Y, z0), new(b.Maxs.X, b.Maxs.Y + 1, top)),
                        _ => new Box(new(b.Mins.X, b.Mins.Y - 1, z0), new(b.Maxs.X, b.Mins.Y, top)),
                    };
                    // Only an edge open to the room along its whole length.
                    var probe = new Box(box.Mins + new Point(dx * 2, dy * 2, 1), box.Maxs + new Point(dx * 2, dy * 2, -1));
                    if (solids.Blocked(probe.Mins, probe.Maxs) || box.Maxs.X - box.Mins.X + box.Maxs.Y - box.Mins.Y < 32)
                        continue;
                    if (doors.Any(d => d.DistanceTo(box) < 8))
                        continue;
                    AddBrush(box, hazard);
                }
            }

            if (brushes.Count > added0)
                solids = new RoomSolids(room, brushes);
        }

        //---------------------------------------------------------------------
        // Pilasters: sets or posts up a tall wall.
        //---------------------------------------------------------------------

        private void Pilasters(Pilasters ps)
        {
            if (room.Height < ps.MinHeight)
                return;
            float top = room.Height - kit.WallDepth;
            int added0 = brushes.Count;
            foreach (var stretch in Stretches(64))
            {
                var (start, span) = Extent(stretch);
                if (span < ps.Spacing)
                    continue;
                var stood = new List<float>();
                var s0 = stretch[0];
                bool Stand(float a)
                {
                    if (!FlatAt(s0, a, sill + 64) || !FlatAt(s0, a, (sill + top) / 2) || !FlatAt(s0, a, top - 24))
                        return false;
                    Box box = OnWall(s0, a - ps.Width / 2f, a + ps.Width / 2f, sill, top, ps.Depth);
                    // A larger room's routes between its sockets run along its walls; a post a hand deep
                    // there takes nothing from a floor twenty giants wide, so its lane does not refuse it.
                    if (!StructureFits(box, solid: room.Cells == 1 || ps.Depth > 24))
                        return false;
                    // A set lining the wall keeps off the edges of decks, stairs and ledges, whose way up a
                    // post beside them would narrow (a deck cut off at level 3's chasm).
                    if (ps.Cap > 0 && NearRaised(box, LedgeClear))
                        return false;
                    AddStructure(box);
                    stood.Add(a);
                    return true;
                }

                if (ps.Cap > 0)
                {
                    // Sets lining a wall (a plate joins them): a post wherever one fits a pitch on from the
                    // last, from the stretch's end, so the bays between doors and ledges fill (brief p4).
                    for (float a = start + ps.Width / 2f + 1; a <= start + span - ps.Width / 2f - 1; a += RoomLint.Grid)
                        if ((stood.Count == 0 || a - stood[^1] >= ps.Spacing) && Stand(a))
                            continue;
                }
                else
                {
                    int count = (int)(span / ps.Spacing);
                    float first = start + (span - (count - 1) * ps.Spacing) / 2;
                    for (int k = 0; k < count; k++)
                        Stand(first + k * ps.Spacing);
                }

                float pitch = ps.Cap > 0 ? 1.5f * ps.Spacing : ps.Spacing;
                // A wall plate between two neighbouring posts makes them a set lining the wall (level 3's
                // halls, brief p4), and the bay under it is where a wall prop stands, clear of both posts.
                for (int k = 0; k + 1 < stood.Count; k++)
                {
                    if (stood[k + 1] - stood[k] > pitch + 1)
                        continue;
                    float b0 = stood[k] + ps.Width / 2f, b1 = stood[k + 1] - ps.Width / 2f;
                    bays.Add((s0, (b0 + b1) / 2, b1 - b0));
                    if (ps.Cap <= 0 || sill + ps.Cap + ps.CapDepth > top - 32)
                        continue;
                    Box plate = OnWall(s0, b0, b1, sill + ps.Cap, sill + ps.Cap + ps.CapDepth, ps.Depth);
                    if (StructureFits(plate, solid: true))
                        AddStructure(plate);
                }
            }

            if (brushes.Count > added0)
                solids = new RoomSolids(room, brushes);
        }

        //---------------------------------------------------------------------
        // Trim bands: a skirting, a dado, belts up a tall wall, a beam under the roof.
        //---------------------------------------------------------------------

        /// <summary>A band's pieces are tried this long along the wall and merged where they fit side by side.</summary>
        private const int BandPiece = 32;

        private void Bands(TrimBands tb)
        {
            float top = room.Height - kit.WallDepth;
            var bands = new List<(float Z0, float Z1, float Proud, bool Solid)>();
            if (tb.Skirting > 0)
                bands.Add((sill, sill + Math.Min(tb.Skirting, TrimBands.MaxSkirting), tb.Proud, false));
            if (tb.Dado > 0 && tb.DadoHeight > tb.Skirting && sill + tb.DadoHeight + tb.Dado < top - tb.Beam - 32)
                bands.Add((sill + tb.DadoHeight, sill + tb.DadoHeight + tb.Dado, tb.Proud, true));
            // A tall wall's belts every 512, so a 1024 wall shows no one repeat of its texture.
            for (float z = sill + 512; tb.Belt > 0 && z < top - tb.Beam - 64; z += 512)
                bands.Add((z, z + tb.Belt, tb.Proud * 2, true));
            if (tb.Beam > 0)
                bands.Add((top - tb.Beam, top, tb.Proud * 2, true));

            int added0 = brushes.Count;
            foreach (var (z0, z1, proud, solid) in bands)
            {
                float mid = (z0 + z1) / 2;
                foreach (var stretch in Stretches(Math.Max(8, mid - sill)))
                {
                    var (start, span) = Extent(stretch);
                    var s0 = stretch[0];
                    float? run = null;
                    float end = start + span;
                    for (float a = start; a < end - 0.5f; a += BandPiece)
                    {
                        float b = Math.Min(a + BandPiece, end);
                        bool fits = FlatAt(s0, (a + b) / 2, mid) && StructureFits(OnWall(s0, a, b, z0, z1, proud), solid)
                                    && (z0 > sill || !(NarrowsGap(s0, a + 1, proud) || NarrowsGap(s0, (a + b) / 2, proud) || NarrowsGap(s0, b - 1, proud)));
                        if (fits && run is null)
                            run = a;
                        if (!fits && run is { } r)
                        {
                            AddStructure(OnWall(s0, r, a, z0, z1, proud), tb.Surface);
                            run = null;
                        }
                    }

                    if (run is { } last && end - last >= BandPiece)
                        AddStructure(OnWall(s0, last, end, z0, z1, proud), tb.Surface);
                }
            }

            if (brushes.Count > added0)
                solids = new RoomSolids(room, brushes);
        }

        /// <summary>
        /// Whether a skirting <paramref name="proud"/> off the wall at <paramref name="along"/> would face
        /// a solid (a post, a pier) closer than <see cref="SkirtingGap"/>: the strip would narrow a slot
        /// a player now stands in to one the navigation's voxels no longer reach (a one-node pocket).
        /// </summary>
        private bool NarrowsGap(WallSite s0, float along, float proud)
        {
            Point face = s0.Dx != 0 ? new(s0.Face.X, along, sill + 8) : new(along, s0.Face.Y, sill + 8);
            return solids.Ray(Out(face, s0.Dx, s0.Dy, proud + 0.5f), -s0.Dx, -s0.Dy) is { } r && r < SkirtingGap;
        }

        /// <summary>The narrowest slot a skirting may face: a giant's width and a voxel.</summary>
        private const float SkirtingGap = FloorPlan.GiantWidth + RoomLint.Grid;

        //---------------------------------------------------------------------
        // Lamps in pools along the route.
        //---------------------------------------------------------------------

        /// <summary>
        /// A room's lamps as pools (<see cref="LampKind.Pools"/>): this few to
        /// this many points along its route, each lamp hung under a set's cap
        /// near the point or set on its stand against the nearest wall, so the
        /// light falls in pools with the dark between them.
        /// </summary>
        private void PoolLamps(LampKind kind, (int Least, int Most) pools)
        {
            if (room.Cells > 1)
            {
                WallPools(kind, pools.Most * room.Cells);
                return;
            }

            var route = routes.SelectMany(r => r).Distinct().ToList();
            if (route.Count == 0)
                return;
            float length = route.Count * RoomLint.Grid;
            int want = Math.Clamp((int)MathF.Round(length / kind.Spacing), pools.Least, pools.Most);
            int placedPools = 0;
            for (int k = 0; k < want; k++)
            {
                var (i, j) = route[(int)((k + 0.5f) * route.Count / want)];
                Point p = plan.At(i, j);
                if (lamps.Any(q => Level(q, p) < kind.Spacing * 0.5f))
                    continue;
                if (HangFromSet(kind, p) || StandNear(kind, p))
                    placedPools++;
            }
        }

        /// <summary>
        /// A larger room's pools (brief p4: a great hall's routes cross its open floor, far from any
        /// wall, so level 3's halls had no lantern): <paramref name="want"/> lamps on their stands round
        /// its walls, evenly round its body's middle, so its floor lies in pools along the walls.
        /// </summary>
        private void WallPools(LampKind kind, int want)
        {
            if (kind.Stand is not { } stand)
                return;
            Point mid = new(room.Body.CentreX, room.Body.CentreY, sill);
            var sites = WallSites(32).OrderBy(s => Math.Atan2(s.Face.Y - mid.Y, s.Face.X - mid.X)).ThenBy(s => s.Face.X).ThenBy(s => s.Face.Y).ToList();
            if (sites.Count == 0 || want <= 0)
                return;
            float apart = sites.Count * RoomLint.Grid / (want * 1.5f);
            int span = Math.Max(1, sites.Count / want);
            for (int j = 0; j < want; j++)
                for (int k = 0; k < span; k++)
                {
                    WallSite s = sites[(j * span + k) % sites.Count];
                    if (lamps.Any(q => Level(q, s.Face) < apart))
                        continue;
                    if (TryLamp(OnStand(kind, stand, s.Face, s.Dx, s.Dy, sill)))
                        break;
                }
        }

        /// <summary>A lamp hung under the cap of a set within reach of <paramref name="p"/>, beside one of its posts.</summary>
        private bool HangFromSet(LampKind kind, Point p)
        {
            PropModel f = kind.Fixture;
            foreach (var (cap, post0, post1, alongX) in setFrames.OrderBy(s => s.Cap.LevelDistanceTo(p.X, p.Y)))
            {
                if (cap.LevelDistanceTo(p.X, p.Y) > 160)
                    break;
                foreach (Box post in (Box[])[post0, post1])
                {
                    // Just inside the post, under the cap.
                    float inward = post == post0 ? 1 : -1;
                    Point c = post.Centre;
                    Point at = alongX ? new(c.X, c.Y + inward * 40, cap.Mins.Z - f.Maxs.Z - 0.5f) : new(c.X + inward * 40, c.Y, cap.Mins.Z - f.Maxs.Z - 0.5f);
                    if (TryLamp(Site(kind, at, alongX ? 0 : 90, hung: true)))
                        return true;
                }
            }

            return false;
        }

        /// <summary>A lamp on its stand against the wall nearest <paramref name="p"/>, off the giant's lane.</summary>
        private bool StandNear(LampKind kind, Point p)
        {
            if (kind.Stand is not { } stand)
                return false;
            foreach (WallSite s in WallSites(32).OrderBy(s => Level(s.Face, p)).Take(24))
                if (Level(s.Face, p) < 256 && TryLamp(OnStand(kind, stand, s.Face, s.Dx, s.Dy, sill)))
                    return true;
            return false;
        }

        //---------------------------------------------------------------------
        // Work lamps: a large room's own light besides the theme's.
        //---------------------------------------------------------------------

        private void WorkLamps(WorkLamps work)
        {
            Room now = room with { World = [.. room.World, .. brushes] };
            var floor = RoomLint.WalkedFloor(now, new RoomColumns(now)).Select(s => new Point(s.X, s.Y, s.Z)).ToList();
            if (floor.Count * RoomLint.Grid * RoomLint.Grid < work.MinFloor)
                return;
            // Aimed at the room's main feature when it has one: the floor round its focus counts most.
            Point focus = room.Focus;
            float Weight(Point q) => work.AtFocus ? (Level(q, focus) < 256 ? 4 : 0) : 1;
            var sites = CoverSites(work.Kind, floor)
                .Select(s => (Site: s, Cone: RoomLint.LightCone.Of(s.Light)!.Value))
                .Select(c => (c.Site, Score: floor.Where(c.Cone.Reaches).Sum(Weight)))
                .Where(c => c.Score > 0)
                .OrderByDescending(c => c.Score).ThenBy(c => c.Site.Origin.X).ThenBy(c => c.Site.Origin.Y).ThenBy(c => c.Site.Origin.Z).ToList();
            var hung = new List<Point>();
            foreach (var (site, _) in sites)
            {
                if (hung.Count >= work.Count)
                    break;
                if (hung.Any(h => Level(h, site.Origin) < work.Apart))
                    continue;
                if (TryLamp(site))
                    hung.Add(site.Origin);
            }
        }

        //---------------------------------------------------------------------
        // Track on each door's axis, and sidings.
        //---------------------------------------------------------------------

        /// <summary>
        /// Track along each door's axis (the line through the middle of its
        /// opening, square to its wall), in the rooms of the theme's
        /// <see cref="PropSet.RailFamilies"/> only (the tunnels and yards; a
        /// level reads as lines through it, not a cross in every room). Each run
        /// starts at the cell face: its first piece crosses the room's half of
        /// the doorway and is kept by the link only where the door is joined
        /// (<c>room_needs joined_&lt;wall&gt;</c>), so two joined rooms' track
        /// meets on the cell face; where the door is capped a buffer stop
        /// (<c>room_needs !joined_&lt;wall&gt;</c>) ends the track instead. Two
        /// opposite doors are one run face to face; two doors at a corner are
        /// joined by a curve (<see cref="PropSet.RailBend"/>); a third or fourth
        /// door's run is a spur ending in a buffer short of the line it meets;
        /// one door's run ends in a buffer at the room's middle. A run is laid in
        /// whole pieces and its last finished with a short one set back to end
        /// on the run's end (no gap); a run blocked by a stair or a pit ends in a
        /// buffer there. Rooms (not hallways) get a siding beside a run with
        /// <see cref="PropSet.Siding"/> on it.
        /// </summary>
        private void DoorRails(PropModel rail, PropSet props)
        {
            bool stub = props.StubFamilies.Contains(Family(room.Name));
            if (!props.RailFamilies.Contains(Family(room.Name)) && !stub)
                return;
            if (room.Cells > 1)
            {
                HallRails(rail, props);
                return;
            }
            float c = kit.Cell, m = c / 2f;
            // A store or a stope gets a stub from one door only, a cart standing at its buffer.
            var faces = stub ? room.Doors.Take(1).ToList() : room.Doors.ToList();
            var pieces = new[] { rail }.Concat(props.RailEnds).OrderByDescending(Length).ToArray();
            PropModel shortest = pieces[^1];
            float first = Length(shortest);

            (Point From, int Dx, int Dy) Axis(CellFace f) => f switch
            {
                CellFace.West => (new Point(0, m, sill), 1, 0),
                CellFace.East => (new Point(c, m, sill), -1, 0),
                CellFace.South => (new Point(m, 0, sill), 0, 1),
                _ => (new Point(m, c, sill), 0, -1),
            };

            // The doorway piece of each door, kept where joined; a buffer where capped.
            foreach (CellFace f in faces)
            {
                var (from, dx, dy) = Axis(f);
                string wall = RoomBuilder.WallName(f);
                var mid = new Point(from.X + dx * first / 2, from.Y + dy * first / 2, sill - shortest.Mins.Z);
                AddRail(shortest, mid, dx != 0 ? 90 : 0, "joined_" + wall);
                if (props.RailStop is { } stop)
                    AddStop(stop, Along(from, dx, dy, first), dx, dy, "!joined_" + wall);
            }

            bool Opposite(CellFace a, CellFace b) => Axis(a).Dx == -Axis(b).Dx && Axis(a).Dy == -Axis(b).Dy;
            var through = faces.FirstOrDefault(a => faces.Any(b => Opposite(a, b) && a is CellFace.West or CellFace.South), (CellFace)(-1));
            var runs = new List<(Point From, int Dx, int Dy, float Length, bool Stop)>();
            if ((int)through >= 0)
            {
                var (from, dx, dy) = Axis(through);
                // Face to face, less the far door's own doorway piece.
                runs.Add((from, dx, dy, c - first, false));
                // The other doors are spurs to the line, ending in a buffer short of its ties.
                foreach (CellFace f in faces.Where(f => f != through && !Opposite(f, through)))
                {
                    var (sf, sdx, sdy) = Axis(f);
                    runs.Add((sf, sdx, sdy, m - SpurShort, true));
                }
            }
            else if (faces.Count == 2 && props.RailBend is { } bend && TryBend(bend, Axis(faces[0]), Axis(faces[1]), m))
            {
                foreach (CellFace f in faces)
                {
                    var (from, dx, dy) = Axis(f);
                    runs.Add((from, dx, dy, m - BendReach, false));
                }
            }
            else
                foreach (CellFace f in faces)
                {
                    var (from, dx, dy) = Axis(f);
                    runs.Add((from, dx, dy, m - (faces.Count == 1 ? 0 : SpurShort), true));
                }

            foreach (var (from, dx, dy, length, stop) in runs)
            {
                float laid = Lay(pieces, from, dx, dy, first, length);
                if (laid < length - 0.5f)
                {
                    // Blocked: a buffer where it stops, and a run face to face is laid from its far door too.
                    if (props.RailStop is { } s)
                        AddStop(s, Along(from, dx, dy, laid), dx, dy, null);
                    if (Math.Abs(length - (c - first)) < 1)
                    {
                        var far = Along(from, dx, dy, c);
                        float back = Lay(pieces, far, -dx, -dy, first, c - laid - 64);
                        if (props.RailStop is { } s2)
                            AddStop(s2, Along(far, -dx, -dy, back), -dx, -dy, null);
                    }
                }
                else if (stop && props.RailStop is { } s)
                    AddStop(s, Along(from, dx, dy, length), dx, dy, null);
                if (stub && props.Siding is { } car)
                {
                    float at = laid - 96;
                    TryProp(car, Along(from, dx, dy, at) with { Z = sill - car.Mins.Z }, dx != 0 ? 90 : 0, Role.Floor, floorStanding: true);
                }
                // A siding in a room, or in a wide drift's loading bay.
                if (props.Siding is { } cart && (!IsHallway(room) || Family(room.Name) == "widedrift") && laid >= 384)
                    Siding(rail, cart, from, dx, dy, laid);
            }
        }

        /// <summary>How far short of the room's middle a spur's buffer stands: half a track's ties and a hand.</summary>
        public const float SpurShort = 64;

        /// <summary>How far the bend's ends stand from the room's middle along each door's axis (the curve's radius, 228, measured off <c>track_arc_90degree_256</c>).</summary>
        public const float BendReach = 228;

        private static float Length(PropModel piece) => piece.Maxs.Y - piece.Mins.Y;

        private static Point Along(Point from, int dx, int dy, float by) => new(from.X + dx * by, from.Y + dy * by, from.Z);

        /// <summary>
        /// Lays whole pieces from <paramref name="start"/> along the run to
        /// <paramref name="length"/>, the longest first, and finishes the run with
        /// the shortest piece set back to end on its end; returns how far the run
        /// reaches unbroken (less than its length where a piece did not fit).
        /// </summary>
        private float Lay(PropModel[] pieces, Point from, int dx, int dy, float start, float length, string? needs = null)
        {
            float laid = start;
            int yaw = dx != 0 ? 90 : 0;
            foreach (PropModel piece in pieces)
            {
                float l = Length(piece);
                while (laid + l <= length + 0.5f)
                {
                    var mid = Along(from, dx, dy, laid + l / 2) with { Z = sill - piece.Mins.Z };
                    if (!TryRail(piece, mid, yaw, needs))
                        return laid;
                    laid += l;
                }
            }

            if (laid < length - 0.5f)
            {
                PropModel last = pieces[^1];
                float l = Length(last);
                var mid = Along(from, dx, dy, length - l / 2) with { Z = sill - last.Mins.Z };
                if (!TryRail(last, mid, yaw, needs))
                    return laid;
                laid = length;
            }

            return laid;
        }

        /// <summary>
        /// The curve between two doors at a corner: <c>track_arc_90degree_256</c>'s
        /// ends stand at (-100, -128) heading +y and (128, 100) heading +x in its own
        /// frame, so the corner of its two end lines is (-100, 100), set on the room's
        /// middle, each end <see cref="BendReach"/> towards its door. Kept only where
        /// the curve's line lies on open floor clear of every brush and ledge's air.
        /// </summary>
        private bool TryBend(PropModel bend, (Point From, int Dx, int Dy) a, (Point From, int Dx, int Dy) b, float m)
        {
            // Unit vectors from the middle towards each door.
            foreach (var (u, v) in new[] { (a, b), (b, a) })
            {
                int ux = -u.Dx, uy = -u.Dy, vx = -v.Dx, vy = -v.Dy;
                int yaw = Yaw(vx, vy);
                // The model's -y must turn to u.
                Point minusY = Turn(new Point(0, -1, 0), yaw);
                if (Math.Abs(minusY.X - ux) > 0.01f || Math.Abs(minusY.Y - uy) > 0.01f)
                    continue;
                Point corner = Turn(new Point(-100, 100, 0), yaw);
                var origin = new Point(m - corner.X, m - corner.Y, sill - bend.Mins.Z);
                // The curve's centre line: the arc of radius 228 about the model's (128, -128).
                Point centre = Turn(new Point(128, -128, 0), yaw) + new Point(origin.X, origin.Y, 0);
                for (int k = 0; k <= 8; k++)
                {
                    double t = k / 8.0 * Math.PI / 2;
                    // From the end towards u round to the end towards v.
                    Point endU = new(m + ux * BendReach, m + uy * BendReach, 0), endV = new(m + vx * BendReach, m + vy * BendReach, 0);
                    Point r0 = endU - centre with { Z = 0 }, r1 = endV - centre with { Z = 0 };
                    float x = (float)(centre.X + r0.X * Math.Cos(t) + r1.X * Math.Sin(t));
                    float y = (float)(centre.Y + r0.Y * Math.Cos(t) + r1.Y * Math.Sin(t));
                    var probe = new Box(new Point(x - 28, y - 28, sill + 1), new Point(x + 28, y + 28, sill + 6));
                    if (solids.Blocked(probe.Mins, probe.Maxs) || InLedgeAir(probe) || solids.FloorBelow(x, y, sill + 1) is not { } f || Math.Abs(f - sill) > 1)
                        return false;
                }

                AddProp(bend, origin, yaw, Role.Rail, shadows: false);
                var (mins, maxs) = bend.Turned(yaw);
                placed.Add((new Box(origin + mins, origin + maxs), Role.Rail));
                return true;
            }

            return false;
        }

        /// <summary>A rail piece laid without the checks: the doorway piece, which crosses the plug and is kept only where the door is joined.</summary>
        private void AddRail(PropModel piece, Point origin, int yaw, string needs)
        {
            AddProp(piece, origin, yaw, Role.Rail, shadows: false, needs);
            var (mins, maxs) = piece.Turned(yaw);
            placed.Add((new Box(origin + mins, origin + maxs), Role.Rail));
        }

        /// <summary>
        /// A buffer stop at <paramref name="end"/> of a run heading (<paramref name="dx"/>, <paramref name="dy"/>)
        /// from its start: just short of the end, its pins towards the track, standing on the floor
        /// (not solid, and under <see cref="RailStopHeight"/>), kept where <paramref name="needs"/> holds.
        /// </summary>
        private void AddStop(PropModel stop, Point end, int dx, int dy, string? needs)
        {
            float half = (stop.Maxs.Y - stop.Mins.Y) / 2;
            // The stop's +y points away from the track, towards the run's end.
            int yaw = Yaw(dx, dy) - 90;
            var (mins, maxs) = stop.Turned(yaw);
            // Stepped back along the track until it stands clear (a yard's kerb at the run's end).
            for (int back = 0; back <= 256; back += 32)
            {
                var origin = new Point(end.X - dx * (half + back), end.Y - dy * (half + back), sill - stop.Mins.Z);
                var box = new Box(origin + mins, origin + maxs);
                if (needs is null && solids.Blocked(box.Mins + new Point(0, 0, 1), box.Maxs))
                    continue;
                AddProp(stop, origin, yaw, Role.Rail, shadows: false, needs);
                placed.Add((box, Role.Rail));
                return;
            }
        }

        /// <summary>A rail piece: flat and not solid, so it may run up to the doorway and under a marker, which a solid piece may not.</summary>
        private bool TryRail(PropModel piece, Point origin, int yaw, string? needs = null)
        {
            var (mins, maxs) = piece.Turned(yaw);
            var box = new Box(origin + mins, origin + maxs);
            float c = room.Extent;
            if (box.Mins.X < 0 || box.Mins.Y < 0 || box.Maxs.X > c || box.Maxs.Y > c)
                return false;
            if (InLedgeAir(box) || solids.Blocked(box.Mins + new Point(0, 0, 1), box.Maxs))
                return false;
            // On the floor (the doorway's floor is let down a little under its sill planks).
            foreach (float x in (float[])[box.Mins.X + 1, box.Maxs.X - 1])
                foreach (float y in (float[])[box.Mins.Y + 1, box.Maxs.Y - 1])
                    if (solids.FloorBelow(x, y, sill + 1) is not { } f || f > sill + 1 || f < sill - RoomBuilder.DoorwayDip - 1)
                        return false;
            AddProp(piece, origin, yaw, Role.Rail, shadows: false, needs);
            placed.Add((box, Role.Rail));
            return true;
        }

        /// <summary>A siding: a length of track beside a run, off the giant's lane, with a cart on it.</summary>
        private void Siding(PropModel rail, PropModel cart, Point from, int dx, int dy, float laid)
        {
            float l = rail.Maxs.Y - rail.Mins.Y;
            float along = Math.Min(laid, kit.Cell / 2f) - l / 2 - 32;
            foreach (int side in (int[])[1, -1])
                foreach (float off in (float[])[128, 160, 192])
                {
                    // Square to the run, off to one side.
                    float sx = -dy * side * off, sy = dx * side * off;
                    var mid = new Point(from.X + dx * along + sx, from.Y + dy * along + sy, sill - rail.Mins.Z);
                    int yaw = dx != 0 ? 90 : 0;
                    var (mins, maxs) = rail.Turned(yaw);
                    var box = new Box(mid + mins, mid + maxs);
                    if (plan.Covered(box).Any(lane.Contains) || doors.Any(d => d.DistanceTo(box) < DoorClear))
                        continue;
                    if (!TryRail(rail, mid, yaw))
                        continue;
                    TryProp(cart, new Point(mid.X, mid.Y, sill - cart.Mins.Z), yaw, Role.Floor, floorStanding: true);
                    return;
                }
        }

        //---------------------------------------------------------------------
        // Clutter in groups.
        //---------------------------------------------------------------------

        /// <summary>
        /// One or two of the theme's companions set beside a piece just placed
        /// at <paramref name="box"/>: along the wall either side of a wall or
        /// corner piece (<paramref name="dx"/>, <paramref name="dy"/> the wall),
        /// round a floor piece.
        /// </summary>
        private void Group(Box box, int dx, int dy, Role role)
        {
            var companions = theme.Props.Companions;
            if (companions.Count == 0)
                return;
            int want = 1 + rng.Int(0, 2);
            var spots = new List<(float X, float Y, int Yaw)>();
            float gap = 4;
            if (dx != 0 || dy != 0)
            {
                // Beside it along its wall, its back to the wall like the piece's.
                foreach (int side in (int[])[1, -1])
                    spots.Add(dx != 0 ? (0, side, Yaw(-dx, -dy)) : (side, 0, Yaw(-dx, -dy)));
            }
            else
            {
                foreach (var (sx, sy) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                    spots.Add((sx, sy, 90 * rng.Int(0, 4)));
            }

            for (int k = spots.Count - 1; k > 0; k--)
            {
                int r = rng.Int(0, k + 1);
                (spots[k], spots[r]) = (spots[r], spots[k]);
            }

            foreach (var (sx, sy, yaw) in spots)
            {
                if (want == 0)
                    break;
                PropModel m = rng.Pick(companions);
                var (mins, maxs) = m.Turned(yaw);
                float x = sx > 0 ? box.Maxs.X + gap - mins.X : sx < 0 ? box.Mins.X - gap - maxs.X : (box.Mins.X + box.Maxs.X) / 2;
                float y = sy > 0 ? box.Maxs.Y + gap - mins.Y : sy < 0 ? box.Mins.Y - gap - maxs.Y : (box.Mins.Y + box.Maxs.Y) / 2;
                // Against the same wall as the piece.
                if (dx > 0) x = box.Maxs.X - maxs.X;
                if (dx < 0) x = box.Mins.X - mins.X;
                if (dy > 0) y = box.Maxs.Y - maxs.Y;
                if (dy < 0) y = box.Mins.Y - mins.Y;
                if (TryProp(m, new Point((float)Math.Round(x, 2), (float)Math.Round(y, 2), sill - mins.Z), yaw, role, floorStanding: true, group: false))
                    want--;
            }
        }
    }
}
