//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// <c>team_round_timer</c> (<c>CTeamRoundTimer</c>, teamplay_round_timer.cpp):
/// a round's countdown, with an optional setup period before it, the
/// minute/second outputs a map hangs its announcer and gates on, and
/// <c>OnFinished</c>, which a map wires to its <c>game_round_win</c> or its
/// control-point master. Networked to everyone (stock <c>FL_EDICT_ALWAYS</c>)
/// as <see cref="TeamRoundTimerNetState"/> for the HUD.
///
/// <para>
/// The timer asks the round machine (<see cref="IRoundTimerHost"/>) the
/// questions stock asked <c>TeamplayRoundBasedRules()</c>: it holds at zero
/// while <see cref="IRoundTimerHost.TimerMayExpire"/> says no (overtime),
/// pauses while a round is won, and tells the machine when its setup starts
/// and ends. The HUD timer is the one <see cref="IRoundTimers"/> elects.
/// </para>
///
/// <para>
/// Not ported (deferred rows): the stopwatch (tournament) and the client's
/// countdown warnings (Stage 3). Stock's KOTH arms of the announcer gates
/// wait on the mode itself.
/// </para>
/// </summary>
[ScriptedEntity(TeamRoundTimerNetState.ClassName, ScriptedEntityKind.Effect)]
public sealed class TeamRoundTimer(IRoundTimerHost host, IRoundTimers timers, IEngineGameEvents events,
                                   IEngineEntityRender render, TFOvertimeNagConVar overtimeNag, IRandomSeeds seeds)
    : ScriptedEntity, ITeamRoundTimer
{
    /// <summary>Stock's think interval for both thinks (0.05 s).</summary>
    public const float ThinkInterval = 0.05f;

    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public TeamRoundTimerVars Vars = new();

    [NetState] public TeamRoundTimerNetState State = new();

    public readonly Output OnRoundStart = new();
    public readonly Output OnFinished = new();
    public readonly Output On5MinRemain = new();
    public readonly Output On4MinRemain = new();
    public readonly Output On3MinRemain = new();
    public readonly Output On2MinRemain = new();
    public readonly Output On1MinRemain = new();
    public readonly Output On30SecRemain = new();
    public readonly Output On10SecRemain = new();
    public readonly Output On5SecRemain = new();
    public readonly Output On4SecRemain = new();
    public readonly Output On3SecRemain = new();
    public readonly Output On2SecRemain = new();
    public readonly Output On1SecRemain = new();
    public readonly Output OnSetupStart = new();
    public readonly Output OnSetupFinished = new();

    private bool _paused;
    private float _timeRemaining;
    private float _endTime;
    private int _timerLength;
    private int _maxLength;
    private bool _disabled;
    private bool _showInHud;
    private bool _autoCountdown;
    private int _setupLength;
    private int _state = TeamRoundTimerNetState.StateNormal;
    private bool _pauseDueToWin;
    private int _timeToUseAfterSetupFinished;
    private float _lastTime;
    private float _nextOvertimeNag;

    /// <summary>Stock's <c>RandomInt( 0, 1 )</c> for the overtime nag, our coin
    /// (<see cref="IRandomSeeds"/> seeded at construction, the TeamRoundFlow way).</summary>
    private readonly Random _coin = new(seeds.Next());

    private bool _fireFinished = true;
    private bool _fire5Min = true, _fire4Min = true, _fire3Min = true, _fire2Min = true, _fire1Min = true;
    private bool _fire30Sec = true, _fire10Sec = true, _fire5Sec = true, _fire4Sec = true;
    private bool _fire3Sec = true, _fire2Sec = true, _fire1Sec = true;

    // ---- ITeamRoundTimer --------------------------------------------------

    /// <summary>Stock <c>GetTimeRemaining</c>: seconds left, paused or not, never negative.</summary>
    public float TimeRemaining => Math.Max(0f, _paused ? _timeRemaining : _endTime - GameTime);

    public bool ShowInHud => _showInHud;

    public bool IsDisabled => _disabled;

    /// <summary>Stock's <c>StartPaused()</c> (teamplay_round_timer.h:36): the map's start_paused key.</summary>
    public bool StartPaused => Vars.StartPaused;

    /// <summary>Whether the countdown is stopped.</summary>
    public bool IsPaused => _paused;

    /// <summary>Whether the timer is in its setup period.</summary>
    public bool InSetupPeriod => _state == TeamRoundTimerNetState.StateSetup;

    public void SetShowInHud(bool show)
    {
        _showInHud = show;
        Publish();
    }

    /// <summary>Stock <c>GetTimerMaxLength</c>: the setup length in setup, else the max (or the current) length.</summary>
    public int TimerMaxLength => _state == TeamRoundTimerNetState.StateSetup
        ? _setupLength
        : _maxLength != 0 ? _maxLength : _timerLength;

    // ---- Spawn and Activate ------------------------------------------------

    public override void OnSpawn()
    {
        // FL_EDICT_ALWAYS: the HUD reads the timer wherever the player is.
        render.SetTransmitAlways(Handle, true);
        Initialise();
        SetNextThink(ThinkInterval);
    }

    public override void OnServerActivate()
    {
        if (_showInHud)
            timers.SetActive(this);
    }

    /// <summary>The construct-and-Spawn half: the keys read, the timer paused at its first length.</summary>
    private void Initialise()
    {
        _paused = false;
        _timeRemaining = 0f;
        _endTime = 0f;
        _maxLength = Vars.MaxLength;
        _showInHud = Vars.ShowInHud;
        _autoCountdown = Vars.AutoCountdown;
        _setupLength = Vars.SetupLength;
        _pauseDueToWin = false;
        _lastTime = 0f;

        int time;

        // Do we have a setup time?
        if (_setupLength > 0)
        {
            time = _setupLength;
            SetState(TeamRoundTimerNetState.StateSetup, fireOutput: true);
        }
        else
        {
            time = Vars.TimerLength;
            SetState(TeamRoundTimerNetState.StateNormal, fireOutput: true);
        }

        _timeToUseAfterSetupFinished = Vars.TimerLength;

        // We need the data initialised before actually becoming disabled.
        _disabled = false;
        PauseTimer();   // start paused
        SetTimeRemaining(time);
        _disabled = Vars.StartDisabled;

        _timerLength = time;
        Publish();
    }

    // ---- The thinks ----------------------------------------------------------

    public override void OnThink()
    {
        if (_state == TeamRoundTimerNetState.StateSetup)
            SetupThink();
        else
            RoundThink();

        Publish();
        SetNextThink(ThinkInterval);
    }

    /// <summary>Stock <c>RoundTimerSetupThink</c>.</summary>
    private void SetupThink()
    {
        float lastTime = _lastTime;
        _lastTime = TimeRemaining;

        if (_disabled || _paused)
            return;

        float time = TimeRemaining;
        host.SetOvertime(false);

        if (_lastTime > 0f)
        {
            int lastSecond = (int)MathF.Floor(lastTime);
            int thisSecond = (int)MathF.Floor(time);
            if (lastSecond != thisSecond)
                Fire(TFMessages.EventPreRoundTimeLeft, e => events.EventSetInt(e, "time", thisSecond));
        }

        if (time <= 0f && _fireFinished)
        {
            Fire(TFMessages.EventSetupFinished, _ => { });

            OnSetupFinished.Fire(Handle);
            _fireFinished = false;

            SetState(TeamRoundTimerNetState.StateNormal, fireOutput: true);
            SetTimeRemaining(_timeToUseAfterSetupFinished);

            if (_showInHud && !host.InWaitingForPlayers)
                Log.Msg("[TF2/Rounds] World triggered \"Round_Setup_End\"\n");
            return;
        }

        if (time <= 60f && _fire1Min) { On1MinRemain.Fire(Handle); _fire1Min = false; }
        else if (time <= 30f && _fire30Sec) { On30SecRemain.Fire(Handle); _fire30Sec = false; }
        else if (time <= 10f && _fire10Sec) { On10SecRemain.Fire(Handle); _fire10Sec = false; }
        else if (time <= 5f && _fire5Sec) { On5SecRemain.Fire(Handle); _fire5Sec = false; }
        else if (time <= 4f && _fire4Sec) { On4SecRemain.Fire(Handle); _fire4Sec = false; }
        else if (time <= 3f && _fire3Sec) { On3SecRemain.Fire(Handle); _fire3Sec = false; }
        else if (time <= 2f && _fire2Sec) { On2SecRemain.Fire(Handle); _fire2Sec = false; }
        else if (time <= 1f && _fire1Sec) { On1SecRemain.Fire(Handle); _fire1Sec = false; }
    }

    /// <summary>Stock <c>RoundTimerThink</c>.</summary>
    private void RoundThink()
    {
        if (_disabled || _paused)
            return;

        // Don't do anything when the game has been won. Stop the timer, but
        // without making the map unpause it: the win's pause undoes itself.
        if (host.State == RoundState.TeamWin)
        {
            if (!_paused)
            {
                PauseTimer();
                _pauseDueToWin = true;
            }

            return;
        }
        else if (_pauseDueToWin)
        {
            // Stock's arm, kept though its own early return above (a paused
            // timer thinks no further) means only an outside resume reaches it.
            ResumeTimer();
            _pauseDueToWin = false;
        }

        float time = TimeRemaining;

        // Is this the timer we're showing in the HUD?
        if (time > 0f && _showInHud)
            host.SetOvertime(false);

        if (time <= 0f && _fireFinished)
        {
            // Allow the rules to prevent expiry (a contested point, a moving cart).
            if (!host.TimerMayExpire())
            {
                // We don't want the timer to keep going (negative time).
                _endTime = GameTime;

                // The HUD's timer owns the overtime word (:999-1023).
                if (_showInHud)
                {
                    if (!host.InOvertime)
                    {
                        host.SetOvertime(true);
                    }
                    else if (overtimeNag.Value
                             && GameTime > _nextOvertimeNag)
                    {
                        // stock's m_flNextOvertimeNag = curtime + 1.0f, then the
                        // coin (:1010-1012): the nag says itself about once a second.
                        _nextOvertimeNag = GameTime + 1f;
                        if (_coin.Next(0, 2) > 0)
                            Fire(TFMessages.EventOvertimeNag, _ => { });
                    }
                }
                return;
            }

            OnFinished.Fire(Handle);
            _fireFinished = false;
        }
        else if (time <= 300f && _fire5Min) { On5MinRemain.Fire(Handle); _fire5Min = false; }
        else if (time <= 240f && _fire4Min) { On4MinRemain.Fire(Handle); _fire4Min = false; }
        else if (time <= 180f && _fire3Min) { On3MinRemain.Fire(Handle); _fire3Min = false; }
        else if (time <= 120f && _fire2Min) { On2MinRemain.Fire(Handle); _fire2Min = false; }
        else if (time <= 60f && _fire1Min) { On1MinRemain.Fire(Handle); _fire1Min = false; }
        else if (time <= 30f && _fire30Sec) { On30SecRemain.Fire(Handle); _fire30Sec = false; }
        else if (time <= 10f && _fire10Sec) { On10SecRemain.Fire(Handle); _fire10Sec = false; }
        else if (time <= 5f && _fire5Sec) { On5SecRemain.Fire(Handle); _fire5Sec = false; }
        else if (time <= 4f && _fire4Sec) { On4SecRemain.Fire(Handle); _fire4Sec = false; }
        else if (time <= 3f && _fire3Sec) { On3SecRemain.Fire(Handle); _fire3Sec = false; }
        else if (time <= 2f && _fire2Sec) { On2SecRemain.Fire(Handle); _fire2Sec = false; }
        else if (time <= 1f && _fire1Sec) { On1SecRemain.Fire(Handle); _fire1Sec = false; }
    }

    // ---- The countdown --------------------------------------------------------

    private void SetState(int state, bool fireOutput)
    {
        _state = state;

        if (state == TeamRoundTimerNetState.StateSetup)
        {
            host.SetSetup(true);
            if (fireOutput)
                OnSetupStart.Fire(Handle);
        }
        else
        {
            host.SetSetup(false);
            if (fireOutput)
                OnRoundStart.Fire(Handle);
        }
    }

    /// <summary>
    /// Stock <c>SetTimeRemaining</c>, public as stock's is: the length, capped
    /// at the max, from now — stock's <c>CKothLogic</c> sets the team timers
    /// with it (tf_gamerules.cpp:19955, 19969).
    /// </summary>
    public void SetTimeRemaining(int seconds)
    {
        if (_disabled)
            return;

        // Make sure we don't go over our max length.
        if (_maxLength > 0)
            seconds = Math.Min(seconds, _maxLength);

        _timeRemaining = seconds;
        _endTime = GameTime + _timeRemaining;
        _timerLength = seconds;

        CalculateOutputMessages();
    }

    /// <summary>Stock <c>CalculateOutputMessages</c>: which outputs are still to come.</summary>
    private void CalculateOutputMessages()
    {
        float time = TimeRemaining;

        _fireFinished = time > 0f;
        _fire5Min = time >= 300f;
        _fire4Min = time >= 240f;
        _fire3Min = time >= 180f;
        _fire2Min = time >= 120f;
        _fire1Min = time >= 60f;
        _fire30Sec = time >= 30f;
        _fire10Sec = time >= 10f;
        _fire5Sec = time >= 5f;
        _fire4Sec = time >= 4f;
        _fire3Sec = time >= 3f;
        _fire2Sec = time >= 2f;
        _fire1Sec = time >= 1f;
    }

    /// <summary>Stock <c>PauseTimer</c>.</summary>
    public void PauseTimer()
    {
        if (_disabled)
            return;

        if (!_paused)
        {
            _paused = true;
            _timeRemaining = _endTime - GameTime;
        }

        // Clear the pause-on-win flag: the mapmaker paused us.
        _pauseDueToWin = false;
    }

    /// <summary>Stock <c>ResumeTimer</c>.</summary>
    public void ResumeTimer()
    {
        if (_disabled)
            return;

        if (_paused)
        {
            _paused = false;
            _endTime = GameTime + _timeRemaining;
        }
    }

    /// <summary>
    /// Stock <c>AddTimerSeconds</c> (:1226-1320): while the round runs (or has
    /// just been won), never past the max length, paused or running.
    ///
    /// <para>
    /// The announcer (:1280-1319): the elected HUD timer tells everyone time
    /// was added — the responsible team <c>Announcer.TimeAwardedForTeam</c>
    /// and every other team <c>Announcer.TimeAddedForEnemy</c> (stock's
    /// per-team <c>CTeamRecipientFilter</c> arms, :1284-1300, whose
    /// <c>LAST_SHARED_TEAM+1</c> gate is Red-and-up), or plain
    /// <c>Announcer.TimeAdded</c> when no team is responsible (:1301-1305).
    /// Only while nobody has won: stock's inner gate is
    /// <c>!InStalemate &amp;&amp; !RoundHasBeenWon</c> — the stalemate is the
    /// early return above, the win the <see cref="RoundState.TeamWin"/> test —
    /// and its KOTH arms wait on the mode. The broadcast-audio event is how
    /// stock's server-side filtered <c>EmitSound</c>s reach players here, the
    /// way <c>CTeamplayRoundBasedRules::BroadcastSound</c> does it
    /// (tf_gamerules.cpp:22356-22370; WinSong uses the same event). The
    /// <c>teamplay_timer_time_added</c> event rides below the sound gate
    /// (:1308-1318).
    /// </para>
    /// </summary>
    public void AddTimerSeconds(int seconds, int team = (int)TFTeam.Unassigned)
    {
        if (_disabled)
            return;

        if (host.State == RoundState.Stalemate)
            return;

        // Only while round_running or team_win, so control points setting
        // their default owner at the map's load don't add time.
        if (host.State is not (RoundState.RoundRunning or RoundState.TeamWin))
            return;

        if (_maxLength > 0 && TimeRemaining + seconds > _maxLength)
            seconds = (int)(_maxLength - TimeRemaining);

        if (_paused)
            _timeRemaining += seconds;
        else
            _endTime += seconds;

        _timerLength += seconds;
        CalculateOutputMessages();

        // Stock's outer gate: the timer the HUD shows (the KOTH arm is not
        // ported); our election is IRoundTimers', which writes the index the
        // objective resource would carry (docs/divergences.md).
        if (ReferenceEquals(timers.Active, this))
        {
            if (host.State != RoundState.TeamWin)
            {
                if (team >= (int)TFTeam.Red)   // :1284 LAST_SHARED_TEAM+1
                {
                    foreach (var t in new[] { TFTeam.Red, TFTeam.Blue })
                        BroadcastSound(t, t == (TFTeam)team
                            ? "Announcer.TimeAwardedForTeam"      // ROUND_TIMER_TIME_ADDED_WINNER
                            : "Announcer.TimeAddedForEnemy");     // ROUND_TIMER_TIME_ADDED_LOSER
                }
                else
                {
                    BroadcastSound(TFTeam.Unassigned, "Announcer.TimeAdded");   // ROUND_TIMER_TIME_ADDED
                }
            }

            // Is this the timer we're showing in the HUD?
            if (_showInHud)
            {
                int added = seconds;
                Fire(TFMessages.EventTimerTimeAdded, e =>
                {
                    events.EventSetInt(e, "timer", Entities.GetIndex(Handle));
                    events.EventSetInt(e, "seconds_added", added);
                });
            }
        }
    }

    /// <summary>
    /// One sound to a team (or, as <see cref="TFTeam.Unassigned"/>, to
    /// everyone): the <c>teamplay_broadcast_audio</c> event, stock's
    /// <c>BroadcastSound</c> (tf_gamerules.cpp:22356-22370) and the song
    /// seam's (<see cref="GameEventRoundAnnouncer"/>).
    /// </summary>
    private void BroadcastSound(TFTeam team, string sound)
    {
        Fire(TFMessages.EventBroadcastAudio, e =>
        {
            events.EventSetInt(e, "team", (int)team);
            events.EventSetString(e, "sound", sound);
        });
    }

    // ---- The inputs -------------------------------------------------------------

    [ScriptedEvent] private void OnPause()
    {
        PauseTimer();
        Publish();
    }

    [ScriptedEvent] private void OnResume()
    {
        ResumeTimer();
        Publish();
    }

    [ScriptedEvent] private void OnSetTime(float seconds)
    {
        SetTimeRemaining((int)seconds);
        Publish();
    }

    [ScriptedEvent] private void OnSetMaxTime(float seconds)
    {
        _maxLength = (int)seconds;

        // Make sure our current time is not above the max length.
        if (_maxLength > 0 && TimeRemaining > _maxLength)
            SetTimeRemaining(_maxLength);
        Publish();
    }

    [ScriptedEvent] private void OnAddTime(float seconds)
    {
        AddTimerSeconds((int)seconds);
        Publish();
    }

    /// <summary>
    /// Stock <c>InputAddTeamTime</c> (:1393-1418): "&lt;team&gt;
    /// &lt;seconds&gt;", both tokens <c>Q_atoi</c>'d, added only when the
    /// seconds are non-zero. The team is the announcer's
    /// <c>iTeamResponsible</c>: it picks which team hears
    /// <c>TimeAwardedForTeam</c> (:1284-1300).
    /// </summary>
    [ScriptedEvent] private void OnAddTeamTime(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int team = parts.Length > 0 ? ParseInt(parts[0]) : (int)TFTeam.Unassigned;
        int seconds = parts.Length > 1 ? ParseInt(parts[1]) : 0;
        if (seconds != 0)
            AddTimerSeconds(seconds, team);
        Publish();
    }

    [ScriptedEvent] private void OnRestart()
    {
        SetTimeRemaining(Vars.TimerLength);
        Publish();
    }

    [ScriptedEvent] private void OnEnable()
    {
        _disabled = false;
        ResumeTimer();

        if (_showInHud)
            timers.SetActive(this);
        Publish();
    }

    [ScriptedEvent] private void OnDisable()
    {
        PauseTimer();
        _disabled = true;

        if (_showInHud)
            timers.SetActive(null);
        Publish();
    }

    [ScriptedEvent("ShowInHUD")] private void OnShowInHudInput(float show) => InputShowInHud((int)show == 1);

    public void InputShowInHud(bool show)
    {
        if (_showInHud && !show)
        {
            _showInHud = false;
            timers.SetActive(null);
        }
        else if (show)
        {
            _showInHud = true;
            timers.SetActive(this);
            SetState(_state, fireOutput: false);   // so the rules hear our setup state again
        }

        _showInHud = show;
        Publish();
    }

    [ScriptedEvent] private void OnAutoCountdown(float auto)
    {
        _autoCountdown = (int)auto == 1;
        Publish();
    }

    [ScriptedEvent] private void OnSetSetupTime(float seconds)
    {
        if ((int)seconds >= 0)
            _setupLength = (int)seconds;

        if (!_disabled && _state == TeamRoundTimerNetState.StateSetup)
            SetTimeRemaining(_setupLength);
        Publish();
    }

    /// <summary>
    /// Stock <c>InputRoundSpawn</c> (the round's RoundSpawn): the time a
    /// running timer keeps across a mini-round, the setup period again if
    /// there is one, and the countdown started unless the map starts it.
    /// </summary>
    [ScriptedEvent("RoundSpawn")]
    public void OnRoundSpawn()
    {
        if (!Vars.ResetTime && _state == TeamRoundTimerNetState.StateNormal)
            _timeToUseAfterSetupFinished = (int)TimeRemaining;
        else
            _timeToUseAfterSetupFinished = Vars.TimerLength;

        if (_setupLength > 0)
        {
            SetState(TeamRoundTimerNetState.StateSetup, fireOutput: true);
            SetTimeRemaining(_setupLength);

            if (_showInHud && !host.InWaitingForPlayers)
                Log.Msg("[TF2/Rounds] World triggered \"Round_Setup_Begin\"\n");
        }
        else
        {
            SetState(TeamRoundTimerNetState.StateNormal, fireOutput: true);
            SetTimeRemaining(_timeToUseAfterSetupFinished);
        }

        if (!Vars.StartPaused && !host.InWaitingForPlayers)
            ResumeTimer();
        Publish();
    }

    // ---- The wire ------------------------------------------------------------------

    private void Publish()
    {
        State.TimerPaused = _paused;
        State.TimeRemaining = _timeRemaining;
        State.TimerEndTime = _endTime;
        State.TimerMaxLength = _maxLength;
        State.IsDisabled = _disabled;
        State.ShowInHud = _showInHud;
        State.TimerLength = _timerLength;
        State.TimerInitialLength = Vars.TimerLength;
        State.AutoCountdown = _autoCountdown;
        State.SetupTimeLength = _setupLength;
        State.State = _state;
        State.StartPaused = Vars.StartPaused;
        State.ShowTimeRemaining = Vars.ShowTimeRemaining;
    }

    private void Fire(string name, Action<IntPtr> fill)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;

        fill(e);
        events.EventFire(e);
    }
}

