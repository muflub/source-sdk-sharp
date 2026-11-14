using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// damage_weapon_pct on the server's hit (<see cref="TF2.Descent.Server.Hero.HeroDamage"/>):
/// every weapon family's hit takes it, the flamethrower's too, and a skill's hit never does.
/// A dev contribution stands in for the weapon upgrades (Marksmanship, Trench Fighter).
/// </summary>
public sealed class HeroWeaponHitTests
{
    private static readonly EntityHandle Target = new(0x0003_0200u);

    /// <summary>What a 100-point hit with <paramref name="weapon"/> in hand deals, without and with +10 % weapon damage.</summary>
    private static (float Without, float With) Hits(string weapon, string? skill = null)
    {
        var rig = new HeroStatsRig();
        rig.Held.ItemClass[rig.Player] = weapon;
        rig.SkillHit.Skill = skill;
        float without = rig.Damage.Modify(new DamageContext(Target, rig.Player, 100f, (int)DamageType.Burn), 100f);
        rig.Dev.Add(HeroRig.Index, "damage_weapon_pct", 10);
        rig.Hero.Recompute();
        float with = rig.Damage.Modify(new DamageContext(Target, rig.Player, 100f, (int)DamageType.Burn), 100f);
        return (without, with);
    }

    [Fact]
    public void A_flamethrowers_hit_takes_the_weapon_damage_stat()
    {
        var (without, with) = Hits("tf_weapon_flamethrower");

        Assert.Equal(10f, with - without, 3);
    }

    [Fact]
    public void A_shovels_hit_takes_the_weapon_damage_stat()
    {
        var (without, with) = Hits("tf_weapon_shovel");

        Assert.Equal(10f, with - without, 3);
    }

    [Fact]
    public void A_skills_hit_does_not_take_the_weapon_damage_stat()
    {
        var (without, with) = Hits("tf_weapon_rocketlauncher", skill: "patriot_missile");

        Assert.True(without > 0f);
        Assert.Equal(without, with, 3);
    }
}
