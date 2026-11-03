using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The spawn's choice of held weapon (stock <c>CTFPlayer::Spawn</c>'s walk,
/// tf_player.cpp:4712-4726): the primary that selects, the secondary only
/// when it does NOT select — stock's second arm reads
/// <c>CanBeSelected()</c> unnegated (:4721) — and the melee last. The spy's
/// knife on spawn is that quirk: his revolver selects, so the walk steps
/// over it and takes the melee.
/// </summary>
public sealed class TFSpawnWeaponWalkTests
{
    private static (PlayerRig Rig, TFSpawnWeaponWalk Walk, CarriedFake Carried) Make(TFClass cls,
        Func<string, bool>? selectable = null)
    {
        var rig = new PlayerRig().As(cls);
        var carried = new CarriedFake(selectable);
        return (rig, new TFSpawnWeaponWalk(rig.Carriers, rig.Loadout, rig.Classes, carried), carried);
    }

    /// <summary>Everything selects, and every switch takes; the walks are recorded.</summary>
    private sealed class CarriedFake(Func<string, bool>? selectable = null) : ITFCarriedWeapons
    {
        public readonly List<string> Switches = [];

        public EntityHandle WeaponOf(EntityHandle player) => EntityHandle.None;

        public bool Selectable(EntityHandle player, string weapon) => selectable?.Invoke(weapon) ?? true;

        public bool SwitchTo(EntityHandle player, string weapon)
        {
            Switches.Add(weapon);
            return true;
        }
    }

    [Fact]
    public void The_spys_spawn_walk_takes_the_knife()
    {
        var (_, walk, carried) = Make(TFClass.Spy);

        walk.OnSpawned(PlayerRig.Player, 0f);

        Assert.Equal(["tf_weapon_knife"], carried.Switches);
    }

    [Fact]
    public void The_walk_takes_the_primary_that_selects()
    {
        var (_, walk, carried) = Make(TFClass.Soldier);

        walk.OnSpawned(PlayerRig.Player, 0f);

        Assert.Equal(["tf_weapon_rocketlauncher"], carried.Switches);
    }

    [Fact]
    public void The_second_arm_takes_a_secondary_that_does_not_select()
    {
        // The quirk's other side: an unselectable secondary is the one the arm
        // switches to, and the melee is never asked.
        var (_, walk, carried) = Make(TFClass.Spy, weapon => weapon != "tf_weapon_revolver");

        walk.OnSpawned(PlayerRig.Player, 0f);

        Assert.Equal(["tf_weapon_revolver"], carried.Switches);
    }

    [Fact]
    public void A_classless_spawn_walks_nothing()
    {
        var rig = new PlayerRig();
        var carried = new CarriedFake();
        var walk = new TFSpawnWeaponWalk(rig.Carriers, rig.Loadout, rig.Classes, carried);

        walk.OnSpawned(PlayerRig.Player, 0f);

        Assert.Empty(carried.Switches);
    }

    [Fact]
    public void The_walk_never_touches_the_spawn_prologue()
    {
        var (_, walk, carried) = Make(TFClass.Spy);

        walk.OnSpawning(PlayerRig.Player, 0f);

        Assert.Empty(carried.Switches);
    }
}
