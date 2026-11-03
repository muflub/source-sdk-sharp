//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Combat;

/// <summary>
/// TF's <c>player_death</c> (<c>CTFGameRules::DeathNotice</c>,
/// <c>shared/tf/tf_gamerules.cpp:12814-13243</c>) as the core's
/// <see cref="IDeathNoticeHook"/>, from the killing hit the damage layer
/// recorded (<see cref="TFDamageRecords"/>):
///
/// <para>
/// — <c>attacker</c>: the death's scorer (<c>GetDeathScorer</c> :12761-12801),
///   which for a suicide is the most recent enemy damager within
///   <c>TF_TIME_SUICIDE_KILL_CREDIT</c> (10 s).
/// </para>
/// <para>
/// — <c>weapon</c> and <c>weaponid</c>: <c>GetKillingWeaponName</c>
///   (:12108-12685): the burning kinds name their weapon whatever the
///   attacker holds now, a scorer that inflicted the kill names its active
///   weapon (not for a suicide), else the inflictor's class; the
///   <c>tf_weapon_grenade_</c>, <c>tf_weapon_</c>, <c>NPC_</c> and
///   <c>func_</c> prefixes stripped (:12540-12551). A scattergun kill is
///   <c>scattergun</c>.
/// </para>
/// <para>
/// — <c>assister</c>: <c>GetAssister</c> (:12690-12736), here the second most
///   recent damager within <c>TF_TIME_ASSIST_KILL</c> (3 s) when it is not the
///   scorer, else -1; never for a suicide.
/// </para>
/// <para>
/// — <c>damagebits</c>, <c>customkill</c> and <c>crit_type</c>: the killing
///   hit's, after the modify rules (a crit's <c>DMG_CRITICAL</c>, a headshot's
///   custom kind); a self-inflicted death with none recorded is the suicide
///   custom kind, stock's CommitSuicide flag; <c>victim_entindex</c>,
///   <c>inflictor_entindex</c>,
///   <c>weapon_logclassname</c> (the same name: no item log names yet),
///   <c>silent_kill</c> false, <c>stun_flags</c> 0 and <c>death_flags</c>:
///   stock's domination and revenge (<see cref="TFDominations"/>, computed
///   before the kill is recorded, tf_gamerules.cpp:11500-11518).
/// </para>
///
/// <para>
/// Not yet: the econ item icon and log names, the kill streaks, the healer,
/// condition and sentry assists, feign death, first blood, the deflected
/// projectiles and the sentry levels (<c>docs/plans/tf2/deferred.tsv</c>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFDeathNotice(IEnginePlayers players, IEngineEntities entities, TFDamageRecords records,
                                  IGameClock clock, TFDominations? dominations = null)
    : IDeathNoticeHook, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 703;

    public int HookOrder => Order;

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        int victimIndex = death.Victim.EntryIndex;
        float now = clock.Now;
        TFHitRecord? last = records.LastHit(victimIndex);
        // The record is the killing hit's when it came from the same attacker
        // (the layer records an attacker that is no entity as none).
        EntityHandle attacker = death.Attacker != EntityHandle.None && entities.IsValid(death.Attacker)
            ? death.Attacker : EntityHandle.None;
        TFHitRecord? hit = last is { } h && h.Attacker == attacker ? h : null;

        EntityHandle scorer = death.Scorer;
        if (death.Attacker == death.Victim && death.Inflictor == death.Victim)
        {
            EntityHandle credit = records.RecentDamager(victimIndex, 0, TFCombatDefs.TimeSuicideKillCredit, now);
            if (credit != EntityHandle.None && entities.IsValid(credit))
                scorer = credit;
        }

        var (weapon, weaponId) = KillingWeapon(hit?.Custom ?? TFDamageCustom.None, scorer, death.Inflictor, death.Victim,
                                               hit?.WeaponName ?? "", hit?.Weapon ?? TFWeaponId.None);

        EntityHandle assister = scorer != EntityHandle.None && scorer != death.Victim
            ? records.RecentDamager(victimIndex, 1, TFCombatDefs.TimeAssistKill, now) : EntityHandle.None;
        if (assister == scorer || (assister != EntityHandle.None && !entities.IsValid(assister)))
            assister = EntityHandle.None;

        fields.SetInt("victim_entindex", entities.GetIndex(death.Victim));
        fields.SetInt("attacker", scorer != EntityHandle.None ? players.GetUserId(scorer) : 0);
        fields.SetInt("assister", assister != EntityHandle.None ? players.GetUserId(assister) : -1);
        fields.SetString("weapon", weapon);
        fields.SetString("weapon_logclassname", weapon);
        fields.SetInt("weaponid", (int)weaponId);
        fields.SetInt("damagebits", (int)(hit?.Bits ?? DamageType.Generic));
        // The self-kill command's death carries no recorded kind of its own:
        // stock's CommitSuicide flags the damage info
        // TF_DMG_CUSTOM_SUICIDE (tf_player.cpp:10218-10224, passed on at
        // player.cpp:5448-5458), and the event's customkill is what the
        // client's info text reads. A fall keeps its own phrase by the
        // client's precedence.
        var custom = hit?.Custom ?? TFDamageCustom.None;
        if (custom == TFDamageCustom.None
            && (death.Attacker == EntityHandle.None || death.Attacker == death.Victim)
            && (death.Inflictor == EntityHandle.None || death.Inflictor == death.Victim))
            custom = TFDamageCustom.Suicide;
        fields.SetInt("customkill", (int)custom);
        fields.SetInt("inflictor_entindex", death.Inflictor != EntityHandle.None ? entities.GetIndex(death.Inflictor) : -1);
        fields.SetBool("silent_kill", false);
        fields.SetInt("death_flags", dominations?.DeathFlags(victimIndex,
            scorer != EntityHandle.None ? entities.GetIndex(scorer) : -1,
            assister != EntityHandle.None ? entities.GetIndex(assister) : -1) ?? 0);
        fields.SetInt("stun_flags", 0);
        fields.SetInt("crit_type", (int)(hit?.Crit ?? TFCritType.None));
    }

    /// <summary>
    /// <c>GetKillingWeaponName</c> for the arms this build can reach: the
    /// kill-feed name and the weapon id.
    /// </summary>
    public (string Name, TFWeaponId Id) KillingWeapon(TFDamageCustom custom, EntityHandle scorer, EntityHandle inflictor,
                                                       EntityHandle victim, string hitWeaponName, TFWeaponId hitWeapon)
    {
        string name = "world";
        var id = TFWeaponId.None;

        switch (custom)
        {
            // Burning may land after the attacker switched weapons.
            case TFDamageCustom.Burning:
                (name, id) = ("tf_weapon_flamethrower", TFWeaponId.Flamethrower);
                break;
            case TFDamageCustom.BurningFlare:
                (name, id) = ("tf_weapon_flaregun", TFWeaponId.Flaregun);
                break;
            case TFDamageCustom.Telefrag:
                name = "telefrag";
                break;
            case TFDamageCustom.Bleeding:
                name = "tf_weapon_bleed_kill";
                break;
            default:
                if (scorer != EntityHandle.None && inflictor != EntityHandle.None && inflictor == scorer)
                {
                    // Not a suicide: the killer's current weapon did it.
                    if (victim != scorer)
                    {
                        string active = hitWeaponName.Length > 0 ? hitWeaponName : players.GetActiveWeapon(scorer);
                        if (active.Length > 0)
                            (name, id) = (active, hitWeapon);
                    }
                }
                else if (inflictor != EntityHandle.None && entities.IsValid(inflictor))
                {
                    name = entities.GetClassName(inflictor);
                }
                break;
        }

        return (StockName(name), id);
    }

    /// <summary>The prefixes stock strips from a killing weapon's name (:12540-12551), and its renames (:12553-12657).</summary>
    public static string StockName(string name)
    {
        // tf_weapon_grenade_ before tf_weapon_: the first prefix that matches is the one stripped.
        if (name.StartsWith("tf_weapon_grenade_", StringComparison.Ordinal))
            name = name["tf_weapon_grenade_".Length..];
        else if (name.StartsWith("tf_weapon_", StringComparison.Ordinal))
            name = name["tf_weapon_".Length..];
        else if (name.StartsWith("NPC_", StringComparison.Ordinal))
            name = name["NPC_".Length..];
        else if (name.StartsWith("func_", StringComparison.Ordinal))
            name = name["func_".Length..];

        return name switch
        {
            "tf_projectile_sentryrocket" => "obj_sentrygun3",
            "tf_projectile_healing_bolt" => "crusaders_crossbow",
            _ => name,
        };
    }
}

/// <summary>
/// A death's end of the damage layer (<see cref="IPlayerDeathHook"/>, after
/// the notice): the victim's records are cleared, stock's
/// <c>ClearHistories</c> on the next spawn (<c>tf_player.cpp:3720</c>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFCombatDeaths(TFDamageRecords records, TFSentryDamages sentryDamages)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 704;

    public int HookOrder => Order;

    public void OnPlayerDied(PlayerDeath death)
    {
        records.Clear(death.Player.EntryIndex);
        sentryDamages.Clear(death.Player.EntryIndex);
    }
}
