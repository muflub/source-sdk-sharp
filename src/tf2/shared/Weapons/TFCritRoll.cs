//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The crit convars a roll reads, at their stock defaults:
/// <c>tf_weapon_criticals</c> (<c>tf_weaponbase.cpp:277</c>),
/// <c>tf_weapon_criticals_melee</c> (<c>tf_weaponbase_melee.cpp</c>) and the
/// bucket's cap, bottom and starting tokens (<c>basecombatweapon_shared.cpp:60-62</c>).
/// </summary>
public readonly record struct TFCritSettings(
    bool RandomCrits = true,
    int MeleeCrits = 1,
    float BucketCap = 1000f,
    float BucketBottom = -250f,
    float BucketDefault = 300f)
{
    /// <summary>The stock defaults: <c>new()</c> is stock, never all-zero.</summary>
    public TFCritSettings() : this(true, 1, 1000f, -250f, 300f) { }
}

/// <summary>What one attack rolls with: the weapon's half of the inputs.</summary>
/// <param name="Melee">A melee weapon: <c>CTFWeaponBaseMelee</c>'s helpers and seed mask.</param>
/// <param name="RapidFire">The mode's <c>m_bUseRapidFireCrits</c>.</param>
/// <param name="Damage">The mode's <c>m_nDamage</c> through the <c>mult_dmg</c> hook.</param>
/// <param name="BulletsPerShot">The mode's <c>m_nBulletsPerShot</c> through <c>mult_bullets_per_shot</c> (below 1 counts 1).</param>
/// <param name="FireDelay">The mode's <c>m_flTimeFireDelay</c>, which a rapid-fire crit's prepayment divides.</param>
/// <param name="Command">The command's seed and the weapon's and owner's entity indices.</param>
/// <param name="Now">The command's clock.</param>
/// <param name="FirstPrediction">True on the server and on the client's first prediction (<c>IsFirstTimePredicted</c>).</param>
public readonly record struct TFCritShot(
    bool Melee,
    bool RapidFire,
    float Damage,
    int BulletsPerShot,
    float FireDelay,
    WeaponCommand Command,
    float Now,
    bool FirstPrediction);

/// <summary>What one attack rolls with: the owner's half, after the weapon hooks' crit policy.</summary>
/// <param name="CritBoosted"><c>CTFPlayerShared::IsCritBoosted</c>: every shot crits.</param>
/// <param name="WinsCrits">The round-win arm: every shot crits.</param>
/// <param name="CritMult"><c>CTFPlayer::GetCritMult</c>.</param>
/// <param name="ChanceScale">The <c>mult_crit_chance</c> hook's factor.</param>
/// <param name="CanFireCriticalShot"><c>CanFireCriticalShot</c>: false and no crit, boosted or not.</param>
/// <param name="ObservedCritChance"><c>m_flObservedCritChance</c> as the weapon state holds it: the server's last measured rate, networked to the owner, what the client's roll compares.</param>
/// <param name="RangedDamage">The owner's round's <c>TFSTAT_DAMAGE_RANGED</c> — the server's measure's total (<c>tf_gamestats.cpp:1075</c>); read only on the side that has the round's stats.</param>
/// <param name="RandomCritDamage">The owner's round's <c>TFSTAT_DAMAGE_RANGED_CRIT_RANDOM</c>, the crit share of <paramref name="RangedDamage"/> (<c>tf_gamestats.cpp:1086</c>).</param>
/// <param name="StatsMeasured">Whether the round's two numbers came with the policy at all — true only on the server side, whose <c>CanFireRandomCriticalShot</c> measures and sets; false and the roll compares the stored value alone, as stock's client arm does.</param>
public readonly record struct TFCritOwner(
    bool CritBoosted = false,
    bool WinsCrits = false,
    float CritMult = 1f,
    float ChanceScale = 1f,
    bool CanFireCriticalShot = true,
    float ObservedCritChance = 0f,
    int RangedDamage = 0,
    int RandomCritDamage = 0,
    bool StatsMeasured = false)
{
    /// <summary>A plain owner: no boost, multipliers of 1, allowed to crit, no round stats read. <c>new()</c> is this, never all-zero.</summary>
    public TFCritOwner() : this(false, false, 1f, 1f, true, 0f, 0, 0, false) { }
}

