namespace SourceSharp.MapGen.Descent;

/// <summary>
/// What a room's <c>info_poi</c> marker is for (plan_descent_mine_levels.md
/// §5, task A11; plan_tf2_rpg.md 6a and 2l). A marker costs no entity: the
/// room compile takes it out of the map and carries it in the navigation,
/// and what stands on it is decided when the level loads.
/// </summary>
public enum MarkerKind
{
    /// <summary>Where a robot pack spawns.</summary>
    PackSpawn,

    /// <summary>A shrine.</summary>
    Shrine,

    /// <summary>A container (loot).</summary>
    Container,

    /// <summary>A Dispenser.</summary>
    Dispenser,

    /// <summary>An explosive barrel.</summary>
    Barrel,

    /// <summary>A trap.</summary>
    Trap,

    /// <summary>A point on the level map (<c>map_marker</c>, with a <c>map_label</c>).</summary>
    MapMarker,

    /// <summary>A player spawn: the level's start when it has no up room (the tools' own type).</summary>
    Spawn,
}

/// <summary>The marker names: the <c>poi_type</c> each kind writes, and which kinds reward a jump.</summary>
public static class Markers
{
    /// <summary>The <c>poi_type</c> of a kind: plan_tf2_rpg.md 6a's names, <c>descent_</c> first.</summary>
    public static string PoiType(MarkerKind kind) => kind switch
    {
        MarkerKind.PackSpawn => "descent_spawn_pack",
        MarkerKind.Shrine => "descent_mark_shrine",
        MarkerKind.Container => "descent_mark_container",
        MarkerKind.Dispenser => "descent_mark_dispenser",
        MarkerKind.Barrel => "descent_mark_barrel",
        MarkerKind.Trap => "descent_mark_trap",
        MarkerKind.MapMarker => "descent_map_marker",
        MarkerKind.Spawn => "spawn",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no such marker"),
    };

    /// <summary>The kind a <c>poi_type</c> names, or null for one that is not a Descent marker.</summary>
    public static MarkerKind? KindOf(string poiType) =>
        Enum.GetValues<MarkerKind>().Where(k => PoiType(k) == poiType).Select(k => (MarkerKind?)k).FirstOrDefault();

    /// <summary>
    /// Whether a marker of this kind pays for a jump to reach it (plan §4.2,
    /// "every jump spot pays"): something a player wants, not a hazard or a
    /// spawn.
    /// </summary>
    public static bool IsReward(MarkerKind kind) => kind is MarkerKind.Container or MarkerKind.Shrine or MarkerKind.Dispenser;
}
