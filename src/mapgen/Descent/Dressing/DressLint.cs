using System.Globalization;

namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// The dressing lint (task A10): the rules a dressed room keeps, each read
/// from the dressed room against the room the shapes built, never from what
/// the dresser says it did. A dressing piece is an entity carrying
/// <see cref="RoomDresser.RoleKey"/> or a brush the dressed room has that
/// the built one has not. Each rule has a refusing fixture in
/// <c>DescentDressLintTests</c>.
/// </summary>
public static class DressLint
{
    public const string DoorClear = "dress_door_clear";
    public const string MarkerClear = "dress_marker_clear";
    public const string GiantRoute = "dress_giant_route";
    public const string JumpAir = "dress_jump_air";
    public const string InBrush = "dress_in_brush";
    public const string Supported = "dress_supported";
    public const string CeilingRun = "dress_ceiling_run";
    public const string StaticOnly = "dress_static";
    public const string LampLight = "dress_lamp_light";
    public const string CutOff = "dress_cut_off";
    public const string CellFace = "dress_cell_face";
    public const string Breakable = "dress_breakable";
    public const string KeepClear = "dress_keep_clear";
    public const string LampSite = "dress_lamp_site";

    /// <summary>How far a lamp's light may stand from its fixture.</summary>
    public const int LightReach = 100;

    /// <summary>A dressing piece: its box, what it is, and whether it is solid.</summary>
    public sealed record Piece(string What, Box Box, RoomDresser.Role? Role, bool Solid, RoomEntity? Entity);

    /// <summary>
    /// The part of a piece that must stand clear of the room's brushes: all of it, but for a
    /// doorway's track (kept only where its door is joined, <c>room_needs joined_&lt;wall&gt;</c>),
    /// which crosses the wall's depth into the door's plug, so its part beyond the wall's face.
    /// </summary>
    private static (Point Mins, Point Maxs) Inside(Piece p, Room room)
    {
        // The wall's depth and the panel a capped door is closed with (which the link drops where it keeps this piece).
        float t = room.Kit.WallDepth + RoomBuilder.CapThickness, c = room.Extent;
        bool doorway = p.Box.Mins.X <= 0 || p.Box.Mins.Y <= 0 || p.Box.Maxs.X >= c || p.Box.Maxs.Y >= c;
        if (p.Role != RoomDresser.Role.Rail || !doorway)
            return (p.Box.Mins, p.Box.Maxs);
        Point lo = p.Box.Mins, hi = p.Box.Maxs;
        if (lo.X <= 0) lo = lo with { X = Math.Max(lo.X, t) };
        if (hi.X >= c) hi = hi with { X = Math.Min(hi.X, c - t) };
        if (lo.Y <= 0) lo = lo with { Y = Math.Max(lo.Y, t) };
        if (hi.Y >= c) hi = hi with { Y = Math.Min(hi.Y, c - t) };
        return (lo, hi);
    }

