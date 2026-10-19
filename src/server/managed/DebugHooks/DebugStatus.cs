using SourceSharp.Abi;
using Debug.Api.Server.Interfaces;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugStatus"/> for one server composition (group G7). The
/// composition supplies its counters, game-thread state, container and map
/// as functions read on every call; the reload record is its side state's
/// ReloadStore, handed in (statics to zero).
/// </summary>
public sealed class DebugStatus(
    ReloadStore reload,
    Func<DebugCounters> counters,
    Func<DebugGameStatus> game,
    Func<IReadOnlyList<ServiceInfo>> services,
    Func<string> currentMap) : IDebugStatus
{
    private readonly Func<DebugCounters> _counters = counters ?? throw new ArgumentNullException(nameof(counters));
    private readonly Func<DebugGameStatus> _game = game ?? throw new ArgumentNullException(nameof(game));
    private readonly Func<IReadOnlyList<ServiceInfo>> _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly Func<string> _currentMap = currentMap ?? throw new ArgumentNullException(nameof(currentMap));

    public DebugCounters Counters() => _counters();

    public DebugGameStatus Game() => _game();

    public string CurrentMap => _currentMap();

    public IReadOnlyList<DebugService> Services()
        => _services().Select(s => new DebugService(s.Key, s.LookedUpBy, s.Kind, s.Implementation, s.Built)).ToArray();

    public DebugReload Reload()
    {
        var (failed, first) = reload.RestoreFailures;
        return new DebugReload(
            Tally(reload.Saved), Tally(reload.Restored), failed, first,
            ReloadCarrier.Count(reload), ReloadCarrier.PartCount(reload),
            ReloadCarrier.IoCount(reload), ReloadCarrier.ShellLessCount(reload));
    }

    private static DebugReloadTally Tally(ReloadStore.ReloadTally t)
        => new(t.Entities, t.Classes, t.Fields, t.Bytes, t.Parts, t.PartFields, t.PartBytes,
               t.IoEvents, t.IoConnections, t.ShellLess);

    /// <summary>Every counter zero: what /status reported for a host that wired none.</summary>
    public static DebugCounters ZeroCounters() => new(
        new DebugThink(0, 0, 0, 0), new DebugIo(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), new DebugTouch(0, 0, 0, 0, 0, 0, 0),
        new DebugMapSpawn(0, 0, 0, 0, 0, 0), new DebugFactories(false, 0, 0, 0, 0), new DebugLogical(0, 0, 0, 0, 0, 0),
        new DebugOverlayCounts(0, 0, 0, 0, false));
}
