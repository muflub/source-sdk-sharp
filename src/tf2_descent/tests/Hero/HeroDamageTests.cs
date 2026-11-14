using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Hero;
using TF2.Shared.Combat;

using Xunit;

using Sheet = TF2.Descent.Tests.Hero.StatsSheet;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// HE3, the hero's numbers on a hit (<see cref="HeroDamage"/>), applied after
/// TF2's layer: each fact hands it the number TF2's layer left (crit,
/// ramp-up and fall-off already in) and asserts the hero's multiplier on top,
/// never a re-derivation of TF2's.
/// </summary>
public sealed class HeroDamageTests
{
    private const string Rocket = "tf_weapon_rocketlauncher";
    private const string Shotgun = "tf_weapon_shotgun_soldier";
    private const string Shovel = "tf_weapon_shovel";

    /// <summary>A player who is no hero (a second client before it joins, a bot).</summary>
    private static readonly EntityHandle Stranger = new(0x0001_0005u);

    /// <summary>A target that runs the damage rules (the dummy, a robot body).</summary>
    private static readonly EntityHandle Target = new(0x0003_0200u);

    //-------------------------------------------------------------------------
    // Outgoing: three sheets, three weapons
    //-------------------------------------------------------------------------

    // Level 1: Brawn 20 (+20 % melee, +10 % blast), Aim 20 (+20 % bullet and projectile).
    // Level 10: Aim 45. With gear: +20 % blast and +5 % all active; +50 % bullet inactive and +50 % melee broken count nothing.
    [Theory]
    [InlineData(Sheet.Level1, Rocket, 90f, 117f)]                    // x 1.30: blast 10 + projectile 20
    [InlineData(Sheet.Level1, Shotgun, 60f, 72f)]                    // x 1.20: bullet 20
    [InlineData(Sheet.Level1, Shovel, 65f, 78f)]                     // x 1.20: melee 20
    [InlineData(Sheet.Level10, Rocket, 90f, 139.5f)]                 // x 1.55: blast 10 + projectile 45
    [InlineData(Sheet.Level10, Shotgun, 60f, 87f)]                   // x 1.45: bullet 45
    [InlineData(Sheet.Level10, Shovel, 65f, 78f)]                    // x 1.20: melee 20
    [InlineData(Sheet.Level10Contributions, Rocket, 90f, 162f)]      // x 1.80: all 5 + blast 30 + projectile 45
    [InlineData(Sheet.Level10Contributions, Shotgun, 60f, 90f)]      // x 1.50: all 5 + bullet 45 (the inactive 50 counts nothing)
    [InlineData(Sheet.Level10Contributions, Shovel, 65f, 81.25f)]    // x 1.25: all 5 + melee 20 (the broken 50 counts nothing)
    public void A_heros_hit_is_TF2s_number_times_the_rules_multiplier_for_the_weapons_kinds(Sheet sheet, string weapon,
                                                                                         float tf, float expected)
    {
        var rig = new HeroStatsRig(sheet);
        var kinds = HeroHitKinds.KindsOf(new HeroHitKinds(rig.Rig.Rules).FamilyOf(weapon)!);

        float dealt = rig.Hit(Target, rig.Player, tf, DamageType.Blast, weapon);

        Assert.Equal(expected, dealt, 3);
        Assert.Equal(tf * rig.Hero.Stats!.DamageMultiplier(kinds), dealt, 3);
    }

    // Level 10 with gear: +5 % all active, and Smarts 5 is +5 % action. A skill's hit takes those two
    // and nothing of the launcher in hand (its blast 30 and projectile 45 would have made it x 1.80).
    [Fact]
    public void A_skills_hit_is_an_action_whatever_weapon_is_in_hand()
    {
        var rig = new HeroStatsRig(Sheet.Level10Contributions);
        rig.Held.ItemClass[rig.Player] = Rocket;
        rig.SkillHit.Skill = "patriot_missile";

        float dealt = rig.Damage.Modify(new DamageContext(Target, rig.Player, 100f, (int)DamageType.Blast), 100f);

        Assert.Equal(100f * rig.Hero.Stats!.DamageMultiplier(["action"]), dealt, 3);
        Assert.Equal(110f, dealt, 3);
    }

