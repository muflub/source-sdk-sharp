using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using Xunit;

namespace TF2.Descent.Tests.Content;

/// <summary>
/// The content pipeline (plan_descent_logic RU1, plan_tf2_rpg 1b): the tables shipped in
/// TF2.Descent.Rules load, every validation rule turns a planted bad row into a load error
/// naming it, and the content hash both sides log is stable and sees a one-byte change.
/// Each bad row is planted in a copy of the shipped tables, never in the shipped files.
/// </summary>
public sealed class ContentLoadTests
{
    private static MemoryContentSource ShippedCopy() =>
        new(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());

    private static byte[] Bytes(IContentSource source, string table) =>
        source.TryRead(table, out var b) ? b : throw new InvalidOperationException($"no table {table}");

    /// <summary>The shipped tables with <paramref name="table"/> rewritten by <paramref name="mutate"/>.</summary>
    private static MemoryContentSource Planted(string table, Action<JsonNode> mutate)
    {
        var source = ShippedCopy();
        var node = JsonNode.Parse(Bytes(source, table))!;
        mutate(node);
        return source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()));
    }

    private static IReadOnlyList<ContentError> Errors(IContentSource source) => new ContentLoader().Load(source).Errors;

    private static JsonObject Row(JsonNode table, string id) =>
        table.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    //-------------------------------------------------------------------------
    // The shipped content
    //-------------------------------------------------------------------------

    [Fact]
    public void The_shipped_content_loads_with_no_errors()
    {
        var result = new ContentLoader().Load(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly));

        Assert.Empty(result.Errors);
        Assert.Equal(9, result.Content.Classes.Count);   // the stimulus: tables really were read
        Assert.Equal(4, result.Content.Attributes.Count);
    }

    [Fact]
    public void Every_table_the_loader_reads_is_embedded_in_the_rules_assembly()
    {
        var names = typeof(TF2DescentRules).Assembly.GetManifestResourceNames();

        Assert.NotEmpty(ContentTables.Names());
        Assert.All(ContentTables.Names(), t => Assert.Contains(EmbeddedContentSource.ResourcePrefix + t + ".json", names));
    }

    [Fact]
    public void The_rules_module_builds_its_content_in_its_constructor_and_exposes_the_hash()
    {
        var rules = new TF2DescentRules();

        Assert.Equal(64, rules.ContentHash.Length);
        Assert.Equal(new ContentLoader().Load(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly)).Content.Hash, rules.ContentHash);
    }

    [Fact]
    public void A_copy_of_the_shipped_tables_loads_clean_so_a_planted_error_is_the_only_one()
    {
        // The control for every planted fact below: the unmutated copy has no errors.
        Assert.Empty(Errors(ShippedCopy()));
        Assert.Empty(Errors(Planted(ContentTables.Classes, _ => { })));
    }

    //-------------------------------------------------------------------------
    // The hash
    //-------------------------------------------------------------------------

    [Fact]
    public void The_hash_is_the_same_across_two_loads_and_two_rules_instances()
    {
        var a = new ContentLoader().Load(ShippedCopy()).Content.Hash;
        var b = new ContentLoader().Load(ShippedCopy()).Content.Hash;

        Assert.Equal(a, b);
        Assert.Equal(new TF2DescentRules().ContentHash, new TF2DescentRules().ContentHash);
    }

    [Fact]
    public void Changing_one_byte_of_a_value_changes_the_hash()
    {
        var source = ShippedCopy();
        var bytes = Bytes(source, ContentTables.Classes);
        var at = Encoding.UTF8.GetString(bytes).IndexOf("\"amount\": 125", StringComparison.Ordinal) + "\"amount\": 12".Length;
        Assert.True(at > 20);
        var changed = (byte[])bytes.Clone();
        changed[at] = (byte)'6';                     // 125 -> 126: one byte

        var before = new ContentLoader().Load(source).Content.Hash;
        var after = new ContentLoader().Load(ShippedCopy().Set(ContentTables.Classes, changed));

        Assert.Empty(after.Errors);
        Assert.NotEqual(before, after.Content.Hash);
    }

    [Fact]
    public void Whitespace_and_line_endings_do_not_change_the_hash()
    {
        var source = ShippedCopy();
        var text = Encoding.UTF8.GetString(Bytes(source, ContentTables.Hero));
        var crlf = Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n").Replace(": ", ":    "));

        Assert.Equal(new ContentLoader().Load(source).Content.Hash,
            new ContentLoader().Load(ShippedCopy().Set(ContentTables.Hero, crlf)).Content.Hash);
    }

    //-------------------------------------------------------------------------
    // One fact per validation rule
    //-------------------------------------------------------------------------

    [Fact]
    public void A_missing_required_table_is_a_load_error()
    {
        var errors = Errors(ShippedCopy().Remove(ContentTables.Classes));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.MissingTable, e.Rule);
        Assert.Equal(ContentTables.Classes, e.Table);
    }

    [Fact]
    public void A_duplicate_id_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Stats, t => t.AsArray().Add(Row(t, "armour").DeepClone())));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DuplicateId, e.Rule);
        Assert.Equal("armour", e.Row);
    }

    [Fact]
    public void An_unknown_stat_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Attributes,
            t => Row(t, "brawn")["perPoint"]!.AsArray()[0]!["stat"] = "damage_meele_pct"));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.UnknownStat, e.Rule);
        Assert.Equal((ContentTables.Attributes, "brawn"), (e.Table, e.Row));
        Assert.Contains("damage_meele_pct", e.Message);
    }

    [Fact]
    public void An_unknown_stat_in_the_hero_settings_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Hero, t => t["resistAll"] = "resist_everything_pct"));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.UnknownStat, e.Rule);
        Assert.Equal(ContentTables.Hero, e.Table);
    }

    [Fact]
    public void A_class_naming_an_attribute_that_does_not_exist_is_a_dangling_reference()
    {
        var errors = Errors(Planted(ContentTables.Classes,
            t => Row(t, "medic")["perPoint"]!.AsArray()[0]!["attribute"] = "luck"));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Equal((ContentTables.Classes, "medic"), (e.Table, e.Row));
        Assert.Contains("luck", e.Message);
    }

    [Fact]
    public void A_class_without_bounds_for_an_attribute_is_a_dangling_reference()
    {
        var errors = Errors(Planted(ContentTables.Classes,
            t => Row(t, "spy")["attributes"]!.AsObject().Remove("grit")));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Equal("spy", e.Row);
        Assert.Contains("grit", e.Message);
    }

    [Fact]
    public void An_attribute_that_is_not_a_stat_is_a_dangling_reference()
    {
        var errors = Errors(Planted(ContentTables.Stats,
            t => t.AsArray().Remove(Row(t, "aim"))));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Equal((ContentTables.Attributes, "aim"), (e.Table, e.Row));
    }

    [Fact]
    public void A_cap_below_the_start_is_out_of_range()
    {
        var errors = Errors(Planted(ContentTables.Classes,
            t => Row(t, "heavy")["attributes"]!["smarts"]!["cap"] = 4));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Equal("heavy", e.Row);
        Assert.Contains("smarts.cap", e.Message);
    }

    [Fact]
    public void An_amount_outside_its_stats_bounds_is_out_of_range()
    {
        var errors = Errors(Planted(ContentTables.Attributes,
            t => Row(t, "aim")["perPoint"]!.AsArray()[2]!["amount"] = 101));   // crit_chance_pct is at most 100

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Equal((ContentTables.Attributes, "aim"), (e.Table, e.Row));
        Assert.Contains("crit_chance_pct", e.Message);
    }

    [Fact]
    public void A_roll_whose_low_end_is_above_its_high_end_is_out_of_range()
    {
        var errors = Errors(Planted(ContentTables.Stats, t =>
        {
            Row(t, "max_life")["min"] = 2000;
            Row(t, "max_life")["max"] = 1000;
        }));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Equal((ContentTables.Stats, "max_life"), (e.Table, e.Row));
    }

    [Fact]
    public void A_setting_outside_its_range_is_out_of_range()
    {
        var errors = Errors(Planted(ContentTables.Hero, t => t["resistCap"] = 101));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("resistCap", e.Message);
    }

    [Fact]
    public void A_field_nobody_reads_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Stats, t => Row(t, "armour")["mx"] = 5));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.Malformed, e.Rule);
        Assert.Equal("armour", e.Row);
        Assert.Contains("'mx'", e.Message);
    }

    [Fact]
    public void A_missing_required_field_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Hero, t => t.AsObject().Remove("levelCap")));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.Malformed, e.Rule);
        Assert.Contains("'levelCap'", e.Message);
    }

    [Fact]
    public void A_table_that_is_not_json_is_malformed()
    {
        var errors = Errors(ShippedCopy().Set(ContentTables.Hero, "{ \"levelCap\": "u8.ToArray()));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.Malformed, e.Rule);
        Assert.Equal(ContentTables.Hero, e.Table);
    }

    [Fact]
    public void Content_that_does_not_load_throws_naming_every_error()
    {
        var result = new ContentLoader().Load(Planted(ContentTables.Stats, t => Row(t, "armour")["mx"] = 5));

        var thrown = Assert.Throws<ContentLoadException>(result.OrThrow);
        Assert.Contains("'mx'", thrown.Message);
        Assert.Single(thrown.Errors);
    }
}
