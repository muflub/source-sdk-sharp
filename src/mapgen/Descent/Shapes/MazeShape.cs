using SourceSharp.MapGen.Descent.Themes;
using TF2.Descent.Rules.Layout;

namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// Level 5's maze cells (brief p18, rulings M35 and M36): the square maze round Sir Nukesalot's arena is
/// a 10 by 10 block of 512 cells, each its own room so the automap shows the maze cell by cell. A cell is
/// one corridor junction: a hub in its middle and an arm to each door, <see cref="Width"/> wide, the
/// corners solid rock, a solid roof <see cref="Height"/> high so nobody jumps a wall. A cell's shape
/// depends only on its door mask, so the family is keyed by it: every mask once, unturned, in the
/// connector family its doors make (a stub's one door, a drift's two opposite, a bend's two at a
/// corner, a tee's three, a cross's four), named <c>&lt;family&gt;_maze&lt;v&gt;</c>. Variant
/// <c>a</c> is the bare tunnel; <c>b</c> holds a pack in its hub and a powder barrel or crate on a
/// breakable marker in a niche off a closed side (a dead end a container at its end instead of the
/// pack). Timber sets line each arm where it leaves the hub; a red warning lamp hangs at the junctions,
/// the bends and the dead ends only, and a dim warm fill keeps every walked floor lit by the lint's rule, so the
/// maze reads dark round the bright arena. Tagged <c>corridor</c> or <c>junction</c> (at most one pack),
/// they are tunnels to the jump rules (<see cref="RoomLint.JumpSpaceExempt"/>).
/// </summary>
public static class MazeShape
{
    /// <summary>The corridor's width, hub and arms: the door's 208 and its frame's set, a giant's 92 twice over.</summary>
    public const int Width = 256;

    /// <summary>The cell's height: its roof's underside 384 over the floor's 16, solid, under a jump over any wall.</summary>
    public const int Height = 400;

    /// <summary>What a maze cell's variant name starts with (<c>tee_mazea</c>).</summary>
    public const string VariantPrefix = "maze";

    /// <summary>The families a maze cell's doors make: one door, two opposite, two at a corner, three, four.</summary>
    public static IReadOnlyList<string> Families { get; } = ["stub", "drift", "bend", "tee", "cross"];

    /// <summary>The variants of every mask: the bare tunnel, and one with a pack (or a dead end's reward) and a breakable.</summary>
    public static IReadOnlyList<char> VariantLetters { get; } = ['a', 'b'];

    /// <summary>The dim warm fill in each cell's hub: enough for the lint's lit rule, little to see by.</summary>
    public static Lamp Fill { get; } = new("255 170 130", 48, 192, 576);

    /// <summary>How high the fill hangs over the floor.</summary>
    public const int FillAbove = 200;

    /// <summary>How high a red warning lamp hangs on its wall.</summary>
    public const int LampAbove = 224;

    /// <summary>The connector family a door mask makes.</summary>
    public static string FamilyOf(Doors mask) => DoorSets.Count(mask) switch
    {
        1 => "stub",
        2 => mask is (Doors.East | Doors.West) or (Doors.North | Doors.South) ? "drift" : "bend",
        3 => "tee",
        4 => "cross",
        _ => throw new ArgumentOutOfRangeException(nameof(mask), mask, "a maze cell has 1 to 4 doors"),
    };

    /// <summary>Every door mask, in mask order.</summary>
    public static IEnumerable<Doors> Masks => Enumerable.Range(1, 15).Select(m => (Doors)m);

    /// <summary>Whether a room's layout name is a maze cell's (<c>&lt;family&gt;_maze&lt;v&gt;-...</c>).</summary>
    public static bool IsMaze(string name) =>
        RoomName.TryParse(name, out var shape, out _) && Families.Contains(shape!.Family) && shape.Variant.StartsWith(VariantPrefix, StringComparison.Ordinal);

    /// <summary>A shape family's generator of the maze cells of <paramref name="family"/>: every mask that family covers, each variant.</summary>
    public static ShapeVariants Of(string family) => (kit, theme, seed) => Variants(family, kit, theme, seed);

