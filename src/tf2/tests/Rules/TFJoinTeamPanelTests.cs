using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The viewport panels <c>CTFPlayer::HandleCommand_JoinTeam</c> raises around
/// the join (tf_player.cpp:6407-6455): the class panel for the team just
/// joined (:6450-6455), the team panel again when an unbalanced join is
/// refused (:6436-6440), and nothing for a spectator (:6354-6411), a same-team
/// join (the early return at :6414-6417) or a join after game over (:6226).
/// </summary>
public sealed class TFJoinTeamPanelTests
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

    /// <summary>The core's roster as the command sees it: the join asked for, the answer given.</summary>
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
            // The core's spectator arm answers the observers', not Playing.
            return requested == (int)TFTeam.Spectator ? JoinTeamResult.Spectating : Result;
        }

        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    /// <summary>The panels the command sent, decoded from the VGUIMenu payloads.</summary>
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

    private static TFTeams Policy(RoundRig rig, RoundState state = RoundState.RoundRunning)
        => new(rig.Balance, rig.Roster, rig.Slot, new FixedState(state), new RandomSeeds(7), rig.Settings);

    private static void Fill(RoundRig rig, int red, int blue)
    {
        for (int i = 0; i < red; i++)
            rig.Roster.Add(TFTeam.Red);
        for (int i = 0; i < blue; i++)
            rig.Roster.Add(TFTeam.Blue);
    }

    private static (TFJoinTeamCommand Command, JoinBoard Board, Panels Panels) Rig(
        RoundState state = RoundState.RoundRunning)
    {
        var rig = new RoundRig();
        var board = new JoinBoard();
        var panels = new Panels();
        return (new TFJoinTeamCommand(board, Policy(rig, state), panels), board, panels);
    }

    [Fact]
    public void Joining_red_shows_the_class_panel_for_red()
    {
        var (command, _, panels) = Rig();

        Assert.True(command.Handle(Player, "jointeam", ["red"]));

        Assert.Equal(new[] { ("class_red", true) }, panels.Menus());
    }

    [Fact]
    public void Joining_blue_shows_the_class_panel_for_blue()
    {
        var (command, _, panels) = Rig();

        Assert.True(command.Handle(Player, "jointeam", ["blue"]));

        Assert.Equal(new[] { ("class_blue", true) }, panels.Menus());
    }

    [Fact]
    public void An_auto_join_shows_the_class_panel_of_the_team_it_picked()
    {
        // The policy answers auto from its own roster; the board only records
        // the join, so fill the policy's side: red the bigger team.
        var rig = new RoundRig();
        Fill(rig, red: 2, blue: 1);
        var board = new JoinBoard();
        var sender = new Panels();
        var joined = new TFJoinTeamCommand(board, Policy(rig), sender);

        Assert.True(joined.Handle(Player, "jointeam", ["auto"]));

        Assert.Equal(new[] { (int)TFTeam.Blue }, board.Joined);
        Assert.Equal(new[] { ("class_blue", true) }, sender.Menus());
    }

    [Fact]
    public void Spectating_shows_no_panel()
    {
        var (command, board, panels) = Rig();

        Assert.True(command.Handle(Player, "jointeam", ["spectate"]));

        Assert.Equal(new[] { (int)TFTeam.Spectator }, board.Joined);
        Assert.Empty(panels.Menus());
    }

    [Fact]
    public void Rejoining_your_own_team_moves_and_shows_nothing()
    {
        // Stock's early return: no ChangeTeam, no panel (:6414-6417).
        var (command, board, panels) = Rig();
        board.CurrentTeam = (int)TFTeam.Red;

        Assert.True(command.Handle(Player, "jointeam", ["red"]));

        Assert.Empty(board.Joined);
        Assert.Empty(panels.Menus());
    }

    [Fact]
    public void A_refused_join_shows_the_team_panel_again()
    {
        // Stock's unbalance arm: the team menu is re-shown (:6436-6440).
        var rig = new RoundRig();
        Fill(rig, red: 2, blue: 1);
        var joiner = rig.Roster.Add(TFTeam.Unassigned);
        var board = new JoinBoard { Result = JoinTeamResult.Refused };
        var panels = new Panels();
        var command = new TFJoinTeamCommand(board, Policy(rig), panels);

        Assert.True(command.Handle(joiner, "jointeam", ["red"]));

        Assert.Equal(new[] { ("team", true) }, panels.Menus());
    }

    [Fact]
    public void A_join_after_the_game_is_over_shows_no_panel()
    {
        var (command, board, panels) = Rig(RoundState.GameOver);
        board.Result = JoinTeamResult.Refused;

        Assert.True(command.Handle(Player, "jointeam", ["red"]));

        Assert.Empty(panels.Menus());
    }

    [Fact]
    public void The_panel_goes_reliable_to_the_player_alone()
    {
        var rig = new RoundRig();
        var board = new JoinBoard();
        var panels = new Panels();
        var command = new TFJoinTeamCommand(board, Policy(rig), panels);

        command.Handle(Player, "jointeam", ["red"]);

        var send = Assert.Single(panels.Sent);
        Assert.Equal(Player.EntryIndex, send.Player);
        Assert.True(send.Reliable);
    }
}
