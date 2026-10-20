using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's managed command stages (plan_server_native_v2 T4g) reach the
/// engine through the side they were built over (plan_abi_hotreload D8): two
/// fake shim roots on two sides, each answering, and only the runner's asked.
/// </summary>
public sealed unsafe class ServerCommandSideTests : IDisposable
{
    private readonly FakeShimRoot _first = new();
    private readonly FakeShimRoot _second = new();

    private static readonly EntityHandle Player = new(0x0001_0001u);

    private const string MovementServer = "SourceSharpMovementServer001";

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

        bool attached = api.AttachOwnShims(root.Bind(), out string why);

        Assert.True(attached, why);
        return api;
    }

    private sealed class Globals1 : IEngineEnvironment, IDisposable
    {
        public readonly CGlobalVarsRaw* Raw = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));
        public nint GetInterface(string name) => 0;
        public Globals Globals => new(Raw);
        public void Dispose() => NativeMemory.Free(Raw);
    }

    private sealed class NoPublish : IPlayerMovePublisher
    {
        public void Publish(EntityHandle player, ServerPlayerMove move) { }
    }

    private sealed class NoSounds : IMoveSoundSink
    {
        public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch) { }

        public void MoveSoundScript(int entIndex, Vector origin, string soundName) { }
    }

    [Fact]
    public void TheCommandStagesAskTheirRunnersSide()
    {
        // The prepare asks PlayerCommandBegin for the engine's facts and
        // writes the view angle back, both on the side it was built over; the
        // second side answers too and must never be asked.
        _first.Live(MovementServer, nameof(ShimMovementServerFwd.PlayerCommandBegin),
                    (nint)(delegate* unmanaged<void*, uint, void*, int>)&BeginFirst);
        _first.Live(MovementServer, nameof(ShimMovementServerFwd.PlayerCommandView),
                    (nint)(delegate* unmanaged<void*, uint, void*, int, void>)&ViewFirst);
        _second.Live(MovementServer, nameof(ShimMovementServerFwd.PlayerCommandBegin),
                     (nint)(delegate* unmanaged<void*, uint, void*, int>)&BeginSecond);
        _second.Live(MovementServer, nameof(ShimMovementServerFwd.PlayerCommandView),
                     (nint)(delegate* unmanaged<void*, uint, void*, int, void>)&ViewSecond);
        s_firstCalls = s_secondCalls = 0;

        var engine = new ModuleEngine(Attached(_first, Side.Server));
        _ = Attached(_second, Side.Server);

        using var env = new Globals1();
        env.Raw->interval_per_tick = 0.015f;
        env.Raw->maxClients = 2;

        var commands = new ServerPlayerCommands(engine, new ServerPlayerMoves(), new ServerMoveData(),
                                                new NoSounds(), null,
                                                new MovementRunner(engine), () => default, new NoPublish(), env);

        var cmd = new UserCmd { CommandNumber = 1 };
        Assert.Equal(1, commands.Stage(Player, 0, 0, in cmd));

        // Begin and the view write, both on the first side.
        Assert.Equal((2, 0), (s_firstCalls, s_secondCalls));
    }

    [UnmanagedCallersOnly]
    private static int BeginFirst(void* self, uint player, void* facts) { s_firstCalls++; return 1; }

    [UnmanagedCallersOnly]
    private static int BeginSecond(void* self, uint player, void* facts) { s_secondCalls++; return 1; }

    [UnmanagedCallersOnly]
    private static void ViewFirst(void* self, uint player, void* angles, int fixAngle) { s_firstCalls++; }

    [UnmanagedCallersOnly]
    private static void ViewSecond(void* self, uint player, void* angles, int fixAngle) { s_secondCalls++; }
}
