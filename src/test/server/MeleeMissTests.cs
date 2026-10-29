using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CBaseHL2MPBludgeonWeapon::Swing's animation: a swing that meets something
/// plays ACT_VM_HITCENTER and lands, one that meets nothing plays
/// ACT_VM_MISSCENTER and does not. Asked of the arms on every pass, because
/// the animation is predicted.
///
/// <para>
/// Found side by side with stock (docs/divergences.md, the melee row,
/// 2026-10-25): this mod played the hit for every swing.
/// </para>
/// </summary>
public class MeleeMissTests
{
    private sealed class Arms(bool connects) : IWeaponArms
    {
        public readonly List<WeaponAnim> Anims = [];
        public int Swings;

        public void Sound(string soundScript) { }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public int Misses;

        public void MeleeSwing(in WeaponType type) => Swings++;
        public void MeleeMiss(in WeaponType type) => Misses++;
        public bool MeleeConnects(in WeaponType type) => connects;
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }

        public int LookupSequence(WeaponAnim anim)
        {
            Anims.Add(anim);
            return (int)anim + 1;
        }

        public float AnimDuration(WeaponAnim anim) => 0.5f;
    }

    private static Arms Swing(bool connects, bool effectsLive = true)
    {
        var arms = new Arms(connects);
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf("weapon_crowbar");
        w.Clip = -1;

        new WeaponLogic(arms).RunCommand(w, vm, (int)InputButtons.Attack, 10f, effectsLive);
        return arms;
    }

    [Fact]
    public void ASwingThatConnectsPlaysTheHitAndLands()
    {
        var arms = Swing(connects: true);

        Assert.Contains(WeaponAnim.Primary, arms.Anims);
        Assert.DoesNotContain(WeaponAnim.Miss, arms.Anims);
        Assert.Equal(1, arms.Swings);
    }

    [Fact]
    public void ASwingThatMeetsNothingPlaysTheMissAndLandsNothing()
    {
        var arms = Swing(connects: false);

        Assert.Contains(WeaponAnim.Miss, arms.Anims);
        Assert.DoesNotContain(WeaponAnim.Primary, arms.Anims);
        Assert.Equal((0, 1), (arms.Swings, arms.Misses));
    }

    /// <summary>The replay asks too, and lands on the live pass's animation.</summary>
    [Fact]
    public void TheReplayPlaysTheSameMiss()
    {
        var arms = Swing(connects: false, effectsLive: false);

        Assert.Contains(WeaponAnim.Miss, arms.Anims);
        Assert.Equal((0, 0), (arms.Swings, arms.Misses));
    }
}