    [Fact]
    public void A_skills_hit_is_the_same_with_the_shovel_in_hand_and_with_the_launcher()
    {
        var rig = new HeroStatsRig(Sheet.Level10Contributions);
        rig.SkillHit.Skill = "patriot_missile";

        rig.Held.ItemClass[rig.Player] = Rocket;
        float launcher = rig.Damage.Modify(new DamageContext(Target, rig.Player, 100f, (int)DamageType.Blast), 100f);
        rig.Held.ItemClass[rig.Player] = Shovel;
        float shovel = rig.Damage.Modify(new DamageContext(Target, rig.Player, 100f, (int)DamageType.Blast), 100f);

        Assert.Equal(launcher, shovel, 3);

        // The control: with no skill in flight the two weapons do differ, so the fact can fail.
        rig.SkillHit.Skill = null;
        rig.Held.ItemClass[rig.Player] = Rocket;
        float rocket = rig.Damage.Modify(new DamageContext(Target, rig.Player, 100f, (int)DamageType.Blast), 100f);
        Assert.NotEqual(rocket, shovel, 3);
    }

    [Fact]
    public void A_crit_TF2_already_tripled_takes_the_heros_multiplier_on_top()
    {
        var rig = new HeroStatsRig();
        rig.Held.ItemClass[rig.Player] = Rocket;

        float dealt = rig.Damage.Modify(new DamageContext(Target, rig.Player, 270f, (int)DamageType.Blast, Critical: true), 270f);

        Assert.Equal(351f, dealt, 3);
    }

    [Fact]
    public void A_heros_hit_on_a_player_who_is_no_hero_takes_the_multiplier()
    {
        var rig = new HeroStatsRig();

        Assert.Equal(117f, rig.Hit(Stranger, rig.Player, 90f, DamageType.Blast, Rocket), 3);
    }

    [Fact]
    public void A_weapon_in_no_family_takes_the_all_damage_stat_alone()
    {
        var rig = new HeroStatsRig(Sheet.Level10Contributions);

        Assert.Equal(105f, rig.Hit(Target, rig.Player, 100f, DamageType.Generic, "tf_weapon_unknown"), 3);
    }

    [Fact]
    public void A_hit_from_no_hero_is_left_as_TF2_dealt_it()
    {
        var rig = new HeroStatsRig();

        Assert.Equal(90f, rig.Hit(Target, Stranger, 90f, DamageType.Blast, Rocket), 3);
    }

