using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Director;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Levels for the spawn director's property facts (plan_descent_logic HO9: "no generated pack
/// breaks the rules over 10k seeds"): three 1024-unit rooms in a row whose markers, clearances
/// and doors are drawn from the case's seed, so markers run from open floor to a closet, rooms
/// from the kit's 208 by 192 doors to a slot, and some floors and sills stand a step up.
/// </summary>
internal static class DirectorLevels
{
    public const float Cell = 1024;

    public static HordeLevel Make(ulong seed, int depth)
    {
        var random = new Random(unchecked((int)(seed * 2654435761UL % int.MaxValue)));
        var rooms = new List<HordeRoom>();
        for (int id = 0; id < 3; id++)
        {
            float x0 = id * Cell;
            var markers = ImmutableArray.CreateBuilder<PackMarker>();
            int count = 1 + random.Next(3);
            for (int k = 0; k < count; k++)
            {
                float z = random.NextDouble() < 0.2 ? 48 : 0;
                var at = At(x0 + 128 + (float)random.NextDouble() * (Cell - 256), 128 + (float)random.NextDouble() * (Cell - 256), z);
                MarkerClearance? clearance = random.NextDouble() < 0.1 ? null : new MarkerClearance(Side(random), Side(random), Side(random), Side(random),
                    random.NextDouble() < 0.2 ? 100 + (float)random.NextDouble() * 60 : 160 + (float)random.NextDouble() * 300);
                markers.Add(new PackMarker(at, 90 * k, clearance));
            }
            rooms.Add(new HordeRoom(id, At(x0, 0, -64), At(x0 + Cell, Cell, 512), markers.ToImmutable()));
        }

        var doors = new List<HordeDoor>();
        for (int a = 0; a < 2; a++)
        {
            float width = random.NextDouble() < 0.6 ? 208 : 40 + (float)random.NextDouble() * 168;
            float height = random.NextDouble() < 0.6 ? 192 : 90 + (float)random.NextDouble() * 102;
            float sill = random.NextDouble() < 0.15 ? 48 : 0;
            doors.Add(new HordeDoor(a, a + 1, At((a + 1) * Cell, Cell / 2, sill), At(1, 0, 0), width, height));
        }
        return HordeLevel.Create(seed, depth, rooms, doors);
    }

    /// <summary>How far a marker's clearance reaches one way: a quarter of them a closet's 20 to 60, the rest 60 to 400.</summary>
    private static float Side(Random random) =>
        random.NextDouble() < 0.25 ? 20 + (float)random.NextDouble() * 40 : 60 + (float)random.NextDouble() * 340;
}

/// <summary>One planned level of a sweep: the level, the depth and party, and the director's plan.</summary>
internal sealed record SweepCase(ulong Seed, int Depth, int Party, HordeLevel Level, LevelPlan Plan);

/// <summary>
/// The rules a spawn director's plan must never break (plan_descent_logic HO9; plan_tf2_rpg 2f,
/// 5d, B.11, R16, R25, D11), checked from the content tables and the level, not from the
/// director's own code: each answers the first break it finds, or null.
/// </summary>
internal static class DirectorOracle
{
    private static TF2DescentRules R => TheRules;

    private static float Half(double scale) => (float)(R.Content.RobotRules.Push.HullWidth / 2 * scale);

    private static float Tall(double scale) => (float)(R.Content.RobotRules.Push.HullHeight * scale);

    /// <summary>D11: a pack's rolled size (kept and left out) lies in its depth's range, raised for the party.</summary>
    public static string? PackSize(SweepCase c)
    {
        var row = R.Robots.Depth(c.Depth);
        int min = R.Packs.PackSize(row.PackMin, c.Party), max = R.Packs.PackSize(row.PackMax, c.Party);
        foreach (var p in c.Plan.Packs.Where(p => !p.Robots[0].Boss))
            if (p.Robots.Length + p.Unplaced < min || p.Robots.Length + p.Unplaced > max)
                return $"room {p.Room}: {p.Robots.Length} kept + {p.Unplaced} left out, outside {min}..{max}";
        foreach (var e in c.Plan.Emptied)
            if (e.Robots < min || e.Robots > max) return $"emptied room {e.Room}: {e.Robots} outside {min}..{max}";
        return null;
    }

