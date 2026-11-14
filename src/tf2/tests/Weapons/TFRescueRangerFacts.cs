using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Rescue Ranger's row (<c>items_working.tsv:997</c>): the shotgun whose
/// shot is the repair bolt rides the projectile shell
/// (<c>CTFShotgunBuildingRescue : CTFShotgun</c>, tf_weapon_shotgun.h:183)
/// and keeps the shotgun's shell-by-shell reload
/// (<c>tf_weapon_shotgun.cpp:68</c>).
/// </summary>
public sealed class TFRescueRangerFacts
{
    private const string Rescue = "tf_weapon_shotgun_building_rescue";

    [Fact]
    public void TheRescueRangerRunsTheProjectileBehaviour()
    {
        // The stock subclass inherits the shotgun's firing, so the kind the
        // script resolves to is the projectile shell, and the catalogue hands
        // the row its behaviour — the bolt's own fire.
        var script = WeaponFixtures.ParseExtra(Rescue);
        Assert.Equal(TFWeaponKind.Projectile, TFWeaponKinds.Of(script));

        var world = new TFWeaponWorld(extras: true);
        Assert.Equal(TFWeaponKinds.Key(TFWeaponKind.Projectile), world.Row(Rescue).Behaviour);
        Assert.NotNull(world.Behaviour(Rescue));
    }

    [Fact]
    public void TheRescueRangerReloadsShellByShell()
    {
        // The shotgun constructor's m_bReloadsSingly (tf_weapon_shotgun.cpp:68)
        // through the rescue's id: the reload starts on the start animation's
        // clock and loads one shell per round time.
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Rescue, clip: 0, reserve: 6);

        world.Run(w, vm, 0, now: 10f);
        Assert.True(w.InReload);
        Assert.Equal(TFWeaponBehaviour.ModeReloadingSingly, w.BehaviourMode & TFWeaponBehaviour.ModeReloadingSingly);

        // The rescue's own clocks (the fixture's 0.1 start, 1.0 the round):
        // the first shell lands a full second past the scattergun's.
        world.Run(w, vm, 0, now: 11.2f);
        Assert.Equal(1, w.Clip);        // one shell only — the clip is not whole yet
        Assert.True(w.InReload);
    }
}
