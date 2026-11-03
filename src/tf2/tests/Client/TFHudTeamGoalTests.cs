extern alias client;

using SourceSharp.Abi;
using Xunit;

using TF2.Shared.Rules;

using ClientEntities = client::TF2.Client.Entities;
using ClientPlayer = client::TF2.Client.Player;
using ClientUi = client::TF2.Client.Ui;

using Engine = client::SourceSharp.Abi.RecordingEngine;
using ClientSources = client::SourceSharp.Client;
using ClientStandalone = client::SourceSharp.Client.Standalone;

namespace TF2.Tests.Client;

/// <summary>
/// The goal hint's show rule, stock's <c>CHudTeamGoal::ShouldDraw</c>
/// (tf_hud_teamgoal.cpp:102-158) over <c>ShouldShowTeamGoal</c>
/// (tf_gamerules.cpp:18553): the round's could-see states, the alive local
/// on a game team, the team's goal string, and the 15-second show that
/// re-arms only once the round leaves the could-see states.
/// </summary>
public sealed class TFHudTeamGoalTests
{
    private const int Red = 2;
    private const int Blue = 3;

    /// <summary>The local player's slot.</summary>
    private const int Local = 3;

    private const string Line = "Grab the Intelligence Briefcase from the enemy basement. Return to your basement to win!";

    private sealed class Clock : client::SourceSharp.Abi.IGameClock
    {
        public float Now { get; set; } = 100f;
    }

    private sealed class Box : ClientSources.IClientStandalones
    {
        public readonly List<ClientStandalone.ClientStandaloneEntity> All = [];

        public IReadOnlyList<ClientStandalone.ClientStandaloneEntity> Live() => All;

        public ClientStandalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    /// <summary>The game's token chain, answering the stock line.</summary>
    private sealed class Tokens(string line) : client::SourceSharp.Client.Ui.IUiLocalisation
    {
        public string? Find(string token) => token.Length > 0 && token[0] == '#' ? line : null;
    }

    private sealed class Rig
    {
        public readonly Engine Engine = Engine.Default();
        public readonly Clock Clock = new();
        public readonly ClientPlayer.TFClientPlayerResources Resources = new();
        public readonly ClientPlayer.ClientTFPlayerResource Tf;
        public readonly ClientStandalone.ClientPlayerResource Core;
        public readonly ClientEntities.ClientTFTeamResource TeamRow;
        public readonly ClientEntities.TFClientTeamResources Teams = new();
        public readonly Box St = new();
        public readonly ClientUi.TFHudTeamGoal Hud;

        public Rig()
        {
            Engine.Returns("Hud.LocalPlayerIndex", Local);

            Tf = new ClientPlayer.ClientTFPlayerResource(Engine, Resources);
            Tf.State.BindLocal();
            Tf.State.Team[Local] = Red;
            Tf.State.RoundState = (byte)RoundState.RoundRunning;
            Resources.Live = Tf;

            Core = new ClientStandalone.ClientPlayerResource(new Engine());
            Core.State.BindLocal();
            Core.State.SetRow(Local, 0, 0, 100, 7, Red, connected: true, alive: true, valid: true);
            St.All.Add(Core);

            TeamRow = new ClientEntities.ClientTFTeamResource(Engine, Teams);
            TeamRow.State.BindLocal();
            TeamRow.State.Role[Red] = 1;    // TEAM_ROLE_DEFENDERS, 2fort's red
            TeamRow.State.Role[Blue] = 2;   // TEAM_ROLE_ATTACKERS
            TeamRow.State.GoalStringRed = "#2fort_red_setup_goal";
            Teams.Live = TeamRow;

            Hud = new ClientUi.TFHudTeamGoal(Resources, St, Engine, Clock, Teams, new Tokens(Line));
        }

        /// <summary>The local player's life.</summary>
        public bool Alive
        {
            set => Core.State.SetRow(Local, 0, 0, 100, 7, Red, connected: true, alive: value, valid: true);
        }

        /// <summary>The round state word.</summary>
        public RoundState State
        {
            set => Tf.State.RoundState = (byte)value;
        }

        /// <summary>Stock's m_bInSetup word.</summary>
        public bool Setup
        {
            set => Tf.State.InSetup = value ? (byte)1 : default;
        }

        public bool Shows => Hud.Visible;
    }

    [Fact]
    public void The_goal_shows_on_the_first_could_see_frame()
    {
        var r = new Rig();

        Assert.True(r.Shows);
        Assert.Equal(Red, r.Hud.Team);
        Assert.Equal("hud/hud_icon_defend", r.Hud.Image);   // the red team's role, defence
        Assert.Equal(Line, r.Hud.Text);                     // the token, localised
    }

    [Fact]
    public void The_show_last_fifteen_seconds_of_could_see()
    {
        var r = new Rig();
        Assert.True(r.Shows);

        r.Clock.Now += 14.9f;
        Assert.True(r.Shows);

        r.Clock.Now += 0.2f;
        Assert.False(r.Shows);
    }

    [Fact]
    public void The_show_rearms_once_the_round_leaves_the_could_see()
    {
        var r = new Rig();
        Assert.True(r.Shows);

        r.Clock.Now += 16f;
        Assert.False(r.Shows);                  // expired, round still running

        r.State = RoundState.TeamWin;
        Assert.False(r.Shows);                  // the expiry clears on the leave

        r.State = RoundState.RoundRunning;
        r.Clock.Now += 30f;
        Assert.True(r.Shows);                   // a fresh show

        r.Clock.Now += 15.1f;
        Assert.False(r.Shows);
    }

    [Fact]
    public void A_dead_local_or_an_empty_goal_hides_it()
    {
        var dead = new Rig();
        dead.Alive = false;
        Assert.False(dead.Shows);

        var silent = new Rig();
        silent.TeamRow.State.GoalStringRed = "";
        Assert.False(silent.Shows);
    }

    [Fact]
    public void The_could_see_states_and_the_setup()
    {
        var preround = new Rig { State = RoundState.Preround };
        Assert.True(preround.Shows);

        var setup = new Rig { State = RoundState.TeamWin, Setup = true };
        Assert.True(setup.Shows);

        var stalemate = new Rig { State = RoundState.Stalemate };
        Assert.False(stalemate.Shows);
    }

    [Fact]
    public void Each_team_reads_its_own_string_and_role()
    {
        var blue = new Rig();
        blue.Tf.State.Team[Local] = Blue;
        blue.TeamRow.State.GoalStringBlue = "#2fort_blue_setup_goal";

        Assert.True(blue.Shows);
        Assert.Equal(Blue, blue.Hud.Team);
        Assert.Equal("hud/hud_icon_attack", blue.Hud.Image);    // the blue role, attack
    }
}
