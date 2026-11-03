using SourceSharp.Shared;

using TF2.Server.Weapons;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The server's effects dispatcher: each shared-behaviour effect call reaches
/// the one seam that owns it, with its arguments, and a seam nobody owns does
/// nothing.
/// </summary>
public sealed class TFWeaponEffectsTests
{
    private sealed class Seams : ITFProjectileEffects, ITFFlameEffects, ITFHealEffects, ITFZoomEffects,
                                 ITFItemSwitchEffects
    {
        public readonly List<string> Calls = new();

        public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge)
            => Calls.Add($"projectile {script.Name} {shot.Damage} {shot.Critical} {charge}");

        public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) => Calls.Add($"detonate {script.Name}");

        public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot)
            => Calls.Add($"flames {script.Name} {shot.Damage} {shot.Critical}");

        public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) => Calls.Add($"airblast {script.Name}");

        public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) => Calls.Add($"heal {script.Name} {held}");

        public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) => Calls.Add($"zoom {script.Name} {zoomed}");

        public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) => Calls.Add($"selectlast {script.Name}");
    }

    private static readonly WeaponAttackContext Attack = default;

    [Fact]
    public void A_projectile_and_a_detonation_reach_the_projectile_seam()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(projectiles: seams);
        var launcher = WeaponFixtures.Parse("tf_weapon_pipebomblauncher");

        effects.Projectile(in Attack, launcher, new ShotDamage(120f, Critical: true), 2.5f);
        effects.Detonate(in Attack, launcher);

        Assert.Equal(["projectile tf_weapon_pipebomblauncher 120 True 2.5", "detonate tf_weapon_pipebomblauncher"], seams.Calls);
    }

    [Fact]
    public void Flames_heal_and_zoom_each_reach_their_own_seam()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(flames: seams, heal: seams, zoom: seams);

        effects.Flames(in Attack, WeaponFixtures.Parse("tf_weapon_flamethrower"), new ShotDamage(6.8f));
        effects.Heal(in Attack, WeaponFixtures.Parse("tf_weapon_medigun"), held: true);
        effects.Zoom(in Attack, WeaponFixtures.Parse("tf_weapon_sniperrifle"), zoomed: false);

        Assert.Equal(["flames tf_weapon_flamethrower 6.8 False", "heal tf_weapon_medigun True", "zoom tf_weapon_sniperrifle False"],
                     seams.Calls);
    }

    [Fact]
    public void The_PDAs_item_switch_reaches_the_item_switch_seam()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(itemSwitch: seams);

        effects.SelectLast(in Attack, WeaponFixtures.Parse("tf_weapon_pda_spy"));

        Assert.Equal(["selectlast tf_weapon_pda_spy"], seams.Calls);
    }

    [Fact]
    public void A_seam_nobody_owns_does_nothing()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(heal: seams);   // only the medigun's seam is owned
        var rifle = WeaponFixtures.Parse("tf_weapon_sniperrifle");

        effects.Projectile(in Attack, rifle, new ShotDamage(1f), 0f);
        effects.Detonate(in Attack, rifle);
        effects.Flames(in Attack, rifle, new ShotDamage(1f));
        effects.Zoom(in Attack, rifle, zoomed: true);
        effects.SelectLast(in Attack, rifle);

        Assert.Empty(seams.Calls);
    }
}
