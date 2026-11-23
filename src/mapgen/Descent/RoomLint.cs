namespace SourceSharp.MapGen.Descent;

/// <summary>One lint finding: the rule, the room, and what is wrong.</summary>
public sealed record LintFinding(string Rule, string Room, string Message)
{
    public override string ToString() => $"{Rule}: room '{Room}': {Message}";
}

/// <summary>
/// The room lint (plan task B1, plan_tf2_rpg.md 6a): the rules a room keeps
/// so any legal assembly links into a level that plays, each measured from
/// the room's brushes and markers, never from what the room says of itself.
/// Every rule has a refusing fixture in <c>DescentRoomLintTests</c>.
/// </summary>
/// <remarks>
/// The geometry is read on a grid of 16-unit columns (the navigation
/// voxel): in each column the solid brushes (the world but its door plugs,
/// and the <c>func_brush</c> cap panels) give the surfaces a player can stand
/// on, each with the clear air above it. Displacements count as their base
/// brush.
/// </remarks>
public static class RoomLint
{
    public const string Sealed = "sealed";
    public const string DoorsOnKit = "doors_on_kit";
    public const string MarkerFloor = "marker_floor";
    public const string JumpClearAir = "jump_clear_air";
    public const string LedgePays = "ledge_pays";
    public const string InsideCell = "inside_cell";
    public const string FrameClear = "frame_clear";
    public const string DoorPath = "door_path";
    public const string MarkerCount = "marker_count";
    public const string Lit = "lit";
    public const string HazardPath = "hazard_path";

    /// <summary>A giant robot (R16): the main route between doors is clear for it.</summary>
    public const int GiantWidth = RoomKit.GiantWidth, GiantHeight = RoomKit.GiantHeight;

    /// <summary>How far from a door's opening a pack spawn stands at least.</summary>
    public const int PackFromDoor = 2 * RoomBuilder.DoorApproach;

    /// <summary>TF2's standing hull: 48 square, 82 high (TFViewVectors).</summary>
    public const int HullWidth = 48, HullHeight = 82;

    /// <summary>A step: what a player walks up without jumping.</summary>
    public const int Step = 18;

    /// <summary>The sampling grid.</summary>
    public const int Grid = 16;

    /// <summary>Every finding for the room, rule by rule.</summary>
    public static IReadOnlyList<LintFinding> Check(Room room)
    {
        // A larger room is read over its whole footprint and every socket it has (M24).
        var columns = new RoomColumns(room);
        return [.. CheckSealed(room), .. CheckDoorsOnKit(room), .. CheckMarkerFloor(room, columns), .. CheckJumpClearAir(room, columns),
                .. CheckLedgePays(room, columns), .. CheckInsideCell(room), .. CheckFrameClear(room), .. CheckDoorPath(room, columns),
                .. CheckMarkerCount(room, columns), .. CheckLit(room, columns), .. CheckHazardPath(room, columns)];
    }

