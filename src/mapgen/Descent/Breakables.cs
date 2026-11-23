using System.Globalization;
using SourceSharp.MapGen.Descent.Dressing;
using TF2.Descent.Rules.Layout;

namespace SourceSharp.MapGen.Descent;

/// <summary>What a breakable is: the server's model and size class (M34).</summary>
public enum BreakableKind
{
    /// <summary>A mining crate (<c>props_2fort/miningcrate001</c>).</summary>
    Crate,

    /// <summary>A wooden barrel (<c>props_farm/wooden_barrel</c>).</summary>
    Barrel,
}

/// <summary>
/// The breakable crates and barrels (ruling M34, brief p12): one breakable-prop
/// manager entity per level draws, collides and breaks them, so a room writes
/// no prop for one, only an <c>info_poi</c> where the dresser would have stood
/// it. The room compile carries the point into the level's <c>.nav3d</c>, the
/// server reads it there and its <c>furniture.json</c> places a breakable on
/// it. A theme's prop that becomes one names its kind
/// (<see cref="PropModel.Breakable"/>); the dresser places the server's model
/// (<see cref="Model"/>) in its stead, by its box, while the room has room
/// under its cap, and keeps the theme's prop past it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The point.</b> <c>poi_type</c> is <see cref="PoiType"/> (the type is the
/// model and size class, no other key is read); <c>origin</c> the breakable's
/// foot on the floor: for a crate where the model's origin stands, for a
/// barrel the floor under its middle (the server lifts the barrel's model
/// <see cref="BarrelLift"/>); <c>angles</c> <c>0 yaw 0</c>.
/// </para>
/// <para>
/// <b>The caps.</b> At most <see cref="RoomCap"/> a room and
/// <see cref="LevelCap"/> a level. Neither link (the map tools' nor the
/// host's) can turn a point back into a prop, so the level cap is held in
/// the room compile: a depth's rooms each take at most
/// <see cref="CapAt"/>, the level cap shared over the rooms the depth's
/// levels place (<c>DepthTable</c>'s <c>Rooms</c>, exact, M16), and any level
/// of the depth, whatever rooms it draws, holds no more than the level cap.
/// </para>
/// </remarks>
public static class Breakables
{
    /// <summary>The most breakables one room holds (the server's ask).</summary>
    public const int RoomCap = 8;

    /// <summary>The most breakables one level holds (the server's ask).</summary>
    public const int LevelCap = 96;

    /// <summary>How far the server lifts a barrel's model above its point (the model's origin is its middle).</summary>
    public const int BarrelLift = 30;

    /// <summary>The server's crate: its model and its solid box about its point.</summary>
    public static PropModel Crate { get; } =
        new("models/props_2fort/miningcrate001.mdl", new(-21, -36, 0), new(22, 37, 39), Static: false) { Breakable = BreakableKind.Crate };

    /// <summary>The server's barrel: its model and its solid box above its point (its foot).</summary>
    public static PropModel Barrel { get; } =
        new("models/props_farm/wooden_barrel.mdl", new(-23, -23, 0), new(23, 23, 60), Static: false) { Breakable = BreakableKind.Barrel };

    /// <summary>The server's model of a kind.</summary>
    public static PropModel Model(BreakableKind kind) => kind == BreakableKind.Crate ? Crate : Barrel;

    /// <summary>Whether a model is one the server draws (<see cref="Crate"/>, <see cref="Barrel"/>), not a theme's prop.</summary>
    public static bool IsServerModel(PropModel model) => ReferenceEquals(model, Crate) || ReferenceEquals(model, Barrel);

    /// <summary>The <c>poi_type</c> of a kind.</summary>
    public static string PoiType(BreakableKind kind) => kind switch
    {
        BreakableKind.Crate => "descent_mark_breakable_crate",
        BreakableKind.Barrel => "descent_mark_breakable_barrel",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no such breakable"),
    };

