namespace SourceSharp.Abi;

/// <summary>
/// What the server's entity registry asks of its side's engine past the
/// engine areas: freeing a frame's shells, the shell-less reach, and binding
/// an entity's net states to its blob.
///
/// <para>
/// P1 run 3, the integrator's ruling (a): the registry held the side's
/// <see cref="EngineSide"/> itself, which no game assembly may reach
/// (EngineAreaTests.NothingOutsideTheAbiCanReachTheEngine). The registry is
/// server game code, so it holds this instead, and this is the server's
/// interop code: it lives beside the engine surface and forwards to it.
/// </para>
/// </summary>
public sealed class ServerEntityEngine
{
    internal ServerEntityEngine(EngineSide api, EntityLifecycleArea lifecycle, WorldServerArea world, NetworkServerArea network)
    {
        Api = api ?? throw new ArgumentNullException(nameof(api));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _network = network ?? throw new ArgumentNullException(nameof(network));
    }

    // The areas the facade's own calls reach (plan_shim_areas_split phase 11):
    // the shells' teardown, the invisible-range counter and the blob bind.
    private readonly EntityLifecycleArea _lifecycle;
    private readonly WorldServerArea _world;
    private readonly NetworkServerArea _network;

    /// <summary>Over a composition's engine, with the areas its calls reach taken from it here once.</summary>
    public static ServerEntityEngine For(ModuleEngine engine)
        => For((engine ?? throw new ArgumentNullException(nameof(engine))).Api);

    /// <inheritdoc cref="For(ModuleEngine)"/>
    internal static ServerEntityEngine For(EngineSide side)
        => new(side, side.Areas.Require<EntityLifecycleArea>(), side.Areas.Require<WorldServerArea>(),
               side.Areas.Require<NetworkServerArea>());

    /// <summary>The side this forwards to.</summary>
    internal EngineSide Api { get; }

    /// <summary>The side's connection id stamps, its <see cref="SideState"/>'s (statics to zero).</summary>
    public EventIdStamps Stamps => Api.State.Stamps;

    /// <summary>The side's reload store, its <see cref="SideState"/>'s (statics to zero).</summary>
    public ReloadStore Reload => Api.State.Reload;

    /// <summary>The side's log, over its own console (statics to zero).</summary>
    public ILog Log => Api.Log;

    /// <summary>The lines the side's console prints, which a fact taps to read what its subject logged.</summary>
    public ConsoleLines Lines => Api.Lines;

    /// <summary>Frees a frame's batch of shells: <see cref="EngineSide.DestroyShells"/>.</summary>
    public void DestroyShells(ReadOnlySpan<EntityHandle> batch) => Api.DestroyShells(_lifecycle, batch);

    /// <summary>How far native reaches for a shell-less entity: <see cref="EngineSide.InvisibleRangeReach"/>.</summary>
    public int InvisibleRangeReach() => Api.InvisibleRangeReach(_world);

    /// <summary>Binds an entity's net states to its blob on this side: <see cref="NetStateServerBinding.AttachBound"/>.</summary>
    public NetState[] AttachBound(object instance, EntityHandle handle, NetStateFields fields)
        => NetStateServerBinding.AttachBound(instance, handle, Api, _network, fields);

    /// <summary>
    /// An unattached server side and its engine container: what a registry
    /// built with no composition binds and stamps with (the tests' registry).
    /// </summary>
    public static (ServiceContainer Engine, ServerEntityEngine Side) Unattached()
    {
        var side = new EngineSide(Abi.Side.Server);
        EngineServerBinding.Bind(side);
        var engine = new ServiceContainer();

        // The render binding takes the side's mirror in its constructor.
        engine.AddInstance(new StandaloneMirror());
        EngineServices.AddTo(engine, side);

        // And its clock, as the game's composition registers one: what a
        // registry stamps outputs, thinks and saves with.
        engine.AddInstance<IGameClock>(new EngineClock(side));
        return (engine, For(side));
    }
}
