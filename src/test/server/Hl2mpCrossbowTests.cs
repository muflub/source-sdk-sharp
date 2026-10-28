using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The HL2MP crossbow's own machine (weapon_crossbow.cpp, hl2mp): a bolt at
/// 3500 with Single and BoltFly, both triggers held off 0.75 s, the reload
/// owed once the shot has played, its first model event electrifying the bolt
/// (BoltElectrify, the glowing skin), the scope on the second trigger, and the
/// fidget when nothing is loaded.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25): this
/// mod's bolt left at 2500 every 1.5 s, and its server played neither BoltFly
/// nor BoltElectrify.
/// </para>
/// </summary>
public class Hl2mpCrossbowTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;

    private sealed class Arms : IWeaponArms
    {
        public readonly List<string> Sounds = [];
        public readonly List<WeaponAnim> Anims = [];
        public readonly List<(string Projectile, float Speed, bool Thrown)> Launches = [];
        public readonly List<(int Fov, float Rate)> Fovs = [];

        public void Sound(string soundScript) { if (soundScript.Length > 0) Sounds.Add(soundScript); }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) => Launches.Add((projectile, speed, thrown));
        public void SetFov(int fov, float rate) => Fovs.Add((fov, rate));
        public bool Submerged;
        public bool EyesUnderwater() => Submerged;

        public int LookupSequence(WeaponAnim anim)
        {
            Anims.Add(anim);
            return (int)anim + 1;
        }

        // Every sequence a second long: the events fall at their cycles.
        public float AnimDuration(WeaponAnim anim) => 1f;
    }

    private static (WeaponLogic Machine, Arms Arms, WeaponNetState W, ViewModelNetState Vm) Crossbow(int clip, int reserve)
    {
        var arms = new Arms();
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf("weapon_crossbow");
        w.Clip = clip;
        w.Reserve = reserve;
        return (new WeaponLogic(arms), arms, w, vm);
    }

    private static void Run(WeaponLogic machine, WeaponNetState w, ViewModelNetState vm, int buttons, float from, float to)
    {
        for (float t = from; t < to; t += 0.015f)
            machine.RunCommand(w, vm, buttons, t, effectsLive: true);
    }

    [Fact]
    public void AShotIsABoltAtThirtyFiveHundredWithSingleAndBoltFly()
    {
        var (machine, arms, w, vm) = Crossbow(clip: 1, reserve: 5);

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);

        Assert.Equal(new[] { ("ss_bolt", 3500f, false) }, arms.Launches);
        Assert.Equal(new[] { "Weapon_Crossbow.Single", "Weapon_Crossbow.BoltFly" }, arms.Sounds);
        Assert.Equal(0, w.Clip);
        Assert.Equal((10.75f, 10.75f), (w.NextPrimaryAttack, w.NextSecondaryAttack));
    }

    /// <summary>
    /// FireBolt owes a reload (m_bMustReload) once the idle time has passed,
    /// which stock sets from SequenceDuration( ACT_VM_PRIMARYATTACK ): an
    /// activity where a sequence goes, out of range, so 0.1. The reload's
    /// first event (0.509 of it) electrifies the bolt.
    ///
    /// <para>
    /// Found side by side with stock (tools/stock-ab --weapons
    /// weapon_crossbow, 2026-10-25): this mod waited for the shot to play out
    /// and loaded the next bolt 0.7 s after stock did.
    /// </para>
    /// </summary>
    [Fact]
    public void TheShotIsCutByItsReloadATenthOfASecondLaterWhichElectrifiesTheBolt()
    {
        var (machine, arms, w, vm) = Crossbow(clip: 1, reserve: 5);

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);
        Run(machine, w, vm, 0, 10.015f, 10.09f);
        Assert.False(w.InReload, "reloaded before the idle time had passed");

        Run(machine, w, vm, 0, 10.09f, 10.13f);
        Assert.True(w.InReload);
        Assert.DoesNotContain("Weapon_Crossbow.BoltElectrify", arms.Sounds);

        Run(machine, w, vm, 0, 10.13f, 10.7f);      // 0.509 of the reload: START_LOAD
        Assert.Contains("Weapon_Crossbow.BoltElectrify", arms.Sounds);
        Assert.Equal(1, vm.Skin);

        Run(machine, w, vm, 0, 10.7f, 11.3f);
        Assert.Equal((1, 4, false), (w.Clip, w.Reserve, w.InReload));
    }

    /// <summary>m_bMustReload: a trigger held through the shot does not hold the reload off.</summary>
    [Fact]
    public void AHeldScopeDoesNotHoldTheReloadOff()
    {
        var (machine, _, w, vm) = Crossbow(clip: 1, reserve: 5);

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);
        Run(machine, w, vm, Attack2, 10.015f, 10.09f);
        Assert.False(w.InReload);

        Run(machine, w, vm, Attack2, 10.09f, 10.13f);
        Assert.True(w.InReload);
    }

    /// <summary>FireBolt: BOLT_WATER_VELOCITY, 1500, with the shooter's eyes under water.</summary>
    [Fact]
    public void UnderWaterTheBoltLeavesAtFifteenHundred()
    {
        var (machine, arms, w, vm) = Crossbow(clip: 1, reserve: 5);
        arms.Submerged = true;

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);

        Assert.Equal(new[] { ("ss_bolt", 1500f, false) }, arms.Launches);
    }

    [Fact]
    public void TheSecondTriggerTogglesTheScope()
    {
        var (machine, arms, w, vm) = Crossbow(clip: 1, reserve: 5);

        machine.RunCommand(w, vm, Attack2, 10f, effectsLive: true);
        machine.RunCommand(w, vm, Attack2, 10.015f, effectsLive: true);   // held, not pressed again
        machine.RunCommand(w, vm, 0, 10.03f, effectsLive: true);
        machine.RunCommand(w, vm, Attack2, 10.045f, effectsLive: true);

        Assert.Equal(new[] { (20, 0.1f), (0, 0.2f) }, arms.Fovs);
        Assert.False(w.InZoom);
    }

    [Fact]
    public void AnEmptyBowWithNothingToLoadFidgets()
    {
        var (machine, arms, w, vm) = Crossbow(clip: 0, reserve: 0);

        machine.RunCommand(w, vm, 0, 10f, effectsLive: true);

        Assert.Contains(WeaponAnim.Fidget, arms.Anims);
        Assert.Empty(arms.Sounds);
    }

    [Fact]
    public void ADryTriggerIsSilent()
    {
        var (machine, arms, w, vm) = Crossbow(clip: 0, reserve: 0);

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);

        Assert.Empty(arms.Launches);
        Assert.Empty(arms.Sounds);
    }

    [Fact]
    public void AReplayLaunchesNothingAndLandsOnTheSameState()
    {
        var (live, liveArms, w1, vm1) = Crossbow(clip: 1, reserve: 5);
        var (replay, replayArms, w2, vm2) = Crossbow(clip: 1, reserve: 5);

        for (float t = 10f; t < 12.5f; t += 0.015f)
        {
            int buttons = t < 10.1f ? Attack : 0;
            live.RunCommand(w1, vm1, buttons, t, effectsLive: true);
            replay.RunCommand(w2, vm2, buttons, t, effectsLive: false);
        }

        Assert.Single(liveArms.Launches);
        Assert.Empty(replayArms.Launches);
        Assert.Equal((w1.Clip, w1.Reserve, w1.InReload, w1.MustReload, vm1.Skin, vm1.Sequence),
                     (w2.Clip, w2.Reserve, w2.InReload, w2.MustReload, vm2.Skin, vm2.Sequence));
    }
}
