//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using TF2.Shared.Rules;

namespace TF2.Client.Entities;

/// <summary>One round timer as the client reads it (stock's <c>CTeamRoundTimer</c> client arms).</summary>
public interface ITFRoundTimer
{
    EntityHandle Handle { get; }

    /// <summary>The engine's entity index, which is what the objective resource names (stock <c>GetActiveRoundTimer</c>).</summary>
    int Index { get; }

    /// <summary>Stock <c>GetTimeRemaining</c>: the stopwatch's total in a capture watch, the paused remainder, else the end time minus <paramref name="now"/>, never negative.</summary>
    float TimeRemaining(float now);

    /// <summary><c>m_bTimerPaused</c>.</summary>
    bool IsPaused { get; }

    /// <summary><c>RT_STATE_SETUP</c>.</summary>
    bool InSetup { get; }

    /// <summary><c>m_bShowInHUD</c>.</summary>
    bool ShowInHud { get; }

    bool IsDisabled { get; }

    /// <summary>Stock <c>GetTimerMaxLength</c>: the setup length in setup, else the max (or the current) length.</summary>
    int TimerMaxLength { get; }

    /// <summary><c>m_bShowTimeRemaining</c>.</summary>
    bool ShowTimeRemaining { get; }

    /// <summary><c>m_bStopWatchTimer</c>.</summary>
    bool IsStopWatch { get; }

    /// <summary><c>m_bInCaptureWatchState</c> (stock's <c>IsWatchingTimeStamps</c>).</summary>
    bool InCaptureWatchState { get; }

    /// <summary><c>m_flTotalTime</c> (stock <c>GetStopWatchTotalTime</c>).</summary>
    float StopWatchTotalTime { get; }

    /// <summary><c>m_bAutoCountdown</c>.</summary>
    bool AutoCountdown { get; }

    /// <summary><c>m_nTimerInitialLength</c> (stock <c>GetTimerInitialLength</c>).</summary>
    int InitialLength { get; }

    /// <summary><c>m_nSetupTimeLength</c> (stock <c>GetSetupTimeLength</c>).</summary>
    int SetupLength { get; }
}

/// <summary>
/// The round timers this client knows and the one the HUD shows.
///
/// <para>
/// Stock keeps no set: <c>CTeamplayRoundBasedRules::GetActiveRoundTimer</c>
/// asks the objective resource for <c>m_iTimerToShowInHUD</c> and looks the
/// entity up by index. Our server elects the same timer through
/// <c>IRoundTimers</c> but does not publish its index into the objective
/// resource (docs/divergences.md), so the fallback below is the same walk the
/// server's <c>SetActiveTimer</c> runs: the first enabled HUD timer. When the
/// index does arrive it wins, which is stock's own answer.
/// </para>
/// </summary>
public interface ITFRoundTimers
{
    /// <summary>The timer shown in the HUD (stock <c>GetActiveRoundTimer</c>), or null.</summary>
    ITFRoundTimer? Active { get; }

    /// <summary>Every live timer.</summary>
    IReadOnlyList<ITFRoundTimer> All { get; }

    int Count { get; }
}

/// <summary>
/// The client half of <c>team_round_timer</c>: the decoded
/// <see cref="TeamRoundTimerNetState"/> and stock's client-side arithmetic over
/// it (<c>GetTimeRemaining</c>, <c>GetTimerMaxLength</c>), for the HUD timer.
/// Kept in <see cref="TFClientRoundTimers"/> from its first update to its
/// removal.
/// </summary>
[ClientStandaloneEntity(TeamRoundTimerNetState.ClassName)]
public sealed class ClientTeamRoundTimer : ClientStandaloneEntity, ITFRoundTimer
{
    private readonly TFClientRoundTimers _timers;

    public ClientTeamRoundTimer(IEngineEntityRender render, TFClientRoundTimers timers)
        : base(render)
    {
        _timers = timers;
    }

    public override string ClassName => TeamRoundTimerNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TeamRoundTimerNetState State = new();