    /// <summary>The kind a <c>poi_type</c> names, or null.</summary>
    public static BreakableKind? KindOf(string? poiType) =>
        poiType == PoiType(BreakableKind.Crate) ? BreakableKind.Crate
        : poiType == PoiType(BreakableKind.Barrel) ? BreakableKind.Barrel
        : null;

    /// <summary>Whether an entity is a breakable's point.</summary>
    public static bool Is(RoomEntity e) => e.ClassName == "info_poi" && e.Origin is not null && KindOf(e.Key("poi_type")) is not null;

    /// <summary>A room's breakables' points, in entity order.</summary>
    public static IReadOnlyList<RoomEntity> Of(Room room) => [.. room.Entities.Where(Is)];

    /// <summary>A breakable point's yaw (the <c>angles</c>' second number).</summary>
    public static int Yaw(RoomEntity e) =>
        (int)float.Parse((e.Key("angles") ?? "0 0 0").Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);

    /// <summary>The solid box a breakable's point stands for: the server's model's box, turned, about the point.</summary>
    public static Box BoxOf(RoomEntity e)
    {
        PropModel m = Model(KindOf(e.Key("poi_type")) ?? throw new ArgumentException("not a breakable's point", nameof(e)));
        var (lo, hi) = m.Turned(Yaw(e));
        Point o = e.Origin!.Value;
        return new Box(o + lo, o + hi);
    }

    /// <summary>A breakable's point: <paramref name="kind"/> at <paramref name="origin"/> (its foot), turned <paramref name="yaw"/>, with the dresser's role key.</summary>
    public static RoomEntity Poi(BreakableKind kind, Point origin, int yaw, string role) =>
        new("info_poi", origin,
        [
            new("poi_type", PoiType(kind)),
            new("angles", string.Create(CultureInfo.InvariantCulture, $"0 {((yaw % 360) + 360) % 360} 0")),
            new(RoomDresser.RoleKey, role),
        ], [], []);

    /// <summary>The breakables a room holds at most when a level places <paramref name="rooms"/> rooms: the level cap shared over them, never over the room cap.</summary>
    public static int CapFor(int rooms) => Math.Min(RoomCap, LevelCap / Math.Max(1, rooms));

    /// <summary>The breakables a room of <paramref name="depth"/>'s library holds at most (<see cref="CapFor"/> of the depth's rooms).</summary>
    public static int CapAt(int depth) => CapFor(DepthTable.For(depth).Rooms);

    /// <summary>The map tools' standing agent (the library's default <c>nav_agents</c>): a point of interest stands where it fits.</summary>
    public const int AgentHalfWidth = 16, AgentHeight = 72;

    /// <summary>The navigation voxel the room compile stands a point in.</summary>
    public const int NavVoxel = 16;

    /// <summary>
    /// The box the room compile asks to be free for its standing agent at a point
    /// (map tools <c>RoomNavBuilder.PlacePois</c>, <c>Nav3dClearance</c>): the voxel
    /// the point falls in (a point on a boundary belongs to the voxel above),
    /// widened by the agent's half-width, from the voxel's floor to its top plus
    /// the agent's height. Brushes and solid static props in it refuse the point.
    /// </summary>
    public static Box AgentSweep(Point at)
    {
        float x0 = MathF.Floor(at.X / NavVoxel) * NavVoxel, y0 = MathF.Floor(at.Y / NavVoxel) * NavVoxel, z0 = MathF.Floor(at.Z / NavVoxel) * NavVoxel;
        return new Box(new(x0 - AgentHalfWidth, y0 - AgentHalfWidth, z0), new(x0 + NavVoxel + AgentHalfWidth, y0 + NavVoxel + AgentHalfWidth, z0 + NavVoxel + AgentHeight));
    }

    /// <summary>Whether a brush of <paramref name="solids"/> or one of the solid static props' boxes <paramref name="props"/> stands in an agent box (touching is not).</summary>
    public static bool AgentBlocked(Box sweep, RoomSolids solids, IEnumerable<Box> props) =>
        solids.Blocked(sweep.Mins, sweep.Maxs) || props.Any(p => p.Overlaps(sweep));
}
