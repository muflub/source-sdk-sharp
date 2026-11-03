//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared;

/// <summary>
/// One weapon's crit token bucket: <c>CBaseCombatWeapon</c>'s
/// <c>m_flCritTokenBucket</c>, <c>m_nCritChecks</c> and <c>m_nCritSeedRequests</c>
/// with <c>AddToCritBucket</c>, <c>IsAllowedToWithdrawFromCritBucket</c> and
/// <c>RemoveFromCritBucket</c> (<c>shared/basecombatweapon_shared.cpp:1593-1636</c>,
/// <c>basecombatweapon_shared.h:588-593</c>). Pure: the cap and bottom are handed
/// in (<c>tf_weapon_criticals_bucket_cap</c>/<c>_bottom</c>, :60-61).
///
/// <para>
/// Stock does not network these members: each side keeps its own bucket
/// (the client's grows on every prediction pass, its checks count only on the
/// first), so a bucket is per side and per weapon, never predicted state.
/// </para>
/// </summary>
public sealed class CritBucket
{
    /// <summary>The constructor's values (:104-106): the bucket's default, one check, no requests.</summary>
    public CritBucket(float initialTokens)
    {
        Tokens = initialTokens;
        Checks = 1;
        SeedRequests = 0;
    }

    /// <summary><c>m_flCritTokenBucket</c>.</summary>
    public float Tokens { get; private set; }

    /// <summary><c>m_nCritChecks</c>: how many crit rolls this weapon has made (starts at 1).</summary>
    public int Checks { get; private set; }

    /// <summary><c>m_nCritSeedRequests</c>: how many times a roll said crit and asked the bucket.</summary>
    public int SeedRequests { get; private set; }

    /// <summary>
    /// <c>AddToCritBucket</c>: the raw damage goes in, but only while the bucket
    /// is below its cap, and never past it.
    /// </summary>
    public void Add(float amount, float cap)
    {
        if (Tokens < cap)
        {
            Tokens += amount;
            Tokens = MathF.Min(Tokens, cap);
        }
    }

    /// <summary>A bucket holding what this one holds, to roll against without paying from this one.</summary>
    public CritBucket Copy() => new(Tokens) { Checks = Checks, SeedRequests = SeedRequests };

    /// <summary>One more crit roll (<c>m_nCritChecks++</c>).</summary>
    public void CountCheck() => Checks++;

    /// <summary>
    /// <c>IsAllowedToWithdrawFromCritBucket</c>: a roll that said crit asks to
    /// pay for it. The request is counted first; the cost is the damage at the
    /// crit multiplier times a factor of how often this weapon has asked (melee
    /// a flat half); a cost above the bucket is refused and nothing is paid,
    /// else it is withdrawn and the bucket held at the bottom.
    /// </summary>
    public bool TryWithdraw(float damage, bool isMelee, float bottom)
    {
        SeedRequests++;

        float cost = Cost(damage, isMelee, SeedRequests, Checks);
        if (cost > Tokens)
            return false;

        Tokens -= cost;
        if (Tokens < bottom)
            Tokens = bottom;
        return true;
    }

    /// <summary>
    /// The request factor (:1621): melee 0.5, else
    /// <c>RemapValClamped(requests / checks, 0.1, 1, 1, 3)</c>.
    /// </summary>
    public static float Multiplier(bool isMelee, int requests, int checks) =>
        isMelee ? 0.5f : RemapValClamped((float)requests / checks, 0.1f, 1f, 1f, 3f);

    /// <summary>What a crit of <paramref name="damage"/> costs (:1624): damage × <c>TF_DAMAGE_CRIT_MULTIPLIER</c> × the factor.</summary>
    public static float Cost(float damage, bool isMelee, int requests, int checks) =>
        damage * TFDamage.CritMultiplier * Multiplier(isMelee, requests, checks);

    /// <summary><c>RemapValClamped</c> (<c>public/mathlib/mathlib.h:619-627</c>).</summary>
    public static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float cVal = Math.Clamp((val - a) / (b - a), 0f, 1f);
        return c + (d - c) * cVal;
    }
}