/// <summary>
/// A rapid-fire weapon's crit clocks, which the weapon keeps as predicted state:
/// <c>m_flCritTime</c> (the end of the crit stream) and the last rapid-fire
/// check (<c>m_flLastCritCheckTime</c> / <c>m_flLastRapidFireCritCheckTime</c>).
/// </summary>
public struct TFCritClocks
{
    public float CritUntil;
    public float LastRapidCheck;
}

/// <summary>
/// A roll's answer: <c>m_bCurrentAttackIsCrit</c>, <c>m_bCurrentCritIsRandom</c>
/// and the drawn number (-1 when nothing was drawn), plus the rate the server's
/// <c>CanFireRandomCriticalShot</c> measured — stock's <c>m_flObservedCritChance.Set</c>
/// (<c>tf_weaponbase.cpp:4652</c>) runs before the compare, so it rides the
/// refusal too, and the writer puts it on the weapon state either way. Null on
/// every arm that measured nothing: the client, and a round without damage yet.
/// </summary>
public readonly record struct TFCritResult(bool Critical, bool Random = false, int Roll = -1, float? ObservedSet = null);

/// <summary>
/// <c>CTFWeaponBase::CalcIsAttackCritical</c> and its helpers
/// (<c>shared/tf/tf_weaponbase.cpp:1529-1786</c>,
/// <c>CanFireRandomCriticalShot</c> :4630-4664) with the melee overrides
/// (<c>tf_weaponbase_melee.cpp:1040-1131</c>).
///
/// <para>
/// THE SEED is the command's prediction seed XOR the weapon's and owner's
/// entity indices: <c>seed ^ (weapon &lt;&lt; 8 | owner)</c> for a gun (:1676,
/// :1704), <c>seed ^ (weapon &lt;&lt; 16 | owner &lt;&lt; 8)</c> for melee
/// (melee :1087). Stock reseeds only when that differs from the weapon's
/// <c>m_iCurrentSeed</c>; one roll per command makes it differ every time,
/// so here every roll reseeds and draws one <c>RandomInt(0, 9999)</c>: the same
/// command rolls the same number on the server and on every client replay.
/// The scratch stream is reseeded before each draw and remembers nothing.
/// </para>
///
/// <para>
/// Not ported: the training, PvE and creep-wave bot arms (no bots yet), the
/// demo charge's <c>GetNextMeleeCrit</c>, and the debug convars
/// (<c>tf_weapon_criticals_force_random</c>, <c>_anticheat</c>, <c>_debug</c>,
/// <c>_DEBUG</c> builds only).
/// </para>
/// </summary>
public sealed class TFCritRoll
{
    /// <summary><c>WEAPON_RANDOM_RANGE</c> (<c>tf_weaponbase.h:809</c>).</summary>
    public const int RandomRange = 10000;

    private readonly UniformStream _stream = new();

    /// <summary>The seed a gun's roll draws from (:1676).</summary>
    public static int GunSeed(in WeaponCommand command) =>
        command.Seed ^ ((command.WeaponIndex << 8) | command.OwnerIndex);

    /// <summary>The seed a melee roll draws from (melee :1087).</summary>
    public static int MeleeSeed(in WeaponCommand command) =>
        command.Seed ^ ((command.WeaponIndex << 16) | (command.OwnerIndex << 8));

    /// <summary>
    /// <c>CalcIsAttackCritical</c>: the round's winners crit; with random crits
    /// off only a boost crits (<c>HelperNoCrits</c>); else the helper rolls.
    /// </summary>
    public TFCritResult Roll(in TFCritShot shot, in TFCritOwner owner, in TFCritSettings settings,
                             CritBucket bucket, ref TFCritClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        if (owner.WinsCrits)
            return new TFCritResult(true);

        return settings.RandomCrits
            ? Helper(in shot, in owner, in settings, bucket, ref clocks)
            : HelperNoCrits(in shot, in owner, in settings, bucket, ref clocks);
    }

