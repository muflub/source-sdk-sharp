using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Server.Level;

/// <summary>
/// The level as data (plan_descent_logic WO1): what the server knows of the Descent level it is
/// on. Null on the town and on any map that is not a generated level.
/// </summary>
public interface ILevelData
{
    /// <summary>The current level, read when its entities exist; null on the town, on another map, and between levels.</summary>
    LevelData? Current { get; }

    /// <summary>
    /// The current level as the horde sees it (<see cref="LevelAdapters.ToHorde(LevelData, ulong, string?)"/>),
    /// rolled from the level's seed; null while <see cref="Current"/> is, or while it has no seed (a
    /// hosted level before Booting answers).
    /// </summary>
    HordeLevel? Horde { get; }
}

/// <summary>
/// Reads each Descent level's <c>.nav3d</c> and <c>.map2d</c> once its entities exist, keeps
/// them as <see cref="LevelData"/>, and says what it read in one line
/// (<see cref="LevelSummary.LoadLine"/>). The seed is the map name's on an unhosted level
/// (<c>tf2_descent-&lt;d&gt;-s&lt;seed&gt;</c>) and Booting's instance seed on a hosted one, which
/// replaces it when it answers.
/// </summary>
[SideService(Side.Server, As = [typeof(ILevelData)])]
public sealed class LevelService(IEngineFiles files, ILog log) : ILevelData, IGameLifecycle, IHostBootHook, IOrderedHook
{
    /// <summary>
    /// After the host service (<see cref="HostService"/> at +10) and before the mode's and the
    /// horde's level work, which read <see cref="Current"/>.
    /// </summary>
    public const int Order = ModStages.First + 50;

    public int LifecycleOrder => Order;

    public int HookOrder => Order;

    private string? _map;
    private string? _previousMap;
    private ulong? _bootSeed;
    private HordeLevel? _horde;

    public LevelData? Current { get; private set; }

    public HordeLevel? Horde => _horde ??= Current is { Seed: not null } level ? LevelAdapters.ToHorde(level) : null;

    /// <summary>Counts levels read, so a consumer that keeps per-level state can tell one level from the next.</summary>
    public int Serial { get; private set; }

    /// <summary>
    /// Whether this map was reached by going up from a Descent level in this process: the town a
    /// party returns to (mine plan D2). A server cannot tell for a player who joined it fresh.
    /// </summary>
    public bool ReturnedFromLevel => _previousMap is { } previous && LevelNames.IsLevel(previous) && _map is { } map && !LevelNames.IsLevel(map);

    public void OnLevelInit(string mapName)
    {
        _previousMap = _map;
        _map = mapName;
        _horde = null;
        Current = null;
        Serial++;
        if (!LevelNames.IsLevel(mapName))
            return;

        ulong? seed = _bootSeed ?? LevelNames.Seed(mapName);
        string from = _bootSeed is not null ? "booting" : seed is not null ? "map name" : "not yet: Booting has not answered";
        var clock = Stopwatch.StartNew();
        LevelData level = LevelReader.Read(new LevelFiles(mapName, LevelNames.Depth(mapName), seed, from,
            files.ReadGameFileBytes($"maps/{mapName}.nav3d"), files.ReadGameFileBytes($"maps/{mapName}.map2d")));
        clock.Stop();
        Current = level;
        log.Msg(LevelSummary.LoadLine(level, clock.Elapsed));
        foreach (string note in level.Notes)
            log.Msg($"[TF2Descent/Level] {mapName}: {note}\n");
    }

    public void OnLevelShutdown()
    {
        _bootSeed = null;
        _horde = null;
        Current = null;
    }

    /// <summary>A hosted level's rolls replay from Booting's instance seed (the host's <c>KillRoll</c> seed).</summary>
    public void OnBooted(BootInfo info)
    {
        _bootSeed = info.InstanceSeed;
        if (Current is not { } level)
            return;
        Current = level with
        {
            Seed = info.InstanceSeed,
            SeedFrom = "booting",
            Sources = level.Sources.SetItem(LevelPiece.Seed, PieceSource.Shipped),
        };
        _horde = null;
        log.Msg($"[TF2Descent/Level] {level.Map}: seed {info.InstanceSeed} from booting\n");
    }
}