    //-------------------------------------------------------------------------
    // sealed: a flood from inside every door never reaches the cell's edge.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckSealed(Room room)
    {
        RoomKit kit = room.Kit;
        int n = room.Extent / Grid, nz = room.Height / Grid;
        var solid = new bool[n, n, nz];
        foreach (Brush b in room.World)
        {
            int x0 = Lo(b.Mins.X), x1 = Hi(b.Maxs.X, n), y0 = Lo(b.Mins.Y), y1 = Hi(b.Maxs.Y, n), z0 = Lo(b.Mins.Z), z1 = Hi(b.Maxs.Z, nz);
            for (int x = x0; x < x1; x++)
                for (int y = y0; y < y1; y++)
                    for (int z = z0; z < z1; z++)
                        solid[x, y, z] |= b.Contains(Centre(x, y, z));
        }

        var seen = new bool[n, n, nz];
        var queue = new Queue<(int X, int Y, int Z)>();
        foreach (var (face, offset) in room.Sockets)
        {
            Point inside = room.DoorInside(face, offset, 48) with { Z = kit.Sill + 40 };
            var (x, y, z) = ((int)(inside.X / Grid), (int)(inside.Y / Grid), (int)(inside.Z / Grid));
            if (!solid[x, y, z] && !seen[x, y, z])
            {
                seen[x, y, z] = true;
                queue.Enqueue((x, y, z));
            }
        }

        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            if (x == 0 || y == 0 || z == 0 || x == n - 1 || y == n - 1 || z == nz - 1)
            {
                yield return new LintFinding(Sealed, room.Name,
                    $"the room leaks: open air from its doors reaches the cell's edge at ({Centre(x, y, z)})");
                yield break;
            }

            foreach (var (dx, dy, dz) in Neighbours)
            {
                int a = x + dx, b = y + dy, c = z + dz;
                if (!seen[a, b, c] && !solid[a, b, c])
                {
                    seen[a, b, c] = true;
                    queue.Enqueue((a, b, c));
                }
            }
        }
    }

    //-------------------------------------------------------------------------
    // doors on the kit: each door's plug is exactly the kit's opening.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckDoorsOnKit(Room room)
    {
        RoomKit kit = room.Kit;
        var plugMaterials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (face, offset) in room.Sockets)
        {
            var (mins, maxs) = room.PlugBox(face, offset);
            var plugs = room.World.Where(b => b.IsBox && b.Mins == mins && b.Maxs == maxs).ToArray();
            if (plugs.Length != 1)
            {
                yield return new LintFinding(DoorsOnKit, room.Name,
                    $"its {room.SocketName(face, offset)} door has {plugs.Length} plugs filling the kit's opening ({mins})-({maxs}); a socket has one");
                continue;
            }

            plugMaterials.Add(plugs[0].Material);
        }

        var openings = room.Sockets.Select(s => room.PlugBox(s.Face, s.Offset)).ToHashSet();
        foreach (Brush b in room.World.Where(b => plugMaterials.Contains(b.Material) && !openings.Contains((b.Mins, b.Maxs))))
            yield return new LintFinding(DoorsOnKit, room.Name,
                $"a {b.Material} brush ({b.Mins})-({b.Maxs}) is a plug off the kit's openings");
    }

    //-------------------------------------------------------------------------
    // markers on walkable floor, with a standing player's room above.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckMarkerFloor(Room room, RoomColumns columns)
    {
        // A breakable's point (M34) is where a crate stands, not where a player does: the dress lint
        // holds it to the room compile's own rule (DressLint.Breakable).
        foreach (RoomEntity e in room.Entities.Where(e => e.ClassName == "info_poi" && e.Origin is not null && !Breakables.Is(e)))
        {
            Point at = e.Origin!.Value;
            string what = e.Key("poi_type") ?? "info_poi";
            var under = columns.SurfaceUnder(at.X, at.Y, at.Z);
            if (under is not { } s || at.Z - s.Z > Step)
            {
                yield return new LintFinding(MarkerFloor, room.Name, $"{what} at ({at}) stands on no floor within a step below it");
                continue;
            }

            if ((at.Z - room.Kit.Sill) % room.Kit.NavVoxel != 0)
            {
                yield return new LintFinding(MarkerFloor, room.Name,
                    $"{what} at ({at}) stands {at.Z - room.Kit.Sill} above the floor, not a whole number of the kit's {room.Kit.NavVoxel}-unit navigation voxels: the tools stand it in a voxel its surface cuts");
                continue;
            }

            if (s.Clear < HullHeight || s.NormalZ < 0.7f)
            {
                yield return new LintFinding(MarkerFloor, room.Name,
                    $"{what} at ({at}) stands on a surface with {s.Clear} of headroom and a {s.NormalZ:0.00} slope normal; a player needs {HullHeight} on walkable floor");
                continue;
            }

            // A standing player's hull on the surface: floor within a step under each of its corners, and no brush
            // in the box itself (the tools stand a marker in the voxel its height falls in, and refuse one where a
            // rail or a kerb a step high, however narrow, stands in the hull).
            float h = HullWidth / 2f;
            bool fits = true;
            foreach (var (dx, dy) in ((float, float)[])[(-h, -h), (h, -h), (-h, h), (h, h)])
                if (fits && (columns.SurfaceUnder(at.X + dx, at.Y + dy, s.Z + Step) is not { } c || s.Z - c.Z > Step || c.Clear < HullHeight))
                {
                    yield return new LintFinding(MarkerFloor, room.Name,
                        $"{what} at ({at}): a standing player's hull there has no floor or no headroom at ({at.X + dx} {at.Y + dy})");
                    fits = false;
                }

            Point lo = new(at.X - h, at.Y - h, s.Z + Brush.Epsilon), hi = new(at.X + h, at.Y + h, s.Z + HullHeight);
            if (fits && columns.Solids.FirstOrDefault(b => b.Overlaps(lo, hi)) is { } blocking)
                yield return new LintFinding(MarkerFloor, room.Name,
                    $"{what} at ({at}): a brush ({blocking.Mins})-({blocking.Maxs}) stands in a standing player's hull there");
        }
    }

    //-------------------------------------------------------------------------
    // jump rooms: 576 of clear air over half the floor and a straight 768 run.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether the jump-space rules (plan §4: a jump room's clear air) pass over a room by its role: a tunnel, a
    /// corridor or a junction, whose low roof is the point of it. Level 5's maze cells are tunnels so: their solid
    /// roof at 384 is what stops a jump over the maze's walls (brief p18).
    /// </summary>
    public static bool JumpSpaceExempt(Room room) => room.Entry.Tags.Contains("corridor") || room.Entry.Tags.Contains("junction");

    public static IEnumerable<LintFinding> CheckJumpClearAir(Room room, RoomColumns columns)
    {
        if (!room.Entry.Jump || JumpSpaceExempt(room))
            yield break;
        var reached = columns.ReachableAtT1(room.Sockets.Select(s => room.DoorInside(s.Face, s.Offset, 48)));
        var (clear, floor, run) = columns.ClearAir(JumpTable.RocketClearAir, reached);
        if (clear * 2 < floor)
            yield return new LintFinding(JumpClearAir, room.Name,
                $"it is a jump room with {JumpTable.RocketClearAir} of clear air over {clear} of its {floor} floor columns; it needs half");
        if (run < JumpTable.RocketRun)
            yield return new LintFinding(JumpClearAir, room.Name,
                $"it is a jump room whose longest straight run under {JumpTable.RocketClearAir} of clear air is {run}; it needs {JumpTable.RocketRun}");
    }

    //-------------------------------------------------------------------------
    // every ledge above T1 pays: a reward on it, or a T1 way up to it.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckLedgePays(Room room, RoomColumns columns)
    {
        RoomKit kit = room.Kit;
        var starts = room.Sockets.Select(s => room.DoorInside(s.Face, s.Offset, 48)).ToArray();
        var reached = columns.ReachableAtT1(starts);
        int reach = JumpTable.CriticalPathReach;
        float floor = kit.Sill;

        // The ledges: surfaces higher than T1 above the floor that no T1 route reaches,
        // grouped into the stretches a player can walk or crouch-jump about on.
        // A group with nowhere a player's hull fits (a frame's reveal, a wall's top) is no ledge.
        var ledges = columns.Surfaces().Where(s => s.Z - floor > reach && !reached.Contains(s.Key)).ToList();
        foreach (var group in columns.Group(ledges, reach, JumpTable.Of(JumpTier.T1).GapMax))
        {
            if (!group.Any(columns.HullFits))
                continue;
            bool paid = room.Markers.Any(m => Markers.IsReward(m.Kind)
                                              && group.Any(s => Math.Abs(s.X - m.At.X) <= Grid / 2f && Math.Abs(s.Y - m.At.Y) <= Grid / 2f
                                                                && m.At.Z - s.Z is >= 0 and <= Step));
            if (!paid)
            {
                var top = group.MaxBy(s => s.Z);
                yield return new LintFinding(LedgePays, room.Name,
                    $"a ledge of {group.Count} columns at {top.Z - floor} above the floor (near {top.X} {top.Y}) has no reward and no way up but a jump: every jump spot pays");
            }
        }
    }

    //-------------------------------------------------------------------------
    // nothing outside the cell.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckInsideCell(Room room)
    {
        float c = room.Extent, h = room.Height;
        bool Inside(Point p) => p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X <= c && p.Y <= c && p.Z <= h;
        foreach (Brush b in room.World.Concat(room.Entities.SelectMany(e => e.Brushes)))
            if (!Inside(b.Mins) || !Inside(b.Maxs))
                yield return new LintFinding(InsideCell, room.Name, $"a brush ({b.Mins})-({b.Maxs}) leaves the cell [0, {c}]² by [0, {h}]");
        foreach (RoomEntity e in room.Entities.Where(e => e.Origin is { } o && !Inside(o)))
            yield return new LintFinding(InsideCell, room.Name, $"{e.ClassName} at ({e.Origin}) is outside the cell");
    }

    //-------------------------------------------------------------------------
    // every door framed by the kit's frame model, non-solid, leaving a giant's 92 by 156.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckFrameClear(Room room)
    {
        RoomKit kit = room.Kit;
        if (room.Sockets.Any() && (kit.ClearWidth < RoomKit.GiantWidth || kit.ClearHeight < RoomKit.GiantHeight))
            yield return new LintFinding(FrameClear, room.Name,
                $"the {kit.Name} kit's frame ({kit.Frame.Model}) leaves {kit.ClearWidth} by {kit.ClearHeight}, less than a giant's {RoomKit.GiantWidth} by {RoomKit.GiantHeight}");
        foreach (var (face, offset) in room.Sockets)
        {
            string wall = room.SocketName(face, offset);
            var pieces = room.Entities.Where(e => e.ClassName == "prop_static" && e.Key("room_needs") == "joined_" + wall).ToArray();
            // A slab over the doorway's dip (a model frame's floor, RoomBuilder.SillSlabPiece) is no frame: nothing of it stands above a step.
            var brushFrames = room.Entities.Where(e => e.ClassName == "func_brush" && e.Key("room_needs") == "joined_" + wall
                                                       && e.Brushes.Any(b => b.Maxs.Z > kit.Sill + Step)).ToArray();
            int frames = pieces.Count(e => e.Key("model") == kit.Frame.Model) + brushFrames.Length;
            int sills = kit.Frame.Sill is { } sill ? pieces.Count(e => e.Key("model") == sill.Model) : 0;
            int need = RoomBuilder.SillInsets(kit).Count;
            if (frames != 1 || sills != need)
            {
                yield return new LintFinding(FrameClear, room.Name,
                    $"its {wall} door has {frames} frame(s) and {sills} sill plank(s) kept where it is joined, not one frame and {need} plank(s); the joined doorway would draw as a gap");
                continue;
            }

            if (pieces.Any(e => e.Key("solid") != "0") || brushFrames.Any(e => e.Key("Solidity") != "1"))
            {
                yield return new LintFinding(FrameClear, room.Name, $"its {wall} door's frame is solid; only the brushes may make the passage");
                continue;
            }

            // A brush frame leaves a giant's 92 by 156 through the opening (planks a step high are walked over).
            var (pmins, pmaxs) = room.PlugBox(face, offset);
            bool alongY = face is CellFace.East or CellFace.West;
            float mid = alongY ? (pmins.Y + pmaxs.Y) / 2 : (pmins.X + pmaxs.X) / 2, g = RoomKit.GiantWidth / 2f;
            Point gmin = alongY ? new(pmins.X - 64, mid - g, kit.Sill + Step) : new(mid - g, pmins.Y - 64, kit.Sill + Step);
            Point gmax = alongY ? new(pmaxs.X + 64, mid + g, kit.Sill + RoomKit.GiantHeight) : new(mid + g, pmaxs.Y + 64, kit.Sill + RoomKit.GiantHeight);
            if (brushFrames.SelectMany(e => e.Brushes).FirstOrDefault(b => b.Overlaps(gmin, gmax)) is { } narrow)
                yield return new LintFinding(FrameClear, room.Name,
                    $"its {wall} door's frame brush ({narrow.Mins})-({narrow.Maxs}) stands in a giant's {RoomKit.GiantWidth} by {RoomKit.GiantHeight} way through");
        }
    }

    //-------------------------------------------------------------------------
    // the door path (M8): a giant walks from every door to every other on steps of 18, never jumping.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckDoorPath(Room room, RoomColumns columns)
    {
        // A pack's robots come in at its spawn and walk out to the doors, giants among them: a giant
        // walks from a door to every pack spawn on the floor, in a room of one door too (a vault whose
        // mouth its corners close is floor no giant, and barely a Heavy, reaches).
        var sockets = room.Sockets.ToArray();
        if (sockets.Length > 0)
        {
            var fromDoor = columns.ReachableWalking([room.DoorInside(sockets[0].Face, sockets[0].Offset, 48)], GiantWidth, GiantHeight);
            // Within a giant's half-width and a column of the spawn: a pack spawns over a radius, and a
            // spawn by a wall or a pillar is closer to it than a giant's middle can stand.
            const float near = GiantWidth / 2f + Grid;
            bool Reached(Point at) =>
                Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-3, 7).Select(dy => (dx, dy)))
                    .Any(d => d.dx * d.dx + d.dy * d.dy <= 9 && columns.Reaches(fromDoor, at with { X = at.X + d.dx * near / 3, Y = at.Y + d.dy * near / 3 }));
            // A spawn with no floor under it is marker_floor's to refuse, not this rule's.
            foreach (var (kind, at) in room.Markers.Where(m => m.Kind == MarkerKind.PackSpawn && m.At.Z - room.Kit.Sill <= Step
                                                             && columns.SurfaceUnder(m.At.X, m.At.Y, m.At.Z + Step) is { } s && Math.Abs(s.Z - m.At.Z) <= Step))
                if (!Reached(at))
                    yield return new LintFinding(DoorPath, room.Name,
                        $"a giant ({GiantWidth} by {GiantHeight}) walking from its {room.SocketName(sockets[0].Face, sockets[0].Offset)} door does not reach the pack spawn at ({at})");
        }

        if (sockets.Length < 2)
            yield break;
        // Every pair: a walk may drop off a ledge it cannot climb back, so reach is not symmetric.
        foreach (var (from, fromOffset) in sockets)
        {
            var reached = columns.ReachableWalking([room.DoorInside(from, fromOffset, 48)], GiantWidth, GiantHeight);
            foreach (var (to, toOffset) in sockets.Where(s => s != (from, fromOffset)))
                if (!columns.Reaches(reached, room.DoorInside(to, toOffset, 48)))
                    yield return new LintFinding(DoorPath, room.Name,
                        $"a giant ({GiantWidth} by {GiantHeight}) walking on steps of {Step} from its {room.SocketName(from, fromOffset)} door does not reach its {room.SocketName(to, toOffset)} door");
        }
    }

    //-------------------------------------------------------------------------
    // the hazard path (brief p14): no hazard floor is the only way between two doors.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A giant (92 by 156) walks from every door to every other without its box entering an always-on hazard
    /// volume (slime, lava: <see cref="Dressing.RoomDresser.HazardVolumes"/>) at its feet: a hazard floor is
    /// never the only way through a room (brief p14), for the robots (lava burns them) or the heroes.
    /// </summary>
    public static IEnumerable<LintFinding> CheckHazardPath(Room room, RoomColumns columns)
    {
        var volumes = Dressing.RoomDresser.HazardVolumes(room).ToArray();
        var sockets = room.Sockets.ToArray();
        if (volumes.Length == 0 || sockets.Length < 2)
            yield break;
        bool Wet(int i, int j, float z)
        {
            float x = i * Grid + Grid / 2f, y = j * Grid + Grid / 2f, r = GiantWidth / 2f;
            return volumes.Any(v => x + r > v.Mins.X && x - r < v.Maxs.X && y + r > v.Mins.Y && y - r < v.Maxs.Y && z >= v.Mins.Z - Brush.Epsilon && z < v.Maxs.Z);
        }

        foreach (var (from, fromOffset) in sockets)
        {
            var reached = columns.ReachableWalking([room.DoorInside(from, fromOffset, 48)], GiantWidth, GiantHeight, Wet);
            foreach (var (to, toOffset) in sockets.Where(s => s != (from, fromOffset)))
                if (!columns.Reaches(reached, room.DoorInside(to, toOffset, 48)))
                    yield return new LintFinding(HazardPath, room.Name,
                        $"a giant ({GiantWidth} by {GiantHeight}) walks from its {room.SocketName(from, fromOffset)} door to its {room.SocketName(to, toOffset)} door only through a hazard volume");
        }
    }

    //-------------------------------------------------------------------------
    // markers: a combat room has 2 to 4 packs away from its doors and 1 or 2 pieces of furniture on its floor; a corridor at most one pack.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckMarkerCount(Room room, RoomColumns columns)
    {
        var markers = room.Markers.ToArray();
        var packs = markers.Where(m => m.Kind == MarkerKind.PackSpawn).ToArray();
        if (room.Entry.Quiet)
        {
            // A quiet room stands beside the way in (brief p9): no pack at all.
            if (packs.Length > 0)
                yield return new LintFinding(MarkerCount, room.Name, $"a quiet room has {packs.Length} pack spawn(s); it has none");
            yield break;
        }

        if (room.Entry.Tags.Contains("corridor") || room.Entry.Tags.Contains("junction"))
        {
            if (packs.Length > 1)
                yield return new LintFinding(MarkerCount, room.Name, $"a corridor or junction has {packs.Length} pack spawns; it has one at most");
            yield break;
        }

        if (room.Role != RoomRole.None)
            yield break;
        if (packs.Length is < 2 or > 4)
            yield return new LintFinding(MarkerCount, room.Name, $"a combat room has {packs.Length} pack spawns; it has 2 to 4");
        foreach (var (_, at) in packs)
            foreach (var (face, offset) in room.Sockets)
            {
                Point door = room.DoorInside(face, offset, 0);
                if (Math.Sqrt((at.X - door.X) * (at.X - door.X) + (at.Y - door.Y) * (at.Y - door.Y)) < PackFromDoor)
                    yield return new LintFinding(MarkerCount, room.Name, $"a pack spawn at ({at}) is within {PackFromDoor} of its {room.SocketName(face, offset)} door");
            }

        int furniture = markers.Count(m => m.Kind is MarkerKind.Shrine or MarkerKind.Container or MarkerKind.Dispenser or MarkerKind.Barrel or MarkerKind.Trap
                                           && m.At.Z - room.Kit.Sill <= JumpTable.CriticalPathReach);
        if (furniture is < 1 or > 2)
            yield return new LintFinding(MarkerCount, room.Name, $"a combat room has {furniture} furniture markers on its floor; it has 1 or 2");
    }

    //-------------------------------------------------------------------------
    // lit (§6, "the path is lit"): every floor a player walks to is within a lamp's reach.
    //-------------------------------------------------------------------------

    /// <summary>A lamp's reach: halfway from where it falls to half to where it is gone.</summary>
    public static float Reach(RoomEntity light)
    {
        float Key(string k) => float.Parse(light.Key(k) ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        return (Key("_fifty_percent_distance") + Key("_zero_percent_distance")) / 2;
    }

    /// <summary>
    /// What a <c>light</c> or <c>light_spot</c> reaches: its point, its
    /// <see cref="Reach"/>, and for a spot the axis it points along (its
    /// <c>pitch</c>, negative down as the dresser writes it, and its yaw) and
    /// the cosine of its outer cone (<c>_cone</c> degrees either side).
    /// </summary>
    public readonly record struct LightCone(Point At, float Reach, double Ax, double Ay, double Az, double CosCone, bool IsSpot)
    {
        /// <summary>The cone of an entity, or null when it is not a light.</summary>
        public static LightCone? Of(RoomEntity light)
        {
            if (light.Origin is not { } at || light.ClassName is not ("light" or "light_spot"))
                return null;
            float reach = RoomLint.Reach(light);
            if (light.ClassName == "light")
                return new LightCone(at, reach, 0, 0, 0, -1, false);
            float Key(string k, float none) => light.Key(k) is { } v ? float.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : none;
            string[] angles = (light.Key("angles") ?? "0 0 0").Split(' ');
            double down = -Key("pitch", float.Parse(angles[0], System.Globalization.CultureInfo.InvariantCulture)) * Math.PI / 180;
            double yaw = (angles.Length > 1 ? float.Parse(angles[1], System.Globalization.CultureInfo.InvariantCulture) : 0) * Math.PI / 180;
            return new LightCone(at, reach, Math.Cos(down) * Math.Cos(yaw), Math.Cos(down) * Math.Sin(yaw), -Math.Sin(down),
                Math.Cos(Key("_cone", 45) * Math.PI / 180) - 1e-6, true);
        }

        /// <summary>Whether the light reaches <paramref name="p"/>.</summary>
        public bool Reaches(Point p)
        {
            float dx = p.X - At.X, dy = p.Y - At.Y, dz = p.Z - At.Z;
            float d2 = dx * dx + dy * dy + dz * dz;
            if (d2 > Reach * Reach)
                return false;
            return !IsSpot || d2 < 1 || (Ax * dx + Ay * dy + Az * dz) / Math.Sqrt(d2) >= CosCone;
        }
    }

    /// <summary>
    /// Whether <paramref name="light"/> reaches <paramref name="p"/>: a
    /// <c>light</c> within its <see cref="Reach"/>; a <c>light_spot</c> within
    /// its reach and inside its outer cone (<see cref="LightCone"/>).
    /// </summary>
    public static bool Lights(RoomEntity light, Point p) => LightCone.Of(light) is { } cone && cone.Reaches(p);

    /// <summary>The floor the lit rule reads: every surface a standing player walks to from one of the room's doors.</summary>
    public static IReadOnlyList<RoomColumns.Surface> WalkedFloor(Room room, RoomColumns columns)
    {
        var reached = columns.ReachableWalking(room.Sockets.Select(s => room.DoorInside(s.Face, s.Offset, 48)), RoomLint.HullWidth, RoomLint.HullHeight);
        return [.. columns.Surfaces().Where(s => reached.Contains(s.Key))];
    }

    /// <summary>The surfaces of <paramref name="floor"/> no light of <paramref name="room"/> reaches.</summary>
    public static IReadOnlyList<RoomColumns.Surface> Dark(Room room, IEnumerable<RoomColumns.Surface> floor)
    {
        var lights = room.Entities.Select(LightCone.Of).OfType<LightCone>().ToArray();
        return [.. floor.Where(s => !lights.Any(l => l.Reaches(new Point(s.X, s.Y, s.Z))))];
    }

    public static IEnumerable<LintFinding> CheckLit(Room room, RoomColumns columns)
    {
        var dark = Dark(room, WalkedFloor(room, columns));
        if (dark.Count > 0)
            yield return new LintFinding(Lit, room.Name,
                $"{dark.Count} columns of its walkable floor are beyond every lamp's reach (one at {dark[0].X} {dark[0].Y} {dark[0].Z})");
    }

    //-------------------------------------------------------------------------

    /// <summary>A point <paramref name="depth"/> inside the room from a door's wall, at the door's middle, on the floor.</summary>
    public static Point DoorInside(RoomKit kit, CellFace face, float depth)
    {
        float c = kit.Cell, m = c / 2f, d = kit.WallDepth + depth;
        return face switch
        {
            CellFace.East => new(c - d, m, kit.Sill),
            CellFace.West => new(d, m, kit.Sill),
            CellFace.North => new(m, c - d, kit.Sill),
            _ => new(m, d, kit.Sill),
        };
    }

    private static readonly (int, int, int)[] Neighbours = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    private static Point Centre(int x, int y, int z) => new(x * Grid + Grid / 2f, y * Grid + Grid / 2f, z * Grid + Grid / 2f);

    private static int Lo(float v) => Math.Max(0, (int)Math.Floor(v / Grid));

    private static int Hi(float v, int n) => Math.Min(n, (int)Math.Ceiling(v / Grid));
}