    /// <summary>Every finding for the dressed room, rule by rule; at most <paramref name="breakableCap"/> breakables (<see cref="Breakables"/>).</summary>
    public static IReadOnlyList<LintFinding> Check(Room built, Room dressed, RoomTheme theme, int breakableCap = Breakables.RoomCap)
    {
        var findings = new List<LintFinding>();
        var models = Models(theme);
        var pieces = Pieces(built, dressed, models, findings);
        var solids = new RoomSolids(built);
        var plan = new FloorPlan(built);
        var air = RoomDresser.LedgeAir(plan).ToArray();
        RoomKit kit = built.Kit;
        float sill = kit.Sill;
        var doors = built.Sockets.Select(s => (Name: built.SocketName(s.Face, s.Offset), Box: Box.DoorOpening(kit, s.Face, s.Offset, built.Cells))).ToArray();
        var markers = built.Entities.Where(e => e.ClassName == "info_poi" && e.Origin is not null).Select(e => (Type: e.Key("poi_type"), At: e.Origin!.Value)).ToArray();
        void Add(string rule, string message) => findings.Add(new LintFinding(rule, dressed.Name, message));
        // Whether the dressed room's brushes keep a jump room's clear air (RoomLint's own measure).
        var keepsAir = new Lazy<bool>(() =>
        {
            Room walls = built with { World = dressed.World };
            return !RoomLint.CheckJumpClearAir(walls, new RoomColumns(walls)).Any();
        });

        // A shape's set piece that is not solid (a boss arena's steel headframe) takes no one's way, air or track:
        // it is held to standing in the cell, on something, out of the brushes, never to the clearances.
        bool Frame(Piece p) => !p.Solid && p.Entity?.Origin is { } o && built.SetPieces.Any(sp => !sp.Model.Solid && sp.Model.Model == p.Entity.Key("model")
                                                                                         && Math.Abs(sp.Origin.X - o.X) < 0.5f && Math.Abs(sp.Origin.Y - o.Y) < 0.5f);
        foreach (Piece p in pieces)
        {
            // Track is flat and not solid: it runs on its door's axis up to the doorway, so it lines up from room to
            // room, and under a marker, which stands on it as on the floor.
            bool flat = p.Role == RoomDresser.Role.Rail && !p.Solid && p.Box.Maxs.Z <= sill + RoomDresser.RailStopHeight;
            // A brush skin no thicker than paint (a hazard stripe along a ledge's edge) takes no one's air or room.
            bool skin = p.Role == RoomDresser.Role.Set && Math.Min(p.Box.Maxs.X - p.Box.Mins.X, p.Box.Maxs.Y - p.Box.Mins.Y) <= 2;
            flat |= skin || Frame(p);
            // A brush piece over a door's head (the Mill's hazard stripe) stands above the passage.
            bool overHead = p.Role == RoomDresser.Role.Set && p.Box.Mins.Z >= kit.Head;
            foreach (var d in doors.Where(d => !flat && !overHead && d.Box.DistanceTo(p.Box) < RoomDresser.DoorClear))
                Add(DoorClear, $"{p.What} is {d.Box.DistanceTo(p.Box):0.#} from its {d.Name} door's opening; dressing keeps {RoomDresser.DoorClear} clear");
            // A breakable the shape stood on its own marker (level 5's maze cells, brief p18) is that marker, not a piece beside it.
            bool Own((string? Type, Point At) m) => p.Entity is { } pe && Breakables.Is(pe) && pe.Origin == m.At && pe.Key("poi_type") == m.Type;
            foreach (var m in markers.Where(m => !flat && !Own(m) && p.Box.Mins.Z < m.At.Z + RoomLint.HullHeight && p.Box.Maxs.Z > m.At.Z - 1 && p.Box.LevelDistanceTo(m.At.X, m.At.Y) < RoomDresser.MarkerClear))
                Add(MarkerClear, $"{p.What} stands {p.Box.LevelDistanceTo(m.At.X, m.At.Y):0.#} from the {m.Type} marker at ({m.At}); dressing keeps {RoomDresser.MarkerClear} clear");
            // A centrepiece stands on a hall's plinth, in the air of no surface a jump needs (T1 at most).
            bool centre = p.Role == RoomDresser.Role.Centre;
            if (!skin && !Frame(p) && air.FirstOrDefault(a => a.Overlaps(p.Box) && (!centre || a.Mins.Z > sill + JumpTable.CriticalPathReach)) is { } hit && hit != default)
                Add(JumpAir, $"{p.What} stands in the {FloorPlan.LedgeAir} of air over the surface at {hit.Mins.Z - sill} above the floor near ({hit.Centre.X} {hit.Centre.Y})");
            else if (!flat && RoomDresser.JumpAirRefuses(built, plan, p.Box) && !(p.Entity is null && keepsAir.Value))
                // A set piece of brushes (a hall's headframe) may stand there only while the room keeps the
                // plan's clear air measured from its brushes (§4.2, brief p4); a prop never.
                Add(JumpAir, $"{p.What} stands on the open floor of a jump room below {JumpTable.RocketClearAir}");
            float c = built.Extent;
            if (p.Box.Mins.X < 0 || p.Box.Mins.Y < 0 || p.Box.Mins.Z < 0 || p.Box.Maxs.X > c || p.Box.Maxs.Y > c || p.Box.Maxs.Z > built.Height)
                Add(InBrush, $"{p.What} ({p.Box.Mins})-({p.Box.Maxs}) leaves the cell");
            // A curve of track is checked along its line by the dresser (its box spans a bend's inner corner).
            // A hollow frame's box holds what it stands over (the lift's doorway and track); its own feet are in the cell.
            else if (p.Role != RoomDresser.Role.Set && !Frame(p) && !(flat && p.What.Contains("_arc_", StringComparison.Ordinal))
                     && Inside(p, built) is var (lo, hi) && solids.Blocked(lo, hi))
                Add(InBrush, $"{p.What} ({p.Box.Mins})-({p.Box.Maxs}) stands in a brush of the room");
            // Held by the room, by a set it hangs from, by a piece it stands on (a lantern on its crate), or by
            // a cord it hangs from (a pendant's lamp under its cord, each cord piece under the next).
            bool standsOn = pieces.Any(q => q != p && q.Role != RoomDresser.Role.Set
                                            && (Math.Abs(q.Box.Maxs.Z - p.Box.Mins.Z) <= 2 || (q.Role == RoomDresser.Role.Cord && Math.Abs(q.Box.Mins.Z - p.Box.Maxs.Z) <= 2))
                                            && q.Box.Mins.X < p.Box.Maxs.X && q.Box.Maxs.X > p.Box.Mins.X && q.Box.Mins.Y < p.Box.Maxs.Y && q.Box.Maxs.Y > p.Box.Mins.Y);
            if (p.Role != RoomDresser.Role.Set && !standsOn && !solids.Touches(p.Box.Mins, p.Box.Maxs) && !pieces.Any(q => q != p && q.Role == RoomDresser.Role.Set && q.Box.DistanceTo(p.Box) <= 2))
                Add(Supported, $"{p.What} ({p.Box.Mins})-({p.Box.Maxs}) floats: no floor, wall or ceiling within 2 of it");
            if (p.Role == RoomDresser.Role.CeilingRun && p.Box.Mins.Z < sill + RoomDresser.CeilingRunFloor)
                Add(CeilingRun, $"{p.What}'s underside is {p.Box.Mins.Z - sill} above the floor; a ceiling run hangs above {RoomDresser.CeilingRunFloor}");
        }

        // What a shape keeps clear (the Tank's track) no dressing stands in.
        foreach (Piece p in pieces.Where(p => !Frame(p)))
            foreach (var (lo, hi) in built.KeepClear.Where(k => new Box(k.Mins, k.Maxs).Overlaps(p.Box)))
                Add(KeepClear, $"{p.What} ({p.Box.Mins})-({p.Box.Maxs}) stands in the box ({lo})-({hi}) the room keeps clear");

        // A room that lights itself has every lamp it asked for.
        if (built.OwnLamps)
            foreach (var (kind, origin, _) in built.LampSites.Where(s => RoomDresser.SiteOrigin(s.Kind, s.Origin, s.Yaw) is var want
                                                                        && !pieces.Any(p => p.Role == RoomDresser.Role.Lamp && p.Entity?.Key("model") == s.Kind.Fixture.Model
                                                                                            && p.Entity.Origin is { } o && Math.Abs(o.X - want.X) < 0.5f && Math.Abs(o.Y - want.Y) < 0.5f)))
                Add(LampSite, $"the {kind.Fixture.Model} the room asks for at ({origin}) was not hung: it does not fit there");

        // A larger room's dressing brushes keep off its internal cell faces, as its shape's do (the tools refuse a face on one).
        foreach (Piece p in pieces.Where(p => p.Entity is null))
            for (int k = 1; k < built.Cells; k++)
                foreach (float v in (float[])[p.Box.Mins.X, p.Box.Maxs.X, p.Box.Mins.Y, p.Box.Maxs.Y])
                    if (Math.Abs(v - k * kit.Cell) < 0.01f)
                        Add(CellFace, $"{p.What} ({p.Box.Mins})-({p.Box.Maxs}) has a face on the internal cell face at {k * kit.Cell}");

        // The giant's route between doors, kept if the shapes kept it.
        if (!plan.RouteBroken())
        {
            var dressedPlan = new FloorPlan(dressed with { Entities = [.. dressed.Entities.Where(e => e.ClassName != "prop_static" && e.ClassName != "prop_dynamic")] });
            var blocked = new HashSet<(int, int)>();
            foreach (Piece p in pieces.Where(p => p.Solid && p.Role != RoomDresser.Role.Set && p.Box.Mins.Z < sill + FloorPlan.GiantHeight && p.Box.Maxs.Z > sill + RoomLint.Step))
                foreach (var col in dressedPlan.Covered(p.Box))
                    blocked.Add(col);
            if (dressedPlan.RouteBroken(blocked))
                Add(GiantRoute, $"the dressing cuts the {FloorPlan.GiantWidth} by {FloorPlan.GiantHeight} route a giant has between the room's doors");
        }

        // Every floor a giant walked to before the dressing it still walks to, but the floor
        // beside a piece: a bay or vault walled off by props is floor nobody reaches.
        if (CutOffFloor(built, dressed, pieces) is { Count: > 0 } lost)
            Add(CutOff, $"{lost.Count} columns of floor a giant walked to are cut off by the dressing (one at column {lost[0].I} {lost[0].J})");
        if (CutOffWalk(built, dressed, pieces) is { Count: > 0 } walk)
            Add(CutOff, $"{walk.Count} columns of floor a player walked to are cut off by the dressing (one at {walk[0].X} {walk[0].Y} {walk[0].Z})");

        // Static wherever the model allows; dynamics within the theme's budget.
        int dynamic = 0;
        foreach (Piece p in pieces.Where(p => p.Entity is not null && p.Entity.ClassName is "prop_static" or "prop_dynamic"))
        {
            string model = p.Entity!.Key("model") ?? "";
            if (!models.TryGetValue(model, out PropModel? m))
                continue;
            if (p.Entity.ClassName == "prop_static" && !m.Static)
                Add(StaticOnly, $"{model} is a prop_static, but the model cannot be static");
            if (p.Entity.ClassName == "prop_dynamic")
            {
                dynamic++;
                if (m.Static)
                    Add(StaticOnly, $"{model} is a prop_dynamic, but the model can be static (an entity spent for nothing)");
            }
        }

        if (dynamic > theme.DynamicBudget)
            Add(StaticOnly, $"{dynamic} prop_dynamics, over the theme's budget of {theme.DynamicBudget}");

        // Every lamp has its light and every light its lamp.
        // A theme's cool fill (RoomTheme.CoolFill) is light with no fixture, by design.
        var lights = dressed.Entities.Where(e => e.ClassName is "light" or "light_spot" && e.Key(RoomDresser.RoleKey) is { } role
                                                 && role != nameof(RoomDresser.Role.Fill)).ToArray();
        var fixtures = pieces.Where(p => p.Role == RoomDresser.Role.Lamp).ToArray();
        foreach (RoomEntity l in lights.Where(l => !fixtures.Any(f => f.Box.DistanceTo(new Box(l.Origin!.Value, l.Origin!.Value)) <= LightReach)))
            Add(LampLight, $"a {l.ClassName} at ({l.Origin}) has no lamp within {LightReach}");
        // A failed lamp (LampKind.Dead, level 11) hangs dark by design: it, and only it, gives no light.
        foreach (Piece f in fixtures.Where(f => f.Entity?.Key(RoomDresser.DeadKey) != "1" && !lights.Any(l => f.Box.DistanceTo(new Box(l.Origin!.Value, l.Origin!.Value)) <= LightReach)))
            Add(LampLight, $"{f.What} gives no light: no light within {LightReach}");
        if (theme.Lamps is not null && dressed.Entities.Any(e => e.ClassName is "light" or "light_spot" && e.Key(RoomDresser.RoleKey) is null))
            Add(LampLight, "a shape's own light stays in a room the theme lights");

        // Breakables (M34): under the room's cap, turned by yaw alone, and each where the room compile's
        // standing agent fits at its point (it refuses a point of interest where it does not); every other
        // rule above holds one as the solid it is until broken.
        var breakables = Breakables.Of(dressed);
        if (breakables.Count > breakableCap)
            Add(Breakable, $"{breakables.Count} breakables, over the room's cap of {breakableCap}");
        var dressedSolids = new RoomSolids(dressed);
        var navProps = pieces.Where(p => p.Solid && p.Entity?.ClassName == "prop_static").Select(p => p.Box).ToArray();
        foreach (RoomEntity b in breakables)
        {
            string[] angles = (b.Key("angles") ?? "").Split(' ');
            if (angles.Length != 3 || angles[0] != "0" || angles[2] != "0")
                Add(Breakable, $"the {b.Key("poi_type")} at ({b.Origin}) is turned \"{b.Key("angles")}\", not by its yaw alone");
            if (Breakables.AgentBlocked(Breakables.AgentSweep(b.Origin!.Value), dressedSolids, navProps))
                Add(Breakable, $"the {b.Key("poi_type")} at ({b.Origin}): the room compile's standing agent ({2 * Breakables.AgentHalfWidth} by {Breakables.AgentHeight}) does not fit in its voxel, so it refuses the point");
        }

        return findings;
    }

