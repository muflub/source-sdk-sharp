namespace TF2.Descent.Rules.Layout;

/// <summary>How much a depth favours each height class; a zero is used only when nothing else fits.</summary>
public sealed record HeightWeights(int Low, int Chamber, int Hall, int Shaft)
{
    public int Of(HeightClass height) => height switch
    {
        HeightClass.Low => Low,
        HeightClass.Chamber => Chamber,
        HeightClass.Hall => Hall,
        _ => Shaft,
    };
}

/// <summary>The four kinds of room a level's mix is counted in (M21).</summary>
public enum RoomClass
{
    /// <summary>A hallway: a corridor, a junction or a connector family.</summary>
    Connector,
    /// <summary>A chamber, or a low room that is not a hallway (a crypt).</summary>
    Chamber,
    Hall,
    Shaft,
}

/// <summary>A kind's target share of a level's fill rooms, and its most (M21).</summary>
public sealed record KindTarget(RoomClass Kind, int SharePercent, int Max = int.MaxValue);

/// <summary>A family's target share of a level's fill rooms (0 for none), its fewest and its most (M21).</summary>
public sealed record FamilyTarget(string Family, int SharePercent, int Min = 0, int Max = int.MaxValue);

/// <summary>
/// One depth's layout numbers (task C2, plan_descent_mine_levels section 7; M16, M18 to M21).
/// </summary>
/// <param name="Depth">1 to 15.</param>
/// <param name="Act">1 (levels 1-5), 2 (6-10) or 3 (11-15).</param>
/// <param name="IsBoss">A boss level (5, 10, 15): a fixed row, not a grown level (C3).</param>
/// <param name="HasDownExit">False at the bottom (15): the level has no down room.</param>
/// <param name="Rooms">
/// The rooms the level places, exactly (M16, M19): the budget a normal level grows to. A boss
/// level's row length.
/// </param>
/// <param name="MaxSpan">
/// The most cells the level's bounding box may span on either side (M19). The link lays cells
/// from the origin and refuses a level past 16,384 units (<c>LevelLinker.CheckExtent</c>), so a
/// side is at most 16 cells of 1024 or 10 of 1536; the table stays a cell under that.
/// </param>
/// <param name="PathMin">The shortest critical path, in rooms from the arrival to the exit inclusive.</param>
/// <param name="PathMax">The longest critical path, in rooms.</param>
/// <param name="Loops">The loops the level aims at: tunnels joined where they pass each other, no more.</param>
/// <param name="Heights">The height classes the depth favours.</param>
/// <param name="Families">Shape families weighted other than 1: 3 favours a family, 0 leaves it out unless nothing else fits.</param>
/// <param name="JumpSharePercent">
/// The share of combat rooms the layout steers towards jump rooms. R19's 60 % is held per act (E4),
/// so a low level (1, 9) sits under it and a vertical one (7, 13) over it.
/// </param>
/// <param name="MinSTierSpots">The fewest Scout-tier spots (S2, S3) the level places (plan 4.2).</param>
/// <param name="MinBTierSpots">The fewest blast-tier spots (B1-B3) the level places (plan 4.2).</param>
/// <param name="ArrivalFamily">The family the arrival is drawn from when the pool has it (the camps at 6 and 11, M10), else null.</param>
/// <param name="EndSharePercent">
/// The share of the level's rooms that are tunnel ends (rooms with one joined door, but the arrival
/// and the exit), a soft target the growth steers towards (M20). Every end is a destination; a
/// hallway never ends (M18).
/// </param>
/// <param name="MaxShrines">The most shrine stubs the level holds (M20, M21: unique rooms are counts).</param>
/// <param name="Kinds">The level's mix by kind (M21): each kind's target share of the fill rooms, and its most.</param>
/// <param name="FamilyTargets">The families plan section 7 names for the level, with shares, fewest and most (M21).</param>
/// <param name="Variety">0 to 1: how strongly a room is drawn less beside its own family and the more often its variant already stands (M21).</param>
/// <param name="Straightness">0 to 1: the chance the critical path carries straight on rather than turning (M19).</param>
/// <param name="Sprawl">0 to 1: how strongly a new room prefers a cell with few placed neighbours; high is thin tunnels, low compact workings.</param>
/// <param name="BranchLength">The rooms a side tunnel typically runs before it ends in its destination.</param>
/// <param name="Clusters">0 to 1: how strongly a hall or shaft is placed beside another, making worked-out areas of 2 to 4 tall rooms.</param>
public sealed record DepthProfile(
    int Depth,
    int Act,
    bool IsBoss,
    bool HasDownExit,
    int Rooms,
    int MaxSpan,
    int PathMin,
    int PathMax,
    int Loops,
    HeightWeights Heights,
    IReadOnlyDictionary<string, int> Families,
    int JumpSharePercent,
    int MinSTierSpots,
    int MinBTierSpots,
    string? ArrivalFamily,
    int EndSharePercent,
    int MaxShrines,
    IReadOnlyList<KindTarget> Kinds,
    IReadOnlyList<FamilyTarget> FamilyTargets,
    double Variety,
    double Straightness,
    double Sprawl,
    int BranchLength,
    double Clusters)
{
    /// <summary>The weight multiplier of a family at this depth: 1 unless the table leaves the family out (0).</summary>
    public int FamilyWeight(string family) => Families.TryGetValue(family, out var w) ? w : 1;

    /// <summary>The kind's target, or none.</summary>
    public KindTarget? KindTargetOf(RoomClass kind) => Kinds.FirstOrDefault(k => k.Kind == kind);

    /// <summary>The family's target, or none.</summary>
    public FamilyTarget? FamilyTargetOf(string family) => FamilyTargets.FirstOrDefault(f => f.Family == family);

    /// <summary>
    /// The most rooms larger than one cell the level places (M24, <see cref="DepthTable.BigRoomsAt"/>);
    /// the layout draws between half of it, rounded up, and all of it, when the pool has such rooms.
    /// </summary>
    public int MaxBigRooms { get; init; }
}

