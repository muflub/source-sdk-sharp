using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Descent.Server.Mode;

namespace TF2.Descent.Server.Level;

/// <summary>
/// Places arriving heroes (mine plan D1, D2) through the core's spawn chain: on a Descent level,
/// at the level's arrival points (<see cref="LevelData.Arrivals"/>, made into
/// <c>info_player_deathmatch</c> spots named <see cref="ArrivalName"/> the first time one is
/// asked for), or, on a level reached by its way up from the level below
/// (<see cref="LevelService.CameUp"/>), at its way down's points (<see cref="WayDownPoints"/>);
/// on a map reached by going up from a level in this process (the town), at its own spots of
/// that name. Only while the Descent mode is active; anywhere else it names no spot, and the
/// chain goes on as before (TF2's team walk, the core's walk).
/// </summary>
[SideService(Side.Server)]
public sealed class ArrivalSpawns(LevelService level, IDescentModeState mode, IEngineEntities entities, ILog log)
    : ISpawnSelector, IOrderedHook
{
    /// <summary>The town's arrival spawns' targetname (src/mapgen/TF2DescentTown.cs), given to a level's too.</summary>
    public const string ArrivalName = "tf2_descent_arrival";

    /// <summary>After TF2's band, beside the level's own service.</summary>
    public const int Order = ModStages.First + 55;

    public int HookOrder => Order;

    private int _serial = -1;
    private List<EntityHandle> _spots = [];
    private readonly Dictionary<EntityHandle, int> _held = [];

    public EntityHandle SelectSpawn(EntityHandle player)
    {
        if (!mode.Active)
            return EntityHandle.None;

        if (_serial != level.Serial)
        {
            _serial = level.Serial;
            _held.Clear();
            _spots = Spots();
        }

        if (_spots.Count == 0)
            return EntityHandle.None;

        foreach (EntityHandle gone in _held.Keys.Where(h => !entities.IsValid(h)).ToList())
            _held.Remove(gone);
        int? had = _held.TryGetValue(player, out int h) ? h : null;
        int pick = ArrivalPicker.Pick(_spots.Count, [.. _held.Where(kv => kv.Key != player).Select(kv => kv.Value)], had);
        _held[player] = pick;
        if (had != pick)
            log.Msg($"[TF2Descent/Level] {player} arrives at point {pick} of {_spots.Count} {LevelSummary.At(entities.GetOrigin(_spots[pick]))}\n");
        return _spots[pick];
    }

    private List<EntityHandle> Spots()
    {
        if (level.Current is { } here && level.CameUp && WayDownPoints(here) is { Count: > 0 } below)
        {
            var made = Make(below);
            log.Msg($"[TF2Descent/Level] {here.Map}: came up from the level below: {made.Count} of {below.Count} way-down points made spawn spots\n");
            return made;
        }

        if (level.Current is { Arrivals.Length: > 0 } data)
        {
            var made = Make([.. data.Arrivals.Select(a => (a.Position, a.Yaw))]);
            log.Msg($"[TF2Descent/Level] {data.Map}: {made.Count} of {data.Arrivals.Length} arrival points made spawn spots\n");
            return made;
        }

        if (level.ReturnedFromLevel)
        {
            var named = entities.FindAllByClassname(EntityNames.InfoPlayerDeathmatch).Where(e => entities.GetName(e) == ArrivalName).ToList();
            log.Msg($"[TF2Descent/Level] returned from a level: {named.Count} {ArrivalName} spawns\n");
            return named;
        }

        return [];
    }

    private List<EntityHandle> Make(IReadOnlyList<(Vector Position, float Yaw)> points)
    {
        var made = new List<EntityHandle>();
        foreach (var (position, yaw) in points)
        {
            EntityHandle spot = entities.Create(EntityNames.InfoPlayerDeathmatch);
            if (!entities.IsValid(spot))
                continue;
            entities.SetKeyValue(spot, "targetname", ArrivalName);
            entities.SetOrigin(spot, position);
            entities.SetAngles(spot, new Vector(0, yaw, 0));
            entities.Spawn(spot);
            made.Add(spot);
        }
        return made;
    }

    /// <summary>
    /// Where a party coming up from the level below stands: the <c>spawn</c> and <c>arrival</c>
    /// markers of the level's way down room (<see cref="LevelRoomRole.Exit"/>; each library exit room
    /// carries its own); empty when it has none, and the level's arrival points serve.
    /// </summary>
    public static List<(Vector Position, float Yaw)> WayDownPoints(LevelData data)
    {
        int room = data.Exit?.Room ?? -1;
        for (int i = 0; room < 0 && i < data.Rooms.Length; i++)
            if (data.Rooms[i].Role == LevelRoomRole.Exit)
                room = i;
        return room < 0
            ? []
            : [.. data.In(room).Where(m => m.Kind is LevelMarkerKind.Spawn or LevelMarkerKind.Arrival).Select(m => (m.Position, m.Yaw))];
    }
}