    //-------------------------------------------------------------------------
    // The weapon's family and the hit's type
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(Rocket, "rocket_launcher")]
    [InlineData("tf_weapon_rocketlauncher_directhit", "rocket_launcher")]
    [InlineData(Shotgun, "shotgun")]
    [InlineData("tf_weapon_shotgun_hwg", "shotgun")]
    [InlineData("tf_weapon_shotgun_primary", "shotgun")]
    [InlineData("tf_weapon_pistol_scout", "pistol")]
    [InlineData(Shovel, "melee")]
    public void A_weapons_item_class_finds_its_family(string itemClass, string family)
    {
        Assert.Equal(family, new HeroHitKinds(new TF2.Descent.Rules.TF2DescentRules()).FamilyOf(itemClass)?.Id);
    }

    [Fact]
    public void No_weapon_and_an_unknown_one_have_no_family()
    {
        var kinds = new HeroHitKinds(new TF2.Descent.Rules.TF2DescentRules());

        Assert.Null(kinds.FamilyOf(null));
        Assert.Null(kinds.FamilyOf("tf_weapon_unknown"));
    }

    [Fact]
    public void Every_family_the_rules_ship_has_its_kinds_stated()
    {
        var families = new TF2.Descent.Rules.TF2DescentRules().Content.Families.Rows;

        var unstated = families.Where(f => HeroHitKinds.KindsOf(f).Length == 0).Select(f => f.Id).ToArray();

        Assert.True(families.Count >= 18, $"only {families.Count} families");
        Assert.Empty(unstated);
    }

    [Theory]
    [InlineData(DamageType.Burn, "fire")]
    [InlineData(TFDamageBits.Ignite, "fire")]
    [InlineData(DamageType.Blast | TFDamageBits.HalfFalloff | TFDamageBits.UseDistanceMod, "blast")]
    [InlineData(DamageType.Bullet, "bullet")]
    [InlineData(DamageType.Buckshot | TFDamageBits.UseDistanceMod, "bullet")]
    [InlineData(DamageType.Club, "bullet")]
    [InlineData(DamageType.Slash, "bullet")]
    [InlineData(TFDamageBits.Melee, "bullet")]
    [InlineData(DamageType.Shock, "shock")]
    [InlineData(DamageType.Fall, null)]
    [InlineData(DamageType.Generic, null)]
    public void A_TF_hit_is_resisted_as_the_type_its_bits_name(DamageType bits, string? type)
    {
        Assert.Equal(type, HeroHitKinds.ResistedAs(bits));
    }

    //-------------------------------------------------------------------------
    // Incoming: armour, then the resistance
    //-------------------------------------------------------------------------

    [Fact]
    public void A_hit_on_a_hero_loses_the_armours_share_then_the_resistance()
    {
        // Level 1: armour 4 (Aim 20 x 0.2) against a level-1 attacker, 4 / (4 + 50 + 10);
        // resist blast 3 % (Grit 15 x 0.2 to every resistance).
        var rig = new HeroStatsRig();

        float taken = rig.Hit(rig.Player, HeroStatsRig.Robot, 100f, DamageType.Blast);

        Assert.Equal(100f * (1f - 4f / 64f) * 0.97f, taken, 3);
        Assert.Equal(90.9375f, taken, 3);
    }

    [Fact]
    public void A_level_10_heros_armour_and_grit_take_more()
    {
        // Armour 9 (Aim 45), 9 / (9 + 60); resist 7 % (Grit 35).
        var rig = new HeroStatsRig(Sheet.Level10);

        Assert.Equal(100f * (1f - 9f / 69f) * 0.93f, rig.Hit(rig.Player, HeroStatsRig.Robot, 100f, DamageType.Bullet), 3);
    }

    [Fact]
    public void Armour_is_worth_less_against_a_deeper_attacker()
    {
        var rig = new HeroStatsRig();
        rig.AttackerLevels.Level = 10;

        Assert.Equal(100f * (1f - 4f / 154f) * 0.97f, rig.Hit(rig.Player, HeroStatsRig.Robot, 100f, DamageType.Blast), 3);
    }

    [Fact]
    public void A_resistance_stops_at_the_rules_cap()
    {
        var rig = new HeroStatsRig();
        rig.Dev.Add(HeroRig.Index, "resist_blast_pct", 200);
        rig.Hero.Recompute();

        Assert.Equal(0.75, rig.Hero.Stats!.Resist("blast"), 6);
        Assert.Equal(100f * (1f - 4f / 64f) * 0.25f, rig.Hit(rig.Player, HeroStatsRig.Robot, 100f, DamageType.Blast), 3);
    }

    [Fact]
    public void A_heros_own_blast_is_resisted_as_blast_and_nothing_more()
    {
        // No outgoing multiplier on himself, no armour against himself: the resistance alone.
        var rig = new HeroStatsRig();

        Assert.Equal(97f, rig.Hit(rig.Player, rig.Player, 100f, DamageType.Blast, Rocket), 3);
    }

    [Fact]
    public void A_hit_of_no_resisted_type_from_no_attacker_lands_as_dealt()
    {
        var rig = new HeroStatsRig();

        Assert.Equal(100f, rig.Hit(rig.Player, EntityHandle.None, 100f, DamageType.Fall), 3);
    }

    [Fact]
    public void A_fire_hit_on_a_hero_is_resisted_as_fire()
    {
        var rig = new HeroStatsRig();
        rig.Dev.Add(HeroRig.Index, "resist_fire_pct", 27);
        rig.Hero.Recompute();

        Assert.Equal(100f * (1f - 4f / 64f) * 0.70f, rig.Hit(rig.Player, HeroStatsRig.Robot, 100f, DamageType.Burn), 3);
    }

    [Fact]
    public void The_hero_layer_runs_after_TF2s_damage_layer_in_the_stats_block()
    {
        Assert.True(HeroDamage.Order > TF2.Server.Combat.TFDamageLayer.Order);
        Assert.True(HeroDamage.Order >= ModStages.First);
    }
}
