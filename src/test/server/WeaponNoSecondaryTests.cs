using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A row with no ammunition secondary still reaches its behaviour's
/// <see cref="IWeaponBehaviour.SecondaryAttack"/> on a held alt-fire: stock's
/// ItemPostFrame calls SecondaryAttack() for every weapon the same way, and
/// the TF arms whose secondary is the class's special skill live there
/// (CTFWeaponBaseGun::SecondaryAttack, tf_weaponbase_gun.cpp:234-251).
/// </summary>
public class WeaponNoSecondaryTests
{
    private const int Attack2 = (int)InputButtons.Attack2;

    private sealed class RecordingBehaviour : IWeaponBehaviour
    {
        public int Secondaries;
        public int PostFrames;

        public void SecondaryAttack(in WeaponAttackContext attack) => Secondaries++;
        public void ItemPostFrame(in WeaponAttackContext attack) => PostFrames++;
    }

    private sealed class Source(WeaponType row, IWeaponBehaviour behaviour) : IWeaponTypeSource, IWeaponBehaviourSource
    {
        public IEnumerable<WeaponType> Rows() => [row];
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours() =>
            [KeyValuePair.Create(row.Behaviour, behaviour)];
    }

    private static (WeaponLogic Machine, WeaponNetState W, ViewModelNetState Vm, RecordingBehaviour Behaviour) Rig()
    {
        var row = new WeaponType("weapon_test_gun", Interval: 1f) { Behaviour = "test.gun" };
        var behaviour = new RecordingBehaviour();
        var machine = new WeaponLogic(new RecordingArms(),
            new WeaponCatalog(() => [new Source(row, behaviour)]),
            new WeaponBehaviours(() => [new Source(row, behaviour)]));
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = new WeaponCatalog(() => [new Source(row, behaviour)]).IndexOf("weapon_test_gun");
        return (machine, w, vm, behaviour);
    }

    private sealed class RecordingArms : IWeaponArms
    {
        public void Sound(string soundScript) { }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot) { }
        public void MeleeSwing(in WeaponType type) { }
        public void MeleeSwing(in WeaponType type, ShotDamage shot) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => -1;
        public float AnimDuration(WeaponAnim anim) => 0f;
        public void SetFov(int fov, float time) { }
    }

    [Fact]
    public void ARowWithoutASecondaryReachesItsBehaviourOnAltFire()
    {
        var (machine, w, vm, behaviour) = Rig();

        machine.RunCommand(w, vm, Attack2, now: 5f, effectsLive: true);

        Assert.Equal(1, behaviour.Secondaries);
    }

    [Fact]
    public void TheAltFireTailStillRunsAfterTheSecondaryDispatch()
    {
        var (machine, w, vm, behaviour) = Rig();

        machine.RunCommand(w, vm, Attack2, now: 5f, effectsLive: true);

        Assert.Equal(1, behaviour.PostFrames);
    }
}
