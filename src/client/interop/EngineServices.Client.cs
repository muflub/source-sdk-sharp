namespace SourceSharp.Abi;

public static partial class EngineServices
{
    // The client's own engine interfaces: IVEngineClient and its slices, the
    // Steam launch's connect, the save allocator and the input host's engine
    // calls, the same object; the input system and the engine sound.
    static partial void AddSideForwarders(ServiceContainer services, EngineSide side)
    {
        IFwdEngineClient client = EngineBound.Client(side);
        services.AddInstance(client);
        services.AddInstance(Slice<ISteamLaunchCommands>(client));
        services.AddInstance(Slice<ISaveMemory>(client));
        services.AddInstance(Slice<Client.Input.IClientInputEngine>(client));
        services.AddInstance(EngineBound.InputSystem(side));
        services.AddInstance(EngineBound.SoundClient(side));
    }
}