    private TFCritResult HelperNoCrits(in TFCritShot shot, in TFCritOwner owner, in TFCritSettings settings,
                                       CritBucket bucket, ref TFCritClocks clocks)
    {
        // Melee: tf_weapon_criticals_melee 2 rolls whatever tf_weapon_criticals says.
        if (shot.Melee && settings.MeleeCrits > 1)
            return MeleeHelper(in shot, in owner, in settings, bucket, ref clocks);

        // "Support critboosted even in no crit mode."
        return new TFCritResult(owner.CritBoosted);
    }

    private TFCritResult Helper(in TFCritShot shot, in TFCritOwner owner, in TFCritSettings settings,
                                CritBucket bucket, ref TFCritClocks clocks) =>
        shot.Melee
            ? MeleeHelper(in shot, in owner, in settings, bucket, ref clocks)
            : GunHelper(in shot, in owner, in settings, bucket, ref clocks);

    private TFCritResult GunHelper(in TFCritShot shot, in TFCritOwner owner, in TFCritSettings settings,
                                   CritBucket bucket, ref TFCritClocks clocks)
    {
        if (!owner.CanFireCriticalShot)
            return new TFCritResult(false);

        if (owner.CritBoosted)
            return new TFCritResult(true);

        // A rapid-fire weapon crits for the whole of a crit stream.
        if (shot.RapidFire && clocks.CritUntil > shot.Now)
            return new TFCritResult(true);

        // The bucket takes the shot's whole damage, every pellet of it.
        float damage = shot.Damage * Math.Max(1, shot.BulletsPerShot);
        bucket.Add(damage, settings.BucketCap);

        float critChance;
        int roll;
        bool crit;

        if (shot.RapidFire)
        {
            // One check a second for a rapid-fire weapon.
            if (shot.Now < clocks.LastRapidCheck + 1f)
                return new TFCritResult(false, Random: true);
            clocks.LastRapidCheck = shot.Now;

            float totalCritChance = Math.Clamp(TFDamage.CritChanceRapid * owner.CritMult, 0.01f, 0.99f);
            float critDuration = TFDamage.CritDurationRapid;
            float nonCritDuration = critDuration / totalCritChance - critDuration;
            float startCritChance = 1f / nonCritDuration * owner.ChanceScale;

            roll = Draw(GunSeed(shot.Command));
            crit = roll < startCritChance * RandomRange;
            critChance = crit ? startCritChance : 0f;
        }
        else
        {
            critChance = TFDamage.CritChance * owner.CritMult * owner.ChanceScale;
            roll = Draw(GunSeed(shot.Command));
            crit = roll < critChance * RandomRange;
        }

        // Each check counts; the client counts its first prediction only.
        if (shot.FirstPrediction)
            bucket.CountCheck();

        float? observedSet = null;

        if (crit)
        {
            // The long-term rate (CanFireRandomCriticalShot), after the draw
            // and before the prepayment and withdrawal, as stock's gun helper
            // orders it.
            if (!CanFireRandomCriticalShot(in shot, in owner, critChance, out observedSet))
                return new TFCritResult(false, Random: true, roll) { ObservedSet = observedSet };

            // A rapid-fire crit pays for the whole stream up front, never more than the cap.
            if (shot.RapidFire)
            {
                damage *= TFDamage.CritDurationRapid / shot.FireDelay;
                int bucketCap = (int)settings.BucketCap;
                if (damage * TFDamage.CritMultiplier > bucketCap)
                    damage = (float)bucketCap / TFDamage.CritMultiplier;
            }

            crit = bucket.TryWithdraw(damage, isMelee: false, settings.BucketBottom);

            if (crit && shot.RapidFire)
                clocks.CritUntil = shot.Now + TFDamage.CritDurationRapid;
        }

        // The measured rate rides out with the answer: stock sets it inside
        // the helper before the compare, so both arms of the check carry it.
        return new TFCritResult(crit, Random: true, roll) { ObservedSet = observedSet };
    }

