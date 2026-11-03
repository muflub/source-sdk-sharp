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

namespace TF2.Server.Entities;

/// <summary>
/// The rules' KOTH state, stock's <c>CTFGameRules</c> members behind
/// <c>IsInKothMode</c>, <c>SetKothTeamTimer</c> and
/// <c>GetRed/BlueKothRoundTimer</c>: the koth mode's flag and the pair of
/// per-team hold-the-point timers the <see cref="KothLogic"/> entity creates
/// for it.
/// </summary>
public interface IKothRoundState
{
    /// <summary>
    /// Stock's <c>IsInKothMode</c> — the flag <c>CTFGameRules::Activate</c>
    /// raises when the map holds a <c>tf_logic_koth</c>
    /// (shared/tf/tf_gamerules.cpp:4384-4388). Answered from the entity
    /// directory: the entity standing is exactly what the Activate walk
    /// found at map load.
    /// </summary>
    bool InKothMode { get; }

    /// <summary>Stock's <c>SetKothTeamTimer</c>: the team's hold-the-point timer.</summary>
    void SetKothTeamTimer(TFTeam team, TeamRoundTimer? timer);

    /// <summary>
    /// Stock's <c>GetRedKothRoundTimer</c>/<c>GetBlueKothRoundTimer</c>, or
    /// null: a timer gone from the world reads gone here, as a
    /// <c>CBaseHandle</c> does.
    /// </summary>
    TeamRoundTimer? KothTeamTimer(TFTeam team);

    /// <summary>
    /// Stock's koth arm of <c>CheckWinConditions</c>
    /// (server/team_control_point_master.cpp:679-700, mirrored in
    /// server/team_control_point_round.cpp:122-141): the owning team's win
    /// stands only once its hold-the-point timer has run out, and only while
    /// the round machine lets timers expire (<c>TimerMayExpire</c>) — during
    /// overtime the timer cannot end the round, so the win waits.
    /// </summary>
    bool WinMayStand(TFTeam winner);
}

/// <summary><see cref="IKothRoundState"/> over the entity directory (the stock rules' state).</summary>
[SideService(Side.Server, As = [typeof(IKothRoundState)])]
public sealed class KothRoundState(IEntityDirectory directory, IRoundTimerHost host) : IKothRoundState
{
    private TeamRoundTimer? _redTimer;
    private TeamRoundTimer? _blueTimer;

    public bool InKothMode => directory.OfClass(KothLogic.MapClass).Any();

    public void SetKothTeamTimer(TFTeam team, TeamRoundTimer? timer)
    {
        if (team == TFTeam.Red)
            _redTimer = timer;
        else if (team == TFTeam.Blue)
            _blueTimer = timer;
    }

    public TeamRoundTimer? KothTeamTimer(TFTeam team) =>
        Live(team == TFTeam.Red ? _redTimer : _blueTimer);

    public bool WinMayStand(TFTeam winner)
    {
        if (!InKothMode)
            return true;

        // Stock: pTimer = the winner's koth timer; if (pTimer) and its
        // GetTimeRemaining() > 0, or TimerMayExpire() == false, then bWinner
        // = false (team_control_point_master.cpp:679-700). No timer, no
        // suppression — stock's null-pTimer arm.
        var timer = KothTeamTimer(winner);
        return timer is null || (timer.TimeRemaining <= 0 && host.TimerMayExpire());
    }

    /// <summary>The timer while it still stands in the world; null once it is gone.</summary>
    private TeamRoundTimer? Live(TeamRoundTimer? timer) =>
        timer is not null && ReferenceEquals(directory.Get(timer.Handle), timer) ? timer : null;
}

