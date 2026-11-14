//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Player;

/// <summary>
/// The soldier's buff charge — stock's <c>m_flRageMeter</c>, built from
/// damage dealt by <c>CTFPlayer::HandleRageGain</c>
/// (<c>tf_player.cpp:8718-8741</c>, called at :9722 with the hit's damage and
/// the divisor 6). The gain is the buff's scale from
/// <c>g_RageBuffTypes</c> (<c>tf_shareddefs.h:2760-2769</c>) over the damage
/// divided: Buff Banner and Battalion's Backup scale 1, the Concheror 1.25,
/// and only their flag <c>kRageBuffFlag_OnDamageDealt</c> answers this arm.
/// The meter caps at 100 and refuses gains while the deploy drains
/// (<c>SetRageMeter</c>, <c>tf_player_shared.cpp:13870-13898</c>). The item's
/// <c>rage_giving_scale</c> rides on the damage as stock's caller multiplies
/// it in (:9714-9715). The HUD's bar is the meter over 100
/// (<c>CTFBuffItem::GetProgress</c>, tf_weapon_buff_item.cpp:444-451).
/// </summary>
public static class TFRageMeter
{
    /// <summary>The item attribute naming the buff (<c>set_buff_type</c>, the
    /// hook <c>UpdateRageBuffsAndRage</c> reads, tf_player_shared.cpp:13816).</summary>
    public const string BuffTypeAttribute = "set_buff_type";

    /// <summary>The item scale on the incoming damage (<c>rage_giving_scale</c>,
    /// tf_player.cpp:9714-9715).</summary>
    public const string GivingScaleAttribute = "rage_giving_scale";

    /// <summary>A full meter: the deploy's gate (<c>ActivateRageBuff</c>,
    /// :13756) and the HUD bar's denominator (<c>GetProgress</c>).</summary>
    public const float FullMeter = 100f;

    /// <summary>The damage's divisor for the dealt arm (stock's <c>6.0f</c>,
    /// tf_player.cpp:9722).</summary>
    public const float DamageDivisor = 6f;

    /// <summary>The pulses every buff type in <c>g_RageBuffTypes</c> carries
    /// (its third column, tf_shareddefs.h:2760-2769): the deploy drains the
    /// meter over this many one-second ticks.</summary>
    public const int BuffPulses = 10;

    /// <summary>The deploy's reach (<c>fMaxRadius</c>, tf_player_shared.cpp:13944).</summary>
    public const float BuffRadius = 450f;

    /// <summary>The seconds one pulse keeps its buff on (<c>AddCond(eBuffCond,
    /// 1.2f, m_pOuter)</c>, tf_player_shared.cpp:14006).</summary>
    public const float BuffDurationSeconds = 1.2f;

    /// <summary>The heal-on-hit share of the Concheror's regen-on-damage buff
    /// (<c>TF_COND_REGENONDAMAGEBUFF</c>): a buffed carrier heals this much of
    /// the damage each of its hits deals — stock's
    /// <c>tf_dev_health_on_damage_recover_percentage</c>, declared with the
    /// default <c>"0.35"</c> at tf_weaponbase.cpp:5050 and applied at
    /// :5213-5245, its float truncated into the int that heals (:5215). The
    /// buff's other half, the carried speed boost, is
    /// <c>TF_COND_SPEED_BOOST</c> (tf_player_shared.cpp:4234-4259).</summary>
    public const float HealOnHitScale = 0.35f;

    /// <summary>
    /// The meter scale of a buff type that gains on damage dealt, stock's
    /// <c>g_RageBuffTypes</c> rows whose flag is
    /// <c>kRageBuffFlag_OnDamageDealt</c> (tf_shareddefs.h:2763-2765): Buff
    /// Banner and Battalion's Backup scale 1, the Concheror 1.25. Any other
    /// type, 0 included, answers none — the flag test of
    /// <c>HandleRageGain</c>, tf_player.cpp:8736.
    /// </summary>
    public static float BuffScale(int buffType) => buffType switch
    {
        1 => 1f,
        2 => 1f,
        3 => 1.25f,
        _ => 0f,
    };

    /// <summary>
    /// The condition a buff type wears: <c>g_SoldierBuffAttributeIDToConditionMap</c>
    /// (tf_player_shared.cpp:264-273) — Buff Banner's offense buff, Battalion's
    /// Backup's defense buff, the Concheror's heal-on-hit buff, and the
    /// no-heal, pyro-rage and sniper-focus buffs behind them. Any other type
    /// answers none, stock's <c>TF_COND_LAST</c> arm (:14004).
    /// </summary>
    public static TFCond BuffCond(int buffType) => buffType switch
    {
        1 => TFCond.Offensebuff,
        2 => TFCond.Defensebuff,
        3 => TFCond.Regenondamagebuff,
        4 => TFCond.Nohealingdamagebuff,
        5 => TFCond.CritboostedRageBuff,
        6 => TFCond.SniperchargeRageBuff,
        _ => TFCond.Invalid,
    };

    /// <summary>
    /// Book a dealt hit on the soldier's meter: the buff's scale over the
    /// scaled damage and the divisor 6, held at full as stock's
    /// <c>SetRageMeter</c> holds <c>MIN(val, 100)</c>
    /// (<c>tf_player_shared.cpp:13883</c>). While the deploy drains the gain
    /// is refused (:13875-13876); no buff, or one whose flag is not the dealt
    /// one, answers nothing (:8736).
    /// </summary>
    public static void GainOnDamageDealt(TFPlayerNetState player, float damage, float givingScale, int buffType)
    {
        float scale = BuffScale(buffType);
        if (scale <= 0f)
            return;

        if (player.RageDraining)
            return;

        player.RageMeter = Math.Min(FullMeter, player.RageMeter + scale * ((damage * givingScale) / DamageDivisor));
    }
}
