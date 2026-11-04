extern alias client;

using Xunit;

using Abi = client::SourceSharp.Abi;
using ClientShared = client::SourceSharp.Shared;
using ClientSide = client::SourceSharp.Client;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Menu = client::TF2.Client.Ui.TFClassMenuUi;
using Nav = client::TF2.Client.Ui.ITFMenuNavigation;
using Standalone = client::SourceSharp.Client.Standalone;
using TFResource = client::TF2.Client.Player.ClientTFPlayerResource;
using TFResources = client::TF2.Client.Player.TFClientPlayerResources;
using ChangeClass = client::TF2.Client.Ui.TFChangeClassCommand;

// The shared convars and the class enum, through the client alias: the
// test project also references the server assembly, which compiles the
// same shared sources as its own types, and the menu wants the client's.
using ClassLimitConVar = client::TF2.Shared.Player.TFClassLimitConVar;
using HighlanderConVar = client::TF2.Shared.MpHighlanderConVar;
using TFClass = client::TF2.Shared.TFClass;

namespace TF2.Tests.Client;

/// <summary>
/// The TF2 class menu's game half (stage 3.2): the per-class counts off
/// the two player resources with the local player not counted, the limit
/// off the replicated convars, and what a choice sends — the named
/// <c>joinclass</c>, with stock's full-class arm that sends nothing and
/// the closing click (CTFClassMenu::Go and Join_Class,
/// tf_classmenu.cpp:1216-1275, :1330-1339), plus the <c>changeclass</c>
/// command's gate on a game team (tf_viewport.cpp:193-211). Before and
/// after the data arrives, as every adapter fact does.
/// </summary>
public sealed class TFClassMenuUiTests
{
    [Fact]
    public void The_class_counts_bind_to_the_player_resources()
    {
        var rig = new Rig(arrived: false);

        // Before the resources arrive: nothing to count, no class, nobody.
        Assert.Equal(0, rig.Menu.Choice(1).Players);
        Assert.False(rig.Menu.HasClass);
        Assert.Equal(0, rig.Menu.Team);

        // After: two scouts and a medic on RED beside the local soldier
        // (slot 2).
        rig.Arrive();
        Assert.Equal(2, rig.Menu.Choice(1).Players);          // the teammates on scout
        Assert.Equal(1, rig.Menu.Choice(5).Players);          // the teammate medic
        Assert.True(rig.Menu.Choice(3).Current);              // the local soldier
        Assert.False(rig.Menu.Choice(1).Current);
        Assert.True(rig.Menu.HasClass);
        Assert.Equal(2, rig.Menu.Team);
    }

    /// <summary>Stock's count excludes the local player (:1492-1498): a
    /// scout themselves still counts only their scout teammates.</summary>
    [Fact]
    public void The_local_player_is_not_counted_against_their_own_class()
    {
        var rig = new Rig(localOnTeam: true);                 // local (slot 2) is the soldier
        Assert.Equal(0, rig.Menu.Choice(3).Players);          // one soldier: themselves

        rig.SetClass(2, (byte)TFClass.Scout);                   // and as a scout...
        Assert.Equal(2, rig.Menu.Choice(1).Players);          // ...still only the two teammates
        Assert.True(rig.Menu.Choice(1).Current);
    }

    [Fact]
    public void A_choice_sends_the_named_joinclass_and_closes_the_menu()
    {
        var rig = new Rig(localOnTeam: false);

        rig.Menu.Opened();                                    // the show that raised the menu
        rig.Menu.Choose(1);
        Assert.Equal(["menuopen", "joinclass scout"], rig.Sent.Lines);   // the choice; the hide's word is the close's
        Assert.Equal(["tf2classmenu"], rig.Nav.Closed);       // and the menu closes with it

        rig.Menu.Closed();                                    // the host's word for the hide
        Assert.Equal(["menuopen", "joinclass scout", "menuclosed"], rig.Sent.Lines);

        rig.Menu.Choose(9);
        Assert.Contains("joinclass engineer", rig.Sent.Lines);
    }

