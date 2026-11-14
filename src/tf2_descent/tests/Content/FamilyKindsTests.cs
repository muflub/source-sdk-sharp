using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using Xunit;

namespace TF2.Descent.Tests.Content;

/// <summary>
/// What a weapon family's hit counts as, and which family a TF weapon is, both in the
/// content (families.json's kinds; classes.json's item class words): the server read
/// them from its own table (HeroHitKinds) and two copies of the class-word rule.
/// </summary>
public sealed class FamilyKindsTests
{
    private static readonly DescentContent Content = new TF2DescentRules().Content;

    /// <summary>
    /// The server's table before the kinds moved into the content (HeroHitKinds.KindsOf at
    /// 720e23724), with the flamethrower and the medigun, which had none, given the plain
    /// weapon kind (RU-E item 1): no family's hit changes but by damage_weapon_pct.
    /// </summary>
    private static readonly Dictionary<string, string[]> OldTable = new()
    {
        ["rocket_launcher"] = ["blast", "projectile"], ["grenade_launcher"] = ["blast", "projectile"],
        ["stickybomb_launcher"] = ["blast", "projectile"],
        ["scattergun"] = ["bullet"], ["shotgun"] = ["bullet"], ["pistol"] = ["bullet"], ["revolver"] = ["bullet"],
        ["smg"] = ["bullet"], ["minigun"] = ["bullet"], ["sniper_rifle"] = ["bullet"],
        ["flare_gun"] = ["projectile"], ["syringe_gun"] = ["projectile"], ["bow"] = ["projectile"],
        ["energy_weapon"] = ["projectile"], ["cleaver"] = ["projectile"],
        ["melee"] = ["melee"],
        ["flamethrower"] = ["weapon"], ["medigun"] = ["weapon"],
    };

    private static MemoryContentSource Planted(string table, Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(table, out var bytes), $"no table {table}");
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()));
    }

    private static JsonObject Row(JsonNode table, string id) =>
        table.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    private static IReadOnlyList<ContentError> Errors(IContentSource source) => new ContentLoader().Load(source).Errors;

    [Fact]
    public void Every_familys_kinds_are_the_servers_old_table()
    {
        Assert.Equal(OldTable.Keys.Order(), Content.Families.Rows.Select(f => f.Id).Order());
        Assert.All(Content.Families.Rows, f => Assert.Equal(OldTable[f.Id], f.Kinds));
    }

    [Fact]
    public void Every_working_weapon_item_class_resolves_to_a_family()
    {
        var weapons = WeaponFamilyTests.ItemsWorking()
            .Where(r => r.Status == "works" && !WeaponFamilyTests.NotWeapons.Contains(r.ItemClass))
            .Select(r => r.ItemClass).Distinct().ToList();

        Assert.True(weapons.Count > 50, $"only {weapons.Count} weapon classes");
        Assert.All(weapons, c => Assert.True(Content.TryGetFamilyOfItemClass(c, out _), c));
    }

    [Theory]
    [InlineData("tf_weapon_shotgun_soldier", "shotgun")]
    [InlineData("tf_weapon_shotgun_hwg", "shotgun")]
    [InlineData("tf_weapon_shotgun_pyro", "shotgun")]
    [InlineData("tf_weapon_shotgun_primary", "shotgun")]
    [InlineData("tf_weapon_pistol_scout", "pistol")]
    [InlineData("tf_weapon_syringegun_medic", "syringe_gun")]
    public void A_classs_copy_of_a_shared_weapon_resolves_to_the_weapons_family(string entity, string family)
    {
        Assert.True(Content.TryGetFamilyOfItemClass(entity, out var found), entity);
        Assert.Equal(family, found.Id);
    }

    [Fact]
    public void Every_working_weapon_class_with_any_class_word_resolves_to_its_own_family()
    {
        var words = Content.Classes.Rows.SelectMany(c => c.ItemClassWords).ToList();
        var weapons = WeaponFamilyTests.ItemsWorking()
            .Where(r => r.Status == "works" && !WeaponFamilyTests.NotWeapons.Contains(r.ItemClass))
            .Select(r => r.ItemClass).Distinct().ToList();

        Assert.Contains("hwg", words);   // the stimulus: the words were read
        Assert.All(weapons, c =>
        {
            Assert.True(Content.TryGetFamilyOfItemClass(c, out var own), c);
            Assert.All(words, w =>
            {
                Assert.True(Content.TryGetFamilyOfItemClass(c + "_" + w, out var copy), c + "_" + w);
                Assert.Equal(own.Id, copy.Id);
            });
        });
    }

    [Fact]
    public void A_non_weapon_and_an_unknown_word_have_no_family()
    {
        Assert.False(Content.TryGetFamilyOfItemClass("tf_weapon_jar_milk", out _));
        Assert.False(Content.TryGetFamilyOfItemClass("tf_weapon_lunchbox_soldier", out _));
        Assert.False(Content.TryGetFamilyOfItemClass("tf_weapon_shotgun_robot", out _));
    }

    [Fact]
    public void A_family_with_no_kind_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "minigun")["kinds"] = new JsonArray()));

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Families, "minigun", ContentRule.Malformed), (e.Table, e.Row, e.Rule));
    }

    [Fact]
    public void A_family_whose_kind_is_an_action_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "minigun")["kinds"] = new JsonArray("action")));

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Families, "minigun"), (e.Table, e.Row));
        Assert.Contains("'action'", e.Message);
    }

    [Fact]
    public void A_family_with_an_unknown_kind_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Families, t => Row(t, "bow")["kinds"] = new JsonArray("arrow")));

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Families, "bow"), (e.Table, e.Row));
        Assert.Contains("'arrow'", e.Message);
    }

    [Fact]
    public void An_item_class_word_two_classes_share_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Classes, t => Row(t, "spy")["itemClassWords"]!.AsArray().Add("hwg")));

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Classes, "spy", ContentRule.DuplicateId), (e.Table, e.Row, e.Rule));
        Assert.Contains("'hwg'", e.Message);
    }
}