    private static IEnumerable<Room> Variants(string family, RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "maze:" + family);
        foreach (Doors mask in Masks.Where(m => FamilyOf(m) == family))
            foreach (char v in VariantLetters)
                yield return Cell(family, v, mask, kit, theme, rng);
    }

    private static readonly (Doors Door, CellFace Face)[] Sides =
        [(Doors.East, CellFace.East), (Doors.West, CellFace.West), (Doors.North, CellFace.North), (Doors.South, CellFace.South)];

    /// <summary>The walls a mask opens, in face order.</summary>
    public static CellFace[] FacesOf(Doors mask) => [.. Sides.Where(s => (mask & s.Door) != 0).Select(s => s.Face)];

    private static Room Cell(string family, char variant, Doors mask, RoomKit kit, RoomTheme theme, RoomSeed rng)
    {
        CellFace[] doors = FacesOf(mask);
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f, h = Width / 2f;
        var hub = new Rect(m - h, m - h, m + h, m + h);
        bool reward = variant == 'b';
        var b = new RoomBuilder($"{family}_{VariantPrefix}{variant}", kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([doors.Length >= 3 ? "junction" : "corridor"], MinDepth: 5, MaxDepth: 5))
            .WithMapLabel("Maze").WithOwnLamps().WithOutline(hub).WithBody(hub).WithFocus(new Point(m, m, kit.Sill));
        foreach (CellFace face in doors)
            b.WithOutline(Parts.Neck(kit, face, Width, m - h - t + 1));
        float f = b.Floor, roof = b.CeilingUnderside;

        // A cross's b swells its hub into a small chamber (it has no closed side for a niche).
        CellFace[] closed = [.. Sides.Select(s => s.Face).Where(s => !doors.Contains(s))];
        if (reward && closed.Length == 0)
        {
            hub = Rect.Around(m, m, Width + 64, Width + 64);
            b.WithOutline(hub).WithBody(hub);
        }

        // Timber sets where each arm leaves the hub: two posts against its sides and a cap under the roof.
        Surface timber = theme.TrimOrBlock;
        foreach (CellFace face in doors)
        {
            var arm = Parts.Neck(kit, face, Width, m - h - t);
            Rect set = face switch
            {
                CellFace.East => new(hub.X1, arm.Y0, hub.X1 + 16, arm.Y1),
                CellFace.West => new(hub.X0 - 16, arm.Y0, hub.X0, arm.Y1),
                CellFace.North => new(arm.X0, hub.Y1, arm.X1, hub.Y1 + 16),
                _ => new(arm.X0, hub.Y0 - 16, arm.X1, hub.Y0),
            };
            bool alongX = face is CellFace.East or CellFace.West;
            if (alongX)
            {
                b.WithBox(new Point(set.X0, set.Y0, f), new Point(set.X1, set.Y0 + 16, roof - 16), timber)
                    .WithBox(new Point(set.X0, set.Y1 - 16, f), new Point(set.X1, set.Y1, roof - 16), timber);
            }
            else
            {
                b.WithBox(new Point(set.X0, set.Y0, f), new Point(set.X0 + 16, set.Y1, roof - 16), timber)
                    .WithBox(new Point(set.X1 - 16, set.Y0, f), new Point(set.X1, set.Y1, roof - 16), timber);
            }

            b.WithBox(new Point(set.X0, set.Y0, roof - 16), new Point(set.X1, set.Y1, roof), timber);
        }

        // A niche off a closed side: a breakable on a marker, and in a dead end the reward at its end.
        CellFace? nicheSide = null;
        if (reward && closed.Length > 0)
        {
            CellFace side = rng.Pick(closed);
            nicheSide = side;
            Rect niche = side switch
            {
                CellFace.East => new(hub.X1, m - 80, hub.X1 + 80, m + 80),
                CellFace.West => new(hub.X0 - 80, m - 80, hub.X0, m + 80),
                CellFace.North => new(m - 80, hub.Y1, m + 80, hub.Y1 + 80),
                _ => new(m - 80, hub.Y0 - 80, m + 80, hub.Y0),
            };
            b.WithOutline(niche);
            var kind = rng.Chance(0.5f) ? BreakableKind.Barrel : BreakableKind.Crate;
            b.WithBreakable(kind, new Point(niche.CentreX, niche.CentreY, f), side is CellFace.East or CellFace.West ? 0 : 90);
        }

        if (reward && doors.Length == 1)
        {
            // A dead end's reward at its end, the arm's far side from its door: a Scout's or anyone's walk.
            CellFace end = Opposite(doors[0]);
            var (ex, ey) = Inside(hub, end, 48);
            b.Mark(MarkerKind.Container, ex, ey);
        }
        else if (reward)
            b.Pack(m, m);
        else if (rng.Chance(0.25f) && closed.Length > 0)
            // Now and then a powder barrel against a closed wall of a bare cell, where the hub's corner leaves room.
            b.Mark(MarkerKind.Barrel, Inside(hub, closed[0], 40).X, Inside(hub, closed[0], 40).Y);

        // Light: the dim fill in the hub, and a red warning lamp at a dead end, a bend and a tee (a drift and a cross keep the fill alone).
        b.WithFill(new Point(m, m, f + FillAbove), Fill);
        // (A cross has no wall far enough from its four doors for one: its hub is the fill's alone.)
        CellFace[] walls = [.. closed.Where(s => s != nicheSide)];
        bool bend = doors.Length == 2 && FamilyOf(mask) == "bend";
        if (theme.Lamps is { Mount: LampMount.Wall } red && (doors.Length is 1 or 3 || bend) && walls.Length > 0)
        {
            CellFace face = walls[^1];
            var (lx, ly) = face switch
            {
                CellFace.East => (hub.X1, m + 72),
                CellFace.West => (hub.X0, m + 72),
                CellFace.North => (m + 72, hub.Y1),
                _ => (m + 72, hub.Y0),
            };
            b.WithLampSite(red, new Point(lx, ly, f + LampAbove), Stations.Inward(face));
        }

        return b.Build();
    }

    /// <summary>A point <paramref name="inset"/> in from <paramref name="r"/>'s <paramref name="side"/>, on its middle line.</summary>
    private static (float X, float Y) Inside(Rect r, CellFace side, float inset) => side switch
    {
        CellFace.East => (r.X1 - inset, r.CentreY),
        CellFace.West => (r.X0 + inset, r.CentreY),
        CellFace.North => (r.CentreX, r.Y1 - inset),
        _ => (r.CentreX, r.Y0 + inset),
    };

    private static CellFace Opposite(CellFace face) => face switch
    {
        CellFace.East => CellFace.West,
        CellFace.West => CellFace.East,
        CellFace.North => CellFace.South,
        _ => CellFace.North,
    };
}
