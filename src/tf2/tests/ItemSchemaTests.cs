using SourceSharp.Abi;

using TF2.Server.Commands;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The item schema parse (stage1 1.4): prefab merge order, the TF overlay's
/// class and slot words, attribute value storage and the refusals, over the
/// trimmed shipped schema and a synthetic prefab file
/// (src/tf2/tests/TestData/items/README.md).
/// </summary>
public sealed class ItemSchemaTests
{
    private static ItemSchemaContents Prefabs() => SchemaParser.Parse(EconTestFiles.Fixture("prefab_order.txt"));

    private static string Layer(ItemSchemaContents schema, int defindex) =>
        schema.Items[defindex].Keys.GetString("layer", null)!;

    // A minimal schema around one items block, for the refusal facts.
    private static string With(string items, string attributes = "", string prefabs = "") =>
        "\"items_game\" { \"qualities\" { \"normal\" { \"value\" \"0\" } } \"prefabs\" { " + prefabs +
        " } \"items\" { " + items + " } \"attributes\" { " + attributes + " } }";

    [Fact]
    public void ThePrefabWordsApplyLastWordFirstSoTheFirstWordWins()
    {
        // "adjective noun": noun, then adjective over it (MergeDefinitionPrefab's backwards walk).
        Assert.Equal("adjective", Layer(Prefabs(), 1));
    }

    [Fact]
    public void ReversedPrefabWordsReverseWhichLayerWins()
    {
        Assert.Equal("noun", Layer(Prefabs(), 2));
    }

    [Fact]
    public void TheItemsOwnKeyIsAppliedOverEveryPrefab()
    {
        Assert.Equal("item", Layer(Prefabs(), 3));
    }

    [Fact]
    public void APrefabsOwnPrefabIsMergedBeneathIt()
    {
        var item = Prefabs().Items[1];

        Assert.Equal("1", item.Keys.GetString("from_base", null));
        Assert.Equal("tf_weapon_bat", item.ItemClass);
    }

    [Fact]
    public void BlocksFromSeveralPrefabsMergeKeyByKey()
    {
        // noun's used_by_classes { scout 1 } and adjective's { pyro secondary } become one block.
        var item = Prefabs().Items[1];

        Assert.Equal(new[] { TFClass.Scout, TFClass.Pyro }, item.UsedByClasses);
    }

    [Fact]
    public void AClassValueThatIsASlotWordOverridesTheItemSlot()
    {
        var item = Prefabs().Items[1];

        Assert.Equal(TFLoadoutSlot.Melee, item.Slot(TFClass.Scout));
        Assert.Equal(TFLoadoutSlot.Secondary, item.Slot(TFClass.Pyro));
        Assert.Equal(TFLoadoutSlot.Invalid, item.Slot(TFClass.Spy));
    }

    [Fact]
    public void UsedByClassesParsesToTheClassSet()
    {
        var schema = EconTestFiles.Trimmed();

        Assert.Equal(new[] { TFClass.Scout }, schema.Items[0].UsedByClasses);
        Assert.Equal(new[] { TFClass.Engineer }, schema.Items[9].UsedByClasses);
        Assert.True(schema.Items[11].UsedBy(TFClass.HeavyWeapons));
    }

    [Fact]
    public void ItemSlotMapsToTheLoadoutSlot()
    {
        var scattergun = EconTestFiles.Trimmed().Items[13];

        Assert.Equal("TF_WEAPON_SCATTERGUN", scattergun.Name);
        Assert.Equal(TFLoadoutSlot.Primary, scattergun.DefaultSlot);
        Assert.Equal(TFLoadoutSlot.Primary, scattergun.Slot(TFClass.Scout));
        Assert.Equal("tf_weapon_scattergun", scattergun.ItemClass);
        Assert.Equal("models/weapons/c_models/c_scattergun.mdl", scattergun.ModelPlayer);
    }

