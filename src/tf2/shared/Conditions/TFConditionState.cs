//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.CompilerServices;

using SourceSharp.Abi;

namespace TF2.Shared.Conditions;

/// <summary>
/// Everything <see cref="TFConditions"/> keeps, as one plain value: stock's
/// five condition words, its per-condition <c>condition_source_t</c> fields
/// (<c>tf_player_shared.h:172-189</c>: <c>m_flExpireTime</c>,
/// <c>m_pProvider</c>, <c>m_bPrevActive</c>) and the one condition-list
/// object stock has, <c>CTFCondition_CritBoost</c>'s min/max durations
/// (<c>tf_condition.h:89-90</c>).
///
/// <para>
/// No references: the arrays are inline, the providers are handles, so
/// assigning a state copies it whole and a prediction ring or a netstate can
/// hold it by value. What stock networks of it is only <see cref="Bits"/>
/// (the five words, predicted) and the providers (sent, not predicted); the
/// expire times, the previous-active flags and the crit durations are server
/// state (their SendProps are commented out, <c>tf_player_shared.cpp:528-533</c>,
/// and <c>CTFConditionList</c> sends only its bits, <c>tf_condition.cpp:31-33</c>).
/// </para>
/// </summary>
public struct TFConditionState
{
    /// <summary>The active conditions: stock's five words, with <c>CTFConditionList</c>'s bit for <c>Critboosted</c> folded into word 0.</summary>
    public TFCondMask Bits;

    /// <summary>
    /// <c>m_bPrevActive</c> per condition: at <c>AddCond</c>, whether the
    /// condition's expire time was non-zero (<c>tf_player_shared.cpp:1142</c>);
    /// cleared by <c>RemoveCond</c> (:1202).
    /// </summary>
    public TFCondMask PrevActive;

    /// <summary>
    /// <c>m_flExpireTime</c> per condition: seconds left, or
    /// <see cref="TFConditions.Permanent"/>. Counted down only on the server
    /// (<c>ConditionGameRulesThink</c>). Never written for <c>Critboosted</c>,
    /// whose durations are <see cref="CritBoostMaxDuration"/> and
    /// <see cref="CritBoostMinDuration"/>.
    /// </summary>
    public TFCondTimes ExpireTimes;

    /// <summary><c>m_pProvider</c> per condition: who applied it, or <see cref="EntityHandle.None"/>.</summary>
    public TFCondProviders Providers;

    /// <summary>
    /// <c>m_nPreventedDamageFromCondition</c> per condition
    /// (<c>tf_player_shared.h:172-189</c>): the damage a condition has
    /// prevented, accumulated by its damage rules and read out — then zeroed —
    /// when the condition ends (<c>tf_player_shared.cpp:1184-1198</c>). Server
    /// state, like the expire times.
    /// </summary>
    public TFCondPrevented PreventedDamage;

    /// <summary><c>CTFCondition::_max_duration</c> of the crit boost: seconds left, or permanent.</summary>
    public float CritBoostMaxDuration;

    /// <summary><c>CTFCondition::_min_duration</c> of the crit boost: the floor a removal waits out.</summary>
    public float CritBoostMinDuration;
}

/// <summary>One float per condition, inline (no array reference).</summary>
[InlineArray((int)TFCond.Last)]
public struct TFCondTimes
{
    private float _element0;
}

/// <summary>One provider handle per condition, inline (no array reference).</summary>
[InlineArray((int)TFCond.Last)]
public struct TFCondProviders
{
    private EntityHandle _element0;
}

/// <summary>One prevented-damage total per condition, inline (no array reference).</summary>
[InlineArray((int)TFCond.Last)]
public struct TFCondPrevented
{
    private int _element0;
}
