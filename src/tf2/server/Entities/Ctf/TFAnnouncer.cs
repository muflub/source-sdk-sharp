//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Ctf;

namespace TF2.Server.Entities.Ctf;

/// <summary>The CTF flag's soundscripts (entity_capture_flag.h:37-47) — the CTF set the announcer plays.</summary>
public static class TFCtfSounds
{
    /// <summary>TF_CTF_ENEMY_STOLEN: the flag was taken, to the team that lost it.</summary>
    public const string EnemyStolen = "CaptureFlag.EnemyStolen";

    /// <summary>TF_CTF_ENEMY_DROPPED.</summary>
    public const string EnemyDropped = "CaptureFlag.EnemyDropped";

    /// <summary>TF_CTF_ENEMY_CAPTURED.</summary>
    public const string EnemyCaptured = "CaptureFlag.EnemyCaptured";

    /// <summary>TF_CTF_ENEMY_RETURNED.</summary>
    public const string EnemyReturned = "CaptureFlag.EnemyReturned";

    /// <summary>TF_CTF_TEAM_STOLEN: we took the enemy's flag, to the taking team.</summary>
    public const string TeamStolen = "CaptureFlag.TeamStolen";

    /// <summary>TF_CTF_TEAM_DROPPED.</summary>
    public const string TeamDropped = "CaptureFlag.TeamDropped";

    /// <summary>TF_CTF_TEAM_CAPTURED.</summary>
    public const string TeamCaptured = "CaptureFlag.TeamCaptured";

    /// <summary>TF_CTF_TEAM_RETURNED.</summary>
    public const string TeamReturned = "CaptureFlag.TeamReturned";
}

/// <summary>The attack/defend flag's soundscripts (entity_capture_flag.h:56-71) — the AD arms the announcer plays.</summary>
public static class TFAttackDefendSounds
{
    /// <summary>TF_AD_ENEMY_STOLEN: the flag was taken, to the team that lost it.</summary>
    public const string EnemyStolen = "AttackDefend.EnemyStolen";

    /// <summary>TF_AD_ENEMY_DROPPED.</summary>
    public const string EnemyDropped = "AttackDefend.EnemyDropped";

    /// <summary>TF_AD_ENEMY_CAPTURED.</summary>
    public const string EnemyCaptured = "AttackDefend.EnemyCaptured";

    /// <summary>TF_AD_ENEMY_RETURNED.</summary>
    public const string EnemyReturned = "AttackDefend.EnemyReturned";

    /// <summary>TF_AD_TEAM_STOLEN: we took the enemy's flag, to the taking team.</summary>
    public const string TeamStolen = "AttackDefend.TeamStolen";

    /// <summary>TF_AD_TEAM_DROPPED.</summary>
    public const string TeamDropped = "AttackDefend.TeamDropped";

    /// <summary>TF_AD_TEAM_CAPTURED.</summary>
    public const string TeamCaptured = "AttackDefend.TeamCaptured";

    /// <summary>TF_AD_TEAM_RETURNED.</summary>
    public const string TeamReturned = "AttackDefend.TeamReturned";

    /// <summary>TF_AD_CAPTURED_SOUND: the capture sting, to everyone.</summary>
    public const string Captured = "AttackDefend.Captured";
}

/// <summary>
/// The flag's per-team announcer (stock's <c>CCaptureFlag::PlaySound</c> +
/// <c>CTFGameRules::SendHudNotification</c> pairs, entity_capture_flag.cpp's
/// CTF arms and func_capture_zone.cpp's throttle): each team hears its own
/// sound and its own notice. The teams' names read from the listener's side:
/// the team that LOST the flag hears the "Enemy*" sound and the
/// YOUR_FLAG_* notice; the team that has it hears the "Team*" sound and the
/// ENEMY_FLAG_* notice, on a pick-up without the picker. A notice is stock's
/// <c>SendHudNotification</c>: nothing while waiting for players
/// (tf_gamerules.cpp:18656-18659) — the sound plays on.
/// </summary>
public interface ITFAnnouncer
{
    /// <summary>PickUp's CTF arm (entity_capture_flag.cpp:1259-1281).</summary>
    void Stolen(EntityHandle flag, TFTeam pickerTeam, EntityHandle picker);