/// <summary>
/// A room's floor plan in 16-unit columns: in each column, the surfaces a
/// standing player fits on (the top of a run of solid brushes with at least
/// the hull's height clear above it), each with its clear air.
/// </summary>
public sealed class RoomColumns
{
    /// <summary>One surface: its column, height, the clear air above it and its face's upward normal.</summary>
    public readonly record struct Surface(int I, int J, int K, float X, float Y, float Z, float Clear, float NormalZ)
    {
        public (int, int, int) Key => (I, J, K);
    }

    /// <summary>
    /// Where in its 16-unit square a column is probed: just off the middle,
    /// at a half unit, so a brush face on a whole unit (every face here is)
    /// never lies on the probe and the column always falls to one side.
    /// </summary>
    public const float Probe = 8.5f;

    private readonly Room room;
    private readonly int n;

    /// <summary>The brushes a player stands on and is stopped by: the world but its door plugs, and the cap panels.</summary>
    public IReadOnlyList<Brush> Solids { get; }
    private readonly Surface[][] columns;

    public RoomColumns(Room room)
    {
        this.room = room;
        RoomKit kit = room.Kit;
        n = room.Extent / RoomLint.Grid;
        var plugs = room.Sockets.Select(s => room.PlugBox(s.Face, s.Offset)).ToHashSet();
        var solids = room.World.Where(b => !(b.IsBox && plugs.Contains((b.Mins, b.Maxs))))
            .Concat(room.Entities.Where(e => e.ClassName == "func_brush" && e.Key("Solidity") != "1").SelectMany(e => e.Brushes)).ToArray();
        Solids = solids;
        columns = new Surface[n * n][];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                columns[i * n + j] = Column(solids, i, j);
    }

