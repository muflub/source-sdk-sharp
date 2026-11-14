//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Ctf;
using TF2.Server.Rules;
using TF2.Shared;

namespace TF2.Server.Entities;

/// <summary>The map keys of <see cref="TFGameRulesProxy"/>.</summary>
[ScriptVars]
public partial class TFGameRulesProxyVars
{
    /// <summary>Stock's <c>m_bOvertimeAllowedForCTF</c> (tf_gamerules.cpp:1595): while
    /// it stands, a flag away from home holds the round timers (:14022). The
    /// proxy ctor's default is on (:1643).</summary>
    [VarName("ctf_overtime")] public bool OvertimeAllowedForCtf { get; set; } = true;

    /// <summary>Stock's <c>m_nHudType</c> (:1642): the <c>hud_type</c> keyfield
    /// (<c>FIELD_INTEGER</c>, :1590), ctor-defaulted <c>TF_HUDTYPE_UNDEFINED</c>
    /// (0, tf_shareddefs.h:306). Activate pushes it only when named (:1896-1899).</summary>
    [VarName("hud_type")] public int HudType { get; set; }

    /// <summary>Stock's <c>m_bRopesHolidayLightsAllowed</c> (:1644): the
    /// <c>ropes_holiday_lights_allowed</c> keyfield (:1596), ctor-defaulted on;
    /// Activate pushes it always (:1906).</summary>
    [VarName("ropes_holiday_lights_allowed")] public bool HolidayLightsAllowed { get; set; } = true;
}