/// <summary>
/// The per-depth table (task C2) as data, from plan_descent_mine_levels section 7:
/// the room budget of M16 (user, 2026-11-10: 18 to 51 rooms), grown in any shape within a
/// bounding box (M19, 2026-11-10: "keep the max room limit but allow it to build any shape to
/// be more mine tunnel like"), the critical path about 40 % of the rooms at level 1 falling to
/// a third at 13 and 14, the shapes and heights each level is written as, the jump minimums of
/// 4.2 (two S-tier and two B-tier spots on a normal level, but one S and none at the hallway
/// level 1 and one of each at the crypt level 9, whose section 7 rows ask few jumps), the
/// tunnel-end share of M20 (a tunnel may always end, in a destination), the shape controls of
/// M19 per act's character, the room mix of M21, and the boss levels' fixed rows (5, 10, 15;
/// no down exit at 15).
/// </summary>
public static class DepthTable
{
    public const int Depths = 15;

    /// <summary>The engine's coordinates stop at this many units from the origin (<c>LevelLinker.CheckExtent</c>).</summary>
    public const int WorldExtent = 16384;

    /// <summary>The normal kit's cell (E1).</summary>
    public const int CellUnits = 1024;

    /// <summary>The bounding box's side in cells unless a depth lowers it: one cell inside the 16 the extent allows at 1024.</summary>
    public const int DefaultMaxSpan = 15;

