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
/// </summary>
public unsafe class SideMovementEngineTests : IDisposable
{
    // This class's own command queues and shadow run (statics to zero):
    // xUnit builds one instance per fact, so no fact sees another's.
    private readonly PlayerCommandQueue _queue = new(new RecordingLog());

    private readonly FakeShimRoot _first = new();
    private readonly FakeShimRoot _second = new();

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
    }

    private static EngineSide Attached(FakeShimRoot root, Side side)
    {
        var api = new EngineSide(side);
        api.Initialize();

        string why;
        bool attached = side == Side.Server
            ? api.AttachShims(root.Bind(), out why)
            : api.AttachClientShims(root.Bind(), out why);

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

    [Fact]
    public void EachRunnersCommandStagesRunOnItsOwnSide()
    {
        var (first, second) = TwoSides(Side.Server, MovementServer, nameof(ShimMovementServerVtbl.PlayerRunMove),
            (nint)(delegate* unmanaged<void*, uint, int, int, int>)&RunMoveSeven,
            (nint)(delegate* unmanaged<void*, uint, int, int, int>)&RunMoveNine);

        var mine = new PlayerRunMoveStages(new MovementRunner(new ModuleEngine(first)));
        var theirs = new PlayerRunMoveStages(new MovementRunner(new ModuleEngine(second)));

        Assert.Equal(7, mine.RunMove(Player, 0, 1));
        Assert.Equal(9, theirs.RunMove(Player, 0, 1));
        Assert.Equal((1, 1), (s_firstCalls, s_secondCalls));
    }

    [Fact]
    public void EachRunnersWorldAsksItsOwnSide()
    {
        var (first, second) = TwoSides(Side.Server, MovementServer, nameof(ShimMovementServerVtbl.MovePointContents),
            (nint)(delegate* unmanaged<void*, void*, int, int>)&ContentsSeven,
            (nint)(delegate* unmanaged<void*, void*, int, int>)&ContentsNine);

        var mine = new MovementRunner(new ModuleEngine(first));
        _ = new MovementRunner(new ModuleEngine(second));

        Assert.Equal(7, mine.World.PointContents(Vector.Zero, 0));
        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));
    }

    // The shadow's bodies over one side (plan_physics_bones_native_zero 2.5).
    // No vphysics table at all: the facts slot answers 0, so nothing past it runs.
    private static PlayerShadowBodies BodiesOver(EngineSide side)
        => PlayerShadowBodies.ForEngine(new ModuleEngine(side), new VPhysicsShadowCalls(null, null, TestVPhysics.None()), () => true, _ => { });

    [Fact]
    public void ThePlayerShadowProbesTheSideItsBodiesWereBuiltOver()
    {
        var (first, second) = TwoSides(Side.Server, MovementServer, nameof(ShimMovementServerVtbl.PlayerShadowFacts),
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

    [Fact]
    public void TheCommandLoopRunsItsStagesAndShadowOnItsRunnersSide()
    {
        // Both slots live on both roots; the second side answers differently
        // and must never be asked.
        _first.Live(MovementServer, nameof(ShimMovementServerVtbl.PlayerRunMove),
                    (nint)(delegate* unmanaged<void*, uint, int, int, int>)&RunMoveOne);
        _first.Live(MovementServer, nameof(ShimMovementServerVtbl.PlayerShadowFacts),
                    (nint)(delegate* unmanaged<void*, uint, void*, int>)&ProbeFirst);
        _second.Live(MovementServer, nameof(ShimMovementServerVtbl.PlayerRunMove),
                     (nint)(delegate* unmanaged<void*, uint, int, int, int>)&RunMoveNine);
        _second.Live(MovementServer, nameof(ShimMovementServerVtbl.PlayerShadowFacts),
                     (nint)(delegate* unmanaged<void*, uint, void*, int>)&ProbeSecond);
        s_firstCalls = s_secondCalls = 0;

        EngineSide firstSide = Attached(_first, Side.Server);
        var mine = new PlayerRunMoveStages(new MovementRunner(new ModuleEngine(firstSide)));
        _ = new MovementRunner(new ModuleEngine(Attached(_second, Side.Server)));
        _queue.Shadow.Bodies = BodiesOver(firstSide);

        var player = new EntityHandle(0x0D8C_0001u);
        _queue.Forget(player);
        try
        {
            UserCmd[] cmds = [new() { StructSize = (uint)sizeof(int), CommandNumber = 1 }];
            int[] order = new int[256];
            fixed (UserCmd* pCmds = cmds)
            fixed (int* pOrder = order)
                Assert.True(_queue.Queue(player, pCmds, 1, 1, 0, pOrder, order.Length) == 1,
                            "one command was queued");

            Assert.Equal(1, _queue.Run(player, 1, mine, null, null, shadowEnabled: true));
        }
        finally
        {
            _queue.Forget(player);
        }

        // Three stages and one shadow probe, all on the first side.
        Assert.Equal((4, 0), (s_firstCalls, s_secondCalls));
    }

    //-------------------------------------------------------------------------
    // The client's replay and frame blocks, held by its entity area.
    //-------------------------------------------------------------------------

    [Fact]
    public void EachEntityAreasReplayFetchesFromItsOwnSide()
    {
        var (first, second) = TwoSides(Side.Client, "SourceSharpPredictionClient001",
            nameof(ShimPredictionClientVtbl.ClientGetUserCmd),
            (nint)(delegate* unmanaged<void*, int, void*, int>)&UserCmdFound,
            (nint)(delegate* unmanaged<void*, int, void*, int>)&UserCmdMissing);

        IClientReplayEngine mine = ClientEntities(first).ClientReplay;
        IClientReplayEngine theirs = ClientEntities(second).ClientReplay;

        Assert.True(mine.TryGetUserCmd(42, out UserCmd cmd), "the first side has the command");
        Assert.Equal(42, cmd.CommandNumber);
        Assert.False(theirs.TryGetUserCmd(42, out _), "and the second side does not");
        Assert.Equal((1, 1), (s_firstCalls, s_secondCalls));
    }

    [Fact]
    public void EachEntityAreasInterpolationListComesFromItsOwnSide()
    {
        var (first, second) = TwoSides(Side.Client, "SourceSharpStandaloneClient001",
            nameof(ShimStandaloneClientVtbl.ClientInterpBlocks),
            (nint)(delegate* unmanaged<void*, void*, void*, void*>)&InterpOne,
            (nint)(delegate* unmanaged<void*, void*, void*, void*>)&InterpNone);

        Assert.Equal(1, ClientEntities(first).InterpolationBlocks.Begin(out float amount));
        Assert.Equal(0.25f, amount);
        Assert.Equal(0, ClientEntities(second).InterpolationBlocks.Begin(out _));
        Assert.Equal((1, 1), (s_firstCalls, s_secondCalls));
    }

    [Fact]
    public void EachEntityAreasRenderPolicyListComesFromItsOwnSide()
    {
        var (first, second) = TwoSides(Side.Client, "SourceSharpStandaloneClient001",
            nameof(ShimStandaloneClientVtbl.ClientPolicyBlocks),
            (nint)(delegate* unmanaged<void*, void*, void*>)&PolicyOne,
            (nint)(delegate* unmanaged<void*, void*, void*>)&PolicyNone);

        Assert.Equal(1, ClientEntities(first).RenderPolicyBlocks.Begin());
        Assert.Equal(0, ClientEntities(second).RenderPolicyBlocks.Begin());
        Assert.Equal((1, 1), (s_firstCalls, s_secondCalls));
    }

    /// <summary>
    /// A side's entity area as the client composition builds it: the client's
    /// three fetches registered first (ClientEntityFetches), then the areas.
    /// </summary>
    private static IEngineEntities ClientEntities(EngineSide side)
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        SourceSharp.Client.Interop.ClientEntityFetches.AddTo(services, new ModuleEngine(side));
        EngineServices.AddTo(services, side);
        return services.Require<IEngineEntities>();
    }

    [Fact]
    public void TheClientsAreaAndContainerHoldTheSameThreeFetches()
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        var side = new EngineSide(Side.Client);
        SourceSharp.Client.Interop.ClientEntityFetches.AddTo(services, new ModuleEngine(side));
        EngineServices.AddTo(services, side);
        var entities = services.Require<IEngineEntities>();

        Assert.Same(services.Require<IClientReplayEngine>(), entities.ClientReplay);
        Assert.Same(services.Require<IInterpolationBlocks>(), entities.InterpolationBlocks);
        Assert.Same(services.Require<IRenderPolicyBlocks>(), entities.RenderPolicyBlocks);
        Assert.IsNotType<NoClientReplayEngine>(entities.ClientReplay);
    }

    [Fact]
    public void ASideWithNoClientFetchesAnswersNothing()
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, new EngineSide(Side.Server));
        var entities = services.Require<IEngineEntities>();

        Assert.Same(NoClientReplayEngine.Instance, entities.ClientReplay);
        Assert.Same(NoInterpolationBlocks.Instance, entities.InterpolationBlocks);
        Assert.Same(NoRenderPolicyBlocks.Instance, entities.RenderPolicyBlocks);
    }

    //-------------------------------------------------------------------------
    // The engine client's C++ proxies.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEngineClientPrintsThroughTheSideItWasAcquiredOn()
    {
        var (first, _) = TwoSides(Side.Client, "SourceSharpViewClient001",
            nameof(ShimViewClientVtbl.ConNPrintf),
            (nint)(delegate* unmanaged<void*, void*, int, byte*, void>)&PrintFirst,
            (nint)(delegate* unmanaged<void*, void*, int, byte*, void>)&PrintSecond);

        void* self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        try
        {
            var client = (IVtableEngineClient)typeof(EngineClient)
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single()
                .Invoke([first, Pointer.Box(self, typeof(void*)), Pointer.Box(null, typeof(EngineClientFwd*))]);

            client.ConsolePrintAt(3, "hello");
        }
        finally
        {
            NativeMemory.Free(self);
        }

        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));
    }

    //-------------------------------------------------------------------------
    // A networked state's blob, bound on its registry's side.
    //-------------------------------------------------------------------------

    [Fact]
    public void ANetStateBindsAndAnswersOnTheSideItWasBoundOn()
    {
        _first.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerVtbl.EntityBindBlob),
                    (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobFirst);
        _second.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerVtbl.EntityBindBlob),
                     (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobSecond);
        s_firstCalls = s_secondCalls = 0;

        EngineSide first = Attached(_first, Side.Server);
        _ = Attached(_second, Side.Server);

        var state = new SourceSharp.Shared.BarrelNetState();
        state.Bind(first, Player);

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
        _first.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerVtbl.EntityBindBlob),
                    (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobFirst);
        _second.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerVtbl.EntityBindBlob),
                     (nint)(delegate* unmanaged<void*, uint, void*, void*, void*, void*>)&BindBlobSecond);
        s_firstCalls = s_secondCalls = 0;

        EngineSide first = Attached(_first, Side.Server);
        _ = Attached(_second, Side.Server);

        var holder = new HoldsAState();
        NetState[] states = NetStateServerBinding.AttachBound(holder, Player, first, new NetStateFields(new RecordingLog()));

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
        state.Bind(new EngineSide(Side.Server), Player);

        Assert.False(state.IsBound, "no engine: a local state, not a bound one");
        Assert.True(state.IsLive, "but it has storage");
    }

    //-------------------------------------------------------------------------
    // The fakes.
    //-------------------------------------------------------------------------

    private static readonly nint* s_oneBlock = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));

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
    private static int RunMoveSeven(void* self, uint player, int index, int stage) { s_firstCalls++; return 7; }

    [UnmanagedCallersOnly]
    private static int RunMoveOne(void* self, uint player, int index, int stage) { s_firstCalls++; return 1; }

    [UnmanagedCallersOnly]
    private static int RunMoveNine(void* self, uint player, int index, int stage) { s_secondCalls++; return 9; }

    [UnmanagedCallersOnly]
    private static int ContentsSeven(void* self, void* point, int slot) { s_firstCalls++; return 7; }

    [UnmanagedCallersOnly]
    private static int ContentsNine(void* self, void* point, int slot) { s_secondCalls++; return 9; }

    [UnmanagedCallersOnly]
    private static int ProbeFirst(void* self, uint player, void* state) { s_firstCalls++; return 0; }

    [UnmanagedCallersOnly]
    private static int ProbeSecond(void* self, uint player, void* state) { s_secondCalls++; return 0; }

    [UnmanagedCallersOnly]
    private static int UserCmdFound(void* self, int number, void* cmd)
    {
        s_firstCalls++;
        ((UserCmd*)cmd)->CommandNumber = number;
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int UserCmdMissing(void* self, int number, void* cmd) { s_secondCalls++; return 0; }

    [UnmanagedCallersOnly]
    private static void* InterpOne(void* self, void* count, void* amount)
    {
        s_firstCalls++;
        *(int*)count = 1;
        *(float*)amount = 0.25f;
        return s_oneBlock;
    }

    [UnmanagedCallersOnly]
    private static void* InterpNone(void* self, void* count, void* amount)
    {
        s_secondCalls++;
        *(int*)count = 0;
        *(float*)amount = 0f;
        return null;
    }

    [UnmanagedCallersOnly]
    private static void* PolicyOne(void* self, void* count)
    {
        s_firstCalls++;
        *(int*)count = 1;
        return s_oneBlock;
    }

    [UnmanagedCallersOnly]
    private static void* PolicyNone(void* self, void* count)
    {
        s_secondCalls++;
        *(int*)count = 0;
        return null;
    }

    [UnmanagedCallersOnly]
    private static void PrintFirst(void* self, void* client, int position, byte* text) => s_firstCalls++;

    [UnmanagedCallersOnly]
    private static void PrintSecond(void* self, void* client, int position, byte* text) => s_secondCalls++;
}