    /// <summary>
    /// The floor a giant (92 by 156, <see cref="FloorPlan.GiantFits"/>) walks
    /// to from the doors of <paramref name="built"/> that it no longer reaches
    /// in <paramref name="dressed"/> with the solid pieces' columns blocked,
    /// but within the giant's reach and a column of a piece (floor a piece
    /// stands on or beside): a bay or vault the dressing walled off. A giant's
    /// hull is held, not a Heavy's, because a prop's collision is wider than
    /// its mesh box in places and the preview's walker finds what the box
    /// misses (a corner pile in a vault's mouth, depth 4).
    /// </summary>
    public static IReadOnlyList<(int I, int J)> CutOffFloor(Room built, Room dressed, IReadOnlyList<Piece> pieces)
    {
        float sill = built.Kit.Sill;
        var solid = pieces.Where(p => p.Solid && p.Role != RoomDresser.Role.Set && p.Box.Mins.Z < sill + FloorPlan.GiantHeight && p.Box.Maxs.Z > sill + RoomLint.Step).ToArray();
        if (solid.Length == 0)
            return [];
        var before = new FloorPlan(built).GiantReached();
        var dressedPlan = new FloorPlan(dressed with { Entities = [.. dressed.Entities.Where(e => e.ClassName is not ("prop_static" or "prop_dynamic"))] });
        var blocked = new HashSet<(int, int)>();
        foreach (Piece p in solid)
            foreach (var col in dressedPlan.Covered(p.Box))
                blocked.Add(col);
        var after = dressedPlan.GiantReached(blocked);
        var beside = new HashSet<(int, int)>(blocked);
        foreach (Piece p in pieces.Where(p => p.Role == RoomDresser.Role.Set))
            foreach (var col in dressedPlan.Covered(p.Box))
                beside.Add(col);
        int reach = FloorPlan.GiantReach + 1;
        bool Near((int I, int J) c) => beside.Any(b => Math.Abs(b.Item1 - c.I) <= reach && Math.Abs(b.Item2 - c.J) <= reach);
        return [.. before.Where(c => !after.Contains(c) && !Near(c)).OrderBy(c => c.I).ThenBy(c => c.J)];
    }