    private Surface[] Column(Brush[] solids, int i, int j)
    {
        float x = i * RoomLint.Grid + Probe, y = j * RoomLint.Grid + Probe;
        var spans = new List<(float Low, float High, Brush Brush)>();
        foreach (Brush b in solids)
            if (b.Mins.X < x && b.Maxs.X > x && b.Mins.Y < y && b.Maxs.Y > y && b.Column(x, y) is { } c)
                spans.Add((c.Low, c.High, b));
        spans.Sort((a, b) => a.Low.CompareTo(b.Low));

        var merged = new List<(float Low, float High, Brush Top)>();
        foreach (var s in spans)
            if (merged.Count > 0 && s.Low <= merged[^1].High + Brush.Epsilon)
            {
                if (s.High > merged[^1].High)
                    merged[^1] = (merged[^1].Low, s.High, s.Brush);
            }
            else
                merged.Add(s);

        var surfaces = new List<Surface>();
        for (int k = 0; k < merged.Count; k++)
        {
            float top = merged[k].High, ceiling = k + 1 < merged.Count ? merged[k + 1].Low : room.Height;
            float clear = ceiling - top;
            if (clear < RoomLint.HullHeight)
                continue;
            float nz = merged[k].Top.Faces.Where(f => f.Normal.Z > 0)
                .Select(f => (f.Normal.Z, Z: (f.Distance - f.Normal.X * x - f.Normal.Y * y) / f.Normal.Z))
                .OrderBy(f => f.Z).Select(f => f.Item1).DefaultIfEmpty(1).First();
            surfaces.Add(new Surface(i, j, surfaces.Count, x, y, top, clear, nz));
        }

        return [.. surfaces];
    }

