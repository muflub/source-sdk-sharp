using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>Installs the server's managed sound emitter as its side's sound route (plan_server_native_v2 T4a).</summary>
public static class ServerSoundRouting
{
    /// <summary>
    /// The module's side's sound members answered by <paramref name="route"/>
    /// (null: they make no sound).
    /// </summary>
    public static void Route(ModuleEngine engine, IServerSoundRoute? route)
        => (engine ?? throw new ArgumentNullException(nameof(engine))).Api.ServerSounds = route;
}
