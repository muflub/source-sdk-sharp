using System.Runtime.InteropServices;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Steam callbacks (client Steam, Decision 1a; plan_engine_forwarders.md
/// phase 5). libss_fwd_steam builds each CCallbackBase Steam registers over a
/// managed block, and these facts hold what managed does with it: acquire the
/// library's table, build one callback per registration over its own block,
/// hand Steam that callback, and on Dispose unregister what Steam still holds,
/// free the callback's memory and release the block.
///
/// <para>
/// THE LIBRARY IS A FAKE HERE, and models the native callback only as far as
/// managed can see it: the managed block it calls, the payload size, and the
/// flags and id Steam writes into it. How the real one orders CCallbackBase's
/// overloads and answers Steam is fwd_steam_test's.
/// </para>
/// </summary>
public unsafe class SteamCallbacksTests
{
    /// <summary>
    /// libss_fwd_steam's table over a fake callback: word 0 the managed block
    /// it calls, the int at 8 the payload size, the flags Steam writes at 16
    /// and the id at 20.
    /// </summary>
    internal static class FakeSteamLibrary
    {
        public const int CallbackBytes = 24;

        [UnmanagedCallersOnly]
        private static int SizeOf() => CallbackBytes;

        [UnmanagedCallersOnly]
        private static void* Construct(void* memory, void* target, int payloadBytes)
        {
            if (memory == null || target == null)
                return null;
            ((nint*)memory)[0] = (nint)target;
            ((int*)memory)[2] = payloadBytes;
            ((int*)memory)[4] = 0;
            ((int*)memory)[5] = 0;
            return memory;
        }

        [UnmanagedCallersOnly]
        private static void* Refuse(void* memory, void* target, int payloadBytes) => null;

        [UnmanagedCallersOnly]
        private static byte IsRegistered(void* callback)
            => callback != null && (((byte*)callback)[16] & 1) != 0 ? (byte)1 : (byte)0;

        [UnmanagedCallersOnly]
        private static int CallbackId(void* callback) => callback == null ? 0 : ((int*)callback)[5];

        /// <summary>Fills <paramref name="fwd"/>; with <paramref name="builds"/> false, Construct builds nothing.</summary>
        public static void Fill(SteamCallbackFwd* fwd, bool builds = true)
        {
            fwd->SizeOf = &SizeOf;
            fwd->Construct = builds
                ? (delegate* unmanaged<void*, void*, int, void*>)&Construct
                : &Refuse;
            fwd->IsRegistered = &IsRegistered;
            fwd->CallbackId = &CallbackId;
        }

        /// <summary>The managed block the callback was built over.</summary>
        public static nint TargetOf(nint callback) => ((nint*)callback)[0];

        /// <summary>The payload size the callback was built with.</summary>
        public static int PayloadBytesOf(nint callback) => ((int*)callback)[2];
    }

    /// <summary>What libsteam_api does to a callback, against the fake callback's bytes.</summary>
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
            ((byte*)callback)[16] |= 1;            // the registered flag
            ((int*)callback)[5] = callbackId;      // the id
        }

        public void UnregisterCallback(nint callback)
        {
            Unregistered.Add(callback);
            ((byte*)callback)[16] &= unchecked((byte)~1);
        }

        public string? LaunchCommandLine(out int bytes)
        {
            bytes = LaunchBytes;
            return LaunchLine;
        }

        /// <summary>
        /// SteamAPI_RunCallbacks dispatching one callback, as the native one
        /// runs it: its managed block's Run (IManagedSteamCallback slot 2)
        /// with the payload.
        /// </summary>
        public static void Run(nint callback, byte[] payload)
        {
            void* block = (void*)FakeSteamLibrary.TargetOf(callback);
            var run = (delegate* unmanaged<void*, void*, void>)(*(nint**)block)[2];
            fixed (byte* p = payload)
                run(block, p);
        }
    }

    private sealed class Log : ILog
    {
        public readonly List<string> Lines = new();
        public void Msg(string m) => Lines.Add(m);
        public void Warning(string m) => Lines.Add(m);
    }

    /// <summary>The callbacks over a client engine whose root serves the fake library.</summary>
    internal sealed class Fixture : IDisposable
    {
        private readonly FakeShimRoot _root = new();

        public readonly ManagedVtables Vtables = new();
        public readonly SteamDouble Steam = new();
        public readonly SteamCallbacks Callbacks;

        public Fixture(bool builds = true, SteamDouble? steam = null, ILog? log = null)
        {
            Steam = steam ?? Steam;
            _root.Forwarders(out SteamCallbackFwd* fwd);
            FakeSteamLibrary.Fill(fwd, builds);

            var side = new EngineSide(Side.Client);
            side.Initialize();
            Assert.True(side.AttachShims(_root.Bind(), out string attach), attach);

            Callbacks = SteamCallbacks.Acquire(new ModuleEngine(side), Vtables, Steam, log ?? new Log(), out string why)
                ?? throw new InvalidOperationException(why);
        }

        public void Dispose()
        {
            Callbacks.Dispose();
            Vtables.Dispose();
            _root.Dispose();
        }
    }

    [Fact]
    public void RegisteringHandsSteamTheNativeCallbackAndTheIdItWritesBack()
    {
        using var f = new Fixture();

        Assert.True(f.Callbacks.Register("connected", 101, 1, _ => { }));

        nint callback = f.Callbacks.CallbackOf("connected");
        Assert.NotEqual(0, callback);
        Assert.Equal(new[] { (callback, 101) }, f.Steam.Registered);
    }

    [Fact]
    public void TheNativeCallbackIsBuiltOverItsOwnBlockWithThePayloadSize()
    {
        using var f = new Fixture();
        f.Callbacks.Register("join", 337, 264, _ => { });

        nint callback = f.Callbacks.CallbackOf("join");
        Assert.Equal(f.Callbacks.BlockOf("join")!.Address, FakeSteamLibrary.TargetOf(callback));
        Assert.Equal(264, FakeSteamLibrary.PayloadBytesOf(callback));
        Assert.Equal(0, f.Callbacks.BlockOf("join")!.ReservedWords);   // the block carries no CCallbackBase data
    }

    [Fact]
    public void SteamsRunHandsTheHandlerThePayloadBytes()
    {
        using var f = new Fixture();
        byte[]? seen = null;
        f.Callbacks.Register("failure", 102, 8, payload => seen = payload.ToArray());

        SteamDouble.Run(f.Callbacks.CallbackOf("failure"), [5, 0, 0, 0, 1, 0, 0, 0]);

        Assert.Equal(new byte[] { 5, 0, 0, 0, 1, 0, 0, 0 }, seen);
    }

    [Fact]
    public void EachCallbackIsItsOwnBlock()
    {
        using var f = new Fixture();
        var hits = new List<string>();
        f.Callbacks.Register("a", 101, 1, _ => hits.Add("a"));
        f.Callbacks.Register("b", 103, 4, _ => hits.Add("b"));

        SteamDouble.Run(f.Callbacks.CallbackOf("b"), [0, 0, 0, 0]);
        SteamDouble.Run(f.Callbacks.CallbackOf("a"), [0]);

        Assert.Equal(new[] { "b", "a" }, hits);
        Assert.NotEqual(f.Callbacks.CallbackOf("a"), f.Callbacks.CallbackOf("b"));
        Assert.Equal(2, f.Vtables.Live);
    }

    [Fact]
    public void ACallbackIdIsRegisteredOnce()
    {
        using var f = new Fixture();

        Assert.True(f.Callbacks.Register("a", 101, 1, _ => { }));
        Assert.False(f.Callbacks.Register("b", 101, 1, _ => { }));

        Assert.Single(f.Steam.Registered);
        Assert.Equal(1, f.Vtables.Live);
    }

    [Fact]
    public void NoSteamInTheProcessRegistersNothing()
    {
        using var f = new Fixture(steam: new SteamDouble { Present = false });

        Assert.False(f.Callbacks.Register("a", 101, 1, _ => { }));

        Assert.Empty(f.Steam.Registered);
        Assert.Equal(0, f.Vtables.Live);
    }

    [Fact]
    public void ACallbackTheLibraryDoesNotBuildIsNotRegisteredAndLeavesNoBlock()
    {
        var log = new Log();
        using var f = new Fixture(builds: false, log: log);

        Assert.False(f.Callbacks.Register("a", 101, 1, _ => { }));

        Assert.Empty(f.Steam.Registered);
        Assert.Empty(f.Callbacks.Registered);
        Assert.Equal(0, f.Vtables.Live);
        Assert.Equal(1, f.Vtables.Freed);   // the control: a block was made, then released
        Assert.Contains(log.Lines, l => l.Contains("a: the native callback was not built"));
    }

    [Fact]
    public void DisposeUnregistersWhatSteamHoldsAndFreesEveryBlock()
    {
        using var f = new Fixture();
        f.Callbacks.Register("a", 101, 1, _ => { });
        f.Callbacks.Register("b", 103, 4, _ => { });
        nint a = f.Callbacks.CallbackOf("a"), b = f.Callbacks.CallbackOf("b");
        Assert.Equal(2, f.Vtables.Live);   // the control

        f.Callbacks.Dispose();

        Assert.Equal(new[] { a, b }, f.Steam.Unregistered.OrderBy(x => x == a ? 0 : 1));
        Assert.Equal(0, f.Vtables.Live);
        Assert.Equal(2, f.Vtables.Freed);
        Assert.Empty(f.Callbacks.Registered);
    }

    [Fact]
    public void ACallbackSteamNoLongerHoldsIsFreedWithoutAnUnregister()
    {
        using var f = new Fixture();
        f.Callbacks.Register("a", 101, 1, _ => { });
        f.Steam.UnregisterCallback(f.Callbacks.CallbackOf("a"));   // Steam dropped it already
        f.Steam.Unregistered.Clear();

        f.Callbacks.Dispose();

        Assert.Empty(f.Steam.Unregistered);
        Assert.Equal(0, f.Vtables.Live);
    }

    [Fact]
    public void AfterDisposeTheRegistryHasNothingToDetachForAReload()
    {
        using var f = new Fixture();
        f.Callbacks.Register("a", 101, 1, _ => { });
        f.Callbacks.Dispose();

        Assert.Equal(0, f.Vtables.Detach());

        // The next load registers afresh, on a new block and a new callback.
        Assert.True(f.Callbacks.Register("a", 101, 1, _ => { }));
    }

    [Fact]
    public void NoCallbacksWhereTheFactoryServesNoCallbackTable()
    {
        using var root = new FakeShimRoot();   // serves no SSFwd_SteamCallback
        var side = new EngineSide(Side.Client);
        side.Initialize();
        Assert.True(side.AttachShims(root.Bind(), out string attach), attach);
        using var vtables = new ManagedVtables();

        SteamCallbacks? callbacks = SteamCallbacks.Acquire(new ModuleEngine(side), vtables, new SteamDouble(), new Log(), out string why);

        Assert.Null(callbacks);
        Assert.Contains("SSFwd_SteamCallback", why);
    }
}
