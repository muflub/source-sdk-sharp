//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Conditions;

/// <summary>
/// What conditions do to combat and healing, as the stock rules that read
/// only conditions decide it: the condition halves of
/// <c>CTFPlayerShared::IsCritBoosted</c>/<c>IsInvulnerable</c>/<c>IsStealthed</c>,
/// the mini-crit arms of <c>CTFGameRules::ApplyOnDamageModifyRules</c>, and
/// the heal rules of <c>CTFPlayer::TakeHealth</c> and
/// <c>ConditionGameRulesThink</c>. Read by combat (Stage 2.7) and healing
/// (2.9); speed is movement's (<c>TFMaxSpeed</c>, Phase 1.8).
///
/// <para>
/// Not here, because stock does not key them on conditions: cloak refusal
/// (<c>CTFPlayer::CanGoInvisible</c>, <c>tf_player_shared.cpp:12678-12698</c>,
/// is the flag, PASS Time ball and the losing team), and a heal block while
/// burning: burning adds <c>HealingDebuff</c> (:6653), which scales medigun
/// healing by 0.8 and blocks nothing (<see cref="HealerHealScale"/>).
/// </para>
/// </summary>
public sealed class TFConditionEffects
{
    /// <summary><c>PYRO_AFTERBURN_HEALING_REDUCTION</c> (<c>tf_shareddefs.h:2828</c>).</summary>
    public const float AfterburnHealingReduction = 0.2f;

    /// <summary>
    /// The conditions that crit-boost every weapon: <c>IsCritBoosted</c>'s
    /// first test (<c>tf_player_shared.cpp:8058-8069</c>). <c>CritboostedDemoCharge</c>
    /// counts only on the client (<c>#ifdef CLIENT_DLL</c>, :8061-8063). The
    /// weapon-dependent rest (rage buff on a primary, the low-health attribute)
    /// is the weapon's (Phase 1.9).
    /// </summary>
    public bool IsCritBoosted(in TFCondMask conds, bool clientSide) =>
        conds.Has(TFCond.Critboosted)
        || conds.Has(TFCond.CritboostedPumpkin)
        || conds.Has(TFCond.CritboostedUserBuff)
        || (clientSide && conds.Has(TFCond.CritboostedDemoCharge))
        || conds.Has(TFCond.CritboostedFirstBlood)
        || conds.Has(TFCond.CritboostedBonusTime)
        || conds.Has(TFCond.CritboostedCtfCapture)
        || conds.Has(TFCond.CritboostedOnKill)
        || conds.Has(TFCond.CritboostedCardEffect)
        || conds.Has(TFCond.CritboostedRuneTemp);

    /// <summary><c>IsInvulnerable</c> (:8099-8107).</summary>
    public bool IsInvulnerable(in TFCondMask conds) =>
        conds.Has(TFCond.Invulnerable)
        || conds.Has(TFCond.InvulnerableUserBuff)
        || conds.Has(TFCond.InvulnerableHideUnlessDamaged)
        || conds.Has(TFCond.InvulnerableCardEffect);

    /// <summary><c>IsStealthed</c> (:8112-8116).</summary>
    public bool IsStealthed(in TFCondMask conds) =>
        conds.Has(TFCond.Stealthed)
        || conds.Has(TFCond.StealthedUserBuff)
        || conds.Has(TFCond.StealthedUserBuffFading);

    /// <summary>
    /// The victim takes mini-crits: jarate, marked for death (loud or silent),
    /// the PASS Time penalty (<c>tf_gamerules.cpp:6126-6129</c>).
    /// </summary>
    public bool TakesMiniCrits(in TFCondMask victim) =>
        victim.Has(TFCond.Urine)
        || victim.Has(TFCond.Markedfordeath)
        || victim.Has(TFCond.MarkedfordeathSilent)
        || victim.Has(TFCond.PasstimePenaltyDebuff);

    /// <summary>
    /// The attacker deals mini-crits: the soldier's buff banner and its
    /// no-healing variant (<c>tf_gamerules.cpp:6140</c>).
    /// </summary>
    public bool DealsMiniCrits(in TFCondMask attacker) =>
        attacker.Has(TFCond.Offensebuff) || attacker.Has(TFCond.Nohealingdamagebuff);

    /// <summary>
    /// The damage factor the conditions alone give a hit that is not already
    /// a crit and not self-damage (both tested before, :6096, :6120):
    /// <see cref="TFDamage.MinicritMultiplier"/> when either side's
    /// conditions mini-crit, since stock adds <c>(1.35 - 1) × damage</c>
    /// (:6693-6694); else 1. Full crits and the weapon arms are combat's.
    /// </summary>
    public float DamageMultiplier(in TFCondMask attacker, in TFCondMask victim) =>
        TakesMiniCrits(victim) || DealsMiniCrits(attacker) ? TFDamage.MinicritMultiplier : 1f;

    /// <summary>
    /// No healing at all: <c>TakeHealth</c> returns 0 under the no-healing
    /// damage buff (<c>server/tf/tf_player.cpp:8419-8424</c>).
    /// </summary>
    public bool HealBlocked(in TFCondMask conds) => conds.Has(TFCond.Nohealingdamagebuff);

    /// <summary>
    /// The factor on healers' healing: <c>1 - </c><see cref="AfterburnHealingReduction"/>
    /// under the afterburn debuff (<c>tf_player_shared.cpp:2537-2540</c>), else 1.
    /// </summary>
    public float HealerHealScale(in TFCondMask conds) =>
        conds.Has(TFCond.HealingDebuff) ? 1f - AfterburnHealingReduction : 1f;

    /// <summary>
    /// The conditions healers wear off faster, for <see cref="TFConditions.Tick"/>:
    /// stock's <c>ConditionExpiresFast</c>
    /// (<c>tf_shareddefs.cpp:286-293</c>) — burning, jarate, bleeding, mad
    /// milk and gas. <c>ConditionGameRulesThink</c> applies it only under at
    /// least one healer, the jarate arm <c>healers × frametime</c> and the
    /// rest <c>healers × frametime × 4</c> (<c>tf_player_shared.cpp:2357-2367</c>).
    /// </summary>
    public TFCondMask ExpiresFastUnderHealing =>
        TFCondMask.Of(
            stackalloc TFCond[]
            {
                TFCond.Burning,
                TFCond.Urine,
                TFCond.Bleeding,
                TFCond.MadMilk,
                TFCond.Gas,
            });
}
