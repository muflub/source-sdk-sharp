namespace SourceSharp.Abi;

internal sealed partial class EngineBoundSet
{
    // IVEngineServer's wrapper is Server.Interop's (EngineServer: the tables it
    // holds are libss_fwd_server's, one forwarder library per side), so this
    // shared slot does not name it. The server side hands its acquirer in as
    // the side is created (EngineServerBinding.Bind, through SideLoad.Load), before
    // anything asks; a side that never does -- the client, whose engine has no
    // IVEngineServer -- answers every ask by name rather than with a zero.
    private BoundInterface<IFwdEngineServer> _server =
        new(IFwdEngineServer.InterfaceVersion, NoServer);

    internal BoundInterface<IFwdEngineServer> Server => _server;

    /// <summary>
    /// A set that holds nothing yet and keeps the acquirer installed here: the
    /// attach starts one, after SideLoad.Load has installed the server's.
    /// </summary>
    internal EngineBoundSet Unheld() => new() { _server = _server.Unheld() };

    /// <summary>The server side's acquirer for IVEngineServer, installed before first use.</summary>
    internal void UseServer(BoundInterface<IFwdEngineServer>.Acquirer acquire)
        => _server = new(IFwdEngineServer.InterfaceVersion, acquire ?? throw new ArgumentNullException(nameof(acquire)));

    private static IFwdEngineServer? NoServer(NativeInterfaceFactory factory, Side side, out string why)
    {
        why = $"the {side} side binds no IVEngineServer ({IFwdEngineServer.InterfaceVersion}): its wrapper is the server's";
        return null;
    }
}

internal static partial class EngineBound
{
    /// <summary><c>IVEngineServer</c>. Server side only.</summary>
    internal static IFwdEngineServer Server(EngineSide side) => side.Attachment.Bound.Server.For(side);
}