/// <summary>The map keys <see cref="TeamRoundTimer"/> reads (stock's <c>DEFINE_KEYFIELD</c>s, their defaults the constructor's).</summary>
[ScriptVars]
public partial class TeamRoundTimerVars
{
    [VarName("timer_length")] public int TimerLength { get; set; }

    [VarName("max_length")] public int MaxLength { get; set; }

    [VarName("show_in_hud")] public bool ShowInHud { get; set; }

    public bool StartDisabled { get; set; }

    [VarName("auto_countdown")] public bool AutoCountdown { get; set; } = true;

    [VarName("setup_length")] public int SetupLength { get; set; }

    [VarName("reset_time")] public bool ResetTime { get; set; }

    [VarName("start_paused")] public bool StartPaused { get; set; } = true;

    [VarName("show_time_remaining")] public bool ShowTimeRemaining { get; set; } = true;
}

/// <summary><c>tf_overtime_nag</c> (<c>teamplay_round_timer.cpp:86</c>, under
/// <c>#if defined( GAME_DLL ) && defined( TF_DLL )</c>): the overtime nag's
/// switch, declared in the timer's own file as stock declares it in its cpp.</summary>
[ConVar("tf_overtime_nag", "0", Flags = ConVarFlags.Notify, Help = "Announcer overtime nag.")]
public sealed class TFOvertimeNagConVar : BoolConVar { }
