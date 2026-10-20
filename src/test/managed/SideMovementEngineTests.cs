extern alias client;
using System.Reflection;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D8, the movement and client half of lane d8c: the
/// movement runner, the command loop's stages, the player shadow, the client's
/// replay and frame blocks and the engine client's proxies reached the engine
/// through the static <c>Engine.Current</c>, which followed whichever side was
/// active. Each is now built over one side. These facts attach two fake shim
/// roots to two sides, each answering differently, and show every call lands
/// on the side it was built over.
///
/// <para>
/// The client's half (its entity area's replay and frame blocks, and the
/// engine client) is <c>ClientSideEngineTests</c> in the client suite: each
/// side compiles its own copy of the shared interop (plan_shim_areas_split
/// phase 3), and the client's areas are built from the client's.
/// </para>
/// </summary>
public unsafe class SideMovementEngineTests : IDisposable
{
    // This class's own command queues and shadow run (statics to zero):
    // xUnit builds one instance per fact, so no fact sees another's.
    private readonly PlayerCommandQueue _queue = new(new RecordingLog());

    private readonly FakeShimRoot _first = new();
    private readonly FakeShimRoot _second = new();
    private readonly nint* _self1 = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
    private readonly nint* _self2 = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));

    private static readonly EntityHandle Player = new(0x0001_0001u);

    private const string MovementServer = "SourceSharpMovementServer001";

    // Which fake each side's engine reached. The callbacks are unmanaged, so
    // they report through these; every fact resets them first.
    private static int s_firstCalls;
    private static int s_secondCalls;

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
        NativeMemory.Free(_self1);
        NativeMemory.Free(_self2);
    }

    private static EngineSide Attached(FakeShimRoot root, Side side)
    {
        var api = TestSides.Create(side);
        api.Initialize();

        string why;
        bool attached = api.AttachOwnShims(root.Bind(), out why);

        Assert.True(attached, why);
        return api;
    }

    private (EngineSide First, EngineSide Second) TwoSides(Side side, string version, string slot,
                                                          nint first, nint second)
    {
        _first.Live(version, slot, first);
        _second.Live(version, slot, second);
        s_firstCalls = s_secondCalls = 0;
        return (Attached(_first, side), Attached(_second, side));
    }

    //-------------------------------------------------------------------------
    // The server's movement runner and the command loop beside it.
    //-------------------------------------------------------------------------

    // Each runner's command stages asking their own side is
    // ServerCommandSideTests' since the stages are managed
    // (plan_server_native_v2 T4g); PlayerRunMove's slot is retired.

    // The world's point contents go through each side's own IEngineTrace
    // (2026-10-19), where the MovePointContents slot answered.
    private (EngineSide First, EngineSide Second) TwoTraces()
    {
        _first.Interface(EngineTrace.ServerInterfaceVersion, _self1).Forwarders(out EngineTraceFwd* a);
        a->GetPointContents = &ContentsSeven;
        _second.Interface(EngineTrace.ServerInterfaceVersion, _self2).Forwarders(out EngineTraceFwd* b);
        b->GetPointContents = &ContentsNine;
        s_firstCalls = s_secondCalls = 0;
        return (Attached(_first, Side.Server), Attached(_second, Side.Server));
    }

    [Fact]
    public void EachRunnersWorldAsksItsOwnSide()
    {
        var (first, second) = TwoTraces();

        var mine = MovementRunner.For(new ModuleEngine(first));
        _ = MovementRunner.For(new ModuleEngine(second));

        Assert.Equal(7, mine.World.PointContents(Vector.Zero, 0));
        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));
    }

    [Fact]
    public void ASlotAnswersAgainForAPointWithinAUnit()
    {
        var world = MovementRunner.For(new ModuleEngine(TwoTraces().First)).World;
        world.ResetPointContentsCache(optimizations: true);

        world.PointContents(new Vector(0, 0, 0), 1);
        world.PointContents(new Vector(0, 0, 1), 1);
        Assert.Equal(1, s_firstCalls);

        world.PointContents(new Vector(0, 0, 1.01f), 1);
        Assert.Equal(2, s_firstCalls);
    }

    [Fact]
    public void EachSlotIsItsOwnAndSlotMinusOneIsNeverCached()
    {
        var world = MovementRunner.For(new ModuleEngine(TwoTraces().First)).World;
        world.ResetPointContentsCache(optimizations: true);

        world.PointContents(Vector.Zero, 0);
        world.PointContents(Vector.Zero, 2);
        world.PointContents(Vector.Zero, -1);
        world.PointContents(Vector.Zero, -1);
        Assert.Equal(4, s_firstCalls);
    }

    [Fact]
    public void AResetForgetsEverySlot()
    {
        var world = MovementRunner.For(new ModuleEngine(TwoTraces().First)).World;
        world.ResetPointContentsCache(optimizations: true);
        world.PointContents(Vector.Zero, 0);

        world.ResetPointContentsCache(optimizations: true);
        world.PointContents(Vector.Zero, 0);
        Assert.Equal(2, s_firstCalls);
    }

    [Fact]
    public void WithoutTheOptimizationsNothingIsCached()
    {
        var world = MovementRunner.For(new ModuleEngine(TwoTraces().First)).World;
        world.ResetPointContentsCache(optimizations: false);

        world.PointContents(Vector.Zero, 0);
        world.PointContents(Vector.Zero, 0);
        Assert.Equal(2, s_firstCalls);
    }

    // The shadow's bodies over one side (plan_physics_bones_native_zero 2.5).
    // No vphysics table at all: the facts slot answers 0, so nothing past it runs.
    private static PlayerShadowBodies BodiesOver(EngineSide side)
        => PlayerShadowBodies.ForEngine(new ModuleEngine(side), new VPhysicsShadowCalls(null, new DefaultForwarders.NoSurfaceProps(), TestVPhysics.None()), () => true, _ => { });

    [Fact]
    public void ThePlayerShadowProbesTheSideItsBodiesWereBuiltOver()
    {
        var (first, second) = TwoSides(Side.Server, MovementServer, nameof(ShimMovementServerFwd.PlayerShadowFacts),
            (nint)(delegate* unmanaged<void*, uint, void*, int>)&ProbeFirst,
            (nint)(delegate* unmanaged<void*, uint, void*, int>)&ProbeSecond);

        // The facts slot answers 0 (no player), so the step stops there: what
        // is measured is which side was asked. The bodies are the composition's,
        // built over its own side, and the loop steps through them.
        _queue.Shadow.Bodies = BodiesOver(first);
        Assert.False(_queue.Shadow.Step(first, Player, 0, enabled: true));
        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));

        _queue.Shadow.Bodies = BodiesOver(second);
        Assert.False(_queue.Shadow.Step(second, Player, 0, enabled: true));
        Assert.Equal((1, 1), (s_firstCalls, s_secondCalls));

        // And a loop with no engine steps nothing, rather than asking any side.
        Assert.False(_queue.Shadow.Step(null, Player, 0, enabled: true));
        Assert.Equal((1, 1), (s_firstCalls, s_secondCalls));

        // Nor does one with no bodies.
        _queue.Shadow.Bodies = null;
        Assert.False(_queue.Shadow.Step(first, Player, 0, enabled: true));
        Assert.Equal((1, 1), (s_firstCalls, s_secondCalls));
    }

    //-------------------------------------------------------------------------
    // A networked state's blob, bound on its registry's side.
    //-------------------------------------------------------------------------

    [Fact]
    public void ANetStateBindsAndAnswersOnTheSideItWasBoundOn()
    {
        _first.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerFwd.EntityBindBlob),
                    (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobFirst);
        _second.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerFwd.EntityBindBlob),
                     (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobSecond);
        s_firstCalls = s_secondCalls = 0;

        EngineSide first = Attached(_first, Side.Server);
        _ = Attached(_second, Side.Server);

        var state = new SourceSharp.Shared.BarrelNetState();
        state.Bind(first, first.Areas.Require<INetworkServerArea>(), Player);

        // Bound to the blob the first side answered (IsBound itself asks the
        // side's EntityIsValid, a SuppressGCTransition slot a managed fake
        // cannot serve, so the binding is read off the handle and storage).
        Assert.Equal(Player, state.Handle);
        Assert.True(state.IsLive, "it has the first side's blob");
        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));
    }

    private sealed class HoldsAState
    {
        [NetState] public SourceSharp.Shared.BarrelNetState? State = null;
    }

    [Fact]
    public void AttachingWithASideBindsEveryStateThere()
    {
        _first.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerFwd.EntityBindBlob),
                    (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobFirst);
        _second.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerFwd.EntityBindBlob),
                     (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobSecond);
        s_firstCalls = s_secondCalls = 0;

        EngineSide first = Attached(_first, Side.Server);
        _ = Attached(_second, Side.Server);

        var holder = new HoldsAState();
        NetState[] states = NetStateServerBinding.AttachBound(holder, Player, first, first.Areas.Require<INetworkServerArea>(),
                                                              new NetStateFields(new RecordingLog()));

        Assert.Single(states);
        Assert.Equal(Player, holder.State!.Handle);
        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));

        // And with no side every state is local, asking nobody.
        var local = new HoldsAState();
        NetStateBinding.AttachLocal(local, Player, new NetStateFields(new RecordingLog()));
        Assert.Equal(EntityHandle.None, local.State!.Handle);
        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));
    }

    [Fact]
    public void ANetStateOnASideWithNoEngineTakesLocalStorage()
    {
        var state = new SourceSharp.Shared.BarrelNetState();
        var side = TestSides.Create(Side.Server);
        state.Bind(side, side.Areas.Require<INetworkServerArea>(), Player);

        Assert.False(state.IsBound, "no engine: a local state, not a bound one");
        Assert.True(state.IsLive, "but it has storage");
    }

    //-------------------------------------------------------------------------
    // The fakes.
    //-------------------------------------------------------------------------

    // A blob and a slot a fake bind can answer with; test-lifetime storage.
    private static readonly byte* s_blob = (byte*)NativeMemory.AllocZeroed(4096);
    private static readonly EntitySlot* s_slot = (EntitySlot*)NativeMemory.AllocZeroed(1, (nuint)sizeof(EntitySlot));

    [UnmanagedCallersOnly]
    private static void* BindBlobFirst(void* self, uint handle, void* size, void* slot, void* serial)
    {
        s_firstCalls++;
        *(int*)size = 4096;
        *(EntitySlot**)slot = s_slot;
        s_slot->Serial = 1;
        *(uint*)serial = 1;
        return s_blob;
    }

    [UnmanagedCallersOnly]
    private static void* BindBlobSecond(void* self, uint handle, void* size, void* slot, void* serial)
    {
        s_secondCalls++;
        return null;
    }

    [UnmanagedCallersOnly]
    private static int ContentsSeven(void* self, void* point, void* entity) { s_firstCalls++; return 7; }

    [UnmanagedCallersOnly]
    private static int ContentsNine(void* self, void* point, void* entity) { s_secondCalls++; return 9; }

    [UnmanagedCallersOnly]
    private static int ProbeFirst(void* self, uint player, void* state) { s_firstCalls++; return 0; }

    [UnmanagedCallersOnly]
    private static int ProbeSecond(void* self, uint player, void* state) { s_secondCalls++; return 0; }
}
