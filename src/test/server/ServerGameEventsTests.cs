using System.Linq;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's game events through IGameEventManager2's forwarders
/// (plan_server_native_v2 T3): <see cref="ServerGameEvents"/> makes the calls
/// the EventCreate / EventFire / EventFree slot bodies made, with stock's
/// default arguments written out, and passes every other member to the area
/// it decorates.
/// </summary>
public sealed unsafe class ServerGameEventsTests
{
    private static nint s_Self, s_Event, s_Answer;
    private static string? s_Name;
    private static int s_Flag, s_Creates, s_Fires, s_Frees;
    private static byte s_FireAnswer;

    [UnmanagedCallersOnly]
    private static void* Create(void* self, byte* name, byte force)
    {
        s_Creates++;
        s_Self = (nint)self;
        s_Name = Marshal.PtrToStringUTF8((nint)name);
        s_Flag = force;
        return (void*)s_Answer;
    }

    [UnmanagedCallersOnly]
    private static byte Fire(void* self, void* e, byte dontBroadcast)
    {
        s_Fires++;
        s_Self = (nint)self;
        s_Event = (nint)e;
        s_Flag = dontBroadcast;
        return s_FireAnswer;
    }

    [UnmanagedCallersOnly]
    private static void Free(void* self, void* e)
    {
        s_Frees++;
        s_Self = (nint)self;
        s_Event = (nint)e;
    }

    private static ModuleEngine ServerOver(FakeShimRoot root)
    {
        var side = EngineSide.Create(Side.Server);
        side.Initialize();
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        return new ModuleEngine(side);
    }

    private sealed class Rig : System.IDisposable
    {
        public readonly FakeShimRoot Root = new();
        public readonly nint* Self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        public readonly RecordingEngine Inner = new() { Strict = false };
        public readonly RecordingLog Log = new();
        public readonly IEngineGameEvents Events;

        public Rig()
        {
            Root.Interface(ServerGameEvents.InterfaceVersion, Self).Forwarders(out GameEventManagerFwd* fwd);
            fwd->CreateEvent = &Create;
            fwd->FireEvent = &Fire;
            fwd->FreeEvent = &Free;
            Events = ServerGameEvents.Acquire(ServerOver(Root), Inner, Log, out string why)
                ?? throw new System.InvalidOperationException(why);
            s_Creates = s_Fires = s_Frees = 0;
            s_Flag = -1;
            s_Answer = 0;
            s_FireAnswer = 0;
        }

        public void Dispose()
        {
            NativeMemory.Free(Self);
            Root.Dispose();
        }
    }

    [Fact]
    public void CreatingAnEventAsksTheManagerForItByNameWithoutForcing()
    {
        using var rig = new Rig();
        s_Answer = 0x5150;

        nint e = rig.Events.EventCreate("player_say");

        Assert.Equal((nint)0x5150, e);
        Assert.Equal(1, s_Creates);
        Assert.Equal(((nint)rig.Self, "player_say", 0), (s_Self, s_Name, s_Flag));
        Assert.Empty(rig.Log.Lines);
    }

    [Fact]
    public void AnUndeclaredEventIsZeroAndSaysSo()
    {
        using var rig = new Rig();

        Assert.Equal(nint.Zero, rig.Events.EventCreate("no_such_event"));
        var line = Assert.Single(rig.Log.Lines);
        Assert.True(line.Warning);
        Assert.Contains("'no_such_event'", line.Text);
    }

    [Fact]
    public void AnEmptyNameAsksNothing()
    {
        using var rig = new Rig();

        Assert.Equal(nint.Zero, rig.Events.EventCreate(""));
        Assert.Equal(0, s_Creates);
    }

    [Fact]
    public void FiringBroadcastsTheEventAndAnswersTheManager()
    {
        using var rig = new Rig();
        s_FireAnswer = 1;

        Assert.True(rig.Events.EventFire((nint)0x77));
        Assert.Equal(((nint)rig.Self, (nint)0x77, 0), (s_Self, s_Event, s_Flag));

        s_FireAnswer = 0;
        Assert.False(rig.Events.EventFire((nint)0x77));
        Assert.Equal(2, s_Fires);
    }

    [Fact]
    public void ZeroIsNeitherFiredNorFreed()
    {
        using var rig = new Rig();

        Assert.False(rig.Events.EventFire(nint.Zero));
        rig.Events.EventFree(nint.Zero);

        Assert.Equal((0, 0), (s_Fires, s_Frees));
    }

    [Fact]
    public void FreeingHandsTheEventBack()
    {
        using var rig = new Rig();

        rig.Events.EventFree((nint)0x88);

        Assert.Equal(1, s_Frees);
        Assert.Equal(((nint)rig.Self, (nint)0x88), (s_Self, s_Event));
    }

    [Fact]
    public void SettingAndReadingAndListeningAreTheDecoratedAreas()
    {
        using var rig = new Rig();

        rig.Events.EventSetInt((nint)1, "userid", 7);
        rig.Events.EventSetString((nint)1, "text", "hi");
        rig.Events.ListenForGameEvent("round_start");

        Assert.Equal(new[] { "GameEvents.EventSetInt", "GameEvents.EventSetString", "GameEvents.ListenForGameEvent" },
                     rig.Inner.Calls.Select(c => c.Member).ToArray());
        Assert.Equal(0, s_Creates + s_Fires + s_Frees);
    }

    [Fact]
    public void NoDecoratorWhereTheEngineServesNoManager()
    {
        using var root = new FakeShimRoot();   // serves no GAMEEVENTSMANAGER002

        Assert.Null(ServerGameEvents.Acquire(ServerOver(root), new RecordingEngine(), new RecordingLog(), out string why));
        Assert.Contains("GAMEEVENTSMANAGER002", why);
    }
}
