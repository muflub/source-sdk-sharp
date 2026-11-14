extern alias client;

using SourceSharp.Abi;

using Xunit;

using Engine = client::SourceSharp.Abi.RecordingEngine;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using Timer = client::TF2.Client.Entities.ClientTeamRoundTimer;
using Timers = client::TF2.Client.Entities.TFClientRoundTimers;
using TimerState = client::TF2.Shared.Rules.TeamRoundTimerNetState;
using Warnings = client::TF2.Client.Entities.TFRoundTimerWarnings;

namespace TF2.Tests.Client;

/// <summary>
/// The client countdown half of <c>team_round_timer</c> (stock's
/// <c>ClientThink</c>/:469-519, <c>SendTimeWarning</c>/:699-783 and the bell
/// of <c>OnDataChanged</c>/:535-549): the per-second warnings ring once at
/// their trigger with the right name set, the round-start bell rings once as
/// setup hands over to normal, and a paused timer stays silent. The sounds
/// reach the local player's ears through the client's emitter (the same
/// record the contested announcer's facts read).
/// </summary>
public sealed class RoundTimerWarningsTests
{
    [Fact]
    public void A_normal_rounds_minute_warning_plays_once_when_its_second_comes()
    {
        var rig = new Rig();

        // The election's own timer, counting down from 61 seconds, in the
        // normal state and with the countdown on.
        rig.Arrive(s =>
        {
            s.State = TimerState.StateNormal;
            s.ShowInHud = true;
            s.AutoCountdown = true;
            s.TimerLength = 61;
            s.TimerEndTime = 61f;
        });
        rig.Clock.Now = 0f;

        rig.Think();
        rig.Think();

        // The minute's warning, its normal-set name, once.
        Assert.Equal(["Announcer.RoundEnds60seconds"], rig.Names);
    }

    [Fact]
    public void A_setup_round_rings_the_setup_names()
    {
        var rig = new Rig();

        rig.Arrive(s =>
        {
            s.State = TimerState.StateSetup;
            s.ShowInHud = true;
            s.AutoCountdown = true;
            s.TimerLength = 11;
            s.TimerEndTime = 11f;
        });
        rig.Clock.Now = 0f;

        rig.Think();
        rig.Think();

        // Eleven seconds left: the ten-second arm answers, with the setup
        // set's capitalised name (:38-45), once.
        Assert.Equal(["Announcer.RoundBegins10Seconds"], rig.Names);
    }

    [Fact]
    public void The_bell_rings_once_as_setup_hands_over_to_normal()
    {
        var rig = new Rig();

        rig.Arrive(s =>
        {
            s.State = TimerState.StateSetup;
            s.ShowInHud = true;
            s.AutoCountdown = true;
            s.TimerLength = 100;
            s.TimerEndTime = 100f;
        });
        rig.Clock.Now = 0f;
        rig.Think();

        // In setup, far from any warning second: nothing has rung.
        Assert.Empty(rig.Names);

        rig.Arrive(s => s.State = TimerState.StateNormal);
        rig.Think();
        rig.Think();

        // ROUND_START_BELL (:55), once, and the long normal remainder adds
        // nothing.
        Assert.Equal(["Ambient.Siren"], rig.Names);
    }

    [Fact]
    public void A_paused_timer_keeps_silent_until_it_resumes()
    {
        var rig = new Rig();

        rig.Arrive(s =>
        {
            s.State = TimerState.StateNormal;
            s.ShowInHud = true;
            s.AutoCountdown = true;
            s.TimerPaused = true;
            s.TimerLength = 61;
            s.TimerEndTime = 61f;

            // The paused remainder is what the server writes alongside the
            // pause (stock m_nTimeRemaining), so the flags see the real clock.
            s.TimeRemaining = 61f;
        });
        rig.Clock.Now = 0f;

        rig.Think();
        rig.Think();

        // Paused: ClientThink's gate (:471) and SendTimeWarning's (:708) both
        // answer silence.
        Assert.Empty(rig.Names);

        rig.Arrive(s => s.TimerPaused = false);
        rig.Think();

        // Resumed: the owed warning rings.
        Assert.Equal(["Announcer.RoundEnds60seconds"], rig.Names);
    }

