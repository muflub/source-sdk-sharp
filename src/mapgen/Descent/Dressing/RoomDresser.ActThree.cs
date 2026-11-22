namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// The dressing Act III adds (brief p14): the depth's hazard on the floor a room's route does not
/// need (level 12's and 14's slime, level 13's lava, <see cref="RoomTheme.HazardFloor"/>), each area
/// under a hazard volume of its kind, and the hazard's own glow over every hazard volume the room holds.
/// </summary>
public static partial class RoomDresser
{
    /// <summary>How far, in columns, a hazard floor keeps beyond the giant's lane between the doors.</summary>
    public const int HazardFromLane = 2;

    /// <summary>How far, level, a hazard floor keeps from a marker's point: a player stands there, a pack spawns there.</summary>
    public const int HazardFromMarker = 112;

    /// <summary>The clear air a hazard floor needs over it: the floor under a deck is no place for one.</summary>
    public const int HazardAir = 2 * RoomLint.HullHeight;

    /// <summary>The longest side of one hazard area the dresser lays.</summary>
    public const int HazardMaxSide = 768;

    /// <summary>The volumes a room holds that are always on (the game's hazards; a boss's staged ones start off).</summary>
    public static IEnumerable<(string Kind, Point Mins, Point Maxs)> HazardVolumes(Room room) =>
        room.Entities.Where(e => e.ClassName == "trigger_multiple" && e.Key("StartDisabled") != "1" && e.Brushes.Count == 1
                                 && e.Key("targetname") is { } n && n.StartsWith(TF2.Descent.Rules.World.HazardRules.VolumePrefix, StringComparison.Ordinal))
            .Select(e => (TF2.Descent.Rules.World.HazardRules.KindOfVolume(e.Key("targetname")!)!, e.Brushes[0].Mins, e.Brushes[0].Maxs));

    /// <summary>The steps on the square ring <paramref name="r"/> steps out from a point, in a fixed order (the point itself at 0).</summary>
    private static IEnumerable<(int Dx, int Dy)> Ring(int r)
    {
        if (r == 0)
        {
            yield return (0, 0);
            yield break;
        }

        for (int d = -r; d <= r; d++)
        {
            yield return (d, -r);
            yield return (d, r);
        }

        for (int d = -r + 1; d <= r - 1; d++)
        {
            yield return (-r, d);
            yield return (r, d);
        }
    }