    /// <summary>The show tells the server the menu is up: stock's
    /// <c>SetVisible(true)</c> arm (tf_classmenu.cpp:1180-1182), the
    /// <c>m_bIsClassMenuOpen</c> the respawn wave holds on.</summary>
    [Fact]
    public void The_menu_tells_the_server_when_it_opens()
    {
        var rig = new Rig(localOnTeam: true);
        Assert.Empty(rig.Sent.Lines);                         // before: nothing sent

        rig.Menu.Opened();
        Assert.Equal(["menuopen"], rig.Sent.Lines);           // the show's word
    }

    /// <summary>The hide is told by the host's close, whichever way it
    /// came (stock's SetVisible(false), tf_classmenu.cpp:1195-1199): the
    /// closing click that sends no choice still ends in the
    /// <c>menuclosed</c>.</summary>
    [Fact]
    public void Cancelling_closes_and_the_close_tells_the_server()
    {
        var rig = new Rig(localOnTeam: true);

        rig.Menu.Opened();                                    // the show that raised the menu
        rig.Menu.Cancel();
        Assert.Equal(["menuopen"], rig.Sent.Lines);           // the close itself sends nothing yet
        Assert.Equal(["tf2classmenu"], rig.Nav.Closed);

        rig.Menu.Closed();
        Assert.Equal(["menuopen", "menuclosed"], rig.Sent.Lines);
    }

    /// <summary>The close is stock's once per hide (:1199): a second
    /// close with no show between adds no word, and a close with no show
    /// at all says nothing — the flag the show raises.</summary>
    [Fact]
    public void The_close_is_told_once_per_show()
    {
        var neverShown = new Rig(localOnTeam: true);
        neverShown.Menu.Closed();
        Assert.Empty(neverShown.Sent.Lines);                  // no show, no word

        var rig = new Rig(localOnTeam: true);
        rig.Menu.Opened();
        rig.Menu.Closed();
        rig.Menu.Closed();
        Assert.Equal(["menuopen", "menuclosed"], rig.Sent.Lines);   // once
    }

    /// <summary>The random arm: the menu names <c>joinclass random</c> and
    /// the server picks the class.</summary>
    [Fact]
    public void A_random_choice_sends_joinclass_random_and_closes_the_menu()
    {
        var rig = new Rig(localOnTeam: false);

        rig.Menu.ChooseRandom();
        Assert.Equal(["joinclass random"], rig.Sent.Lines);
        Assert.Equal(["tf2classmenu"], rig.Nav.Closed);
    }

    /// <summary>Stock's <c>Go</c> asks <c>CanPlayerChooseClass</c> first
    /// (:1216-1275): a class at its limit sends nothing and the menu stays
    /// open — and an open class still sends (the stimulus).</summary>
    [Fact]
    public void A_full_class_sends_nothing_and_keeps_the_menu_open()
    {
        var rig = new Rig();
        rig.ConVars.Limit.NotifyChanged("1", "0");            // one of each, by tf_classlimit
        Assert.True(rig.Menu.Choice(1).LimitHit);             // two teammates already scout

        rig.Menu.Choose(1);                                   // the full class
        Assert.Empty(rig.Sent.Lines);                         // the CanPlayerChooseClass arm
        Assert.Empty(rig.Nav.Closed);                         // and the menu is still up

        rig.Menu.Choose(3);                                   // and the stimulus: an open class sends
        Assert.Equal(["joinclass soldier"], rig.Sent.Lines);
    }

