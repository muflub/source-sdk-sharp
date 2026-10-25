namespace SourceSharp.Abi;

public static partial class EngineServices
{
    // The server's own engine interfaces: IVEngineServer (whose acquirer
    // EngineServerBinding installs) and its slices, the save allocator and
    // the area portal records' three calls, the same object; the engine
    // sound and the filesystem.
    static partial void AddSideForwarders(ServiceContainer services, EngineSide side)
    {
        IFwdEngineServer server = EngineBound.Server(side);
        services.AddInstance(server);
        services.AddInstance(Slice<ISaveMemory>(server));
        services.AddInstance(Slice<SourceSharp.Server.Interop.IAreaPortalEngine>(server));
        services.AddInstance(EngineBound.SoundServer(side));
        services.AddInstance(EngineBound.FileSystem(side));
    }

    // The server's entity motion (teleports, velocity writes over vphysics),
    // which its entity binding takes by constructor, and its six late routes
    // (plan_engineside_removal Phase 3), empty until the composition fills
    // them: the bindings that read one take it by constructor.
    static partial void AddOwnServices(ServiceContainer services, EngineSide side)
    {
        services.AddInstance(side.Motion);
        services.AddInstance(side.ServerSounds);
        services.AddInstance(side.TraceCost);
        services.AddInstance(side.Tracer);
        services.AddInstance(side.PlayerMoves);
        services.AddInstance(side.PhysicsInit);
        services.AddInstance(side.Teleported);
    }
}
