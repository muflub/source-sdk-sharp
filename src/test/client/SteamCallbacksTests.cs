using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Steam callbacks' blocks (client Steam, Decision 1a), driven the way
/// Steam drives a CCallbackBase: SteamAPI_RegisterCallback writes the flags and
/// id into the word after the vptr, SteamAPI_RunCallbacks calls slot 0 with the
/// payload, and SteamAPI_UnregisterCallback clears the registered flag.
/// </summary>
public unsafe class SteamCallbacksTests
{
    /// <summary>What libsteam_api does to a CCallbackBase, against the block's own bytes.</summary>
    internal sealed class SteamDouble : ISteamClientApi
    {
        public readonly List<(nint Callback, int Id)> Registered = new();
        public readonly List<nint> Unregistered = new();
        public bool Present { get; set; } = true;
        public string? LaunchLine { get; set; }
        public int LaunchBytes { get; set; }

        public bool Available => Present;
        public bool InitSafe() => true;
        public void SetTryCatchCallbacks(bool tryCatch) { }
        public bool HasUserAndUtils => true;
        public bool InitFilterText() => true;
        public bool LoggedOn => true;
        public ulong SteamId => 0;
        public int Universe => 1;
        public uint AppId => 0;
        public string? FilterText(int context, ulong id, string text, int max) => text;

        public void RegisterCallback(nint callback, int callbackId)
        {
            Registered.Add((callback, callbackId));
            ((byte*)callback)[8] |= SteamCallbacks.RegisteredFlag;   // m_nCallbackFlags
            *(int*)((byte*)callback + 12) = callbackId;              // m_iCallback
        }

        public void UnregisterCallback(nint callback)
        {
            Unregistered.Add(callback);
            ((byte*)callback)[8] &= unchecked((byte)~SteamCallbacks.RegisteredFlag);
        }

        public string? LaunchCommandLine(out int bytes)
        {
            bytes = LaunchBytes;
            return LaunchLine;
        }

        /// <summary>SteamAPI_RunCallbacks dispatching one callback: slot 0 with the payload.</summary>
        public static void Run(nint callback, byte[] payload)
        {
            var run = (delegate* unmanaged<void*, void*, void>)((nint*)((nint*)callback)[0])[0];
            fixed (byte* p = payload)
                run((void*)callback, p);
        }

        public static void RunResult(nint callback, byte[] payload)
        {
            var run = (delegate* unmanaged<void*, void*, byte, ulong, void>)((nint*)((nint*)callback)[0])[1];
            fixed (byte* p = payload)
                run((void*)callback, p, 0, 99);
        }

        public static int SizeOf(nint callback)
            => ((delegate* unmanaged<void*, int>)((nint*)((nint*)callback)[0])[2])((void*)callback);
    }

    private sealed class Log : ILog
    {
        public readonly List<string> Lines = new();
        public void Msg(string m) => Lines.Add(m);
        public void Warning(string m) => Lines.Add(m);
    }

    [Fact]
    public void RegisteringHandsSteamTheBlockAndTheIdItWritesBack()
    {
        using var vtables = new ManagedVtables();
        var steam = new SteamDouble();
        using var callbacks = new SteamCallbacks(vtables, steam, new Log());

        Assert.True(callbacks.Register("connected", 101, 1, _ => { }));

        var block = callbacks.BlockOf("connected")!;
        Assert.Equal(new[] { (block.Address, 101) }, steam.Registered);
        Assert.Equal(101, SteamCallbacks.CallbackIdOf(block));
        Assert.Equal(SteamCallbacks.RegisteredFlag, SteamCallbacks.FlagsOf(block));
        Assert.Equal(1, block.ReservedWords);
    }

    [Fact]
    public void SteamsRunHandsTheHandlerThePayloadBytes()
    {
        using var vtables = new ManagedVtables();
        var steam = new SteamDouble();
        using var callbacks = new SteamCallbacks(vtables, steam, new Log());
        byte[]? seen = null;
        callbacks.Register("failure", 102, 8, payload => seen = payload.ToArray());

        SteamDouble.Run(callbacks.BlockOf("failure")!.Address, [5, 0, 0, 0, 1, 0, 0, 0]);

        Assert.Equal(new byte[] { 5, 0, 0, 0, 1, 0, 0, 0 }, seen);
    }

