using SourceSharp.Shared;

using TF2.Descent.Server.Hero;
using TF2.Shared;

using Xunit;

using Sheet = TF2.Descent.Tests.Hero.StatsSheet;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// HE3, crit chance: the sheet's <c>CritChance</c> (Aim buys 0.1 % a point)
/// adds to TF2's own random crit for the hero's shots, through the weapon
/// chain's crit policy, and nothing when the sheet's is zero.
/// </summary>
public sealed class HeroCritsTests
{
    private static TFWeaponHookContext Weapon(int owner) => default(TFWeaponHookContext) with { OwnerIndex = owner };

    [Theory]
    [InlineData(Sheet.Level1, 0.02f)]     // Aim 20
    [InlineData(Sheet.Level10, 0.045f)]   // Aim 45
    public void The_sheets_crit_chance_is_added_to_TF2s(Sheet sheet, float chance)
    {
        var rig = new HeroStatsRig(sheet);

        var policy = rig.Crits.CritPolicy(Weapon(HeroRig.Index), new TFCritOwner { ChanceScale = 1.5f });

        Assert.Equal(chance, policy.AddedChance, 5);
        Assert.Equal(1.5f, policy.ChanceScale);
    }

    [Fact]
    public void A_sheet_with_no_crit_chance_adds_nothing()
    {
        var rig = new HeroStatsRig();
        rig.Dev.Add(HeroRig.Index, "crit_chance_pct", -2);
        rig.Hero.Recompute();
        Assert.Equal(0.0, rig.Hero.Stats!.CritChance, 9);

        Assert.Equal(0f, rig.Crits.CritPolicy(Weapon(HeroRig.Index), new TFCritOwner()).AddedChance);
    }

    [Fact]
    public void A_weapon_nobody_holds_or_no_hero_holds_adds_nothing()
    {
        var rig = new HeroStatsRig();

        Assert.Equal(0f, rig.Crits.CritPolicy(Weapon(0), new TFCritOwner()).AddedChance);
        Assert.Equal(0f, rig.Crits.CritPolicy(Weapon(9), new TFCritOwner()).AddedChance);
    }

    [Fact]
    public void Under_one_seed_a_draw_past_TF2s_two_percent_crits_for_the_hero_alone()
    {
        // A gun's draw in [200, 400) of 10,000: past stock's 2 %, inside 2 % + the level-1 sheet's 2 %.
        var rig = new HeroStatsRig();
        var roll = new TFCritRoll();
        int seed = Enumerable.Range(1, 200_000).First(s => roll.Draw(TFCritRoll.GunSeed(new WeaponCommand(s, 5, HeroRig.Index))) is >= 200 and < 400);
        var shot = new TFCritShot(Melee: false, RapidFire: false, Damage: 90f, BulletsPerShot: 1, FireDelay: 0.8f,
                                  new WeaponCommand(seed, 5, HeroRig.Index), 10f, FirstPrediction: true);
        var settings = new TFCritSettings();

        var plainClocks = new TFCritClocks();
        var heroClocks = new TFCritClocks();
        bool plain = roll.Roll(in shot, new TFCritOwner(), in settings, new CritBucket(1000f), ref plainClocks).Critical;
        var heroPolicy = rig.Crits.CritPolicy(Weapon(HeroRig.Index), new TFCritOwner());
        bool hero = roll.Roll(in shot, in heroPolicy, in settings, new CritBucket(1000f), ref heroClocks).Critical;

        Assert.False(plain);
        Assert.True(hero);
    }
}
