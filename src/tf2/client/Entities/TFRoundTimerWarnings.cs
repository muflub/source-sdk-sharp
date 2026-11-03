//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Sounds;

using TF2.Client.Player;

namespace TF2.Client.Entities;

/// <summary>
/// The countdown half of <c>team_round_timer</c> as the client hears it
/// (teamplay_round_timer.cpp): the entity think (<c>ClientThink</c> :469-519)
/// plays each per-second warning once as the remaining time crosses its
/// trigger (:479-518), with the setup set's names during setup and the normal
/// set's afterwards (<c>GetTimeWarningSound</c> :555-694), and the round-start
/// bell rings once as setup hands over to normal (:545-549). The pending flags
/// follow <c>CalculateOutputMessages</c> (:435-461): fresh for a new or
/// re-set timer, the client adding its two-second cushion over ten seconds so
/// a late packet does not skip a warning (:439-447). <c>SendTimeWarning</c>'s
/// gates (:708) answer through the client's mirrors: only the elected timer
/// plays (the HUD-index check, :763, through <see cref="TFClientRoundTimers"/>'s
/// election), and nothing plays paused, with the countdown turned off, during
/// waiting-for-players, or with no local player. Commentary mode has no answer
/// in this build, so its arm is absent; the tournament stopwatch, KOTH and
/// Merasmus arms wait on their deferred rows. Stock plays through
/// <c>pPlayer-&gt;EmitSound</c> (:777) — the local player's ears, which here is
/// <see cref="IClientEntitySounds.EmitFrom"/> from no entity, as the contested
/// announcer does.
/// </summary>
[SideService(Side.Client, As = [typeof(IClientFrameThink)])]
public sealed class TFRoundTimerWarnings(ITFRoundTimers timers, IGameClock clock,
                                         TFClientPlayerResources resources, IEngineHud hud,
                                         IClientEntitySounds sounds) : IClientFrameThink, IOrderedHook
{
    /// <summary>The rules' client half sits in the rules band (+100..+199).</summary>
    public const int Order = 1_000_000 - 1_000 + 190;

    public int HookOrder => Order;

    // Stock's trigger seconds, in the order its else-if chain checks them
    // (ClientThink :479-518).
    private readonly float[] _triggers = [61f, 31f, 11f, 6f, 5f, 4f, 3f, 2f];

    // CalculateOutputMessages' pending-flag floors (:450-461).
    private readonly float[] _floors = [60f, 30f, 10f, 5f, 4f, 3f, 2f, 1f];

    // ROUND_TIMER_* (:29-36) and ROUND_SETUP_* (:38-45), and the bell (:55).
    private readonly string[] _normalNames =
    [
        "Announcer.RoundEnds60seconds", "Announcer.RoundEnds30seconds",
        "Announcer.RoundEnds10seconds", "Announcer.RoundEnds5seconds",
        "Announcer.RoundEnds4seconds", "Announcer.RoundEnds3seconds",
        "Announcer.RoundEnds2seconds", "Announcer.RoundEnds1seconds",
    ];

    private readonly string[] _setupNames =
    [
        "Announcer.RoundBegins60Seconds", "Announcer.RoundBegins30Seconds",
        "Announcer.RoundBegins10Seconds", "Announcer.RoundBegins5Seconds",
        "Announcer.RoundBegins4Seconds", "Announcer.RoundBegins3Seconds",
        "Announcer.RoundBegins2Seconds", "Announcer.RoundBegins1Seconds",
    ];

    /// <summary><c>ROUND_START_BELL</c> (:55).</summary>
    public const string RoundStartBell = "Ambient.Siren";

    private readonly Dictionary<ITFRoundTimer, WarnState> _states = new();

    /// <summary>One timer's pending warnings and last seen state word.</summary>
    private sealed record WarnState(int Length, bool PrevSetup, bool[] Flags);

    public void ClientThink(float frameTime)
    {
        foreach (ITFRoundTimer timer in timers.All)
        {
            WarnState state = Track(timer);

            // OnDataChanged's setup-to-normal flip plays the bell (:545-549);
            // the state pair is kept every think, gated or not, as stock's
            // OnPreDataChanged keeps it (:524-530).
            if (state.PrevSetup && !timer.InSetup)
                Play(timer, RoundStartBell);
            _states[timer] = state with { PrevSetup = timer.InSetup };

            // ClientThink's gates (:471-475).
            if (timer.IsDisabled || timer.IsPaused)
                continue;
            if (timer.IsStopWatch && timer.InCaptureWatchState)
                continue;

            float time = timer.TimeRemaining(clock.Now);
            for (int i = 0; i < _triggers.Length; i++)
            {
                if (time <= _triggers[i] && state.Flags[i])
                {
                    state.Flags[i] = false;   // stock clears the flag before playing
                    Play(timer, timer.InSetup ? _setupNames[i] : _normalNames[i]);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Stock's per-timer message state: fresh pending flags whenever the timer
    /// is new or its length changed (<c>OnDataChanged</c> :539-543 calls
    /// <c>CalculateOutputMessages</c>, which reads the remaining time plus the
    /// client's two-second cushion over ten seconds, :437-461). The length is
    /// the one the HUD reads (<c>GetTimerMaxLength</c> :417-430): it is the
    /// setup length in setup and the timer's length otherwise, so any real
    /// change to the clock shows up in it.
    /// </summary>
    private WarnState Track(ITFRoundTimer timer)
    {
        if (_states.TryGetValue(timer, out WarnState? seen) && seen.Length == timer.TimerMaxLength)
            return seen;

        float time = timer.TimeRemaining(clock.Now);
        if (time > 10f)
            time += 2f;

        var flags = new bool[_floors.Length];
        for (int i = 0; i < _floors.Length; i++)
            flags[i] = time >= _floors[i];

        // The setup-to-normal pair survives a recompute: stock's
        // OnPreDataChanged (:524-530) saves it before every packet, whatever
        // else the update changes.
        var state = new WarnState(timer.TimerMaxLength, seen?.PrevSetup ?? timer.InSetup, flags);
        _states[timer] = state;
        return state;
    }

    /// <summary>
    /// Stock <c>SendTimeWarning</c>' gates (:708): nothing plays while paused,
    /// when the countdown is turned off, during waiting-for-players, or for a
    /// client with no local player and no objective resource; the elected HUD
    /// timer answers for the index check (:763-766). The tournament and KOTH
    /// arms stay on their deferred row. Stock's sound reaches the local
    /// player's ears through the client's emitter, from no entity.
    /// </summary>
    private void Play(ITFRoundTimer timer, string sound)
    {
        if (timer.IsPaused || !timer.AutoCountdown)
            return;
        if ((resources.Live?.State.WaitingForPlayers ?? 0) != 0)
            return;
        if (hud.LocalPlayerIndex <= 0)
            return;
        if (!ReferenceEquals(timers.Active, timer))
            return;

        sounds.EmitFrom(EntityHandle.None, sound);
    }
}