    private sealed partial class Dresser
    {
        /// <summary>
        /// The depth's hazard floor in a room of its families: the largest rectangles of floor that the
        /// giant's lane between the doors (and <see cref="HazardFromLane"/> more), the doors' approaches,
        /// the markers and a blast ledge's take-off leave free, at most <see cref="HazardFloor.MaxAreas"/>
        /// of them, each <see cref="HazardFloor.MinSide"/> across at least; and the floor under every
        /// hazard volume the shapes laid (a pool's basin, a chasm's gap), which no piece then stands on.
        /// </summary>
        private void HazardFloors(HazardFloor hazard)
        {
            foreach (var (_, mins, maxs) in HazardVolumes(room))
                hazardAreas.Add(new Rect(mins.X, mins.Y, maxs.X, maxs.Y));
            if (room.Role != RoomRole.None || room.Entry.Tags.Contains("boss_arena") || !hazard.Families.Contains(Family(room.Name)))
                return;

            int n = plan.Size, grid = RoomLint.Grid;
            var near = new HashSet<(int, int)>();
            foreach (var (i, j) in lane)
                for (int di = -HazardFromLane; di <= HazardFromLane; di++)
                    for (int dj = -HazardFromLane; dj <= HazardFromLane; dj++)
                        near.Add((i + di, j + dj));
            int blast = JumpTable.Of(JumpTier.S3).LedgeMax;
            var takeOff = plan.JumpLedges.Where(s => s.Z > sill + blast).ToArray();
            var floorMarkers = markers.Where(m => m.Z <= sill + RoomLint.Step).ToArray();
            var free = new bool[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (!plan.IsFloor(i, j) || plan.Clear(i, j) < HazardAir || near.Contains((i, j)))
                        continue;
                    float x = i * grid + grid / 2f, y = j * grid + grid / 2f;
                    var cell = new Box(new Point(i * grid, j * grid, sill), new Point(i * grid + grid, j * grid + grid, sill + 1));
                    if (doors.Any(d => d.DistanceTo(cell) < DoorClear + 2 * grid)
                        || floorMarkers.Any(m => MathF.Max(MathF.Abs(m.X - x), MathF.Abs(m.Y - y)) < HazardFromMarker)
                        || takeOff.Any(s => MathF.Max(MathF.Abs(s.I * grid + grid / 2f - x), MathF.Abs(s.J * grid + grid / 2f - y)) < TakeOff)
                        || hazardAreas.Any(a => x > a.X0 - 2 * grid && x < a.X1 + 2 * grid && y > a.Y0 - 2 * grid && y < a.Y1 + 2 * grid))
                        continue;
                    free[i, j] = true;
                }

            int least = hazard.MinSide / grid, most = HazardMaxSide / grid;
            var skins = new List<Rect>();
            var dry = new HashSet<(int, int)>();
            for (int k = 0, tries = 0; k < hazard.MaxAreas && tries < 4 * hazard.MaxAreas; tries++)
            {
                if (LargestFree(free, n, least, most) is not { } r)
                    break;
                var area = new Rect(r.I0 * grid, r.J0 * grid, r.I1 * grid, r.J1 * grid);
                // Keep the next area apart from this one, a player's width of dry floor between.
                for (int i = Math.Max(0, r.I0 - 4); i < Math.Min(n, r.I1 + 4); i++)
                    for (int j = Math.Max(0, r.J0 - 4); j < Math.Min(n, r.J1 + 4); j++)
                        free[i, j] = false;
                // A giant still walks dry from the doors to each other and to every pack on the floor.
                var wet = new HashSet<(int, int)>(dry);
                for (int i = r.I0; i < r.I1; i++)
                    for (int j = r.J0; j < r.J1; j++)
                        wet.Add((i, j));
                if (!KeepsWay(wet))
                    continue;
                dry = wet;
                k++;
                hazardAreas.Add(area);
                if (hazard.Liquid)
                    hazardWater.Add(Brush.Box(new(area.X0, area.Y0, sill), new(area.X1, area.Y1, sill + hazard.Depth), hazard.Surface));
                else
                    skins.Add(area);
                if (hazard.Lips is { } lips)
                    Lips(area, lips);
                added.Add(RoomBuilder.HazardVolume(hazard.Kind, new Point(area.X0, area.Y0, sill),
                    new Point(area.X1, area.Y1, sill + hazard.SurfaceHeight + HazardFloor.VolumeAbove), theme.Trigger));
            }

            if (skins.Count > 0)
                added.Add(RoomBuilder.HazardSkin(hazard, skins, sill));
        }

        /// <summary>The most a lip of rock stands over the floor: under a step, so it is walked over.</summary>
        public const int LipHeight = 16;

        /// <summary>
        /// Low lumps of rock round the edge of a hazard area (<see cref="HazardFloor.Lips"/>): one over each corner and a
        /// row along each side, their middles 16 inside the edge (a corner's 32 in from both), each 48 to 88 across and 6
        /// to <see cref="LipHeight"/> high, so the edge is broken and the corners rounded.
        /// </summary>
        private void Lips(Rect area, Surface rock)
        {
            void Lump(float x, float y)
            {
                int r = rng.Int(24, 45), h = rng.Int(6, LipHeight + 1);
                // A larger room's: no side on an internal cell face (the tools refuse one), so a lump that would end on one moves.
                bool OnCellFace(Brush b) => Enumerable.Range(1, Math.Max(0, room.Cells - 1)).Any(k =>
                    ((float[])[b.Mins.X, b.Maxs.X, b.Mins.Y, b.Maxs.Y]).Any(v => Math.Abs(v - k * kit.Cell) < 0.01f));
                Brush lump = Shapes.Parts.Rock(x, y, r, r, sill, sill + h, rock, rng, sides: 6);
                for (int nudge = 3; OnCellFace(lump) && nudge < 12; nudge += 3)
                    lump = Shapes.Parts.Rock(x + nudge, y + nudge, r, r, sill, sill + h, rock, rng, sides: 6);
                if (!OnCellFace(lump))
                    brushes.Add(lump);
            }

            float inset = 16;
            foreach (var (cx, cy) in ((float, float)[])[(area.X0, area.Y0), (area.X1, area.Y0), (area.X1, area.Y1), (area.X0, area.Y1)])
                Lump(cx + (cx == area.X0 ? 1 : -1) * 2 * inset, cy + (cy == area.Y0 ? 1 : -1) * 2 * inset);
            foreach (var (ax, ay, bx, by, nx, ny) in ((float, float, float, float, float, float)[])
                     [(area.X0, area.Y0, area.X1, area.Y0, 0, 1), (area.X1, area.Y0, area.X1, area.Y1, -1, 0),
                      (area.X1, area.Y1, area.X0, area.Y1, 0, -1), (area.X0, area.Y1, area.X0, area.Y0, 1, 0)])
            {
                float length = MathF.Abs(bx - ax) + MathF.Abs(by - ay);
                for (float t = 64 + rng.Int(0, 32); t < length - 64; t += rng.Int(56, 96))
                    Lump(ax + (bx - ax) * t / length + nx * inset, ay + (by - ay) * t / length + ny * inset);
            }
        }

