namespace SourceSharp.Abi;

// THE CLIENT'S OWN ENGINE INTERFACES, bound on first use like the shared ones
// (EngineBound.cs): IVEngineClient and IEngineSound's client spelling exist
// only in client.so, and IInputSystem is asked for by the client alone. They
// exist once the client DLL is up, which is after the module registers what
// it composes, so a consumer is handed a forwarder that acquires on its first
// call (BoundForwarders.Client.cs) and throws, naming the interface, if the
// engine refuses it.

internal sealed partial class EngineBoundSet
{
    internal BoundInterface<IFwdEngineClient> Client { get; } =
        new(EngineClient.InterfaceVersion, (EngineSide side, out string why) => EngineClient.Acquire(side, out why));

    internal BoundInterface<IFwdInputSystem> InputSystem { get; } =
        new(global::SourceSharp.Abi.InputSystem.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => global::SourceSharp.Abi.InputSystem.Acquire(factory, out why));

    internal BoundInterface<IFwdEngineSoundClient> SoundClient { get; } =
        new(EngineSoundClient.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => EngineSoundClient.Acquire(factory, out why));
}

internal static partial class EngineBound
{
    /// <summary><c>IVEngineClient</c>. Client side only.</summary>
    internal static IFwdEngineClient Client(EngineSide side) => side.Bound.Client.For(side);

    /// <summary><c>IInputSystem</c>. Client side only.</summary>
    internal static IFwdInputSystem InputSystem(EngineSide side) => side.Bound.InputSystem.For(side);

    /// <summary><c>IEngineSound</c>, the client's. Client side only.</summary>
    internal static IFwdEngineSoundClient SoundClient(EngineSide side) => side.Bound.SoundClient.For(side);
}
