// descent-breakables: the breakable crates and barrels of a linked Descent
// level (plan_descent_mine_levels.md M34, brief p12), read from its .nav3d the
// way the server reads its markers (TF2.Descent.Server.Level.LevelReader:
// Nav3dReader.Open, every point of interest but the doors, by its poi_type).
//
//   descent-breakables list <map.nav3d>
//       one line per breakable: its poi_type, position, yaw and cell, then the
//       counts. The read-back of what a room compile carried into the level.
//   descent-breakables obstacles <map.nav3d>
//       one line per dynamic obstacle (a door, a func_brush the map switches): its targetname, class,
//       kind, bounds and how many leaves it names. The grid treats each as open space and the runtime
//       blocks its leaves while it is in the way (map tools Nav3dObstacle): what the nav believes of a
//       boss arena's switched brushes (brief p13).
//   descent-breakables reach <map.nav3d> <width> <height> <x,y,z> <x,y,z>...
//       whether a box of width and height standing on the floor walks (face steps, a step of 18 at
//       most, nothing but the grid) from the first point to each other, with each of the level's
//       named obstacles in the way in turn: first every func_brush that starts enabled, then for
//       n = 1, 2, ...: descent_boss_pillar_1..n out of the way and descent_boss_rubble_1..n in it
//       (the Bore's fight as its pillars fall, brief p13).
//   descent-breakables preview <map-preview.bsp> [<map-preview.nav3d>]
//       stands the server's model at each breakable as a solid static prop
//       (no shadow) in the PREVIEW's copy of the map, in place: a crate's model
//       at its point, a barrel's 30 above it (its model's origin is its
//       middle), turned by its yaw. The game draws them from the breakable
//       manager, so the map the host links has none; the preview, which draws
//       a map's static props and walks into their collision, would otherwise
//       show open floor where each stands. The .nav3d defaults to the one the
//       link wrote beside the map.
//
// The rooms write the points (src/mapgen/Descent/Breakables.cs); the models and
// lift are the server's (descent-breakables branch, FurnitureBreakables), and a
// fact holds the room side's copy of them to the asset database.

using System.Globalization;

using SourceSharp.MapFormats.Bsp;
using SourceSharp.MapFormats.Bsp.Structs;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace DescentBreakables;

internal static class BreakablesTool
{
    private const string Usage = "usage: descent-breakables list <map.nav3d> | obstacles <map.nav3d> | preview <map-preview.bsp> [<map-preview.nav3d>]";

