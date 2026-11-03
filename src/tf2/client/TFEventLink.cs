using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Client;

/// <summary>
/// The TF2 client's one event adapter (SS0013's): stock's panels and modes
/// call <c>ListenForGameEvent</c> in their constructors, and the mod gate
/// routes that through this class rather than letting every service hold a
/// core event. The freeze panel's death is today's listener; the next one
/// registers here too.
/// </summary>
[SideService(Side.Client)]
public sealed class EventLink
{
    private readonly List<Action<PlayerDeathArgs>> _deathListeners = [];
    private readonly List<Action<PlayerHurtArgs>> _hurtListeners = [];
    private readonly List<Action<ControlPointTouchArgs>> _touchEnterListeners = [];
    private readonly List<Action<ControlPointTouchArgs>> _touchLeaveListeners = [];
    private readonly List<Action<TeamplayWinPanelArgs>> _winPanelListeners = [];
    private readonly List<Action<TeamplayRoundStartArgs>> _roundStartListeners = [];
    private readonly List<Action<BlastJumpArgs>> _blastJumpListeners = [];

    public EventLink(PlayerDeathEvent deaths, PlayerHurtEvent hurts,
                     ControlPointStartTouchEvent startTouch, ControlPointEndTouchEvent endTouch,
                     TeamplayWinPanelEvent winPanel, TeamplayRoundStartEvent roundStart,
                     RocketJumpEvent rocketJump = null!, StickyJumpEvent stickyJump = null!)
    {
        deaths.Fired += Dispatch;    // the engine's dispatch, as the core's KillFeed takes it
        hurts.Fired += Hurt;
        startTouch.Fired += TouchEnter;
        endTouch.Fired += TouchLeave;
        winPanel.Fired += WinPanel;
        roundStart.Fired += RoundStart;
        if (rocketJump is not null)
            rocketJump.Fired += BlastJump;
        if (stickyJump is not null)
            stickyJump.Fired += BlastJump;
    }

    /// <summary>Registers a death listener (stock's ListenForGameEvent, in
    /// the shape a service's constructor can call).</summary>
    public void OnDeath(Action<PlayerDeathArgs> listener) => _deathListeners.Add(listener);

    /// <summary>Registers a player_hurt listener, the account panel's event
    /// (tf_hud_account.cpp FireGameEvent).</summary>
    public void OnHurt(Action<PlayerHurtArgs> listener) => _hurtListeners.Add(listener);

    /// <summary>Registers a controlpoint_starttouch listener, the progress
    /// bar's enter arm (hud_controlpointicons.cpp FireGameEvent).</summary>
    public void OnTouchEnter(Action<ControlPointTouchArgs> listener) => _touchEnterListeners.Add(listener);

    /// <summary>Registers a controlpoint_endtouch listener, the progress
    /// bar's leave arm (hud_controlpointicons.cpp FireGameEvent).</summary>
    public void OnTouchLeave(Action<ControlPointTouchArgs> listener) => _touchLeaveListeners.Add(listener);

    /// <summary>Registers a teamplay_win_panel listener, the win panel's
    /// event (tf_hud_winpanel.cpp Init's ListenForGameEvent).</summary>
    public void OnWinPanel(Action<TeamplayWinPanelArgs> listener) => _winPanelListeners.Add(listener);

    /// <summary>Registers a teamplay_round_start listener, the panel's
    /// hide arm (tf_hud_winpanel.cpp:119-126).</summary>
    public void OnRoundStart(Action<TeamplayRoundStartArgs> listener) => _roundStartListeners.Add(listener);

    /// <summary>Registers a blast-jump listener for both events
    /// (<c>rocket_jump</c>, <c>sticky_jump</c>): the feet-smoke's gate, stock's
    /// <c>RocketJumped() || StickyJumped()</c> of tf_player.cpp:1509 — the two
    /// bits that fire an event, so the events alone are that gate.</summary>
    public void OnBlastJump(Action<BlastJumpArgs> listener) => _blastJumpListeners.Add(listener);

    private void Dispatch(PlayerDeathArgs death)
    {
        foreach (var listener in _deathListeners)
            listener(death);
    }

    private void Hurt(PlayerHurtArgs hurt)
    {
        foreach (var listener in _hurtListeners)
            listener(hurt);
    }

    private void TouchEnter(ControlPointTouchArgs touch)
    {
        foreach (var listener in _touchEnterListeners)
            listener(touch);
    }

    private void TouchLeave(ControlPointTouchArgs touch)
    {
        foreach (var listener in _touchLeaveListeners)
            listener(touch);
    }

    private void WinPanel(TeamplayWinPanelArgs win)
    {
        foreach (var listener in _winPanelListeners)
            listener(win);
    }

    private void RoundStart(TeamplayRoundStartArgs round)
    {
        foreach (var listener in _roundStartListeners)
            listener(round);
    }

    private void BlastJump(BlastJumpArgs jump)
    {
        foreach (var listener in _blastJumpListeners)
            listener(jump);
    }
}