    /// <summary>2f, R16: every robot is a template its depth draws (weight above 0, no boss) at a scale in the template's range.</summary>
    public static string? Templates(SweepCase c)
    {
        var row = R.Robots.Depth(c.Depth);
        foreach (var r in Robots(c))
        {
            var t = R.Robots.Template(r.Roll.Template);
            if (!row.Templates.Any(w => w.Template == t.Id && w.Weight > 0) || t.Boss) return $"{t.Id} is not drawn at depth {c.Depth}";
            if (r.Roll.Scale < t.ScaleMin || r.Roll.Scale > t.ScaleMax) return $"{t.Id} at {r.Roll.Scale}, outside {t.ScaleMin}..{t.ScaleMax}";
        }
        return null;
    }

    /// <summary>5d: a robot stands at a marker whose clearance holds its scaled hull, in a room it can leave by a door its hull fits (a floor-bound one by a door on its floor).</summary>
    public static string? Clearance(SweepCase c)
    {
        var d = R.Content.Horde.Director;
        foreach (var r in Robots(c))
        {
            var room = c.Level.Rooms[r.Room];
            var marker = room.Markers[r.Marker];
            float half = Half(r.Roll.Scale), tall = Tall(r.Roll.Scale);
            if (marker.Clearance is { } m && (MathF.Min(MathF.Min(m.West, m.East), MathF.Min(m.South, m.North)) < half || m.Height < tall))
                return $"{r.Roll.Template} at {r.Roll.Scale:0.###} ({2 * half:0.#} by {tall:0.#}) at room {r.Room} marker {r.Marker}, clearance {m}";
            var t = R.Robots.Template(r.Roll.Template);
            bool floorBound = d.FloorBoundSizes.Contains(t.Size);
            bool leaves = c.Level.Doors.Any(door => (door.RoomA == r.Room || door.RoomB == r.Room)
                                                    && door.Width - 2 * d.DoorMargin >= 2 * half && door.Height >= tall
                                                    && (!floorBound || MathF.Abs(door.Position.Z - marker.Position.Z) <= d.StepHeight));
            if (!leaves) return $"{r.Roll.Template} at {r.Roll.Scale:0.###} cannot leave room {r.Room}";
        }
        return null;
    }

    /// <summary>R25, 2f: no pack is more than half immune to one damage type.</summary>
    public static string? HalfImmune(SweepCase c)
    {
        foreach (var p in c.Plan.Packs)
            foreach (var g in p.Robots.Where(r => r.Roll.Stats.Immunity is not null).GroupBy(r => r.Roll.Stats.Immunity))
                if (g.Count() * 2 > p.Robots.Length) return $"room {p.Room}: {g.Count()} of {p.Robots.Length} immune to {g.Key}";
        return null;
    }

    /// <summary>2f, R25: a champion carries one to its depth's most affixes, all different, at most one immunity; none at a depth with no champion chance; no robot is immune to every type.</summary>
    public static string? Champions(SweepCase c)
    {
        var row = R.Robots.Depth(c.Depth);
        foreach (var r in Robots(c))
        {
            var a = r.Roll.Affixes;
            if (a.Length > row.MaxAffixes) return $"{a.Length} affixes at depth {c.Depth} (most {row.MaxAffixes})";
            if (a.Length > 0 && row.ChampionPct <= 0) return $"a champion at depth {c.Depth}, which has no champion chance";
            if (a.Distinct().Count() != a.Length) return $"repeated affixes {string.Join("+", a)}";
            if (a.Count(x => R.Content.Champions[x].Immunity is not null) > 1) return $"two immunities {string.Join("+", a)}";
            if (r.Roll.Stats.Resist.Values.Count(v => v >= 100) >= R.Content.DamageTypes.Rows.Count) return "immune to every type";
        }
        return null;
    }

    /// <summary>B.11: a pack of two or more is not all one style, unless its re-rolls ran out.</summary>
    public static string? Mixed(SweepCase c)
    {
        int cap = R.Content.RobotRules.Temperament.PackRerolls;
        foreach (var p in c.Plan.Packs.Where(p => p.Robots.Length > 1))
            if (p.Robots.Select(r => r.Roll.Temperament.Style).Distinct().Count() == 1 && p.StyleRerolls < cap)
                return $"room {p.Room}: all {p.Robots[0].Roll.Temperament.Style} after {p.StyleRerolls} re-rolls";
        return null;
    }