    /// <summary>
    /// The floor of <paramref name="built"/> a standing player (48 by 82)
    /// walks to from its doors on steps that it no longer walks to in
    /// <paramref name="dressed"/> with its solid pieces standing as boxes,
    /// but within a hull's half and a column of a piece: a pocket between a
    /// pile and a pillar the giant's rule cannot see, because no giant fits
    /// there to begin with.
    /// </summary>
    public static IReadOnlyList<RoomColumns.Surface> CutOffWalk(Room built, Room dressed, IReadOnlyList<Piece> pieces)
    {
        float sill = built.Kit.Sill;
        var solid = pieces.Where(p => p.Solid && p.Role != RoomDresser.Role.Set && p.Box.Maxs.Z > sill + RoomLint.Step).ToArray();
        if (solid.Length == 0)
            return [];
        Room blocked = dressed with { World = [.. dressed.World, .. solid.Select(p => Brush.Box(p.Box.Mins, p.Box.Maxs, new Surface("TOOLS/TOOLSCLIP")))] };
        var after = RoomLint.WalkedFloor(blocked, new RoomColumns(blocked)).Select(s => s.Key).ToHashSet();
        float margin = RoomLint.HullWidth / 2f + RoomLint.Grid;
        var near = pieces.Select(p => new Box(p.Box.Mins - new Point(margin, margin, 0), p.Box.Maxs + new Point(margin, margin, 0))).ToArray();
        return [.. RoomLint.WalkedFloor(built, new RoomColumns(built))
            .Where(s => !after.Contains(s.Key) && !near.Any(b => b.LevelDistanceTo(s.X, s.Y) <= 0))];
    }

