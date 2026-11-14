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
/// library and manifest they make. Phase 1's catalogue holds the two
/// demonstration families; the twenty of plan §5 join it.
/// </summary>
public static class RoomCatalogue
{
    /// <summary>The demonstration families: a drift and a hall.</summary>
    public static IReadOnlyList<RoomShape> Demonstration { get; } =
    [
        new("drift", Shapes.DriftShape.Variants),
        new("hall", Shapes.HallShape.Variants),
    ];

    /// <summary>Every room of every family, family by family, in each family's order.</summary>
    public static IReadOnlyList<Room> Rooms(IEnumerable<RoomShape> shapes, RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rooms = new List<Room>();
        foreach (RoomShape shape in shapes)
            foreach (Room room in shape.Variants(kit, theme, seed))
            {
                if (!room.Name.StartsWith(shape.Family + "_", StringComparison.Ordinal))
                    throw new ArgumentException($"family {shape.Family} made a room called {room.Name}; its rooms are named {shape.Family}_<variant>");
                rooms.Add(room);
            }

        return rooms;
    }

    /// <summary>The library of <paramref name="rooms"/> on <paramref name="kit"/>.</summary>
    public static RoomLibrary Library(RoomKit kit, IEnumerable<Room> rooms)
    {
        var library = new RoomLibrary(kit);
        foreach (Room room in rooms)
            library.Add(room);
        return library;
    }
}