    /// <summary><c>mp_highlander</c> is stock's one of each, whatever
    /// <c>tf_classlimit</c> says.</summary>
    [Fact]
    public void Highlander_makes_the_limit_one_of_each()
    {
        var rig = new Rig(localOnTeam: true);
        rig.ConVars.Highlander.NotifyChanged("1", "0");       // tf_classlimit stays 0
        Assert.True(rig.Menu.Choice(1).LimitHit);             // the teammates fill the scout class

        rig.Menu.Choose(1);
        Assert.Empty(rig.Sent.Lines);

        rig.ConVars.Highlander.NotifyChanged("0", "1");       // and off again: the class opens
        Assert.False(rig.Menu.Choice(1).LimitHit);
        rig.Menu.Choose(1);
        Assert.Equal(["joinclass scout"], rig.Sent.Lines);
    }

    /// <summary>The <c>changeclass</c> gate (tf_viewport.cpp:193-211): the
    /// panel raises for a player on a game team and for nobody else — and
    /// raises it by that team's own name, as stock's switch shows the
    /// panel (tf_viewport.cpp:148-213's PANEL_CLASS_RED/PANEL_CLASS_BLUE).</summary>
    [Fact]
    public void Changeclass_raises_the_named_panel_only_for_a_player_on_a_game_team()
    {
        var onTeam = new Rig(localOnTeam: true);              // RED, with a class
        var command = new ChangeClass(onTeam.Menu, onTeam.Nav);
        Assert.Empty(onTeam.Nav.OpenedPanels);                // before: unopened
        command.Execute();
        Assert.Equal(["class_red"], onTeam.Nav.OpenedPanels); // after: the team's own name
        Assert.Empty(onTeam.Nav.Opened);                      // raised by name, not by screen

        var spectator = new Rig(localOnTeam: true);           // the same rows...
        spectator.SetTeam(2, 1);                              // ...but a spectator now
        spectator.SetClass(2, 0);
        var scmd = new ChangeClass(spectator.Menu, spectator.Nav);
        scmd.Execute();
        Assert.Empty(spectator.Nav.OpenedPanels);             // and nobody else's
    }

    /// <summary>Edit Loadout is stock's openloadout
    /// (tf_classmenu.cpp:1307-1320): the loadout screen rises on the class
    /// the menu is on and the ask goes with it — and with no class neither
    /// happens (stock's OnTick arm, :1138-1142).</summary>
    [Fact]
    public void Edit_loadout_opens_the_loadout_and_asks_for_the_class()
    {
        var rig = new Rig(localOnTeam: true);
        Assert.Empty(rig.Nav.Opened);                        // before: nothing raised
        Assert.Empty(rig.Loadout.Selects);

        rig.Menu.EditLoadout();
        Assert.Equal(["tf2loadout"], rig.Nav.Opened);        // the loadout screen rises
        Assert.Equal([3], rig.Loadout.Selects);              // the soldier's reply asked

        var none = new Rig(localOnTeam: false);
        none.Menu.EditLoadout();
        Assert.Empty(none.Nav.Opened);                       // no class: nothing raises
        Assert.Empty(none.Loadout.Selects);
    }

    /// <summary>Reset clears the class's every loadout slot — the
    /// <c>tf2_equip &lt;class&gt; all none</c> line (stock's resetclass
    /// arm is highlander-only, tf_classmenu.cpp:1300-1306;
    /// docs/divergences.md) — and a classless menu sends nothing.</summary>
    [Fact]
    public void Reset_sends_the_all_none_equip()
    {
        var rig = new Rig(localOnTeam: true);
        Assert.Empty(rig.Sent.Lines);                        // before

        rig.Menu.ResetLoadout();
        Assert.Equal(["tf2_equip soldier all none"], rig.Sent.Lines);

        var none = new Rig(localOnTeam: false);
        none.Menu.ResetLoadout();
        Assert.Empty(none.Sent.Lines);                       // no class: nothing to reset
    }

    // ---- the rig ----

    private sealed record ConVars(ClassLimitConVar Limit, HighlanderConVar Highlander);

