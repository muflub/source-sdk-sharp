namespace SourceSharp.Abi;

//=============================================================================
// THE BOUND ENGINE INTERFACES, ACQUIRED ONCE PER SIDE, FOR THE AREA FACADES.
//
// plan_native_api.md Part 5b. Converting one flat entry means an area facade
// calling a bound impl instead of the god-class -- and an area is constructed
// by the container with no arguments, on BOTH sides, before anything has asked
// it for anything. So the acquire cannot be in the constructor and cannot be
// repeated per call, which leaves exactly this: acquired at the first use, per
// side, and held.
//
// PER SIDE, AND THAT IS NOT BOOKKEEPING. Both game DLLs share this assembly in
// the default ALC. `IVEngineServer` exists only in server.so and
// `IVEngineClient` only in client.so; `IVModelInfo` exists in both and they are
// TWO OBJECTS -- the engine registers VModelInfoServer004 and
// VModelInfoClient006 for two instances of one class. One field here would be
// whichever side bound last, and the wrong one is not a crash: it is a
// plausible answer about the other side's world. That is the same bug the two
// EngineApi tables in Engine.cs exist to prevent, one level up, and it cost
// this port a debugging session before.
//
// IT THROWS RATHER THAN ANSWERING ZERO. An area that cannot reach the interface
// it was converted onto is a bug, not a degraded area -- the native precedent
// is SourceSharp_AcquireEngineInterfaces returning false the moment one
// interface is missing, because a server missing `modelinfo` does not
// half-work, it crashes on the first model several seconds later somewhere
// else. The exception names the interface and the reason at the site that
// asked.
//=============================================================================

/// <summary>
/// One engine interface, acquired at first use and held per side.
/// </summary>
/// <typeparam name="T">The bound interface, as game code would see it.</typeparam>
internal sealed class BoundInterface<T> where T : class
{
    private readonly string _version;
    private readonly Acquirer _acquire;

    private T? _held;

    internal delegate T? Acquirer(NativeInterfaceFactory factory, Side side, out string why);

    internal BoundInterface(string version, Acquirer acquire)
    {
        _version = version;
        _acquire = acquire;
    }

    /// <summary>The same acquirer, holding nothing: what a fresh bound set keeps of this one.</summary>
    internal BoundInterface<T> Unheld() => new(_version, _acquire);

    /// <summary>
    /// The interface, acquired through <paramref name="side"/>'s own factory
    /// on first use and held by that side's engine (plan_abi_hotreload D8,
    /// statics to zero).
    ///
    /// <para>
    /// NOT LOCKED. Two threads can acquire at once; both get the same
    /// registered singleton from the same switch and construct two wrappers
    /// over one pointer, and a reference-sized write of an equivalent value is
    /// indistinguishable from one. The same argument the global-vars bind makes.
    /// </para>
    /// </summary>
    internal T For(EngineSide side)
    {
        ArgumentNullException.ThrowIfNull(side);

        if (_held is not null)
            return _held;

        T bound = _acquire(side.Interfaces, side.Side, out string why)
            ?? throw InterfaceAcquisitionException.FromReport(
                   SourceSharpFactoryType.AppSystem, _version, why);

        _held = bound;
        return bound;
    }

    /// <summary>
    /// Answers <paramref name="bound"/> from now on, without asking the
    /// factory: a test side's default forwarder (TestSides), which a root's
    /// attach replaces with the bound set itself (EngineSide.Attached).
    /// </summary>
    internal void Hold(T bound) => _held = bound ?? throw new ArgumentNullException(nameof(bound));
}

/// <summary>
/// One engine side's bound interfaces (statics to zero): what were four
/// process-wide <c>PerSide</c> slots, now held by the <see cref="EngineSide"/>
/// they were acquired from, so two engines of one side never share one and a
/// reloaded side's engine acquires its own on first use.
/// </summary>
internal sealed partial class EngineBoundSet
{
    internal BoundInterface<IUniformRandom> Random { get; } =
        new(UniformRandomStream.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => UniformRandomStream.Acquire(factory, out why));

    // THE TRACE IS ASKED FOR BY THE SIDE'S OWN NAME, as the model info is.
    internal BoundInterface<IFwdEngineTrace> Trace { get; } =
        new(EngineTrace.ServerInterfaceVersion, EngineTrace.Acquire);

    internal BoundInterface<IFwdSoundEmitter> SoundEmitter { get; } =
        new(SoundEmitterBase.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => SoundEmitterBase.Acquire(factory, out why));

    internal BoundInterface<IFwdPhysicsSurfaceProps> SurfaceProps { get; } =
        new(PhysicsSurfaceProps.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => PhysicsSurfaceProps.Acquire(factory, out why));

    internal BoundInterface<IFwdBaseFileSystem> Files { get; } =
        new(EngineBaseFileSystem.InterfaceVersion,
            (NativeInterfaceFactory factory, Side _, out string why) => EngineBaseFileSystem.Acquire(factory, out why));

    // THE MODEL INFO IS ASKED FOR BY THE SIDE'S OWN NAME. Both names are served
    // on both sides for the table LENGTH, but the object behind each is that
    // side's own -- so a server that asked for the client spelling would get
    // the client's model info on a listen server and nothing at all on a
    // dedicated one.
    internal BoundInterface<IFwdModelInfo> ModelInfo { get; } =
        new(EngineModelInfo.ServerInterfaceVersion, EngineBound.AcquireModelInfo);
}

/// <summary>The bound interfaces, read off the side asking: stateless forwarders.</summary>
internal static partial class EngineBound
{
    internal static IFwdModelInfo? AcquireModelInfo(NativeInterfaceFactory factory, Side side, out string why)
        => EngineModelInfo.Acquire(factory, out why, ModelInfoVersion(side));

    internal static string ModelInfoVersion(Side side)
        => side == Side.Client
               ? EngineModelInfo.ClientInterfaceVersion
               : EngineModelInfo.ServerInterfaceVersion;

    /// <summary><c>VEngineRandom001</c>, this side's.</summary>
    internal static IUniformRandom Random(EngineSide side) => side.Bound.Random.For(side);

    /// <summary><c>EngineTraceServer003</c> or <c>EngineTraceClient003</c>, this side's.</summary>
    internal static IFwdEngineTrace Trace(EngineSide side) => side.Bound.Trace.For(side);

    /// <summary><c>VSoundEmitter002</c>, this side's.</summary>
    internal static IFwdSoundEmitter SoundEmitter(EngineSide side) => side.Bound.SoundEmitter.For(side);

    /// <summary><c>VPhysicsSurfaceProps001</c>, this side's.</summary>
    internal static IFwdPhysicsSurfaceProps SurfaceProps(EngineSide side) => side.Bound.SurfaceProps.For(side);

    /// <summary><c>VBaseFileSystem011</c>, this side's.</summary>
    internal static IFwdBaseFileSystem Files(EngineSide side) => side.Bound.Files.For(side);

    /// <summary>The side's own model info.</summary>
    internal static IFwdModelInfo ModelInfo(EngineSide side) => side.Bound.ModelInfo.For(side);
}
