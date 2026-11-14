using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// Stock's <c>CTFWeaponBase::GetWorldModel</c> (tf_weaponbase.cpp:681-701) as
/// the chain's rule: the holder's item names the model — its
/// <c>model_world</c>, else its player display model for the holder's class —
/// and the row's own only when it names neither.
/// </summary>
public sealed class TFItemWorldModelTests
{
    private const int Heavy = 3;

    private const string Row = "tf_weapon_fists";

    private const string RowsModel = "models/weapons/w_fists.mdl";

    private const string Gloves = "models/workshop/weapons/c_models/c_gloves/c_gloves.mdl";

    /// <summary>The unlock, a world-model-toting item, and one with neither.</summary>
    private const string Schema = """
        "items_game"
        {
            "qualities"
            {
                "unique"
                {
                    "value"	"6"
                }
            }
            "attributes"
            {
            }
            "items"
            {
                "239"
                {
                    "name"	"The Gloves of Running Urgently"
                    "item_class"	"tf_weapon_fists"
                    "item_quality"	"unique"
                    "item_slot"	"melee"
                    "model_player"	"models/workshop/weapons/c_models/c_gloves/c_gloves.mdl"
                    "used_by_classes"
                    {
                        "heavy"	"1"
                    }
                }
                "240"
                {
                    "name"	"boxed"
                    "item_class"	"tf_weapon_fists"
                    "item_quality"	"unique"
                    "model_world"	"models/weapons/c_models/c_boxed.mdl"
                    "model_player"	"models/weapons/c_models/c_boxed_view.mdl"
                    "used_by_classes"
                    {
                        "heavy"	"1"
                    }
                }
                "241"
                {
                    "name"	"bare"
                    "item_class"	"tf_weapon_fists"
                    "item_quality"	"unique"
                    "used_by_classes"
                    {
                        "heavy"	"1"
                    }
                }
            }
        }
        """;

    private static ItemDefinition Item(int defindex) => SchemaParser.Parse(Schema).Items[defindex];

    private static TFItemWorldModelRule Rule(int defindex)
    {
        var owners = new FakeOwners();
        owners.Classes[Heavy] = TFClass.HeavyWeapons;
        owners.ByName[Heavy] = new() { [Row] = new AttributeContainer(Item(defindex)) };
        return new(owners);
    }

    private static WeaponType Fists() => new(Row, Interval: 0.5f);

    private static string? Model(TFItemWorldModelRule rule) => rule.Model(Heavy, Fists(), RowsModel);

    [Fact]
    public void AnEquippedUnlockWearsItsOwnWorldModel()
    {
        Assert.Equal(Gloves, Model(Rule(239)));
    }

    [Fact]
    public void AnItemsWorldModelBeatsItsPlayerModel()
    {
        Assert.Equal("models/weapons/c_models/c_boxed.mdl", Model(Rule(240)));
    }

    [Fact]
    public void AnItemWithNoModelKeepsTheRowsModel()
    {
        Assert.Equal(RowsModel, Model(Rule(241)));
    }

    [Fact]
    public void AStockWeaponKeepsTheRowsModel()
    {
        // No item behind the row at all: stock's fall-through,
        // BaseClass::GetWorldModel — the row's own, untouched.
        var owners = new FakeOwners();
        owners.Classes[Heavy] = TFClass.HeavyWeapons;

        Assert.Equal(RowsModel, new TFItemWorldModelRule(owners).Model(Heavy, Fists(), RowsModel));
    }

    [Fact]
    public void AClasslessHolderKeepsTheRowsModelToo()
    {
        // The per-class ask is stock's GetPlayerDisplayModel(iClass): class -1
        // is outside the array, and the base display model answers instead.
        var owners = new FakeOwners();
        owners.ByName[Heavy] = new() { [Row] = new AttributeContainer(Item(239)) };

        Assert.Equal(Gloves, new TFItemWorldModelRule(owners).Model(Heavy, Fists(), RowsModel));
    }
}
