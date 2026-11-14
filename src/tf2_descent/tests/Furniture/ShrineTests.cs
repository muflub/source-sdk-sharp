using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Hero;
using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// Shrines (plan_descent_logic RU11; plan_tf2_rpg 2l): a fact per shipped effect, each with
/// its expiry on leaving the level, plus one use, party-wide takers and the cryptic message.
/// </summary>
public sealed class ShrineTests
{
    private static readonly TF2DescentRules Rules = new();
    private static ShrineRules Shrines => Rules.Furniture.Shrines;

    private static readonly string[] Party = ["ann", "bob", "cat"];

    private static IReadOnlyCollection<string> Spells(string hero) => hero == "cat" ? [] : ["fireball", "lightning"];

    private static ShrineUse Use(string effect, string user = "ann") =>
        Shrines.Use(new ShrineState("altar", effect), user, Party, Spells, 99);

    private static ShrineBuffs Taken(string effect, string taker = "ann") => ShrineRules.Take(ShrineBuffs.None, Use(effect), taker);

    private static (string, double)[] Stats(ShrineBuffs buffs) =>
        [.. ShrineRules.Contributions(buffs).Select(c => (c.Stat, c.Amount))];

    [Fact]
    public void Fury_gives_25_percent_damage_until_the_level_is_left()
    {
        var buffs = Taken("fury");

        Assert.Equal([("damage_all_pct", 25.0)], Stats(buffs));
        Assert.Empty(Stats(ShrineRules.LeaveLevel(buffs)));
    }

    [Fact]
    public void Plenty_refills_on_use_and_leaves_nothing_to_carry()
    {
        var use = Use("plenty");

        Assert.True(use.Effect.Refill);
        Assert.Empty(Taken("plenty").Active);
    }

    [Fact]
    public void Sight_reveals_the_map_for_the_whole_party()
    {
        var use = Use("sight");

        Assert.True(use.Effect.RevealMap);
        Assert.True(use.PartyWide);
        Assert.Equal(Party, use.Takers.Order());
        Assert.Empty(Taken("sight").Active);
    }

    [Fact]
    public void Lore_raises_a_known_spell_one_rank_until_the_level_is_left()
    {
        var buffs = Taken("lore");

        var ranks = ShrineRules.SpellRanks(buffs);
        var (spell, rank) = Assert.Single(ranks);
        Assert.Contains(spell, Spells("ann"));
        Assert.Equal(1, rank);
        Assert.Empty(ShrineRules.SpellRanks(ShrineRules.LeaveLevel(buffs)));
    }

    [Fact]
    public void Lore_raises_nothing_for_a_hero_who_knows_no_spell()
    {
        var use = Shrines.Use(new ShrineState("altar", "lore"), "cat", Party, Spells, 99);

        Assert.Empty(use.Spells);
        Assert.Empty(ShrineRules.SpellRanks(ShrineRules.Take(ShrineBuffs.None, use, "cat")));
    }

    [Fact]
    public void Lores_spell_is_seed_stable()
    {
        Assert.Equal(Use("lore").Spells["ann"], Use("lore").Spells["ann"]);
    }

    [Fact]
    public void Bargain_trades_smarts_for_brawn_until_the_level_is_left()
    {
        var buffs = Taken("bargain");

        Assert.Equal([("brawn", 5.0), ("smarts", -5.0)], Stats(buffs));
        Assert.Empty(Stats(ShrineRules.LeaveLevel(buffs)));
    }

    [Fact]
    public void Frailty_takes_15_percent_life_until_the_level_is_left()
    {
        var buffs = Taken("frailty");

        Assert.Equal([("max_life_pct", -15.0)], Stats(buffs));
        Assert.Empty(Stats(ShrineRules.LeaveLevel(buffs)));
    }

    [Fact]
    public void Drought_halves_mana_regeneration_for_the_whole_party_until_the_level_is_left()
    {
        var use = Use("drought");

        Assert.Equal(Party, use.Takers.Order());
        foreach (var taker in Party)
        {
            var buffs = ShrineRules.Take(ShrineBuffs.None, use, taker);
            Assert.Equal([("mana_regen_pct", -50.0)], Stats(buffs));
            Assert.Empty(Stats(ShrineRules.LeaveLevel(buffs)));
        }
    }

    [Fact]
    public void Every_shipped_effect_has_a_fact_here()
    {
        string[] covered = ["fury", "plenty", "sight", "lore", "bargain", "frailty", "drought"];

        Assert.Equal(covered.Order(), Rules.Content.Furniture.Shrines.Select(s => s.Id).Order());
    }

    [Fact]
    public void A_shrine_is_used_once()
    {
        var first = Use("fury");
        var again = Shrines.Use(first.Shrine, "bob", Party, Spells, 99);

        Assert.True(first.Ok);
        Assert.Equal(ShrineRefusal.AlreadyUsed, again.Refused);
        Assert.Empty(again.Takers);
    }

    [Fact]
    public void A_shrine_for_one_is_taken_by_its_user_alone()
    {
        var use = Use("fury", "bob");

        Assert.Equal(["bob"], use.Takers.ToArray());
        Assert.False(use.PartyWide);
        Assert.Empty(Stats(ShrineRules.Take(ShrineBuffs.None, use, "ann")));
    }

    [Fact]
    public void A_shrine_speaks_in_its_message_never_its_name()
    {
        Assert.All(Rules.Content.Furniture.Shrines, s =>
        {
            var use = Shrines.Use(new ShrineState("altar", s.Id), "ann", Party, Spells, 1);
            Assert.Equal(s.MessageToken, use.MessageToken);
            Assert.NotEqual(s.NameToken, use.MessageToken);
        });
    }

    [Fact]
    public void A_shrine_buff_reaches_the_hero_sheet_as_a_buff()
    {
        var hero = Rules.Heroes.New("soldier", "t");
        var plain = Rules.Heroes.Stats(hero);
        var frail = Rules.Heroes.Stats(hero, ShrineRules.Contributions(Taken("frailty")));

        Assert.All(ShrineRules.Contributions(Taken("frailty")), c => Assert.Equal(SourceKind.Buff, c.Source.Kind));
        Assert.True(frail.MaxLife < plain.MaxLife);
    }
}
