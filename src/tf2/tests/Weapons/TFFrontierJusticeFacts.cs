using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Frontier Justice's bookends (<c>CTFShotgun_Revenge::Holster</c> and
/// <c>::Deploy</c>, <c>tf_weapon_shotgun.cpp:168-195</c>): the boost leaves
/// with the Justice at the switch and returns at its draw, and only while
/// the count stands — the shot's own arms never touch the hands' state.
/// </summary>
public sealed class TFFrontierJusticeFacts
{
    private const string Justice = "tf_weapon_sentry_revenge";

    /// <summary>The world's owner whose window holds <paramref name="crits"/>, the boost as <paramref name="boosted"/> says.</summary>
    private static (TFWeaponWorld World, TFPlayerNetState Carrier, TFConditions Conds) Window(int crits, bool boosted)
    {
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        carrier.RevengeCrits = crits;
        var world = new TFWeaponWorld(new FakePredictedData { Carrier = carrier },
                                      files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));
        var conds = new TFConditions(authoritative: true,
                             new TFCondHolder());
        world.Owners.Conditions[TFWeaponWorld.Owner] = conds;
        if (boosted)
            conds.Add(TFCond.Critboosted);
        return (world, carrier, conds);
    }

    [Fact]
    public void TheJusticeRowIsTheShotguns()
    {
        var (world, _, _) = Window(0, boosted: false);

        // CTFShotgun_Revenge is CTFShotgunBase's: the gun kind the base
        // behaviour runs, the shot's own bookends on it.
        Assert.Equal("tf2.gun", world.Row(Justice).Behaviour);
    }

    [Fact]
    public void TheHolsterTakesTheBoostOffTheHands()
    {
        var (world, carrier, conds) = Window(crits: 2, boosted: true);
        var (w, vm) = world.Armed(Justice, clip: 6, reserve: 8);

        world.Behaviour(Justice).Holster(world.Attack(w, vm, 10f));

        // Holster's RemoveCond while the count stands (tf_weapon_shotgun.cpp:172-175);
        // the pile itself is not the hands' to keep — the next weapon draws plain.
        Assert.False(conds.Has(TFCond.Critboosted));
        Assert.Equal(2, carrier.RevengeCrits);
    }

    [Fact]
    public void TheDrawPutsTheBoostBackOn()
    {
        var (world, _, conds) = Window(crits: 2, boosted: false);
        var (w, vm) = world.Armed(Justice, clip: 6, reserve: 8);

        world.Behaviour(Justice).Deploy(world.Attack(w, vm, 10f));

        // Deploy's AddCond while the count stands (:188-191): the grant that
        // landed under another weapon lights at the Justice's own draw.
        Assert.True(conds.Has(TFCond.Critboosted));
    }

    [Fact]
    public void AnEmptyWindowHolstersNothing()
    {
        var (world, _, conds) = Window(crits: 0, boosted: true);
        var (w, vm) = world.Armed(Justice, clip: 6, reserve: 8);

        world.Behaviour(Justice).Holster(world.Attack(w, vm, 10f));

        // The guard is the count (:172): a boost from another source stands.
        Assert.True(conds.Has(TFCond.Critboosted));
    }
}
