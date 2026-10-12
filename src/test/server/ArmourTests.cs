using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What armour soaks.
///
/// Worth testing more than most things here: every branch is a number a player
/// feels and nobody can see, and the rule only moved out of C++ at all because
/// the native block that used to apply it is now guarded away. If this is wrong,
/// armour silently does nothing — or protects twice as well as it should — and
/// the only symptom is a firefight that feels off.
/// </summary>
public class ArmourTests
{

    [Fact]
    public void StockRatio()
    {
        // 100 damage, plenty of armour: 20% reaches health, 80 points of armour
        // go at the modern bonus of 1.0.
        var r = Armour.Apply(damage: 100f, armour: 100, damageType: DamageType.Bullet);

        Assert.True(Math.Abs(r.Damage - 20f) < 0.001f, "a fifth of the damage reaches health");
        Assert.True(r.ArmourLeft == 20, "the rest is taken out of armour");
        Assert.True(Math.Abs(r.Absorbed - 80f) < 0.001f, "and it reports what it absorbed");
    }

    [Fact]
    public void Overflow()
    {
        // Only 10 armour against a hit that would consume 80. The remaining
        // armour still does its proportional job rather than vanishing: it is
        // converted back through the bonus to find how much damage it stopped.
        var r = Armour.Apply(damage: 100f, armour: 10, damageType: DamageType.Bullet);

        Assert.True(r.ArmourLeft == 0, "armour is emptied");
        Assert.True(Math.Abs(r.Absorbed - 10f) < 0.001f, "it absorbed only what it had");
        Assert.True(Math.Abs(r.Damage - 90f) < 0.001f, "and stopped exactly that much damage");

        // The old rule made a point of armour worth two of health, so the same
        // ten points stop twice as much.
        var old = Armour.Apply(damage: 100f, armour: 10, damageType: DamageType.Bullet,
                               ratio: Armour.DefaultRatio, bonus: 0.5f);

        Assert.True(Math.Abs(old.Damage - 80f) < 0.001f, "at the old bonus the same armour stops twice as much");
    }

    [Fact]
    public void Unprotected()
    {
        // A vest does not help you hit the ground more softly. Stock's list, and
        // getting it wrong is invisible until someone survives a fall they
        // should not have.
        foreach (var (name, bit) in new[]
        {
            ("falling", DamageType.Fall), ("drowning", DamageType.Drown),
            ("poison", DamageType.Poison), ("radiation", DamageType.Radiation),
        })
        {
            var r = Armour.Apply(damage: 50f, armour: 100, damageType: bit);

            Assert.True(r.Damage == 50f && r.ArmourLeft == 100 && r.Absorbed == 0f, $"armour does not protect against {name}");
        }

        // But it does against everything else.
        var shot = Armour.Apply(damage: 50f, armour: 100, damageType: DamageType.Bullet);
        Assert.True(shot.Damage < 50f, "armour does protect against bullets");
    }

    [Fact]
    public void Edges()
    {
        var none = Armour.Apply(damage: 50f, armour: 0, damageType: DamageType.Bullet);
        Assert.True(none.Damage == 50f && none.Absorbed == 0f, "no armour changes nothing");

        var zero = Armour.Apply(damage: 0f, armour: 100, damageType: DamageType.Bullet);
        Assert.True(zero.ArmourLeft == 100 && zero.Absorbed == 0f, "no damage spends no armour");

        // A minimum of one point per hit, or a stream of tiny hits passes
        // through armour untouched.
        var tiny = Armour.Apply(damage: 0.5f, armour: 100, damageType: DamageType.Bullet);
        Assert.True(tiny.ArmourLeft == 99, "a tiny hit still costs a point of armour");

        // Damage never comes back negative, however the overflow lands.
        var odd = Armour.Apply(damage: 1f, armour: 1, damageType: DamageType.Bullet);
        Assert.True(odd.Damage >= 0f, "damage is never negative");
    }
    /// <summary>
    /// Stock's "blasts damage armor more": DMG_BLAST in a multiplayer game
    /// doubles the bonus, so a 50 blast into 100 armour spends 80, not 40.
    /// </summary>
    [Fact]
    public void A_multiplayer_blast_spends_armour_at_double_the_bonus()
    {
        var r = Armour.Apply(50f, 100, DamageType.Blast, multiplayer: true);
        Assert.Equal((10f, 20, 80f), (r.Damage, r.ArmourLeft, r.Absorbed));

        var single = Armour.Apply(50f, 100, DamageType.Blast, multiplayer: false);
        Assert.Equal(60, single.ArmourLeft);   // the control: no doubling outside multiplayer
    }

    /// <summary>A doubled blast that empties the suit converts the rest back at the doubled bonus.</summary>
    [Fact]
    public void A_doubled_blast_into_thin_armour_stops_half_the_armour_in_damage()
    {
        var r = Armour.Apply(50f, 10, DamageType.Blast, multiplayer: true);
        Assert.Equal((45f, 0, 10f), (r.Damage, r.ArmourLeft, r.Absorbed));
    }

    /// <summary>
    /// Stock subtracts the float and truncates the RESULT (int m_ArmorValue -=
    /// float flArmor): 100 - 20.5 is 79, not 100 - 20 = 80.
    /// </summary>
    [Fact]
    public void Armour_is_truncated_after_the_subtraction()
    {
        var r = Armour.Apply(25.625f, 100, DamageType.Bullet);   // 20.5 from armour
        Assert.Equal(79, r.ArmourLeft);
    }
}
