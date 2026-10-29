namespace SourceSharp.Abi;

public static partial class EngineServices
{
    // The client's own engine interfaces: IVEngineClient and its slices, the
    // Steam launch's connect, the save allocator, the input host's engine
    // calls, the playback state, the level's cl_predict switch and the
    // weapon-selection binds' server line, the same object; the input
    // system and the engine sound.
    static partial void AddSideForwarders(ServiceContainer services, EngineSide side)
    {
        IFwdEngineClient client = EngineBound.Client(side);
        services.AddInstance(client);
        services.AddInstance(Slice<ISteamLaunchCommands>(client));
        services.AddInstance(Slice<ISaveMemory>(client));
        services.AddInstance(Slice<Client.Input.IClientInputEngine>(client));
        services.AddInstance(Slice<IPlaybackState>(client));
        services.AddInstance(Slice<IViolenceSettings>(client));
        services.AddInstance(Slice<IClientPredictCommand>(client));
        services.AddInstance(Slice<IWeaponSelectionCommand>(client));
        services.AddInstance(EngineBound.InputSystem(side));
        services.AddInstance(EngineBound.SoundClient(side));
    }
}
