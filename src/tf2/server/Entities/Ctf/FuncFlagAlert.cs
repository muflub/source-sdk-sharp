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

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// func_flag_alert: a volume that sounds one alert per team when a carrier of
/// the other team's flag enters it (stock's <c>CFuncFlagAlertZone</c>,
/// server/tf/func_flag_alert.cpp). The alert is the
/// <c>Announcer.SecurityAlert</c> broadcast to the carrier's opposite team and
/// one of the two team outputs; each team's repeat is held off by its own
/// <c>alert_delay</c> clock.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncFlagAlert(IGameClock clock, ICtfPlayers players, ICtfRules rules, IRoundAnnouncer announcer)
    : TriggerVolume(clock)
{
    public const string MapClass = "func_flag_alert";

    public readonly Output OnTriggeredByTeam1 = new();
    public readonly Output OnTriggeredByTeam2 = new();

    [ScriptVars] public FuncFlagAlertVars ZoneVars = new();

    // m_flNextAlertTime[TF_TEAM_COUNT]: one clock per team, so red's alert
    // never holds blue's back.
    private readonly float[] _nextAlertTime = new float[(int)TFTeam.Count];

    /// <summary>The zone's team (CBaseEntity's TeamNum).</summary>
    public TFTeam Team => (TFTeam)ZoneVars.TeamNum;

    public bool PlaysSound => ZoneVars.PlaySound;

    public int AlertDelay => ZoneVars.AlertDelay;

    /// <summary>SF_TRIGGER_ALLOW_ALL: the spawnflags name no kind; the map's filter decides.</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.None;

    protected override void Fire(EntityHandle toucher) { }

    public override void OnTouch(EntityHandle toucher)
    {
        // Stock speaks first and hands to the base last: the alert fires even
        // where the base's own outputs would not (func_flag_alert.cpp:56-93).
        AlertTouch(toucher);

        // BaseClass::StartTouch.
        base.OnTouch(toucher);
    }

    private void AlertTouch(EntityHandle other)
    {
        // Is the zone enabled?
        if (Disabled)
            return;

        if (!players.IsPlayer(other) || players.Team(other) == Team)
            return;

        // HasTheFlag: carrying the capture item, captured or not.
        if (rules.CarriedBy(other) is null)
            return;

        TFTeam team = players.Team(other);

        if (Clock.Now <= _nextAlertTime[(int)team])
            return;

        if (PlaysSound)
        {
            TFTeam broadcastTeam = team == TFTeam.Red ? TFTeam.Blue : TFTeam.Red;
            announcer.Broadcast(broadcastTeam, "Announcer.SecurityAlert");
        }

        switch (team)
        {
            // FireOutput( this, this ): the zone names itself, the carrier does
            // not appear (func_flag_alert.cpp:77-82).
            case TFTeam.Red: OnTriggeredByTeam1.Fire(Handle); break;
            case TFTeam.Blue: OnTriggeredByTeam2.Fire(Handle); break;
        }

        _nextAlertTime[(int)team] = Clock.Now + AlertDelay;
    }
}

/// <summary>The map keys <see cref="FuncFlagAlert"/> declares (func_flag_alert.cpp:18-26).</summary>
[ScriptVars]
public partial class FuncFlagAlertVars
{
    [VarName("playsound")] public bool PlaySound { get; set; } = true;

    [VarName("alert_delay")] public int AlertDelay { get; set; } = 10;

    [VarName("TeamNum")] public int TeamNum { get; set; }
}
