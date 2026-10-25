namespace SourceSharp.Abi;

// THE CLIENT'S OWN ENGINE INTERFACES, bound on first use like the shared ones
// (EngineBound.cs): IVEngineClient and IEngineSound's client spelling are the
// client's, and IInputSystem is asked for by the client alone. The composition
// acquires them as it registers them (EngineServices.Client.cs), inside
// CShellClientDLL::Init, which holds the engine's factory by then.

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
    internal static IFwdEngineClient Client(EngineSide side) => side.Attachment.Bound.Client.For(side);

    /// <summary><c>IInputSystem</c>. Client side only.</summary>
    internal static IFwdInputSystem InputSystem(EngineSide side) => side.Attachment.Bound.InputSystem.For(side);

    /// <summary><c>IEngineSound</c>, the client's. Client side only.</summary>
    internal static IFwdEngineSoundClient SoundClient(EngineSide side) => side.Attachment.Bound.SoundClient.For(side);
}
