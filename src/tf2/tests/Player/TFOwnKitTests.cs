using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// What a player knows of their own kit (plan_bots_teams_tf2 P7, <see cref="ITFOwnKit"/>), the seam a bot's binding
/// reads its loadout through: each weapon in slot order with the weapon row and catalog kind it became, the worn items
/// in weapon slots after them, and a version that moves with every kit handed.
/// </summary>
public sealed class TFOwnKitTests
{
    private const int Targe = 131;

    private static (PlayerRig Rig, LocalInventory Inventory, TFLoadoutProvider Loadout, WeaponCatalog Catalog) Make(TFClass cls)
    {
        var rig = new PlayerRig().As(cls);
        var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), rig.Schema, rig.Log),
                                           rig.Schema, new TFWeaponOwnersTests.NoClients(), rig.Log);
        inventory.OnClientActive(PlayerRig.Index);
        var shipped = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        var catalog = new WeaponCatalog(() => [new TFWeaponTypes(new TFWeaponScripts(rig.Log, shipped), rig.Classes, rig.Schema)], rig.Log);
        var loadout = LoadoutDeps.Loadout(inventory, rig.Schema, classes: rig.Carriers, items: new TFPlayerItems(), table: rig.Classes,
                                          catalog: catalog, players: rig.Engine, precache: rig.Engine, log: rig.Log);
        return (rig, inventory, loadout, catalog);
    }

    /// <summary>The Heavy's stock kit: the minigun, his shotgun and the fists, in slot order, each with its catalog kind.</summary>
    [Fact]
    public void TheKitIsEachWeaponInSlotOrderWithItsKind()
    {
        var (_, _, loadout, catalog) = Make(TFClass.HeavyWeapons);
        loadout.Weapons(PlayerRig.Player).ToList();
        IReadOnlyList<TFOwnKitItem> kit = new TFOwnKit(loadout, catalog).KitOf(PlayerRig.Index);

        Assert.Equal([(TFLoadoutSlot.Primary, "tf_weapon_minigun"), (TFLoadoutSlot.Secondary, "tf_weapon_shotgun_hwg"), (TFLoadoutSlot.Melee, "tf_weapon_fists")],
                     kit.Select(i => (i.Slot, i.Weapon)));
        Assert.All(kit, i => Assert.Equal(catalog.IndexOf(i.Weapon), i.WeaponKind));
        Assert.All(kit, i => Assert.True(i.WeaponKind >= 0, $"{i.Weapon} has no kind"));
    }

    /// <summary>A shield in the secondary slot is worn, not wielded: it is in the kit after the weapons, with no weapon kind.</summary>
    [Fact]
    public void AWornItemInAWeaponSlotIsInTheKit()
    {
        var (_, inventory, loadout, catalog) = Make(TFClass.Demoman);
        var targe = inventory.Grant(PlayerRig.Index, Targe)!;
        Assert.True(inventory.Equip(PlayerRig.Index, TFClass.Demoman, TFLoadoutSlot.Secondary, targe.Id), "the Targe is equipped as the Demoman's secondary");
        loadout.Weapons(PlayerRig.Player).ToList();

        TFOwnKitItem worn = new TFOwnKit(loadout, catalog).KitOf(PlayerRig.Index)[^1];
        Assert.Equal((TFLoadoutSlot.Secondary, Targe, -1, ""), (worn.Slot, worn.Defindex, worn.WeaponKind, worn.Weapon));
    }

    /// <summary>The version moves with every kit handed, so a reader knows to read it again.</summary>
    [Fact]
    public void EveryKitHandedMovesTheVersion()
    {
        var (_, _, loadout, catalog) = Make(TFClass.Soldier);
        var kits = new TFOwnKit(loadout, catalog);
        int before = kits.Version(PlayerRig.Index);
        loadout.Weapons(PlayerRig.Player).ToList();
        int first = kits.Version(PlayerRig.Index);
        loadout.Weapons(PlayerRig.Player).ToList();
        Assert.True(before < first && first < kits.Version(PlayerRig.Index), $"{before}, {first}, {kits.Version(PlayerRig.Index)}");
    }
}
