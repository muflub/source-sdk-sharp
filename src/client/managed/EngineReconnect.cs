using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IClientReconnect"/> over IVEngineClient, acquired on first use:
/// the engine interface exists once the client DLL is up, which is after this
/// module registers its services.
/// </summary>
public sealed class EngineReconnect(Func<IVtableEngineClient?> acquire, ILog log) : IClientReconnect
{
    private IVtableEngineClient? _engine;

    public static EngineReconnect FromEngine(ModuleEngine engine, ILog log)
        => new(() => EngineClient.Acquire(engine, out _), log);

    public void Reconnect()
    {
        _engine ??= acquire();
        if (_engine is null)
        {
            log.Warning("[SourceSharp/Client] no IVEngineClient to reconnect with\n");
            return;
        }

        log.Msg("[SourceSharp/Client] the server asked this client to reconnect\n");
        _engine.Reconnect();
    }
}