    /// <summary>The server's model per poi_type and how far above the point its origin stands.</summary>
    private static readonly Dictionary<string, (string Model, float Lift)> Kinds = new(StringComparer.Ordinal)
    {
        ["descent_mark_breakable_crate"] = ("models/props_2fort/miningcrate001.mdl", 0),
        ["descent_mark_breakable_barrel"] = ("models/props_farm/wooden_barrel.mdl", 30),
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["list", string nav] => List(nav),
                ["obstacles", string nav] => Obstacles(nav),
                ["reach", string nav, string w, string h, .. var points] when points.Length >= 2 =>
                    Reach(nav, float.Parse(w, CultureInfo.InvariantCulture), float.Parse(h, CultureInfo.InvariantCulture), points),
                ["preview", string bsp] => await PreviewAsync(bsp, Path.ChangeExtension(bsp, ".nav3d")).ConfigureAwait(false),
                ["preview", string bsp, string nav] => await PreviewAsync(bsp, nav).ConfigureAwait(false),
                _ => Fail(Usage, 2),
            };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Fail($"descent-breakables: {e.Message}", 1);
        }
    }

    private static int Fail(string message, int code)
    {
        Console.Error.WriteLine(message);
        return code;
    }

    /// <summary>The level's breakables, as the server reads its markers.</summary>
    private static List<Nav3dPoi> Read(string nav)
    {
        Nav3dReader reader = Nav3dReader.Open(File.ReadAllBytes(nav));
        var all = new List<Nav3dPoi>();
        for (int p = 0; p < reader.PoiCount; p++)
        {
            Nav3dPoi poi = reader.Poi(p);
            if ((poi.Flags & Nav3dPoiFlags.Door) == 0 && Kinds.ContainsKey(poi.Type))
                all.Add(poi);
        }

        return all;
    }

    private static int List(string nav)
    {
        var all = Read(nav);
        foreach (Nav3dPoi p in all)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{p.Type} {p.Position.X:0.##} {p.Position.Y:0.##} {p.Position.Z:0.##} yaw {p.Yaw:0.##} cell {p.Cell}"));
        Console.WriteLine(Counts(all));
        return 0;
    }

    private static int Obstacles(string nav)
    {
        var reader = Nav3dReader.Open(File.ReadAllBytes(nav));
        for (int i = 0; i < reader.ObstacleCount; i++)
        {
            Nav3dObstacle o = reader.Obstacle(i);
            if (o.Name is null)
                continue;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{o.Name} {o.ClassName} {o.Kind} cell {o.Cell} ({o.Mins.X:0} {o.Mins.Y:0} {o.Mins.Z:0})-({o.Maxs.X:0} {o.Maxs.Y:0} {o.Maxs.Z:0}) leaves {reader.ObstacleLeaves(i).Length}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{reader.ObstacleCount} obstacles, unnamed ones (door frames, caps) not listed"));
        return 0;
    }

    private static int Reach(string nav, float width, float height, string[] points)
    {
        var reader = Nav3dReader.Open(File.ReadAllBytes(nav));
        var at = points.Select(p => p.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()).Select(v => new Vec3(v[0], v[1], v[2] + 1)).ToArray();
        string?[] names = [.. Enumerable.Range(0, reader.ObstacleCount).Select(i => reader.Obstacle(i).Name)];
        int pillars = names.Count(n => n is not null && n.StartsWith("descent_boss_pillar_", StringComparison.Ordinal));
        int failures = 0;
        for (int fallen = 0; fallen <= pillars; fallen++)
        {
            var blocking = new bool[reader.ObstacleCount];
            for (int i = 0; i < blocking.Length; i++)
            {
                string? n = names[i];
                if (n is null)
                    continue;
                int k = n.StartsWith("descent_boss_pillar_", StringComparison.Ordinal) || n.StartsWith("descent_boss_rubble_", StringComparison.Ordinal)
                    ? int.Parse(n[(n.LastIndexOf('_') + 1)..], CultureInfo.InvariantCulture) : 0;
                blocking[i] = n.StartsWith("descent_boss_pillar_", StringComparison.Ordinal) ? k > fallen
                    : n.StartsWith("descent_boss_rubble_", StringComparison.Ordinal) ? k <= fallen
                    : n != "descent_boss_exit";
            }

            int start = reader.FindLeaf(at[0]);
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            if (start >= 0 && reader.StandableAtCentre(start, width, height, Nav3dClipClass.Npc, blocking))
            {
                seen.Add(start);
                queue.Enqueue(start);
            }

            while (queue.Count > 0)
            {
                int l = queue.Dequeue();
                foreach (Nav3dNeighbour nb in reader.Neighbours(l))
                    if (!seen.Contains(nb.Leaf) && reader.StandableAtCentre(nb.Leaf, width, height, Nav3dClipClass.Npc, blocking)
                        && Math.Abs(reader.Leaf(nb.Leaf).FloorZ(Nav3dClipClass.Npc) - reader.Leaf(l).FloorZ(Nav3dClipClass.Npc)) <= 18)
                    {
                        seen.Add(nb.Leaf);
                        queue.Enqueue(nb.Leaf);
                    }
            }

            var line = new List<string>();
            for (int p = 1; p < at.Length; p++)
            {
                int leaf = reader.FindLeaf(at[p]);
                bool ok = leaf >= 0 && seen.Contains(leaf);
                failures += ok ? 0 : 1;
                line.Add(string.Create(CultureInfo.InvariantCulture, $"{points[p]} {(ok ? "reached" : leaf < 0 ? "NO LEAF" : "NOT REACHED")}"));
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{width}x{height}, pillars fallen {fallen} of {pillars}, from {points[0]} ({seen.Count} leaves): {string.Join("; ", line)}"));
            if (pillars == 0)
                break;
        }

        return failures == 0 ? 0 : 1;
    }

    private static string Counts(List<Nav3dPoi> all) => string.Create(CultureInfo.InvariantCulture,
        $"{all.Count(p => p.Type.EndsWith("_crate", StringComparison.Ordinal))} crates, {all.Count(p => p.Type.EndsWith("_barrel", StringComparison.Ordinal))} barrels, {all.Count} breakables");

    private static async Task<int> PreviewAsync(string bspPath, string nav)
    {
        var all = Read(nav);
        BspData bsp;
        await using (FileStream input = File.OpenRead(bspPath))
            bsp = await BspFile.LoadAsync(input).ConfigureAwait(false);

        int id = GameLumpId.MakeId(GameLumpId.StaticProps);
        int index = bsp.GameLumps.FindIndex(e => e.Id == id);
        StaticPropLump lump = index >= 0 ? StaticPropLump.Read(bsp.GameLumps[index]) : new StaticPropLump();
        int added = 0;
        foreach (Nav3dPoi p in all)
        {
            var (model, lift) = Kinds[p.Type];
            int type = lump.ModelNames.IndexOf(model);
            if (type < 0)
            {
                lump.ModelNames.Add(model);
                type = lump.ModelNames.Count - 1;
            }

            var origin = new Vec3(p.Position.X, p.Position.Y, p.Position.Z + lift);
            // Once: a map already holding the model there (this run twice) is left as it is.
            if (lump.Props.Any(q => q.PropType == type && q.Origin == origin))
                continue;
            lump.Props.Add(new StaticProp
            {
                Origin = origin,
                Angles = new Vec3(0, p.Yaw, 0),
                PropType = (ushort)type,
                FirstLeaf = (ushort)lump.LeafEntries.Count,
                LeafCount = 0,
                // SOLID_VPHYSICS: the preview's walker meets its collision, as a player meets the manager's box.
                Solid = 6,
                Flags = StaticPropFlags.NoShadow,
                LightingOrigin = origin,
                ForcedFadeScale = 1,
            });
            added++;
        }

        GameLumpEntry entry = lump.Write();
        if (index >= 0)
            bsp.GameLumps[index] = entry;
        else
            bsp.GameLumps.Add(entry);
        string temp = bspPath + ".breakables.tmp";
        await using (FileStream output = File.Create(temp))
            await BspFile.SaveAsync(bsp, output, BspWriteMode.PreserveSourceLayout).ConfigureAwait(false);
        File.Move(temp, bspPath, overwrite: true);
        Console.WriteLine($"descent-breakables: {Counts(all)}; {added} stood in {Path.GetFileName(bspPath)} as static props");
        return 0;
    }
}