    /// <summary>The depth's row; throws <see cref="ArgumentOutOfRangeException"/> outside 1 to 15.</summary>
    /// <remarks>
    /// The mix (M21) follows each level's "Shapes" row of plan section 7, as shares of the fill
    /// rooms by kind (hallway, chamber, hall, shaft) and by the families it names, with counts
    /// where it gives one ("two chambers; one store", "a first stope", "a chimney", "a chasm",
    /// "two tall naves").
    /// </remarks>
    public static DepthProfile For(int depth) => depth switch
    {
        // Act I, the Mann Co. mine: engineered drifts, straight with right-angle crosscuts.
        // "Drifts, bends, tees; two chambers; one store": level 1 is hallways, and asks one Scout
        // spot and no blast spot, so the minimums pull in no jump rooms.
        1 => Normal(1, 18, 6, 8, 3, new(6, 1, 0, 0), 25, Mix(75, 25, 0, 0, chamberMax: 5),
            [new("drift", 30), new("bend", 15), new("tee", 10), new("store", 0, Min: 1, Max: 1)],
            straight: 0.85, sprawl: 0.6, branch: 4, clusters: 0.3, ends: 15, shrines: 2, minS: 1, minB: 0),
        // "Wide drifts, yards, a first stope".
        2 => Normal(2, 21, 7, 10, 3, new(2, 3, 1, 0), 50, Mix(45, 40, 15, 0),
            [new("widedrift", 20), new("yard", 15), new("stope", 0, Min: 1, Max: 3)],
            straight: 0.85, sprawl: 0.6, branch: 4, clusters: 0.3, ends: 15, shrines: 1),
        // "Stopes, split levels, pit rooms".
        3 => Normal(3, 27, 9, 12, 4, new(1, 1, 3, 0), 75, Mix(30, 35, 35, 0),
            [new("stope", 20), new("split", 15), new("pit", 15)],
            straight: 0.8, sprawl: 0.55, branch: 3, clusters: 0.4, ends: 15, shrines: 1),
        // "Galleries, a chimney, stores".
        4 => Normal(4, 27, 9, 12, 4, new(1, 1, 3, 1), 80, Mix(30, 25, 40, 5),
            [new("gallery", 25), new("chimney", 0, Min: 1, Max: 2), new("store", 15)],
            straight: 0.8, sprawl: 0.55, branch: 3, clusters: 0.5, ends: 15, shrines: 1),
        5 => Boss(5, columns: 4, down: true),
        // Act II, the breakthrough: caves, winding and sprawling. 7 has the big caverns; 9 is the
        // tight, twisty crypt level, the deliberate ground-game level.
        // "Camp, drifts into caverns, a chasm".
        6 => Normal(6, 27, 9, 12, 4, new(1, 3, 3, 0), 65, Mix(40, 20, 40, 0),
            [new("drift", 25), new("cavern", 25), new("chasm", 0, Min: 1, Max: 2)],
            straight: 0.5, sprawl: 0.75, branch: 4, clusters: 0.5, ends: 18, shrines: 1, arrival: "camp"),
        // "Caverns, chimneys, chasms": the most vertical level.
        7 => Normal(7, 32, 10, 13, 5, new(1, 0, 3, 3), 90, Mix(20, 0, 45, 35),
            [new("cavern", 30), new("chimney", 20), new("chasm", 15)],
            straight: 0.45, sprawl: 0.7, branch: 3, clusters: 0.9, ends: 18, shrines: 1),
        // "Pools, pit rooms, drifts with water".
        8 => Normal(8, 37, 11, 15, 5, new(1, 3, 3, 0), 65, Mix(35, 35, 30, 0),
            [new("pool", 25), new("pit", 20), new("drift", 20)],
            straight: 0.45, sprawl: 0.75, branch: 4, clusters: 0.5, ends: 18, shrines: 1),
        // "Low crypt drifts, stubs, two tall naves": the low crypt level, "few: nave ledges only".
        9 => Normal(9, 37, 11, 15, 6, new(4, 1, 1, 0), 35, Mix(40, 50, 10, 0, hallMax: 2),
            [new("crypt", 35), new("drift", 20)],
            straight: 0.3, sprawl: 0.7, branch: 2, clusters: 0.2, ends: 20, shrines: 3, minS: 1, minB: 1),
        10 => Boss(10, columns: 4, down: true),
        // Act III, below: 11 a regular factory floor, 12 to 14 ever more winding and sprawling.
        // "Camp, galleries, yards, wide drifts".
        11 => Normal(11, 37, 11, 15, 6, new(1, 1, 4, 0), 75, Mix(35, 25, 40, 0),
            [new("gallery", 30), new("yard", 20), new("widedrift", 20)],
            straight: 0.9, sprawl: 0.35, branch: 3, clusters: 0.4, ends: 15, shrines: 1, arrival: "camp"),
        // "Pools, pit rooms, chimneys".
        12 => Normal(12, 44, 13, 17, 6, new(1, 3, 3, 2), 60, Mix(30, 35, 20, 15),
            [new("pool", 20), new("pit", 20), new("chimney", 15)],
            straight: 0.6, sprawl: 0.7, branch: 4, clusters: 0.5, ends: 18, shrines: 1),
        // "Chasms over lava, caverns, stopes".
        13 => Normal(13, 51, 15, 19, 7, new(1, 0, 3, 3), 80, Mix(25, 0, 45, 30),
            [new("chasm", 20), new("cavern", 25), new("stope", 15)],
            straight: 0.5, sprawl: 0.8, branch: 5, clusters: 0.6, ends: 18, shrines: 2),
        // "Caverns, pools, doglegs; no straight drift".
        14 => Normal(14, 51, 15, 19, 7, new(3, 1, 2, 2), 50, Mix(35, 10, 30, 25),
            [new("cavern", 25), new("pool", 20), new("dogleg", 20), new("drift", 0, Max: 0)],
            straight: 0.4, sprawl: 0.85, branch: 5, clusters: 0.6, ends: 18, shrines: 2, excluded: ["drift"]),
        15 => Boss(15, columns: 3, down: false),
        _ => throw new ArgumentOutOfRangeException(nameof(depth), depth, $"depth {depth} is not a level of the shaft (1 to {Depths})"),
    };