    private Surface[] At(int i, int j) => i < 0 || j < 0 || i >= n || j >= n ? [] : columns[i * n + j];

    /// <summary>The highest surface at or below <paramref name="z"/> in the column holding (x, y), or null.</summary>
    public Surface? SurfaceUnder(float x, float y, float z)
    {
        var col = At((int)Math.Floor(x / RoomLint.Grid), (int)Math.Floor(y / RoomLint.Grid));
        Surface? best = null;
        foreach (Surface s in col)
            if (s.Z <= z + Brush.Epsilon && (best is null || s.Z > best.Value.Z))
                best = s;
        return best;
    }

    /// <summary>
    /// Whether a standing player's hull fits on the surface: the eight
    /// columns round it (48 units across with it) hold a surface within a
    /// step of it. A frame's 8-unit reveal or a wall's top is not a ledge.
    /// </summary>
    public bool HullFits(Surface s)
    {
        for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
                if (!At(s.I + di, s.J + dj).Any(t => Math.Abs(t.Z - s.Z) <= RoomLint.Step))
                    return false;
        return true;
    }

    /// <summary>Every surface of the room.</summary>
    public IEnumerable<Surface> Surfaces() => columns.SelectMany(c => c);

    /// <summary>
    /// The room's floor: each column whose lowest surface everyone reaches
    /// (<paramref name="reached"/>, from the doors). How many of those have at
    /// least <paramref name="air"/> clear above, how many there are, and the
    /// longest straight run (along x or y) of clear ones, in units.
    /// </summary>
    public (int Clear, int Floor, int Run) ClearAir(float air, IReadOnlySet<(int, int, int)> reached)
    {
        bool Floor(int i, int j) => At(i, j) is { Length: > 0 } c && reached.Contains(c[0].Key);
        bool Ok(int i, int j) => Floor(i, j) && At(i, j)[0].Clear >= air;
        int clear = 0, floor = 0, run = 0;
        for (int i = 0; i < n; i++)
        {
            int along = 0, across = 0;
            for (int j = 0; j < n; j++)
            {
                if (Floor(i, j))
                    floor++;
                if (Ok(i, j))
                    clear++;
                along = Ok(i, j) ? along + 1 : 0;
                across = Ok(j, i) ? across + 1 : 0;
                run = Math.Max(run, Math.Max(along, across) * RoomLint.Grid);
            }
        }

        return (clear, floor, run);
    }

