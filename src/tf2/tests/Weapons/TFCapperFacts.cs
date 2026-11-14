using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The C.A.P.P.E.R (defindex 30666, the invasion pistol), the engineer's
/// secondary carried as an item over the pistol's own script row: the
/// shipped schema gives it to the engineer through the <c>weapon_pistol</c>
/// prefab alone (<c>prefab</c> "no_craft weapon_pistol marketable", shipped
/// items_game.txt:86286), so the equipped item must reach the pistol's row
/// and the press behind it must shoot. Over the shipped fixtures.
/// </summary>
public sealed class TFCapperFacts
{
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>The shipped schema's own blocks for the C.A.P.P.E.R: the prefab
    /// it rides, the attribute definitions its static attributes name, and its
    /// item block — quoted from the shipped items_game.txt (:19814, :247126,
    /// :249704, :86283).</summary>
    private const string CapperSchema = """
        "items_game"
        {
            "prefabs"
            {
                "no_craft"
                {
                    "craft_class"	""
                }
                "weapon_pistol"
                {
                    "craft_class"	""
                    "baseitem"		"0"
                    "enabled"	"1"
                    "item_class"	"tf_weapon_pistol"
                    "craft_material_type"	"weapon"
                    "item_slot"	"secondary"
                    "item_quality"		"unique"
                    "min_ilevel"	"1"
                    "max_ilevel"	"1"
                    "used_by_classes"
                    {
                        "engineer"	"1"
                    }
                }
                "marketable"
                {
                    "static_attrs"
                    {
                        "is marketable"	"1"
                    }
                }
            }
            "qualities"
            {
                "unique"
                {
                    "value"	"1"
                }
            }
            "attributes"
            {
                "350"
                {
                    "name"	"ragdolls become ash"
                    "attribute_class"	"ragdolls_become_ash"
                    "description_format"	"value_is_additive"
                    "hidden"	"1"
                    "effect_type"	"positive"
                }
                "731"
                {
                    "name"	"weapon_allow_inspect"
                    "attribute_class"	"weapon_allow_inspect"
                    "description_format"	"value_is_additive"
                    "hidden"	"1"
                }
                "2028"
                {
                    "name"	"is marketable"
                    "attribute_class"	"is_marketable"
                    "hidden"	"1"
                }
            }
            "items"
            {
                "30666"
                {
                    "name"		"The C.A.P.P.E.R"
                    "prefab"		"no_craft weapon_pistol marketable"
                    "craft_class"		""
                    "item_name"		"#TF_invasion_pistol"
                    "static_attrs"
                    {
                        "weapon_allow_inspect"		"1"
                        "ragdolls become ash"		"1"
                    }
                }
            }
        }
        """;

    /// <summary>The kit over the shipped blocks: the engineer joined, the
    /// inventory wired like the loadout facts.</summary>
    private static (PlayerRig Rig, LocalInventory Inventory, TFLoadoutProvider Loadout) CapperKit()
    {
        var rig = new PlayerRig().As(TFClass.Engineer);
        var schema = EconTestFiles.Schema(CapperSchema);
        var inventory = new LocalInventory(
            new InventoryStore(new ModInfo(_ => null), schema, rig.Log), schema,
            new TFWeaponOwnersTests.NoClients(), rig.Log);
        inventory.OnClientActive(PlayerRig.Index);
        var loadout = new TFLoadoutProvider(inventory, schema, rig.Carriers, rig.Items, rig.Classes,
                                             catalog: rig.Catalog, carriers: rig.Carriers, log: rig.Log);
        return (rig, inventory, loadout);
    }

    /// <summary>
    /// The shipped item shape, whole: the C.A.P.P.E.R's definition merges its
    /// pistol class, slot and used-by from the space-separated prefab words,
    /// and its static attributes land — the item's own pair and the
    /// <c>marketable</c> prefab's, the display-name lookups standing for the
    /// two whose shipped definitions carry spaces in their names
    /// ("ragdolls become ash" :247126, "is marketable" :251189).
    /// </summary>
    [Fact]
    public void The_capper_merges_the_pistol_prefab_and_keeps_the_merged_static_attributes()
    {
        var schema = EconTestFiles.Schema(CapperSchema);
        var definition = schema.Item(30666);

        Assert.NotNull(definition);
        Assert.Equal("tf_weapon_pistol", definition!.ItemClass);
        Assert.Equal(3, definition.Attributes.Count);
    }

    /// <summary>
    /// The engineer's own press with the C.A.P.P.E.R: the item equips into the
    /// secondary through its merged slot, the kit hands the slot the pistol's
    /// row, and the press shoots the script's bullet — one pellet, the script's
    /// fifteen, no crit (tf_weapon_pistol.txt Damage 15, BulletsPerShot 1).
    /// </summary>
    [Fact]
    public void The_capper_equips_the_engineers_secondary_and_the_press_shoots_the_pistol()
    {
        var (rig, inventory, loadout) = CapperKit();
        var item = inventory.Grant(PlayerRig.Index, 30666);
        Assert.NotNull(item);
        Assert.True(inventory.Equip(PlayerRig.Index, TFClass.Engineer, TFLoadoutSlot.Secondary, item!.Id));

        Assert.Contains("tf_weapon_pistol", loadout.Weapons(PlayerRig.Player));

        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_pistol", clip: 8, reserve: 60);

        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(200, 10000));

        var shot = Assert.Single(world.Arms.Bullets);
        Assert.Equal("tf_weapon_pistol", shot.Weapon);
        Assert.Equal(1, shot.Pellets);
        Assert.Equal(new ShotDamage(15f, false), shot.Shot);
        Assert.Equal(7, w.Clip);
    }
}
