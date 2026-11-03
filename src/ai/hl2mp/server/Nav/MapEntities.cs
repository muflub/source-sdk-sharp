using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SourceSharp.MapFormats.Bsp;
using SourceSharp.MapFormats.Bsp.Structs;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>One entity of interest from the map's entity lump: its classname, name, origin and, for a brush entity, its bounds in the world.</summary>
public readonly record struct MapEntity(string ClassName, string? Name, AiVector Origin, AiVector? Mins, AiVector? Maxs,
                                        AiVector? Point0 = null, AiVector? Point1 = null);

/// <summary>
/// What a bot knows about the map before it plays (plan_hl2mp_ai §5.0, §6.2),
/// read from the bsp's entity lump: the spawns, the weapon and item spawns,
/// the hurt volumes the nav leaves out, and the ladders and obstacles the
/// lump names. A player who knows the game knows these.
/// </summary>
public sealed class MapEntities
{
    private const int EntityLumpIndex = 0;
    private const int ModelsLumpIndex = 14;
    private const int ModelBytes = 48;

    public MapEntities(IReadOnlyList<MapEntity> all, string? levelId)
    {
        All = all;
        LevelId = levelId;
    }

    public IReadOnlyList<MapEntity> All { get; }

    /// <summary>The worldspawn's <c>ss_level_id</c>: a linked level's id, which its navigation binds by; null on a stock map.</summary>
    public string? LevelId { get; }

    /// <summary>The deathmatch spawns.</summary>
    public IEnumerable<MapEntity> Spawns => All.Where(e => e.ClassName == "info_player_deathmatch");

    /// <summary>The weapons the map places.</summary>
    public IEnumerable<MapEntity> Weapons => All.Where(e => e.ClassName.StartsWith("weapon_", StringComparison.Ordinal));

    /// <summary>The items the map places (health, armour, ammo, chargers).</summary>
    public IEnumerable<MapEntity> Items => All.Where(e => e.ClassName.StartsWith("item_", StringComparison.Ordinal));

    /// <summary>Where people go: the spawns and the pickups, what the choke points' routes run between (§5.5).</summary>
    public IEnumerable<AiVector> PeoplesPlaces() => Spawns.Concat(Weapons).Concat(Items).Select(e => e.Origin);

    /// <summary>A point of each <c>func_useableladder</c>: its origin, whose x and y are the ladder's.</summary>
    public IReadOnlyList<AiVector> Ladders => _ladders ??= [.. All.Where(e => e.ClassName == "func_useableladder").Select(e => e.Origin)];

    private IReadOnlyList<AiVector>? _ladders;

    /// <summary>Where each <c>func_useableladder</c> climbs: its point0 and point1, which the movement holds a climber to.</summary>
    public IReadOnlyList<LadderClimb> LadderClimbs =>
        [.. All.Where(e => e.ClassName == "func_useableladder" && e.Point0 is not null && e.Point1 is not null)
               .Select(e => new LadderClimb(e.Point0!.Value, e.Point1!.Value))];

    /// <summary>The <c>trigger_hurt</c> volumes, which no derived cell may stand in.</summary>
    public IReadOnlyList<HurtVolume> Hurt =>
        [.. All.Where(e => e.ClassName == "trigger_hurt" && e.Mins is not null).Select(e => new HurtVolume(e.Mins!.Value, e.Maxs!.Value))];

    /// <summary>Reads the entity lump of a whole bsp file.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a bsp, or its lumps lie outside it.</exception>
    public static MapEntities Read(ReadOnlySpan<byte> bsp)
    {
        if (bsp.Length < BspData.HeaderSize || BinaryPrimitives.ReadInt32LittleEndian(bsp) != BspData.Ident)
            throw new InvalidDataException("not a bsp: the VBSP ident is missing");
        ReadOnlySpan<byte> entities = Lump(bsp, EntityLumpIndex);
        ReadOnlySpan<byte> models = Lump(bsp, ModelsLumpIndex);
        int end = entities.IndexOf((byte)0);
        string text = Encoding.UTF8.GetString(end >= 0 ? entities[..end] : entities);

        var all = new List<MapEntity>();
        string? levelId = null;
        foreach (BspEntity e in EntityLump.ParseText(text))
        {
            if (e.ClassName == "worldspawn")
                levelId = e.Get("ss_level_id");
            AiVector origin = Vec(e.Get("origin")) ?? default;
            AiVector? mins = null, maxs = null;
            string? model = e.Get("model");
            if (model is { Length: > 1 } && model[0] == '*'
                && int.TryParse(model.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                && (index + 1) * ModelBytes <= models.Length)
            {
                ReadOnlySpan<byte> m = models.Slice(index * ModelBytes, ModelBytes);
                mins = origin + new AiVector(F(m, 0), F(m, 4), F(m, 8));
                maxs = origin + new AiVector(F(m, 12), F(m, 16), F(m, 20));
            }

            all.Add(new MapEntity(e.ClassName, e.Get("targetname"), origin, mins, maxs, Vec(e.Get("point0")), Vec(e.Get("point1"))));
        }

        return new MapEntities(all, levelId);
    }

    private static float F(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadSingleLittleEndian(b[at..]);

    private static ReadOnlySpan<byte> Lump(ReadOnlySpan<byte> bsp, int index)
    {
        int at = 8 + index * 16;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bsp[at..]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bsp[(at + 4)..]);
        if (offset < 0 || length < 0 || (long)offset + length > bsp.Length)
            throw new InvalidDataException($"bsp lump {index} lies outside the file");
        return bsp.Slice(offset, length);
    }

    private static AiVector? Vec(string? text)
    {
        if (text is null)
            return null;
        string[] p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 3
               && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
               && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
               && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            ? new AiVector(x, y, z) : null;
    }
}

/// <summary>Where on the derived graph a map point is.</summary>
public static class NavPoints
{
    /// <summary>How far across a player's box reaches an item it touches: its half-width, an item's, and a column.</summary>
    public const float TouchReach = 48f;

    /// <summary>
    /// The cells a body of <paramref name="cls"/> could stand in to touch
    /// <paramref name="p"/> (an item on a floor, a shelf or a crate): within
    /// <see cref="TouchReach"/> across, the floor from a player's height below
    /// the point to a step above it; nearest first, then by index.
    /// </summary>
    public static List<CellId> CellsNear(NavGraph graph, AiVector p, ClearanceClass cls)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var found = new HashSet<int>();
        for (float dy = -TouchReach; dy <= TouchReach; dy += 8f)
        {
            for (float dx = -TouchReach; dx <= TouchReach; dx += 8f)
            {
                CellId c = graph.FindCell(new AiVector(p.X + dx, p.Y + dy, p.Z + 18f));
                for (int guard = 0; c.IsValid && guard < 4; guard++)
                {
                    NavCell cell = graph.Cell(c);
                    if (cell.FloorZ < p.Z - 72f)
                        break;
                    if (cell.Admits(cls))
                        found.Add(c.Index);
                    c = graph.FindCell(new AiVector(p.X + dx, p.Y + dy, cell.FloorZ - 2f));
                }
            }
        }

        return [.. found.OrderBy(i => graph.Cell(i).Centre.DistanceTo(p)).ThenBy(i => i).Select(i => new CellId(i))];
    }

    /// <summary>The nearest of <see cref="CellsNear"/>, or None.</summary>
    public static CellId CellNear(NavGraph graph, AiVector p, ClearanceClass cls)
    {
        List<CellId> cells = CellsNear(graph, p, cls);
        return cells.Count > 0 ? cells[0] : CellId.None;
    }
}