/// <summary>
/// <c>tf_logic_koth</c> (<c>CKothLogic</c>, shared/tf/tf_gamerules.cpp:19863-20000):
/// the entity that says a control-point map is KOTH, and the one that builds
/// and drives the round around the single contested point.
///
/// <para>
/// Its <c>RoundSpawn</c> input (stock <c>InputRoundSpawn</c> L19880-19914)
/// creates the two team timers — blue first, then red, as stock does — under
/// the names <c>zz_blue_koth_timer</c> / <c>zz_red_koth_timer</c>, shows them
/// in the HUD, and starts each paused at <c>timer_length</c> (the stock
/// constructor's 180). Its <c>RoundActivate</c> input (L19919-19944) locks
/// every point of the first master for <c>unlock_point</c> seconds (the
/// stock constructor's 30): the round starts with the point unreachable and
/// the teams fighting for position until the delay runs out. The
/// <c>SetRedTimer</c>/<c>SetBlueTimer</c>/<c>AddRedTimer</c>/<c>AddBlueTimer</c>
/// inputs (L19949-20000) set and add a team's hold-the-point time, all four
/// gated on the koth flag as stock gates them.
/// </para>
///
/// <para>
/// Stock creates its timers with <c>CBaseEntity::Create</c>; the managed port
/// creates them through <c>IEngineEntities.Create</c> so the scripted
/// registry attaches the <see cref="TeamRoundTimer"/> half. Stock leaks the
/// old pair across round restarts (its <c>SetupOnRoundStart</c> nulls the
/// rules' pair at L4852-4853 but never the entity's, L4838-4863); this port
/// nulls the pair on each <c>RoundSpawn</c> before re-spawning it, so a
/// round whose spawn is refused waits on no stale timer. Stock's
/// <c>UpdateTransmitState</c> (tf_gamerules.h:1766) sends the entity with
/// <c>FL_EDICT_ALWAYS</c>.
/// </para>
/// </summary>
[ScriptedEntity(KothLogic.MapClass)]
public sealed class KothLogic(IEntityDirectory entities, EntityDirectory live, IEngineEntities world,
                              IKothRoundState koth, IObjectiveEntities objectiveEntities, IEngineEntityRender render)
    : ScriptedEntity
{
    public const string MapClass = "tf_logic_koth";

    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public KothLogicVars Vars = new();

    public override void OnSpawn() => render.SetTransmitAlways(Handle, true);

    /// <summary>
    /// Stock <c>InputRoundSpawn</c> (L19880-19914), gated on the koth flag:
    /// the two team timers, blue first then red.
    /// </summary>
    [ScriptedEvent("RoundSpawn")]
    public void OnRoundSpawn()
    {
        if (!koth.InKothMode)
            return;

        // Stock's SetupOnRoundStart nulls the rules' pair before the RoundSpawn
        // walk (tf_gamerules.cpp:4852-4853); the spawns below re-set both
        // halves, so a round whose spawn is refused waits on no stale timer.
        koth.SetKothTeamTimer(TFTeam.Blue, null);
        koth.SetKothTeamTimer(TFTeam.Red, null);
        SpawnTeamTimer(TFTeam.Blue, "zz_blue_koth_timer");
        SpawnTeamTimer(TFTeam.Red, "zz_red_koth_timer");
    }

    /// <summary>
    /// Stock <c>InputRoundActivate</c> (L19919-19944): every point of the
    /// first master is locked, and when the delay is on it unlocks itself
    /// after <c>unlock_point</c> seconds.
    /// </summary>
    [ScriptedEvent("RoundActivate")]
    public void OnRoundActivate()
    {
        if (!koth.InKothMode)
            return;

        // Stock: g_hControlPointMasters[0].
        if (objectiveEntities.Master is not { } master)
            return;

        foreach (var point in master.ControlPoints)
        {
            Input(point.Handle, "SetLocked", Vars.TimeToUnlockPoint);

            if (Vars.TimeToUnlockPoint > 0)
                Input(point.Handle, "SetUnlockTime", Vars.TimeToUnlockPoint);
        }
    }

    [ScriptedEvent] private void OnSetRedTimer(float seconds)
    {
        if (koth.InKothMode)
            koth.KothTeamTimer(TFTeam.Red)?.SetTimeRemaining((int)seconds);
    }

    [ScriptedEvent] private void OnSetBlueTimer(float seconds)
    {
        if (koth.InKothMode)
            koth.KothTeamTimer(TFTeam.Blue)?.SetTimeRemaining((int)seconds);
    }

    [ScriptedEvent] private void OnAddRedTimer(float seconds)
    {
        if (koth.InKothMode)
            koth.KothTeamTimer(TFTeam.Red)?.AddTimerSeconds((int)seconds);
    }

    [ScriptedEvent] private void OnAddBlueTimer(float seconds)
    {
        if (koth.InKothMode)
            koth.KothTeamTimer(TFTeam.Blue)?.AddTimerSeconds((int)seconds);
    }

    /// <summary>
    /// One stock <c>InputRoundSpawn</c> arm: create the timer, name it, hand
    /// it to the rules, show it in the HUD, and start it paused at the
    /// initial length — stock's <c>Create</c>/<c>SetKothTeamTimer</c>/
    /// <c>SetName</c>/<c>SetShowInHud</c>/<c>SetTime</c>/<c>Pause</c>. Stock
    /// also keeps the pair on the entity beside the rules' pair
    /// (<c>m_hRed/BlueTimer</c>); this port carries it once, in
    /// <see cref="IKothRoundState"/>, and lives-checks it as stock's handles
    /// do.
    /// </summary>
    private void SpawnTeamTimer(TFTeam team, string name)
    {
        var handle = world.Create(RoundTimers.ClassName);
        if (handle == EntityHandle.None)
            return;

        world.Spawn(handle);

        if (entities.Get(handle) is not TeamRoundTimer timer)
            return;

        // Stock's SetName after the create (L19887-19893): the name the map's
        // outputs and the directory's FindByName answer to, written through
        // the directory — the CoreRoundWorld pattern for a spawned entity.
        live.SetName(handle, name);

        koth.SetKothTeamTimer(team, timer);
        timer.SetShowInHud(true);
        Input(handle, "SetTime", Vars.TimerLength);
        Input(handle, "Pause", 0f);
    }

    /// <summary>
    /// An input on an entity this one spawns or drives, fired the way the
    /// timer's <c>SetTime</c>/<c>Pause</c> arms and the point's
    /// <c>SetLocked</c>/<c>SetUnlockTime</c> arms expect: through the
    /// registry's dispatch, which runs their <c>Publish()</c> for the HUD
    /// (the plain methods publish nothing). The same guarded dispatch as
    /// <c>CoreRoundWorld.Input</c>.
    /// </summary>
    private void Input(EntityHandle handle, string name, float value)
    {
        if (entities is ScriptedEntityRegistry registry)
            registry.Input(handle, name, string.Empty, value, EntityHandle.None);
    }
}

/// <summary>The map keys <see cref="KothLogic"/> reads (stock's <c>DEFINE_KEYFIELD</c>s, their defaults the constructor's — tf_gamerules.h:1773-1774).</summary>
[ScriptVars]
public partial class KothLogicVars
{
    /// <summary><c>m_nTimerInitialLength</c>: the team timers' starting length.</summary>
    [VarName("timer_length")] public int TimerLength { get; set; } = 180;

    /// <summary><c>m_nTimeToUnlockPoint</c>: how long the point stays locked at the round's start.</summary>
    [VarName("unlock_point")] public int TimeToUnlockPoint { get; set; } = 30;
}
