using System.Text;
using System.Text.Json;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// What of an item may leave the server (plan_tf2_rpg 2i, D22; ruling L11), asserted on the
/// serialised bytes: an unidentified item's view is its base, rarity and item level and
/// nothing else.
/// </summary>
public sealed class ItemViewTests
{
    private static readonly TF2DescentRules R = NewRules();

    /// <summary>An unidentified item with positive and cursed affixes and an unusual effect: everything there is to hide.</summary>
    private static ItemInstance Hidden()
    {
        for (ulong s = 0; s < 10_000; s++)
        {
            var item = R.ItemRules.Roller.Roll(Ctx(3, 30, depth: 15), s);
            if (!item.Identified && item.UnusualEffect is not null && item.Affixes.Any(a => R.Content.Affixes[a.Id].IsCursed))
                return item;
        }
        throw new InvalidOperationException("no unidentified cursed Unusual in 10,000 seeds: the roll does not hide what it should");
    }

    [Fact]
    public void An_unidentified_items_view_holds_its_base_rarity_and_item_level_and_nothing_else()
    {
        var item = Hidden();
        foreach (var viewer in new[] { ItemViewer.Owner, ItemViewer.Other })
        {
            var bytes = ItemViews.Encode(R.ItemRules.Views.View(item, viewer));
            using var doc = JsonDocument.Parse(bytes);

            Assert.Equal(["base", "rarity", "itemLevel"], doc.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal(item.BaseId, doc.RootElement.GetProperty("base").GetString());
        }
    }

    [Fact]
    public void An_unidentified_items_bytes_name_no_affix_curse_or_effect()
    {
        var item = Hidden();
        var text = Encoding.UTF8.GetString(ItemViews.Encode(R.ItemRules.Views.View(item, ItemViewer.Other)));

        Assert.DoesNotContain("affix", text);
        Assert.DoesNotContain("cursed", text);
        Assert.DoesNotContain("effect", text);
        Assert.All(item.Affixes, a => Assert.DoesNotContain(a.Id, text));
        Assert.DoesNotContain(item.UnusualEffect!, text);
    }

    [Fact]
    public void An_identified_items_view_shows_every_affix_curses_flagged_and_its_effect_to_any_viewer()
    {
        var item = Hidden() with { Identified = true };
        var text = Encoding.UTF8.GetString(ItemViews.Encode(R.ItemRules.Views.View(item, ItemViewer.Other)));

        Assert.All(item.Affixes, a => Assert.Contains("\"id\":\"" + a.Id + "\"", text));
        Assert.Contains("\"cursed\":true", text);
        Assert.Contains("\"effect\":\"" + item.UnusualEffect + "\"", text);
        Assert.Equal(ItemViews.Encode(R.ItemRules.Views.View(item, ItemViewer.Owner)), ItemViews.Encode(R.ItemRules.Views.View(item, ItemViewer.Other)));
    }

    [Fact]
    public void Identifying_reveals_the_affixes_rolled_at_drop_and_never_rolls_again()
    {
        var item = R.RollItem(Ctx(2), 31);
        var before = R.ItemRules.Codec.Instance(item);

        var once = R.Identify(item, 1);
        var other = R.Identify(item, 999);

        Assert.True(once.Ok);
        var after = R.ItemRules.Codec.Instance(once.Produced[0]);
        Assert.True(after.Identified);
        Assert.Equal(before.Affixes.ToArray(), after.Affixes.ToArray());
        Assert.Equal(once.Produced[0].Instance, other.Produced[0].Instance);   // any seed: the same item
        Assert.False(R.Identify(once.Produced[0], 2).Ok);                     // and once known, it stays known
    }
}
