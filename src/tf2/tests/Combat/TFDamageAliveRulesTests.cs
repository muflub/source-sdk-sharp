using SourceSharp.Abi;

using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFDamageAliveRules"/>: the resist arms of stock's
/// <c>ApplyOnDamageAliveModifyRules</c> (<c>tf_gamerules.cpp:7191-7336</c>).
/// Every fact wears one 10-damage hit and the attributes it names; the
/// expected number is the hit through those attributes, worked by hand.
/// </summary>
public sealed class TFDamageAliveRulesTests
{
    private static IAttributeProvider Item(AttributeDescriptionFormat format, params (string Hook, float Value)[] attrs)
        => Mixed(attrs.Select(a => (a.Hook, a.Value, format)).ToArray());

    private static IAttributeProvider Mixed(params (string Hook, float Value, AttributeDescriptionFormat Format)[] attrs)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        for (int i = 0; i < attrs.Length; i++)
        {
            container.Set(AttributeValue.OfFloat(new AttributeDefinition(i + 1, "resist " + i, attrs[i].Hook,
                                        attrs[i].Format, AttributeEffectType.Neutral, false, false,
                                        AttributeValueType.Default, MergedKeys.Block("x")), attrs[i].Value));
        }
        return container;
    }

    private static IAttributeProvider Victim(params (string Hook, float Value)[] attrs)
        => Item(AttributeDescriptionFormat.Percentage, attrs);

    private static float Resist(float damage, DamageType bits, IAttributeProvider? victim = null,
                                IAttributeProvider? weaponItem = null, bool self = false, float bonus = 0f,
                                IAttributeProvider? held = null, TFCondMask victimConds = default,
                                TFClass victimClass = TFClass.Undefined, TFDamageCustom custom = TFDamageCustom.None)
    {
        var hit = new TFHitResult(damage, bits, TFCritType.None, custom, bonus);
        TFHit input = new(damage, bits, custom, self, AttackerIsPlayer: true, TFWeaponId.Pistol,
                          null, victimConds, default, weaponItem, VictimItem: held);
        return TFDamageAliveRules.Resist(hit, in input, new AttributeHooks(), victim, victimClass, 0.8f);
    }

    // ---- one resist per damage type ---------------------------------------

    [Fact]
    public void A_bullets_hit_takes_the_bullet_resist()
    {
        const DamageType bullets = DamageType.Bullet;
        Assert.Equal(5f, Resist(10f, bullets, victim: Victim(("mult_dmgtaken_from_bullets", 0.5f))), 4);
    }

    [Fact]
    public void A_blast_hit_takes_no_bullet_resist()
    {
        const DamageType blast = DamageType.Blast;
        Assert.Equal(10f, Resist(10f, blast, victim: Victim(("mult_dmgtaken_from_bullets", 0.5f))), 4);
    }

    [Fact]
    public void A_burn_hit_takes_the_fire_resist()
    {
        const DamageType burn = DamageType.Burn;
        Assert.Equal(5f, Resist(10f, burn, victim: Victim(("mult_dmgtaken_from_fire", 0.5f))), 4);
    }

    [Fact]
    public void An_ignite_hit_takes_the_fire_resist()
    {
        const DamageType ignite = TFDamageBits.Ignite;
        Assert.Equal(5f, Resist(10f, ignite, victim: Victim(("mult_dmgtaken_from_fire", 0.5f))), 4);
    }

    [Fact]
    public void A_plain_bullet_hit_takes_no_fire_resist()
    {
        const DamageType bullets = DamageType.Bullet;
        Assert.Equal(10f, Resist(10f, bullets, victim: Victim(("mult_dmgtaken_from_fire", 0.5f))), 4);
    }

    [Fact]
    public void A_blast_from_another_takes_the_explosions_resist()
    {
        const DamageType blast = DamageType.Blast;
        Assert.Equal(5f, Resist(10f, blast, victim: Victim(("mult_dmgtaken_from_explosions", 0.5f))), 4);
    }

    [Fact]
    public void The_victims_own_blast_takes_no_explosions_resist()
    {
        const DamageType blast = DamageType.Blast;
        Assert.Equal(10f, Resist(10f, blast, victim: Victim(("mult_dmgtaken_from_explosions", 0.5f)), self: true), 4);
    }

    [Fact]
    public void A_melee_hit_takes_the_melee_resist()
    {
        const DamageType melee = TFDamageBits.Melee;
        Assert.Equal(5f, Resist(10f, melee, victim: Victim(("mult_dmgtaken_from_melee", 0.5f))), 4);
    }

    [Fact]
    public void A_hit_of_both_bits_takes_both_resists()
    {
        // The flaregun's row, stock's own example (:7219): DMG_BULLET|DMG_IGNITE.
        const DamageType flare = DamageType.Bullet | TFDamageBits.Ignite;
        var victim = Victim(("mult_dmgtaken_from_fire", 0.5f), ("mult_dmgtaken_from_bullets", 0.5f));
        Assert.Equal(2.5f, Resist(10f, flare, victim: victim), 4);
    }

    // ---- the split and the order -------------------------------------------

    [Fact]
    public void The_crit_resist_takes_the_bonus_and_leaves_the_base()
    {
        // 30 = 10 base + 20 crit bonus; half the bonus is 10, the base none.
        const DamageType crit = DamageType.Bullet | TFDamageBits.Critical;
        Assert.Equal(20f, Resist(30f, crit, victim: Victim(("mult_dmgtaken_from_crit", 0.5f)), bonus: 20f), 4);
    }

    [Fact]
    public void The_all_sources_resist_takes_the_reassembled_whole()
    {
        // After the reassembly (:7284) the bonus is back in, so the (:7327)
        // multiplier rides 30, not the 10 base.
        const DamageType crit = DamageType.Bullet | TFDamageBits.Critical;
        Assert.Equal(15f, Resist(30f, crit, victim: Victim(("mult_dmgtaken", 0.5f)), bonus: 20f), 4);
    }

    [Fact]
    public void The_all_sources_resist_is_taken_after_the_typed_one()
    {
        // 10, halved by the bullet resist (:7255), halved again by
        // mult_dmgtaken (:7327).
        const DamageType bullets = DamageType.Bullet;
        var victim = Victim(("mult_dmgtaken_from_bullets", 0.5f), ("mult_dmgtaken", 0.5f));
        Assert.Equal(2.5f, Resist(10f, bullets, victim: victim), 4);
    }

    // ---- the pierce ---------------------------------------------------------

    [Fact]
    public void A_piercing_weapon_skips_the_typed_resist_but_not_the_last()
    {
        const DamageType bullets = DamageType.Bullet;
        var victim = Victim(("mult_dmgtaken", 0.5f));
        var weapon = Item(AttributeDescriptionFormat.Or, ("mod_pierce_resists_absorbs", 1f));
        Assert.Equal(5f, Resist(10f, bullets, victim: victim, weaponItem: weapon), 4);
    }

    // ---- the victim's held weapon (:7223, :7328, :7375-7387) ----------------

    [Fact]
    public void A_burn_hit_takes_the_held_weapons_fire_resist()
    {
        const DamageType burn = DamageType.Burn;
        var held = Item(AttributeDescriptionFormat.Percentage, ("mult_dmgtaken_from_fire_active", 0.5f));
        Assert.Equal(5f, Resist(10f, burn, held: held), 4);
    }

    [Fact]
    public void A_bullet_hit_takes_no_held_fire_resist()
    {
        const DamageType bullets = DamageType.Bullet;
        var held = Item(AttributeDescriptionFormat.Percentage, ("mult_dmgtaken_from_fire_active", 0.5f));
        Assert.Equal(10f, Resist(10f, bullets, held: held), 4);
    }

    [Fact]
    public void The_held_all_sources_resist_takes_the_reassembled_whole()
    {
        // After the reassembly (:7284) the bonus is back in, so :7328 rides
        // 30, not the 10 base.
        const DamageType crit = DamageType.Bullet | TFDamageBits.Critical;
        var held = Item(AttributeDescriptionFormat.Percentage, ("mult_dmgtaken_active", 0.5f));
        Assert.Equal(15f, Resist(30f, crit, held: held, bonus: 20f), 4);
    }

    [Fact]
    public void A_piercing_weapon_leaves_the_held_all_sources_resist_standing()
    {
        // :7328 is outside the pierce gate, as :7375 is inside it.
        const DamageType bullets = DamageType.Bullet;
        var held = Item(AttributeDescriptionFormat.Percentage, ("mult_dmgtaken_active", 0.5f));
        var weapon = Item(AttributeDescriptionFormat.Or, ("mod_pierce_resists_absorbs", 1f));
        Assert.Equal(5f, Resist(10f, bullets, weaponItem: weapon, held: held), 4);
    }

    [Fact]
    public void A_club_hit_takes_the_held_melee_resist()
    {
        const DamageType club = DamageType.Club;
        var held = Item(AttributeDescriptionFormat.Percentage, ("dmg_from_melee", 0.5f));
        Assert.Equal(5f, Resist(10f, club, held: held), 4);
    }

    [Fact]
    public void A_blast_hit_takes_no_held_melee_resist()
    {
        const DamageType blast = DamageType.Blast;
        var held = Item(AttributeDescriptionFormat.Percentage, ("dmg_from_melee", 0.5f));
        Assert.Equal(10f, Resist(10f, blast, held: held), 4);
    }

    [Fact]
    public void A_blast_hit_takes_the_held_ranged_resist()
    {
        const DamageType blast = DamageType.Blast;
        var held = Item(AttributeDescriptionFormat.Percentage, ("dmg_from_ranged", 0.5f));
        Assert.Equal(5f, Resist(10f, blast, held: held), 4);
    }

    [Fact]
    public void A_club_and_blast_hit_takes_only_the_held_melee_resist()
    {
        // Stock's arms are an else-if (:7377-7386): the club takes the melee
        // arm and never the ranged one.
        const DamageType clubAndBlast = DamageType.Club | DamageType.Blast;
        var held = Item(AttributeDescriptionFormat.Percentage, ("dmg_from_melee", 0.5f), ("dmg_from_ranged", 0.5f));
        Assert.Equal(5f, Resist(10f, clubAndBlast, held: held), 4);
    }

    [Fact]
    public void A_piercing_weapon_skips_the_held_melee_and_ranged_resists()
    {
        const DamageType clubAndBlast = DamageType.Club | DamageType.Blast;
        var held = Item(AttributeDescriptionFormat.Percentage, ("dmg_from_melee", 0.5f), ("dmg_from_ranged", 0.5f));
        var weapon = Item(AttributeDescriptionFormat.Or, ("mod_pierce_resists_absorbs", 1f));
        Assert.Equal(10f, Resist(10f, clubAndBlast, weaponItem: weapon, held: held), 4);
    }

    [Fact]
    public void A_player_holding_no_weapon_takes_no_held_resist()
    {
        const DamageType burn = DamageType.Burn | DamageType.Club;
        var held = Item(AttributeDescriptionFormat.Percentage, ("mult_dmgtaken_from_fire_active", 0.5f),
                        ("dmg_from_melee", 0.5f));
        Assert.Equal(10f, Resist(10f, burn, held: null), 4);
    }

    // ---- the attacker's held weapon vs burning (:7229-7232) -----------------

    [Fact]
    public void A_burning_victim_takes_the_attackers_held_damage_vs_burning()
    {
        // A bullet: the arm keys on the victim's burning, not the hit's type.
        const DamageType bullets = DamageType.Bullet;
        var weapon = Item(AttributeDescriptionFormat.Percentage, ("mult_dmg_vs_burning", 0.5f));
        Assert.Equal(5f, Resist(10f, bullets, weaponItem: weapon, victimConds: TFCondMask.Of(TFCond.Burning)), 4);
    }

    [Fact]
    public void An_unburning_victim_takes_no_damage_vs_burning()
    {
        const DamageType bullets = DamageType.Bullet;
        var weapon = Item(AttributeDescriptionFormat.Percentage, ("mult_dmg_vs_burning", 0.5f));
        Assert.Equal(10f, Resist(10f, bullets, weaponItem: weapon), 4);
    }

    [Fact]
    public void A_piercing_attack_skips_the_damage_vs_burning()
    {
        const DamageType bullets = DamageType.Bullet;
        var weapon = Mixed(("mult_dmg_vs_burning", 0.5f, AttributeDescriptionFormat.Percentage),
                           ("mod_pierce_resists_absorbs", 1f, AttributeDescriptionFormat.Or));
        Assert.Equal(10f, Resist(10f, bullets, weaponItem: weapon,
                                 victimConds: TFCondMask.Of(TFCond.Burning)), 4);
    }

    [Fact]
    public void The_layer_gives_the_burn_arm_the_victims_carrier_burning()
    {
        var rig = new CombatRig();
        rig.Conditions.Add(CombatRig.Victim, TFCond.Burning);
        rig.AttackerItem("mult_dmg_vs_burning", AttributeDescriptionFormat.Percentage, 0.5f);
        Assert.Equal(5f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    [Fact]
    public void An_unburning_victim_takes_no_damage_vs_burning_through_the_layer()
    {
        var rig = new CombatRig();
        rig.AttackerItem("mult_dmg_vs_burning", AttributeDescriptionFormat.Percentage, 0.5f);
        Assert.Equal(10f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    // ---- the layer hands the resists the victim's held weapon ---------------

    [Fact]
    public void The_layer_gives_the_resists_the_victims_held_weapon()
    {
        // The pistol's own table makes the hit a bullet, so the held weapon's
        // all-sources resist (:7328) is the arm that shows.
        var rig = new CombatRig();
        rig.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_pistol";
        rig.Owners.Items[CombatRig.Victim.EntryIndex] =
            Item(AttributeDescriptionFormat.Percentage, ("mult_dmgtaken_active", 0.5f));
        Assert.Equal(5f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    // ---- the spun-up heavy, through the layer's gate (:7266) ----------------

    private CombatRig SpunUpRig(int health)
    {
        var rig = new CombatRig();
        rig.VictimState.SetClass(TFClass.HeavyWeapons);
        rig.Conditions.Add(CombatRig.Victim, TFCond.Aiming);
        rig.Resupply.MaxHealthAnswer = 300;
        rig.Engine.Returns("Entities.GetHealth", health);
        rig.Owners.Managers[CombatRig.Victim.EntryIndex] =
            Item(AttributeDescriptionFormat.Percentage, ("spunup_damage_resistance", 0.5f));
        return rig;
    }

    [Fact]
    public void A_spun_up_heavy_hit_below_half_takes_the_resist()
    {
        // (100 - 10) / 300 = 0.3, at the gate; the 10 halves.
        Assert.Equal(5f, SpunUpRig(health: 100).Hit(10f, DamageType.Bullet), 4);
    }

    [Fact]
    public void A_spun_up_heavy_hit_above_half_takes_none()
    {
        // (290 - 10) / 300 = 0.933..., past the gate.
        Assert.Equal(10f, SpunUpRig(health: 290).Hit(10f, DamageType.Bullet), 4);
    }

    [Fact]
    public void A_heavy_not_aiming_takes_no_spun_up_resist()
    {
        var rig = SpunUpRig(health: 100);
        rig.Conditions.Remove(CombatRig.Victim, TFCond.Aiming);
        Assert.Equal(10f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    [Fact]
    public void Another_class_aiming_takes_no_spun_up_resist()
    {
        var rig = new CombatRig();
        rig.Conditions.Add(CombatRig.Victim, TFCond.Aiming);
        rig.Resupply.MaxHealthAnswer = 300;
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.Owners.Managers[CombatRig.Victim.EntryIndex] =
            Item(AttributeDescriptionFormat.Percentage, ("spunup_damage_resistance", 0.5f));
        Assert.Equal(10f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    // ---- the sentry's arm, through the layer's inflictor (:7330-7337) -------

    [Fact]
    public void A_sentrys_hit_takes_the_sentry_resist()
    {
        var buildings = new TF2.Tests.Buildings.BuildingsRig();
        var sentry = buildings.Spawn<TFSentryGun>(TFSentryGun.MapClass);

        var rig = new CombatRig();
        rig.Entities.Placed[sentry.Handle] = sentry;
        rig.Owners.Managers[CombatRig.Victim.EntryIndex] =
            Item(AttributeDescriptionFormat.Percentage, ("dmg_from_sentry_reduced", 0.5f));

        using (rig.Inflictors.Push(sentry.Handle, default))
            Assert.Equal(5f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    [Fact]
    public void A_players_hit_takes_no_sentry_resist()
    {
        var rig = new CombatRig();
        rig.Owners.Managers[CombatRig.Victim.EntryIndex] =
            Item(AttributeDescriptionFormat.Percentage, ("dmg_from_sentry_reduced", 0.5f));
        Assert.Equal(10f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    // ---- the cloaked spy (:7390-7428) ---------------------------------------

    [Fact]
    public void A_cloaked_spys_hit_takes_the_stealth_reduction()
    {
        const DamageType bullets = DamageType.Bullet;
        Assert.Equal(8f, Resist(10f, bullets, victimConds: TFCondMask.Of(TFCond.Stealthed),
                                victimClass: TFClass.Spy), 4);
    }

    [Fact]
    public void A_visible_spy_takes_no_stealth_reduction()
    {
        const DamageType bullets = DamageType.Bullet;
        Assert.Equal(10f, Resist(10f, bullets, victimClass: TFClass.Spy), 4);
    }

    [Fact]
    public void A_taunting_cloaked_spy_takes_no_stealth_reduction()
    {
        const DamageType bullets = DamageType.Bullet;
        Assert.Equal(10f, Resist(10f, bullets, victimClass: TFClass.Spy,
                                 victimConds: TFCondMask.Of([TFCond.Taunting, TFCond.Stealthed])), 4);
    }

    [Fact]
    public void A_cloaked_spy_of_no_class_takes_no_stealth_reduction()
    {
        const DamageType bullets = DamageType.Bullet;
        Assert.Equal(10f, Resist(10f, bullets, victimConds: TFCondMask.Of(TFCond.Stealthed)), 4);
    }

    [Fact]
    public void A_telefrag_takes_no_stealth_reduction()
    {
        const DamageType telefrag = DamageType.Blast;
        Assert.Equal(10f, Resist(10f, telefrag, victimClass: TFClass.Spy,
                                 victimConds: TFCondMask.Of(TFCond.Stealthed),
                                 custom: TFDamageCustom.Telefrag), 4);
    }

    [Fact]
    public void A_piercing_attack_skips_the_stealth_reduction()
    {
        const DamageType bullets = DamageType.Bullet;
        var weapon = Item(AttributeDescriptionFormat.Or, ("mod_pierce_resists_absorbs", 1f));
        Assert.Equal(10f, Resist(10f, bullets, victimClass: TFClass.Spy, weaponItem: weapon,
                                 victimConds: TFCondMask.Of(TFCond.Stealthed)), 4);
    }

    [Fact]
    public void A_feigning_spy_takes_no_stealth_reduction()
    {
        // The feign arm ahead of the stealth one (:7393-7423) is the dead
        // ringer's; until its ready bit lands the else-if leaves a feigning
        // spy the plain hit.
        const DamageType bullets = DamageType.Bullet;
        Assert.Equal(10f, Resist(10f, bullets, victimClass: TFClass.Spy,
                                 victimConds: TFCondMask.Of([TFCond.FeignDeath, TFCond.Stealthed])), 4);
    }

    [Fact]
    public void The_layer_reduces_a_cloaked_spys_hit()
    {
        var rig = new CombatRig();
        rig.VictimState.SetClass(TFClass.Spy);
        rig.Conditions.Add(CombatRig.Victim, TFCond.Stealthed);
        Assert.Equal(8f, rig.Hit(10f, DamageType.Bullet), 4);
    }

    [Fact]
    public void The_layer_leaves_a_visible_spy_the_plain_hit()
    {
        var rig = new CombatRig();
        rig.VictimState.SetClass(TFClass.Spy);
        Assert.Equal(10f, rig.Hit(10f, DamageType.Bullet), 4);
    }
}
