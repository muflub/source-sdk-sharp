using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IClientReconnect"/> over IVEngineClient, acquired on first use:
/// the engine interface exists once the client DLL is up, which is after this
/// module registers its services.
/// </summary>
public sealed class EngineReconnect(IFwdEngineClient engine, ILog log) : IClientReconnect
{
    public void Reconnect()
    {
        log.Msg("[SourceSharp/Client] the server asked this client to reconnect\n");
        engine.Reconnect();
    }
}