    /// <summary>One timer's countdown announcer, driven the way the client
    /// module drives it: state packets arrive as the registry delivers them,
    /// and thinks run once a frame.</summary>
    private sealed class Rig
    {
        public readonly Timers Timers;

        public readonly Timer Timer;

        public readonly MoveClock Clock = new();

        public readonly ContestedSounds Sounds = new();

        /// <summary>The gamerules' words the warnings' gates read through the
        /// live <c>tf_player_manager</c>; a fact puts one on it as the registry
        /// does when the entity arrives.</summary>
        public readonly PlayerResources Resources = new();

        public readonly Warnings Announcer;

        public Rig()
        {
            Timers = new Timers(new ObjectiveRig().Source);
            Timer = new Timer(Engine.Default(), Timers);
            Timer.State.BindLocal();
            Timer.OnSpawn();

            var hud = Engine.Default();
            hud.Returns("Hud.LocalPlayerIndex", 1);

            Announcer = new Warnings(Timers, Clock, Resources, hud, Sounds);
        }

        /// <summary>Writes onto the one timer state, snapshots and delivers it,
        /// then calls the entity's data-changed the way the registry does. The
        /// fields persist across packets, as they do behind the entity.</summary>
        public void Arrive(Action<TimerState> write)
        {
            _state ??= CreateBound();
            write(_state);

            byte[] blob = _state.Snapshot();
            Timer.State.Decode(blob);
            Timer.OnDataChanged(blob, DataUpdateType.DataTableChanged);
        }

        private TimerState? _state;

        private static TimerState CreateBound()
        {
            var state = new TimerState();
            state.BindLocal();
            return state;
        }

        public void Think() => Announcer.ClientThink(1f / 60f);

        /// <summary>The script names the emitter was asked for, in order.</summary>
        public List<string> Names => Sounds.Emitted.Select(e => e.Name).ToList();
    }

    /// <summary>A clock a fact moves (client::SourceSharp.Abi.IGameClock).</summary>
    private sealed class MoveClock : client::SourceSharp.Abi.IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>
    /// In KOTH the other team's timer announces too (stock's KOTH arm of
    /// <c>SendTimeWarning</c>, :768-771): a timer the HUD does not show still
    /// plays its warning while the rules carry the KOTH word.
    /// </summary>
    [Fact]
    public void In_koth_a_timer_the_hud_does_not_show_still_warns()
    {
        var rig = new Rig();

        // The gamerules' KOTH word, riding the player resource as the other
        // gamerules words do (stock's IsInKothMode, :768).
        var resource = new PlayerResource(Engine.Default(), rig.Resources);
        resource.State.BindLocal();
        resource.State.KothMode = 1;
        rig.Resources.Live = resource;

        rig.Arrive(s =>
        {
            s.State = TimerState.StateNormal;
            s.ShowInHud = false;      // the election names no timer at all
            s.AutoCountdown = true;
            s.TimerLength = 61;
            s.TimerEndTime = 61f;
        });
        rig.Clock.Now = 0f;

        rig.Think();
        rig.Think();

        Assert.Equal(["Announcer.RoundEnds60seconds"], rig.Names);
    }

    /// <summary>Outside KOTH the election alone decides (:763-766): a timer
    /// the HUD does not show answers nothing.</summary>
    [Fact]
    public void Outside_koth_a_timer_the_hud_does_not_show_stays_silent()
    {
        var rig = new Rig();   // no live resource: the KOTH word answers nothing

        rig.Arrive(s =>
        {
            s.State = TimerState.StateNormal;
            s.ShowInHud = false;
            s.AutoCountdown = true;
            s.TimerLength = 61;
            s.TimerEndTime = 61f;
        });
        rig.Clock.Now = 0f;

        rig.Think();
        rig.Think();

        Assert.Empty(rig.Names);
    }
}