    /// <summary>
    /// The surfaces everyone reaches from <paramref name="starts"/> (floor
    /// points) by T1 moves: to a neighbouring column's surface up to a
    /// crouch-jump higher, or any distance down.
    /// </summary>
    public HashSet<(int, int, int)> ReachableAtT1(IEnumerable<Point> starts)
    {
        int up = JumpTable.CriticalPathReach;
        var seen = new HashSet<(int, int, int)>();
        var queue = new Queue<Surface>();
        foreach (Point p in starts)
            if (SurfaceUnder(p.X, p.Y, p.Z + RoomLint.Step) is { } s && seen.Add(s.Key))
                queue.Enqueue(s);
        while (queue.Count > 0)
        {
            Surface s = queue.Dequeue();
            foreach (var (di, dj) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                foreach (Surface t in At(s.I + di, s.J + dj))
                    if (t.Z - s.Z <= up && seen.Add(t.Key))
                        queue.Enqueue(t);
        }

        return seen;
    }

    /// <summary>
    /// Where an agent <paramref name="width"/> wide stands with its box
    /// centred on column (<paramref name="i"/>, <paramref name="j"/>), coming
    /// from height <paramref name="from"/>: on the highest surface under its
    /// box no more than a step above <paramref name="from"/> (a hull stands
    /// on the highest step under it, so stairs and lips are walked, and it
    /// falls to the floor below), or null when nothing is under it.
    /// </summary>
    public float? StandingHeight(int i, int j, float from, int width)
    {
        int r = BoxRadius(width);
        float? z = null;
        for (int di = -r; di <= r; di++)
            for (int dj = -r; dj <= r; dj++)
                foreach (Surface t in At(i + di, j + dj))
                    if (t.Z <= from + RoomLint.Step && (z is null || t.Z > z))
                        z = t.Z;
        return z;
    }

    /// <summary>Whether the air is open from <paramref name="z"/> to <paramref name="z"/> + <paramref name="height"/> in every column of a box <paramref name="width"/> wide centred on (<paramref name="i"/>, <paramref name="j"/>).</summary>
    public bool BoxClear(int i, int j, float z, int width, int height)
    {
        int r = BoxRadius(width);
        for (int di = -r; di <= r; di++)
            for (int dj = -r; dj <= r; dj++)
                if (!At(i + di, j + dj).Any(t => t.Z <= z + Brush.Epsilon && t.Z + t.Clear >= z + height))
                    return false;
        return true;
    }

    private static int BoxRadius(int width) => (int)Math.Ceiling((width / 2f - RoomLint.Grid / 2f) / RoomLint.Grid);

    /// <summary>
    /// The surfaces an agent of <paramref name="width"/> by
    /// <paramref name="height"/> walks over from <paramref name="starts"/>
    /// (floor points), never jumping: its box moves a column at a time,
    /// climbing at most a step (<see cref="StandingHeight"/>) and falling any
    /// distance, wherever its box is clear. A surface is reached when the
    /// agent's centre stands on it.
    /// </summary>
    /// <param name="avoid">Where the agent's centre may not stand (column, standing height): a hazard's floor.</param>
    public HashSet<(int, int, int)> ReachableWalking(IEnumerable<Point> starts, int width, int height, Func<int, int, float, bool>? avoid = null)
    {
        var states = new HashSet<(int I, int J, int Z)>();
        var queue = new Queue<(int I, int J, float Z)>();
        var reached = new HashSet<(int, int, int)>();
        void Visit(int i, int j, float z)
        {
            if (!states.Add((i, j, (int)MathF.Round(z))) || !BoxClear(i, j, z, width, height) || (avoid?.Invoke(i, j, z) ?? false))
                return;
            queue.Enqueue((i, j, z));
            foreach (Surface t in At(i, j))
                if (Math.Abs(t.Z - z) <= RoomLint.Step)
                    reached.Add(t.Key);
        }

        foreach (Point p in starts)
        {
            int i = (int)Math.Floor(p.X / RoomLint.Grid), j = (int)Math.Floor(p.Y / RoomLint.Grid);
            if (StandingHeight(i, j, p.Z, width) is { } z)
                Visit(i, j, z);
        }

        while (queue.Count > 0)
        {
            var (i, j, z) = queue.Dequeue();
            foreach (var (di, dj) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                if (StandingHeight(i + di, j + dj, z, width) is { } next)
                    Visit(i + di, j + dj, next);
        }

        return reached;
    }

    /// <summary>Whether <paramref name="reached"/> holds the surface under the floor point <paramref name="p"/>.</summary>
    public bool Reaches(IReadOnlySet<(int, int, int)> reached, Point p) =>
        SurfaceUnder(p.X, p.Y, p.Z + RoomLint.Step) is { } s && reached.Contains(s.Key);

    /// <summary>
    /// The surfaces grouped into ledges: neighbouring columns within a step
    /// of each other are one surface, and surfaces within a crouch-jump
    /// (<paramref name="rise"/> up or down, <paramref name="gap"/> across)
    /// of each other are one ledge, so a row of pillars is one spot.
    /// </summary>
    public IEnumerable<List<Surface>> Group(IReadOnlyList<Surface> surfaces, int rise, int gap)
    {
        var index = surfaces.Select((s, i) => (s.Key, i)).ToDictionary(p => p.Key, p => p.i);
        var parent = Enumerable.Range(0, surfaces.Count).ToArray();
        int Find(int a) => parent[a] == a ? a : parent[a] = Find(parent[a]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);

        // Walkable stretches first.
        for (int a = 0; a < surfaces.Count; a++)
            foreach (var (di, dj) in ((int, int)[])[(1, 0), (0, 1)])
                foreach (Surface t in At(surfaces[a].I + di, surfaces[a].J + dj))
                    if (index.TryGetValue(t.Key, out int b) && Math.Abs(t.Z - surfaces[a].Z) <= RoomLint.Step)
                        Union(a, b);

        // Then stretches a crouch-jump apart, compared by their bounds.
        var stretches = surfaces.Select((s, i) => (s, i)).GroupBy(p => Find(p.i)).Select(g => g.ToList()).ToList();
        for (int a = 0; a < stretches.Count; a++)
            for (int b = a + 1; b < stretches.Count; b++)
            {
                var (ra, rb) = (Bounds(stretches[a]), Bounds(stretches[b]));
                float dx = Math.Max(0, Math.Max(ra.X0 - rb.X1, rb.X0 - ra.X1)), dy = Math.Max(0, Math.Max(ra.Y0 - rb.Y1, rb.Y0 - ra.Y1));
                if (Math.Sqrt(dx * dx + dy * dy) <= gap && Math.Abs(ra.Z - rb.Z) <= rise)
                    Union(stretches[a][0].i, stretches[b][0].i);
            }

        return surfaces.Select((s, i) => (s, i)).GroupBy(p => Find(p.i)).Select(g => g.Select(p => p.s).ToList());

        static (float X0, float X1, float Y0, float Y1, float Z) Bounds(List<(Surface s, int i)> g)
        {
            float h = RoomLint.Grid / 2f;
            return (g.Min(p => p.s.X) - h, g.Max(p => p.s.X) + h, g.Min(p => p.s.Y) - h, g.Max(p => p.s.Y) + h, g.Average(p => p.s.Z));
        }
    }
}
