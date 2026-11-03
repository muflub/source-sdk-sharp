using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The AR2's alt-fire, as CWeaponAR2's: half a second's charge from the press, then the ball.</summary>
public class Ar2ChargeTests
{
    private const int Attack = (int)InputButtons.Attack, Attack2 = (int)InputButtons.Attack2, ReloadKey = (int)InputButtons.Reload;

    private sealed class Arms(PlayerNetState carrier) : IWeaponArms
    {
        public readonly List<string> Sounds = [];
        public readonly List<string> Launched = [];
        public int Volleys, Flashes;

        public PlayerNetState? Carrier => carrier;
        public void Sound(string soundScript) => Sounds.Add(soundScript);
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) => Volleys++;
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) => Launched.Add(projectile);
        public void ChargedFlash() => Flashes++;
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 0.1f;
    }

    private static (WeaponLogic Logic, Arms Arms, WeaponNetState W, ViewModelNetState Vm, PlayerNetState Carrier) Rig(int reserve = 2)
    {
        var carrier = new PlayerNetState();
        carrier.BindLocal();
        var arms = new Arms(carrier);
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponAr2);
        w.Clip = 30;
        w.Reserve = 60;
        w.SecondaryReserve = reserve;
        return (new WeaponLogic(arms), arms, w, vm, carrier);
    }

    [Fact]
    public void The_press_charges_and_nothing_is_spent_or_thrown_yet()
    {
        var (logic, arms, w, vm, carrier) = Rig();

        logic.RunCommand(w, vm, Attack2, 10f, true);

        Assert.Equal(10.5f, w.ChargeFiresAt);
        Assert.Equal(2, w.SecondaryReserve);
        Assert.Empty(arms.Launched);
        Assert.Contains("Weapon_CombineGuard.Special1", arms.Sounds);
        Assert.Equal(0f, carrier.PunchAngleVel.AsVector().X);
    }

    [Fact]
    public void It_goes_off_once_after_the_charge_whatever_is_held()
    {
        var (logic, arms, w, vm, carrier) = Rig();
        logic.RunCommand(w, vm, Attack2, 10f, true);

        logic.RunCommand(w, vm, 0, 10.4f, true);
        Assert.Empty(arms.Launched);

        logic.RunCommand(w, vm, 0, 10.51f, true);
        Assert.Equal([EntityNames.PropCombineBall], arms.Launched);
        Assert.Equal(1, arms.Flashes);
        Assert.Equal(1, w.SecondaryReserve);
        Assert.Equal(0f, w.ChargeFiresAt);
        Assert.NotEqual(0f, carrier.PunchAngleVel.AsVector().X);

        logic.RunCommand(w, vm, 0, 10.6f, true);
        Assert.Single(arms.Launched);
    }

    [Fact]
    public void After_it_the_primary_waits_half_a_second_and_the_alt_a_whole_one()
    {
        var (logic, _, w, vm, _) = Rig();
        logic.RunCommand(w, vm, Attack2, 10f, true);
        logic.RunCommand(w, vm, 0, 10.6f, true);

        Assert.Equal(11.1f, w.NextPrimaryAttack, 3);
        Assert.Equal(11.6f, w.NextSecondaryAttack, 3);
    }

    /// <summary>While it charges the gun does nothing else: no bullets, no reload.</summary>
    [Theory]
    [InlineData(Attack)]
    [InlineData(ReloadKey)]
    public void While_it_charges_nothing_else_happens(int buttons)
    {
        var (logic, arms, w, vm, _) = Rig();
        w.Clip = 10;
        logic.RunCommand(w, vm, Attack2, 10f, true);

        // Past the attack timers, which the charge also holds: it is m_bShotDelayed that says no.
        w.NextPrimaryAttack = 0f;
        logic.RunCommand(w, vm, buttons, 10.3f, true);

        Assert.Equal(0, arms.Volleys);
        Assert.False(w.InReload);
    }

    [Fact]
    public void With_nothing_to_fire_it_does_not_charge()
    {
        var (logic, arms, w, vm, _) = Rig(reserve: 0);

        logic.RunCommand(w, vm, Attack2, 10f, true);

        Assert.Equal(0f, w.ChargeFiresAt);
        Assert.Equal(10.5f, w.NextSecondaryAttack);
        Assert.Contains(WeaponCatalog.Stock().For(EntityNames.WeaponAr2)!.Value.EmptySound, arms.Sounds);
    }

    [Fact]
    public void Under_water_it_does_not_charge()
    {
        var (logic, _, w, vm, carrier) = Rig();
        carrier.WaterLevel = 3;

        logic.RunCommand(w, vm, Attack2, 10f, true);

        Assert.Equal(0f, w.ChargeFiresAt);
        Assert.Equal(2, w.SecondaryReserve);
    }

    /// <summary>A replay moves the same state and starts no effects: the ball is thrown once.</summary>
    [Fact]
    public void A_replay_counts_down_and_throws_nothing()
    {
        var (logic, arms, w, vm, _) = Rig();
        logic.RunCommand(w, vm, Attack2, 10f, false);
        Assert.Equal(10.5f, w.ChargeFiresAt);

        logic.RunCommand(w, vm, 0, 10.6f, false);

        Assert.Equal(0f, w.ChargeFiresAt);
        Assert.Equal(1, w.SecondaryReserve);
        Assert.Empty(arms.Launched);
    }
}