    /// <summary>Every model a theme places, by path (its own, its tall lamps' and its arrival's).</summary>
    public static IReadOnlyDictionary<string, PropModel> Models(RoomTheme theme)
    {
        var all = new Dictionary<string, PropModel>(StringComparer.Ordinal);
        foreach (RoomTheme? t in (RoomTheme?[])[theme, theme.Arrival, theme.Caves])
        {
            if (t is null)
                continue;
            foreach (LampKind? k in (LampKind?[])[t.Lamps, t.TallLamps, t.CoverLamps, t.WorkLamps?.Kind, t.Pendants, t.RoutePools, .. t.ArenaLamps])
                foreach (PropModel? m in (PropModel?[])[k?.Fixture, k?.Stand, .. k?.Cords ?? []])
                    if (m is not null)
                        all.TryAdd(m.Model, m);
            foreach (PropModel? m in (PropModel?[])[t.Gantry?.Hook, t.Gantry?.Cable])
                if (m is not null)
                    all.TryAdd(m.Model, m);
            foreach (PropModel m in t.Props.Models.Concat(t.SetPieces))
                all.TryAdd(m.Model, m);
        }

        return all;
    }

    /// <summary>The dressing pieces of <paramref name="dressed"/>: its props carrying the role key, and the brushes <paramref name="built"/> has not.</summary>
    public static IReadOnlyList<Piece> Pieces(Room built, Room dressed, IReadOnlyDictionary<string, PropModel> models, List<LintFinding>? findings = null)
    {
        var pieces = new List<Piece>();
        var shell = new HashSet<Brush>(built.World, ReferenceEqualityComparer.Instance);
        foreach (Brush b in dressed.World.Where(b => !shell.Contains(b)))
            pieces.Add(new Piece($"a {b.Material} set brush", new Box(b.Mins, b.Maxs), RoomDresser.Role.Set, true, null));
        foreach (RoomEntity e in dressed.Entities.Where(e => e.ClassName is "prop_static" or "prop_dynamic" && e.Key(RoomDresser.RoleKey) is not null))
        {
            string model = e.Key("model") ?? "";
            if (!models.TryGetValue(model, out PropModel? m))
            {
                findings?.Add(new LintFinding(StaticOnly, dressed.Name, $"{model} is not a model of the theme"));
                continue;
            }

            int yaw = (int)float.Parse((e.Key("angles") ?? "0 0 0").Split(' ')[1], CultureInfo.InvariantCulture);
            var (mins, maxs) = m.Turned(yaw);
            Point o = e.Origin!.Value;
            RoomDresser.Role? role = Enum.TryParse(e.Key(RoomDresser.RoleKey), out RoomDresser.Role r) ? r : null;
            bool solid = e.ClassName == "prop_static" && e.Key("solid") != "0";
            pieces.Add(new Piece($"{e.ClassName} {model} at ({o})", new Box(o + mins, o + maxs), role, solid, e));
        }

        // A breakable is the server's crate or barrel, solid until broken.
        foreach (RoomEntity e in dressed.Entities.Where(Breakables.Is))
        {
            RoomDresser.Role? role = Enum.TryParse(e.Key(RoomDresser.RoleKey), out RoomDresser.Role r) ? r : null;
            pieces.Add(new Piece($"{e.Key("poi_type")} at ({e.Origin})", Breakables.BoxOf(e), role, true, e));
        }

        return pieces;
    }
}