    [Fact]
    public void TheScoutsBasePrimaryIsTheScattergun()
    {
        var schema = EconTestFiles.TrimmedSchema();

        Assert.Equal(13, schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Primary)?.Defindex);
        // Not 23 (TF_WEAPON_PISTOL_SCOUT): 22's prefab weapon_pistol names the scout as well,
        // its used_by_classes merges over that, and the walk takes the first match.
        Assert.Equal(22, schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Secondary)?.Defindex);
        Assert.Equal(0, schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Melee)?.Defindex);
        Assert.Equal(9, schema.BaseItem(TFClass.Engineer, TFLoadoutSlot.Primary)?.Defindex);
    }

    [Fact]
    public void ACosmeticSlotOrANonClassHasNoBaseItem()
    {
        var schema = EconTestFiles.TrimmedSchema();

        Assert.Null(schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Head));
        Assert.Null(schema.BaseItem(TFClass.Undefined, TFLoadoutSlot.Primary));
        Assert.NotNull(schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Primary));
    }

    [Fact]
    public void TheTrimmedSchemaHoldsItsThirtySixBaseItemsAndTheDefault()
    {
        var schema = EconTestFiles.Trimmed();

        Assert.Equal(36, schema.BaseItems.Count);
        Assert.Equal("default", schema.DefaultItem?.Name);
        Assert.Equal(schema.BaseItems.OrderBy(i => i.Defindex), schema.BaseItems);
    }

    [Fact]
    public void ABaseItemBelowTheFirstInsertedIsNotWalked()
    {
        // GenerateBaseItems walks NextInorder from node 0: "5" is inserted first, so "2" is never reached.
        var schema = SchemaParser.Parse(With(
            "\"5\" { \"name\" \"FIVE\" \"item_class\" \"x\" \"baseitem\" \"1\" } " +
            "\"2\" { \"name\" \"TWO\" \"item_class\" \"x\" \"baseitem\" \"1\" } " +
            "\"7\" { \"name\" \"SEVEN\" \"item_class\" \"x\" \"baseitem\" \"1\" }"));

        Assert.Equal(new[] { 5, 7 }, schema.BaseItems.Select(i => i.Defindex));
        Assert.True(schema.Items[2].BaseItem);
    }

    [Fact]
    public void AStoredAsIntegerStaticAttributeKeepsTheIntegerBits()
    {
        var item = EconTestFiles.Trimmed().Items[441];
        var noAmmo = item.Attributes.Single(a => a.Definition.Name == "energy weapon no ammo");

        Assert.True(noAmmo.Definition.StoredAsInteger);
        Assert.Equal(1u, noAmmo.Bits);
    }

    [Fact]
    public void AFloatStaticAttributeOfTheSameTextKeepsTheFloatBits()
    {
        // The control for the fact above: "1" in the same block, a float definition.
        var item = EconTestFiles.Trimmed().Items[441];
        var minicrits = item.Attributes.Single(a => a.Definition.Name == "crits_become_minicrits");

        Assert.False(minicrits.Definition.StoredAsInteger);
        Assert.Equal(BitConverter.SingleToUInt32Bits(1f), minicrits.Bits);
    }

    [Fact]
    public void AStoredAsIntegerAttributeInTheAttributesBlockIsStillAFloat()
    {
        // BInitFromKV_MultiLine passes the backwards-compatibility flag: atof, whatever the storage.
        var hook = EconTestFiles.Trimmed().Items[1152];
        var projectile = hook.Attributes.Single(a => a.Definition.Name == "override projectile type");

        Assert.True(projectile.Definition.StoredAsInteger);
        Assert.Equal(26f, projectile.AsFloat);
    }

    [Fact]
    public void StaticAttrsComeBeforeTheAttributesBlock()
    {
        var item = SchemaParser.Parse(With(
            "\"1\" { \"name\" \"A\" \"item_class\" \"x\" \"attributes\" { \"late\" { \"value\" \"2\" } } \"static_attrs\" { \"early\" \"1\" } }",
            "\"10\" { \"name\" \"early\" } \"11\" { \"name\" \"late\" }")).Items[1];

        Assert.Equal(new[] { "early", "late" }, item.Attributes.Select(a => a.Definition.Name));
    }

    [Fact]
    public void AnAttributeDefinitionReadsItsHookFields()
    {
        var schema = EconTestFiles.TrimmedSchema();
        var def = schema.Attribute(280)!;

        Assert.Equal("override projectile type", def.Name);
        Assert.Equal("override_projectile_type", def.AttributeClass);
        Assert.Equal(AttributeDescriptionFormat.Additive, def.DescriptionFormat);
        Assert.True(def.StoredAsInteger);
        Assert.Same(def, schema.Attribute("OVERRIDE PROJECTILE TYPE"));
    }

    [Fact]
    public void AnAttributeClassFindsItsDefinitions()
    {
        var schema = EconTestFiles.TrimmedSchema();

        Assert.Equal(new[] { 241 }, schema.AttributesOfClass("mult_reload_time_hidden").Select(d => d.Defindex));
        Assert.Empty(schema.AttributesOfClass("no_such_class"));
    }

    [Fact]
    public void AnUnknownDefindexOrNameIsNull()
    {
        var schema = EconTestFiles.TrimmedSchema();

        Assert.Null(schema.Item(99999));
        Assert.Null(schema.Item("NO_SUCH_ITEM"));
        Assert.Null(schema.Attribute(99999));
        Assert.Null(schema.Attribute("no such attribute"));
        Assert.Equal(13, schema.Item("tf_weapon_scattergun")?.Defindex);
    }

    [Fact]
    public void TheItemQualityIsTheQualitiesBlocksValue()
    {
        var prefabs = Prefabs();

        Assert.Equal(6, prefabs.Items[3].Quality);
        Assert.Equal(ItemDefinition.QualityAny, prefabs.Items[1].Quality);
        Assert.Equal(0, EconTestFiles.Trimmed().Items[8].Quality);
    }

    [Fact]
    public void KeysThePortDoesNotReadAreKept()
    {
        var item = EconTestFiles.Trimmed().Items[13];

        Assert.Equal("2010/09/29", item.Keys.GetString("first_sale_date", null));
        Assert.NotNull(item.Keys.Find("image_inventory"));
    }

    [Fact]
    public void AnUnknownPrefabIsAWarningAndTheItemStillLoads()
    {
        var schema = SchemaParser.Parse(With("\"1\" { \"name\" \"A\" \"item_class\" \"x\" \"prefab\" \"nowhere\" }"));

        Assert.True(schema.Items.ContainsKey(1));
        Assert.Contains(schema.Warnings, w => w.Contains("nowhere"));
    }

    [Fact]
    public void APrefabCycleIsRefused()
    {
        var e = Assert.Throws<ItemSchemaException>(() => SchemaParser.Parse(With(
            "\"1\" { \"name\" \"A\" \"item_class\" \"x\" \"prefab\" \"a\" }", "",
            "\"a\" { \"prefab\" \"b\" } \"b\" { \"prefab\" \"a\" }")));

        Assert.Contains(e.Errors, m => m.Contains("cycle"));
    }

    [Fact]
    public void AnAttributeNoDefinitionNamesRefusesTheSchema()
    {
        var e = Assert.Throws<ItemSchemaException>(() => SchemaParser.Parse(With(
            "\"1\" { \"name\" \"A\" \"item_class\" \"x\" \"static_attrs\" { \"ghost\" \"1\" } }")));

        Assert.Contains(e.Errors, m => m.Contains("\"ghost\""));
    }

    [Fact]
    public void ADuplicateItemNameRefusesTheSchema()
    {
        var e = Assert.Throws<ItemSchemaException>(() => SchemaParser.Parse(With(
            "\"1\" { \"name\" \"A\" \"item_class\" \"x\" } \"2\" { \"name\" \"a\" \"item_class\" \"x\" }")));

        Assert.Contains(e.Errors, m => m.Contains("Duplicate name"));
    }

    [Fact]
    public void AnItemInBothWearableAndWeaponSlotsRefusesTheSchema()
    {
        var e = Assert.Throws<ItemSchemaException>(() => SchemaParser.Parse(With(
            "\"1\" { \"name\" \"A\" \"item_class\" \"x\" \"item_slot\" \"melee\" \"used_by_classes\" { \"scout\" \"1\" \"spy\" \"misc\" } }")));

        Assert.Contains(e.Errors, m => m.Contains("both wearable and not wearable"));
    }

    [Fact]
    public void AMissingRequiredBlockRefusesTheSchema()
    {
        var e = Assert.Throws<ItemSchemaException>(() => SchemaParser.Parse("\"items_game\" { \"qualities\" { } \"attributes\" { } }"));

        Assert.Contains(e.Errors, m => m.Contains("\"items\""));
    }

    [Fact]
    public void AMalformedFileIsRefusedWithItsLine()
    {
        // The items block, opened on line 4, is never closed (the innermost open block is the one named).
        string text = "\"items_game\"\n{\n\t\"items\"\n\t{\n\t\t\"1\" { \"name\" \"A\" }\n";

        var e = Assert.Throws<ItemSchemaException>(() => SchemaParser.Parse(text));

        Assert.Equal(4, e.Line);
        Assert.StartsWith("line 4:", e.Message);
    }

    [Fact]
    public void AMissingFileIsReportedNotAnEmptySchema()
    {
        var log = new RecordingLog();
        var schema = new ItemSchema(new EconTestFiles(), log);

        Assert.False(schema.IsLoaded);
        Assert.Contains(ItemSchema.FilePath, schema.Failure);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("no GAME search path"));
    }

    [Fact]
    public void APresentFileLoadsOnceAndSaysWhatItFound()
    {
        var log = new RecordingLog();
        var files = new EconTestFiles().With(ItemSchema.FilePath, EconTestFiles.Fixture("items_game.trimmed.txt"));
        var schema = new ItemSchema(files, log);

        Assert.True(schema.IsLoaded);
        Assert.NotNull(schema.Item(13));
        Assert.Single(files.Reads);
        Assert.Equal(37, schema.Stats!.Items);
        Assert.Contains(log.Lines, l => !l.Warning && l.Text.Contains("37 items, 27 attributes, 28 prefabs, 36 base items"));
    }

    [Fact]
    public void AMalformedFileIsReportedWithItsLine()
    {
        var log = new RecordingLog();
        var schema = EconTestFiles.Schema("\"items_game\"\n{\n\t\"items\" {\n", log);

        Assert.False(schema.IsLoaded);
        Assert.Contains("line 3", schema.Failure);
        Assert.Null(schema.Item(1));
    }

    [Fact]
    public void TheDumpCommandPrintsTheParsedItem()
    {
        string text = SchemaDumpCommand.Describe(EconTestFiles.TrimmedSchema(), "13");

        Assert.Contains("13 TF_WEAPON_SCATTERGUN", text);
        Assert.Contains("item_class tf_weapon_scattergun", text);
        Assert.Contains("used_by_classes: Scout=Primary", text);
    }

    [Fact]
    public void TheDumpCommandListsAnItemsAttributes()
    {
        string text = SchemaDumpCommand.Describe(EconTestFiles.TrimmedSchema(), "441");

        Assert.Contains("\"energy weapon no ammo\" = 1  [energy_weapon_no_ammo, Additive]", text);
        Assert.Contains("\"crits_become_minicrits\" = 1  [", text);
    }

    [Fact]
    public void TheDumpCommandWithNoArgumentPrintsTheCounts()
    {
        string text = SchemaDumpCommand.Describe(EconTestFiles.TrimmedSchema(), "");

        Assert.Contains("37 items", text);
        Assert.DoesNotContain("TF_WEAPON", text);
    }
}
