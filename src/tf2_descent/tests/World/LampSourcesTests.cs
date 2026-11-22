using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Rules.World;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.World;

/// <summary>
/// Where the lamp's stats come from (the user, 2026-11-13: "from skills and itmes"; provisional
/// numbers): the Radiant prefix (brightness) and the of Light suffix (range) on a hat or a trinket,
/// and the Mercenary tab's Lamplighter. Gear grants them only when it counts, as every other stat:
/// an unusable item (L7 as amended) and one whose requirement Now does not meet (L2) grant nothing.
/// A fully invested hero reaches the ceilings; a bare one has lamp.json's lamp.
/// </summary>
public sealed class LampSourcesTests
{
    private static readonly TF2DescentRules R = NewRules();

    private static Equipment Eq => R.ItemRules.Equipment;

    private static LampSettings Lamp => R.Content.Lamp;

    private static readonly RolledAffix Light20 = new("of_light", 10000);    // +20 % lamp range, its top roll
    private static readonly RolledAffix Radiant30 = new("radiant", 10000);   // +30 % lamp brightness, its top roll

    private static HeroStats Worn(HeroSheet sheet, params EquippedItem[] items) =>
        R.Heroes.Stats(sheet, Eq.Resolve(sheet, items).Contributions);

    private static HeroSheet WithLamplighter(int rank, int level = 30) =>
        Soldier(R, level: level) with { SkillRanks = Soldier(R).SkillRanks.SetItem("lamplighter", rank) };

    [Fact]
    public void Of_light_on_a_hat_raises_the_lamps_range()
    {
        var stats = Worn(Soldier(R), new EquippedItem("hat", Item("cap", 1, 10, Light20)));

        Assert.Equal(20, stats.LampRangeBonus, 9);
        Assert.Equal(Lamp.Light.Radius * 1.2, stats.LampRadius, 9);
        Assert.Equal(1, stats.LampBrightness);
    }

    [Fact]
    public void Radiant_on_a_trinket_raises_the_lamps_brightness()
    {
        var stats = Worn(Soldier(R), new EquippedItem("trinket1", Item("dog_tags", 1, 10, Radiant30)));

        Assert.Equal(1.3, stats.LampBrightness, 9);
        Assert.Equal(Lamp.Light.Radius, stats.LampRadius);
    }

    [Fact]
    public void An_unusable_hat_grants_the_lamp_nothing()
    {
        var hat = Item("cap", 1, 10, Light20, Radiant30) with { Durability = 0, MaxDurability = 0 };
        var result = Eq.Resolve(Soldier(R), [new EquippedItem("hat", hat)]);
        var stats = R.Heroes.Stats(Soldier(R), result.Contributions);

        Assert.Equal(InactiveReason.Unusable, result.State("hat").Reason);
        Assert.Contains(result.Contributions, c => c.Stat == Lamp.Range.Stat);   // it is there, and does not count
        Assert.Equal(Lamp.Light.Radius, stats.LampRadius);
        Assert.Equal(1, stats.LampBrightness);
    }

    [Fact]
    public void A_helmet_whose_brawn_requirement_is_unmet_grants_the_lamp_nothing_until_it_is_met()
    {
        var helmet = new EquippedItem("hat", Item("helmet", 1, 10, Light20));   // requires 25 Brawn

        var unmet = Worn(Soldier(R, brawn: 24), helmet);
        var met = Worn(Soldier(R, brawn: 25), helmet);

        Assert.Equal(Lamp.Light.Radius, unmet.LampRadius);
        Assert.Equal(Lamp.Light.Radius * 1.2, met.LampRadius, 9);
    }

    [Fact]
    public void Lamplighter_raises_range_and_brightness_by_its_rank()
    {
        var one = R.Skills.Stats(WithLamplighter(1), SkillBonuses.None);
        var five = R.Skills.Stats(WithLamplighter(5), SkillBonuses.None);

        Assert.Equal((8.0, 15.0), (one.LampRangeBonus, one.LampBrightnessBonus));
        Assert.Equal((40.0, 75.0), (five.LampRangeBonus, five.LampBrightnessBonus));
    }

    [Fact]
    public void Lamplighter_is_open_to_every_class_on_the_mercenary_tab()
    {
        var skill = R.Content.Skills["lamplighter"];

        Assert.Equal(("mercenary", "mercenary", SkillKind.Upgrade), (skill.Class, skill.Tab, skill.Kind));
        Assert.Equal([3, 6, 9, 12, 15], Enumerable.Range(1, 5).Select(r => skill.LevelFloor!.At(r)));
    }

    [Fact]
    public void A_fully_invested_hero_reaches_both_ceilings()
    {
        var sheet = WithLamplighter(5);
        var gear = Eq.Resolve(sheet,
        [
            new EquippedItem("hat", Item("cap", 1, 10, Radiant30, Light20)),
            new EquippedItem("trinket1", Item("dog_tags", 1, 10, Radiant30, Light20)),
        ]);
        var stats = R.Skills.Stats(sheet, SkillBonuses.None, gear.Contributions);

        Assert.True(stats.Total(Lamp.Range.Stat) > Lamp.Range.Max);             // more than the ceiling was given
        Assert.True(stats.Total(Lamp.Brightness.Stat) > Lamp.Brightness.Max);
        Assert.Equal(Lamp.Light.Radius * (1 + Lamp.Range.Max / 100), stats.LampRadius, 9);
        Assert.Equal(1 + Lamp.Brightness.Max / 100, stats.LampBrightness, 9);
    }

    [Fact]
    public void The_lamp_affixes_roll_only_on_a_hat_or_a_trinket()
    {
        foreach (var id in new[] { "of_light", "radiant" })
        {
            var a = R.Content.Affixes[id];
            Assert.Equal(["hat", "trinket"], a.Slots);
        }
        Assert.Equal((Lamp.Range.Stat, Lamp.Brightness.Stat), (R.Content.Affixes["of_light"].Stat, R.Content.Affixes["radiant"].Stat));
    }
}
