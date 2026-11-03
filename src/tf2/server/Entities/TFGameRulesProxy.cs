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
/// data table to the clients (<c>DT_TFGameRulesProxy</c>, L1557-1582) and
/// keeps the rest of its datadesc (goal strings, VO, the remaining keyfields)
/// for later ports — see the deferred table. The <c>ctf_overtime</c>
/// keyfield is read: Activate pushes it to the rules
/// (<see cref="ICtfRules.SetOvertimeAllowedForCtf"/>, L1904), as stock's
/// does.
/// </summary>
[ScriptedEntity(TFGameRulesProxy.MapClass)]
public sealed class TFGameRulesProxy(IKothRoundState koth, ITeamRespawnWaves waves, IEngineEntityRender render,
                                     ICtfRules ctf, ITeamRoundControl rounds, IFlagCarrierEffects effects)
    : ScriptedEntity
{
    public const string MapClass = "tf_gamerules";

    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public TFGameRulesProxyVars Vars = new();

    /// <summary>Stock's <c>CGameRulesProxy::UpdateTransmitState</c>: always sent (gamerules.h:101-102).</summary>
    public override void OnSpawn() => render.SetTransmitAlways(Handle, true);

    /// <summary>
    /// Stock <c>CTFGameRulesProxy::Activate</c>'s rules push (L1904): the
    /// map's <c>ctf_overtime</c>, ctor-defaulted on (L1643) when the key is
    /// absent.
    /// </summary>
    public override void OnServerActivate() => ctf.SetOvertimeAllowedForCtf(Vars.OvertimeAllowedForCtf);

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
