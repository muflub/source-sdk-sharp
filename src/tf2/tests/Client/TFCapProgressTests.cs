extern alias client;

using Xunit;

using Data = client::TF2.Shared.Player.ITFPlayerData;
using EndTouch = client::SourceSharp.Shared.ControlPointEndTouchEvent;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using EventLink = client::TF2.Client.EventLink;
using HurtEvent = client::SourceSharp.Shared.PlayerHurtEvent;
using DeathEvent = client::SourceSharp.Shared.PlayerDeathEvent;
using Local = TF2.Client.Ui.ITFLocalPlayer;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using Progress = client::TF2.Client.Ui.TFCapProgress;
using RoundStateId = client::TF2.Shared.Rules.RoundState;
using StartTouch = client::SourceSharp.Shared.ControlPointStartTouchEvent;
using WinPanel = client::SourceSharp.Shared.TeamplayWinPanelEvent;
using RoundStart = client::SourceSharp.Shared.TeamplayRoundStartEvent;
using Touch = client::SourceSharp.Shared.ControlPointTouchArgs;
using TFCondId = client::TF2.Shared.TFCond;
using TFCondMaskId = client::TF2.Shared.Conditions.TFCondMask;
using TFNetState = client::TF2.Shared.Player.TFPlayerNetState;
using TFNetStateViews = client::TF2.Shared.Player.TFPlayerNetStateViews;
using Limits = client::TF2.Shared.Resources.ObjectiveLimits;
using Team = client::TF2.Shared.TFTeam;

// The touch args and the contract interfaces are unaliased: the record rides
// the contract assembly, and the interfaces are its alone.

namespace TF2.Tests.Client;

/// <summary>
/// The cap teardrop's data (stock's <c>CControlPointProgressBar</c>): the
/// local player's touch events keep the touched point, the bar arm answers
/// only for the local team's unblocked cap, the sweep is
/// <c>GetCPCapPercentage</c>, and the text is <c>UpdateBarText</c>'s ladder
/// with <c>PlayerMayCapturePoint</c>'s player gates (hud_controlpointicons.cpp:874-891,
/// :1499-1646; tf_gamerules.cpp:16769, :16835-16948).
/// </summary>
public sealed class TFCapProgressTests
{
    private const int Blue = (int)Team.Blue;
    private const int Red = (int)Team.Red;

    private sealed class LocalFake : Local
    {
        public int Team { get; set; } = Red;

        public string ClassName => "Soldier";
        public int Health => 100;
        public int MaxHealth => 125;
        public int MaxBuffedHealth => 187;
        public int Overheal => 0;
        public bool ShowsAmmo => true;
        public int Clip => 4;
        public int Reserve => 20;
        public int MaxTotalAmmo => 20;
        public int Metal => 0;
    }

    private sealed class DataFake : Data
    {
        public readonly TFNetState Carrier = new();

        public DataFake() => Carrier.BindLocal();

        public TFNetState? Predicted(int playerIndex) => playerIndex == 2 ? Carrier : null;

        public TFNetState? Networked(int playerIndex) => playerIndex == 2 ? Carrier : null;
    }

    private sealed class SetupFake : client::TF2.Shared.Rules.ITFRoundSetup
    {
        public bool InSetupWithRoundTimer { get; set; }
    }

    private sealed class Rig
    {
        public readonly Engine Engine = new();
        public readonly ObjectiveRig Objectives = new();
        public readonly PlayerResources Resources = new();
        public readonly StartTouch Touches = new();
        public readonly EndTouch Leaves = new();
        public readonly DataFake Data = new();
        public readonly SetupFake Setup = new();
        public readonly Progress Source;

        public Rig()
        {
            Engine.Returns("Hud.LocalPlayerIndex", 2);

            var resource = new PlayerResource(Engine, Resources);
            resource.State.BindLocal();
            resource.OnSpawn();

            Source = new Progress(
                new EventLink(new DeathEvent(), new HurtEvent(), Touches, Leaves,
                              new WinPanel(), new RoundStart()),
                Engine, Data, new LocalFake(), Objectives.Source,
                new SettingsFake(), Setup, Resources, Engine);
        }

        /// <summary>The round is running, so points may be captured.</summary>
        public void RunRound() => Resources.Live!.State.RoundState = (byte)RoundStateId.RoundRunning;

        /// <summary>A cap of <paramref name="seconds"/> for <paramref name="team"/>
        /// on point 0, with <paramref name="onPoint"/> of that team in the zone.</summary>
        public void Cap(int team, float seconds, int onPoint = 1)
        {
            int slot = Limits.TeamArray(0, team);
            Objectives.Arrive(s =>
            {
                s.NumControlPoints = 1;
                s.CappingTeam[0] = (byte)team;
                s.TeamInZone[0] = (byte)team;
                s.TeamCapTime[slot] = seconds;
                s.NumTeamMembers[slot] = (byte)onPoint;
            });
        }

