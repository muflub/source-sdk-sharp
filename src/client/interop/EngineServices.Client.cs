namespace SourceSharp.Abi;

public static partial class EngineServices
{
    // The client's own engine interfaces (BoundForwarders.Client.cs).
    static partial void AddSideForwarders(ServiceContainer services, EngineSide side)
    {
        // And its slices, the same object: the Steam launch's connect, the
        // save allocator and the input host's engine calls.
        var client = new BoundEngineClient(side);
        services.AddInstance<IFwdEngineClient>(client);
        services.AddInstance<ISteamLaunchCommands>(client);
        services.AddInstance<ISaveMemory>(client);
        services.AddInstance<Client.Input.IClientInputEngine>(client);
        services.AddInstance<IFwdInputSystem>(new BoundInputSystem(side));
        services.AddInstance<IFwdEngineSoundClient>(new BoundSoundClient(side));
    }
}
