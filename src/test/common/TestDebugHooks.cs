using SourceSharp.Abi;
using SourceSharp.Server;
using Debug.Api.Server.Interfaces;

namespace SourceSharp.Tests;

/// <summary>
/// The hooks a test dev API server is built with: the server's real hook
/// implementations, over test doubles of what the composition would give them.
/// One place, so each P2 group adds its hook here once.
/// </summary>
internal static partial class TestDebugHooks
{
    public static DebugServerHooks Server(ILog? log = null, IServiceLocator? engine = null,
                                        Func<IReadOnlyList<ScriptedClassInfo>>? classes = null,
                                        Func<IReadOnlyList<ScriptedInstanceInfo>>? instances = null,
                                        Func<DebugCounters>? counters = null,
                                        Func<DebugGameStatus>? game = null,
                                        Func<IReadOnlyList<ServiceInfo>>? services = null,
                                        Func<string>? map = null,
                                        Func<NetTrafficReport>? traffic = null,
                                        Action? resetTraffic = null,
                                        IDebugConsole? console = null,
                                        IDebugNetSchema? netSchema = null,
                                        Func<IEnumerable<(EntityHandle A, EntityHandle B)>>? touching = null,
                                        Crossings? crossings = null,
                                        BuildInfo? build = null,
                                        ReloadStore? reload = null,
                                        ScriptedEntityRegistry? registry = null,
                                        ConsoleLines? lines = null,
                                        IDebugWorld? world = null,
                                        IDebugCounterGroups? counterGroups = null,
                                        IDebugProcess? process = null,
                                        IDebugCommandRun? commandRun = null)
    {
        engine ??= TestEngines.Server().Engine;
        return new(new DebugLog(log ?? new RecordingLog(), lines ?? new ConsoleLines()), new DebugBuild(build ?? new BuildInfo(), crossings ?? new Crossings()),
                   new DebugEntityEvents(engine.Require<IEngineEntities>(), registry ?? new ScriptedEntityRegistry(TestEngines.Server())),
                   console ?? DebugConsoleHook.Over(engine.Require<IEngineConsole>(), () => [], () => [], _ => null),
                   new DebugEntityCatalog(classes ?? (() => []), instances ?? (() => [])),
                   new DebugStatus(reload ?? new ReloadStore(Side.Server), counters ?? (() => DebugStatus.ZeroCounters()),
                                 game ?? (() => new DebugGameStatus("", 0, 0, 0, 0)),
                                 services ?? (() => []), map ?? (() => "")),
                   new DebugNetTrafficHook(traffic, resetTraffic ?? (() => { })),
                   world ?? DebugWorld.From(engine, touching),
                   netSchema ?? new DebugNetSchema(() => [], () => EmptyTiers.Instance, () => null, NetEntityBlob.Missing),
                   counterGroups, process, commandRun);
    }
}
