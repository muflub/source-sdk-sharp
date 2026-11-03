using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The arms stock names call <c>DoClassSpecialSkill</c> from their
/// <c>SecondaryAttack</c> — the base gun (<c>tf_weaponbase_gun.cpp:245</c>),
/// the melee (<c>tf_weaponbase_melee.cpp:244</c>), the knife
/// (<c>tf_weapon_knife.cpp:304</c>), the PDA (<c>tf_weapon_pda.cpp:114</c>)
/// and the grenade launcher (<c>tf_weapon_grenadelauncher.cpp:499</c>) — so
/// the class's special runs whichever weapon is in the hands, under the
/// <c>m_bInAttack2</c> debounce and the 0.5 s refire the base arms.
/// </summary>
public class TFClassSpecialSkillTests
{
    private const int Attack2 = (int)InputButtons.Attack2;

    private static TFWeaponWorld World(TFClass playerClass)
    {
        var world = new TFWeaponWorld();
        world.Owners.Classes[TFOwner] = playerClass;
        return world;
    }

    private const int TFOwner = TFWeaponWorld.Owner;

    [Fact]
    public void ARevolverAltFireReachesTheClassSkill()
    {
        var world = World(TFClass.Spy);
        var (w, vm) = world.Armed("tf_weapon_revolver", clip: 6, reserve: 24);

        world.Run(w, vm, Attack2, now: 10f);

        Assert.Equal([TFOwner], world.Effects.ClassSkills);
        Assert.Equal(10.5f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void AKnifeAltFireReachesTheClassSkill()
    {
        var world = World(TFClass.Spy);
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        world.Run(w, vm, Attack2, now: 10f);

        Assert.Equal([TFOwner], world.Effects.ClassSkills);
    }

    /// <summary>
    /// The sapper is the builder family's shell, not a skill arm: its
    /// alt-fire is the builder's rotate (<c>CTFWeaponBuilder::SecondaryAttack</c>,
    /// the m_bInAttack2 debounce), and the class's special never runs from it.
    /// </summary>
    [Fact]
    public void ASapperAltFireIsTheBuildersRotateNotTheClassSkill()
    {
        var world = World(TFClass.Spy);
        var (w, vm) = world.Armed("tf_weapon_sapper", clip: -1, reserve: 0);

        world.Run(w, vm, Attack2, now: 10f);

        Assert.Empty(world.Effects.ClassSkills);
        Assert.Contains("rotate 3", world.Builder.Asked);
        Assert.Equal(10.2f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void ADemomansGrenadeLauncherAltFireReachesTheClassSkill()
    {
        var world = World(TFClass.Demoman);
        var (w, vm) = world.Armed("tf_weapon_grenadelauncher", clip: 4, reserve: 20);

        world.Run(w, vm, Attack2, now: 10f);

        Assert.Equal([TFOwner], world.Effects.ClassSkills);
    }

    [Fact]
    public void TheStickybombLauncherDetonatesInsteadOfTheClassSkill()
    {
        var world = World(TFClass.Demoman);
        var (w, vm) = world.Armed("tf_weapon_pipebomblauncher", clip: 4, reserve: 20);

        world.Run(w, vm, Attack2, now: 10f);

        Assert.Empty(world.Effects.ClassSkills);
        Assert.Equal(1, world.Effects.Detonations);
    }

    [Fact]
    public void AMinigunAltFireReachesNoClassSkill()
    {
        var world = World(TFClass.HeavyWeapons);
        var (w, vm) = world.Armed("tf_weapon_minigun", clip: 200, reserve: 0);

        world.Run(w, vm, Attack2, now: 10f);

        Assert.Empty(world.Effects.ClassSkills);
    }

    [Fact]
    public void AHeldAltFireRunsTheSkillOnceUntilTheRefirePasses()
    {
        var world = World(TFClass.Spy);
        var (w, vm) = world.Armed("tf_weapon_revolver", clip: 6, reserve: 24);

        world.Run(w, vm, Attack2, now: 10f);
        world.Run(w, vm, Attack2, now: 10.2f);

        Assert.Single(world.Effects.ClassSkills);
    }

    [Fact]
    public void TheTriggerLiftedTheNextPressRunsAgain()
    {
        var world = World(TFClass.Spy);
        var (w, vm) = world.Armed("tf_weapon_revolver", clip: 6, reserve: 24);

        world.Run(w, vm, Attack2, now: 10f);
        world.Run(w, vm, 0, now: 10.3f);
        world.Run(w, vm, Attack2, now: 10.6f);

        Assert.Equal(2, world.Effects.ClassSkills.Count);
    }

    [Fact]
    public void ABusyWeaponStillReachesTheSkill()
    {
        // The busy frame's arm (tf_weaponbase.cpp:2388): a reload does not
        // silence the class special — the demoman detonates in the middle of
        // one (CTFPipebombLauncher::ItemBusyFrame).
        var world = World(TFClass.Demoman);
        var (w, vm) = world.Armed("tf_weapon_grenadelauncher", clip: 0, reserve: 4);

        world.Run(w, vm, (int)InputButtons.Reload, now: 10f); // reload starts
        world.Run(w, vm, Attack2 | (int)InputButtons.Reload, now: 10.1f);

        Assert.Single(world.Effects.ClassSkills);
    }
}
