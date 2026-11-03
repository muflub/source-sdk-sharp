//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// func_capturezone: where a carrier scores the flag they carry (stock's
/// <c>CCaptureZone</c>, server/tf/func_capture_zone.cpp). Asked every frame
/// a player stands in it (stock's Touch, <see cref="ShimTouch"/>): a carrier
/// of the zone's own team captures, so long as flags may be capped and
/// <c>CanFlagBeCaptured</c> allows it.
///
/// <para>
/// Not ported (docs/plans/tf2/deferred.tsv): player destruction's think
/// (<c>PlayerDestructionThink</c>, the <c>_PD</c> outputs), and the holiday drops.
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncCaptureZone(IGameClock clock, ICtfRules rules, ICtfPlayers players, ICtfEvents events,
                                    IModeObjectives objectives, ITFAnnouncer announcer)
    : TriggerVolume(clock)
{
    public const string MapClass = "func_capturezone";

    public readonly Output OnCapture = new();
    public readonly Output OnCapTeam1 = new();
    public readonly Output OnCapTeam2 = new();

    [ScriptVars] public FuncCaptureZoneVars ZoneVars = new();

    private float _nextTouchingEnemyZoneWarning = -1f;

    /// <summary>The zone's team (CBaseEntity's TeamNum); unassigned takes every team's flags.</summary>
    public TFTeam Team => (TFTeam)ZoneVars.TeamNum;

    /// <summary>m_nCapturePoint: the zone's number, handed to the flag's Capture.</summary>
    public int CapturePoint => ZoneVars.CapturePoint;

    public bool IsDisabled => Disabled;

    /// <summary>Stock's Touch is every frame of the overlap, not the edges.</summary>
    public override bool TouchEveryTick => true;

    /// <summary>SF_TRIGGER_ALLOW_ALL: the spawnflags name no kind; only the map's filter decides.</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.None;

    protected override bool Passes(EntityHandle toucher) => PassesFilter(toucher);

    public override void OnSpawn()
    {
        base.OnSpawn();
        _nextTouchingEnemyZoneWarning = -1f;

        // The carried flag's arrow points here (stock's CarriedImage panel
        // pointed a second arrow at the C_CaptureZone entity); the centre is
        // static, so the spawn is the only publish.
        objectives.SetZoneOrigin((int)Team, Origin);
    }

    public override void OnTouch(EntityHandle toucher)
    {
        // The volume's own bookkeeping (who is inside, OnStartTouch) once, then the frame's touch.
        base.OnTouch(toucher);

        if (!Disabled && Passes(toucher))
            ShimTouch(toucher);
    }

    /// <summary>A capture zone acts in its touch; entering fires nothing more.</summary>
    protected override void Fire(EntityHandle toucher) { }

    /// <summary>ShimTouch (L216).</summary>
    public void ShimTouch(EntityHandle other)
    {
        // Is the zone enabled?
        if (IsDisabled)
            return;

        // Get the TF player.
        if (!players.IsPlayer(other) || !players.IsTFPlayer(other))
            return;

        // Check to see if the player has the capture flag.
        if (rules.CarriedBy(other) is not { } flag)
            return;

        // we have a special think that will handle the player destruction flags
        if (flag.Type == TFFlagType.PlayerDestruction)
            return;

        if (!flag.IsCaptured)
        {
            // does this capture point have a team number assigned?
            if (Team != TFTeam.Unassigned)
            {
                // Check to see if the capture zone team matches the player's team.
                TFTeam playerTeam = players.Team(other);
                if (playerTeam != TFTeam.Unassigned && playerTeam != Team)
                {
                    if (flag.Type == TFFlagType.Ctf && _nextTouchingEnemyZoneWarning < Clock.Now)
                    {
                        // HUD_NOTIFY_TOUCHING_ENEMY_CTF_CAP, to the player alone
                        // (func_capture_zone.cpp:256-262).
                        announcer.TouchingEnemyCap(other);
                        _nextTouchingEnemyZoneWarning = Clock.Now + 5;
                    }

                    return;
                }
            }
        }

        // in MvM, the "flag" is the bomb and is captured when the carrying bot deploys it
        if (rules.FlagsMayBeCapped)
            Capture(other);
    }

    /// <summary>Capture (L292).</summary>
    public void Capture(EntityHandle other)
    {
        // Check to see if the player has the capture flag and flag is allowed to be captured.
        if (rules.CarriedBy(other) is not { } flag || !rules.CanFlagBeCaptured(other))
            return;

        // we have a special think that will handle the player destruction flags
        if (flag.Type == TFFlagType.PlayerDestruction)
            return;

        if (flag.IsCaptured)
            return;

        TFTeam cappingTeam = players.Team(other);
        flag.Capture(other, CapturePoint);

        // Outputs
        OnCapture.Fire();

        switch (cappingTeam)
        {
            case TFTeam.Red: OnCapTeam1.Fire(); break;
            case TFTeam.Blue: OnCapTeam2.Fire(); break;
        }

        events.FlagCaptured(cappingTeam, rules.FlagCaptures(cappingTeam), players.UserId(other));
    }
}

/// <summary>The map keys <see cref="FuncCaptureZone"/> declares (func_capture_zone.cpp:22-27, CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class FuncCaptureZoneVars
{
    [VarName("CapturePoint")] public int CapturePoint { get; set; }
    [VarName("capture_delay")] public float CaptureDelay { get; set; } = 1.1f;
    [VarName("capture_delay_offset")] public float CaptureDelayOffset { get; set; } = 0.025f;
    [VarName("shouldBlock")] public bool ShouldBlock { get; set; } = true;
    [VarName("TeamNum")] public int TeamNum { get; set; }
}