    /// <summary>
    /// <c>CTFWeaponBase::CanFireRandomCriticalShot</c>
    /// (<c>shared/tf/tf_weaponbase.cpp:4630-4664</c>). On the server, where the
    /// owner's round stats came with the policy: no owner, no crit
    /// (:4635-4637); a round without damage yet is the early-out — allowed,
    /// measured by neither the set nor the compare (:4648-4649); otherwise
    /// the rate is measured and <c>out</c> (stock's <c>Set</c> at :4652 runs
    /// before the compare, so the refusal carries it too), and both sides'
    /// compare follows: past the chance by more than the tenth, no random
    /// crit (:4660). Without the round's numbers — the client's replay, or a
    /// server whose stats are gone — the weapon state's stored value is what
    /// compares, and nothing is measured.
    /// </summary>
    private static bool CanFireRandomCriticalShot(in TFCritShot shot, in TFCritOwner owner,
                                                   float critChance, out float? observedSet)
    {
        observedSet = null;

        // GetTFPlayerOwner(): a weapon held by nobody has no rate to measure.
        if (shot.Command.OwnerIndex <= 0)
            return false;

        if (owner.StatsMeasured)
        {
            // "Early out" — stock returns true here, setting and comparing neither.
            if (owner.RangedDamage == 0)
                return true;

            observedSet = ObservedCritRate(owner.RandomCritDamage, owner.RangedDamage);
        }

        return (observedSet ?? owner.ObservedCritChance) <= critChance + 0.1f;
    }

    /// <summary>
    /// The observed-rate formula (<c>tf_weaponbase.cpp:4651-4652</c>): the
    /// round's random-crit damage normalised by the crit multiplier
    /// (<c>TF_DAMAGE_CRIT_MULTIPLIER</c>, <see cref="TFDamage.CritMultiplier"/>),
    /// divided by that plus the plain damage it rode — the share of the
    /// owner's expected-shot damage their random crits actually dealt.
    /// </summary>
    public static float ObservedCritRate(int randomCritDamage, int totalRangedDamage)
    {
        float normalized = (float)randomCritDamage / TFDamage.CritMultiplier;
        return normalized / (normalized + (totalRangedDamage - randomCritDamage));
    }

    private TFCritResult MeleeHelper(in TFCritShot shot, in TFCritOwner owner, in TFCritSettings settings,
                                     CritBucket bucket, ref TFCritClocks clocks)
    {
        // tf_weapon_criticals_melee 0: no random melee crits at all.
        if (settings.MeleeCrits == 0)
            return new TFCritResult(owner.CritBoosted);

        if (!owner.CanFireCriticalShot)
            return new TFCritResult(false);

        if (owner.CritBoosted)
            return new TFCritResult(true);

        float critChance = TFDamage.CritChanceMelee * owner.CritMult * owner.ChanceScale;

        bucket.Add(shot.Damage, settings.BucketCap);

        // Melee counts every check, first prediction or not.
        bucket.CountCheck();

        int roll = Draw(MeleeSeed(shot.Command));
        bool crit = roll < critChance * RandomRange;

        if (crit)
            crit = bucket.TryWithdraw(shot.Damage, isMelee: true, settings.BucketBottom);

        // The melee helper never marks its crit random (only the gun's sets
        // m_bCurrentCritIsRandom), so a melee crit is not a ranged random crit.
        return new TFCritResult(crit, Random: false, roll);
    }

    /// <summary><c>RandomSeed(seed); RandomInt(0, WEAPON_RANDOM_RANGE - 1)</c>.</summary>
    public int Draw(int seed)
    {
        _stream.SetSeed(seed);
        return _stream.RandomInt(0, RandomRange - 1);
    }
}
