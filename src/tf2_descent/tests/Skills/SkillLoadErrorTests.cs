using System.Text.Json.Nodes;
using TF2.Descent.Rules.Content;
using Xunit;
using static TF2.Descent.Tests.Skills.SkillContent;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// R17's load errors and the skill table's other rules (plan_tf2_rpg 2e, R17; RU6 step 2):
/// one fact per rule, each planting its bad row in a copy of the shipped tables, never in
/// the shipped files. The item bases come from a fixture table (the items package ships the
/// real one); its control shows the copy with the fixture loads clean, so each planted error
/// is the only one.
/// </summary>
public sealed class SkillLoadErrorTests
{
    /// <summary>A shared-tab upgrade, for the rules of the tab with no tiers.</summary>
    private static JsonObject SharedRow(string id, int maxRank, JsonNode floor) => new()
    {
        ["id"] = id, ["name"] = "Planted " + id, ["class"] = "mercenary", ["tab"] = "mercenary",
        ["kind"] = "upgrade", ["maxRank"] = maxRank, ["scope"] = "all", ["levelFloor"] = floor,
        ["effect"] = new JsonArray(new JsonObject
        {
            ["primitive"] = "passive",
            ["stats"] = new JsonArray(new JsonObject { ["stat"] = "max_life", ["amount"] = new JsonObject { ["perRank"] = 1 } }),
        }),
    };

    /// <summary>A copy of a shipped row under a new id and name.</summary>
    private static JsonObject Copy(JsonNode table, string from, string id)
    {
        var row = Row(table, from).DeepClone().AsObject();
        row["id"] = id;
        row["name"] = "Planted " + id;
        return row;
    }

    private static IReadOnlyList<ContentError> WithBases(Action<JsonNode> plant)
    {
        var (source, tables) = WithItemBases(SomeBases);
        return Errors(Planted(source, ContentTables.Skills, plant), tables);
    }

    //-------------------------------------------------------------------------
    // Controls
    //-------------------------------------------------------------------------

    [Fact]
    public void The_shipped_tables_with_fixture_item_bases_load_clean()
    {
        var (source, tables) = WithItemBases(SomeBases);
        var result = new ContentLoader(tables).Load(source);

        Assert.Empty(result.Errors);
        // The stimulus: the fixture's bases were read, beside the shipped item bases.
        Assert.All(SomeBases, b => Assert.True(result.Content.KnownItemBases().HasId(b.Id)));
        Assert.Equal(Shipped().ItemBases.Rows.Count + SomeBases.Count(b => !Shipped().ItemBases.Contains(b.Id)),
                     result.Content.KnownItemBases().Ids.Count);
        Assert.True(result.Content.KnownItemBases().HasName("Black Box"));
    }

    [Fact]
    public void The_shipped_item_bases_are_the_item_bases_the_skills_are_checked_against()
    {
        var content = Shipped();

        Assert.NotEmpty(content.ItemBases.Rows);
        Assert.Equal(content.ItemBases.Rows.Count, content.KnownItemBases().Ids.Count);
        Assert.All(content.ItemBases.Rows, row => Assert.True(content.KnownItemBases().HasId(row.Id)));
    }

    [Fact]
    public void A_family_whose_id_is_also_an_item_base_id_is_still_the_family()
    {
        // "rocket_launcher" is a family and, in the fixture, the stock launcher's base id too.
        var (source, tables) = WithItemBases([.. SomeBases, ("rocket_launcher", "Rocket Launcher Prime")]);
        var errors = Errors(Planted(source, ContentTables.Skills,
            t => t.AsArray().Add(Copy(t, "marksmanship", "planted_family").Also(r => r["scope"] = "rocket_launcher"))), tables);

        Assert.Empty(errors);
    }

    //-------------------------------------------------------------------------
    // R17
    //-------------------------------------------------------------------------

