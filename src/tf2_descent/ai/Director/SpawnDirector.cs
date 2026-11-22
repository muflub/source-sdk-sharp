using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Horde;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Ai.Director;

/// <summary>
/// The spawn director (plan_descent_logic HO9; plan_tf2_rpg 2f, 5d): the one place that decides
/// a level's roster, from (depth, seed, party size, the level's rooms, pack markers with their
/// clearances, and doors). Pure and deterministic: no engine, no clock.
/// <list type="bullet">
/// <item>The boss, if the level has one, first, its own pack of one at its spot.</item>
/// <item>Then each room with pack markers, in id order, holds one pack, rolled by
/// <see cref="PackRules.RollPack"/> with this room's fit (<see cref="RoomFit"/>): a robot
/// stands only at a marker whose clearance holds its scaled hull, in a room it can leave by a
/// door its hull fits (and, for a floor-bound size, a door on its own floor).</item>
/// <item>A pack the roster's capacity cannot take whole is dropped whole (and named).</item>
/// <item>Each robot gets a post of its own round its marker (<see cref="Posts"/>), so a pack
/// does not start inside itself.</item>
/// </list>
/// </summary>
public sealed class SpawnDirector
{
    private readonly TF2DescentRules _rules;

    public SpawnDirector(TF2DescentRules rules) => _rules = rules;

    private DirectorSettings S => _rules.Content.Horde.Director;

    /// <summary>The standing hull's half-width at <paramref name="scale"/> (robot_rules.json's push hull, as the bodies are bounded).</summary>
    public float HalfWidth(double scale) => (float)(_rules.Content.RobotRules.Push.HullWidth / 2 * scale);

    /// <summary>The standing hull's height at <paramref name="scale"/>.</summary>
    public float Height(double scale) => (float)(_rules.Content.RobotRules.Push.HullHeight * scale);

    /// <summary>Whether a robot of <paramref name="template"/>'s size class keeps to the floor it stands on (cannot climb stairs).</summary>
    public bool FloorBound(RobotTemplate template) => S.FloorBoundSizes.Contains(template.Size);

    /// <summary>
    /// The level's roster for a party of <paramref name="partySize"/>; with <paramref name="reach"/>
    /// (the level's navigation, when the server has it), a robot also stands only where its hull's
    /// floor joins the rest of the level's.
    /// </summary>
    public LevelPlan Plan(HordeLevel level, int partySize, IHullReach? reach = null)
    {
        int capacity = _rules.Content.Horde.RosterCapacity;
        int party = Math.Clamp(partySize, 1, _rules.Content.RobotRules.Party.MaxMembers);
        var packs = ImmutableArray.CreateBuilder<PlannedPack>();
        var dropped = ImmutableArray.CreateBuilder<DroppedPack>();
        int count = 0;
        int monsterLevel = _rules.Robots.MonsterLevel(level.Depth);
        var reserved = new List<(int Room, AiVector At, float Half)>();

        if (level.Boss is { } boss)
        {
            var t = _rules.Robots.Template(boss.Template);
            if (!t.Boss) throw new ArgumentException($"'{boss.Template}' is not a boss template", nameof(level));
            ulong seed = Seeded.From(level.Seed, "boss", level.Depth, boss.Template).NextULong();
            double scale = RobotRules.RollScale(t, seed, 0);
            var stats = _rules.Robots.Stats(t, scale, monsterLevel, [], party);
            var temperament = _rules.Temperaments.Roll(seed, 0, t, scale);
            if (capacity < 1) dropped.Add(new DroppedPack(boss.ArenaRoom, 1));
            else
            {
                var roll = new RolledRobot(0, t.Id, scale, [], stats, temperament, -1);
                packs.Add(new PlannedPack(boss.ArenaRoom, seed,
                    [new PlannedRobot(roll, boss.ArenaRoom, -1, boss.Spot.Position, boss.Spot.Yaw, Seeded.From(seed, "robot", 0).NextULong(), true, false)],
                    0, 0, 0));
                reserved.Add((boss.ArenaRoom, boss.Spot.Position, HalfWidth(scale)));
                count = 1;
            }
        }

        int unmeasured = 0;
        var emptied = ImmutableArray.CreateBuilder<DroppedPack>();
        foreach (var room in level.Rooms)
        {
            if (room.Markers.IsEmpty) continue;
            unmeasured += room.Markers.Count(m => m.Clearance is null);
            var pack = _rules.Packs.RollPack(level.Seed, room.Id, level.Depth, party, room.Markers.Length, new RoomFit(this, level, room, reach));
            if (pack.Robots.IsEmpty)
            {
                if (pack.Unplaced > 0) emptied.Add(new DroppedPack(room.Id, pack.Unplaced));
                continue;
            }
            if (count + pack.Robots.Length > capacity)
            {
                dropped.Add(new DroppedPack(room.Id, pack.Robots.Length));
                continue;
            }
            var posts = Posts(room, pack.Robots, reserved);
            var robots = ImmutableArray.CreateBuilder<PlannedRobot>(pack.Robots.Length);
            for (int k = 0; k < pack.Robots.Length; k++)
            {
                var r = pack.Robots[k];
                robots.Add(new PlannedRobot(r, room.Id, r.Marker, posts[k].At, room.Markers[r.Marker].Yaw,
                    Seeded.From(pack.Seed, "robot", r.Index).NextULong(), false, posts[k].Crowded));
            }
            packs.Add(new PlannedPack(room.Id, pack.Seed, robots.MoveToImmutable(), pack.StyleRerolls, pack.Refits, pack.Unplaced));
            count += pack.Robots.Length;
        }

        return new LevelPlan(level.Depth, level.Seed, party, packs.ToImmutable(), dropped.ToImmutable(), emptied.ToImmutable(), unmeasured);
    }

