namespace TF2.Descent.Rules.Layout;

/// <summary>
/// The shape families the layout knows (plan_descent_mine_levels section 5, and
/// section 7's crypts), each with the tags a library room of it may carry. The
/// map generator's catalogue and the layout's test catalogue are both held to this
/// one list, so a family the depth table names is a family a library can hold,
/// spelled the same on both sides.
/// </summary>
public static class RoomFamilies
{
    /// <summary>The hallway families (M18): a room of one is a connector whatever its tags.</summary>
    public static IReadOnlySet<string> Connectors { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "drift", "bend", "tee", "cross", "dogleg", "widedrift", RoomTags.StubFamily,
    };

    /// <summary>Every family, and the tags a room of it may carry (none required).</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Tags { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
    {
        ["drift"] = Set(RoomTags.Corridor),
        ["bend"] = Set(RoomTags.Corridor),
        ["tee"] = Set(RoomTags.Junction),
        ["cross"] = Set(RoomTags.Junction),
        ["dogleg"] = Set(RoomTags.Corridor),
        ["widedrift"] = Set(RoomTags.Corridor),
        [RoomTags.StubFamily] = Set(RoomTags.Corridor, RoomTags.Shrine, RoomTags.Treasure),
        ["crypt"] = Set(),
        ["pillar"] = Set(),
        ["split"] = Set(),
        ["pit"] = Set(),
        ["store"] = Set(),
        ["yard"] = Set(),
        ["pool"] = Set(),
        ["stope"] = Set(),
        ["gallery"] = Set(),
        ["chasm"] = Set(),
        ["chimney"] = Set(),
        ["cavern"] = Set(),
        // Rooms larger than one cell (M24): a 2x2 yard hall, 2x2 and 3x3 caverns, a 2x2 flooded gallery.
        ["hall"] = Set(),
        ["grotto"] = Set(),
        ["flood"] = Set(),
        ["treasure"] = Set(RoomTags.Treasure),
        ["arrival"] = Set(RoomTags.Start),
        ["exit"] = Set(RoomTags.Stairs),
        [RoomTags.CampFamily] = Set(RoomTags.Start),
        [RoomTags.AntechamberFamily] = Set(),
        ["arena"] = Set(RoomTags.BossArena),
    };

    /// <summary>Whether <paramref name="family"/> is a family of the list.</summary>
    public static bool IsKnown(string family) => Tags.ContainsKey(family);

    private static HashSet<string> Set(params string[] tags) => new(tags, StringComparer.Ordinal);
}
