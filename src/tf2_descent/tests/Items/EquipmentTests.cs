using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// Equipped items to stats (ruling L2, C.3, plan_tf2_rpg 2b): requirements read Now; an unmet
/// one leaves the item equipped and inactive, contributing nothing through the one
/// <see cref="Contribution"/> path, and meeting it restores the item with nothing moved; gear
/// that grants an attribute can enable other gear, resolved to a fixed point.
/// </summary>
public sealed class EquipmentTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static Equipment Eq => R.ItemRules.Equipment;

    private static readonly RolledAffix Brawn8 = new("of_brawn", 10000);       // +8 Brawn
    private static readonly RolledAffix Leech = new("vampiric", 10000);        // +3 % life leech (a Descent stat)
    private static readonly RolledAffix Heavy = new("heavy", 10000);           // mult_dmg 1.12 (a TF attribute)

    /// <summary>The Black Box (requires 35 Brawn, level 9) with a Descent and a TF affix.</summary>
    private static EquippedItem BlackBox() => new("primary", Item("black_box", 2, 20, Leech, Heavy));

    [Fact]
    public void An_item_whose_requirement_is_unmet_is_inactive_and_contributes_nothing()
    {
        var sheet = Soldier(R, brawn: 34);
        var result = Eq.Resolve(sheet, [BlackBox()]);
        var stats = R.Heroes.Stats(sheet, result.Contributions);

        Assert.Equal((SourceState.Inactive, InactiveReason.Requirement), (result.State("primary").State, result.State("primary").Reason));
        Assert.NotEmpty(result.Contributions);                                   // it is still there, marked
        Assert.All(result.Contributions, c => Assert.Equal(SourceState.Inactive, c.Source.State));
        Assert.Equal(0, stats.Total("life_leech_pct"));
        Assert.Empty(result.TFAttributes);
    }

    [Fact]
    public void Meeting_the_requirement_restores_the_item_with_nothing_moved()
    {
        var equipped = new List<EquippedItem> { BlackBox() };
        var unmet = Eq.Resolve(Soldier(R, brawn: 34), equipped);
        var met = Eq.Resolve(Soldier(R, brawn: 35), equipped);

        Assert.Equal(SourceState.Inactive, unmet.State("primary").State);
        Assert.Equal(SourceState.Active, met.State("primary").State);
        Assert.Equal(3, R.Heroes.Stats(Soldier(R, brawn: 35), met.Contributions).Total("life_leech_pct"), 6);
        Assert.Equal([BlackBox().Item.BaseId], equipped.Select(e => e.Item.BaseId));   // the same one item, in the same slot
    }

    [Fact]
    public void Only_an_active_item_gives_its_tf_attributes()
    {
        Assert.Empty(Eq.Resolve(Soldier(R, brawn: 20), [BlackBox()]).TFAttributes);

        var grant = Assert.Single(Eq.Resolve(Soldier(R, brawn: 40), [BlackBox()]).TFAttributes);
        Assert.Equal(("primary", "mult_dmg", 1.12), (grant.Slot, grant.Attribute, grant.Value));
    }

    [Fact]
    public void An_inactive_weapon_fires_as_its_stock_base()
    {
        Assert.True(Eq.Resolve(Soldier(R, brawn: 20), [BlackBox()]).State("primary").FiresAsStock);
        Assert.False(Eq.Resolve(Soldier(R, brawn: 40), [BlackBox()]).State("primary").FiresAsStock);
    }

    [Fact]
    public void Gear_that_grants_brawn_can_be_what_lets_you_wear_other_gear()
    {
        var sheet = Soldier(R, brawn: 30);
        var hat = new EquippedItem("hat", Item("helmet", 1, 20, Brawn8));   // requires 25: met by 30

        Assert.Equal(SourceState.Inactive, Eq.Resolve(sheet, [BlackBox()]).State("primary").State);
        var both = Eq.Resolve(sheet, [BlackBox(), hat]);
        Assert.Equal(SourceState.Active, both.State("hat").State);
        Assert.Equal(SourceState.Active, both.State("primary").State);      // 30 + 8 = 38 >= 35
    }

    [Fact]
    public void An_item_never_meets_its_own_requirement()
    {
        // The helmet needs 25 Brawn and grants 8: at 20, counting its own 8 would make 28.
        var result = Eq.Resolve(Soldier(R, brawn: 20), [new EquippedItem("hat", Item("helmet", 1, 20, Brawn8))]);

        Assert.Equal(SourceState.Inactive, result.State("hat").State);
    }

    [Fact]
    public void A_chain_of_enabling_items_resolves_in_any_order_within_n_plus_one_passes()
    {
        // 20 Brawn; the cap (no requirement) gives 8 -> 28; the helmet (25) gives 8 -> 36; the Black Box needs 35.
        var cap = new EquippedItem("hat", Item("cap", 1, 20, Brawn8));
        var helmet = new EquippedItem("trinket1", Item("helmet", 1, 20, Brawn8));
        EquippedItem[] forward = [cap, helmet, BlackBox()];
        EquippedItem[] backward = [BlackBox(), helmet, cap];
        var sheet = Soldier(R, brawn: 20);

        var a = Eq.Resolve(sheet, forward);
        var b = Eq.Resolve(sheet, backward);

        Assert.All(a.States, s => Assert.Equal(SourceState.Active, s.State));
        Assert.Equal(a.States.OrderBy(s => s.Slot), b.States.OrderBy(s => s.Slot));
        Assert.Equal(4, b.Passes);                     // one item a pass, then a pass that adds nothing
        Assert.True(b.Passes <= backward.Length + 1);
    }

    [Fact]
    public void A_hero_below_the_bases_level_cannot_use_it()
    {
        var result = Eq.Resolve(Soldier(R, level: 8, brawn: 40), [BlackBox()]);

        Assert.Equal((SourceState.Inactive, InactiveReason.Level), (result.State("primary").State, result.State("primary").Reason));
    }

    [Fact]
    public void An_unidentified_item_counts_for_nothing()
    {
        var item = new EquippedItem("primary", BlackBox().Item with { Identified = false });
        var result = Eq.Resolve(Soldier(R, brawn: 40), [item]);

        Assert.Equal(InactiveReason.Unidentified, result.State("primary").Reason);
        Assert.Equal(0, R.Heroes.Stats(Soldier(R, brawn: 40), result.Contributions).Total("life_leech_pct"));
    }

    [Fact]
    public void Meets_requirements_reads_now_not_base()
    {
        var sheet = Soldier(R, brawn: 30);
        var item = BlackBox().Item;
        var buff = new Contribution("brawn", 5, new ContributionSource(SourceKind.Buff, "orders"));

        Assert.False(Eq.MeetsRequirements(item, R.Heroes.Stats(sheet)));
        Assert.True(Eq.MeetsRequirements(item, R.Heroes.Stats(sheet, [buff])));
    }

    [Fact]
    public void The_stats_that_act_on_the_item_itself_are_not_the_heros()
    {
        var item = new EquippedItem("primary", Item("rocket_launcher", 2, 20, new RolledAffix("of_permanence", 10000), new RolledAffix("fragile", 10000)));
        var result = Eq.Resolve(Soldier(R), [item]);

        Assert.DoesNotContain(result.Contributions, c => c.Stat is "indestructible" or "wear_rate_pct");
    }
}
