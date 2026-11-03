//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Rules;

/// <summary>
/// What a <c>team_round_timer</c> asks of the round machine and tells it:
/// stock's timer calls <c>TeamplayRoundBasedRules()-&gt;SetSetup</c>,
/// <c>SetOvertime</c>, <c>TimerMayExpire</c>, <c>RoundHasBeenWon</c> and
/// <c>InStalemate</c>. The round flow is its one implementation.
/// </summary>
public interface IRoundTimerHost : ITeamRoundState
{
    /// <summary>Stock's <c>m_bInOvertime</c>.</summary>
    bool InOvertime { get; }

    /// <summary>A HUD timer entered or left its setup period (stock <c>SetSetup</c>).</summary>
    void SetSetup(bool setup);

    /// <summary>A HUD timer reached zero and was held there, or let go (stock <c>SetOvertime</c>).</summary>
    void SetOvertime(bool overtime);

    /// <summary>Stock's <c>TimerMayExpire</c>: the active mode and every <see cref="ITimerExpiryVeto"/>.</summary>
    bool TimerMayExpire();
}

/// <summary>One round timer, as the timer set and the machine read it.</summary>
public interface ITeamRoundTimer
{
    EntityHandle Handle { get; }

    /// <summary>Stock <c>GetTimeRemaining</c>: seconds left, paused or not, never negative.</summary>
    float TimeRemaining { get; }

    bool ShowInHud { get; }

    bool IsDisabled { get; }

    /// <summary>Stock <c>StartPaused()</c> (teamplay_round_timer.h:36): the map's <c>start_paused</c> key.</summary>
    bool StartPaused { get; }

    /// <summary>Stock <c>ResumeTimer()</c> (teamplay_round_timer.cpp:1203): unpause at the seconds left.</summary>
    void ResumeTimer();

    /// <summary>Stock <c>SetShowInHud</c>: the flag alone, no re-election.</summary>
    void SetShowInHud(bool show);

    /// <summary>
    /// Stock's <c>ShowInHUD</c> input: off re-elects the HUD timer, on makes
    /// this one it and re-tells the machine whether it is in setup.
    /// </summary>
    void InputShowInHud(bool show);
}

/// <summary>
/// The round timers and the one the HUD shows. FOR OTHER LANES (2.4): the
/// objective resource networks <see cref="Active"/>'s entity index as stock's
/// <c>m_iTimerToShowInHUD</c>.
/// </summary>
public interface IRoundTimers
{
    /// <summary>The timer shown in the HUD (stock <c>GetActiveRoundTimer</c>), or null.</summary>
    ITeamRoundTimer? Active { get; }

    /// <summary>
    /// Stock <c>CTeamRoundTimer::SetActiveTimer</c>: <paramref name="newlyActive"/>
    /// becomes the HUD timer and every other enabled HUD timer is switched
    /// off; null elects the first other enabled HUD timer instead.
    /// </summary>
    void SetActive(ITeamRoundTimer? newlyActive);

    /// <summary>Stock <c>HideActiveTimer</c>: the HUD timer, if any, stops showing and is remembered.</summary>
    void HideActive();

    /// <summary>Stock <c>RestoreActiveTimer</c>: the remembered timer shows again.</summary>
    void RestoreActive();

    /// <summary>
    /// Stock's cancel arm (:962-969): the remembered hidden HUD timer resumes
    /// when it is not a start_paused one. Called while the hidden handle
    /// still stands -- the restore's clear drops it.
    /// </summary>
    void ResumeHiddenIfRunning();
}

/// <summary>
/// The timer set: stock's <c>SetActiveTimer</c> walk over every
/// <c>team_round_timer</c>, and the HUD choice it ends in, which stock kept
/// on the objective resource. Per level: a timer handle does not survive a
/// map change.
/// </summary>
[SideService(Side.Server, As = [typeof(IRoundTimers)])]
public sealed class RoundTimers(IEntityDirectory directory) : IRoundTimers, IGameLifecycle
{
    /// <summary>The classname every timer is found under.</summary>
    public const string ClassName = "team_round_timer";

    private ITeamRoundTimer? _active;
    private ITeamRoundTimer? _hidden;

    public ITeamRoundTimer? Active => _active;

    int IGameLifecycle.LifecycleOrder => TeamRoundOrders.RoundTimers;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        _active = null;
        _hidden = null;
    }

    public void SetActive(ITeamRoundTimer? newlyActive)
    {
        ITeamRoundTimer? chosen = newlyActive;

        // Ensure all other timers are off.
        foreach (var entity in directory.OfClass(ClassName))
        {
            if (entity is not ITeamRoundTimer timer || ReferenceEquals(timer, newlyActive))
                continue;

            if (!timer.IsDisabled && timer.ShowInHud)
            {
                if (chosen is not null)
                    timer.SetShowInHud(false);   // turn off all other hud timers
                else
                    chosen = timer;              // found a timer: use it
            }
        }

        _active = chosen;
    }

    public void HideActive()
    {
        // Stock asserts there is no remembered timer: it cannot restore two.
        _hidden = null;
        foreach (var entity in directory.OfClass(ClassName))
        {
            if (entity is ITeamRoundTimer { ShowInHud: true } timer)
            {
                _hidden = timer;
                timer.InputShowInHud(false);
            }
        }
    }

    public void RestoreActive()
    {
        if (_hidden is not { } timer)
            return;

        _hidden = null;
        timer.InputShowInHud(true);
    }

    public void ResumeHiddenIfRunning()
    {
        // Stock's cancel arm (:955-973): read the hidden timer HERE, before
        // SetInWaitingForPlayers' restore clears the handle, and resume it
        // unless the map starts it paused (:965). The restore's own show
        // still follows through RestoreActive.
        if (_hidden is { StartPaused: false } timer)
            timer.ResumeTimer();
    }
}