        /// <summary>One condition on the local carrier, as the decode lands it.</summary>
        public void Cloak(TFCondId cond)
        {
            Objectives.Arrive(null);   // a carrier row, so the table's getters live
            TFCondMaskId mask = default;
            mask |= TFCondMaskId.Of(cond);
            TFNetStateViews.SetConditions(Data.Carrier, mask);
        }

        /// <summary>A legacy stun entry on the local carrier: the active
        /// index, the flags word and the stunned condition, as the decode
        /// lands them.</summary>
        public void Stun(client::TF2.Shared.TFStunFlags flags, bool cond = true)
        {
            Objectives.Arrive(null);
            Data.Carrier.StunIndex = 0;
            Data.Carrier.StunFlags = (int)flags;
            if (cond)
                Cloak(TFCondId.Stunned);
        }
    }

    // ---- the touched point (m_iCurrentCP) ----

    [Fact]
    public void The_local_players_touch_names_the_touched_point()
    {
        var r = new Rig();

        r.Touches.Raise(new Touch(2, 1));

        Assert.Equal(1, r.Source.Touched);
    }

    [Fact]
    public void Another_players_touch_is_ignored()
    {
        var r = new Rig();

        r.Touches.Raise(new Touch(3, 1));

        Assert.Equal(-1, r.Source.Touched);
    }

    [Fact]
    public void A_leave_clears_the_touched_point()
    {
        var r = new Rig();
        r.Touches.Raise(new Touch(2, 0));

        r.Leaves.Raise(new Touch(2, 0));

        Assert.Equal(-1, r.Source.Touched);
    }

    // ---- the bar arm (SetupForPoint, hud_controlpointicons.cpp:1519) ----

    [Fact]
    public void The_local_teams_running_cap_shows_the_bar()
    {
        var r = new Rig();
        r.RunRound();
        r.Cap(Red, 10f, onPoint: 1);

        Assert.True(r.Source.ShowsBar(0));
    }

    [Fact]
    public void The_enemy_teams_cap_hides_the_bar()
    {
        var r = new Rig();
        r.RunRound();
        r.Cap(Blue, 10f, onPoint: 1);

        Assert.False(r.Source.ShowsBar(0));
    }

    [Fact]
    public void A_cap_nobody_is_pushing_hides_the_bar()
    {
        var r = new Rig();
        r.RunRound();
        r.Cap(Red, 10f, onPoint: 0);

        Assert.False(r.Source.ShowsBar(0));
    }

    // ---- the sweep (GetCPCapPercentage) ----

    [Fact]
    public void The_sweep_takes_the_caps_progress()
    {
        var r = new Rig();
        r.Cap(Red, 10f, onPoint: 1);
        r.Engine.Returns("Clock.Now", 3f);

        Assert.Equal(0.3f, r.Source.Progress(0), 3);
    }

    // ---- the text ladder (UpdateBarText, :1546-1646) ----

    [Fact]
    public void Before_the_round_the_point_names_not_now()
    {
        var r = new Rig();

        Assert.Equal("#Team_Capture_NotNow", r.Source.Text(0));
    }

    [Fact]
    public void The_setup_period_names_not_now()
    {
        var r = new Rig();
        r.RunRound();
        r.Setup.InSetupWithRoundTimer = true;
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = true);

