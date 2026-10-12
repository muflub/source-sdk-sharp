using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server.Interface;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Loads the development API, but only when the process asked for it.
///
/// The switch is checked here rather than inside the implementation, because
/// checking it there would mean loading the assembly to find out whether to
/// load the assembly.
/// </summary>
internal static class DebugApiLoader
{
    /// <summary>Turns the API on at all.</summary>
    public const string EnableParam = "-debugapi";

    private const string AssemblyName = "Debug.Api.Server";

    /// <param name="commandLine">The process's command line, which says whether to load at all.</param>
    /// <param name="hooks">What this composition serves the API (plan_devapi_interfaces §5.3).</param>
    public static IDebugApi? LoadIfRequested(ILocalCommandLine commandLine, IDebugServerHooks hooks, ILog log)
    {
        // The command line arrives as a parameter rather than being resolved
        // here: this decides whether to LOAD the development API at all, and it
        // is called while the module's container is still being built.
        if (!LocalCommandLine.Has(commandLine.Args, EnableParam))
            return null;

        try
        {
            // By name, not by path: the side's load context already probes the
            // managed directory, so this lands in the same collectible context
            // as the rest of the module and goes away with it on unload.
            var assembly = Assembly.Load(AssemblyName);

            var type = assembly.GetTypes().FirstOrDefault(
                t => typeof(IDebugApi).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (type is null)
            {
                log.Warning($"[SourceSharp] {AssemblyName} has no IDebugApi implementation\n");
                return null;
            }

            // The argument list, not the Shared service: the dev API names nothing
            // of the game but the ABI and its contracts (plan_side_isolation §2.6).
            // And this side's container, which is an ABI type.
            return Activator.CreateInstance(type, commandLine.Args, hooks) as IDebugApi;
        }
        catch (Exception e)
        {
            // Missing or broken, this is a development tool and the game should
            // carry on without it — but silently would leave someone staring at
            // a socket that never appears.
            log.Warning($"[SourceSharp] could not load {AssemblyName}: {e.Message}\n");
            return null;
        }
    }
}
