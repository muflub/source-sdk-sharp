using Debug.Api.Server.Interfaces;

namespace SourceSharp.Server;

/// <summary>
/// The hooks one server composition serves its dev API
/// (plan_devapi_interfaces §5.3). Built by that composition, owned by it.
/// </summary>
public sealed class DebugServerHooks(IDebugLog log, IDebugBuild build, IDebugEntityEvents entityEvents, IDebugConsole console,
                                   IDebugEntityCatalog catalog, IDebugStatus status, IDebugNetTraffic netTraffic,
                                   IDebugWorld world, IDebugNetSchema netSchema,
                                   IDebugCounterGroups? counterGroups = null,
                                   IDebugProcess? process = null,
                                   IDebugCommandRun? commandRun = null) : IDebugServerHooks
{
    public IDebugLog Log { get; } = log ?? throw new ArgumentNullException(nameof(log));

    public IDebugBuild Build { get; } = build ?? throw new ArgumentNullException(nameof(build));

    public IDebugEntityEvents EntityEvents { get; } = entityEvents ?? throw new ArgumentNullException(nameof(entityEvents));

    public IDebugEntityCatalog Catalog { get; } = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public IDebugStatus Status { get; } = status ?? throw new ArgumentNullException(nameof(status));

    public IDebugNetTraffic NetTraffic { get; } = netTraffic ?? throw new ArgumentNullException(nameof(netTraffic));

    public IDebugConsole Console { get; } = console ?? throw new ArgumentNullException(nameof(console));

    public IDebugWorld World { get; } = world ?? throw new ArgumentNullException(nameof(world));

    public IDebugNetSchema NetSchema { get; } = netSchema ?? throw new ArgumentNullException(nameof(netSchema));

    public IDebugCounterGroups CounterGroups { get; } = counterGroups ?? new DebugCounterGroups([]);

    public IDebugProcess Process { get; } = process ?? new NoProcess();

    public IDebugCommandRun CommandRun { get; } = commandRun ?? new NoRun();

    /// <summary>No recorders: a composition that hands none over (the tests' default).</summary>
    private sealed class NoProcess : IDebugProcess
    {
        public void Arm(string recordPath) { }
        public DebugExitFacts Read() => DebugProcess.NoFacts;
        public void Disarm() { }
    }

    /// <summary>Nothing runs managedly: a composition that hands no runner over (the tests' default).</summary>
    private sealed class NoRun : IDebugCommandRun
    {
        public IReadOnlyList<string>? TryRunManaged(string name, string args = "") => null;
    }
}