    //-------------------------------------------------------------------------
    // What a room holds
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether a robot of <paramref name="t"/> at <paramref name="scale"/> stands at
    /// <paramref name="marker"/> of <paramref name="room"/> and can leave the room from there:
    /// the marker's clearance holds its scaled hull (an unmeasured marker holds anything), and a
    /// door of the room is wide enough for the hull with the director's margin on each side,
    /// tall enough, and, for a floor-bound size, has its sill within a step of the marker's floor.
    /// A room with no door is left by nothing, so holds nothing, unless it is the level's only room.
    /// With <paramref name="reach"/>, the hull's floor at the marker must also join the arrival's
    /// (<see cref="IHullReach.Joins"/>): a room's doors can fit a hull the floor beyond them does not.
    /// </summary>
    public bool Holds(HordeLevel level, HordeRoom room, int marker, RobotTemplate t, double scale, IHullReach? reach = null)
    {
        var m = room.Markers[marker];
        float half = HalfWidth(scale), height = Height(scale);
        if (m.Clearance is { } c && !c.Holds(half, height)) return false;
        if (!Leaves(level, room, m.Position, t, half, height)) return false;
        return reach is null || reach.Joins(room.Id, m.Position, scale);
    }

    private bool Leaves(HordeLevel level, HordeRoom room, AiVector from, RobotTemplate t, float half, float height)
    {
        var doors = level.DoorsOf(room.Id);
        if (doors.IsEmpty) return level.RoomCount == 1;
        bool floorBound = FloorBound(t);
        foreach (int d in doors)
        {
            var door = level.Doors[d];
            if (door.Width - 2 * S.DoorMargin < 2 * half || door.Height < height) continue;
            if (floorBound && MathF.Abs(door.Position.Z - from.Z) > S.StepHeight) continue;
            return true;
        }
        return false;
    }

    /// <summary>One room's fit for the pack roll.</summary>
    private sealed class RoomFit(SpawnDirector director, HordeLevel level, HordeRoom room, IHullReach? reach) : IPackFit
    {
        public bool Fits(RobotTemplate template, double scale, int marker) => director.Holds(level, room, marker, template, scale, reach);
    }

    //-------------------------------------------------------------------------
    // Posts
    //-------------------------------------------------------------------------

    /// <summary>
    /// Each robot's post (plan_descent_logic 4.7: a pack lent on one marker started inside
    /// itself): on a square grid round its marker, one cell the pack's widest hull plus the
    /// director's gap, tried ring by ring (the marker first, then up to <c>postRings</c> rings,
    /// each walked from the east side anticlockwise), the first that is inside the room's bounds,
    /// inside the marker's clearance and clear of every post already taken in the room (the
    /// boss's too) by the gap. A robot for which none is free stands on its marker, crowded.
    /// </summary>
    public (AiVector At, bool Crowded)[] Posts(HordeRoom room, ImmutableArray<RolledRobot> robots,
                                              List<(int Room, AiVector At, float Half)> reserved)
    {
        float gap = (float)S.PostGap;
        float widest = 0;
        foreach (var r in robots) widest = MathF.Max(widest, HalfWidth(r.Scale));
        float step = 2 * widest + gap;
        var taken = reserved.Where(x => x.Room == room.Id).Select(x => (x.At, x.Half)).ToList();
        var posts = new (AiVector At, bool Crowded)[robots.Length];

        for (int k = 0; k < robots.Length; k++)
        {
            var marker = room.Markers[robots[k].Marker];
            float half = HalfWidth(robots[k].Scale);
            AiVector? found = null;
            for (int ring = 0; ring <= S.PostRings && found is null; ring++)
                foreach (var (i, j) in Ring(ring))
                {
                    var at = new AiVector(marker.Position.X + i * step, marker.Position.Y + j * step, marker.Position.Z);
                    if (!Inside(room, at, half) || !InClearance(marker, i * step, j * step, half) || Overlaps(taken, at, half, gap))
                        continue;
                    found = at;
                    break;
                }

            posts[k] = found is { } p ? (p, false) : (marker.Position, true);
            taken.Add((posts[k].At, half));
        }
        return posts;
    }

    /// <summary>The cells of ring <paramref name="r"/> of a square grid: (0, 0) for ring 0; else its perimeter from (r, 0), anticlockwise.</summary>
    public static IEnumerable<(int I, int J)> Ring(int r)
    {
        if (r == 0)
        {
            yield return (0, 0);
            yield break;
        }
        for (int j = 0; j <= r; j++) yield return (r, j);
        for (int i = r - 1; i >= -r; i--) yield return (i, r);
        for (int j = r - 1; j >= -r; j--) yield return (-r, j);
        for (int i = -r + 1; i <= r; i++) yield return (i, -r);
        for (int j = -r + 1; j < 0; j++) yield return (r, j);
    }

    private static bool Inside(HordeRoom room, AiVector at, float half) =>
        at.X - half >= room.Min.X && at.X + half <= room.Max.X && at.Y - half >= room.Min.Y && at.Y + half <= room.Max.Y;

    private static bool InClearance(PackMarker marker, float dx, float dy, float half) =>
        marker.Clearance is not { } c
        || (dx - half >= -c.West && dx + half <= c.East && dy - half >= -c.South && dy + half <= c.North);

    private static bool Overlaps(List<(AiVector At, float Half)> taken, AiVector at, float half, float gap)
    {
        foreach (var (other, h) in taken)
        {
            float apart = h + half + gap;
            if (MathF.Abs(other.X - at.X) < apart && MathF.Abs(other.Y - at.Y) < apart) return true;
        }
        return false;
    }
}