    /// <summary>Capture's CTF arm under <c>bNotify</c> (entity_capture_flag.cpp:1501-1549).</summary>
    void Captured(EntityHandle flag, TFTeam capperTeam, EntityHandle capper);

    /// <summary>Drop's CTF arm under <c>bMessage</c> (entity_capture_flag.cpp:1947-1968).</summary>
    void Dropped(EntityHandle flag, TFTeam dropperTeam, EntityHandle dropper);

    /// <summary>ResetMessage's CTF arm (entity_capture_flag.cpp:912-930): the flag's own team hears the returned line.</summary>
    void Returned(EntityHandle flag, TFTeam flagTeam);

    /// <summary>The touching-enemy-cap notice, to the one player over a <c>CSingleUserRecipientFilter</c> — unreliable (func_capture_zone.cpp:256-262).</summary>
    void TouchingEnemyCap(EntityHandle player);

    /// <summary>PickUp's attack/defend arm (entity_capture_flag.cpp:1282-1305): the two sounds, no notices.</summary>
    void AttackDefendStolen(EntityHandle flag, TFTeam pickerTeam, EntityHandle picker);

    /// <summary>Capture's attack/defend arm (entity_capture_flag.cpp:1574-1609): the two sounds and the broadcast sting.</summary>
    void AttackDefendCaptured(EntityHandle flag, TFTeam capperTeam, EntityHandle capper);

    /// <summary>Drop's attack/defend arm under <c>bMessage</c> (entity_capture_flag.cpp:2002-2028): the two sounds, no notices.</summary>
    void AttackDefendDropped(EntityHandle flag, TFTeam dropperTeam, EntityHandle dropper);

    /// <summary>ResetMessage's attack/defend arm (entity_capture_flag.cpp:945-967): the flag's own team hears the returned line.</summary>
    void AttackDefendReturned(EntityHandle flag, TFTeam flagTeam);
}

