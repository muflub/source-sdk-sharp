using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The SMG1's grenade launcher (weapon_smg1.cpp, hl2mp, SecondaryAttack):
/// grenade_ar2 along the eyes at 1000, one SMG1_Grenade spent, the trigger
/// held off half a second and the next grenade a second; a dry click and
/// half a second with none left; and a grenade cuts a reload short.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25): this
/// mod lobbed the frag grenade from the SMG1 instead.
/// </para>
/// </summary>
public class Hl2mpSmg1Tests
{
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int ReloadKey = (int)InputButtons.Reload;

    private sealed class Arms : IWeaponArms
    {
        public readonly List<string> Sounds = [];
        public readonly List<WeaponAnim> Anims = [];
        public readonly List<(string Projectile, float Speed, bool Thrown)> Launches = [];

        public void Sound(string soundScript) { if (soundScript.Length > 0) Sounds.Add(soundScript); }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) => Launches.Add((projectile, speed, thrown));

        public int LookupSequence(WeaponAnim anim)
        {
            Anims.Add(anim);
            return (int)anim + 1;
        }

        public float AnimDuration(WeaponAnim anim) => 0.5f;
    }

    private static (WeaponLogic Machine, Arms Arms, WeaponNetState W, ViewModelNetState Vm) Smg1(int grenades, int clip = 45)
    {
        var arms = new Arms();
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf("weapon_smg1");
        w.Clip = clip;
        w.Reserve = 90;
        w.SecondaryReserve = grenades;
        return (new WeaponLogic(arms), arms, w, vm);
    }

    [Fact]
    public void AGrenadeIsGrenadeAr2AimedAtAThousandAndSpendsOne()
    {
        var (machine, arms, w, vm) = Smg1(grenades: 3);

        machine.RunCommand(w, vm, Attack2, 10f, effectsLive: true);

        Assert.Equal(new[] { ("grenade_ar2", 1000f, false) }, arms.Launches);
        Assert.Equal(2, w.SecondaryReserve);
        Assert.Equal(45, w.Clip);
        Assert.Equal(new[] { "Weapon_SMG1.Double" }, arms.Sounds);
        Assert.Contains(WeaponAnim.Secondary, arms.Anims);
    }

    [Fact]
    public void TheTriggerWaitsHalfASecondAndTheNextGrenadeASecond()
    {
        var (machine, _, w, vm) = Smg1(grenades: 3);

        machine.RunCommand(w, vm, Attack2, 10f, effectsLive: true);

        Assert.Equal(10.5f, w.NextPrimaryAttack);
        Assert.Equal(11f, w.NextSecondaryAttack);
    }

    [Fact]
    public void WithNoGrenadeItClicksAndWaitsHalfASecond()
    {
        var (machine, arms, w, vm) = Smg1(grenades: 0);

        machine.RunCommand(w, vm, Attack2, 10f, effectsLive: true);

        Assert.Empty(arms.Launches);
        Assert.Equal(new[] { "Weapon_SMG1.Empty" }, arms.Sounds);
        Assert.Contains(WeaponAnim.DryFire, arms.Anims);
        Assert.Equal(10.5f, w.NextSecondaryAttack);
    }

    [Fact]
    public void AGrenadeCutsAReloadShort()
    {
        var (machine, arms, w, vm) = Smg1(grenades: 3, clip: 10);

        machine.RunCommand(w, vm, ReloadKey, 10f, effectsLive: true);
        Assert.True(w.InReload, "the reload started");

        machine.RunCommand(w, vm, Attack2, 10.1f, effectsLive: true);

        Assert.False(w.InReload, "the grenade did not cut the reload short");
        Assert.Single(arms.Launches);
        Assert.Equal(10, w.Clip);
    }

    [Fact]
    public void AReplayLaunchesNothingAndLandsOnTheSameState()
    {
        var (live, liveArms, w1, vm1) = Smg1(grenades: 3);
        var (replay, replayArms, w2, vm2) = Smg1(grenades: 3);

        live.RunCommand(w1, vm1, Attack2, 10f, effectsLive: true);
        replay.RunCommand(w2, vm2, Attack2, 10f, effectsLive: false);

        Assert.Single(liveArms.Launches);
        Assert.Empty(replayArms.Launches);
        Assert.Equal((w1.SecondaryReserve, w1.NextSecondaryAttack, vm1.Sequence), (w2.SecondaryReserve, w2.NextSecondaryAttack, vm2.Sequence));
    }
}