    /// <summary>How many updates have decoded into <see cref="State"/>.</summary>
    public int Decodes { get; private set; }

    /// <summary>Stock <c>GetTimeRemaining</c> (teamplay_round_timer.cpp:377-403).</summary>
    public float TimeRemaining(float now)
    {
        float seconds;

        if (IsStopWatch && InCaptureWatchState)
            seconds = StopWatchTotalTime;
        else if (IsPaused)
            seconds = State.TimeRemaining;
        else
            seconds = State.TimerEndTime - now;

        return seconds < 0f ? 0f : seconds;
    }

    public bool IsPaused => State.TimerPaused;

    public bool InSetup => State.State == TeamRoundTimerNetState.StateSetup;

    public bool ShowInHud => State.ShowInHud;

    public bool IsDisabled => State.IsDisabled;

    public int TimerMaxLength => InSetup
        ? State.SetupTimeLength
        : State.TimerMaxLength != 0 ? State.TimerMaxLength : State.TimerLength;

    public bool ShowTimeRemaining => State.ShowTimeRemaining;

    public bool IsStopWatch => State.StopWatchTimer;

    public bool InCaptureWatchState => State.InCaptureWatchState;

    public float StopWatchTotalTime => State.TotalTime;

    public bool AutoCountdown => State.AutoCountdown;

    public int InitialLength => State.TimerInitialLength;

    public int SetupLength => State.SetupTimeLength;

    public override void OnSpawn() => _timers.Track(this);

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;
        _timers.Track(this);
    }

    public override void OnRemoved() => _timers.Untrack(this);
}

/// <summary>The client's live round timers, in arrival order.</summary>
[SideService(Side.Client, As = [typeof(ITFRoundTimers)])]
public sealed class TFClientRoundTimers(ITFObjectiveSource objectives) : ITFRoundTimers
{
    private readonly List<ITFRoundTimer> _live = new();

    public int Count => _live.Count;

    public IReadOnlyList<ITFRoundTimer> All => _live;

    public void Track(ITFRoundTimer timer)
    {
        if (!_live.Contains(timer))
            _live.Add(timer);
    }

    public void Untrack(ITFRoundTimer timer) => _live.Remove(timer);

    /// <summary>
    /// Stock <c>GetActiveRoundTimer</c> first: the timer the objective resource
    /// names. Our server does not write that index yet, so the fallback is the
    /// election its <c>SetActiveTimer</c> makes — the first enabled HUD timer.
    /// </summary>
    public ITFRoundTimer? Active
    {
        get
        {
            int index = objectives.TimerToShowInHud;
            if (index > 0)
            {
                foreach (var timer in _live)
                {
                    if (timer.Index == index)
                        return timer;
                }
            }

            foreach (var timer in _live)
            {
                if (timer.ShowInHud && !timer.IsDisabled)
                    return timer;
            }

            return null;
        }
    }

    /// <summary>The election's inputs, for <c>tf2_cl_round_timers</c>: what
    /// the objective resource names, and every live timer's HUD words.</summary>
    public string Report()
    {
        var text = new StringBuilder($"[TF2] round timers: {_live.Count} live,"
                                     + $" objective names index {objectives.TimerToShowInHud}");
        foreach (var timer in _live)
        {
            text.Append($"\n  index {timer.Index} handle {timer.Handle.Value}: showInHud {timer.ShowInHud},"
                        + $" disabled {timer.IsDisabled}, paused {timer.IsPaused}, setup {timer.InSetup},"
                        + $" showRemaining {timer.ShowTimeRemaining}, max {timer.TimerMaxLength}s,"
                        + $" remaining {timer.TimeRemaining(0f):F1}s (now-independent arms only)");
        }

        return text.ToString();
    }
}

/// <summary>The console door onto the client's round-timer election.</summary>
[ConCommand("tf2_cl_round_timers", Help = "The round timers this client has decoded and the HUD's election")]
public sealed class TFRoundTimerReportCommand(ILog log, TFClientRoundTimers timers)
{
    public void Execute() => log.Msg(timers.Report() + "\n");
}
