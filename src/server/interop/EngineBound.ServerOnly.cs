namespace SourceSharp.Abi;

// THE SERVER'S OWN ENGINE INTERFACES beside IVEngineServer (whose slot is the
// shared file's, installed by EngineServerBinding): IEngineSound's server
// spelling and the full IFileSystem, bound on first use like the shared ones
// (EngineBound.cs).

internal sealed partial class EngineBoundSet
{
    internal BoundInterface<IFwdEngineSoundServer> SoundServer { get; } =
        new(EngineSoundServer.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => EngineSoundServer.Acquire(factory, out why));

    internal BoundInterface<IFwdFileSystem> FileSystem { get; } =
        new(EngineFileSystemServer.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => EngineFileSystemServer.Acquire(factory, out why));
}

internal static partial class EngineBound
{
    /// <summary><c>IEngineSound</c>, the server's. Server side only.</summary>
    internal static IFwdEngineSoundServer SoundServer(EngineSide side) => side.Bound.SoundServer.For(side);

    /// <summary><c>IFileSystem</c>. Server side only.</summary>
    internal static IFwdFileSystem FileSystem(EngineSide side) => side.Bound.FileSystem.For(side);
}
