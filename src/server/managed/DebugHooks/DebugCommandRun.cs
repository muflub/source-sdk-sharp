using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugCommandRun"/> over the composition's real command registry
/// and real container (user ruling 2026-10-07: command capture is all managed
/// logic, no native code).
///
/// <para>
/// A FRESH instance per call — never the registered singleton, which carries
/// whatever state a previous run left behind (the hot-reload lesson). The
/// instance is built through the same <see cref="ServiceActivator"/> the
/// container uses, but from a locator that answers <see cref="ILog"/> and
/// <see cref="IEngineConsole"/> with a sink and everything else from the real
/// container, so the command runs with its real dependencies and only its two
/// print channels are watched.
/// </para>
///
/// <para>
/// The sink collects and FORWARDS: a bystander reading the console — or
/// console.log through -condebug — sees this command's output exactly as they
/// did before capture existed. The server dedups the reply, not the print.
/// </para>
/// </summary>
public sealed class DebugCommandRun(ConCommandRegistry registry,
                                    Func<ServiceContainer> services,
                                    ILog live) : IDebugCommandRun
{
    private readonly ConCommandRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly Func<ServiceContainer> _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly ILog _live = live ?? throw new ArgumentNullException(nameof(live));

    // Constructor plans cached per run object, the way the container caches
    // them per container: captures are rare, but reflection is the same
    // reflection, and the plan never changes for a type. Game-thread only, as
    // the interface requires, so this dictionary needs no lock.
    private readonly Dictionary<Type, ServiceActivator> _activators = new();

    public IReadOnlyList<string>? TryRunManaged(string name, string args = "")
    {
        // A name with no declared class — unregistered, or native-bodied —
        // is not ours to run: answer null and let the caller use the engine
        // path rather than capture a carrier that prints "no body".
        Type? type = _registry.DeclaredType(name);
        if (type is null)
            return null;

        var sink = new Sink(_live, () => _services().Resolve<IEngineConsole>());
        var watched = new ServiceContainer()
            .AddInstance<ILog>(sink)
            .AddInstance<IEngineConsole>(sink);

        object? made;
        try
        {
            made = ActivatorFor(type).Create(new FallbackLocator(watched, _services()));
        }
        catch (ServiceResolutionException)
        {
            // A declared class this build's container cannot fill (a refused
            // command): the registry owns no runnable instance, so the
            // capture has nothing to run and must not claim one.
            return null;
        }
        catch (System.Reflection.TargetInvocationException)
        {
            // The constructor itself threw. The same answer as an unfillable
            // constructor: this is not a command the managed path can run.
            return null;
        }

        if (made is not IConCommand command)
            return null;

        command.Execute(args);
        return sink.Printed;
    }

    private ServiceActivator ActivatorFor(Type type)
    {
        if (!_activators.TryGetValue(type, out var activator))
            _activators[type] = activator = ServiceActivator.For(type);

        return activator;
    }

    /// <summary>
    /// The two channels a command prints with, watched together: what it
    /// printed is collected for the reply, and handed on to the live log so
    /// the print still reaches the console and console.log. Commands print
    /// through <see cref="ILog"/> and <see cref="IEngineConsole"/> — a name
    /// list of the two is what a console command can actually say anything
    /// with — and every other console member is passed to the real console
    /// so setting a convar or running a subcommand still does its job.
    /// </summary>
    private sealed class Sink(ILog live, Func<IEngineConsole?> realConsole) : ILog, IEngineConsole
    {
        private readonly List<string> _lines = new();

        /// <summary>Every line printed, in print order, verbatim.</summary>
        public IReadOnlyList<string> Printed => _lines;

        public void Msg(string message) => Print(message, warning: false);

        public void Warning(string message) => Print(message, warning: true);

        void IEngineConsole.Msg(string message) => Print(message, warning: false);

        void IEngineConsole.Warning(string message) => Print(message, warning: true);

        private void Print(string message, bool warning)
        {
            _lines.Add(message);

            // Forwarded through the log, which is the side's own print path:
            // it reaches the console and the file the same way the command's
            // own logging would. Forwarding the console's Msg as well would
            // print it twice.
            if (warning)
                live.Warning(message);
            else
                live.Msg(message);
        }

        // Everything else is the real console's business, unchanged. A
        // composition with no engine (a test's bare container) answers the
        // neutral value rather than throwing: the capture is not the place a
        // missing engine becomes an error.
        public int RegisterConCommand(string name, string help, ConVarFlags flags)
            => realConsole()?.RegisterConCommand(name, help, flags) ?? -1;

        public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                  float? min = null, float? max = null)
            => realConsole()?.RegisterConVar(name, defaultValue, help, flags, min, max) ?? -1;

        public void ExecuteServerCommand(string command) => realConsole()?.ExecuteServerCommand(command);

        public string? FindConVarValue(string name) => realConsole()?.FindConVarValue(name);

        public string GetConVarValue(int id) => realConsole()?.GetConVarValue(id) ?? "";

        public void SendClientCommand(EntityHandle player, string command)
            => realConsole()?.SendClientCommand(player, command);

        public void SetConVarValue(int id, string value) => realConsole()?.SetConVarValue(id, value);

        public void SetConVarValueByName(string name, string value) => realConsole()?.SetConVarValueByName(name, value);

        public int Snapshot() => realConsole()?.Snapshot() ?? 0;

        public bool SnapshotEntry(int index, out NativeConsoleEntry entry)
        {
            if (realConsole() is { } console)
                return console.SnapshotEntry(index, out entry);

            entry = default;
            return false;
        }
    }
}
