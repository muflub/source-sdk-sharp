using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Wrangler's triggers (<c>CTFLaserPointer</c>, tf_weapon_laser_pointer.cpp):
/// the held vetoed primary is the sentry's <c>FireNextFrame</c> under the
/// spin-up anim, the alt-fire the rockets, every command aims the dot, the
/// holster releases — the sentry work itself the seam's
/// (<see cref="ITFSentryWrangle"/>, recorded here).
/// </summary>
public sealed class TFWranglerBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;

    [Fact]
    public void The_held_trigger_fires_the_sentry()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f);
        world.Run(w, vm, Attack, 10.1f);   // no refire of the pointer's own: asked again at once

        // Each command also aims the dot (ItemPostFrame), after the trigger.
        Assert.Equal(["fire 3", "aim 3", "fire 3", "aim 3"], world.Wrangle.Asked);
    }

    [Fact]
    public void The_trigger_fires_no_bullet_of_the_pointers_own()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f);

        // The stock override's whole body is sentry commands (PrimaryAttack,
        // tf_weapon_laser_pointer.cpp:192-214): the core's shot is vetoed.
        Assert.Empty(world.Arms.Bullets);
        Assert.Empty(world.Arms.Sounds);
    }

    [Fact]
    public void The_held_trigger_spins_the_reload_anim()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);
        world.Arms.Sequences[WeaponAnim.Reload] = 12;   // the wrangler's spin-up, ACT_ITEM1_VM_RELOAD

        world.Run(w, vm, Attack, 10f);

        // ACT_ITEM1_VM_RELOAD while held (:212), the machine's primary anim
        // vetoed with the shot.
        Assert.Equal(12, vm.Sequence);
    }

    [Fact]
    public void The_alt_fire_asks_the_rockets()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);

        world.Run(w, vm, Attack2, 10f);

        // The command's tail aims the dot as well.
        Assert.Equal(["rockets 3", "aim 3"], world.Wrangle.Asked);
    }

    [Fact]
    public void Every_command_aims_the_dot()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);

        world.Run(w, vm, 0, 10f);

        // UpdateLaserDot, no trigger needed (ItemPostFrame's
        // m_hLaserDot arm, tf_weapon_laser_pointer.cpp:154).
        Assert.Equal(["aim 3"], world.Wrangle.Asked);
    }

    [Fact]
    public void A_replay_asks_the_seam_nothing()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f, live: false);

        // The sentry is the server's: the replay's seam is empty and the
        // state writes are the whole of it.
        Assert.Empty(world.Wrangle.Asked);
    }

    [Fact]
    public void The_holster_releases_the_sentry()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);

        // Outside a command, the owner is the weapon state's — the deploy
        // dressed it (the builder's note, stock's GetOwner()).
        world.Behaviour("tf_weapon_laser_pointer").Holster(world.Attack(w, vm, 10f));

        Assert.Equal(["release 3"], world.Wrangle.Asked);
    }

    [Fact]
    public void The_row_kinds_as_the_laser_pointer()
    {
        var world = new TFWeaponWorld(extras: true);

        // The engineer's second unlock: its own kind, the triggers' behaviour.
        Assert.Equal("tf2.laserpointer", world.Row("tf_weapon_laser_pointer").Behaviour);
        Assert.IsType<TFWranglerBehaviour>(world.Behaviour("tf_weapon_laser_pointer"));
    }
}
