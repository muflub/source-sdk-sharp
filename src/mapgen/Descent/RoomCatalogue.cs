namespace SourceSharp.MapGen.Descent;

/// <summary>
/// A shape family's generator: every variant of the family on a kit, in a
/// theme, for a seed. The same arguments give the same rooms, byte for byte.
/// </summary>
public delegate IEnumerable<Room> ShapeVariants(RoomKit kit, RoomTheme theme, ulong seed);

/// <summary>A shape family (plan §5): its name, which prefixes its rooms' names, and its generator.</summary>
public sealed record RoomShape(string Family, ShapeVariants Variants);

/// <summary>
/// A seeded stream of choices for one shape family (SplitMix64): the seed
/// mixed with the family's name, so families drawn from one library seed
/// vary independently, and adding a family changes no other family's rooms.
/// Never <see cref="System.Random"/>, whose sequence is not promised across
/// runtimes.
/// </summary>
public sealed class RoomSeed
{
    private ulong state;

    public RoomSeed(ulong seed, string family)
    {
        // FNV-1a of the family's name, stable across runs and runtimes.
        ulong hash = 14695981039346656037UL;
        foreach (char c in family)
            hash = (hash ^ c) * 1099511628211UL;
        state = seed ^ hash;
    }

    /// <summary>The next 64 bits.</summary>
    public ulong Next()
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A whole number from <paramref name="min"/> up to but not including <paramref name="max"/>.</summary>
    public int Int(int min, int max)
    {
        if (max <= min)
            throw new ArgumentOutOfRangeException(nameof(max), max, $"an empty range [{min}, {max})");
        return min + (int)(Next() % (ulong)(max - min));
    }

    /// <summary>A fraction from 0 to 1 inclusive, in 1/1024ths: a tier pick.</summary>
    public float Fraction() => Int(0, 1025) / 1024f;

    /// <summary>True with chance <paramref name="p"/>.</summary>
    public bool Chance(float p) => Int(0, 1 << 20) < p * (1 << 20);

    /// <summary>One of <paramref name="items"/>.</summary>
    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count)];
}

/// <summary>
/// The room catalogue: the shape families a library is made of, and the
/// library and manifest they make: the mine's families of plan §5 for a
/// normal depth, a boss level's row, and two of them as a demonstration.
/// </summary>
public static class RoomCatalogue
{
    /// <summary>The demonstration families: a connector and a jump hall.</summary>
    public static IReadOnlyList<RoomShape> Demonstration { get; } =
    [
        new("drift", Shapes.DriftShape.Variants),
        new("gallery", Shapes.GalleryShape.Variants),
    ];

    /// <summary>
    /// The mine's catalogue for a normal depth (plan §5): the real shape
    /// families, each its own file in <c>Shapes/</c>, with the role rooms each
    /// depth draws its ways in and down from by their depth ranges.
    /// </summary>
    public static IReadOnlyList<RoomShape> Mine { get; } =
    [
        new("drift", Shapes.DriftShape.Variants),
        new("bend", Shapes.BendShape.Variants),
        new("tee", Shapes.TeeShape.Variants),
        new("cross", Shapes.CrossShape.Variants),
        new("dogleg", Shapes.DoglegShape.Variants),
        new("stub", Shapes.StubShape.Variants),
        new("widedrift", Shapes.WideDriftShape.Variants),
        new("pillar", Shapes.PillarShape.Variants),
        new("split", Shapes.SplitShape.Variants),
        new("pit", Shapes.PitShape.Variants),
        new("store", Shapes.StoreShape.Variants),
        new("yard", Shapes.YardShape.Variants),
        new("pool", Shapes.PoolShape.Variants),
        new("stope", Shapes.StopeShape.Variants),
        new("gallery", Shapes.GalleryShape.Variants),
        new("chasm", Shapes.ChasmShape.Variants),
        new("chimney", Shapes.ChimneyShape.Variants),
        new("cavern", Shapes.CavernShape.Variants),
        new("hall", Shapes.BigShapes.Hall),
        new("grotto", Shapes.BigShapes.Grotto),
        new("flood", Shapes.BigShapes.Flood),
        new("crypt", Shapes.CryptShape.Variants),
        new("treasure", Shapes.TreasureShape.Variants),
        new("arrival", Shapes.ArrivalShape.Variants),
        new("exit", Shapes.ExitShape.Variants),
        new("camp", Shapes.CampShape.Variants),
    ];

