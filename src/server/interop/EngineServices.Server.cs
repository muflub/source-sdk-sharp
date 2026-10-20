namespace SourceSharp.Abi;

public static partial class EngineServices
{
    // The server's own engine interfaces (BoundForwarders.Server.cs, and the
    // shared file's IVEngineServer, whose acquirer EngineServerBinding installs).
    static partial void AddSideForwarders(ServiceContainer services, EngineSide side)
    {
        // And its slices, the same object: the save allocator and the area
        // portal records' three calls.
        var server = new BoundEngineServer(side);
        services.AddInstance<IFwdEngineServer>(server);
        services.AddInstance<ISaveMemory>(server);
        services.AddInstance<SourceSharp.Server.Interop.IAreaPortalEngine>(server);
        services.AddInstance<IFwdEngineSoundServer>(new BoundSoundServer(side));
        services.AddInstance<IFwdFileSystem>(new BoundFileSystem(side));
    }
}
