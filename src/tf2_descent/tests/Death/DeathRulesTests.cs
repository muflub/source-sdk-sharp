using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Death;
using Xunit;

namespace TF2.Descent.Tests.Death;

/// <summary>
/// What a death does (plan_descent_logic RU10; plan_tf2_rpg 2g, D6, D7, R5): what falls to
/// the corpse, who may loot it, the Lost &amp; Found's clock and fee, and hardcore. The
/// expected numbers are the plan's, written here, so a table that departs from D6 fails.
/// </summary>
public sealed class DeathRulesTests
{
    private static readonly TF2DescentRules Rules = new();
    private static DeathRules Deaths => Rules.Deaths;

    private static CarriedGear Gear() => new(["launcher", "shovel", "hat"], ["pack1", "pack2"], ["sandvich", "bonk"], 340);

    [Fact]
    public void The_shipped_death_table_has_the_plans_numbers()
    {
        var d = Rules.Content.Death;

        Assert.Equal(new DeathDrops(true, true, true, true), d.Drops);
        Assert.Equal(900, d.CorpseLingerSeconds);
        Assert.Equal(25, d.LostAndFoundFeePct);
        Assert.Equal(new DownedSettings(20, 5, 5, 3, 96, 30, 2), d.Downed);
    }

    [Fact]
    public void A_death_drops_everything_equipped_backpack_belt_and_australium_to_the_corpse()
    {
        var corpse = Deaths.Die("hero", hardcore: false, Gear()).Corpse;

        Assert.Equal(["launcher", "shovel", "hat", "pack1", "pack2", "sandvich", "bonk"], corpse.Items.ToArray());
        Assert.Equal(340, corpse.Australium);
        Assert.Equal("hero", corpse.Owner);
    }

    [Fact]
    public void After_a_death_the_hero_keeps_nothing()
    {
        var kept = Deaths.Die("hero", hardcore: false, Gear()).Kept;

        Assert.Empty(kept.Items);
        Assert.Equal(0, kept.Australium);
    }

    [Fact]
    public void A_category_the_table_does_not_drop_stays_with_the_hero()
    {
        var content = Load(n => n["drops"]!["belt"] = false);
        var result = new DeathRules(content).Die("hero", hardcore: false, Gear());

        Assert.Equal(["sandvich", "bonk"], result.Kept.Belt.ToArray());
        Assert.DoesNotContain("sandvich", result.Corpse.Items);
        Assert.Contains("launcher", result.Corpse.Items);
    }

    [Fact]
    public void Only_the_owner_can_loot_a_corpse()
    {
        var corpse = Deaths.Die("hero", hardcore: false, Gear()).Corpse;

        Assert.True(DeathRules.CanLoot(corpse, "hero"));
        Assert.False(DeathRules.CanLoot(corpse, "partner"));
    }

    [Fact]
    public void Nobody_can_loot_a_hardcore_corpse_not_even_its_owner()
    {
        var corpse = Deaths.Die("hero", hardcore: true, Gear()).Corpse;

        Assert.False(DeathRules.CanLoot(corpse, "hero"));
        Assert.False(DeathRules.CanLoot(corpse, "partner"));
    }

    [Fact]
    public void A_corpse_goes_to_the_lost_and_found_fifteen_minutes_after_its_instance_empties()
    {
        Assert.False(Deaths.Expired(899.9));
        Assert.True(Deaths.Expired(900));
    }

    [Fact]
    public void The_lost_and_found_holds_what_the_corpse_held()
    {
        var corpse = Deaths.Die("hero", hardcore: false, Gear()).Corpse;
        var entry = DeathRules.ToLostAndFound(corpse);

        Assert.NotNull(entry);
        Assert.Equal(corpse.Items.ToArray(), entry.Items.ToArray());
        Assert.Equal(340, entry.Australium);
        Assert.Equal("hero", entry.Owner);
    }

    [Fact]
    public void A_hardcore_corpse_never_reaches_the_lost_and_found()
    {
        var corpse = Deaths.Die("hero", hardcore: true, Gear()).Corpse;

        Assert.NotEmpty(corpse.Items);   // the stimulus: there was gear to lose
        Assert.Null(DeathRules.ToLostAndFound(corpse));
    }

    [Theory]
    [InlineData(100, 25)]
    [InlineData(1000, 250)]
    [InlineData(10, 3)]   // 2.5 rounds up
    [InlineData(9, 2)]    // 2.25 rounds down
    [InlineData(0, 0)]
    public void The_lost_and_found_fee_is_a_quarter_of_the_contents_vendor_value(long value, long fee) =>
        Assert.Equal(fee, Deaths.LostAndFoundFee(value));

    [Fact]
    public void A_hardcore_death_leaves_the_hero_fallen_and_unplayable()
    {
        var hero = Rules.Heroes.New("soldier", "t") with { Hardcore = true };

        var after = DeathRules.AfterDeath(hero);

        Assert.True(DeathRules.Playable(hero));
        Assert.True(after.Fallen);
        Assert.False(DeathRules.Playable(after));
    }

    [Fact]
    public void A_softcore_death_leaves_the_sheet_unchanged()
    {
        var hero = Rules.Heroes.New("soldier", "t");

        Assert.Same(hero, DeathRules.AfterDeath(hero));
        Assert.True(DeathRules.Playable(hero));
    }

    [Fact]
    public void A_downed_window_shorter_than_its_floor_is_a_load_error()
    {
        var errors = new ContentLoader().Load(Source(n => n["downed"]!["windowSeconds"] = 4)).Errors;

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Death, ContentRule.OutOfRange), (e.Table, e.Rule));
    }

    //-------------------------------------------------------------------------

    private static MemoryContentSource Source(Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(ContentTables.Death, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return source.Set(ContentTables.Death, Encoding.UTF8.GetBytes(node.ToJsonString()));
    }

    private static DescentContent Load(Action<JsonNode> mutate) => new ContentLoader().Load(Source(mutate)).OrThrow();
}
