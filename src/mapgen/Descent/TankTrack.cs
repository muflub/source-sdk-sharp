using System.Globalization;
using SourceSharp.MapGen.Descent.Dressing;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// Whether the MvM Tank fits along a boss arena's track (level 10, brief p13): its hull, stock
/// <c>boss_tank.mdl</c>'s (-115.3 -79.1 -16.8 to 117.3 79.1 176.2, read from the model's header), standing
/// on the floor along each leg facing the leg's way, and turning in place at each point of the track
/// (the disc its footprint sweeps, <see cref="Shapes.ArenaShape.TankRadius"/>), must meet nothing solid
/// up to its height: no brush that is solid at the start or once a pillar falls (the world, every named
/// brush, its rubble included), no solid prop, no breakable. Sampled every 8 units along the track and on
/// an 8-unit grid over the hull's footprint.
/// </summary>
public static class TankTrack
{
    /// <summary>The hull's footprint about its origin, its front along +x.</summary>
    public const float Front = 117.32f, Back = 115.33f, Side = 79.12f;

    private const float Step = 8;

    /// <summary>What the Tank meets along <paramref name="room"/>'s track, one line per leg that meets something (empty when it fits); <paramref name="closing"/> also drives the way back from the last point to the first.</summary>
    public static IReadOnlyList<string> Collisions(Room room, IReadOnlyDictionary<string, PropModel> models, bool closing = false)
    {
        if (room.Arena is not { Track.Count: > 1 } arena)
            return [];
        float f = room.Kit.Sill, z0 = f + 2, z1 = f + Shapes.ArenaShape.TankHeight;
        var solids = new RoomSolids(room).Brushes.Where(b => b.Maxs.Z > z0 && b.Mins.Z < z1 && !IsStrip(room, b)).ToArray();
        // Every brush entity the encounter switches is solid at some time: the rubble too, though it starts disabled.
        solids = [.. solids, .. room.Entities.Where(e => e.ClassName == "func_brush" && e.Key("targetname") is { } n && n.StartsWith("descent_boss_", StringComparison.Ordinal))
                                  .SelectMany(e => e.Brushes).Where(b => b.Maxs.Z > z0 && b.Mins.Z < z1 && !solids.Contains(b))];
        var props = DressLint.Pieces(room, room, models).Where(p => p.Entity is not null && p.Solid && p.Box.Maxs.Z > z0 && p.Box.Mins.Z < z1)
            .Select(p => (p.What, p.Box)).ToArray();
        var lines = new List<string>();
        var points = arena.Track.Select(p => p.At).ToList();
        int legs = closing ? points.Count : points.Count - 1;
        for (int i = 0; i < legs; i++)
        {
            Point a = points[i], c = points[(i + 1) % points.Count];
            float dx = c.X - a.X, dy = c.Y - a.Y, length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 0.5f)
                continue;
            float ux = dx / length, uy = dy / length;
            string? hit = null;
            for (float s = 0; s <= length + 0.01f && hit is null; s = s >= length ? length + 1 : Math.Min(length, s + Step))
                hit = Meets(a.X + ux * s, a.Y + uy * s, ux, uy, solids, props, z0, z1);
            // Turning in place at the leg's end.
            hit ??= MeetsDisc(c.X, c.Y, solids, props, z0, z1);
            if (i == 0)
                hit ??= MeetsDisc(a.X, a.Y, solids, props, z0, z1);
            if (hit is not null)
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"leg {i} ({a.X:0} {a.Y:0})-({c.X:0} {c.Y:0}): {hit}"));
        }

        return lines;
    }

    private static bool IsStrip(Room room, Brush b) => room.Entities.Any(e => RoomSolids.IsStrips(e) && e.Brushes.Contains(b));

    /// <summary>What the hull standing at (x, y), facing (ux, uy), meets.</summary>
    private static string? Meets(float x, float y, float ux, float uy, Brush[] solids, (string What, Box Box)[] props, float z0, float z1)
    {
        float reach = MathF.Sqrt(Front * Front + Side * Side) + 1;
        var near = solids.Where(b => b.Mins.X < x + reach && b.Maxs.X > x - reach && b.Mins.Y < y + reach && b.Maxs.Y > y - reach).ToArray();
        var nearProps = props.Where(p => p.Box.Mins.X < x + reach && p.Box.Maxs.X > x - reach && p.Box.Mins.Y < y + reach && p.Box.Maxs.Y > y - reach).ToArray();
        if (near.Length == 0 && nearProps.Length == 0)
            return null;
        for (float a = -Back; a <= Front + 0.01f; a = a >= Front ? Front + 1 : Math.Min(Front, a + Step))
            for (float s = -Side; s <= Side + 0.01f; s = s >= Side ? Side + 1 : Math.Min(Side, s + Step))
                if (At(x + ux * a - uy * s, y + uy * a + ux * s, near, nearProps, z0, z1) is { } what)
                    return what;
        return null;
    }

    /// <summary>What the disc the hull turns in at (x, y) meets.</summary>
    private static string? MeetsDisc(float x, float y, Brush[] solids, (string What, Box Box)[] props, float z0, float z1)
    {
        float r = Shapes.ArenaShape.TankRadius;
        var near = solids.Where(b => b.Mins.X < x + r && b.Maxs.X > x - r && b.Mins.Y < y + r && b.Maxs.Y > y - r).ToArray();
        var nearProps = props.Where(p => p.Box.Mins.X < x + r && p.Box.Maxs.X > x - r && p.Box.Mins.Y < y + r && p.Box.Maxs.Y > y - r).ToArray();
        for (float px = -r; px <= r; px += Step)
            for (float py = -r; py <= r; py += Step)
                if (px * px + py * py <= r * r && At(x + px, y + py, near, nearProps, z0, z1) is { } what)
                    return what + " (turning)";
        return null;
    }

    private static string? At(float x, float y, Brush[] near, (string What, Box Box)[] props, float z0, float z1)
    {
        foreach (Brush b in near)
            if (b.Mins.X < x && b.Maxs.X > x && b.Mins.Y < y && b.Maxs.Y > y && b.Column(x, y) is { } c && c.High > z0 && c.Low < z1)
                return string.Create(CultureInfo.InvariantCulture, $"a {b.Material} brush ({b.Mins})-({b.Maxs}) at ({x:0} {y:0})");
        foreach (var (what, box) in props)
            if (box.Mins.X < x && box.Maxs.X > x && box.Mins.Y < y && box.Maxs.Y > y)
                return $"{what} at ({x:0} {y:0})";
        return null;
    }
}
