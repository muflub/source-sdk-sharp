//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Player;

/// <summary>
/// The scout's hype — stock's <c>m_flHypeMeter</c>
/// (<see cref="TFPlayerNetState.HypeMeter"/>), the Soda Popper's meter. The
/// gain is the dealt damage remapped
/// 1..200 onto 1..50 and held at 100
/// (<c>CTFGameRules::ApplyOnDamageAliveModifyRules</c>,
/// tf_gamerules.cpp:7526-7532, run for the attacker while it is not self
/// damage, :7490); the setter refuses gains while the hype buff is on and
/// clamps 0..100 (<c>CTFPlayerShared::SetScoutHypeMeter</c>,
/// tf_player_shared.cpp:14124-14141). The drink spends it
/// (<c>UpdateEnergyDrinkMeter</c>, :14071-14078).
/// </summary>
public static class TFHype
{
    /// <summary>The item attribute arming the gain (<c>hype_on_damage</c>,
    /// the gamerules hook :7527).</summary>
    public const string GainAttribute = "hype_on_damage";

    /// <summary>The item attribute arming the boost gain
    /// (<c>boost_on_damage</c>, tf_weaponbase.cpp:5301).</summary>
    public const string BoostAttribute = "boost_on_damage";

    /// <summary>The item attribute pricing the dash's hype
    /// (<c>hype_resets_on_jump</c>, tf_gamemovement.cpp:1009).</summary>
    public const string JumpResetAttribute = "hype_resets_on_jump";

    /// <summary>The item attribute pricing the taken hit's hype
    /// (<c>lose_hype_on_take_damage</c>, tf_player.cpp:9766).</summary>
    public const string TakenDamageAttribute = "lose_hype_on_take_damage";

    /// <summary>A full meter: the drink's activation gate
    /// (<c>CTFSodaPopper::SecondaryAttack</c>, tf_weapon_shotgun.cpp:490) and
    /// the gain's cap (:7531).</summary>
    public const float FullMeter = 100f;

    /// <summary>The damage the remap's floor sits at (:7530's 1).</summary>
    public const float DamageFloor = 1f;

    /// <summary>The damage the remap's ceiling sits at (:7530's 200).</summary>
    public const float DamageCeiling = 200f;

    /// <summary>The gain at the floor damage (:7530's 1).</summary>
    public const float GainFloor = 1f;

    /// <summary>The gain at the ceiling damage (:7530's 50).</summary>
    public const float GainCeiling = 50f;

    /// <summary><c>tf_scout_hype_pep_max</c>, the boost gain's cap
    /// (tf_weaponbase.cpp:80; the cheat convar's default is the constant).</summary>
    public const float PepMax = 99f;

    /// <summary><c>tf_scout_hype_pep_min_damage</c>, the boost gain's damage
    /// floor (tf_weaponbase.cpp:81).</summary>
    public const float PepMinDamage = 5f;

    /// <summary><c>tf_scout_hype_pep_mod</c>, the boost gain's divisor
    /// (tf_weaponbase.cpp:79).</summary>
    public const float PepMod = 1f;

    /// <summary>
    /// Book a dealt hit on the scout's meter: the damage remapped 1..200 onto
    /// 1..50, held at full as stock's <c>Min(100.f, ...)</c> (:7531), the
    /// setter under it.
    /// </summary>
    public static void GainOnDamage(TFPlayerNetState player, float damage)
    {
        float gain = CritBucket.RemapValClamped(damage, DamageFloor, DamageCeiling, GainFloor, GainCeiling);
        Set(player, Math.Min(FullMeter, gain + player.HypeMeter));
    }

    /// <summary>
    /// The BFB's boost gain (<c>tf_weaponbase.cpp:5303-5308</c>): the damage
    /// floored at the pep minimum over the pep modulus, held at the pep max.
    /// </summary>
    public static void GainBoostOnDamage(TFPlayerNetState player, float damage)
        => Set(player, Math.Min(PepMax, player.HypeMeter + MathF.Max(PepMinDamage, damage) / PepMod));

    /// <summary>
    /// Take hype off the meter, the dash's toll and the taken hit's alike —
    /// stock's <c>SetScoutHypeMeter(meter - x)</c> calls
    /// (tf_gamemovement.cpp:1013-1014, tf_player.cpp:9770-9771).
    /// </summary>
    public static void Lose(TFPlayerNetState player, float amount) => Set(player, player.HypeMeter - amount);

    /// <summary>
    /// <c>CTFPlayerShared::SetScoutHypeMeter</c>
    /// (tf_player_shared.cpp:14124-14141): while the hype buff runs no write
    /// lands (<c>IsHypeBuffed</c>'s out, :14126-14127) and the value clamps
    /// 0..100 (:14129).
    /// </summary>
    private static void Set(TFPlayerNetState player, float val)
    {
        if (player.Conditions().Has(TFCond.SodapopperHype))
            return;

        player.HypeMeter = Math.Clamp(val, 0f, FullMeter);
    }
}
