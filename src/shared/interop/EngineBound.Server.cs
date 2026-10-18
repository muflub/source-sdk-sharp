namespace SourceSharp.Abi;

internal sealed partial class EngineBoundSet
{
    // IVEngineServer's wrapper is Server.Interop's (EngineServer: the tables it
    // holds are libss_fwd_server's, one forwarder library per side), so this
    // shared slot does not name it. The server side hands its acquirer in as
    // the side is created (EngineServerBinding.Bind, through SideLoad.Load), before
    // anything asks; a side that never does -- the client, whose engine has no
    // IVEngineServer -- answers every ask by name rather than with a zero.
    private BoundInterface<IVtableEngineServer> _server =
        new(IVtableEngineServer.InterfaceVersion, NoServer);

    internal BoundInterface<IVtableEngineServer> Server => _server;

    /// <summary>The server side's acquirer for IVEngineServer, installed before first use.</summary>
    internal void UseServer(BoundInterface<IVtableEngineServer>.Acquirer acquire)
        => _server = new(IVtableEngineServer.InterfaceVersion, acquire ?? throw new ArgumentNullException(nameof(acquire)));

    private static IVtableEngineServer? NoServer(NativeInterfaceFactory factory, Side side, out string why)
    {
        why = $"the {side} side binds no IVEngineServer ({IVtableEngineServer.InterfaceVersion}): its wrapper is the server's";
        return null;
    }
}

internal static partial class EngineBound
{
    /// <summary><c>IVEngineServer</c>. Server side only.</summary>
    internal static IVtableEngineServer Server(EngineSide side) => side.Bound.Server.For(side);
}