    [Fact]
    public void TheCallResultOverloadRunsTheSameHandler()
    {
        using var vtables = new ManagedVtables();
        var steam = new SteamDouble();
        using var callbacks = new SteamCallbacks(vtables, steam, new Log());
        int runs = 0;
        callbacks.Register("disconnected", 103, 4, _ => runs++);

        SteamDouble.RunResult(callbacks.BlockOf("disconnected")!.Address, [0, 0, 0, 0]);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void TheSizeSlotAnswersThePayloadSize()
    {
        using var vtables = new ManagedVtables();
        using var callbacks = new SteamCallbacks(vtables, new SteamDouble(), new Log());
        callbacks.Register("join", 337, 264, _ => { });

        Assert.Equal(264, SteamDouble.SizeOf(callbacks.BlockOf("join")!.Address));
    }

    [Fact]
    public void EachCallbackIsItsOwnBlock()
    {
        using var vtables = new ManagedVtables();
        using var callbacks = new SteamCallbacks(vtables, new SteamDouble(), new Log());
        var hits = new List<string>();
        callbacks.Register("a", 101, 1, _ => hits.Add("a"));
        callbacks.Register("b", 103, 4, _ => hits.Add("b"));

        SteamDouble.Run(callbacks.BlockOf("b")!.Address, [0, 0, 0, 0]);
        SteamDouble.Run(callbacks.BlockOf("a")!.Address, [0]);

        Assert.Equal(new[] { "b", "a" }, hits);
        Assert.Equal(2, vtables.Live);
    }

    [Fact]
    public void ACallbackIdIsRegisteredOnce()
    {
        using var vtables = new ManagedVtables();
        var steam = new SteamDouble();
        using var callbacks = new SteamCallbacks(vtables, steam, new Log());

        Assert.True(callbacks.Register("a", 101, 1, _ => { }));
        Assert.False(callbacks.Register("b", 101, 1, _ => { }));

        Assert.Single(steam.Registered);
        Assert.Equal(1, vtables.Live);
    }

    [Fact]
    public void NoSteamInTheProcessRegistersNothing()
    {
        using var vtables = new ManagedVtables();
        var steam = new SteamDouble { Present = false };
        using var callbacks = new SteamCallbacks(vtables, steam, new Log());

        Assert.False(callbacks.Register("a", 101, 1, _ => { }));

        Assert.Empty(steam.Registered);
        Assert.Equal(0, vtables.Live);
    }

    [Fact]
    public void DisposeUnregistersWhatSteamHoldsAndFreesEveryBlock()
    {
        using var vtables = new ManagedVtables();
        var steam = new SteamDouble();
        var callbacks = new SteamCallbacks(vtables, steam, new Log());
        callbacks.Register("a", 101, 1, _ => { });
        callbacks.Register("b", 103, 4, _ => { });
        nint a = callbacks.BlockOf("a")!.Address, b = callbacks.BlockOf("b")!.Address;
        Assert.Equal(2, vtables.Live);   // the control

        callbacks.Dispose();

        Assert.Equal(new[] { a, b }, steam.Unregistered.OrderBy(x => x == a ? 0 : 1));
        Assert.Equal(0, vtables.Live);
        Assert.Equal(2, vtables.Freed);
        Assert.Empty(callbacks.Registered);
    }

    [Fact]
    public void ABlockSteamNeverRegisteredIsFreedWithoutAnUnregister()
    {
        using var vtables = new ManagedVtables();
        var steam = new SteamDouble();
        var callbacks = new SteamCallbacks(vtables, steam, new Log());
        callbacks.Register("a", 101, 1, _ => { });
        steam.UnregisterCallback(callbacks.BlockOf("a")!.Address);   // Steam dropped it already
        steam.Unregistered.Clear();

        callbacks.Dispose();

        Assert.Empty(steam.Unregistered);
        Assert.Equal(0, vtables.Live);
    }

    [Fact]
    public void AfterDisposeTheRegistryHasNothingToDetachForAReload()
    {
        using var vtables = new ManagedVtables();
        var callbacks = new SteamCallbacks(vtables, new SteamDouble(), new Log());
        callbacks.Register("a", 101, 1, _ => { });
        callbacks.Dispose();

        Assert.Equal(0, vtables.Detach());

        // The next load registers afresh, on a new block.
        using var next = new SteamCallbacks(vtables, new SteamDouble(), new Log());
        Assert.True(next.Register("a", 101, 1, _ => { }));
    }
}
