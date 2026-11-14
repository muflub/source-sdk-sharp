using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using Xunit;

namespace TF2.Descent.Tests.Content;

/// <summary>
/// The shared weapon vocabulary (plan_tf2_rpg 2b, 2e, 2f; R12, R17, R25): damage_types.json
/// (the four types the hero sheet resists) and families.json (each weapon family, its slots,
/// its one damage type and the TF item classes under it). Each validation rule is proven by
/// a bad row planted in a copy of the shipped tables, never in the shipped files.
/// </summary>
public sealed class WeaponFamilyTests
{
    /// <summary>
    /// The item classes of items_working.tsv that are not weapons, so no family holds them:
    /// build and disguise tools, consumables, banners, throwables that deal no damage of
    /// their own, the Wrangler (it aims the sentry), movement items and wearables.
    /// </summary>
    private static readonly string[] NotWeapons =
    [
        "tf_weapon_builder", "tf_weapon_sapper", "tf_weapon_invis", "tf_weapon_pda_engineer_destroy",
        "tf_weapon_lunchbox", "tf_weapon_lunchbox_drink", "tf_weapon_buff_item",
        "tf_weapon_jar", "tf_weapon_jar_milk", "tf_weapon_jar_gas", "tf_weapon_laser_pointer",
        "tf_weapon_parachute", "tf_weapon_rocketpack",
        "tf_wearable", "tf_wearable_demoshield", "tf_wearable_razorback",
    ];

    private static MemoryContentSource ShippedCopy() =>
        new(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());

