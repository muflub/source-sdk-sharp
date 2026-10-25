namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IStaticPropPhysics"/> over the client engine's
/// IStaticPropMgrClient, called through libss_fwd_client's forwarders: the
/// static props' objects the client's physics system makes in each level's
/// environment (stock's client PhysicsLevelInit). The client's twin of
/// Server.Interop's StaticPropMgrServer.
/// </summary>
public sealed unsafe class StaticPropMgrClient : IStaticPropPhysics
{
    /// <summary><c>INTERFACEVERSION_STATICPROPMGR_CLIENT</c>.</summary>
    public const string InterfaceVersion = "StaticPropMgrClient004";

    private readonly void* _self;
    private readonly StaticPropMgrClientFwd* _fwd;

    private StaticPropMgrClient(void* self, StaticPropMgrClientFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The static prop manager from <paramref name="engine"/>'s own side's factory, or null with the reason.</summary>
    public static IStaticPropPhysics? Acquire(ModuleEngine engine, out string why)
        => Forwarders.TryAcquire<StaticPropMgrClientFwd>(
               (engine ?? throw new ArgumentNullException(nameof(engine))).Interfaces,
               SourceSharpFactoryType.AppSystem, InterfaceVersion,
               out void* self, out StaticPropMgrClientFwd* table, out why)
            ? new StaticPropMgrClient(self, table)
            : null;

    /// <inheritdoc/>
    public void CreateVPhysicsRepresentations(nint environment, nint keyHandler)
        => _fwd->CreateVPhysicsRepresentations(_self, (void*)environment, (void*)keyHandler, null);
}
