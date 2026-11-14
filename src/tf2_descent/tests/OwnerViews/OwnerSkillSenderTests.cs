using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Skills;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Skills;

using Xunit;

namespace TF2.Descent.Tests.OwnerViews;

/// <summary>The skill tree's ranks and refusals, as the rules answer them, to the owner alone (plan_descent_logic UI3).</summary>
public sealed class OwnerSkillSenderTests
{
    private static int SkillIndex(OwnerRig rig, string id) => rig.Rules.Content.Skills.Rows.ToList().FindIndex(s => s.Id == id);

    private static SkillRanks Last(OwnerRig rig) => rig.Of<SkillRanks>().Last();

    [Fact]
    public void Every_skill_of_the_table_is_sent_in_its_order()
    {
        var rig = new OwnerRig();

        var ranks = Last(rig);

        int total = rig.Rules.Content.Skills.Count;
        Assert.Equal(total, ranks.Total);
        Assert.Equal(0, ranks.First);
        Assert.Equal(total, ranks.Ranks.Length);
        Assert.Equal(total, ranks.Why.Length);
        Assert.Equal(total, ranks.NextLevel.Length);
    }

    [Fact]
    public void A_skill_above_the_heros_level_is_refused_below_level_with_the_level_it_asks()
    {
        var rig = new OwnerRig();
        rig.Level(3);

        var ranks = Last(rig);

        int salvo = SkillIndex(rig, "salvo");   // tier 2: level 6
        Assert.Equal((byte)SkillWhy.BelowLevel, ranks.Why[salvo]);
        Assert.Equal(6, ranks.NextLevel[salvo]);
    }

    [Fact]
    public void A_tier_one_skill_with_a_point_to_spend_is_buyable()
    {
        var rig = new OwnerRig();
        rig.Level(3);

        Assert.Equal((byte)SkillWhy.Buyable, Last(rig).Why[SkillIndex(rig, "patriot_missile")]);
    }

    [Fact]
    public void With_no_point_a_tier_one_skill_says_so()
    {
        var rig = new OwnerRig();

        Assert.Equal((byte)SkillWhy.NoPoints, Last(rig).Why[SkillIndex(rig, "patriot_missile")]);
    }

    [Fact]
    public void A_skill_whose_prerequisite_is_missing_says_so()
    {
        var rig = new OwnerRig();
        rig.Level(6);

        Assert.Equal((byte)SkillWhy.Prerequisite, Last(rig).Why[SkillIndex(rig, "salvo")]);
    }

    [Fact]
    public void Buying_a_rank_sends_the_new_rank()
    {
        var rig = new OwnerRig();
        rig.Level(3);
        int from = rig.Sent.Count;

        rig.Hero.Apply(s => rig.Rules.Skills.Buy(s, "patriot_missile").Sheet);

        Assert.Equal(1, rig.Of<SkillRanks>(from).Single().Ranks[SkillIndex(rig, "patriot_missile")]);
    }

    [Fact]
    public void Ranks_that_did_not_change_are_not_sent_again()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Hero.Recompute();

        Assert.Empty(rig.Of<SkillRanks>(from));
    }

    [Fact]
    public void The_whole_table_fits_one_message()
    {
        var rig = new OwnerRig();

        var messages = rig.Skills.Of(rig.Hero.Sheet!);

        var only = Assert.Single(messages);
        Assert.InRange(only.Write(new byte[255]), 1, 255);
    }

    [Fact]
    public void A_charge_skills_bar_carries_the_charges_length_in_ticks()
    {
        var rig = new SkillRig();
        rig.Join(level: 6);
        rig.Learn("patriot_missile");
        rig.Learn("salvo");
        rig.Slot(1, "salvo");
        var charge = rig.Skills.Resolve(rig.Hero().Sheet!, rig.Hero().Stats!, SkillBonuses.None, "salvo")
            .Effects.First(e => e.Primitive == Primitive.Charge);

        Assert.Equal(SkillUseKind.Charging, rig.Runtime.Press(SkillRig.Index, 1).Kind);

        var bar = rig.Told.Bars.Last().Bar;
        Assert.Equal(1, bar.ChargeSlot);
        Assert.Equal(rig.Binders.Ticks(charge["time"]), bar.ChargeTicks);
        Assert.True(bar.ChargeTicks > 0);
    }

    [Fact]
    public void A_bar_with_no_charge_carries_no_length()
    {
        var rig = new SkillRig();
        rig.Join(level: 3);
        rig.Learn("patriot_missile");
        rig.Slot(1, "patriot_missile");

        Assert.Equal(0, rig.Told.Bars.Last().Bar.ChargeTicks);
    }
}
