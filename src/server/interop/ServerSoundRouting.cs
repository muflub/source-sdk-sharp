using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>Installs the server's managed sound emitter as its side's sound route (plan_server_native_v2 T4a).</summary>
public static class ServerSoundRouting
{
    /// <summary>
    /// The side's sound members answered by <paramref name="route"/> (null:
    /// they make no sound), through the route its composition registered.
    /// </summary>
    public static void Route(IServiceLocator services, IServerSoundRoute? route)
        => services.Require<SideRoute<IServerSoundRoute>>().Fill(route);
}
