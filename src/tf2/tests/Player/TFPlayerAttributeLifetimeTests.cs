using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// How long a player's runtime attributes live
/// (<c>CTFPlayer::AddCustomAttribute</c>): stock keeps them on the player's
/// own list through a regeneration — the kit's items come and go around it —
/// and drops them all at the spawn and at the death
/// (<c>RemoveAllCustomAttributes</c>, tf_player.cpp:3906, :12925).
/// </summary>
public sealed class TFPlayerAttributeLifetimeTests
{
    private const string HookClass = "mult_crit_chance";

    private static (PlayerRig Rig, TFPlayerAttributes Attributes) Soldier()
    {
        var rig = new PlayerRig();
        rig.Engine.Returns("Clock.Now", 5f);
        // The kit grant's own asks: nobody holds a weapon, a model index nobody reads.
        rig.Engine.Returns("Players.GetHandle", PlayerRig.Player);
        rig.Engine.Returns("Players.GetActiveWeapon", string.Empty);
        rig.Engine.Returns("Precache.PrecacheModel", 0);
        rig.Extra.Add(new TFPlayerAttributes(rig.Carriers, rig.Items, rig.Schema, rig.Engine));
        rig.As(TFClass.Soldier);

        // The first kit: the player's manager stands.
        Assert.NotEmpty(rig.Loadout.Weapons(PlayerRig.Player));
        return (rig, rig.Carriers.Part<TFPlayerAttributes>(PlayerRig.Index)!);
    }

    /// <summary>A probe of 4 walked through the player's manager.</summary>
    private static float Walked(PlayerRig rig)
        => new AttributeHooks().Float(4f, HookClass, rig.Items.ManagerOf(PlayerRig.Index));

    [Fact]
    public void ARuntimeAttributeOutlivesAKitRegrant()
    {
        // Regenerate re-runs the kit (InitClass) and never touches the
        // player's own list: the Dalokohs bar's maximum outlives the locker.
        var (rig, attributes) = Soldier();
        attributes.Add("crit mod disabled", 0.5f);
        Assert.Equal(2f, Walked(rig)); // the stimulus is on the manager

        rig.Loadout.Weapons(PlayerRig.Player);

        Assert.Equal(2f, Walked(rig));
    }

    [Fact]
    public void TheSpawnSweepsTheRuntimeAttributes()
    {
        // CTFPlayer::Spawn's RemoveAllCustomAttributes (tf_player.cpp:3906).
        var (rig, attributes) = Soldier();
        attributes.Add("crit mod disabled", 0.5f);
        Assert.Equal(2f, Walked(rig));

        new TFCustomAttributeLifetime(rig.Carriers).OnSpawned(PlayerRig.Player, 5f);

        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void TheDeathSweepsTheRuntimeAttributes()
    {
        // Event_Killed's RemoveAllCustomAttributes (tf_player.cpp:12925).
        var (rig, attributes) = Soldier();
        attributes.Add("crit mod disabled", 0.5f);
        Assert.Equal(2f, Walked(rig));

        new TFCustomAttributeLifetime(rig.Carriers).OnPlayerDied(
            new PlayerDeath(PlayerRig.Player, PlayerRig.Index, EntityHandle.None, default, EntityHandle.None));

        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void ASweptAttributeDoesNotComeBackWithTheNextKit()
    {
        var (rig, attributes) = Soldier();
        attributes.Add("crit mod disabled", 0.5f);
        new TFCustomAttributeLifetime(rig.Carriers).OnSpawned(PlayerRig.Player, 5f);

        rig.Loadout.Weapons(PlayerRig.Player);

        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void AnExpiredAttributeDoesNotComeBackWithTheNextKit()
    {
        var (rig, attributes) = Soldier();
        attributes.Add("crit mod disabled", 0.5f, duration: 2f);
        Assert.Equal(2f, Walked(rig));
        attributes.Tick(8f);

        rig.Loadout.Weapons(PlayerRig.Player);

        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void AWornItemsProvisionOutlivesAKitRegrant()
    {
        // A wearable provides to the manager from its spawn until its strip
        // (TFWearables.Rebuild / RemoveAll) and is no weapon of the kit: the
        // locker's regrant builds a new manager and must not lose it.
        var (rig, _) = Soldier();
        var worn = new AttributeContainer();
        worn.Set(AttributeValue.OfFloat(rig.Schema.Attribute("crit mod disabled")!, 0.5f));
        worn.ProvideTo(Assert.IsType<AttributeContainer>(rig.Items.ManagerOf(PlayerRig.Index)));
        Assert.Equal(2f, Walked(rig));

        rig.Loadout.Weapons(PlayerRig.Player);

        Assert.Equal(2f, Walked(rig));
    }

    [Fact]
    public void TheOutgoingKitsWeaponsDoNotRideOntoTheNewManager()
    {
        var (rig, _) = Soldier();
        int first = Assert.IsType<AttributeContainer>(rig.Items.ManagerOf(PlayerRig.Index)).Providers.Count;
        Assert.True(first > 0, "the first kit provided nothing");

        rig.Loadout.Weapons(PlayerRig.Player);

        Assert.Equal(first, Assert.IsType<AttributeContainer>(rig.Items.ManagerOf(PlayerRig.Index)).Providers.Count);
    }
}
