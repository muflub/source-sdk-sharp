namespace SourceSharp.Abi;

internal sealed partial class EngineBoundSet
{
    internal BoundInterface<IVtableEngineServer> Server { get; } =
        new(EngineServer.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => EngineServer.Acquire(factory, out why));
}

internal static partial class EngineBound
{
    /// <summary><c>IVEngineServer</c>. Server side only.</summary>
    internal static IVtableEngineServer Server(EngineSide side) => side.Bound.Server.For(side);
}
