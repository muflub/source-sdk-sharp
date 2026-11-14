extern alias client;

using System.Reflection;

using TF2.Descent.Rules.Content;
using TF2.Descent.Server.OwnerViews;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The client's display half of the content (plan_descent_logic UI3): the
/// client embeds the rules' own table files and reads names and shapes from
/// them, so every row it reads is the rules' row, in the rules' order, and
/// the fingerprint the owner's messages carry agrees on both sides.
/// </summary>
public sealed class ClientCatalogTests
{
    private static readonly DescentContent Rules = DescentContent.Shipped();

    private static Catalog Client() => new();

    [Fact]
    public void Every_table_the_client_embeds_is_the_rules_file_byte_for_byte()
    {
        var rules = new EmbeddedContentSource();
        var catalog = Client();

        foreach (string table in Catalog.Tables)
        {
            Assert.True(rules.TryRead(table, out byte[] expected), $"the rules ship {table}");
            Assert.Equal(expected, catalog.Bytes(table));
        }
    }

    [Fact]
    public void The_client_embeds_exactly_the_tables_its_catalog_lists()
    {
        var embedded = typeof(Catalog).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Catalog.ResourcePrefix, StringComparison.Ordinal))
            .Select(n => n[Catalog.ResourcePrefix.Length..^".json".Length])
            .Order(StringComparer.Ordinal);

        Assert.Equal(Catalog.Tables.Order(StringComparer.Ordinal), embedded);
    }

    [Fact]
    public void The_client_references_neither_the_rules_nor_host_contracts()
    {
        var references = typeof(Catalog).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToList();

        Assert.NotEmpty(references);   // the reading worked
        Assert.DoesNotContain("TF2.Descent.Rules", references);
        Assert.DoesNotContain("SourceSharp.Host.Contracts", references);
    }

    [Fact]
    public void The_reference_check_sees_the_rules_where_they_are_referenced()
    {
        // Fail-first arm: the server references the rules, so the same reading names them.
        var references = typeof(ContentKeys).Assembly.GetReferencedAssemblies().Select(r => r.Name);

        Assert.Contains("TF2.Descent.Rules", references);
    }

    [Fact]
    public void Every_skill_reads_as_the_rules_hold_it_in_their_order()
    {
        var client = Client().Skills;
        var rules = Rules.Skills.Rows;

        Assert.Equal(rules.Count, client.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            var c = client[i];
            Assert.Equal((r.Id, r.Name, r.Class, r.Tab, r.Tier, r.Kind.ToString().ToLowerInvariant(), r.MaxRank),
                         (c.Id, c.Name, c.Class, c.Tab, c.Tier, c.Kind, c.MaxRank));
            Assert.Equal(r.Prerequisites.SelectMany(p => p.Skills), c.Prerequisites);
        }
    }

    [Fact]
    public void A_skill_charges_on_the_client_exactly_when_its_rules_effect_has_a_charge()
    {
        var client = Client();

        foreach (var r in Rules.Skills.Rows)
            Assert.Equal(r.Effect.Any(e => e.Primitive == TF2.Descent.Rules.Skills.Primitive.Charge), client.Skill(r.Id)!.Charges);
        Assert.Contains(client.Skills, s => s.Charges);   // the stimulus: the table has charge skills
    }

    [Fact]
    public void Every_skill_tab_reads_as_the_rules_hold_it()
    {
        Assert.Equal(Rules.SkillTabs.Rows.Select(r => (r.Id, r.Class, r.Name)),
                     Client().Tabs.Select(c => (c.Id, c.Class, c.Name)));
    }

    [Fact]
    public void The_tier_levels_and_the_shared_class_are_the_rules()
    {
        var client = Client();

        Assert.Equal(Rules.SkillSettings.TierLevels, client.TierLevels);
        Assert.Equal(Rules.SkillSettings.SharedClass, client.SharedClass);
    }

    [Fact]
    public void Every_item_base_reads_as_the_rules_hold_it_in_their_order()
    {
        var client = Client().Bases;
        var rules = Rules.ItemBases.Rows;

        Assert.Equal(rules.Count, client.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            var c = client[i];
            Assert.Equal((r.Id, r.Name, r.Kind, r.Family, r.Slot, r.Level, r.IsGear),
                         (c.Id, c.Name, c.Kind, c.Family, c.Slot, c.Level, c.IsGear));
            Assert.Equal(r.Classes, c.Classes);
            Assert.Equal(r.Requires.Select(q => (q.Attribute, q.Min)), c.Requires.Select(q => (q.Attribute, q.Min)));
        }
    }

    [Fact]
    public void Every_affix_reads_as_the_rules_hold_it_in_their_order()
    {
        Assert.Equal(Rules.Affixes.Rows.Select(r => (r.Id, r.Name, r.Stat, r.IsTF, r.IsCursed)),
                     Client().Affixes.Select(c => (c.Id, c.Name, c.Stat, c.TF, c.Cursed)));
    }

    [Fact]
    public void Every_attribute_and_what_a_point_buys_reads_as_the_rules_hold_it()
    {
        Assert.Equal(Rules.Attributes.Rows.Select(r => (r.Id, r.Name, string.Join(",", r.PerPoint.Select(p => $"{p.Stat}:{p.Amount}")))),
                     Client().Attributes.Select(c => (c.Id, c.Name, string.Join(",", c.PerPoint.Select(p => $"{p.Stat}:{p.Amount}")))));
    }

    [Fact]
    public void The_named_tables_read_as_the_rules_hold_them()
    {
        var client = Client();

        Assert.Equal(Rules.Rarities.Rows.Select(r => (r.Id, r.Name)), client.Rarities.Select(c => (c.Id, c.Name)));
        Assert.Equal(Rules.Families.Rows.Select(r => (r.Id, r.Name)), client.Families.Select(c => (c.Id, c.Name)));
        Assert.Equal(Rules.DamageTypes.Rows.Select(r => (r.Id, r.Name)), client.DamageTypes.Select(c => (c.Id, c.Name)));
        Assert.Equal(Rules.UnusualEffects.Rows.Select(r => (r.Id, r.Name)), client.UnusualEffects.Select(c => (c.Id, c.Name)));
        Assert.Equal(Rules.Uniques.Rows.Select(r => (r.Id, r.Name)), client.Uniques.Select(c => (c.Id, c.Name)));
    }

    [Fact]
    public void The_equipment_slots_and_the_backpack_base_are_the_rules()
    {
        var client = Client();

        Assert.Equal(Rules.InventorySettings.Equipment.Select(r => (r.Id, r.Accepts)), client.EquipSlots.Select(c => (c.Id, c.Accepts)));
        Assert.Equal(Rules.InventorySettings.Backpack.Base, client.BackpackBase);
    }

    [Fact]
    public void Client_and_server_compute_one_fingerprint()
    {
        Assert.Equal(ContentKeys.Fingerprint(Rules), Client().Fingerprint);
    }

    [Fact]
    public void A_table_with_one_row_more_has_another_fingerprint()
    {
        // The fingerprint sees a change: the shipped ids, then the same with one base added.
        var shipped = Rules.ItemBases.Rows.Select(r => r.Id).ToList();
        uint before = client::TF2.Descent.Shared.ContentFingerprint.Of(shipped);
        uint after = client::TF2.Descent.Shared.ContentFingerprint.Of([.. shipped, "one_more"]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void An_assembly_without_the_tables_refuses_to_read_rather_than_answer_empty()
    {
        var catalog = Catalog.From(typeof(ClientCatalogTests).Assembly);

        Assert.Null(catalog.Bytes("skills"));
        Assert.Throws<InvalidOperationException>(() => catalog.Skills);
    }
}
