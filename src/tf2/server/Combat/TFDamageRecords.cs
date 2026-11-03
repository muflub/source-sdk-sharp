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
/// One hit a player took, as the damage layer left it: what the kill feed
/// reads when it was the last (<c>CTFGameRules::DeathNotice</c>'s
/// <c>CTakeDamageInfo</c>).
/// </summary>
public readonly record struct TFHitRecord(EntityHandle Attacker, float Damage, DamageType Bits, TFCritType Crit,
                                          TFDamageCustom Custom, TFWeaponId Weapon, string WeaponName, float Time);

/// <summary>
/// The TF combat state of the hit being dealt right now, carried beside the
/// damage call as <see cref="DamageHitGroups"/> carries the hitgroup: the
/// kill's custom kind and the weapon behind it, for damage the damage layer
/// deals itself (an afterburn tick: <c>TF_DMG_CUSTOM_BURNING</c>, the burn's
/// weapon) where the attacker's active weapon is not the one. Outside every
/// scope a hit has no custom kind and is the attacker's active weapon's.
/// </summary>
[SideService(Side.Server)]
public sealed class TFDamageScopes
{
    private readonly Stack<(TFDamageCustom Custom, TFWeaponId Weapon, string WeaponName)> _open = new();

    /// <summary>Opens a scope in which every hit is <paramref name="custom"/>, by <paramref name="weapon"/>.</summary>
    public Scope Push(TFDamageCustom custom, TFWeaponId weapon, string weaponName)
    {
        _open.Push((custom, weapon, weaponName));
        return new Scope(this);
    }

    /// <summary>The innermost open scope, if any.</summary>
    public bool TryCurrent(out TFDamageCustom custom, out TFWeaponId weapon, out string weaponName)
    {
        if (_open.TryPeek(out var top))
        {
            (custom, weapon, weaponName) = top;
            return true;
        }

        (custom, weapon, weaponName) = (TFDamageCustom.None, TFWeaponId.None, "");
        return false;
    }

    public readonly struct Scope(TFDamageScopes owner) : IDisposable
    {
        public void Dispose() => owner._open.Pop();
    }
}

/// <summary>
/// What each player has been hit by, per player index: the last hit (for
/// the death notice) and stock's damager history
/// (<c>CAchievementData::aDamagers</c>, <c>server/tf/tf_achievementdata.h</c>):
/// up to <see cref="HistorySlots"/> distinct attackers, most recent first, an
/// attacker hitting again moving to the front (<c>InsertHistory</c> :26-47).
/// Only enemies enter it (<c>CTFPlayer::OnTakeDamage</c>,
/// <c>tf_player.cpp:9042-9044</c>: "ignore damagers on our team").
/// <see cref="Clear"/> is stock's <c>ClearHistories</c> (on spawn,
/// <c>tf_player.cpp:3720</c>; here at death, after the notice).
/// </summary>
[SideService(Side.Server)]
public sealed class TFDamageRecords
{
    /// <summary><c>MAX_ACHIEVEMENT_HISTORY_SLOTS</c> (<c>tf_achievementdata.h:10</c>).</summary>
    public const int HistorySlots = 4;

    private readonly Dictionary<int, TFHitRecord> _last = new();
    private readonly Dictionary<int, List<(EntityHandle Attacker, float Time)>> _damagers = new();

    /// <summary>A hit <paramref name="victim"/> took; <paramref name="damager"/> when it counts toward the history.</summary>
    public void Note(int victim, in TFHitRecord hit, bool damager)
    {
        _last[victim] = hit;
        if (!damager || hit.Attacker == EntityHandle.None)
            return;

        if (!_damagers.TryGetValue(victim, out var list))
            _damagers[victim] = list = new List<(EntityHandle, float)>(HistorySlots + 1);

        EntityHandle attacker = hit.Attacker;
        int at = list.FindIndex(d => d.Attacker == attacker);
        if (at >= 0)
            list.RemoveAt(at);
        list.Insert(0, (attacker, hit.Time));
        if (list.Count > HistorySlots)
            list.RemoveAt(list.Count - 1);
    }

