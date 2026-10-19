using System.Reflection;
using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;

namespace SourceSharp.Client;

/// <summary>
/// Loads the client's half of the dev API, Debug.Api.Client, by name
/// and only when this process was started with
/// <see cref="DebugChannelProtocol.ClientParam"/>: the counterpart of the
/// server's DebugApiLoader (plan_side_isolation §2.6).
/// </summary>
internal static class DebugApiClientLoader
{
    private const string AssemblyName = "Debug.Api.Client";

    /// <summary>Whether the command line asks for the client's dev API at all.</summary>
    /// <summary>
    /// The flag that asks for the client's dev API: the loader's own, as the
    /// server's DebugApiLoader has its -debugapi. The dev API reads the same flag
    /// for its own options; a fact holds the two spellings equal.
    /// </summary>
    public const string EnableParam = "-debugui";

    public static bool RequestedBy(IReadOnlyList<string> args)
        => LocalCommandLine.Has(args, EnableParam);

    /// <param name="hooks">What this composition serves the client dev API (P3).</param>
    public static IDebugApiClient? LoadIfRequested(IReadOnlyList<string> args, IDebugClientHooks hooks, ILog log)
    {
        if (!RequestedBy(args))
            return null;

        try
        {
            // By name: the side's load context probes the managed directory,
            // so it lands in the client's collectible context and goes with it.
            var assembly = Assembly.Load(AssemblyName);

            var type = assembly.GetTypes().FirstOrDefault(
                t => typeof(IDebugApiClient).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (type is null)
            {
                log.Warning($"[SourceSharp/Client] {AssemblyName} has no IDebugApiClient implementation\n");
                return null;
            }

            return Activator.CreateInstance(type, args, hooks) as IDebugApiClient;
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp/Client] could not load {AssemblyName}: {e.Message}\n");
            return null;
        }
    }
}
