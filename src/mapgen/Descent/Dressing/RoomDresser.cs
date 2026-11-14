using System.Globalization;

namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// The dressing pass (plan_descent_mine_levels.md task A10): a built room
/// and a theme in, the room dressed out. It reads only the room's own facts
/// (its brushes, floor, walls, doors, markers and lamps), so it dresses any
/// shape, the grey placeholders and the real ones alike.
/// </summary>
/// <remarks>
/// <para>
/// What it adds, in order: the theme's sets (brush frames across a
/// corridor); its lamps along the room's lit path (the giant's route from
/// door to door, <see cref="FloorPlan.GiantRoutes"/>) with their
/// <c>light</c> or <c>light_spot</c> entities, replacing the shapes' own
/// lamps; rails along the route; runs along the walls under the ceiling;
/// piles in corners; clutter against walls and on open floor.
/// </para>
/// <para>
/// Every piece keeps the rules <see cref="DressLint"/> holds: nothing
/// within <see cref="DoorClear"/> of a door's opening; nothing in the air a
/// ledge's jump needs (<see cref="FloorPlan.LedgeAir"/> over every raised
/// surface), and in a jump room nothing on the open floor below
/// <see cref="JumpTable.RocketClearAir"/>; nothing on a marker; nothing
/// solid in the lane round the giant's route; nothing in a brush or outside
/// the cell; every prop held up by a floor, a wall or a ceiling; ceiling runs
/// only above <see cref="CeilingRunFloor"/>; static props wherever the model
/// allows, a <c>prop_dynamic</c> only within the theme's budget. The same
/// room, theme and seed give the same bytes.
/// </para>
/// </remarks>
public static partial class RoomDresser
{
    /// <summary>How close to a door's opening no dressing may come (the approach a shape keeps clear, §5).</summary>
    public const int DoorClear = RoomBuilder.DoorApproach;

    /// <summary>How close to a marker's point no dressing may come, level, below a standing player's head.</summary>
    public const int MarkerClear = 56;

    /// <summary>The lowest a ceiling run's underside may hang above the floor.</summary>
    public const int CeilingRunFloor = 192;

    /// <summary>The columns either side of the giant's route kept clear of solid props (its reach and one more).</summary>
    public const int LaneReach = FloorPlan.GiantReach + 1;

    /// <summary>How far a floor lamp (a lantern set down) stands out from its wall, so its light pools on the floor, not on the wall behind it.</summary>
    public const int FloorLampOut = 24;

    /// <summary>How far clutter keeps from a deck's, a ledge's or a pit's edge.</summary>
    public const int LedgeClear = 80;

    /// <summary>How far open-floor clutter keeps from any brush of the room but its floor (a player's width and more).</summary>
    public const int FeatureClear = 96;

    /// <summary>The tallest a piece of track may stand (a buffer stop) and still be flat to the lint: not solid, a giant walks through it.</summary>
    public const int RailStopHeight = 24;

    /// <summary>A room's shape family: its name before the first '_'.</summary>
    public static string Family(string name) => name.Split('_')[0];

    /// <summary>A hallway (its entry tagged <c>corridor</c> or <c>junction</c>), whose floor is its route: no clutter stands on it.</summary>
    public static bool IsHallway(Room room) => room.Entry.Tags.Contains("corridor") || room.Entry.Tags.Contains("junction");

    /// <summary>The key a dressing piece carries so the lint and the reports can tell it from a shape's (its role).</summary>
    public const string RoleKey = "descent_dress";

    /// <summary>The room dressed in <paramref name="theme"/> for <paramref name="seed"/>; the room itself when the theme dresses nothing (the grey).</summary>
    public static Room Dress(Room room, RoomTheme theme, ulong seed)
    {
        if (theme.Lamps is null && theme.Props == PropSet.None)
            return room;
        return new Dresser(room, theme, new RoomSeed(seed, "dress:" + room.Name)).Run();
    }

    /// <summary>A dressing role: what kind of piece a prop is.</summary>
    public enum Role
    {
        Lamp,
        Wall,
        Corner,
        Floor,
        CeilingRun,
        Rail,
        Set,
        Hung,
        Fill,
        Cord,
        Centre,
        Hanging,
    }

    /// <summary>The yaw of a level direction.</summary>
    public static int Yaw(int dx, int dy) => (dx, dy) switch
    {
        (1, 0) => 0,
        (0, 1) => 90,
        (-1, 0) => 180,
        _ => 270,
    };

