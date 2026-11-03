//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using TF2.Client.Entities;
using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Client.Ui;

/// <summary>
/// The cap teardrop's data: stock's <c>CControlPointProgressBar</c> kept by
/// the local player's touch events and answered its arms from the gamerules
/// and the shared (hud_controlpointicons.cpp:874-891, :1499-1646). The
/// touched point is the <c>m_iCurrentCP</c> the starttouch/endtouch pair
/// keep (the local player's entindex the events name); the bar arm is
/// <c>SetupForPoint</c>'s condition (:1519); the text is
/// <c>UpdateBarText</c>'s ladder (:1546-1646) with the player gates of
/// <c>CTFGameRules::PlayerMayCapturePoint</c> (tf_gamerules.cpp:16835-16948)
/// answered from the decoded carrier.
///
/// <para>
/// Divergence: the arena arm of the linear gate reads
/// <c>#Team_Capture_Linear</c> always (no arena flag client-side).
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFCapProgress)])]
public sealed class TFCapProgress : ITFCapProgress
{
    private readonly IEngineHud _screen;
    private readonly ITFPlayerData _data;
    private readonly ITFLocalPlayer _player;
    private readonly ITFObjectiveSource _objectives;
    private readonly ITFObjectiveSettings _settings;
    private readonly ITFRoundSetup _roundSetup;
    private readonly TFClientPlayerResources _resources;
    private readonly IGameClock _clock;

    /// <summary>Stock's m_iCurrentCP: -1 before any touch and after a leave.</summary>
    private int _touched = -1;

    public TFCapProgress(EventLink events, IEngineHud screen, ITFPlayerData data,
                         ITFLocalPlayer player, ITFObjectiveSource objectives,
                         ITFObjectiveSettings settings, ITFRoundSetup roundSetup,
                         TFClientPlayerResources resources, IGameClock clock)
    {
        (_screen, _data, _player, _objectives, _settings, _roundSetup, _resources, _clock) =
            (screen, data, player, objectives, settings, roundSetup, resources, clock);
        events.OnTouchEnter(TouchEnter);
        events.OnTouchLeave(TouchLeave);
    }

    public int Touched => _touched;

    /// <summary>SetupForPoint's bar arm (:1519): not blocked, a cap runs and
    /// it is the local team's.</summary>
    public bool ShowsBar(int point)
    {
        int capping = _objectives.CappingTeam(point);
        return !_objectives.CapIsBlocked(point)
               && capping != Unassigned && capping != _objectives.OwningTeam(point)
               && capping == _player.Team;
    }

    public float Progress(int point) => _objectives.CapProgress(point, _clock.Now);

    /// <summary>UpdateBarText's ladder (:1546-1646), in stock's order.</summary>
    public string Text(int point)
    {
        int playerTeam = _player.Team;
        int capping = _objectives.CappingTeam(point);

        if (!PointsMayBeCaptured)
            return "#Team_Capture_NotNow";

        if (_objectives.IsLocked(point))
            return "#Team_Capture_NotNow";

        if (_settings.BlockStyle == 1 && capping != Unassigned && capping != playerTeam)
        {
            if (_objectives.IsBlocked(point))
                return "#Team_Blocking_Capture";

            if (_objectives.OwningTeam(point) == Unassigned)
                return "#Team_Reverting_Capture";
        }

        if (_objectives.OwningTeam(point) == playerTeam)
        {
            // If the opponents can never recapture this point back, we use a
            // different string (:1585-1600).
            if (playerTeam != Unassigned && !_objectives.TeamCanCap(point, playerTeam == Red ? Blue : Red))
                return "#Team_Capture_Owned";

            return "#Team_Capture_OwnPoint";
        }

        if (!_objectives.TeamMayCap(point, playerTeam))
            return "#Team_Capture_Linear";

        if (CaptureDenied() is { } denied)
            return denied;

        // In Capstyle 1, more players simply cap faster, no required amounts
        // (:1623-1631).
        bool haveRequired = _settings.CapStyle == 1
                            || _objectives.NumPlayersInArea(point, playerTeam) >= _objectives.RequiredCappers(point, playerTeam);

        if (capping == playerTeam && haveRequired)
            return "#Team_Capture_Blocked";

        if (!_objectives.TeamCanCap(point, playerTeam))
            return "#Team_Cannot_Capture";

        return "#Team_Waiting_for_teammate";
    }

    /// <summary>The touch events, as stock's FireGameEvent keeps them
    /// (:874-891): only the local player's entindex moves the point.</summary>
    private void TouchEnter(ControlPointTouchArgs touch)
    {
        if (touch.Player == _screen.LocalPlayerIndex)
            _touched = touch.Area;
    }

    private void TouchLeave(ControlPointTouchArgs touch)
    {
        if (touch.Player == _screen.LocalPlayerIndex)
            _touched = -1;
    }

    /// <summary><c>CTeamplayRoundBasedRules::PointsMayBeCaptured</c>
    /// (teamplayroundbased_gamerules.h:192) over the mirror's round-state and
    /// waiting-for-players words, with <c>CTFGameRules</c>'s override's setup
    /// arm (tf_gamerules.cpp:22079): the elected timer inside the setup
    /// period stops the capture, <see cref="ITFRoundSetup"/>'s word.</summary>
    private bool PointsMayBeCaptured
        => _resources.Live is { } live
           && live.State.RoundState is (byte)RoundState.RoundRunning or (byte)RoundState.Stalemate
           && live.State.WaitingForPlayers == 0
           && !_roundSetup.InSetupWithRoundTimer;
    /// <summary><c>CTFGameRules::PlayerMayCapturePoint</c>'s player gates
    /// (tf_gamerules.cpp:16835-16948), the token each names; null when the
    /// player may capture. The MvM exemption is not a mode this client runs.</summary>
    private string? CaptureDenied()
    {
        if (Carrier is not { } state)
            return null;

        if (state.IsStealthed())
            return "#Cant_cap_stealthed";

        if (state.IsInvulnerable() || state.InCond(TFCond.Megaheal) || state.InCond(TFCond.Phase))
            return "#Cant_cap_invuln";

        // IsControlStunned (tf_player_shared.cpp:9952-9961) on the carrier's
        // words: the active entry behind the index, the stunned condition and
        // the controls bit, no expiry (TFMovementRules's reading).
        if (state.StunIndex >= 0
            && state.InCond(TFCond.Stunned)
            && ((TFStunFlags)state.StunFlags & TFStunFlags.Controls) != 0)
            return "#Cant_cap_stunned";

        if (state.InCond(TFCond.Disguised) && state.DisguiseTeam != _player.Team)
            return "#Cant_cap_disguised";

        return null;
    }

    /// <summary>The local player's decoded carrier (the same slot guard
    /// TFHudSpy keeps).</summary>
    private TFPlayerNetState? Carrier
        => _screen.LocalPlayerIndex > 0 ? _data.Networked(_screen.LocalPlayerIndex) : null;

    /// <summary>TEAM_UNASSIGNED: no team.</summary>
    private const int Unassigned = 0;

    /// <summary>TF_TEAM_RED.</summary>
    private const int Red = 2;

    /// <summary>TF_TEAM_BLUE.</summary>
    private const int Blue = 3;
}
