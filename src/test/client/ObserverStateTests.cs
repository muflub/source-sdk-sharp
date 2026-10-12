using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Tests.Ui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The spectator's view as stock's CSpectatorGUI drew it (SpectatorGUI.cpp):
/// the mode and map while observing, the watched player only in in-eye and
/// chase, and the spec_* commands forwarded as ForwardSpecCmdToServer sent them.
/// </summary>
public class ObserverStateTests
{
    private sealed class Fields : IObserverFields
    {
        public int Mode { get; set; }
        public uint Target { get; set; } = EntityHandle.None.Value;
    }

    private sealed class Players : ISpectatedPlayers
    {
        public bool TryFind(int client, out string name, out int team, out int health)
        {
            (name, team, health) = ($"p{client}", client % 2 == 0 ? 2 : 3, client * 10);
            return client != 0;          // any entry but the world: the handle's own "none" must be refused first
        }
    }

    private static (ObserverState State, Fields Fields, List<string> Reads) Make(string level = "maps/dm_lockdown.bsp")
    {
        var fields = new Fields();
        var reads = new List<string>();
        return (new ObserverState(fields, new Players(), () => { reads.Add(level); return level; }), fields, reads);
    }

    // Serial 5 in the bits above the entry: the entry index is what names the player.
    private static uint Handle(int entry) => (5u << 16) | (uint)entry;

    // ---- the local player's networked fields (PlayerNetState, written by the server's Observers) ----

    private sealed class Standalones : IClientStandalones
    {
        public readonly Dictionary<int, Client.Standalone.ClientStandaloneEntity> ByIndex = new();

        public IReadOnlyList<Client.Standalone.ClientStandaloneEntity> Live() => ByIndex.Values.ToList();

        public Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => ByIndex.GetValueOrDefault(entityIndex);
    }

    private static Client.Standalone.ClientSsPlayer Player(int entry, int mode, uint target)
    {
        var p = new Client.Standalone.ClientSsPlayer(new RecordingEngine()) { Handle = new EntityHandle(Handle(entry)) };
        p.State.BindLocal();
        p.State.ObserverMode = mode;
        p.State.ObserverTarget = target;
        return p;
    }

    [Fact]
    public void The_local_players_networked_chase_names_its_target()
    {
        var standalones = new Standalones();
        standalones.ByIndex[1] = Player(1, (int)ObserverMode.Chase, Handle(3));
        var o = new ObserverState(new LocalObserverFields(standalones, () => 1), new Players(), () => "maps/dm_lockdown.bsp");

        Assert.Equal(SpectatorMode.Chase, o.Mode);
        Assert.True(o.HasTarget);
        Assert.Equal(("p3", 3), (o.Name, o.Team));
        Assert.Equal("dm_lockdown", o.MapName);
    }

    [Fact]
    public void Another_players_observer_fields_are_not_the_local_players()
    {
        var standalones = new Standalones();
        standalones.ByIndex[1] = Player(1, (int)ObserverMode.None, EntityHandle.None.Value);
        standalones.ByIndex[2] = Player(2, (int)ObserverMode.InEye, Handle(3));
        var fields = new LocalObserverFields(standalones, () => 1);
        Assert.Equal(0, fields.Mode);
        Assert.Equal(EntityHandle.None.Value, fields.Target);

        // Control: the same registry read as player 2 is observing.
        var two = new LocalObserverFields(standalones, () => 2);
        Assert.Equal((int)ObserverMode.InEye, two.Mode);
        Assert.Equal(Handle(3), two.Target);
    }

    [Fact]
    public void No_local_player_reads_not_observing()
    {
        var fields = new LocalObserverFields(new Standalones(), () => 1);
        Assert.Equal(0, fields.Mode);
        Assert.Equal(EntityHandle.None.Value, fields.Target);
    }

    [Fact]
    public void The_spectator_modes_are_the_servers_observer_modes_by_value()
    {
        foreach (var m in Enum.GetValues<ObserverMode>())
            Assert.Equal(m.ToString(), ((SpectatorMode)(int)m).ToString());
        Assert.Equal(Enum.GetValues<ObserverMode>().Length, Enum.GetValues<SpectatorMode>().Length);
    }

    [Fact]
    public void The_default_fields_read_not_observing_and_no_target()
    {
        var o = new ObserverState(new NoObserverFields(), new Players(), () => "maps/x.bsp");
        Assert.Equal(SpectatorMode.None, o.Mode);
        Assert.False(o.HasTarget);
    }

    [Fact]
    public void Chase_names_the_target_by_its_handles_entry_index()
    {
        var (o, f, _) = Make();
        (f.Mode, f.Target) = ((int)SpectatorMode.Chase, Handle(3));
        Assert.True(o.HasTarget);
        Assert.Equal(("p3", 3, 30, 100), (o.Name, o.Team, o.Health, o.MaxHealth));
    }

    [Fact]
    public void Only_in_eye_and_chase_show_a_target()
    {
        var (o, f, _) = Make();
        f.Target = Handle(2);
        var shown = Enum.GetValues<SpectatorMode>().Where(m => { f.Mode = (int)m; return o.HasTarget; });
        Assert.Equal([SpectatorMode.InEye, SpectatorMode.Chase], shown);
    }

    [Fact]
    public void The_invalid_handle_is_no_target_and_zero_is_the_world_not_a_player()
    {
        var (o, f, _) = Make();
        f.Mode = (int)SpectatorMode.InEye;
        f.Target = EntityHandle.None.Value;
        Assert.False(o.HasTarget);
        f.Target = 0;
        Assert.False(o.HasTarget);
        Assert.Equal(string.Empty, o.Name);
    }