    /// <summary>A vector in a piece's own frame (x along its front) turned to the world by <paramref name="yaw"/>.</summary>
    public static Point Turn(Point v, int yaw)
    {
        double r = yaw * Math.PI / 180;
        float c = (float)Math.Round(Math.Cos(r), 6), s = (float)Math.Round(Math.Sin(r), 6);
        return new(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
    }

    internal static string Num(float v) => Point.Fmt(v);

    private sealed partial class Dresser
    {
        private readonly Room room;
        private readonly RoomTheme theme;
        private readonly RoomSeed rng;
        private readonly RoomKit kit;
        private readonly float sill;
        private readonly FloorPlan plan;
        private readonly Box[] doors;
        private readonly Point[] markers;
        private readonly List<Brush> brushes = [];
        private readonly List<RoomEntity> added = [];
        private readonly List<(Box Box, Role Role)> placed = [];
        private readonly List<Point> lamps = [];
        private readonly List<(Box Cap, Box Post0, Box Post1, bool AlongX)> setFrames = [];

        /// <summary>The bays between neighbouring pilasters: the stretch's first site, the bay's middle along the wall, its width.</summary>
        private readonly List<(WallSite Site, float Mid, float Width)> bays = [];
        private readonly Box[] ledgeAir;
        private RoomSolids solids;
        private HashSet<(int, int)> lane = [];
        private IReadOnlyList<IReadOnlyList<(int I, int J)>> routes = [];
        private int dynamics;

        public Dresser(Room room, RoomTheme theme, RoomSeed rng)
        {
            this.room = room;
            this.theme = theme;
            this.rng = rng;
            kit = room.Kit;
            sill = kit.Sill;
            plan = new FloorPlan(room);
            solids = new RoomSolids(room);
            doors = [.. room.Sockets.Select(s => Box.DoorOpening(kit, s.Face, s.Offset, room.Cells))];
            markers = [.. room.Entities.Where(e => e.ClassName == "info_poi" && e.Origin is not null).Select(e => e.Origin!.Value)];
            ledgeAir = [.. LedgeAir(plan)];
        }

        public Room Run()
        {
            routes = plan.GiantRoutes();
            lane = plan.Lane(routes, LaneReach);
            PropSet props = theme.Props;
            if (props.Sets is { } sets)
                Sets(sets);
            if (theme.Pilasters is { } pilasters)
                Pilasters(pilasters);
            if (theme.Gantry is { } gantry)
                GantryOverFloor(gantry);
            if (props.Centrepiece.Count > 0)
                Centrepiece(props.Centrepiece);
            if (theme.Headframe)
                HeadframeOverFocus();
            if (theme.RoofBeams > 0)
                RoofBeams(theme.RoofBeams);
            if (theme.Lamps is not null)
                Lamps();
            if (theme.WorkLamps is { } work)
                WorkLamps(work);
            if (theme.CoolFill is { } fill)
                CoolFill(fill.Light, fill.Pitch);
            if (props.Rail is { } rail)
                DoorRails(rail, props);
            if (props.CeilingRun is { } run)
            {
                CeilingRuns(run);
                if (props.WallRunHeight > 0)
                    WallRun(run, props.WallRunHeight);
            }
            if (props.Corner.Count > 0)
                Corners(props);
            if (props.Wall.Count > 0)
                WallClutter(props);
            if (props.Hung.Count > 0)
                HungClutter(props);
            if (props.Hanging.Count > 0)
                HangingClutter(props);
            // A jump room's floor too, clear of its ledges' take-off patches (user ruling 2026-11-10).
            if (props.Floor.Count > 0)
                FloorClutter(props, props.Floor);
            // Last, so the bands run behind and round what stands on the walls, never through it.
            if (theme.Bands is { } bands)
                Bands(bands);
            if (theme.Hazard is { } hazard)
                HazardStripes(hazard);

            var keep = theme.Lamps is null ? room.Entities : room.Entities.Where(e => e.ClassName is not ("light" or "light_spot")).ToList();
            var water = theme.Standing is { } standing ? StandingWater(standing) : [];
            return Unblock(room with { World = [.. room.World, .. brushes], Entities = [.. keep, .. added], Water = [.. room.Water, .. water] });
        }

        /// <summary>The most props the unblocking pass takes out of one room.</summary>
        private const int MaxUnblock = 8;

        /// <summary>
        /// Takes out the solid props that cut floor off (<see cref="DressLint.CutOffFloor"/>,
        /// <see cref="DressLint.CutOffWalk"/>): while some floor walked to before the dressing
        /// is no longer, the solid prop nearest it goes, never a lamp.
        /// </summary>
        private Room Unblock(Room dressed)
        {
            var models = DressLint.Models(theme);
            for (int n = 0; n < MaxUnblock; n++)
            {
                var pieces = DressLint.Pieces(room, dressed, models);
                Point? at = DressLint.CutOffFloor(room, dressed, pieces) is { Count: > 0 } giant
                    ? plan.At(giant[0].I, giant[0].J)
                    : DressLint.CutOffWalk(room, dressed, pieces) is { Count: > 0 } walk ? new Point(walk[0].X, walk[0].Y, walk[0].Z) : null;
                if (at is not { } p)
                    return dressed;
                var culprit = pieces.Where(q => q.Solid && q.Entity is not null && q.Role is not (Role.Lamp or Role.Set))
                    .OrderBy(q => q.Box.LevelDistanceTo(p.X, p.Y)).FirstOrDefault();
                if (culprit is null)
                    return dressed;
                dressed = dressed with { Entities = [.. dressed.Entities.Where(e => !ReferenceEquals(e, culprit.Entity))] };
            }

            return dressed;
        }

        //---------------------------------------------------------------------
        // Rules every piece keeps.
        //---------------------------------------------------------------------

        private bool Fits(Box box, bool solid, bool floorStanding, Role role, bool touching = false)
        {
            float c = room.Extent;
            if (box.Mins.X < 0 || box.Mins.Y < 0 || box.Mins.Z < 0 || box.Maxs.X > c || box.Maxs.Y > c || box.Maxs.Z > room.Height)
                return false;
            if (doors.Any(d => d.DistanceTo(box) < DoorClear))
                return false;
            if (markers.Any(m => box.Mins.Z < m.Z + RoomLint.HullHeight && box.Maxs.Z > m.Z - 1 && box.LevelDistanceTo(m.X, m.Y) < MarkerClear))
                return false;
            if ((role == Role.Centre ? InJumpLedgeAir(box) : InLedgeAir(box)) || InJumpAir(box))
                return false;
            if (solid && box.Mins.Z < sill + FloorPlan.GiantHeight && plan.Covered(box).Any(lane.Contains))
                return false;
            // Pieces keep 4 apart, but a run's pieces meet end to end, and a lamp hung from a set or set on
            // its stand touches what holds it (and never overlaps anything).
            // A set, a band or a pilaster is a brush: a prop may stand against it as against a wall.
            if (placed.Any(p => p.Role == role && role is Role.Rail or Role.CeilingRun ? p.Box.Overlaps(box)
                                : p.Role == Role.Set || (touching && p.Role == Role.Lamp) ? p.Box.Overlaps(box) : p.Box.DistanceTo(box) < 4))
                return false;
            if (solids.Blocked(box.Mins, box.Maxs))
                return false;
            if (floorStanding)
            {
                // No overhang over a pit or a step: the floor under each corner is the floor it stands on.
                foreach (float x in (float[])[box.Mins.X + 1, box.Maxs.X - 1])
                    foreach (float y in (float[])[box.Mins.Y + 1, box.Maxs.Y - 1])
                        if (solids.FloorBelow(x, y, box.Mins.Z + 1) is not { } f || Math.Abs(f - box.Mins.Z) > 1)
                            return false;
            }

            return true;
        }

        private bool InLedgeAir(Box box) => ledgeAir.Any(a => a.Overlaps(box));

        /// <summary>
        /// Whether the box stands in the air over a surface a jump reaches (above T1): a centrepiece stands
        /// on a hall's plinth, a step-and-a-crouch-jump up, whose air no jump needs.
        /// </summary>
        private bool InJumpLedgeAir(Box box) => ledgeAir.Any(a => a.Mins.Z > sill + JumpTable.CriticalPathReach && a.Overlaps(box));

        /// <summary>The narrowest gap a solid piece leaves to a wall or another piece, unless it stands against it: a player's width and a hand.</summary>
        private const int SliverClear = 56;

        /// <summary>The widest gap that counts as standing against a wall or a piece.</summary>
        private const int Flush = 6;

        /// <summary>
        /// Whether a solid piece at <paramref name="box"/> would leave a sliver
        /// of floor between itself and a wall or another solid piece: a gap
        /// wider than <see cref="Flush"/> and narrower than
        /// <see cref="SliverClear"/>, which a player drops or wedges into and
        /// cannot walk out of (the preview's one-node traps by a chamfer).
        /// </summary>
        private bool LeavesSliver(Box box)
        {
            float z = box.Mins.Z + 24;
            foreach (var (dx, dy) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                foreach (float t in (float[])[0.15f, 0.5f, 0.85f])
                {
                    float x = dx > 0 ? box.Maxs.X : dx < 0 ? box.Mins.X : box.Mins.X + (box.Maxs.X - box.Mins.X) * t;
                    float y = dy > 0 ? box.Maxs.Y : dy < 0 ? box.Mins.Y : box.Mins.Y + (box.Maxs.Y - box.Mins.Y) * t;
                    if (solids.Ray(new Point(x, y, z), dx, dy) is { } r && r > Flush && r < SliverClear)
                        return true;
                }

            // A lamp standing on the floor (a lamp post, a brazier, a lantern's crate) walls a gap in as any piece does.
            return placed.Any(p => (p.Role is not (Role.Set or Role.Rail or Role.CeilingRun or Role.Lamp) || p.Role == Role.Lamp && p.Box.Mins.Z <= sill + 1)
                                   && p.Box.Mins.Z < box.Maxs.Z && p.Box.Maxs.Z > box.Mins.Z
                                   && p.Box.DistanceTo(box) is var d && d > Flush && d < SliverClear);
        }

        /// <summary>
        /// Whether a solid piece at <paramref name="box"/> would cut off floor a
        /// giant reaches now (<see cref="DressLint.CutOffFloor"/>'s rule, on the
        /// pieces placed so far), but the floor beside it.
        /// </summary>
        private bool CutsOff(Box box)
        {
            var blocked = new HashSet<(int, int)>();
            foreach (var (b, role) in placed)
                if (role is not (Role.Set or Role.Rail or Role.CeilingRun) && b.Mins.Z < sill + FloorPlan.GiantHeight && b.Maxs.Z > sill + RoomLint.Step)
                    foreach (var col in plan.Covered(b))
                        blocked.Add(col);
            var before = plan.GiantReached(blocked);
            var mine = plan.Covered(box).ToHashSet();
            blocked.UnionWith(mine);
            var after = plan.GiantReached(blocked);
            int reach = FloorPlan.GiantReach + 1;
            return before.Any(c => !after.Contains(c) && !mine.Any(m => Math.Abs(m.Item1 - c.I) <= reach && Math.Abs(m.Item2 - c.J) <= reach));
        }

        /// <summary>Whether the edge of a surface higher or lower than the box's floor stands within <paramref name="by"/> of the box, level (the deck it stands on is no edge).</summary>
        private bool NearRaised(Box box, float by) =>
            plan.Raised.Any(s => Math.Abs(s.Z - box.Mins.Z) > RoomLint.Step
                                 && box.LevelDistanceTo(s.I * RoomLint.Grid + RoomLint.Grid / 2f, s.J * RoomLint.Grid + RoomLint.Grid / 2f) < by);

        private bool InJumpAir(Box box) => JumpAirRefuses(room, plan, box);

        //---------------------------------------------------------------------
        // Sets: brush frames across a corridor.
        //---------------------------------------------------------------------

        private void Sets(SetFrame frame)
        {
            // Sets hold up a drift's roof: a hallway or a low room, not a chamber's aisles.
            if (!IsHallway(room) && room.Height > LayoutNames.LowMax)
                return;
            Surface trim = theme.TrimOrBlock;
            var at = new List<Point>();
            foreach (var route in routes)
            {
                float walked = 0;
                for (int k = 2; k + 2 < route.Count; k++, walked += RoomLint.Grid)
                {
                    var (i, j) = route[k];
                    int di = route[k + 1].I - i, dj = route[k + 1].J - j;
                    // Straight here: the two columns either side run the same way.
                    if (route[k - 2].I != i - 2 * di || route[k - 2].J != j - 2 * dj || route[k + 2].I != i + 2 * di || route[k + 2].J != j + 2 * dj)
                        continue;
                    Point p = plan.At(i, j);
                    var centre = new Point((float)Math.Floor(p.X), (float)Math.Floor(p.Y), sill);
                    if (at.Any(q => Math.Abs(q.X - centre.X) + Math.Abs(q.Y - centre.Y) < frame.Spacing))
                        continue;
                    if (TrySet(frame, trim, centre, di != 0))
                        at.Add(centre);
                }
            }
        }

        /// <summary>A set across the corridor at <paramref name="c"/>; <paramref name="alongX"/> when the corridor runs along x.</summary>
        private bool TrySet(SetFrame frame, Surface trim, Point c, bool alongX)
        {
            int ax = alongX ? 0 : 1, ay = alongX ? 1 : 0;
            float h = frame.Post / 2f;
            // The walls either side, measured at both faces of the set: a straight wall.
            float? Side(int sign, float offset)
            {
                var from = new Point(c.X + (alongX ? offset : 0), c.Y + (alongX ? 0 : offset), sill + 64);
                return solids.Ray(from, ax * sign, ay * sign);
            }

            if (Side(1, -h + 1) is not { } r1 || Side(1, h - 1) is not { } r1b || Side(-1, -h + 1) is not { } r0 || Side(-1, h - 1) is not { } r0b
                || Math.Abs(r1 - r1b) > 0.01f || Math.Abs(r0 - r0b) > 0.01f || r0 + r1 > frame.MaxWidth)
                return false;
            float lo = (alongX ? c.Y : c.X) - r0, hi = (alongX ? c.Y : c.X) + r1;
            if (solids.CeilingAbove(c.X, c.Y, sill + 1) is not { } ceil || ceil - sill > 512)
                return false;
            if (hi - lo - 2 * frame.Post < FloorPlan.GiantWidth + 32 || ceil - frame.Cap - sill < FloorPlan.GiantHeight + 16)
                return false;
            Box Piece(float a0, float a1, float z0, float z1) => alongX
                ? new(new(c.X - h, a0, z0), new(c.X + h, a1, z1))
                : new(new(a0, c.Y - h, z0), new(a1, c.Y + h, z1));
            Box[] pieces = [Piece(lo, lo + frame.Post, sill, ceil - frame.Cap), Piece(hi - frame.Post, hi, sill, ceil - frame.Cap), Piece(lo, hi, ceil - frame.Cap, ceil)];
            foreach (Box b in pieces)
            {
                if (doors.Any(d => d.DistanceTo(b) < DoorClear) || InLedgeAir(b) || solids.Blocked(b.Mins, b.Maxs))
                    return false;
                if (markers.Any(m => b.Mins.Z < m.Z + RoomLint.HullHeight && b.LevelDistanceTo(m.X, m.Y) < MarkerClear))
                    return false;
            }

            // A set fallen in (level 6): one post left standing, its cap and the other post gone.
            if (frame.Broken > 0 && rng.Chance(frame.Broken))
            {
                AddBrush(pieces[rng.Chance(0.5f) ? 0 : 1], trim);
                solids = new RoomSolids(room, brushes);
                return true;
            }

            foreach (Box b in pieces)
                AddBrush(b, trim);

            setFrames.Add((pieces[2], pieces[0], pieces[1], alongX));

            solids = new RoomSolids(room, brushes);
            return true;
        }

        //---------------------------------------------------------------------
        // Lamps.
        //---------------------------------------------------------------------

        private void Lamps()
        {
            LampKind kind = theme.Lamps!;
            var floorClear = Enumerable.Range(0, plan.Size * plan.Size)
                .Select(k => plan.Clear(k / plan.Size, k % plan.Size)).Where(v => v > 0).Order().ToArray();
            float typical = floorClear.Length == 0 ? 0 : floorClear[floorClear.Length / 2];
            bool tall = kind.Mount == LampMount.Ceiling && typical > kind.MaxHang;
            LampKind use = tall ? theme.TallLamps ?? kind : kind;
            if (use.Pools is { } pools)
                PoolLamps(use, pools);
            else if (use.Mount == LampMount.Ceiling)
                CeilingLamps(use);
            else
                PerimeterLamps(use);
            if (theme.Pendants is { Grid: > 0 } pendants && room.Height >= PendantRoom && !IsHallway(room))
                PendantGrid(pendants);
            CoverDark(use);
        }

        /// <summary>The lowest room that takes a grid of pendants: a hall.</summary>
        public const int PendantRoom = 1024;

        /// <summary>
        /// Pendants on a grid over a hall's or shaft's body (<see cref="RoomTheme.Pendants"/>),
        /// each over walked floor, clear of what stands there: lamps on long cords, overhead.
        /// </summary>
        private void PendantGrid(LampKind kind)
        {
            Rect b = room.Body;
            int nx = Math.Max(1, (int)((b.X1 - b.X0) / kind.Grid)), ny = Math.Max(1, (int)((b.Y1 - b.Y0) / kind.Grid));
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    float x = MathF.Round(b.X0 + (b.X1 - b.X0) * (i + 0.5f) / nx), y = MathF.Round(b.Y0 + (b.Y1 - b.Y0) * (j + 0.5f) / ny);
                    var (ci, cj) = FloorPlan.ColumnOf(x, y);
                    if (!plan.IsFloor(ci, cj) || lamps.Any(q => Level(q, new Point(x, y, 0)) < kind.Grid / 2f))
                        continue;
                    if (PendantSite(kind, x, y, sill) is { } site)
                        TryLamp(site);
                }
        }

        /// <summary>
        /// A pendant over (<paramref name="x"/>, <paramref name="y"/>): its lamp's bottom the kind's
        /// height above the floor there (and above a jump room's rocket air), its cord stacked from the
        /// lamp's top to the ceiling, longest pieces first and the last set back to end on the ceiling.
        /// </summary>
        private LampSite? PendantSite(LampKind kind, float x, float y, float floorZ)
        {
            if (solids.FloorBelow(x, y, floorZ + 1) is not { } floor || solids.CeilingAbove(x, y, floor + 1) is not { } ceil || kind.Cords.Count == 0)
                return null;
            PropModel f = kind.Fixture;
            float bottom = floor + kind.WallHeight;
            if (room.Entry.Jump)
                bottom = Math.Max(bottom, sill + JumpTable.RocketClearAir + 16);
            float oz = bottom - f.Mins.Z, top = oz + f.Maxs.Z;
            if (ceil - top < 8)
                return null;
            var cord = new List<(PropModel, Point)>();
            float z = top;
            foreach (PropModel c in kind.Cords)
            {
                float l = c.Maxs.Z - c.Mins.Z;
                while (ceil - z >= l - 0.5f)
                {
                    cord.Add((c, new Point(x, y, z - c.Mins.Z)));
                    z += l;
                }
            }

            if (ceil - z > 0.5f)
            {
                PropModel last = kind.Cords[^1];
                cord.Add((last, new Point(x, y, ceil - (last.Maxs.Z - last.Mins.Z) - last.Mins.Z)));
            }

            return Site(kind, new Point(x, y, oz), 0) with { Cord = cord };
        }

        private void CeilingLamps(LampKind kind)
        {
            foreach (var route in routes)
            {
                float walked = 0;
                float next = kind.Spacing / 2f;
                foreach (var (i, j) in route)
                {
                    if (walked >= next)
                    {
                        Point p = plan.At(i, j);
                        if (!lamps.Any(q => Level(q, p) < kind.Spacing * 0.6f) && TryCeilingLamp(kind, p))
                            next = walked + kind.Spacing;
                    }

                    walked += RoomLint.Grid;
                }
            }

            if (kind.Fill <= 0)
                return;
            int step = Math.Max(1, kind.Fill / RoomLint.Grid);
            for (int i = step / 2; i < plan.Size; i += step)
                for (int j = step / 2; j < plan.Size; j += step)
                {
                    if (!plan.IsFloor(i, j))
                        continue;
                    Point p = plan.At(i, j);
                    if (!lamps.Any(q => Level(q, p) < kind.Fill * 0.75f))
                        TryCeilingLamp(kind, p);
                }
        }

        private bool TryCeilingLamp(LampKind kind, Point p)
        {
            var at = new Point((float)Math.Round(p.X), (float)Math.Round(p.Y), sill);
            if (solids.CeilingAbove(at.X, at.Y, sill + 1) is not { } ceil)
                return false;
            var origin = new Point(at.X, at.Y, ceil - kind.Fixture.Maxs.Z);
            return TryLamp(kind, origin, yaw: 0);
        }

        private void PerimeterLamps(LampKind kind)
        {
            float z0 = kind.Mount == LampMount.Floor ? 32 : kind.WallHeight;
            var sites = WallSites(z0).OrderBy(s => Math.Atan2(s.Face.Y - room.Extent / 2f, s.Face.X - room.Extent / 2f)).ThenBy(s => s.Face.X).ThenBy(s => s.Face.Y).ToList();
            var chosen = new List<Point>();
            foreach (var s in sites)
            {
                if (chosen.Any(q => Level(q, s.Face) < kind.Spacing))
                    continue;
                bool any = false;
                if (kind.Mount == LampMount.Floor)
                {
                    PropModel f = kind.Fixture;
                    Point origin = Out(s.Face with { Z = sill - f.Mins.Z }, s.Dx, s.Dy, f.Back + FloorLampOut);
                    any = TryLamp(kind, origin, Yaw(-s.Dx, -s.Dy) - f.Front);
                }
                else
                {
                    // Rows up the wall to the room's roof; a row a deck or catwalk stands
                    // in is refused by the fit, and the rows above it still light the room.
                    float roof = room.Height - kit.WallDepth;
                    for (float z = sill + kind.WallHeight; z + kind.Fixture.Maxs.Z <= roof - 16; z += kind.RowPitch > 0 ? kind.RowPitch : float.MaxValue)
                    {
                        // The wall is there, flat, at this row's height.
                        Point from = Out(s.Face with { Z = z }, s.Dx, s.Dy, 8);
                        if (solids.Ray(from, s.Dx, s.Dy) is not { } r || Math.Abs(r - 8) > 0.01f)
                            continue;
                        Point origin = Out(s.Face with { Z = z }, s.Dx, s.Dy, kind.Fixture.Back + 0.5f);
                        any |= TryLamp(kind, origin, Yaw(-s.Dx, -s.Dy) - kind.Fixture.Front);
                        if (kind.RowPitch <= 0)
                            break;
                    }
                }

                if (any)
                    chosen.Add(s.Face);
            }
        }

        /// <summary>A lamp the dresser could hang: its kind, where its fixture stands and faces, its box and its light.</summary>
        /// <summary>A lamp the dresser could hang: its kind, where its fixture stands and faces, its box and its light, and what it stands on (a crate under a lantern) or hangs from.</summary>
        private readonly record struct LampSite(LampKind Kind, Point Origin, int Yaw, Box Box, RoomEntity Light, LampStand? Stand = null, bool Hung = false)
        {
            /// <summary>A pendant's cord pieces and their origins.</summary>
            public IReadOnlyList<(PropModel Model, Point Origin)>? Cord { get; init; }
        }

        /// <summary>The prop a lamp stands on: its model, origin, yaw and box.</summary>
        private sealed record LampStand(PropModel Model, Point Origin, int Yaw, Box Box);

        private static LampSite Site(LampKind kind, Point origin, int yaw, LampStand? stand = null, bool hung = false)
        {
            PropModel f = kind.Fixture;
            var (mins, maxs) = f.Turned(yaw);
            int front = ((yaw + f.Front) % 360 + 360) % 360;
            Point light = origin + Turn(kind.LightAt, front);
            return new LampSite(kind, origin, yaw, new Box(origin + mins, origin + maxs), LightEntity(kind, light, front), stand, hung);
        }

        /// <summary>A lamp of <paramref name="kind"/> set on its stand against a wall at <paramref name="face"/>, the wall along (dx, dy), on the floor at <paramref name="floorZ"/>.</summary>
        private static LampSite OnStand(LampKind kind, PropModel stand, Point face, int dx, int dy, float floorZ)
        {
            int yaw = Yaw(-dx, -dy) - stand.Front;
            var (smins, smaxs) = stand.Turned(yaw);
            Point so = Out(face with { Z = floorZ - stand.Mins.Z }, dx, dy, stand.Back + 1);
            var box = new Box(so + smins, so + smaxs);
            PropModel f = kind.Fixture;
            var top = new Point((box.Mins.X + box.Maxs.X) / 2, (box.Mins.Y + box.Maxs.Y) / 2, box.Maxs.Z - f.Mins.Z + 0.25f);
            return Site(kind, top, Yaw(-dx, -dy) - f.Front, new LampStand(stand, so, yaw, box));
        }

        private bool TryLamp(LampKind kind, Point origin, int yaw) => TryLamp(Site(kind, origin, yaw));

        private bool TryLamp(LampSite site)
        {
            PropModel f = site.Kind.Fixture;
            if (!f.Static && dynamics >= theme.DynamicBudget)
                return false;
            bool onFloor = site.Kind.Mount == LampMount.Floor && site.Stand is null && !site.Hung;
            if (!Fits(site.Box, solid: f.Solid && f.Static, floorStanding: onFloor, Role.Lamp, touching: site.Stand is not null || site.Hung))
                return false;
            // A lamp standing on the floor (a brazier, a lamp post) is clutter a giant walks round: it may not wall floor off.
            if (onFloor && f.Solid && f.Static && CutsOff(site.Box))
                return false;
            if (site.Stand is { } stand && (!Fits(stand.Box, solid: stand.Model.Solid, floorStanding: true, Role.Lamp) || CutsOff(stand.Box)))
                return false;
            var cordBoxes = (site.Cord ?? []).Select(c => (c.Model, c.Origin, Box: new Box(c.Origin + c.Model.Mins, c.Origin + c.Model.Maxs))).ToArray();
            if (cordBoxes.Any(c => !Fits(c.Box, solid: false, floorStanding: false, Role.Cord, touching: true)))
                return false;
            Point light = site.Light.Origin!.Value;
            if (solids.Blocked(light - new Point(1, 1, 1), light + new Point(1, 1, 1)))
                return false;
            if (site.Stand is { } s)
            {
                AddProp(s.Model, s.Origin, s.Yaw, Role.Lamp, shadows: true);
                placed.Add((s.Box, Role.Lamp));
            }

            AddProp(f, site.Origin, site.Yaw, Role.Lamp, shadows: false);
            placed.Add((site.Box, Role.Lamp));
            foreach (var (model, origin, box) in cordBoxes)
            {
                AddProp(model, origin, 0, Role.Cord, shadows: false);
                placed.Add((box, Role.Cord));
            }
            lamps.Add(light);
            added.Add(site.Light);
            return true;
        }

        /// <summary>The height up a wall the cover pass hangs a wall lamp where the theme's own height is out of reach (under a deck).</summary>
        private const int LowWallLamp = 104;

        /// <summary>The most lamps the cover pass adds to one cell of a room.</summary>
        private const int MaxCoverLamps = 12;

        /// <summary>
        /// The cover pass: while some floor a player walks to is beyond every
        /// light (<see cref="RoomLint.Dark"/>, the lint's own rule), hang the
        /// theme's lamp that lights most of it, of any of its kinds, where it
        /// fits. A free-form room's alcoves and ledges are off the route the
        /// lamps were hung along, and a chamber's roof can be above a ceiling
        /// lamp's reach of its floor.
        /// </summary>
        private void CoverDark(LampKind first)
        {
            CoverDark([first, theme.TallLamps, theme.Lamps, theme.CoverLamps, room.Height >= PendantRoom ? theme.Pendants : null]);
        }

        private void CoverDark(IEnumerable<LampKind?> order)
        {
            Room now = room with { World = [.. room.World, .. brushes] };
            var columns = new RoomColumns(now);
            var floor = RoomLint.WalkedFloor(now, columns).Select(s => new Point(s.X, s.Y, s.Z)).ToList();
            var lit = added.Select(RoomLint.LightCone.Of).OfType<RoomLint.LightCone>().ToArray();
            var dark = floor.Where(p => !lit.Any(l => l.Reaches(p))).ToList();
            if (dark.Count == 0)
                return;
            LampKind[] kinds = [.. order.OfType<LampKind>().Distinct()];
            var sites = kinds.SelectMany(k => CoverSites(k, floor)).Select(s => (Site: s, Cone: RoomLint.LightCone.Of(s.Light)!.Value)).ToList();
            for (int n = 0; n < MaxCoverLamps * room.Cells * room.Cells && dark.Count > 0; n++)
            {
                var candidates = sites.Select(c => (c.Site, c.Cone, Lit: dark.Count(c.Cone.Reaches)))
                    .Where(c => c.Lit > 0)
                    .OrderByDescending(c => c.Lit).ThenBy(c => c.Site.Origin.X).ThenBy(c => c.Site.Origin.Y).ThenBy(c => c.Site.Origin.Z).ToList();
                RoomLint.LightCone? hung = null;
                foreach (var c in candidates)
                    if (TryLamp(c.Site))
                    {
                        hung = c.Cone;
                        break;
                    }

                if (hung is not { } h)
                    return;
                dark.RemoveAll(h.Reaches);
            }
        }

        /// <summary>Where a lamp of <paramref name="kind"/> could hang over <paramref name="floor"/>: under the ceiling over it, or on the walls seen from it.</summary>
        private IEnumerable<LampSite> CoverSites(LampKind kind, IReadOnlyList<Point> floor)
        {
            PropModel f = kind.Fixture;
            var seen = new HashSet<(int, int, int)>();
            if (kind.Mount == LampMount.Pendant)
            {
                foreach (Point p in floor)
                {
                    float x = MathF.Floor(p.X / 64) * 64 + 32, y = MathF.Floor(p.Y / 64) * 64 + 32;
                    if (!seen.Add(((int)x, (int)y, (int)p.Z)) || PendantSite(kind, x, y, p.Z) is not { } site)
                        continue;
                    yield return site;
                }

                yield break;
            }

            if (kind.Mount == LampMount.Ceiling)
            {
                foreach (Point p in floor)
                {
                    float x = MathF.Floor(p.X / 32) * 32 + 16, y = MathF.Floor(p.Y / 32) * 32 + 16;
                    if (solids.CeilingAbove(x, y, p.Z + 1) is not { } ceil || !seen.Add(((int)x, (int)y, (int)ceil)))
                        continue;
                    yield return Site(kind, new Point(x, y, ceil - f.Maxs.Z), 0);
                }

                yield break;
            }

            if (kind.Mount == LampMount.Floor)
            {
                // Set down by a wall on any floor a player walks: the room's floor, a pit's, a deck's.
                foreach (Point p in floor)
                {
                    float x = MathF.Floor(p.X / 32) * 32 + 16, y = MathF.Floor(p.Y / 32) * 32 + 16;
                    if (!seen.Add(((int)x, (int)y, (int)p.Z)))
                        continue;
                    foreach (var (dx, dy) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                        if (solids.Ray(new Point(x, y, p.Z + 32), dx, dy) is { } r && r <= 64)
                            yield return kind.Stand is { } stand
                                ? OnStand(kind, stand, new Point(x + dx * r, y + dy * r, p.Z), dx, dy, p.Z)
                                : Site(kind, Out(new Point(x + dx * r, y + dy * r, p.Z - f.Mins.Z), dx, dy, f.Back + FloorLampOut), Yaw(-dx, -dy) - f.Front);
                }

                yield break;
            }

            // On a wall seen from the floor, the kind's height above that floor: the walls
            // round the room and the faces of its terraces and decks alike.
            var faces = new HashSet<(int, int, int, int, int)>();
            float reach = (kind.Light.FiftyPercent + kind.Light.ZeroPercent) / 2;
            // Under a deck or a low roof, lower down the wall: a lamp a hand above head height.
            foreach (Point p in floor)
            {
                float x = MathF.Floor(p.X / 32) * 32 + 16, y = MathF.Floor(p.Y / 32) * 32 + 16;
                if (!seen.Add(((int)x, (int)y, (int)p.Z)))
                    continue;
                foreach (float z in (float[])[p.Z + kind.WallHeight, p.Z + LowWallLamp])
                foreach (var (dx, dy) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                {
                    if (solids.Ray(new Point(x, y, z), dx, dy) is not { } r || r > reach)
                        continue;
                    var face = new Point(x + dx * r, y + dy * r, z);
                    if (!faces.Add(((int)face.X, (int)face.Y, (int)z, dx, dy)))
                        continue;
                    // The wall is there, flat, at the lamp's height.
                    if (solids.Ray(Out(face, dx, dy, 8), dx, dy) is not { } back || Math.Abs(back - 8) > 0.01f)
                        continue;
                    yield return Site(kind, Out(face, dx, dy, f.Back + 0.5f), Yaw(-dx, -dy) - f.Front);
                }
            }
        }

        private static RoomEntity LightEntity(LampKind kind, Point at, int yaw)
        {
            Lamp l = kind.Light;
            if (kind.Spot is not { } spot)
                return new RoomEntity("light", at, [.. l.Keys, new(RoleKey, nameof(Role.Lamp))], [], []);
            return new RoomEntity("light_spot", at,
            [
                new("_light", l.Light), new("_lightHDR", "-1 -1 -1 1"),
                new("_fifty_percent_distance", Num(l.FiftyPercent)), new("_zero_percent_distance", Num(l.ZeroPercent)),
                new("_inner_cone", Num(spot.Inner)), new("_cone", Num(spot.Outer)), new("_exponent", "1"),
                new("pitch", Num(-spot.Pitch)), new("angles", string.Create(CultureInfo.InvariantCulture, $"{-spot.Pitch} {yaw} 0")),
                new(RoleKey, nameof(Role.Lamp)),
            ], [], []);
        }

        //---------------------------------------------------------------------
        // Ceiling runs.
        //---------------------------------------------------------------------

        private void CeilingRuns(PropModel run)
        {
            float length = run.Maxs.X - run.Mins.X, half = Math.Max(Math.Abs(run.Mins.Y), Math.Abs(run.Maxs.Y));
            // Runs along each straight stretch of wall, grouped by side and the wall's line.
            var stretches = WallSites(64)
                .GroupBy(s => (s.Dx, s.Dy, Line: s.Dx != 0 ? s.Face.X : s.Face.Y))
                .SelectMany(g => Contiguous(g.OrderBy(s => s.Dx != 0 ? s.Face.Y : s.Face.X).ToList()))
                .Where(s => s.Count * RoomLint.Grid >= length)
                .OrderByDescending(s => s.Count).ThenBy(s => s[0].Face.X).ThenBy(s => s[0].Face.Y).ToList();
            if (stretches.Count == 0)
                return;
            // One or two walls carry runs, not every wall.
            int walls = Math.Min(stretches.Count, 1 + rng.Int(0, 2));
            foreach (var stretch in stretches.Take(walls))
            {
                var s0 = stretch[0];
                bool alongX = s0.Dy != 0;
                float first = alongX ? s0.Face.X - RoomColumns.Probe : s0.Face.Y - RoomColumns.Probe;
                float span = stretch.Count * RoomLint.Grid;
                int pieces = (int)(span / length);
                float start = first + (span - pieces * length) / 2;
                for (int p = 0; p < pieces; p++)
                {
                    float a0 = start + p * length;
                    Point face = alongX ? new(a0, s0.Face.Y, 0) : new(s0.Face.X, a0, 0);
                    float probeX = alongX ? a0 + length / 2 : s0.Face.X - s0.Dx * 8, probeY = alongX ? s0.Face.Y - s0.Dy * 8 : a0 + length / 2;
                    if (solids.CeilingAbove(probeX, probeY, sill + 1) is not { } ceil)
                        continue;
                    // Hung against the ceiling and the wall, a unit off each.
                    float z = ceil - run.Maxs.Z - 1;
                    if (z + run.Mins.Z < sill + CeilingRunFloor)
                        continue;
                    // Along the wall; the model's length along +x turned to the wall's run.
                    int yaw = alongX ? 0 : 90;
                    float offset = half + 1;
                    Point origin = alongX
                        ? new(a0 - run.Mins.X, s0.Face.Y - s0.Dy * offset, z)
                        : new(s0.Face.X - s0.Dx * offset, a0 - run.Mins.X, z);
                    TryProp(run, origin, yaw, Role.CeilingRun, floorStanding: false);
                }
            }
        }

        private static IEnumerable<List<WallSite>> Contiguous(List<WallSite> sites)
        {
            var current = new List<WallSite>();
            foreach (var s in sites)
            {
                if (current.Count > 0)
                {
                    var last = current[^1];
                    float step = Math.Abs(s.Face.X - last.Face.X) + Math.Abs(s.Face.Y - last.Face.Y);
                    if (Math.Abs(step - RoomLint.Grid) > 0.01f)
                    {
                        yield return current;
                        current = [];
                    }
                }

                current.Add(s);
            }

            if (current.Count > 0)
                yield return current;
        }

        //---------------------------------------------------------------------
        // Corners, walls, open floor.
        //---------------------------------------------------------------------

        private void Corners(PropSet props)
        {
            for (int i = 0; i < plan.Size; i++)
                for (int j = 0; j < plan.Size; j++)
                {
                    if (!plan.IsFloor(i, j))
                        continue;
                    foreach (var (dx, dy) in ((int, int)[])[(1, 1), (1, -1), (-1, 1), (-1, -1)])
                    {
                        if (!IsWall(i + dx, j) || !IsWall(i, j + dy) || !plan.IsFloor(i - dx, j) || !plan.IsFloor(i, j - dy))
                            continue;
                        Point p = plan.At(i, j) with { Z = sill + 32 };
                        if (solids.Ray(p, dx, 0) is not { } rx || solids.Ray(p, 0, dy) is not { } ry || rx > RoomLint.Grid || ry > RoomLint.Grid)
                            continue;
                        if (!rng.Chance(props.CornerChance))
                            continue;
                        var corner = new Point(p.X + dx * rx, p.Y + dy * ry, sill);
                        PropModel m = rng.Pick(props.Corner);
                        int yaw = 90 * rng.Int(0, 4);
                        if (!TryInCorner(m, corner, dx, dy, yaw))
                            continue;
                        // A pile: a second piece beside the first along one wall, half the time.
                        if (rng.Chance(0.5f))
                        {
                            var (mins, maxs) = m.Turned(yaw);
                            bool alongX = rng.Chance(0.5f);
                            float shift = (alongX ? maxs.X - mins.X : maxs.Y - mins.Y) + 6;
                            Point next = alongX ? corner with { X = corner.X - dx * shift } : corner with { Y = corner.Y - dy * shift };
                            TryInCorner(rng.Pick(props.Corner), next, dx, dy, 90 * rng.Int(0, 4));
                        }
                    }
                }
        }

        private bool TryInCorner(PropModel m, Point corner, int dx, int dy, int yaw)
        {
            var (mins, maxs) = m.Turned(yaw);
            float x = dx > 0 ? corner.X - 1 - maxs.X : corner.X + 1 - mins.X;
            float y = dy > 0 ? corner.Y - 1 - maxs.Y : corner.Y + 1 - mins.Y;
            return TryProp(m, new Point((float)Math.Round(x, 2), (float)Math.Round(y, 2), sill - mins.Z), yaw, Role.Corner, floorStanding: true, wallDx: dx);
        }

        private void WallClutter(PropSet props)
        {
            var sites = WallSites(48).ToList();
            // A stable shuffle by the room's seed.
            for (int k = sites.Count - 1; k > 0; k--)
            {
                int r = rng.Int(0, k + 1);
                (sites[k], sites[r]) = (sites[r], sites[k]);
            }

            float perimeter = sites.Count * RoomLint.Grid;
            int want = (int)(perimeter / props.WallSpacing);
            var chosen = new List<Point>();
            // A bay between two pilasters first, the piece in its middle (brief p4: pilasters every 288
            // left a sliver beside any piece set down at random, so tall rooms stood bare).
            foreach (var (s0, mid, width) in bays)
            {
                if (chosen.Count >= want)
                    break;
                Point face = s0.Dx != 0 ? s0.Face with { Y = mid } : s0.Face with { X = mid };
                if (chosen.Any(q => Level(q, face) < props.WallSpacing / 2f))
                    continue;
                PropModel m = rng.Pick(props.Wall);
                int yaw = Yaw(-s0.Dx, -s0.Dy) - m.Front;
                var (mins, maxs) = m.Turned(yaw);
                if ((s0.Dx != 0 ? maxs.Y - mins.Y : maxs.X - mins.X) > width - 2 * 56)
                    continue;
                // Centred on the bay: the mesh's middle across the wall on the bay's middle.
                float off = s0.Dx != 0 ? (mins.Y + maxs.Y) / 2 : (mins.X + maxs.X) / 2;
                Point origin = Out(face with { Z = sill - m.Mins.Z }, s0.Dx, s0.Dy, m.Back + 1);
                origin = s0.Dx != 0 ? origin with { Y = mid - off } : origin with { X = mid - off };
                if (TryProp(m, origin, yaw, Role.Wall, floorStanding: true, wallDx: s0.Dx, wallDy: s0.Dy))
                    chosen.Add(face);
            }

            foreach (var s in sites)
            {
                if (chosen.Count >= want)
                    break;
                if (chosen.Any(q => Level(q, s.Face) < props.WallSpacing))
                    continue;
                PropModel m = rng.Pick(props.Wall);
                int yaw = Yaw(-s.Dx, -s.Dy) - m.Front;
                Point origin = Out(s.Face with { Z = sill - m.Mins.Z }, s.Dx, s.Dy, m.Back + 1);
                if (TryProp(m, origin, yaw, Role.Wall, floorStanding: true, wallDx: s.Dx, wallDy: s.Dy))
                    chosen.Add(s.Face);
            }
        }

        /// <summary>
        /// Furniture hung on flat walls at eye height and above (<see cref="PropSet.Hung"/>):
        /// each piece at its own height, its back a unit off the wall, one per
        /// <see cref="PropSet.HungSpacing"/> of wall, clear of doors, lamps and what stands there.
        /// </summary>
        private void HungClutter(PropSet props)
        {
            var sites = WallSites(props.Hung.Min(h => h.Height)).ToList();
            for (int k = sites.Count - 1; k > 0; k--)
            {
                int r = rng.Int(0, k + 1);
                (sites[k], sites[r]) = (sites[r], sites[k]);
            }

            int want = (int)(sites.Count * RoomLint.Grid / props.HungSpacing);
            var chosen = new List<Point>();
            foreach (var s in sites)
            {
                if (chosen.Count >= want)
                    break;
                if (chosen.Any(q => Level(q, s.Face) < props.HungSpacing))
                    continue;
                HungModel h = rng.Pick(props.Hung);
                PropModel m = h.Model;
                float z = sill + h.Height - (m.Mins.Z + m.Maxs.Z) / 2;
                if (z + m.Maxs.Z > room.Height - kit.WallDepth - 8)
                    continue;
                // The wall stands flat behind the whole piece.
                Point probe = s.Face with { Z = sill + h.Height };
                if (solids.Ray(Out(probe, s.Dx, s.Dy, 8), s.Dx, s.Dy) is not { } r || Math.Abs(r - 8) > 0.01f)
                    continue;
                int yaw = Yaw(-s.Dx, -s.Dy) - m.Front;
                Point origin = Out(s.Face with { Z = z }, s.Dx, s.Dy, m.Back + 1);
                if (TryProp(m, origin, yaw, Role.Hung, floorStanding: false, group: false))
                    chosen.Add(s.Face);
            }
        }

        private void FloorClutter(PropSet props, IReadOnlyList<PropModel> models)
        {
            var open = new List<(int I, int J)>();
            int floorColumns = 0;
            for (int i = 0; i < plan.Size; i++)
                for (int j = 0; j < plan.Size; j++)
                {
                    if (!plan.IsFloor(i, j))
                        continue;
                    floorColumns++;
                    if (!lane.Contains((i, j)) && plan.WallDistance(i, j, 5) > 4)
                        open.Add((i, j));
                }

            float area = floorColumns * RoomLint.Grid * RoomLint.Grid;
            // A hallway's floor is its route: clutter stands only on the floor of a room.
            if (IsHallway(room))
                return;
            int want = (int)(props.FloorDensity * area / 1e6f + rng.Fraction());
            // A machine no larger than its share of the room's floor: a generator in a hall, not a chamber.
            var fitting = models.Where(f => (f.Maxs.X - f.Mins.X) * (f.Maxs.Y - f.Mins.Y) <= props.FloorShare * area).ToArray();
            if (fitting.Length == 0)
                return;
            for (int tries = 0; tries < want * 6 && want > 0 && open.Count > 0; tries++)
            {
                var (i, j) = rng.Pick(open);
                PropModel m = rng.Pick(fitting);
                int yaw = 90 * rng.Int(0, 4);
                Point p = plan.At(i, j);
                var origin = new Point((float)Math.Round(p.X), (float)Math.Round(p.Y), sill - m.Mins.Z);
                // Clear of the room's own features by a player's width and more, so a pile never
                // pinches the way between a pillar, a pit's edge or a terrace and itself into a trap.
                var (mins, maxs) = m.Turned(yaw);
                var grown = new Point(FeatureClear, FeatureClear, 0);
                if (solids.Blocked(origin + mins - grown + new Point(0, 0, 1), origin + maxs + grown))
                    continue;
                if (TryProp(m, origin, yaw, Role.Floor, floorStanding: true) && --want == 0)
                    break;
            }
        }

        //---------------------------------------------------------------------
        // Pieces.
        //---------------------------------------------------------------------

        private bool TryProp(PropModel m, Point origin, int yaw, Role role, bool floorStanding, bool group = true, int wallDx = 0, int wallDy = 0)
        {
            if (!m.Static && dynamics >= theme.DynamicBudget)
                return false;
            var (mins, maxs) = m.Turned(yaw);
            var box = new Box(origin + mins, origin + maxs);
            if (!Fits(box, solid: m.Solid, floorStanding, role))
                return false;
            // Clutter keeps off the edges of the room's decks, ledges and pits: a pile beside one walls
            // in the floor under it, which a player drops into and cannot leave (a trap).
            if (m.Solid && role is Role.Wall or Role.Corner or Role.Floor && (NearRaised(box, LedgeClear) || LeavesSliver(box)))
                return false;
            AddProp(m, origin, yaw, role, shadows: role is Role.Wall or Role.Corner or Role.Floor);
            placed.Add((box, role));
            if (group && role is Role.Wall or Role.Corner or Role.Floor)
                Group(box, wallDx, wallDy, role);
            return true;
        }

        /// <param name="needs">The link's <c>room_needs</c> condition the prop is kept under (a static prop, casting no shadow), or null.</param>
        private void AddProp(PropModel m, Point origin, int yaw, Role role, bool shadows, string? needs = null, int roll = 0)
        {
            int y = ((yaw % 360) + 360) % 360;
            string angles = string.Create(CultureInfo.InvariantCulture, $"0 {y} {roll}");
            if (m.Static)
            {
                var keys = new List<KeyValuePair<string, string>>
                {
                    new("model", m.Model), new("angles", angles), new("solid", m.Solid ? "6" : "0"),
                    new("disableshadows", shadows && needs is null ? "0" : "1"), new(RoleKey, role.ToString()),
                };
                if (needs is not null)
                    keys.Add(new("room_needs", needs));
                added.Add(new RoomEntity("prop_static", origin, keys, [], []));
                return;
            }

            dynamics++;
            added.Add(new RoomEntity("prop_dynamic", origin,
                [new("model", m.Model), new("angles", angles), new("solid", "0"), new("DisableShadows", "1"), new(RoleKey, role.ToString())], [], []));
        }

        /// <summary>A floor column against a wall: its wall's face at <paramref name="z"/> above the floor and the way into it.</summary>
        private readonly record struct WallSite(Point Face, int Dx, int Dy);

        private IEnumerable<WallSite> WallSites(float z)
        {
            for (int i = 0; i < plan.Size; i++)
                for (int j = 0; j < plan.Size; j++)
                {
                    if (!plan.IsFloor(i, j))
                        continue;
                    foreach (var (dx, dy) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                    {
                        if (!IsWall(i + dx, j + dy))
                            continue;
                        Point p = plan.At(i, j) with { Z = sill + z };
                        if (solids.Ray(p, dx, dy) is { } r && r <= RoomLint.Grid)
                            yield return new WallSite(new Point(p.X + dx * r, p.Y + dy * r, sill), dx, dy);
                    }
                }
        }

        /// <summary>A wall column: neither floor nor a raised surface (a ledge's side is no wall to lean on).</summary>
        private bool IsWall(int i, int j) => !plan.IsFloor(i, j) && !plan.IsRaised(i, j);

        /// <summary>A point <paramref name="by"/> out from a wall face into the room (the wall is along +d).</summary>
        private static Point Out(Point face, int dx, int dy, float by) => new(face.X - dx * by, face.Y - dy * by, face.Z);

        private static float Level(Point a, Point b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    /// <summary>The air over every raised surface a jump to it needs: each surface's column from its top up <see cref="FloorPlan.LedgeAir"/>.</summary>
    public static IEnumerable<Box> LedgeAir(FloorPlan plan)
    {
        foreach (RoomColumns.Surface s in plan.Raised)
        {
            float x = s.I * RoomLint.Grid, y = s.J * RoomLint.Grid;
            yield return new Box(new(x, y, s.Z), new(x + RoomLint.Grid, y + RoomLint.Grid, s.Z + Math.Min(FloorPlan.LedgeAir, s.Clear)));
        }
    }

    /// <summary>
    /// How far, level, clutter keeps from a jump ledge (a surface above the critical path's reach) in a
    /// jump room: the take-off patch under or beside it, and the air between it and the ledge (user
    /// ruling 2026-11-10: "dont need to keep floor totally clear for rocket jumping").
    /// </summary>
    public const int TakeOff = 128;

    /// <summary>
    /// Whether <paramref name="box"/> stands where a jump in a jump room needs the air: the room's entry
    /// says jump, the box reaches below the floor plus <see cref="JumpTable.RocketClearAir"/>, it covers
    /// open floor (more than six columns, 96, from a wall), and it stands within <see cref="TakeOff"/>
    /// of a ledge only a jump reaches. The rest of a jump room's floor takes clutter as any room's does
    /// (user ruling 2026-11-10); the ledges' own air is <see cref="FloorPlan.LedgeAir"/>'s, and a jump
    /// room's brushes still keep the plan's clear air (<see cref="RoomLint.CheckJumpClearAir"/>).
    /// </summary>
    public static bool JumpAirRefuses(Room room, FloorPlan plan, Box box)
    {
        float sill = room.Kit.Sill, grid = RoomLint.Grid;
        return room.Entry.Jump && box.Mins.Z < sill + JumpTable.RocketClearAir
               && plan.Covered(box).Any(c => plan.IsFloor(c.Item1, c.Item2) && plan.WallDistance(c.Item1, c.Item2, 6) > 6)
               && plan.JumpLedges.Any(s => box.LevelDistanceTo(s.I * grid + grid / 2, s.J * grid + grid / 2) < TakeOff);
    }
}
