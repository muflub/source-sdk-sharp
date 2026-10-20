using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Steam game server is an instance per server load (P1 run 3, the user's
/// ruling), over a loader seam so no fact loads Steam: a fake library whose
/// three exports are this class's own unmanaged functions.
/// </summary>
public unsafe class SteamGameServerInstanceTests
{
    private const nint FakeServer = 0x5150;

    [UnmanagedCallersOnly]
    private static nint Accessor() => FakeServer;

    [UnmanagedCallersOnly]
    private static byte LoggedOnExport(nint server) => server == FakeServer ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static void StatsExport(nint server)
    {
        if (server == FakeServer)
            StatsAsked.Value++;
    }

    // Per thread rather than per class: xUnit may run another class's facts on
    // another thread, and a count is only read on the thread that asked.
    private static readonly ThreadLocal<int> StatsAsked = new(() => 0);

    /// <summary>libsteam_api.so as a test hands it: loaded or not, with or without its exports.</summary>
    private sealed class FakeLibrary(bool loaded, bool exports = true) : ISteamLibrary
    {
        public int Opens { get; private set; }

        public IntPtr OpenLoaded(string file)
        {
            Opens++;
            return loaded && file == SteamGameServerFlat.Library ? (IntPtr)0x1000 : IntPtr.Zero;
        }

        public bool TryGetExport(IntPtr library, string name, out IntPtr address)
        {
            address = !exports ? IntPtr.Zero : name switch
            {
                _ when name == SteamGameServerFlat.AccessorExport => (IntPtr)(delegate* unmanaged<nint>)&Accessor,
                "SteamAPI_ISteamGameServer_BLoggedOn" => (IntPtr)(delegate* unmanaged<nint, byte>)&LoggedOnExport,
                "SteamAPI_ISteamGameServer_GetGameplayStats" => (IntPtr)(delegate* unmanaged<nint, void>)&StatsExport,
                _ => IntPtr.Zero,
            };
            return address != IntPtr.Zero;
        }
    }

    [Fact]
    public void TwoInstancesShareNoState()
    {
        var up = new SteamGameServerFlat(new FakeLibrary(loaded: true));
        var down = new SteamGameServerFlat(new FakeLibrary(loaded: false));

        Assert.True(up.Reacquire(), "the instance over a loaded library has a game server");
        Assert.False(down.Reacquire(), "the instance over none has none");

        Assert.True(up.LoggedOn(), "the other instance's failed activation did not clear this one");
        Assert.False(down.LoggedOn());
        Assert.Equal(string.Empty, up.WhyUnbound);
        Assert.Contains("is not loaded", down.WhyUnbound, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstanceBuiltAfterActivationBindsOnFirstUse()
    {
        // The cs_sv_reload case: the server was activated in an earlier load,
        // and this load's instance is never told.
        var loader = new FakeLibrary(loaded: true);
        var reloaded = new SteamGameServerFlat(loader);
        Assert.Equal(0, loader.Opens);

        Assert.True(reloaded.LoggedOn(), "the first use binds and fetches the live game server");
        Assert.True(reloaded.HasGameServer);

        int before = StatsAsked.Value;
        reloaded.RequestGameplayStats();
        Assert.Equal(before + 1, StatsAsked.Value);
        Assert.Equal(1, loader.Opens);

        // And a Clear still means "until the next activation", as stock's did.
        reloaded.Clear();
        Assert.False(reloaded.LoggedOn(), "a cleared instance does not fetch again on its own");
        Assert.True(reloaded.Reacquire(), "an activation does");
    }

    [Fact]
    public void AnUnboundInstanceReportsWhyUnbound()
    {
        var fresh = new SteamGameServerFlat(new FakeLibrary(loaded: true, exports: false));
        Assert.Equal("not asked yet", fresh.WhyUnbound);

        Assert.False(fresh.LoggedOn());
        Assert.False(fresh.HasGameServer);
        Assert.Contains("does not export " + SteamGameServerFlat.AccessorExport, fresh.WhyUnbound, StringComparison.Ordinal);
        Assert.Equal(IntPtr.Zero, fresh.LibraryHandle);
    }

    private sealed class AnsweringSteam(bool loggedOn) : ISteamGameServer
    {
        public bool Reacquire() => loggedOn;
        public void Clear() { }
        public void RequestGameplayStats() { }
        public bool LoggedOn() => loggedOn;
        public bool HasGameServer => loggedOn;
        public string WhyUnbound => string.Empty;
    }

    [Fact]
    public void TheHostAnswersFromTheInstanceItsCompositionHandsIt()
    {
        var served = new ServiceContainer().AddInstance(new StandaloneMirror());
        served.AddInstance<ISteamGameServer>(new AnsweringSteam(loggedOn: true));
        EngineServices.AddTo(served, EngineSide.Create(Side.Server));

        var bare = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(bare, EngineSide.Create(Side.Client));

        Assert.True(served.Require<IEngineHost>().SteamGameServerLoggedOn(), "the registered instance answers");
        Assert.False(bare.Require<IEngineHost>().SteamGameServerLoggedOn(), "a side that registers none has no game server");
        Assert.False(bare.Require<IEngineHost>().SteamGameServerReacquire());
    }
}