    [Fact]
    public void A_skill_that_references_an_item_base_is_a_load_error()
    {
        var errors = WithBases(t => Row(t, "marksmanship")["scope"] = "black_box");

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Skills, "marksmanship", ContentRule.ItemBaseReference), (e.Table, e.Row, e.Rule));
        Assert.Contains("black_box", e.Message);
    }

    [Fact]
    public void A_prerequisite_that_names_an_item_base_is_a_load_error()
    {
        var errors = WithBases(t => Row(t, "patriot_missile")["prerequisites"] = new JsonArray(new JsonObject { ["skill"] = "shovel" }));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.ItemBaseReference, "patriot_missile"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_skill_whose_id_is_an_item_base_id_is_a_load_error()
    {
        var errors = WithBases(t => t.AsArray().Add(Copy(t, "marksmanship", "shovel")));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.NameCollision, "shovel"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_skill_whose_display_name_is_an_item_base_name_is_a_load_error()
    {
        // "Black Box" is the item "The Black Box": names compare without case and a leading "The".
        var errors = WithBases(t => Row(t, "patriot_missile")["name"] = "black box");

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.NameCollision, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("black box", e.Message);
    }

    [Fact]
    public void An_upgrade_that_names_a_family_no_families_row_has_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "marksmanship")["scope"] = "rocket_launchers"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.DanglingReference, "marksmanship"), (e.Rule, e.Row));
        Assert.Contains("rocket_launchers", e.Message);
    }

    [Fact]
    public void An_action_that_names_a_family_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "patriot_missile")["scope"] = "rocket_launcher"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.FamilyOnAction, "patriot_missile"), (e.Rule, e.Row));
    }

    [Fact]
    public void An_action_effect_that_names_a_family_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "patriot_missile")["effect"]!.AsArray()[0]!["family"] = "melee"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.FamilyOnAction, "patriot_missile"), (e.Rule, e.Row));
    }

    [Fact]
    public void An_action_with_no_damage_type_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "patriot_missile").Remove("damageType")));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.Malformed, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("damageType", e.Message);
    }

    [Fact]
    public void An_action_with_an_unknown_damage_type_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "patriot_missile")["damageType"] = "poison"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.DanglingReference, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("poison", e.Message);
    }

    [Fact]
    public void An_upgrade_with_no_scope_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "marksmanship").Remove("scope")));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.Malformed, "marksmanship"), (e.Rule, e.Row));
    }

    //-------------------------------------------------------------------------
    // Prerequisites and synergies
    //-------------------------------------------------------------------------

    [Fact]
    public void A_dangling_prerequisite_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "patriot_missile")["prerequisites"] = new JsonArray(new JsonObject { ["skill"] = "marksmenship" })));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.DanglingReference, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("marksmenship", e.Message);
    }

    [Fact]
    public void A_dangling_synergy_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "patriot_missile")["synergies"]!.AsArray()[0]!["skill"] = "marksmenship"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.DanglingReference, "patriot_missile"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_synergy_that_scales_nothing_the_skill_has_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "patriot_missile")["synergies"]!.AsArray()[0]!["scales"] = "drain_pct"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.DanglingReference, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("drain_pct", e.Message);
    }

    [Fact]
    public void A_prerequisite_cycle_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t =>
        {
            Row(t, "patriot_missile")["prerequisites"] = new JsonArray(new JsonObject { ["skill"] = "marksmanship" });
            Row(t, "marksmanship")["prerequisites"] = new JsonArray(new JsonObject { ["skill"] = "patriot_missile" });
        }));

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(ContentRule.PrerequisiteCycle, e.Rule));
        Assert.Equal(["marksmanship", "patriot_missile"], errors.Select(e => e.Row).Order());
    }

    [Fact]
    public void A_prerequisite_on_itself_is_a_cycle()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "marksmanship")["prerequisites"] = new JsonArray(new JsonObject { ["skill"] = "marksmanship" })));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.PrerequisiteCycle, "marksmanship"), (e.Rule, e.Row));
    }

    //-------------------------------------------------------------------------
    // Tiers, level floors, max ranks
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void A_class_skill_tier_outside_the_six_tiers_is_a_load_error(int tier)
    {
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "marksmanship")["tier"] = tier));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "marksmanship"), (e.Rule, e.Row));
        Assert.Contains("'tier'", e.Message);
    }

    [Fact]
    public void A_class_skill_with_level_floors_of_its_own_is_a_load_error()
    {
        // A class skill unlocks at its tier's level (1 / 6 / 12 / 18 / 24 / 30), never at a floor of its own.
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "marksmanship")["levelFloor"] = 3));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "marksmanship"), (e.Rule, e.Row));
        Assert.Contains("levelFloor", e.Message);
    }

    [Fact]
    public void A_shared_tab_skill_planted_well_formed_loads_clean()
    {
        // The control for the shared-tab facts below.
        var errors = Errors(Planted(ContentTables.Skills,
            t => t.AsArray().Add(SharedRow("planted_ok", 5, new JsonObject { ["perRank"] = 3 }))));

        Assert.Empty(errors);
    }

    [Fact]
    public void A_shared_tab_level_floor_past_the_level_cap_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => t.AsArray().Add(SharedRow("planted_floor", 5, new JsonArray(3, 6, 9, 12, 31)))));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "planted_floor"), (e.Rule, e.Row));
        Assert.Contains("levelFloor[5]", e.Message);
    }

    [Fact]
    public void A_shared_tab_level_floor_that_falls_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => t.AsArray().Add(SharedRow("planted_floor", 5, new JsonArray(3, 6, 5, 12, 15)))));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("levelFloor[3]", e.Message);
    }

    [Fact]
    public void A_shared_tab_toggle_that_drains_mana_is_a_load_error()
    {
        // L26: the shared tab's toggle is a passive switched on and off; Lamplighter itself loads clean (the control above).
        var errors = Errors(Planted(ContentTables.Skills, t =>
        {
            var row = Copy(t, "lamplighter", "planted_toggle");
            row["effect"]![0]!["drain"] = 1;
            t.AsArray().Add(row);
        }));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "planted_toggle"), (e.Rule, e.Row));
        Assert.Contains("drains no mana", e.Message);
    }

    [Fact]
    public void A_shared_tab_toggle_that_costs_mana_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t => t.AsArray().Add(Copy(t, "lamplighter", "planted_toggle").Also(r => r["manaCost"] = 5))));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "planted_toggle"), (e.Rule, e.Row));
        Assert.Contains("costs no mana", e.Message);
    }

    [Fact]
    public void A_shared_tab_skill_with_a_tier_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => t.AsArray().Add(SharedRow("planted_tier", 5, new JsonObject { ["perRank"] = 3 }).Also(r => r["tier"] = 1))));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "planted_tier"), (e.Rule, e.Row));
    }

    [Fact]
    public void An_action_max_rank_past_five_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t =>
        {
            Row(t, "patriot_missile")["maxRank"] = 6;
            Row(t, "patriot_missile")["effect"]!.AsArray()[0]!["damage"] = new JsonObject { ["from"] = 20, ["to"] = 70 };
        }));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("'maxRank'", e.Message);
    }

    [Fact]
    public void A_capstone_of_more_than_one_rank_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills, t =>
            t.AsArray().Add(Copy(t, "patriot_missile", "planted_capstone").Also(r =>
            {
                r["kind"] = "capstone";
                r["tier"] = 6;
                r["maxRank"] = 2;
                r.Remove("synergies");
            }))));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "planted_capstone"), (e.Rule, e.Row));
        Assert.Contains("'maxRank'", e.Message);
    }

    [Fact]
    public void A_shared_tab_max_rank_past_seven_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => t.AsArray().Add(SharedRow("planted_ranks", 8, new JsonObject { ["perRank"] = 3 }))));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "planted_ranks"), (e.Rule, e.Row));
        Assert.Contains("'maxRank'", e.Message);
    }
}

internal static class JsonObjectExtensions
{
    /// <summary>Applies <paramref name="change"/> and returns the object, so a planted row reads in one expression.</summary>
    public static JsonObject Also(this JsonObject o, Action<JsonObject> change)
    {
        change(o);
        return o;
    }
}
