namespace SourceSharp.Abi;

public static partial class EngineServices
{
    // The server's own engine interfaces (BoundForwarders.Server.cs, and the
    // shared file's IVEngineServer, whose acquirer EngineServerBinding installs).
    static partial void AddSideForwarders(ServiceContainer services, EngineSide side)
    {
        services.AddInstance<IFwdEngineServer>(new BoundEngineServer(side));
        services.AddInstance<IFwdEngineSoundServer>(new BoundSoundServer(side));
        services.AddInstance<IFwdFileSystem>(new BoundFileSystem(side));
    }
}
