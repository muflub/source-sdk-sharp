using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The RPG (shared/hl2mp/weapon_rpg.cpp, CWeaponRPG) on the shared machine:
/// one missile at a time, the half-second refire, the reload its missile's
/// death starts and the trigger held off until that reload has played, the
/// laser that comes on after the draw, goes off with a holster, is suppressed
/// while lowered, and the lowered idle of an RPG with nothing to fire.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons weapon_rpg,
/// 2026-10-25): this mod fired an unguided rocket every two seconds, painted
/// no laser, and played neither LaserOn nor the reload.
/// </para>
/// </summary>
public class Hl2mpRpgTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;

    private sealed class Arms : IWeaponArms
    {
        public readonly List<string> Sounds = [];
        public readonly List<string> ServerSounds = [];
        public readonly List<string> Missiles = [];
        public readonly List<(bool Exists, bool On)> Dots = [];
        public readonly List<string> Activities = [];

        public void Sound(string soundScript) { if (soundScript.Length > 0) Sounds.Add(soundScript); }
        public void ServerSound(string soundScript) => ServerSounds.Add(soundScript);
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public void LaunchMissile(in WeaponType type) => Missiles.Add(type.Projectile);
        public void UpdateLaserDot(bool exists, bool on) => Dots.Add((exists, on));

        // Every activity the RPG sends is on v_rpg; each its own sequence, a second long.
        public int LookupActivity(string activity)
        {
            Activities.Add(activity);
            return activity.Length;
        }

        public float SequenceDuration(int sequence) => 1f;
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 1f;
    }

    private sealed record Rig(WeaponLogic Machine, Arms Arms, WeaponNetState W, ViewModelNetState Vm)
    {
        /// <summary>The hooks' context at <paramref name="now"/>, as the arsenal asks them.</summary>
        public WeaponAttackContext At(float now) => new(RpgRow, W, Vm, now, true, Arms);
    }

    private static WeaponType RpgRow => WeaponCatalog.Stock().For(EntityNames.WeaponRpg)!.Value;

    /// <summary>The behaviour itself, whose Deploy, CanHolster and Holster the arsenal runs.</summary>
    private static IWeaponBehaviour Rpg => WeaponBehaviours.Stock().For(Hl2mpWeaponBehaviours.Rpg)!;

    /// <summary>An RPG deployed at t=10 with <paramref name="rounds"/> left, its draw played out by t=11.</summary>
    private static Rig Deployed(int rounds = 3)
    {
        var arms = new Arms();
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponRpg);
        w.Clip = -1;
        w.Reserve = rounds;
        var machine = new WeaponLogic(arms);
        Rpg.Deploy(new WeaponAttackContext(RpgRow, w, vm, 10f, true, arms));
        w.TimeWeaponIdle = 11f;
        w.NextPrimaryAttack = 11f;
        return new Rig(machine, arms, w, vm);
    }

    private static bool Has(Rig rig, int flag) => RpgState.Has(rig.W.Special, flag);

    [Fact]
    public void NothingRunsWhileTheDrawPlays()
    {
        var rig = Deployed();

        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 10.5f, effectsLive: true);

        Assert.Empty(rig.Arms.Missiles);
        Assert.Empty(rig.Arms.Dots);
    }

    [Fact]
    public void TheLaserComesOnOnceTheDrawIsOver()
    {
        var rig = Deployed();

        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 11.1f, effectsLive: true);

        Assert.True(Has(rig, RpgState.Guiding));
        Assert.Equal(new[] { "Weapon_RPG.LaserOn" }, rig.Arms.ServerSounds);
        Assert.Equal((true, true), rig.Arms.Dots[^1]);
    }

    [Fact]
    public void TheLaserIsNotToggledByTheAltFire()
    {
        var rig = Deployed();
        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 11.1f, effectsLive: true);

        rig.Machine.RunCommand(rig.W, rig.Vm, Attack2, 11.2f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 11.3f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack2, 11.4f, effectsLive: true);

        Assert.True(Has(rig, RpgState.Guiding));
        Assert.Equal(new[] { "Weapon_RPG.LaserOn" }, rig.Arms.ServerSounds);
    }

    [Fact]
    public void TheTriggerLaunchesAMissileAndSpendsARound()
    {
        var rig = Deployed();

        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.1f, effectsLive: true);

        Assert.Equal(new[] { EntityNames.SsRocket }, rig.Arms.Missiles);
        Assert.Equal(2, rig.W.Reserve);
        Assert.Equal(new[] { "Weapon_RPG.Single" }, rig.Arms.Sounds);
        Assert.Equal(RpgActivity.Primary, RpgState.Activity(rig.W.Special));
        Assert.Equal(11.6f, rig.W.NextPrimaryAttack, 3);
    }

    [Fact]
    public void ReplayingTheShotLaunchesNothing()
    {
        var rig = Deployed();

        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.1f, effectsLive: false);

        Assert.Empty(rig.Arms.Missiles);
        Assert.Empty(rig.Arms.Sounds);
        Assert.True(Has(rig, RpgState.MissileOut), "the state is predicted all the same");
        Assert.Equal(2, rig.W.Reserve);
    }

    [Fact]
    public void ThereIsOneMissileAtATime()
    {
        var rig = Deployed();
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.1f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 11.2f, effectsLive: true);

        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 12.0f, effectsLive: true);

        Assert.Single(rig.Arms.Missiles);
        Assert.Equal(2, rig.W.Reserve);
    }

    [Fact]
    public void ItsMissilesDeathStartsTheReload()
    {
        var rig = Deployed();
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.1f, effectsLive: true);

        Hl2mpWeaponBehaviours.RocketDied(new WeaponAttackContext(WeaponCatalog.Stock().For(EntityNames.WeaponRpg)!.Value,
                                                                 rig.W, rig.Vm, 12f, true, rig.Arms));

        Assert.False(Has(rig, RpgState.MissileOut));
        Assert.Equal(RpgActivity.Reload, RpgState.Activity(rig.W.Special));
        Assert.Equal("ACT_VM_RELOAD", rig.Arms.Activities[^1]);
    }

    [Fact]
    public void TheTriggerWaitsUntilTheReloadHasPlayedAndIsReleased()
    {
        var rig = Deployed();
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.1f, effectsLive: true);
        Hl2mpWeaponBehaviours.RocketDied(new WeaponAttackContext(WeaponCatalog.Stock().For(EntityNames.WeaponRpg)!.Value,
                                                                 rig.W, rig.Vm, 12f, true, rig.Arms));

        // Held through the reload and past it: refused, because only the idle
        // (sent with nothing held) ends ACT_VM_RELOAD.
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 12.5f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 13.5f, effectsLive: true);
        Assert.Single(rig.Arms.Missiles);

        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 13.6f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 13.7f, effectsLive: true);

        Assert.Equal(2, rig.Arms.Missiles.Count);
    }

    [Fact]
    public void ItCannotBePutAwayWhileItsMissileFlies()
    {
        var rig = Deployed();
        Assert.True(Rpg.CanHolster(rig.At(11f)));

        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.1f, effectsLive: true);

        Assert.False(Rpg.CanHolster(rig.At(11.2f)));
    }

    [Fact]
    public void PuttingItAwayTurnsTheLaserOff()
    {
        var rig = Deployed();
        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 11.1f, effectsLive: true);

        Rpg.Holster(rig.At(11.2f));

        Assert.False(Has(rig, RpgState.Guiding));
        Assert.Equal(new[] { "Weapon_RPG.LaserOn", "Weapon_RPG.LaserOff" }, rig.Arms.ServerSounds);
        Assert.Equal((false, false), rig.Arms.Dots[^1]);
    }

    [Fact]
    public void WithNothingLeftItLowersAndTheLaserGoes()
    {
        var rig = Deployed(rounds: 1);
        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 11.1f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.2f, effectsLive: true);
        Assert.True(Has(rig, RpgState.DotOut), "the dot stays while the last missile flies");

        Hl2mpWeaponBehaviours.RocketDied(new WeaponAttackContext(WeaponCatalog.Stock().For(EntityNames.WeaponRpg)!.Value,
                                                                 rig.W, rig.Vm, 12f, true, rig.Arms));
        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 12.1f, effectsLive: true);

        Assert.Equal(RpgActivity.IdleLowered, RpgState.Activity(rig.W.Special));
        Assert.False(Has(rig, RpgState.Guiding));
        Assert.Equal((false, false), rig.Arms.Dots[^1]);

        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 12.2f, effectsLive: true);
        Assert.Equal(new[] { "Weapon_RPG.LaserOn", "Weapon_RPG.LaserOff" }, rig.Arms.ServerSounds);
    }

    [Fact]
    public void AnEmptyTriggerClicksAtMostTwiceASecond()
    {
        var rig = Deployed(rounds: 0);

        // HandleFireOnEmpty: a click and m_bFireOnEmpty; the next frame clears
        // it (ReloadOrSwitchWeapons); the one after clicks again only once
        // half a second has passed.
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.1f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.2f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.3f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.4f, effectsLive: true);
        rig.Machine.RunCommand(rig.W, rig.Vm, Attack, 11.7f, effectsLive: true);

        Assert.Equal(new[] { "Weapon_SMG1.Empty", "Weapon_SMG1.Empty" }, rig.Arms.Sounds);
        Assert.Empty(rig.Arms.Missiles);
    }

    [Fact]
    public void TheDotIsOnlyMadeWithARoundToGuide()
    {
        var rig = Deployed(rounds: 0);

        rig.Machine.RunCommand(rig.W, rig.Vm, 0, 11.1f, effectsLive: true);

        Assert.False(Has(rig, RpgState.DotOut));
        Assert.Empty(rig.Arms.ServerSounds);
    }
}
