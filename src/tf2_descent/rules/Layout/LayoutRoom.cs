using SourceSharp.Host.Contracts;

namespace TF2.Descent.Rules.Layout;

/// <summary>
/// The tags and families the layout reads. The tags are the host's known tags
/// (<c>LibraryManifest.KnownTags</c>); <c>camp</c> and <c>antechamber</c> are
/// families, read from the name, because the host's lint refuses an unknown tag.
/// </summary>
public static class RoomTags
{
    public const string Start = "start";
    public const string Stairs = "stairs";
    public const string BossArena = "boss_arena";
    public const string Treasure = "treasure";
    public const string Corridor = "corridor";
    public const string Junction = "junction";
    public const string Shrine = "shrine";

    public const string CampFamily = "camp";
    public const string AntechamberFamily = "antechamber";
    public const string StubFamily = "stub";
}

/// <summary>What a room is to the layout.</summary>
public enum RoomKind
{
    /// <summary>Any room the layout fills the path and its branches with.</summary>
    Fill,
    /// <summary>The way in: a <c>start</c> room or a camp (<c>room_role up</c>).</summary>
    Arrival,
    /// <summary>The way down: a <c>stairs</c> room (<c>room_role down</c>).</summary>
    Exit,
    /// <summary>A <c>treasure</c> room, placed off the critical path.</summary>
    Treasure,
    /// <summary>A boss level's room before the arena.</summary>
    Antechamber,
    /// <summary>A <c>boss_arena</c>.</summary>
    Arena,
}

/// <summary>A library room with the facts its name carries.</summary>
public sealed record LayoutRoom(RoomInfo Info, RoomShape Shape)
{
    public string Name => Info.Name;

    public bool Has(string tag) => Info.Tags.Contains(tag);

    /// <summary>R19's combat room: everything but a corridor and a junction.</summary>
    public bool IsCombat => !Has(RoomTags.Corridor) && !Has(RoomTags.Junction);

    /// <summary>A low combat room, of which the critical path never holds three in a row (R19).</summary>
    public bool IsLowCombat => IsCombat && Shape.Height == HeightClass.Low;

    /// <summary>
    /// A hallway room (M18): tagged <c>corridor</c> or <c>junction</c>, or of a low connector
    /// family (<see cref="RoomFamilies.Connectors"/>: drift, bend, tee, cross, dogleg, wide drift, stub).
    /// </summary>
    public bool IsConnector =>
        Has(RoomTags.Corridor) || Has(RoomTags.Junction) || RoomFamilies.Connectors.Contains(Shape.Family);

    /// <summary>
    /// A room a branch may end in (M18): the treasure room, a stub with a purpose (a
    /// <c>shrine</c> or <c>treasure</c> tag), or any room that is not a connector.
    /// </summary>
    public bool IsDestination =>
        Has(RoomTags.Treasure) || (Shape.Family == RoomTags.StubFamily && Has(RoomTags.Shrine)) || !IsConnector;

    /// <summary>The kind the level's mix counts the room as (M21).</summary>
    public RoomClass Class =>
        IsConnector ? RoomClass.Connector
        : Shape.Height switch { HeightClass.Hall => RoomClass.Hall, HeightClass.Shaft => RoomClass.Shaft, _ => RoomClass.Chamber };

    /// <summary>A shrine stub: unique rooms are counted per level (M20).</summary>
    public bool IsShrine => Shape.Family == RoomTags.StubFamily && Has(RoomTags.Shrine);

    public RoomKind Kind =>
        Has(RoomTags.Start) || Shape.Family == RoomTags.CampFamily ? RoomKind.Arrival
        : Has(RoomTags.Stairs) ? RoomKind.Exit
        : Has(RoomTags.BossArena) ? RoomKind.Arena
        : Shape.Family == RoomTags.AntechamberFamily ? RoomKind.Antechamber
        : Has(RoomTags.Treasure) ? RoomKind.Treasure
        : RoomKind.Fill;
}

/// <summary>
/// The layout's seeded generator: splitmix64, so a layout is a function of its
/// seed on every runtime (System.Random's sequence is not a contract).
/// </summary>
public sealed class LayoutRandom(ulong seed)
{
    private ulong _state = seed;

    public ulong Next()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>0 to <paramref name="n"/> - 1.</summary>
    public int Below(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);

    /// <summary><paramref name="low"/> to <paramref name="high"/> inclusive.</summary>
    public int Range(int low, int high) => low + Below(high - low + 1);

    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Below(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>An index drawn in proportion to the weights (all positive).</summary>
    public int Weighted(IReadOnlyList<long> weights)
    {
        long total = 0;
        foreach (var w in weights) total += w;
        var pick = (long)(Next() % (ulong)total);
        for (var i = 0; i < weights.Count; i++)
        {
            pick -= weights[i];
            if (pick < 0) return i;
        }
        return weights.Count - 1;
    }
}