    /// <summary>
    /// The most rooms larger than one cell a normal level holds (M24, user 2026-11-10): none at
    /// level 1 (the hallway level), one at 2 to 4, two at 6 to 9, three at 11 to 14. The one place
    /// to tune them.
    /// </summary>
    public static int BigRoomsAt(int depth) => depth switch
    {
        >= 2 and <= 4 => 1,
        >= 6 and <= 9 => 2,
        >= 11 and <= 14 => 3,
        _ => 0,
    };

    /// <summary>The act a depth belongs to: 1 for 1-5, 2 for 6-10, 3 for 11-15.</summary>
    public static int ActOf(int depth) => (depth - 1) / 5 + 1;

    private static KindTarget[] Mix(int connector, int chamber, int hall, int shaft, int chamberMax = int.MaxValue, int hallMax = int.MaxValue) =>
    [
        new(RoomClass.Connector, connector),
        new(RoomClass.Chamber, chamber, chamberMax),
        new(RoomClass.Hall, hall, hallMax),
        new(RoomClass.Shaft, shaft),
    ];

    private static DepthProfile Normal(int depth, int rooms, int pathMin, int pathMax, int loops, HeightWeights heights, int jumpShare,
        KindTarget[] kinds, FamilyTarget[] families, double straight, double sprawl, int branch, double clusters, int ends, int shrines,
        string? arrival = null, string[]? excluded = null, int minS = 2, int minB = 2)
    {
        var weights = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in excluded ?? []) weights[f] = 0;
        return new DepthProfile(depth, ActOf(depth), IsBoss: false, HasDownExit: true, rooms, DefaultMaxSpan, pathMin, pathMax, loops,
            heights, weights, jumpShare, minS, minB, arrival, ends, shrines, kinds, families, Variety: 1, straight, sprawl, branch, clusters)
        {
            MaxBigRooms = BigRoomsAt(depth),
        };
    }

    /// <summary>A boss level: one row of arrival, antechamber, arena and (but at 15) exit.</summary>
    private static DepthProfile Boss(int depth, int columns, bool down) =>
        new(depth, ActOf(depth), IsBoss: true, HasDownExit: down, columns, columns, columns, columns, 0,
            new(1, 1, 1, 1), new Dictionary<string, int>(StringComparer.Ordinal), 100, 0, 0, null,
            EndSharePercent: 0, MaxShrines: 0, Kinds: [], FamilyTargets: [], Variety: 0,
            Straightness: 1, Sprawl: 0, BranchLength: 0, Clusters: 0);
}
