extern alias client;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;

namespace TF2.Descent.Tests;

/// <summary>
/// The skill tree's and the item tooltip's tokens (plan_descent_logic UI3),
/// written from the content by tools/descent-content-tokens: every skill,
/// every tab, every requirement an item base names, each in the language
/// file with the content's own words, so a renamed skill cannot keep its old
/// name on screen.
/// </summary>
public sealed partial class LanguageTokensTests
{
    [Fact]
    public void Every_skill_name_is_in_the_language_file_as_the_content_spells_it()
    {
        var tokens = Tokens();

        foreach (var skill in new Catalog().Skills)
            Assert.Equal(skill.Name, tokens.GetValueOrDefault(skill.NameToken));
    }

    [Fact]
    public void Every_skill_tab_title_is_in_the_language_file_as_the_content_spells_it()
    {
        var tokens = Tokens();

        foreach (var tab in new Catalog().Tabs)
            Assert.Equal(tab.Name, tokens.GetValueOrDefault(tab.TitleToken));
    }

    [Fact]
    public void Every_requirement_an_item_base_names_has_its_tooltip_line()
    {
        var tokens = Tokens();
        var catalog = new Catalog();
        var names = catalog.Attributes.ToDictionary(a => a.Id, a => a.Name);
        int seen = 0;

        foreach (var b in catalog.Bases)
        {
            foreach (var r in b.Requires)
            {
                Assert.Equal($"Requires {names[r.Attribute]} {r.Min}", tokens.GetValueOrDefault(Catalog.RequirementToken(r.Attribute, r.Min)));
                seen++;
            }
            if (b.Level > 1)
                Assert.Equal($"Requires level {b.Level}", tokens.GetValueOrDefault(Catalog.LevelRequirementToken(b.Level)));
        }
        Assert.True(seen > 0, "the bases name requirements");
    }

    [Fact]
    public void Every_stat_an_attribute_point_buys_has_its_name()
    {
        var tokens = Tokens();

        foreach (var a in new Catalog().Attributes)
            foreach (var p in a.PerPoint)
                Assert.True(tokens.ContainsKey(Catalog.StatToken(p.Stat)), $"{Catalog.StatToken(p.Stat)} is in the language file");
    }
}
