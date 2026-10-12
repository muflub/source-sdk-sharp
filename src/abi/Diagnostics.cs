using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// Managed-side crash diagnostics.
///
/// Post-mortem core analysis is close to useless for managed faults: CoreCLR
/// prints "Stack overflow." and aborts *after* unwinding, so a core shows only
/// idle threads. Everything here exists to get the information into the Source
/// console at the moment it happens, while the stack still exists.
/// </summary>
public static class ManagedDiagnostics
{
    /// <summary>
    /// Installs process-wide handlers, once per side state (statics to zero:
    /// the "already installed" flag is the state's, which the Bootstrap keeps
    /// for the side root's life, so a reload does not install a second set).
    /// Safe to call more than once.
    ///
    /// Catches three cases the per-call try/catch in Entry cannot:
    ///  - exceptions on threads SourceSharp did not start (thread pool, GC finalizer)
    ///  - unobserved Task exceptions, which are otherwise swallowed entirely
    ///  - first-chance exceptions, which show the throw site even when something
    ///    downstream catches and hides it
    /// </summary>
    public static void Install(SideState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CrashHandlersInstalled) return;
        state.CrashHandlersInstalled = true;

        // The handlers report through the state's console as it is when they
        // fire (statics to zero): the console of the load that is running then.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            ILog log = LogOf(state);
            log.Warning("[SourceSharp] ===== UNHANDLED MANAGED EXCEPTION =====\n");
            log.Warning(Describe(e.ExceptionObject as Exception));
            log.Warning($"[SourceSharp] runtime terminating: {e.IsTerminating}\n");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ILog log = LogOf(state);
            log.Warning("[SourceSharp] ===== UNOBSERVED TASK EXCEPTION =====\n");
            log.Warning(Describe(e.Exception));
            e.SetObserved();
        };

        LogOf(state).Msg("[SourceSharp] managed crash handlers installed\n");
        ReportDumpConfiguration(LogOf(state));
    }

    /// <summary>
    /// Enables first-chance logging. Noisy — every throw is reported even if
    /// handled — so it is opt-in via the cs_firstchance command rather than on
    /// by default.
    /// </summary>
    /// <returns>
    /// The subscription when <paramref name="enabled"/>, which the caller holds
    /// and disposes to turn it off (statics to zero: the handler is the
    /// caller's, over the caller's log); null when turning off.
    /// </returns>
    public static IDisposable? SetFirstChanceLogging(ILog log, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(log);
        log.Msg($"[SourceSharp] first-chance exception logging {(enabled ? "ON" : "OFF")}\n");
        return enabled ? new FirstChance(log) : null;
    }

    private sealed class FirstChance : IDisposable
    {
        private readonly EventHandler<FirstChanceExceptionEventArgs> _handler;

        public FirstChance(ILog log)
        {
            _handler = (_, e) => log.Warning($"[SourceSharp] first-chance: {e.Exception.GetType().Name}: {e.Exception.Message}\n");
            AppDomain.CurrentDomain.FirstChanceException += _handler;
        }

        public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= _handler;
    }

    /// <summary>The state's console, or standard error before a load has one.</summary>
    private static ILog LogOf(SideState state) => state.Log ?? new StandardErrorLog();


    /// <summary>
    /// Formats an exception with its full chain and stack. Uses the Source
    /// console rather than stdout so it lands in console.log next to the native
    /// trace, which is what makes the two correlatable.
    /// </summary>
    public static string Describe(Exception? ex)
    {
        if (ex is null)
            return "[SourceSharp] (no exception object)\n";

        var sb = new StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            sb.Append("[SourceSharp] ").Append(e.GetType().FullName).Append(": ").Append(e.Message).Append('\n');

            if (!string.IsNullOrEmpty(e.StackTrace))
            {
                foreach (var line in e.StackTrace.Split('\n'))
                    sb.Append("[SourceSharp]   ").Append(line.TrimEnd()).Append('\n');
            }

            if (e.InnerException is not null)
                sb.Append("[SourceSharp] --- caused by ---\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reports whether CoreCLR is configured to write a managed dump. These are
    /// environment variables read at runtime startup, so they must be set before
    /// the game launches — reporting them here makes it obvious when they are
    /// not in effect.
    /// </summary>
    public static void ReportDumpConfiguration(ILog log)
    {
        string? enabled = Environment.GetEnvironmentVariable("DOTNET_DbgEnableMiniDump");

        if (enabled == "1")
        {
            string type = Environment.GetEnvironmentVariable("DOTNET_DbgMiniDumpType") ?? "(default)";
            string path = Environment.GetEnvironmentVariable("DOTNET_DbgMiniDumpName") ?? "(default)";
            log.Msg($"[SourceSharp] managed dumps ENABLED (type={type}, path={path})\n");
        }
        else
        {
            log.Msg(
                "[SourceSharp] managed dumps disabled. To enable, launch with:\n" +
                "[SourceSharp]   DOTNET_DbgEnableMiniDump=1 DOTNET_DbgMiniDumpType=4 \\\n" +
                "[SourceSharp]   DOTNET_DbgMiniDumpName=/tmp/sourcesharp.%d.dmp\n");
        }
    }

    /// <summary>Runtime facts worth having in the log before anything goes wrong.</summary>
    public static void ReportRuntimeInfo(ILog log)
    {
        log.Msg($"[SourceSharp] .NET {Environment.Version} / {RuntimeInformation.FrameworkDescription}\n");
        log.Msg($"[SourceSharp] GC: server={System.Runtime.GCSettings.IsServerGC}, " +
                   $"latency={System.Runtime.GCSettings.LatencyMode}\n");
        log.Msg($"[SourceSharp] process id {Environment.ProcessId}, {Environment.ProcessorCount} cpus\n");
    }
}