    /// <summary>E2: the roster never passes its capacity.</summary>
    public static string? Capacity(SweepCase c) =>
        c.Plan.Robots > R.Content.Horde.RosterCapacity ? $"{c.Plan.Robots} robots past {R.Content.Horde.RosterCapacity}" : null;

    /// <summary>HO9: each robot's post is inside its room, inside its marker's clearance, and clear of every other post in the room by their hulls and the gap, unless it was crowded onto its marker.</summary>
    public static string? Posts(SweepCase c)
    {
        float gap = (float)R.Content.Horde.Director.PostGap;
        foreach (var room in c.Level.Rooms)
        {
            var here = Robots(c).Where(r => r.Room == room.Id).ToList();
            foreach (var r in here.Where(r => !r.Crowded))
            {
                float half = Half(r.Roll.Scale);
                if (r.Post.X - half < room.Min.X || r.Post.X + half > room.Max.X || r.Post.Y - half < room.Min.Y || r.Post.Y + half > room.Max.Y)
                    return $"room {room.Id}: a post at ({r.Post.X:0}, {r.Post.Y:0}) leaves the room";
                var m = room.Markers[r.Marker];
                if (m.Clearance is { } cl)
                {
                    float dx = r.Post.X - m.Position.X, dy = r.Post.Y - m.Position.Y;
                    if (dx - half < -cl.West - 0.01f || dx + half > cl.East + 0.01f || dy - half < -cl.South - 0.01f || dy + half > cl.North + 0.01f)
                        return $"room {room.Id}: a post at ({dx:0}, {dy:0}) from its marker leaves its clearance {cl}";
                }
                if (r.Post.Z != m.Position.Z) return $"room {room.Id}: a post off its marker's floor";
            }
            for (int i = 0; i < here.Count; i++)
                for (int j = i + 1; j < here.Count; j++)
                {
                    var a = here[i];
                    var b = here[j];
                    if (a.Crowded || b.Crowded) continue;
                    float apart = Half(a.Roll.Scale) + Half(b.Roll.Scale) + gap - 0.01f;
                    if (MathF.Abs(a.Post.X - b.Post.X) < apart && MathF.Abs(a.Post.Y - b.Post.Y) < apart)
                        return $"room {room.Id}: posts ({a.Post.X:0}, {a.Post.Y:0}) and ({b.Post.X:0}, {b.Post.Y:0}) overlap";
                }
        }
        return null;
    }

    public static IEnumerable<PlannedRobot> Robots(SweepCase c) => c.Plan.Packs.SelectMany(p => p.Robots).Where(r => !r.Boss);

    /// <summary>Every rule by name, for a sweep that checks them all.</summary>
    public static readonly (string Name, Func<SweepCase, string?> Check)[] All =
    [
        ("pack size", PackSize), ("templates", Templates), ("clearance", Clearance), ("half-immune", HalfImmune),
        ("champions", Champions), ("mixed", Mixed), ("capacity", Capacity), ("posts", Posts),
    ];
}

/// <summary>
/// The routine sweep, planned once for the class: 10,000 seeds, each at depth 1 + seed % 5
/// and party 1 + (seed / 5) % 4, so every depth and party size is met 500 times and more.
/// The full product (10,000 seeds x depths 1 to 5 x parties 1 to 4) is the slow tier's.
/// </summary>
public sealed class DirectorSweep
{
    public const int Seeds = 10_000;

    internal SweepCase[] Cases { get; }

    public DirectorSweep()
    {
        var director = new SpawnDirector(TheRules);
        Cases = new SweepCase[Seeds];
        Parallel.For(0, Seeds, s =>
        {
            ulong seed = (ulong)s;
            int depth = 1 + s % 5, party = 1 + s / 5 % 4;
            var level = DirectorLevels.Make(seed, depth);
            Cases[s] = new SweepCase(seed, depth, party, level, director.Plan(level, party));
        });
    }

    internal static SweepCase Plan(SpawnDirector director, ulong seed, int depth, int party)
    {
        var level = DirectorLevels.Make(seed, depth);
        return new SweepCase(seed, depth, party, level, director.Plan(level, party));
    }
}
