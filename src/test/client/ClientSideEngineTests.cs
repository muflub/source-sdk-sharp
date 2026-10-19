using System.Reflection;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's half of <c>SideMovementEngineTests</c> (plan_abi_hotreload D8,
/// lane d8c): the client entity area's replay and frame blocks and the engine
/// client reached the engine through the static <c>Engine.Current</c>, which
/// followed whichever side was active. Each is now built over one side. These
/// facts attach two fake shim roots to two client sides, each answering
/// differently, and show every call lands on the side it was built over. They
/// live in the client suite because each side compiles its own copy of the
/// shared interop (plan_shim_areas_split phase 3), and the client's areas are
/// built from the client's.
/// </summary>
public unsafe class ClientSideEngineTests : IDisposable
{
    private readonly FakeShimRoot _first = new();
    private readonly FakeShimRoot _second = new();

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
    // The client's replay and frame blocks, held by its entity area.
    //-------------------------------------------------------------------------

    [Fact]
    public void EachEntityAreasReplayFetchesFromItsOwnSide()
    {
        var (first, second) = TwoSides(Side.Client, "SourceSharpPredictionClient001",
            nameof(ShimPredictionClientFwd.ClientGetUserCmd),
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
            nameof(ShimStandaloneClientFwd.ClientInterpBlocks),
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
            nameof(ShimStandaloneClientFwd.ClientPolicyBlocks),
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
        new SourceSharp.Client.Interop.ClientEntityFetches().AddTo(services, new ModuleEngine(side));
        EngineServices.AddTo(services, side);
        return services.Require<IEngineEntities>();
    }

    [Fact]
    public void TheClientsAreaAndContainerHoldTheSameThreeFetches()
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        using var root = new FakeShimRoot();
        var side = new EngineSide(Side.Client);
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        new SourceSharp.Client.Interop.ClientEntityFetches().AddTo(services, new ModuleEngine(side));
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
        void* self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        try
        {
            // Each root serves the engine client, so the one acquired through
            // the first side is the first root's, held as its interface
            // (plan_shim_areas_split phase 10).
            _first.Interface(EngineClient.InterfaceVersion, self).Forwarders<EngineClientFwd>();
            _second.Interface(EngineClient.InterfaceVersion, self).Forwarders<EngineClientFwd>();
            var (first, _) = TwoSides(Side.Client, "SourceSharpViewClient001",
                nameof(ShimViewClientFwd.ConNPrintf),
                (nint)(delegate* unmanaged<void*, void*, int, byte*, void>)&PrintFirst,
                (nint)(delegate* unmanaged<void*, void*, int, byte*, void>)&PrintSecond);

            IVtableEngineClient? client = EngineClient.Acquire(new ModuleEngine(first), out string why);
            Assert.True(client is not null, why);

            client.ConsolePrintAt(3, "hello");
        }
        finally
        {
            NativeMemory.Free(self);
        }

        Assert.Equal((1, 0), (s_firstCalls, s_secondCalls));
    }

    //-------------------------------------------------------------------------
    // The fakes.
    //-------------------------------------------------------------------------

    private static readonly nint* s_oneBlock = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));

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
