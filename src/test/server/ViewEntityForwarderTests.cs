using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine-server calls that were one-call slots until plan_server_native_v2
/// T3, now made through IVEngineServer's forwarders (EngineServer) from the
/// shared bindings. The view-entity rule is the one EngineSideTests held for
/// the slot: a side judges the view entity by its OWN liveness mirror, and a
/// view it knows is gone gives the player its own eyes.
/// </summary>
public sealed unsafe class ViewEntityForwarderTests
{
    private const nint EdictBase = 0x10000;
    private static nint s_ViewPlayer, s_ViewTarget, s_Command;
    private static int s_Executes, s_Style;
    private static string? s_Text;

    [UnmanagedCallersOnly] private static void* Edict(void* self, int index) => (void*)(EdictBase + index);
    [UnmanagedCallersOnly] private static void* NoEdict(void* self, int index) => null;
    [UnmanagedCallersOnly] private static void SetView(void* self, void* player, void* view)
    {
        s_ViewPlayer = (nint)player;
        s_ViewTarget = (nint)view;
    }
    [UnmanagedCallersOnly] private static int UserId(void* self, void* edict) => (int)((nint)edict - EdictBase) + 100;
    [UnmanagedCallersOnly] private static int NoClient(void* self, void* edict) => -1;
    [UnmanagedCallersOnly] private static void Command(void* self, byte* text)
    {
        s_Command++;
        s_Text = Marshal.PtrToStringUTF8((nint)text);
    }
    [UnmanagedCallersOnly] private static void Execute(void* self) => s_Executes++;
    [UnmanagedCallersOnly] private static void Style(void* self, int style, byte* pattern)
    {
        s_Style = style;
        s_Text = Marshal.PtrToStringUTF8((nint)pattern);
    }

    private sealed class Rig : System.IDisposable
    {
        public readonly FakeShimRoot Root = new();
        public readonly nint* Self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        public readonly EngineSide Side = TestSides.Create(SourceSharp.Abi.Side.Server);
        public readonly LivenessMirror Mirror = new();
        public readonly EngineServerFwd* Fwd;
        private readonly System.IDisposable _liveness;

        public Rig()
        {
            Root.Interface(EngineServer.InterfaceVersion, Self).Forwarders(out EngineServerFwd* fwd);
            Root.Forwarders<NetChannelInfoFwd>();
            Fwd = fwd;
            fwd->PEntityOfEntIndex = &Edict;
            fwd->SetView = &SetView;
            fwd->GetPlayerUserId = &UserId;
            fwd->ServerCommand = &Command;
            fwd->ServerExecute = &Execute;
            fwd->LightStyle = &Style;
            Side.Initialize();
            Assert.True(Side.AttachOwnShims(Root.Bind(), out string why), why);
            Side.Bound.UseServer((NativeInterfaceFactory factory, SourceSharp.Abi.Side _, out string w) => EngineServer.Acquire(factory, out w));
            _liveness = Side.AttachLiveness(Mirror);
            s_ViewPlayer = s_ViewTarget = 0;
            s_Command = s_Executes = 0;
            s_Style = -1;
            s_Text = null;
        }

        public void Dispose()
        {
            _liveness.Dispose();
            NativeMemory.Free(Self);
            Root.Dispose();
        }
    }

    [Fact]
    public void TheViewEntityIsJudgedByTheSidesOwnEngine()
    {
        using var rig = new Rig();
        var player = new EntityHandle(1);
        var view = new EntityHandle(40);
        var gone = new EntityHandle(41);
        rig.Mirror.NoteCreated(player);
        rig.Mirror.NoteCreated(view);
        rig.Mirror.NoteCreated(gone);
        rig.Mirror.NoteRemoved(gone);
        var players = SideBindings.Of<IEnginePlayers>(rig.Side);

        players.SetViewEntity(player, view);
        Assert.Equal((EdictBase + 1, EdictBase + 40), (s_ViewPlayer, s_ViewTarget));

        // The control: a view the side knows is gone gives the player its own.
        players.SetViewEntity(player, gone);
        Assert.Equal((EdictBase + 1, EdictBase + 1), (s_ViewPlayer, s_ViewTarget));
    }

    [Fact]
    public void AnUnknownPlayerSetsNoView()
    {
        using var rig = new Rig();
        // Known to the mirror and gone (a handle it never saw would ask the
        // EntityIsValid slot, which a fake root does not serve).
        rig.Mirror.NoteCreated(new EntityHandle(1));
        rig.Mirror.NoteRemoved(new EntityHandle(1));

        SideBindings.Of<IEnginePlayers>(rig.Side).SetViewEntity(new EntityHandle(1), new EntityHandle(40));

        Assert.Equal((nint)0, s_ViewPlayer);
    }

    [Fact]
    public void TheUserIdIsTheEnginesAndZeroWithoutAClient()
    {
        using var rig = new Rig();
        var player = new EntityHandle(3);
        rig.Mirror.NoteCreated(player);
        var players = SideBindings.Of<IEnginePlayers>(rig.Side);

        Assert.Equal(103, players.GetUserId(player));

        rig.Fwd->GetPlayerUserId = &NoClient;
        Assert.Equal(0, players.GetUserId(player));

        rig.Fwd->PEntityOfEntIndex = &NoEdict;
        Assert.Equal(0, players.GetUserId(player));

        var gone = new EntityHandle(9);
        rig.Mirror.NoteCreated(gone);
        rig.Mirror.NoteRemoved(gone);
        Assert.Equal(0, players.GetUserId(gone));   // not alive on this side
    }

    [Fact]
    public void AServerCommandGetsItsNewlineAndRunsNow()
    {
        using var rig = new Rig();

        EngineBound.Server(rig.Side).ServerCommandNow("mp_restartgame 1");

        Assert.Equal((1, 1, "mp_restartgame 1\n"), (s_Command, s_Executes, s_Text));
    }

    [Fact]
    public void ALightStyleOutsideTheEnginesSixtyFourIsNotSent()
    {
        using var rig = new Rig();
        var world = SideBindings.Of<IEngineVisibility>(rig.Side);

        world.SetLightStyle(64, "a");
        Assert.Null(s_Text);
        world.SetLightStyle(-1, "a");
        Assert.Null(s_Text);

        world.SetLightStyle(12, "mmnmmommommnonmmonqnmmo");
        Assert.Equal((12, "mmnmmommommnonmmonqnmmo"), (s_Style, s_Text));
    }
}
