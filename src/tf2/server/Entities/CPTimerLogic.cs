//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Entities;

/// <summary>
/// <c>CTFGameRules::TimerMayExpire</c>'s <c>CCPTimerLogic</c> walk
/// (tf_gamerules.cpp:14033-14041): the round timer may not expire while any
/// <see cref="CPTimerLogic"/> says so — stock's <c>m_CPTimerEnts</c>, found by
/// classname at the rules' setup (L4442-4447), walked here through the entity
/// directory the way the train watchers' walk is walked.
/// </summary>
// A chain member: the registrar (TeamRoundRegistrar) names the
// ITimerExpiryVeto chain and the machine collects its members through
// ResolveAll, so this claims only its own type.
[SideService(Side.Server)]
public sealed class CPTimerLogicTimerVeto(IEntityDirectory directory) : ITimerExpiryVeto, IOrderedHook
{
    public int HookOrder => TeamRoundOrders.CPTimerLogicTimerVeto;

    public bool TimerMayExpire()
    {
        foreach (var timer in directory.OfClass(CPTimerLogic.MapClass).OfType<CPTimerLogic>())
        {
            if (!timer.TimerMayExpire())
                return false;
        }

        return true;
    }
}

/// <summary>
/// <c>tf_logic_cp_timer</c> (<c>CCPTimerLogic</c>, tf_gamerules.cpp:20002-20114):
/// the attack/defend round logic for one team and one control point. While that
/// team may take the point a countdown runs — starting with
/// <see cref="OnCountdownStart"/> and the point's objective-table timer time,
/// and ending (only once the point is fully owned: no capper) with
/// <see cref="OnCountdownEnd"/>, which a map wires to its round win. Until the
/// countdown is spent the entity holds the round timer open
/// (<see cref="TimerMayExpire"/>, walked by <see cref="CPTimerLogicTimerVeto"/>):
/// the defender's time only runs out when the attackers can no longer reach the
/// point.
///
/// <para>
/// The countdown is stock's <c>m_pointTimer</c>
/// (<c>CountdownTimer</c>: start, has-started, elapsed, remaining, invalidate),
/// and the point is (re-)found by name on the <c>RoundSpawn</c> input, stock
/// only knowing its point from then on. Stock's early return when the rules or
/// the objective table are absent has no managed arm: both are injected and
/// always there. Stock's 15/10/5-seconds-remaining arms clear their flags and
/// never fire the outputs of the same names (the outputs are defined at
/// L20010-20012 and dead in stock); they are ported as stock, quirk included.
/// </para>
/// </summary>
[ScriptedEntity(CPTimerLogic.MapClass)]
public sealed class CPTimerLogic(IControlPointRules rules, IObjectiveResource objectives,
                                 IObjectiveEntities entities, IGameClock clock, ITeamRoundState round,
                                 IEngineEntityRender render)
    : ScriptedEntity
{
    public const string MapClass = "tf_logic_cp_timer";

    /// <summary>Stock's <c>CP_TIMER_THINK</c> interval (0.15 s).</summary>
    public const float ThinkInterval = 0.15f;

    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public CPTimerLogicVars Vars = new();

    public readonly Output OnCountdownStart = new();
    public readonly Output OnCountdown15SecRemain = new();
    public readonly Output OnCountdown10SecRemain = new();
    public readonly Output OnCountdown5SecRemain = new();
    public readonly Output OnCountdownEnd = new();

    // m_pointTimer: only ever started, elapsed-checked, read for the remaining
    // time and invalidated, guarded by HasStarted — two fields carry it.
    private bool _timerStarted;
    private float _timerEndTime;

    private bool _fire15SecRemain = true;
    private bool _fire10SecRemain = true;
    private bool _fire5SecRemain = true;

    private TeamControlPoint? _controlPoint;

    /// <summary>
    /// Stock's constructor (L1805-1813): the countdown is not started, the
    /// flags wait for it, and the think begins 0.15 s in. Stock's
    /// <c>UpdateTransmitState</c> (L1814-1817) sends the entity everywhere.
    /// </summary>
    public override void OnSpawn()
    {
        render.SetTransmitAlways(Handle, true);
        SetNextThink(ThinkInterval);
    }

    /// <summary>
    /// Stock <c>TimerMayExpire</c> (L20037-20046): with a point, this team may
    /// not see the timer expire while it can still capture that point.
    /// </summary>
    public bool TimerMayExpire()
    {
        if (_controlPoint is { } point && rules.TeamMayCapturePoint(Vars.TeamNumber, point.PointIndex))
            return false;

        return true;
    }

    /// <summary>
    /// Stock <c>InputRoundSpawn</c> (L20021-20032): the points are recreated
    /// over round restarts, so every round's spawn re-finds this entity's
    /// point by name — and warns when the map named one that is not there.
    /// </summary>
    [ScriptedEvent("RoundSpawn")]
    public void OnRoundSpawn()
    {
        if (Vars.ControlPoint.Length > 0)
        {
            // We need to re-find our control point, because they're recreated
            // over round restarts.
            _controlPoint = entities.FindByName<TeamControlPoint>(Vars.ControlPoint);
            if (_controlPoint is null)
                Log.Warning($"{MapClass} failed to find control point named '{Vars.ControlPoint}'\n");
        }
    }

    /// <summary>Stock <c>Think</c> (L20051-20114), every 0.15 s.</summary>
    public override void OnThink()
    {
        // Stock's `!TFGameRules() || !ObjectiveResource()` early return has no
        // arm here: both are injected and always present.

        if (round.State == RoundState.TeamWin)
        {
            // game has already been won, our job is done. Stock invalidates
            // and re-thinks, then runs on through the block below (no return);
            // ported as stock.
            _timerStarted = false;
        }

        if (_controlPoint is { } point)
        {
            if (rules.TeamMayCapturePoint(Vars.TeamNumber, point.PointIndex))
            {
                if (!_timerStarted)
                {
                    _timerStarted = true;
                    _timerEndTime = clock.Now + Vars.TimerLength;
                    OnCountdownStart.Fire(Handle);

                    objectives.SetCPTimerTime(point.PointIndex, clock.Now + Vars.TimerLength);
                }
                else if (clock.Now > _timerEndTime)
                {
                    // the point must be fully owned by the owner before we reset
                    if (objectives.GetCappingTeam(point.PointIndex) == (int)TFTeam.Unassigned)
                    {
                        _timerStarted = false;
                        OnCountdownEnd.Fire(Handle);
                        _fire15SecRemain = _fire10SecRemain = _fire5SecRemain = true;

                        objectives.SetCPTimerTime(point.PointIndex, -1f);
                    }
                }
                else
                {
                    float flRemainingTime = Math.Max(0f, _timerEndTime - clock.Now);

                    // Stock clears these three flags and never fires the
                    // outputs of the same names; ported as stock, quirk kept.
                    if (flRemainingTime <= 15f && _fire15SecRemain)
                    {
                        _fire15SecRemain = false;
                    }
                    else if (flRemainingTime <= 10f && _fire10SecRemain)
                    {
                        _fire10SecRemain = false;
                    }
                    else if (flRemainingTime <= 5f && _fire5SecRemain)
                    {
                        _fire5SecRemain = false;
                    }
                }
            }
            else
            {
                _timerStarted = false;
            }
        }

        SetNextThink(ThinkInterval);
    }
}

/// <summary>The map keys <see cref="CPTimerLogic"/> reads (stock's <c>DEFINE_KEYFIELD</c>s, their defaults the constructor's).</summary>
[ScriptVars]
public partial class CPTimerLogicVars
{
    /// <summary><c>m_iszControlPointName</c>: the point this countdown guards.</summary>
    [VarName("controlpoint")] public string ControlPoint { get; set; } = string.Empty;

    /// <summary><c>m_nTimerLength</c>: the countdown's length; stock's constructor starts it at 60.</summary>
    [VarName("timer_length")] public int TimerLength { get; set; } = 60;

    /// <summary><c>m_nTimerTeam</c>: the team whose capture of the point the countdown counts for; stock's member default is BLU.</summary>
    [VarName("team_number")] public int TeamNumber { get; set; } = (int)TFTeam.Blue;
}