    /// <summary>The last hit <paramref name="victim"/> took, if any since the last clear.</summary>
    public TFHitRecord? LastHit(int victim) => _last.TryGetValue(victim, out var hit) ? hit : null;

    /// <summary>
    /// <c>CTFGameRules::GetRecentDamager</c> (<c>tf_gamerules.cpp</c>): the
    /// <paramref name="which"/>-th most recent damager (0 the latest) if it hit
    /// within <paramref name="maxElapsed"/> seconds of <paramref name="now"/>.
    /// </summary>
    public EntityHandle RecentDamager(int victim, int which, float maxElapsed, float now)
    {
        if (!_damagers.TryGetValue(victim, out var list) || which >= list.Count)
            return EntityHandle.None;

        var (attacker, time) = list[which];
        return now - time <= maxElapsed ? attacker : EntityHandle.None;
    }

    /// <summary>Forgets everything about <paramref name="victim"/>.</summary>
    public void Clear(int victim)
    {
        _last.Remove(victim);
        _damagers.Remove(victim);
    }
}

/// <summary>
/// Which of a player's wounds came from whose sentry (<c>CAchievementData::
/// aSentryDamagers</c>, <c>server/tf/tf_player.cpp:21175-21204</c>): the
/// assister arm of <c>CTFGameRules::PlayerKilled</c>
/// (<c>tf_gamerules.cpp:11405-11412</c>) asks it — a player the dead one's
/// list names within <see cref="AssistWindow"/> seconds hands the assist to
/// the sentry that softened the target for them. Recorded from the damage
/// layer exactly where stock's <c>OnTakeDamage</c> does it
/// (<c>tf_player.cpp:9050-9055</c>): an enemy player's hit whose inflictor is
/// the sentry. Same history shape as <see cref="TFDamageRecords"/>:
/// <see cref="TFDamageRecords.HistorySlots"/> entries, most recent first, a
/// repeat damager moved to the front.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSentryDamages
{
    /// <summary>IsSentryDamagerInHistory's window at the call site (tf_gamerules.cpp:11405).</summary>
    public const float AssistWindow = 5f;

    private readonly Dictionary<int, List<(EntityHandle Damager, EntityHandle Sentry, float Time)>> _hits = new();

    /// <summary>The sentry wound: <paramref name="damager"/>'s <paramref name="sentry"/> hit <paramref name="victim"/> at <paramref name="now"/>.</summary>
    public void Note(int victim, EntityHandle damager, EntityHandle sentry, float now)
    {
        if (damager == EntityHandle.None || sentry == EntityHandle.None)
            return;

        if (!_hits.TryGetValue(victim, out var list))
            _hits[victim] = list = new List<(EntityHandle, EntityHandle, float)>(TFDamageRecords.HistorySlots + 1);

        int at = list.FindIndex(h => h.Damager == damager);
        if (at >= 0)
            list.RemoveAt(at);

        list.Insert(0, (damager, sentry, now));
        if (list.Count > TFDamageRecords.HistorySlots)
            list.RemoveAt(list.Count - 1);
    }

    /// <summary>
    /// IsSentryDamagerInHistory: the sentry <paramref name="damager"/> hurt
    /// <paramref name="victim"/> with within the window, else none — the
    /// scan stops at the first entry past it (stock returns there, the
    /// history being recency-ordered).
    /// </summary>
    public EntityHandle DamagedBy(int victim, EntityHandle damager, float now)
    {
        if (damager == EntityHandle.None)
            return EntityHandle.None;

        if (!_hits.TryGetValue(victim, out var list))
            return EntityHandle.None;

        foreach (var (hitDamager, sentry, time) in list)
        {
            if (now - time > AssistWindow)
                return EntityHandle.None;

            if (hitDamager == damager)
                return sentry;
        }

        return EntityHandle.None;
    }

    /// <summary>Forgets everything about <paramref name="victim"/>, with the damager history (ClearHistories).</summary>
    public void Clear(int victim) => _hits.Remove(victim);
}
