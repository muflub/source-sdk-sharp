extern alias client;
using System.Reflection;
using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// Every ConVar the two game assemblies register, each through its own side's
/// registry. Each side compiles its own copy of the shared interop
/// (plan_shim_areas_split phase 3), so the server's registry finds nothing in
/// the client's assembly: its declarations are of the client's ConVarBase.
/// </summary>
internal static class BothSidesConVars
{
    /// <summary>The name and declared default of every ConVar either assembly registers.</summary>
    public static List<(string Name, string DefaultValue)> Registered(Assembly server, Assembly client)
    {
        var serverRegistry = new ConVarRegistry(TestServices.UnattachedConsole());
        serverRegistry.RegisterAll(server);

        var console = ClientDoubles.Require<client::SourceSharp.Abi.IEngineConsole>(ClientDoubles.UnattachedEngine());
        var clientRegistry = new client::SourceSharp.Abi.ConVarRegistry(console);
        clientRegistry.RegisterAll(client);

        return serverRegistry.All.Select(c => (c.Name, c.DefaultValue))
                             .Concat(clientRegistry.All.Select(c => (c.Name, c.DefaultValue)))
                             .ToList();
    }
}