    /// <summary>A boss level's row (plan C3): the way in, the antechamber, the arena (a room of several cells) and the way down.</summary>
    public static IReadOnlyList<RoomShape> Boss { get; } =
    [
        new("arrival", Shapes.ArrivalShape.Variants),
        new("antechamber", Shapes.AntechamberShape.Variants),
        new("arena", Shapes.ArenaShape.Variants),
        new("exit", Shapes.ExitShape.Variants),
    ];

    /// <summary>
    /// Every room of every family, family by family, in each family's order,
    /// each renamed from its author's <c>family_variant</c> to the layout's
    /// name (<see cref="LayoutNames"/>).
    /// </summary>
    public static IReadOnlyList<Room> Rooms(IEnumerable<RoomShape> shapes, RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rooms = new List<Room>();
        foreach (RoomShape shape in shapes)
            foreach (Room room in shape.Variants(kit, theme, seed))
                rooms.Add(room with { Name = LayoutNames.Name(room, shape.Family) });

        return rooms;
    }

    /// <summary>The families whose first room has a quiet twin (<see cref="QuietTwins"/>): a door set each of two opposite, two at a corner, three and four, and a stub's one.</summary>
    public static IReadOnlyList<string> QuietFamilies { get; } = ["drift", "bend", "tee", "cross", "stub"];

    /// <summary>
    /// A quiet twin of the first room of each of <see cref="QuietFamilies"/> among <paramref name="rooms"/>
    /// (brief p9: depth 3 seed 1's arrival was a pack's reach from the next room's packs, and the hero
    /// died within seconds of arriving, twice): the same room without its pack spawns, its entry
    /// <see cref="RoomEntry.Quiet"/>, its author's name <c>&lt;family&gt;_&lt;variant&gt;q</c> spelled
    /// by the codec with the flag <c>q</c>. The layout puts only these beside the way in, and them
    /// nowhere else.
    /// </summary>
    public static IReadOnlyList<Room> QuietTwins(IReadOnlyList<Room> rooms)
    {
        var twins = new List<Room>();
        foreach (string family in QuietFamilies)
        {
            if (rooms.FirstOrDefault(r => r.Role == RoomRole.None && TF2.Descent.Rules.Layout.RoomName.TryParse(r.Name, out var s, out _) && s!.Family == family && !s.Quiet) is not { } first)
                continue;
            var shape = TF2.Descent.Rules.Layout.RoomName.Parse(first.Name);
            Room twin = first with
            {
                Name = $"{family}_{shape.Variant}q",
                Entities = [.. first.Entities.Where(e => !(e.ClassName == "info_poi" && e.Key("poi_type") == Markers.PoiType(MarkerKind.PackSpawn)))],
                Entry = first.Entry with { Quiet = true },
            };
            twins.Add(twin with { Name = LayoutNames.Name(twin, family) });
        }

        return twins;
    }

    /// <summary>The room an author named <paramref name="stem"/> (<c>family_variant</c>), by its layout name's stem.</summary>
    public static Room Named(IEnumerable<Room> rooms, string stem) =>
        rooms.Single(r => r.Name.StartsWith(stem + "-", StringComparison.Ordinal));

    /// <summary>The library of <paramref name="rooms"/> on <paramref name="kit"/>, under the town's sky, with the town's sun when a room opens on the sky.</summary>
    public static RoomLibrary Library(RoomKit kit, IEnumerable<Room> rooms)
    {
        var library = new RoomLibrary(kit, sky: MineShaft.Sky);
        foreach (Room room in rooms)
            library.Add(room);
        // A sky ceiling (level 1's shaft) is lit by the town's sun (plan §8).
        if (library.Rooms.Any(r => r.HasSky))
            library.WithSun(MineShaft.Sun);
        return library;
    }
}
