extern alias client;

using Xunit;

using Abi = client::SourceSharp.Abi;
using ClientShared = client::SourceSharp.Shared;
using ClientSide = client::SourceSharp.Client;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Menu = client::TF2.Client.Ui.TFTeamMenuUi;
using MenuChoice = TF2.Client.Ui.TFTeamChoice;          // the contract assembly is unaliased
using Nav = client::TF2.Client.Ui.ITFMenuNavigation;
using Standalone = client::SourceSharp.Client.Standalone;

namespace TF2.Tests.Client;

/// <summary>
/// The TF2 team menu's game half (stage 3.2): the two head counts off the
/// player resource's rows, the local player's team, the spectate gate on
/// <c>mp_allowspectators</c>, and what a choice sends — the named
/// <c>jointeam</c>, with stock's same-team arm and the closing click
/// (CTFTeamMenu::OnCommand, tf_teammenu.cpp:615-665). Before and after
/// the data arrives, as every adapter fact does.
/// </summary>
public sealed class TFTeamMenuUiTests
{
    [Fact]
    public void The_head_counts_bind_to_the_player_resource()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 0);
        var st = new Standalones();
        var menu = new Menu(st, engine, engine, new EngineClient(), new Recorder());

        // Before the resource arrives: nobody on either team.
        Assert.Equal(0, menu.RedPlayers);
        Assert.Equal(0, menu.BluePlayers);
        Assert.Equal(0, menu.CurrentTeam);

        // After: two on RED, one on BLU, and the local player (slot 2) on RED.
        var resource = Arrive(st);
        Assert.Equal(2, menu.RedPlayers);
        Assert.Equal(1, menu.BluePlayers);
        engine.Returns("Hud.LocalPlayerIndex", 2);
        Assert.Equal(2, menu.CurrentTeam);

        // The resource leaving takes the counts back to nobody: the live
        // list is what says so (the cache is keyed on being in it), and
        // an adapter without that check would keep reading the stale rows.
        resource.OnRemoved();
        st.All.Remove(resource);
        Assert.Equal(0, menu.RedPlayers);
    }

    [Fact]
    public void A_choice_sends_the_named_jointeam_and_closes_the_menu()
    {
        var (menu, sent, nav) = Built(connected: false);

        menu.Choose(MenuChoice.Red);
        Assert.Equal(["jointeam red"], sent.Lines);        // before ≠ after: the send is recorded
        Assert.Equal(["tf2teammenu"], nav.Closed);          // and the menu closes whichever way

        menu.Choose(MenuChoice.Auto);
        Assert.Equal(["jointeam red", "jointeam auto"], sent.Lines);
        menu.Choose(MenuChoice.Spectate);
        Assert.Contains("jointeam spectate", sent.Lines);
    }

    /// <summary>Stock's same-team arm (tf_teammenu.cpp:650-654): the team
    /// you are already on sends nothing, and the menu still closes.</summary>
    [Fact]
    public void Choosing_the_team_you_are_on_sends_nothing_but_still_closes()
    {
        var (menu, sent, nav) = Built(connected: true);      // local (slot 2) on RED

        menu.Choose(MenuChoice.Red);
        Assert.Empty(sent.Lines);                            // the same-team arm
        Assert.Equal(["tf2teammenu"], nav.Closed);

        menu.Choose(MenuChoice.Blue);                        // and the stimulus: the other team does send
        Assert.Equal(["jointeam blue"], sent.Lines);
    }

    [Fact]
    public void Cancel_closes_without_sending_anything()
    {
        var (menu, sent, nav) = Built(connected: true);
        menu.Cancel();
        Assert.Empty(sent.Lines);
        Assert.Equal(["tf2teammenu"], nav.Closed);
    }

    /// <summary>The next panel is the server's, not the client's chain:
    /// stock's jointeam shows the class panel from the server once the
    /// team is set (tf_player.cpp:6450-6455) — and a refused join gets
    /// the team panel back instead (:6434-6440), which a client chain
    /// could not know. So the choice sends and closes, and opens
    /// nothing itself; the class menu arrives by the server's own
    /// VGUIMenu (the step-1 push, answered as the named panels open).</summary>
    [Fact]
    public void A_choice_sends_closes_the_team_menu_and_opens_nothing_itself()
    {
        var (menu, _, nav) = Built(connected: false);
        menu.Choose(MenuChoice.Red);
        Assert.Equal(["tf2teammenu"], nav.Closed);
        Assert.Empty(nav.Opened);                            // before the chain's removal this held tf2classmenu

        var (same, _, sameNav) = Built(connected: true);     // local already on RED
        same.Choose(MenuChoice.Red);                         // the same-team arm sends nothing...
        Assert.Equal(["tf2teammenu"], sameNav.Closed);
        Assert.Empty(sameNav.Opened);                        // ...and still raises nothing

        var (spec, _, specNav) = Built(connected: false);
        spec.Choose(MenuChoice.Spectate);
        Assert.Equal(["tf2teammenu"], specNav.Closed);
        Assert.Empty(specNav.Opened);                        // spectating: the same
    }

    /// <summary>The Spectate button's gate: stock reads
    /// <c>mp_allowspectators</c> (tf_teammenu.cpp:729, :836).</summary>
    [Fact]
    public void Spectating_follows_the_mp_allowspectators_switch()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 0);
        engine.Returns("Console.FindConVarValue", "0");
        var menu = new Menu(new Standalones(), engine, engine, new EngineClient(), new Recorder());
        Assert.False(menu.AllowsSpectators);                 // before: off
        engine.Returns("Console.FindConVarValue", "1");
        Assert.True(menu.AllowsSpectators);                  // after: on
    }

    // ---- the rig ----

    private static (Menu Menu, EngineClient Sent, Recorder Nav) Built(bool connected)
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", connected ? 2 : 0);
        var st = new Standalones();
        if (connected)
            Arrive(st);
        var sent = new EngineClient();
        var nav = new Recorder();
        return (new Menu(st, engine, engine, sent, nav), sent, nav);
    }

    /// <summary>The resource arriving with three connected players: two on
    /// RED (slots 2 and 3), one on BLU (slot 4); the local player's slot
    /// (2) is the fact's to ask for.</summary>
    private static Standalone.ClientPlayerResource Arrive(Standalones st)
    {
        var resource = new Standalone.ClientPlayerResource(new Engine());
        resource.State.BindLocal();
        Connect(resource, 2, 2);
        Connect(resource, 3, 2);
        Connect(resource, 4, 3);
        st.All.Add(resource);
        return resource;
    }

    private static void Connect(Standalone.ClientPlayerResource resource, int slot, int team)
    {
        resource.State.Flags[slot] = ClientShared.PlayerResourceNetState.FlagConnected;
        resource.State.Team[slot] = (byte)team;
    }

    private sealed class Standalones : ClientSide.IClientStandalones
    {
        public readonly List<Standalone.ClientStandaloneEntity> All = [];

        public IReadOnlyList<Standalone.ClientStandaloneEntity> Live() => All;

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class Recorder : Nav
    {
        public readonly List<string> Opened = [], OpenedPanels = [], Closed = [];

        public bool Has(string screen) => true;

        public void Open(string screen) => Opened.Add(screen);

        public void OpenPanel(string panel) => OpenedPanels.Add(panel);

        public void Close(string screen) => Closed.Add(screen);
    }

    /// <summary>The engine client recording every line that goes out
    /// (the ChatFeedTests double's shape).</summary>
    private sealed class EngineClient : Abi.IFwdEngineClient
    {
        public readonly List<string> Lines = [];

        public void SubmitConsoleLine(string line) => Lines.Add(line);

        public void SpectatorCommand(Abi.SpectatorKeyCommand command) => Lines.Add(command.ToString()!);
        public void SpectatorServerCommand(string line) => Lines.Add(line);
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) { }
        public int SentenceIndexFromName(string name) => -1;
        public int LocalPlayerSlot() => 0;
        public string? LevelName() => null;
        public string? GameDirectory() => null;
        public uint ProtocolVersion() => 0;
        public bool CopyLocalFile(string source, string destination) => false;
        public bool TryGetPlayerInfo(int entIndex, out Abi.ClientPlayerInfo info) { info = default; return false; }
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() { }
        public void RequestManagedReload() { }
        public void ConsolePrintAt(int position, string text) { }
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }
        // Vector lives in the unaliased Abi assembly; the ClientPlayerInfo/
        // SpectatorKeyCommand/IFwdEngineClient pair is the aliased interface one.
        public bool TryLightForPoint(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
    }
}
