namespace SourceSharp.Abi;

/// <summary>
/// What one side's load attaches to (docs/plans/plan_engineside_removal.md,
/// Phase 1): the shim root, the areas acquired through it, the engine
/// interfaces bound through it, the side's state across loads and where its
/// managed assemblies came from. <c>SideLoad</c> builds one per load, attaches
/// the shims to it, and composition reads it; nothing else holds it.
///
/// <para>
/// <b>Why it is its own object.</b> It was half of <see cref="EngineSide"/>,
/// which also carried the side's services and about eighty entity helpers, so
/// anything handed the side for its areas or its factory could reach all of
/// that too. The attach state is what composition needs and nothing more:
/// every area is over no table until the attach (a call before it throws,
/// naming the area), and the attach replaces them all at once.
/// </para>
///
/// <para>
/// Each side compiles its own (the shared interop is in both side
/// assemblies); what differs by side, the areas a side registers, is in each
/// side's half (<c>SideAttachment.Server.cs</c>, <c>SideAttachment.Client.cs</c>).
/// </para>
/// </summary>
internal sealed unsafe partial class SideAttachment
{
    private ServiceContainer _areas;
    private Action? _attached;
    private NativeInterfaceFactory? _interfaces;

    // A constructor only saves what it is handed: the factories below build
    // the rest.
    private SideAttachment(Side side, SideState state, ServiceContainer areas)
    {
        Side = side;
        State = state;
        _areas = areas;
    }

    /// <summary>
    /// The side over <paramref name="state"/>, which must be the same side's,
    /// with every area detached until the attach: what a load builds.
    /// </summary>
    internal static SideAttachment Detached(Side side, SideState state)
    {
        CheckState(side, state);
        return new SideAttachment(side, state, DetachedAreas());
    }

    /// <summary>
    /// A side over the areas its maker hands it: a test's, over the generated
    /// default areas (<c>TestSides.Create</c>, src/test/support). Nothing checks
    /// whether an engine is behind them, so a side with no engine is given
    /// areas that answer defaults rather than none.
    /// </summary>
    internal static SideAttachment Over(Side side, SideState state, ServiceContainer areas)
    {
        CheckState(side, state);
        ArgumentNullException.ThrowIfNull(areas);
        return new SideAttachment(side, state, areas);
    }

    private static void CheckState(Side side, SideState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Side != side)
            throw new ArgumentException($"a {side} attachment was handed the {state.Side} side's state", nameof(state));
    }

    /// <summary>Which side this is (until each side's composition answers its own; plan PR 3).</summary>
    internal Side Side { get; }

    /// <summary>
    /// What this side keeps across its loads (statics to zero): the
    /// managed-built registry, the crossing counters and the rest, handed in by
    /// the side's load from the handle the Bootstrap keeps. A fresh one for a
    /// side composed without a load, so no two sides share one by default.
    /// </summary>
    internal SideState State { get; }

    /// <summary>The root the attached areas came from, or null before an attach.</summary>
    internal ShimRoot? Root { get; private set; }

    /// <summary>
    /// The side's areas by type: detached before the attach, the root's after.
    /// For the composition that builds a consumer and hands it the areas it
    /// calls, and for nothing else (ShimAreaInjectionCensusTests).
    /// </summary>
    internal IServiceLocator Areas => _areas;

    /// <summary>
    /// The engine interfaces bound through this side's factory, acquired on
    /// first use and held for this load (statics to zero: they were four
    /// process-wide slots). The attach makes a fresh set: what is bound from
    /// then on is the root's, and a test side's held defaults (TestSides) must
    /// not answer for it. Acquirers installed before the attach (the server's
    /// IVEngineServer, EngineServerBinding through SideLoad) carry over.
    /// </summary>
    internal EngineBoundSet Bound { get; private set; } = new();

    /// <summary>
    /// Where this side's managed assemblies were loaded from, as the bootstrap
    /// knows it. Told rather than discovered: the load context reads each
    /// assembly into memory so the files stay unlocked, which leaves
    /// Assembly.Location empty for every one of them, and a relative fallback
    /// resolves against the engine's working directory, which is the Steam
    /// install and not this mod. Empty outside the game.
    /// </summary>
    internal string ManagedDirectory { get; set; } = string.Empty;

    /// <summary>Whether the side's load has finished initialising it (SideLoad).</summary>
    internal bool IsInitialized { get; private set; }

    /// <summary>The load is done with this side: called once by SideLoad after the attach.</summary>
    internal void Initialize() => IsInitialized = true;

    /// <summary>
    /// This side's own interface factory (plan_abi_hotreload D8), over
    /// <see cref="AcquireInterface"/>: what everything bound to this side
    /// acquires through. There is no process-wide factory.
    /// </summary>
    internal NativeInterfaceFactory Interfaces => _interfaces ??= new NativeInterfaceFactory(AcquireInterface);

    /// <summary>
    /// Has <paramref name="react"/> run at each attach, after the areas and the
    /// root are replaced: how a service made over the detached areas (the
    /// side's own log and caches) takes up the attached ones.
    /// </summary>
    internal void WhenAttached(Action react)
    {
        ArgumentNullException.ThrowIfNull(react);
        _attached += react;
    }

    /// <summary>The attach, once every area was taken: the side answers from these from now on.</summary>
    private void Attached(ShimRoot root, ServiceContainer areas)
    {
        Root = root;
        Bound = Bound.Unheld();
        _areas = areas;
        _attached?.Invoke();
    }

    /// <summary>
    /// The interface pointer for a factory kind and a version string, and, on
    /// failure, native's reason.
    ///
    /// <para>
    /// Null for a string this side does not serve. That is not an error:
    /// <c>IVEngineClient</c> exists only on the client and <c>IVEngineServer</c>
    /// only on the server.
    /// </para>
    ///
    /// <para>
    /// <b>The signature is the ABI slot's, not a convenience over it</b>:
    /// <see cref="NativeInterfaceFactory.AcquireFn"/> is this method's shape, so
    /// the production factory is this method and a test's factory is a
    /// delegate the test wrote. When the root is missing (before the attach,
    /// or in a test process with no native half) the reason is written here
    /// rather than left to a null return with an empty buffer, because
    /// "nothing was reported" and "the boundary is not up" are different
    /// failures.
    /// </para>
    /// </summary>
    public void* AcquireInterface(SourceSharpFactoryType type, byte* interfaceVersion, byte* error, int errorBytes)
    {
        if (Root is { } root && interfaceVersion != null)
            return root.AcquireInterface((int)type, interfaceVersion, error, errorBytes);

        SayNoBoundary(error, errorBytes);
        return null;
    }

    // The one reason managed itself can produce, in the same buffer and under
    // the same rules native writes it: ASCII, truncating, terminated.
    private static void SayNoBoundary(byte* error, int errorBytes)
    {
        if (error == null || errorBytes <= 0)
            return;

        ReadOnlySpan<byte> text = "the native half of this side is not initialised"u8;
        int n = Math.Min(text.Length, errorBytes - 1);

        text[..n].CopyTo(new Span<byte>(error, n));
        error[n] = 0;
    }
}