    /// <summary>One test's world: the menu over the two player resources
    /// (arrived or not), the convars at rest (no limit), the recorded
    /// sends and the recorded opens and closes. The local player is
    /// slot 2, the soldier, on RED (slot 1 is nobody); the teammates are
    /// two scouts (3, 4) and a medic (5).</summary>
    private sealed class Rig
    {
        public readonly Engine Engine = new();
        public readonly EngineClient Sent = new();
        public readonly Recorder Nav = new();
        public readonly LoadoutRecorder Loadout = new();
        public readonly ConVars ConVars = new(new ClassLimitConVar(), new HighlanderConVar());
        public readonly Menu Menu;

        private readonly Standalones _st = new();
        private readonly int _localTeam;

        public Rig(bool localOnTeam = true, bool arrived = true)
        {
            _localTeam = localOnTeam ? 2 : 0;
            Engine.Returns("Hud.LocalPlayerIndex", localOnTeam ? 2 : 0);
            Menu = new Menu(_st, Engine, Sent, ConVars.Limit, ConVars.Highlander, Nav, Loadout);
            if (arrived)
                Arrive();
        }

        /// <summary>The two player resources arrive: the local player at
        /// slot 2 on their team as the soldier, with two scout teammates
        /// (3, 4) and a medic (5), all on RED.</summary>
        public void Arrive()
        {
            var core = new Standalone.ClientPlayerResource(new Engine());
            core.State.BindLocal();
            Connect(core, 2, _localTeam);
            Connect(core, 3, 2);
            Connect(core, 4, 2);
            Connect(core, 5, 2);
            _st.All.Add(core);

            var tf = new TFResource(new Engine(), new TFResources());
            tf.State.BindLocal();
            tf.State.PlayerClass[2] = (byte)TFClass.Soldier;
            tf.State.PlayerClass[3] = (byte)TFClass.Scout;
            tf.State.PlayerClass[4] = (byte)TFClass.Scout;
            tf.State.PlayerClass[5] = (byte)TFClass.Medic;
            _st.All.Add(tf);
        }

        public void SetClass(int slot, byte cls) =>
            _st.All.OfType<TFResource>().Single().State.PlayerClass[slot] = cls;

        public void SetTeam(int slot, int team) =>
            _st.All.OfType<Standalone.ClientPlayerResource>().Single().State.Team[slot] = (byte)team;

        private void Connect(Standalone.ClientPlayerResource resource, int slot, int team)
        {
            resource.State.Flags[slot] = ClientShared.PlayerResourceNetState.FlagConnected;
            resource.State.Team[slot] = (byte)team;
        }
    }

    private sealed class Standalones : ClientSide.IClientStandalones
    {
        public readonly List<Standalone.ClientStandaloneEntity> All = [];

        public IReadOnlyList<Standalone.ClientStandaloneEntity> Live() => All;

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    /// <summary>The menu's advanced-options gear opens the core's options
    /// window on its multiplayer page (the page that carries
    /// cfg/user_default.scr's entries).</summary>
    [Fact]
    public void Opentf2options_opens_the_multiplayer_options_page()
    {
        var nav = new Recorder();
        new client::TF2.Client.Ui.TFAdvancedOptionsCommand(nav).Execute();
        Assert.Equal(["multiplayeroptions"], nav.Opened);
    }

    private sealed class Recorder : Nav
    {
        public readonly List<string> Opened = [], OpenedPanels = [], Closed = [];

        public bool Has(string screen) => true;

        public void Open(string screen) => Opened.Add(screen);

        public void OpenPanel(string panel) => OpenedPanels.Add(panel);

        public void Close(string screen) => Closed.Add(screen);
    }

    /// <summary>The loadout half the menu's Edit Loadout asks through:
    /// records the class asks, drops the equips.</summary>
    private sealed class LoadoutRecorder : global::TF2.Client.Ui.ITFLoadoutActions
    {
        public readonly List<int> Selects = [];

        public void Select(int classNumber) => Selects.Add(classNumber);

        public void Equip(int classNumber, int slot, int defindex) { }

        public void Close() { }
    }

    /// <summary>The engine client recording every line that goes out
    /// (the team menu rig's double).</summary>
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
        public bool TryLightForPoint(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
    }
}
