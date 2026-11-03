using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The welcome menu's close (<c>closedwelcomemenu</c>, <c>show_motd</c>):
/// <c>mp_forceautoteam</c>'s auto team and the panels around it
/// (tf_player.cpp:7370-7407, :7767-7796).
/// </summary>
public sealed class TFWelcomeMenuTests
{
    private static readonly EntityHandle Player = new(0x0001_0003u);

    private sealed class FixedState(RoundState state) : ITeamRoundState
    {
        public RoundState State => state;
        public bool InWaitingForPlayers => false;
        public bool InSetup => false;
        public float MapResetTime => 0f;
        public float StalemateStartTime => -1f;
        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    /// <summary>The core's roster as the commands see it: the join asked for, the answer given.</summary>
    private sealed class JoinBoard : ITeams
    {
        public int CurrentTeam;
        public JoinTeamResult Result = JoinTeamResult.Playing;
        public readonly List<int> Joined = new();

        public int Team(EntityHandle player) => CurrentTeam;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;

        public JoinTeamResult Join(EntityHandle player, int requested)
        {
            Joined.Add(requested);
            return Result;
        }

        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    /// <summary>The panels the commands sent, decoded from the VGUIMenu payloads.</summary>
    private sealed class Panels : IUserMessageSender
    {
        public readonly List<(string Key, byte[] Payload, int Player, bool Reliable)> Sent = new();

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
            => Sent.Add((key, payload.ToArray(), player, reliable));

        public (string Name, bool Show)[] Menus()
        {
            var menus = new List<(string, bool)>();
            foreach (var (key, payload, _, _) in Sent.Where(s => s.Key == "VGUIMenu"))
            {
                var reader = new UserMessageReader(payload);
                var menu = VguiMenu.Read(ref reader);
                menus.Add((menu.Name, menu.Show));
            }
            return menus.ToArray();
        }
    }

    /// <summary>The command over a board, a policy whose emptier side is Blue, and the panel sink.</summary>
    private static (TFWelcomeMenuCommands Command, JoinBoard Board, Panels Panels) Rig(
        bool force, int boardTeam = (int)TFTeam.Unassigned, TFPlayerCarriers? carriers = null)
    {
        var rig = new RoundRig();
        for (int i = 0; i < 2; i++)
            rig.Roster.Add(TFTeam.Red);
        rig.Roster.Add(TFTeam.Blue);
        rig.Settings.ForceAutoTeam = force;

        var board = new JoinBoard { CurrentTeam = boardTeam };
        var panels = new Panels();
        var policy = new TFTeams(rig.Balance, rig.Roster, rig.Slot, new FixedState(RoundState.RoundRunning), new RandomSeeds(7), rig.Settings);
        return (new TFWelcomeMenuCommands(board, policy, rig.Settings, carriers ?? new TFPlayerCarriers(), panels), board, panels);
    }

    [Fact]
    public void A_teamless_close_under_the_force_autoteams_to_the_class_panel_of_the_team_landed_on()
    {
        var (command, board, panels) = Rig(force: true);

        Assert.True(command.Handle(Player, "closedwelcomemenu", []));

        Assert.Equal(new[] { (int)TFTeam.Blue }, board.Joined);
        Assert.Equal(new[] { ("class_blue", true) }, panels.Menus());
    }

    [Fact]
    public void A_teamless_close_without_the_force_shows_the_team_panel_and_joins_nobody()
    {
        var (command, board, panels) = Rig(force: false);

        Assert.True(command.Handle(Player, "closedwelcomemenu", []));

        Assert.Empty(board.Joined);
        Assert.Equal(new[] { ("team", true) }, panels.Menus());
    }

    [Fact]
    public void A_close_on_a_team_with_no_class_shows_that_teams_class_panel_again()
    {
        var (command, board, panels) = Rig(force: false, boardTeam: (int)TFTeam.Red);

        Assert.True(command.Handle(Player, "closedwelcomemenu", []));

        Assert.Empty(board.Joined);
        Assert.Equal(new[] { ("class_red", true) }, panels.Menus());
    }

    [Fact]
    public void A_close_on_a_team_already_in_a_class_shows_nothing()
    {
        var players = new PlayerRig().As(TFClass.Soldier);
        var (command, board, panels) = Rig(force: false, boardTeam: (int)TFTeam.Red, players.Carriers);

        Assert.True(command.Handle(PlayerRig.Player, "closedwelcomemenu", []));

        Assert.Empty(board.Joined);
        Assert.Empty(panels.Menus());
    }

    [Fact]
    public void A_motd_close_under_the_force_autoteams_even_off_a_team_already()
    {
        var (command, board, panels) = Rig(force: true, boardTeam: (int)TFTeam.Red);

        Assert.True(command.Handle(Player, "show_motd", []));

        Assert.Equal(new[] { (int)TFTeam.Blue }, board.Joined);
        Assert.Equal(new[] { ("class_blue", true) }, panels.Menus());
    }

    [Fact]
    public void A_motd_close_without_the_force_hides_the_team_panel()
    {
        var (command, board, panels) = Rig(force: false);

        Assert.True(command.Handle(Player, "show_motd", []));

        Assert.Empty(board.Joined);
        Assert.Equal(new[] { ("team", false) }, panels.Menus());
    }

    [Fact]
    public void A_word_the_welcome_menu_does_not_own_is_left_to_the_later_hooks()
    {
        var (command, _, _) = Rig(force: false);

        Assert.False(command.Handle(Player, "jointeam", []));
    }
}