/// <summary>
/// <c>tf_gamerules</c> (<c>CTFGameRulesProxy</c>, shared/tf/tf_gamerules.cpp:1556-1764):
/// the entity the maps' I/O targets to speak to the rules — the stock koth
/// maps' <c>trigger_capture_area</c> fires its <c>OnCapTeam1</c> as
/// <c>tf_gamerules,SetRedKothClockActive</c> and the matching respawn-wave
/// word (koth_viaduct). This port carries the datadesc's inputs as maps fire
/// them: the koth clock pair (L1666-1716 — pause the other team's timer,
/// resume the named team's, through the same <see cref="IKothRoundState"/>
/// pair <see cref="KothLogic"/> created), the four respawn-wave inputs
/// (L1737-1764), routed to the wave state the rules already hold
/// (<see cref="ITeamRespawnWaves"/>), and the stalemate-at-timelimit word
/// (stock's <c>CTeamplayRoundBasedRulesProxy</c> base,
/// teamplayroundbased_gamerules.cpp:175-185, into <see cref="ITeamRoundControl"/>).
/// Stock's proxy also sends the rules'
/// data table to the clients (<c>DT_TFGameRulesProxy</c>, L1557-1582); the rest
/// of the datadesc lands here as maps fire it (.scratch counts, deferred.tsv):
/// the goal-string inputs are here
/// (<see cref="OnSetRedTeamGoalString"/>, through <see cref="ITeamResource"/>),
/// and so the role pair (<see cref="OnSetRedTeamRole"/>, through
/// <see cref="ITFTeamRoles"/>). The <c>ctf_overtime</c>
/// keyfield is read: Activate pushes it to the rules
/// (<see cref="ICtfRules.SetOvertimeAllowedForCtf"/>, L1904), as stock's
/// does. The VO trio lands as measured on stock maps of the played modes
/// (.scratch counts, 2026-11-10): <c>PlayVO</c> broadcasts to every team, the
/// named-team pair to its own (<see cref="IRoundAnnouncer.Broadcast"/>, stock's
/// <c>BroadcastSound</c> arms :1809-1840), <c>SetRoundRespawnFreezeEnabled</c>
/// writes the replicated convar (:1867-1870), and the two small setters
/// (<c>SetMapForcedTruceDuringBossFight</c>, <c>SetCustomUpgradesFile</c>,
/// :1875-1881, :1856-1862) store through <see cref="ICtfRules"/>.
/// <c>HandleMapEvent</c> and the observer keyfield stay deferred: no stock map
/// of the played modes fires them (counted 2026-11-10), and both are gated on
/// modes and an observer surface this build does not carry.
/// </summary>
[ScriptedEntity(TFGameRulesProxy.MapClass)]
public sealed class TFGameRulesProxy(IKothRoundState koth, ITeamRespawnWaves waves, IEngineEntityRender render,
                                     ICtfRules ctf, ITeamRoundControl rounds, IFlagCarrierEffects effects,
                                     ITeamResource teamsTable, ITFTeamRoles roles,
                                     IRoundAnnouncer announcer, TFPlayerMovementRestartFreezeConVar restartFreeze)
    : ScriptedEntity
{
    public const string MapClass = "tf_gamerules";

    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public TFGameRulesProxyVars Vars = new();

    /// <summary>Stock's <c>CGameRulesProxy::UpdateTransmitState</c>: always sent (gamerules.h:101-102).</summary>
    public override void OnSpawn() => render.SetTransmitAlways(Handle, true);

    /// <summary>
    /// Stock <c>CTFGameRulesProxy::Activate</c>'s rules pushes: the hud type,
    /// gated on it being named (L1896-1899), then <c>ctf_overtime</c> (L1904)
    /// and the holiday lights (L1906) always — each ctor-defaulted (:1642-1644)
    /// when the map's key is absent.
    /// </summary>
    public override void OnServerActivate()
    {
        if (Vars.HudType != 0)
            ctf.SetHudType(Vars.HudType);
        ctf.SetOvertimeAllowedForCtf(Vars.OvertimeAllowedForCtf);
        ctf.SetHolidayLightsAllowed(Vars.HolidayLightsAllowed);
    }

    /// <summary>Stock <c>InputSetStalemateOnTimelimit</c> (teamplayroundbased_gamerules.cpp:175-185).</summary>
    [ScriptedEvent] private void OnSetStalemateOnTimelimit(float enabled)
        => rounds.SetStalemateOnTimelimit(enabled != 0);

    /// <summary>Stock <c>InputSetCTFCaptureBonusTime</c> (tf_gamerules.cpp:1688-1694).</summary>
    [ScriptedEvent] private void OnSetCTFCaptureBonusTime(float seconds)
        => effects.SetCaptureBonusTime(seconds);

    /// <summary>
    /// Stock <c>InputSetRedKothClockActive</c> (L1666-1683): the blue team's
    /// koth timer waits, the red team's runs — each arm null-guarded, and the
    /// whole input gated only on the rules standing.
    /// </summary>
    [ScriptedEvent] private void OnSetRedKothClockActive()
    {
        koth.KothTeamTimer(TFTeam.Blue)?.PauseTimer();
        koth.KothTeamTimer(TFTeam.Red)?.ResumeTimer();
    }

    /// <summary>Stock <c>InputSetBlueKothClockActive</c> (L1699-1716): the mirror.</summary>
    [ScriptedEvent] private void OnSetBlueKothClockActive()
    {
        koth.KothTeamTimer(TFTeam.Blue)?.ResumeTimer();
        koth.KothTeamTimer(TFTeam.Red)?.PauseTimer();
    }

    /// <summary>Stock <c>InputSetRedTeamRespawnWaveTime</c> (L1737-1740).</summary>
    [ScriptedEvent] private void OnSetRedTeamRespawnWaveTime(float seconds)
        => waves.SetTeamRespawnWaveTime(TFTeam.Red, seconds);

    /// <summary>Stock <c>InputSetBlueTeamRespawnWaveTime</c> (L1745-1748).</summary>
    [ScriptedEvent] private void OnSetBlueTeamRespawnWaveTime(float seconds)
        => waves.SetTeamRespawnWaveTime(TFTeam.Blue, seconds);

    /// <summary>Stock <c>InputAddRedTeamRespawnWaveTime</c> (L1753-1756).</summary>
    [ScriptedEvent] private void OnAddRedTeamRespawnWaveTime(float seconds)
        => waves.AddTeamRespawnWaveTime(TFTeam.Red, seconds);

    /// <summary>Stock <c>InputAddBlueTeamRespawnWaveTime</c> (L1761-1764).</summary>
    [ScriptedEvent] private void OnAddBlueTeamRespawnWaveTime(float seconds)
        => waves.AddTeamRespawnWaveTime(TFTeam.Blue, seconds);

    /// <summary>Stock <c>InputSetRedTeamGoalString</c> (L1769-1774): the map's
    /// goal line for the red HUD panel; an empty parameter clears it.</summary>
    [ScriptedEvent] private void OnSetRedTeamGoalString(string goal)
        => teamsTable.SetGoal((int)TFTeam.Red, goal);

    /// <summary>Stock <c>InputSetBlueTeamGoalString</c> (L1775-1780).</summary>
    [ScriptedEvent] private void OnSetBlueTeamGoalString(string goal)
        => teamsTable.SetGoal((int)TFTeam.Blue, goal);

    /// <summary>Stock <c>InputSetRedTeamRole</c> (L1605, handler L1785-1792): the map's
    /// integer names RED's role (<c>TEAM_ROLE_*</c>, tf_shareddefs.h:74-76).</summary>
    [ScriptedEvent] private void OnSetRedTeamRole(float role)
        => roles.SetRole(TFTeam.Red, (TFTeamRole)(int)role);

    /// <summary>Stock <c>InputSetBlueTeamRole</c> (L1606, handler L1797-1804): the blue mirror.</summary>
    [ScriptedEvent] private void OnSetBlueTeamRole(float role)
        => roles.SetRole(TFTeam.Blue, (TFTeamRole)(int)role);

    /// <summary>Stock <c>InputPlayVO</c> (:1833-1840): the VO line to every team
    /// (<c>BroadcastSound</c>'s filter-255 arm; the seam's Unassigned is everyone),
    /// stock's pointer guard on the string.</summary>
    [ScriptedEvent] private void OnPlayVO(string sound)
    {
        if (!string.IsNullOrEmpty(sound))
            announcer.Broadcast(TFTeam.Unassigned, sound);
    }

    /// <summary>Stock <c>InputPlayVORed</c> (:1809-1816): the line for RED only.</summary>
    [ScriptedEvent] private void OnPlayVORed(string sound)
    {
        if (!string.IsNullOrEmpty(sound))
            announcer.Broadcast(TFTeam.Red, sound);
    }

    /// <summary>Stock <c>InputPlayVOBlue</c> (:1821-1828): the BLUE mirror.</summary>
    [ScriptedEvent] private void OnPlayVOBlue(string sound)
    {
        if (!string.IsNullOrEmpty(sound))
            announcer.Broadcast(TFTeam.Blue, sound);
    }

    /// <summary>Stock <c>InputSetRoundRespawnFreezeEnabled</c> (:1867-1870): the
    /// input's bool writes the replicated <c>tf_player_movement_restart_freeze</c>.</summary>
    [ScriptedEvent] private void OnSetRoundRespawnFreezeEnabled(float enabled)
        => restartFreeze.SetValue(enabled != 0);

    /// <summary>Stock <c>InputSetMapForcedTruceDuringBossFight</c> (:1875-1881):
    /// gated on the rules standing, the input's bool forces the boss fight's truce.</summary>
    [ScriptedEvent] private void OnSetMapForcedTruceDuringBossFight(float enabled)
        => ctf.SetMapForcedTruceDuringBossFight(enabled != 0);

    /// <summary>Stock <c>InputSetCustomUpgradesFile</c> (:1856-1862): gated on the
    /// rules standing, the rules remember the map's upgrades file.</summary>
    [ScriptedEvent] private void OnSetCustomUpgradesFile(string file)
        => ctf.SetCustomUpgradesFile(file);
}

