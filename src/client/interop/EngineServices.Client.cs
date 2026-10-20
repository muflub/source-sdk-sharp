namespace SourceSharp.Abi;

public static partial class EngineServices
{
    // The client's own engine interfaces (BoundForwarders.Client.cs).
    static partial void AddSideForwarders(ServiceContainer services, EngineSide side)
    {
        services.AddInstance<IFwdEngineClient>(new BoundEngineClient(side));
        services.AddInstance<IFwdInputSystem>(new BoundInputSystem(side));
        services.AddInstance<IFwdEngineSoundClient>(new BoundSoundClient(side));
    }
}