        /// <summary>
        /// Whether a giant, kept off <paramref name="wet"/>, still walks from the first door to every other
        /// and to within its reach of every pack spawn on the floor.
        /// </summary>
        private bool KeepsWay(HashSet<(int, int)> wet)
        {
            if (plan.RouteBroken(wet))
                return false;
            dryReach ??= plan.GiantReached();
            var reached = plan.GiantReached(wet);
            int reach = FloorPlan.GiantReach + 1;
            bool Near(HashSet<(int I, int J)> set, int mi, int mj)
            {
                for (int di = -reach; di <= reach; di++)
                    for (int dj = -reach; dj <= reach; dj++)
                        if (set.Contains((mi + di, mj + dj)))
                            return true;
                return false;
            }

            foreach (Point m in room.Markers.Where(m => m.Kind == MarkerKind.PackSpawn && m.At.Z <= sill + RoomLint.Step).Select(m => m.At))
            {
                var (mi, mj) = FloorPlan.ColumnOf(m.X, m.Y);
                if (Near(dryReach, mi, mj) && !Near(reached, mi, mj))
                    return false;
            }

            return true;
        }

        /// <summary>The columns a giant reaches from the doors with no hazard laid.</summary>
        private HashSet<(int I, int J)>? dryReach;

        /// <summary>
        /// The largest rectangle of free columns, each side from <paramref name="least"/> to
        /// <paramref name="most"/> columns (the bigger side cut back to it), or null: the first of the
        /// largest in column order, so the same room gives the same areas.
        /// </summary>
        private static (int I0, int J0, int I1, int J1)? LargestFree(bool[,] free, int n, int least, int most)
        {
            (int, int, int, int)? best = null;
            int bestArea = 0;
            var height = new int[n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    height[j] = free[i, j] ? height[j] + 1 : 0;
                // Every run of columns j0..j1 with the least height over it: a rectangle ending at row i.
                for (int j0 = 0; j0 < n; j0++)
                {
                    int h = int.MaxValue;
                    for (int j1 = j0; j1 < n && j1 - j0 < most; j1++)
                    {
                        h = Math.Min(h, height[j1]);
                        if (h < least)
                            break;
                        int w = j1 - j0 + 1, hh = Math.Min(h, most);
                        if (w < least || w * hh <= bestArea)
                            continue;
                        bestArea = w * hh;
                        best = (i - hh + 1, j0, i + 1, j1 + 1);
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// The hazard's own light (<see cref="HazardFloor.Glow"/>) over each hazard area of the room, one on a
        /// grid of <see cref="HazardFloor.GlowPitch"/> over it (one at its middle at least), a little over its
        /// surface: a light with no fixture, as a cool fill is (<see cref="Role.Fill"/>).
        /// </summary>
        private void HazardGlows(HazardFloor hazard)
        {
            foreach (Rect a in hazardAreas)
            {
                float z = sill + hazard.SurfaceHeight + 32;
                int nx = Math.Max(1, (int)MathF.Round(a.Width / hazard.GlowPitch)), ny = Math.Max(1, (int)MathF.Round(a.Depth / hazard.GlowPitch));
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        var want = new Point(MathF.Round(a.X0 + a.Width * (i + 0.5f) / nx), MathF.Round(a.Y0 + a.Depth * (j + 0.5f) / ny), z);
                        // Beside what stands in the hazard there (a pool's stepping pillar), the nearest open point over it.
                        Point? at = null;
                        for (int r = 0; r <= 4 && at is null; r++)
                            foreach (var (dx, dy) in Ring(r))
                            {
                                var p = want with { X = want.X + 32 * dx, Y = want.Y + 32 * dy };
                                if (p.X > a.X0 && p.X < a.X1 && p.Y > a.Y0 && p.Y < a.Y1 && !solids.Blocked(p - new Point(1, 1, 1), p + new Point(1, 1, 1)))
                                {
                                    at = p;
                                    break;
                                }
                            }

                        if (at is { } light)
                            added.Add(new RoomEntity("light", light, [.. hazard.Glow.Keys, new(RoleKey, nameof(Role.Fill))], [], []));
                    }
            }
        }
    }
}
