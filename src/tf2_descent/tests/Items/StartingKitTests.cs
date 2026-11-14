using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// starting_kits.json (HE0): each class's starting backpack and belt, in the rules, where the
/// server's StartingKit.cs held them in code. Each validation rule is proven by a bad row
/// planted in a copy of the shipped tables.
/// </summary>
public sealed class StartingKitTests
{
    private static readonly TF2DescentRules Rules = new();

    /// <summary>The kits as StartingKit.cs wrote them at 720e23724.</summary>
    private static readonly Dictionary<string, string[]> CodeKits = new()
    {
        ["soldier"] = ["rocket_launcher", "shotgun", "shovel"],
        ["scout"] = ["scattergun", "pistol", "bat"],
        ["pyro"] = ["flamethrower", "flare_gun", "fire_axe"],
        ["demoman"] = ["grenade_launcher", "stickybomb_launcher", "bottle"],
        ["heavy"] = ["minigun", "fists"],
        ["engineer"] = ["pistol", "wrench"],
        ["medic"] = ["syringe_gun", "medigun", "bonesaw"],
        ["sniper"] = ["sniper_rifle", "smg", "kukri"],
        ["spy"] = ["revolver", "knife"],
    };

    private static readonly string[] CodeBelt = ["health_small", "health_small", "mana_small"];

    private static ContentError One(IContentSource source)
    {
        var errors = Errors(source);
        return Assert.Single(errors);
    }

    [Fact]
    public void The_soldiers_kit_is_the_launcher_shotgun_and_shovel_and_two_health_and_a_mana_potion()
    {
        var kit = Rules.StartingKit("soldier");

        Assert.Equal(["rocket_launcher", "shotgun", "shovel"], kit.Backpack);
        Assert.Equal(["health_small", "health_small", "mana_small"], kit.Belt);
    }

    [Fact]
    public void Every_classs_kit_is_the_kit_the_server_code_gave()
    {
        Assert.Equal(CodeKits.Keys.Order(), Rules.Content.Classes.Rows.Select(c => c.Id).Order());
        Assert.All(CodeKits, k =>
        {
            Assert.Equal(k.Value, Rules.StartingKit(k.Key).Backpack);
            Assert.Equal(CodeBelt, Rules.StartingKit(k.Key).Belt);
        });
    }

    [Fact]
    public void Every_kit_base_exists_and_its_class_may_use_it()
    {
        Assert.All(Rules.Content.Classes.Rows, c =>
        {
            var kit = Rules.StartingKit(c.Id);
            Assert.NotEmpty(kit.Backpack);
            Assert.All(kit.Backpack, b => Assert.True(Rules.Content.ItemBases[b].AllowsClass(c.Id), $"{c.Id}: {b}"));
            Assert.All(kit.Belt, b => Assert.Equal(ItemTables.BeltSlot, Rules.Content.ItemBases[b].Slot));
        });
    }

    [Fact]
    public void A_class_with_no_kit_row_starts_with_nothing()
    {
        var kit = Rules.StartingKit("civilian");

        Assert.Empty(kit.Backpack);
        Assert.Empty(kit.Belt);
    }

    [Fact]
    public void A_kit_base_that_does_not_exist_is_a_dangling_reference()
    {
        var e = One(Planted(StartingKitTable.TableName, t => Row(t, "soldier")["backpack"]!.AsArray().Add("bazooka")));

        Assert.Equal((StartingKitTable.TableName, "soldier", ContentRule.DanglingReference), (e.Table, e.Row, e.Rule));
    }

    [Fact]
    public void A_kit_base_the_class_may_not_use_is_out_of_range()
    {
        var e = One(Planted(StartingKitTable.TableName, t => Row(t, "spy")["backpack"] = new JsonArray("revolver", "shovel")));

        Assert.Equal((StartingKitTable.TableName, "spy", ContentRule.OutOfRange), (e.Table, e.Row, e.Rule));
        Assert.Contains("'shovel'", e.Message);
    }

    [Fact]
    public void A_backpack_base_that_is_no_gear_is_out_of_range()
    {
        var e = One(Planted(StartingKitTable.TableName, t => Row(t, "sniper")["backpack"]!.AsArray().Add("scrap")));

        Assert.Equal((StartingKitTable.TableName, "sniper", ContentRule.OutOfRange), (e.Table, e.Row, e.Rule));
        Assert.Contains("'scrap'", e.Message);
    }

    [Fact]
    public void Two_kit_bases_for_one_slot_are_a_load_error()
    {
        var e = One(Planted(StartingKitTable.TableName, t => Row(t, "soldier")["backpack"]!.AsArray().Add("equalizer")));

        Assert.Equal((StartingKitTable.TableName, "soldier", ContentRule.DuplicateId), (e.Table, e.Row, e.Rule));
        Assert.Contains("'melee'", e.Message);
    }

    [Fact]
    public void A_belt_base_that_is_no_potion_is_out_of_range()
    {
        var e = One(Planted(StartingKitTable.TableName, t => Row(t, "medic")["belt"] = new JsonArray("scrap")));

        Assert.Equal((StartingKitTable.TableName, "medic", ContentRule.OutOfRange), (e.Table, e.Row, e.Rule));
    }

    [Fact]
    public void A_belt_longer_than_the_belt_is_out_of_range()
    {
        var e = One(Planted(StartingKitTable.TableName, t => Row(t, "pyro")["belt"] =
            new JsonArray([.. Enumerable.Repeat("health_small", 20).Select(s => (JsonNode)JsonValue.Create(s))])));

        Assert.Equal((StartingKitTable.TableName, "pyro", ContentRule.OutOfRange), (e.Table, e.Row, e.Rule));
    }

    [Fact]
    public void A_class_with_no_kit_is_a_load_error()
    {
        var e = One(Planted(StartingKitTable.TableName, t => t.AsArray().Remove(Row(t, "heavy"))));

        Assert.Equal((StartingKitTable.TableName, "heavy", ContentRule.DanglingReference), (e.Table, e.Row, e.Rule));
    }

    [Fact]
    public void A_kit_for_no_class_is_a_load_error()
    {
        var e = One(Planted(StartingKitTable.TableName, t =>
        {
            var copy = Row(t, "spy").DeepClone().AsObject();
            copy["id"] = "civilian";
            copy["backpack"] = new JsonArray("cap");
            t.AsArray().Add(copy);
        }));

        Assert.Equal((StartingKitTable.TableName, "civilian", ContentRule.DanglingReference), (e.Table, e.Row, e.Rule));
    }
}