        Assert.Equal("#Team_Capture_NotNow", r.Source.Text(0));
    }

    [Fact]
    public void Waiting_for_players_names_not_now()
    {
        var r = new Rig();
        r.RunRound();
        r.Resources.Live!.State.WaitingForPlayers = 1;
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = true);

        Assert.Equal("#Team_Capture_NotNow", r.Source.Text(0));
    }

    [Fact]
    public void A_locked_point_names_not_now()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s => s.Locked[0] = true);

        Assert.Equal("#Team_Capture_NotNow", r.Source.Text(0));
    }

    [Fact]
    public void A_blocked_enemys_cap_names_blocking()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s =>
        {
            s.NumControlPoints = 1;
            s.CappingTeam[0] = (byte)Blue;
            s.Blocked[0] = true;
        });

        Assert.Equal("#Team_Blocking_Capture", r.Source.Text(0));
    }

    [Fact]
    public void A_reverting_point_names_reverting()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s =>
        {
            s.NumControlPoints = 1;
            s.CappingTeam[0] = (byte)Blue;
        });

        Assert.Equal("#Team_Reverting_Capture", r.Source.Text(0));
    }

    [Fact]
    public void The_local_teams_point_names_own_point()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s =>
        {
            s.NumControlPoints = 1;
            s.Owner[0] = (byte)Red;
            s.TeamCanCap[Limits.TeamArray(0, Blue)] = true;
        });

        Assert.Equal("#Team_Capture_OwnPoint", r.Source.Text(0));
    }

    [Fact]
    public void A_point_the_enemy_can_never_retake_names_owned()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s =>
        {
            s.NumControlPoints = 1;
            s.Owner[0] = (byte)Red;
        });

        Assert.Equal("#Team_Capture_Owned", r.Source.Text(0));
    }

    [Fact]
    public void A_point_behind_the_front_names_linear()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s =>
        {
            s.NumControlPoints = 1;
            s.Owner[0] = (byte)Blue;
            s.PreviousPoints[Limits.PreviousArray(0, Red, 0)] = 1;
        });

        Assert.Equal("#Team_Capture_Linear", r.Source.Text(0));
    }

    [Fact]
    public void A_stealthed_player_is_refused()
    {
        var r = new Rig();
        r.RunRound();
        r.Cloak(TFCondId.Stealthed);

        Assert.Equal("#Cant_cap_stealthed", r.Source.Text(0));
    }

    [Fact]
    public void An_invulnerable_player_is_refused()
    {
        var r = new Rig();
        r.RunRound();
        r.Cloak(TFCondId.Invulnerable);

        Assert.Equal("#Cant_cap_invuln", r.Source.Text(0));
    }

    [Fact]
    public void A_disguised_spy_of_the_enemy_is_refused()
    {
        var r = new Rig();
        r.RunRound();
        r.Cloak(TFCondId.Disguised);
        r.Data.Carrier.DisguiseTeam = Blue;

        Assert.Equal("#Cant_cap_disguised", r.Source.Text(0));
    }

    [Fact]
    public void A_spy_disguised_as_the_local_team_may_capture()
    {
        var r = new Rig();
        r.RunRound();
        r.Cloak(TFCondId.Disguised);
        r.Data.Carrier.DisguiseTeam = Red;
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = true);

        Assert.Equal("#Team_Waiting_for_teammate", r.Source.Text(0));
    }

    [Fact]
    public void A_control_stunned_player_is_refused()
    {
        var r = new Rig();
        r.RunRound();
        r.Stun(client::TF2.Shared.TFStunFlags.Controls);

        Assert.Equal("#Cant_cap_stunned", r.Source.Text(0));
    }

    [Fact]
    public void A_movement_stun_alone_leaves_the_capture_open()
    {
        var r = new Rig();
        r.RunRound();
        r.Stun(client::TF2.Shared.TFStunFlags.Movement);
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = true);

        Assert.Equal("#Team_Waiting_for_teammate", r.Source.Text(0));
    }

    [Fact]
    public void A_stun_word_without_an_active_entry_is_ignored()
    {
        var r = new Rig();
        r.RunRound();
        r.Stun(client::TF2.Shared.TFStunFlags.Controls);
        r.Data.Carrier.StunIndex = -1;   // no entry behind the word
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = true);

        Assert.Equal("#Team_Waiting_for_teammate", r.Source.Text(0));
    }

    [Fact]
    public void A_cap_the_local_team_pushes_names_blocked()
    {
        var r = new Rig();
        r.RunRound();
        r.Cap(Red, 10f, onPoint: 1);
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = true);

        Assert.Equal("#Team_Capture_Blocked", r.Source.Text(0));
    }

    [Fact]
    public void A_point_the_local_team_can_never_cap_names_cannot()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = false);

        Assert.Equal("#Team_Cannot_Capture", r.Source.Text(0));
    }

    [Fact]
    public void An_open_point_names_waiting_for_a_teammate()
    {
        var r = new Rig();
        r.RunRound();
        r.Objectives.Arrive(s => s.TeamCanCap[Limits.TeamArray(0, Red)] = true);

        Assert.Equal("#Team_Waiting_for_teammate", r.Source.Text(0));
    }

    // ---- the linear gate over the table (TeamMayCapturePoint) ----

    [Fact]
    public void A_point_past_an_untaken_one_is_refused()
    {
        var r = new Rig();
        r.Objectives.Arrive(s =>
        {
            s.NumControlPoints = 2;
            s.Owner[0] = (byte)Blue;
            s.PreviousPoints[Limits.PreviousArray(1, Red, 0)] = 0;
        });

        Assert.False(r.Objectives.Source.TeamMayCap(1, Red));
        Assert.True(r.Objectives.Source.TeamMayCap(0, Red));
    }
}
