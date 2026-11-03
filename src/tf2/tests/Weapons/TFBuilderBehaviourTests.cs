using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The builder's shell (<c>CTFWeaponBuilder</c>): the trigger places under
/// its own refire, the alt-fire turns a quarter turn per press, the deploy
/// brings the blueprint out, the holster stops the placement, and the tail
/// keeps the blueprint at the crosshair. The placement itself is the server's
/// (<see cref="ITFBuilderPlacement"/>, recorded here).
/// </summary>
public sealed class TFBuilderBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;

    [Fact]
    public void The_trigger_places_under_its_own_refire()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f);
        world.Run(w, vm, Attack, 10.1f);          // inside the 0.2 (tf_weapon_builder.cpp:440)
        world.Run(w, vm, Attack, 10.25f);         // past it: the second place, its own 0.2 again

        var places = world.Builder.Asked.Where(a => a.StartsWith("place", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["place 3", "place 3"], places);
        Assert.Equal(10.45f, w.NextPrimaryAttack, 4);
        Assert.Empty(world.Arms.Swings);          // the row is a tool: the core swings nothing
        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void The_alt_fire_turns_a_quarter_turn_per_press()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, Attack2, 10.1f);         // still held: m_bInAttack2 (tf_weapon_builder.cpp:445-449)
        world.Run(w, vm, 0, 10.3f);               // released: the debounce clears
        world.Run(w, vm, Attack2, 10.5f);

        var rotates = world.Builder.Asked.Where(a => a.StartsWith("rotate", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["rotate 3", "rotate 3"], rotates);
        Assert.Equal(10.7f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void The_deploy_brings_the_blueprint_out_at_its_refire()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        world.Behaviour("tf_weapon_builder").Deploy(world.Attack(w, vm, 10f));

        Assert.Equal(["start 3"], world.Builder.Asked);
        // Deploy's own 0.35, past the machine's deploy time (tf_weapon_builder.cpp:173).
        Assert.Equal(10.35f, w.NextPrimaryAttack, 4);
    }

    [Fact]
    public void A_deploy_without_a_command_still_names_the_owner()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        // A deploy runs outside a user command (WeaponCommand default, index
        // 0): the owner is the weapon state's, which the deploy dressed
        // (Arsenal.cs:683) — stock's GetOwnerEntity() at :165.
        world.Behaviour("tf_weapon_builder").Deploy(
            new WeaponAttackContext(world.Catalog.ByIndex(w.WeaponTypeId)!.Value, w, vm, 10f, true, world.Arms));

        Assert.Equal(["start 3"], world.Builder.Asked);
    }

    [Fact]
    public void A_holster_without_a_command_still_names_the_owner()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        world.Behaviour("tf_weapon_builder").Holster(
            new WeaponAttackContext(world.Catalog.ByIndex(w.WeaponTypeId)!.Value, w, vm, 10f, true, world.Arms));

        Assert.Equal(["stop 3"], world.Builder.Asked);
    }

    [Fact]
    public void A_holstered_builder_stops_its_placement()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        world.Behaviour("tf_weapon_builder").Holster(world.Attack(w, vm, 10f));

        Assert.Equal(["stop 3"], world.Builder.Asked);
    }

    [Fact]
    public void The_tail_keeps_the_blueprint_at_the_crosshair()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        world.Run(w, vm, 0, 10f);                 // nothing held: UpdatePlacementState still runs

        Assert.Equal(["update 3"], world.Builder.Asked);
    }

    [Fact]
    public void A_replayed_command_asks_the_server_nothing()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_builder", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f, live: false);

        Assert.Empty(world.Builder.Asked);
        // The state writes are predicted on both sides regardless.
        Assert.Equal(10.2f, w.NextPrimaryAttack, 4);
    }
}