/// <summary>The game's <see cref="ITFAnnouncer"/>: the team-filtered emits through
/// the sound seam and the HudNotify user message per team.</summary>
[SideService(Side.Server, As = [typeof(ITFAnnouncer)])]
public sealed class TFAnnouncer(IEngineSound sound, IEnginePlayers players, IEngineEntities entities,
                                IUserMessageSender messages, ICtfRules rules) : ITFAnnouncer
{
    private const int Red = (int)TFTeam.Red;
    private const int Blue = (int)TFTeam.Blue;

    public void Stolen(EntityHandle flag, TFTeam pickerTeam, EntityHandle picker)
    {
        foreach (int team in PlayTeams())
        {
            if (team != (int)pickerTeam)
            {
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.EnemyStolen);
                NotifyTeam(team, TFHudNotifyType.YourFlagTaken);
            }
            else
            {
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.TeamStolen);
                NotifyTeam(team, TFHudNotifyType.EnemyFlagTaken, picker);
            }
        }
    }

    public void Captured(EntityHandle flag, TFTeam capperTeam, EntityHandle capper)
    {
        foreach (int team in PlayTeams())
        {
            if (team != (int)capperTeam)
            {
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.EnemyCaptured);
                NotifyTeam(team, TFHudNotifyType.YourFlagCaptured);
            }
            else
            {
                // The powerup mode's TF_RUNE_INTEL_CAPTURED is not ported (no powerup mode).
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.TeamCaptured);
                NotifyTeam(team, TFHudNotifyType.EnemyFlagCaptured);
            }
        }
    }

    public void Dropped(EntityHandle flag, TFTeam dropperTeam, EntityHandle dropper)
    {
        foreach (int team in PlayTeams())
        {
            if (team != (int)dropperTeam)
            {
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.EnemyDropped);
                NotifyTeam(team, TFHudNotifyType.YourFlagDropped);
            }
            else
            {
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.TeamDropped);
                NotifyTeam(team, TFHudNotifyType.EnemyFlagDropped);
            }
        }
    }

    public void Returned(EntityHandle flag, TFTeam flagTeam)
    {
        foreach (int team in PlayTeams())
        {
            if (team == (int)flagTeam)
            {
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.EnemyReturned);
                NotifyTeam(team, TFHudNotifyType.YourFlagReturned);
            }
            else
            {
                sound.EmitSoundToTeam(team, flag, TFCtfSounds.TeamReturned);
                NotifyTeam(team, TFHudNotifyType.EnemyFlagReturned);
            }
        }
    }

    public void TouchingEnemyCap(EntityHandle player)
    {
        int slot = entities.GetIndex(player);
        if (slot < 1)
            return;

        SendNotice(new TFHudNotify { Type = (byte)TFHudNotifyType.TouchingEnemyCtfCap, ForceShow = false },
                   slot, reliable: false);
    }

    public void AttackDefendStolen(EntityHandle flag, TFTeam pickerTeam, EntityHandle picker)
    {
        // The attack/defend arms pair no notice with their sounds
        // (entity_capture_flag.cpp:1282-1305).
        foreach (int team in PlayTeams())
        {
            sound.EmitSoundToTeam(team, flag,
                team == (int)pickerTeam ? TFAttackDefendSounds.TeamStolen : TFAttackDefendSounds.EnemyStolen);
        }
    }

    public void AttackDefendCaptured(EntityHandle flag, TFTeam capperTeam, EntityHandle capper)
    {
        foreach (int team in PlayTeams())
        {
            sound.EmitSoundToTeam(team, flag,
                team == (int)capperTeam ? TFAttackDefendSounds.TeamCaptured : TFAttackDefendSounds.EnemyCaptured);

            // The sting stock sends over a CBroadcastRecipientFilter
            // (entity_capture_flag.cpp:1601-1603): each team's filter carries
            // it once, so every player hears it once.
            sound.EmitSoundToTeam(team, flag, TFAttackDefendSounds.Captured);
        }
    }

    public void AttackDefendDropped(EntityHandle flag, TFTeam dropperTeam, EntityHandle dropper)
    {
        foreach (int team in PlayTeams())
        {
            sound.EmitSoundToTeam(team, flag,
                team == (int)dropperTeam ? TFAttackDefendSounds.TeamDropped : TFAttackDefendSounds.EnemyDropped);
        }
    }

    public void AttackDefendReturned(EntityHandle flag, TFTeam flagTeam)
    {
        // The inverted pair, as the CTF return: the flag's own team hears the
        // "enemy returned" line (entity_capture_flag.cpp:945-967).
        foreach (int team in PlayTeams())
        {
            sound.EmitSoundToTeam(team, flag,
                team == (int)flagTeam ? TFAttackDefendSounds.EnemyReturned : TFAttackDefendSounds.TeamReturned);
        }
    }

    /// <summary>Stock's loop over TF_TEAM_RED up to TF_TEAM_COUNT: the two play teams.</summary>
    private static IEnumerable<int> PlayTeams()
    {
        yield return Red;
        yield return Blue;
    }

    /// <summary>
    /// <c>SendHudNotification( CTeamRecipientFilter( team, true ), type )</c>:
    /// one HudNotify per player of the team, reliably (the engine's
    /// per-slot filter; <paramref name="except"/> is the actor stock's
    /// <c>RemoveRecipient</c> names — the picker, and no one else).
    /// </summary>
    private void NotifyTeam(int team, TFHudNotifyType type, EntityHandle except = default)
    {
        var payload = new TFHudNotify { Type = (byte)type, ForceShow = false };

        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            EntityHandle player = players.GetHandle(slot);
            if (player == EntityHandle.None || player == except)
                continue;

            if (players.GetTeam(player) != team)
                continue;

            SendNotice(payload, slot, reliable: true);
        }
    }

    /// <summary>
    /// <c>CTFGameRules::SendHudNotification</c> (tf_gamerules.cpp:18656-18659):
    /// nothing is sent while waiting for players — every call here passes
    /// <c>bForceShow = false</c> — and the sound the arm paired it with plays on.
    /// </summary>
    private void SendNotice(TFHudNotify payload, int slot, bool reliable)
    {
        if (rules.InWaitingForPlayers)
            return;

        messages.Send(payload, slot, reliable);
    }
}