/// <summary>
/// Creates <c>tf_gamerules</c> as each level loads, stock's
/// <c>CTFGameRules::CreateStandardEntities</c> arm (shared/tf/tf_gamerules.cpp:12083-12085):
/// the proxy the rules spawn themselves, named <c>tf_gamerules</c> so the
/// maps' outputs find it. The map's own block, once the class is registered,
/// is the map author's second instance — stock has both too.
/// </summary>
[SideService(Side.Server)]
public sealed class TFGameRulesProxySpawner(IEngineEntities entities, EntityDirectory live, ILog log)
    : IGameLifecycle
{
    /// <summary>The rules' band (TeamRoundOrders), the +150 slot.</summary>
    public int LifecycleOrder => 1_000_000 - 1_000 + 150;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        var handle = entities.Create(TFGameRulesProxy.MapClass);
        if (handle == EntityHandle.None)
        {
            log.Warning($"[TF2/Rules] could not create {TFGameRulesProxy.MapClass}; the maps' rules outputs go nowhere\n");
            return;
        }

        entities.Spawn(handle);

        // Stock's SetName after the create (tf_gamerules.cpp:12085): the name
        // the maps' outputs are wired to, on the directory they resolve
        // against — the CoreRoundWorld pattern for a spawned entity.
        live.SetName(handle, TFGameRulesProxy.MapClass);
        log.Msg($"[TF2/Rules] {TFGameRulesProxy.MapClass} up\n");
    }
}