    [Fact]
    public void The_map_is_read_on_starting_to_observe_without_folder_or_extension_and_not_per_frame()
    {
        var (o, f, reads) = Make();
        _ = o.Mode;
        Assert.Empty(reads);
        f.Mode = (int)SpectatorMode.Roaming;
        for (int i = 0; i < 5; i++)
            _ = o.Mode;
        Assert.Equal(("dm_lockdown", 1), (o.MapName, reads.Count));
        f.Mode = 0;
        _ = o.Mode;
        f.Mode = (int)SpectatorMode.Chase;
        _ = o.Mode;
        Assert.Equal(2, reads.Count);
    }

    [Fact]
    public void A_mode_out_of_range_reads_as_not_observing()
    {
        var (o, f, _) = Make();
        f.Mode = 8;
        Assert.Equal(SpectatorMode.None, o.Mode);
        f.Mode = -1;
        Assert.Equal(SpectatorMode.None, o.Mode);
    }

    [Fact]
    public void Spec_commands_forward_bare_or_with_one_quoted_argument()
    {
        var c = new SpectatorCommands();
        Assert.Equal("spec_next", c.Line(["spec_next"], SpectatorMode.Chase, false, out _));
        Assert.Equal("spec_mode \"5\"", c.Line(["spec_mode", "5"], SpectatorMode.Chase, false, out _));
        Assert.Equal("spec_player \"alyx\"", c.Line(["spec_player", "alyx"], SpectatorMode.Roaming, false, out _));
        Assert.Null(c.Line(["spec_prev", "a", "b"], SpectatorMode.Chase, false, out _));
    }

    [Fact]
    public void Spec_commands_send_nothing_when_not_observing_or_playing_a_demo()
    {
        var c = new SpectatorCommands();
        Assert.Null(c.Line(["spec_next"], SpectatorMode.None, false, out bool usage));
        Assert.False(usage);
        Assert.Null(c.Line(["spec_next"], SpectatorMode.Chase, true, out _));
    }

    [Fact]
    public void Spec_player_without_exactly_one_argument_prints_its_usage()
    {
        var c = new SpectatorCommands();
        Assert.Null(c.Line(["spec_player"], SpectatorMode.Chase, false, out bool usage));
        Assert.True(usage);
        Assert.Null(c.Line(["spec_player"], SpectatorMode.None, false, out usage));
        Assert.False(usage);
    }

    private sealed record Observer(SpectatorMode Mode, string MapName) : IObserverSource;

    private static (SpectatorCommandRunner Run, List<string> Sent, RecordingLog Log) Runner(SpectatorMode mode)
    {
        var sent = new List<string>();
        var log = new RecordingLog();
        return (new SpectatorCommandRunner(new SpectatorCommands(), new Observer(mode, ""), log,
                                           () => new ChatFeedTests.RecordingEngineClient(sent)), sent, log);
    }

    [Fact]
    public void The_spec_commands_send_the_server_their_line_with_a_quoted_argument()
    {
        var (run, sent, _) = Runner(SpectatorMode.Chase);
        run.Run("spec_next", "");
        run.Run("spec_player", "\"Dr. Kleiner\"");
        Assert.Equal(["server: spec_next", "server: spec_player \"Dr. Kleiner\""], sent);
    }

    [Fact]
    public void The_spec_commands_send_nothing_for_a_player_and_spec_player_wants_one_argument()
    {
        var (run, sent, log) = Runner(SpectatorMode.None);
        run.Run("spec_next", "");
        Assert.Empty(sent);
        (run, sent, log) = Runner(SpectatorMode.Roaming);
        run.Run("spec_player", "");
        Assert.Empty(sent);
        Assert.Equal([SpectatorCommands.PlayerUsage], log.Lines.Select(l => l.Text));
    }

    private static UiContext Draw(IObserverSource o)
    {
        var screen = new SpecGui(o);
        return UiTestKit.Frame(UiTestKit.Context(), UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui),
                               layer: UiLayerKind.Hud);
    }

    private static int Index(UiContext ctx, string name)
        => Array.FindIndex(UiTestKit.Elements(ctx), e => UiTestKit.NameOf(ctx, e) == name);

    // The screen's own root is the one element a screen always has.
    [Fact]
    public void The_bar_draws_nothing_while_playing()
    {
        Assert.Single(UiTestKit.Elements(Draw(new Observer(SpectatorMode.None, "dm_lockdown"))));
        Assert.True(UiTestKit.Elements(Draw(new Observer(SpectatorMode.Roaming, "dm_lockdown"))).Length > 1);
    }

    [Fact]
    public void The_bar_names_the_mode_and_the_map_while_observing()
    {
        var ctx = Draw(new Observer(SpectatorMode.Chase, "dm_lockdown"));
        Assert.Equal("#SS_UI_SPEC_MODE_CHASE", UiTestKit.RunText(ctx, Index(ctx, "mode")).ToUpperInvariant());
        Assert.Equal("dm_lockdown", UiTestKit.RunText(ctx, Index(ctx, "map")));
    }

    [Fact]
    public void Every_mode_has_its_own_token()
        => Assert.Equal(Enum.GetValues<SpectatorMode>().Length,
                        Enum.GetValues<SpectatorMode>().Select(SpecGui.ModeToken).Distinct().Count());

    [Fact]
    public void The_modes_agree_with_the_abis_observer_modes_by_value()
    {
        foreach (var m in Enum.GetValues<ObserverMode>())
            Assert.Equal((int)m, (int)Enum.Parse<SpectatorMode>(m.ToString()));
    }
}
