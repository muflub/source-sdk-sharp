namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IStaticPropPhysics"/> over the server engine's
/// IStaticPropMgrServer, called through libss_fwd_server's forwarders. The
/// server's alone (plan_engine_forwarders.md, one library per side): the
/// client's engine has no such interface and the client's library no table.
/// </summary>
public sealed unsafe class StaticPropMgrServer : IStaticPropPhysics
{
    /// <summary><c>INTERFACEVERSION_STATICPROPMGR_SERVER</c>.</summary>
    public const string InterfaceVersion = "StaticPropMgrServer002";

    private readonly void* _self;
    private readonly StaticPropMgrServerFwd* _fwd;

    private StaticPropMgrServer(void* self, StaticPropMgrServerFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>
    /// The static prop manager from <paramref name="engine"/>'s own side's
    /// factory, or null with the reason.
    /// </summary>
    public static IStaticPropPhysics? Acquire(ModuleEngine engine, out string why)
        => Forwarders.TryAcquire<StaticPropMgrServerFwd>(
               (engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces,
               SourceSharpFactoryType.AppSystem, InterfaceVersion,
               out void* self, out StaticPropMgrServerFwd* table, out why)
            ? new StaticPropMgrServer(self, table)
            : null;

    /// <inheritdoc/>
    public void CreateVPhysicsRepresentations(nint environment, nint keyHandler)
        => _fwd->CreateVPhysicsRepresentations(_self, (void*)environment, (void*)keyHandler, null);
}