    private static MemoryContentSource Planted(string table, Action<JsonNode> mutate)
    {
        var source = ShippedCopy();
        Assert.True(source.TryRead(table, out var bytes), $"no table {table}");
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()));
    }

    private static IReadOnlyList<ContentError> Errors(IContentSource source) => new ContentLoader().Load(source).Errors;

    private static JsonObject Row(JsonNode table, string id) =>
        table.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    private static DescentContent Shipped() =>
        new ContentLoader().Load(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly)).OrThrow();

    /// <summary>The rows of items_working.tsv as (item_class, slot, status), header dropped.</summary>
    private static IReadOnlyList<(string ItemClass, string Slot, string Status)> ItemsWorking()
    {
        string root = RepoRoot();
        string path = Path.Combine(root, "game", "mod_tf2", "scripts", "items", "items_working.tsv");
        Assert.True(File.Exists(path), $"{path} is missing");
        var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("defindex\tname\titem_class\tslot\tclasses\tfamily\tattributes\tstatus\tevidence", lines[0]);
        var rows = lines.Skip(1).Select(l => l.Split('\t')).Select(c => (c[2], c[3], c[7])).ToList();
        Assert.True(rows.Count > 400, $"only {rows.Count} rows read");   // the reading worked
        return rows;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SourceSharp.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("SourceSharp.slnx not found above " + AppContext.BaseDirectory);
    }

    //-------------------------------------------------------------------------
    // The shipped tables
    //-------------------------------------------------------------------------

    [Fact]
    public void The_shipped_damage_types_and_families_load_with_no_errors()
    {
        var result = new ContentLoader().Load(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly));

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Content.DamageTypes.Rows);   // the stimulus: the tables were read
        Assert.NotEmpty(result.Content.Families.Rows);
    }

    [Fact]
    public void There_are_exactly_the_four_damage_types_each_resisted_by_its_hero_resistance()
    {
        var content = Shipped();

        Assert.Equal(["fire", "blast", "bullet", "shock"], content.DamageTypes.Rows.Select(d => d.Id));
        Assert.Equal(content.Hero.Resistances.Select(r => (r.Id, r.Stat)),
            content.DamageTypes.Rows.Select(d => (d.Id, d.ResistStat)));
    }

    [Fact]
    public void Every_family_declares_exactly_one_existing_damage_type()
    {
        var content = Shipped();

        Assert.True(content.Families.Count >= 10, $"only {content.Families.Count} families");
        Assert.All(content.Families.Rows, f => Assert.True(content.DamageTypes.Contains(f.DamageType), $"{f.Id}: {f.DamageType}"));
    }

    [Fact]
    public void A_family_is_found_by_its_item_class()
    {
        var content = Shipped();

        Assert.True(content.TryGetFamilyOfItemClass("tf_weapon_rocketlauncher_directhit", out var family));
        Assert.Equal(("rocket_launcher", "blast"), (family.Id, family.DamageType));
        Assert.False(content.TryGetFamilyOfItemClass("tf_weapon_lunchbox", out _));
    }

    [Fact]
    public void Every_working_weapon_item_class_maps_to_exactly_one_family_holding_its_slot()
    {
        var content = Shipped();
        var weapons = ItemsWorking().Where(r => r.Status == "works" && !NotWeapons.Contains(r.ItemClass)).ToList();

        Assert.NotEmpty(weapons);
        Assert.All(weapons, r =>
        {
            var families = content.Families.Rows.Where(f => f.ItemClasses.Contains(r.ItemClass)).ToList();
            var family = Assert.Single(families);
            Assert.True(family.Slots.Contains(r.Slot), $"{r.ItemClass} is held in {r.Slot}, which family {family.Id} does not list");
        });
    }

    [Fact]
    public void No_family_holds_an_item_class_that_is_not_a_weapon_or_not_in_the_items_table()
    {
        var content = Shipped();
        var tsvClasses = ItemsWorking().Select(r => r.ItemClass).ToHashSet(StringComparer.Ordinal);
        var held = content.Families.Rows.SelectMany(f => f.ItemClasses).ToList();

        Assert.NotEmpty(held);
        Assert.Empty(held.Intersect(NotWeapons));
        Assert.DoesNotContain(held, c => !tsvClasses.Contains(c));
        Assert.DoesNotContain(NotWeapons, c => !tsvClasses.Contains(c));   // the exclusion list is not stale
    }

    [Fact]
    public void Changing_a_familys_damage_type_changes_the_content_hash()
    {
        var before = new ContentLoader().Load(ShippedCopy());
        var after = new ContentLoader().Load(Planted(ContentTables.Families, t => Row(t, "minigun")["damageType"] = "fire"));

        Assert.Empty(after.Errors);
        Assert.NotEqual(before.Content.Hash, after.Content.Hash);
    }

    //-------------------------------------------------------------------------
    // One fact per validation rule
    //-------------------------------------------------------------------------

    [Fact]
    public void A_copy_of_the_shipped_tables_loads_clean_so_a_planted_error_is_the_only_one()
    {
        Assert.Empty(Errors(Planted(ContentTables.Families, _ => { })));
        Assert.Empty(Errors(Planted(ContentTables.DamageTypes, _ => { })));
    }

    [Fact]
    public void A_family_with_an_unknown_damage_type_is_a_dangling_reference()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "shotgun")["damageType"] = "poison"));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Equal((ContentTables.Families, "shotgun"), (e.Table, e.Row));
        Assert.Contains("poison", e.Message);
    }

    [Fact]
    public void A_family_with_no_damage_type_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "minigun").Remove("damageType")));

        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal((ContentTables.Families, "minigun"), (e.Table, e.Row)));
        Assert.Contains(errors, e => e.Rule == ContentRule.Malformed && e.Message.Contains("'damageType' is required"));
    }

    [Fact]
    public void A_family_with_two_damage_types_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Families,
            t => Row(t, "flamethrower")["damageType"] = new JsonArray("fire", "blast")));

        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal((ContentTables.Families, "flamethrower"), (e.Table, e.Row)));
        Assert.Contains(errors, e => e.Rule == ContentRule.Malformed && e.Message.Contains("'damageType'"));
    }

    [Fact]
    public void A_duplicate_family_id_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Families, t =>
        {
            var copy = Row(t, "pistol").DeepClone().AsObject();
            copy["itemClasses"] = new JsonArray("tf_weapon_pistol_copy");
            t.AsArray().Add(copy);
        }));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DuplicateId, e.Rule);
        Assert.Equal((ContentTables.Families, "pistol"), (e.Table, e.Row));
    }

    [Fact]
    public void An_item_class_under_two_families_is_a_load_error_naming_the_second()
    {
        var errors = Errors(Planted(ContentTables.Families,
            t => Row(t, "syringe_gun")["itemClasses"]!.AsArray().Add("tf_weapon_minigun")));   // after minigun in file order

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DuplicateId, e.Rule);
        Assert.Equal((ContentTables.Families, "syringe_gun"), (e.Table, e.Row));
        Assert.Contains("'tf_weapon_minigun'", e.Message);
        Assert.Contains("'minigun'", e.Message);
    }

    [Fact]
    public void A_family_with_no_item_class_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "revolver")["itemClasses"] = new JsonArray()));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.Malformed, e.Rule);
        Assert.Equal((ContentTables.Families, "revolver"), (e.Table, e.Row));
    }

    [Fact]
    public void A_family_in_an_unknown_slot_is_out_of_range()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "bow")["slots"] = new JsonArray("pda2")));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Equal((ContentTables.Families, "bow"), (e.Table, e.Row));
        Assert.Contains("pda2", e.Message);
    }

    [Fact]
    public void A_family_in_no_slot_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "bow")["slots"] = new JsonArray()));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.Malformed, e.Rule);
        Assert.Equal((ContentTables.Families, "bow"), (e.Table, e.Row));
    }

    [Fact]
    public void A_damage_type_resisted_by_a_stat_that_does_not_exist_is_an_unknown_stat()
    {
        var errors = Errors(Planted(ContentTables.DamageTypes, t => Row(t, "shock")["resistStat"] = "resist_shok_pct"));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.UnknownStat, e.Rule);
        Assert.Equal((ContentTables.DamageTypes, "shock"), (e.Table, e.Row));
        Assert.Contains("resist_shok_pct", e.Message);
    }

    [Fact]
    public void A_damage_type_resisted_by_a_stat_that_is_not_a_hero_resistance_is_a_dangling_reference()
    {
        var errors = Errors(Planted(ContentTables.DamageTypes, t => Row(t, "fire")["resistStat"] = "armour"));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Equal((ContentTables.DamageTypes, "fire"), (e.Table, e.Row));
        Assert.Contains("armour", e.Message);
    }
}
