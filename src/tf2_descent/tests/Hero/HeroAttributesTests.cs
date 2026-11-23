using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Hero;

using Xunit;

using Sheet = TF2.Descent.Tests.Hero.StatsSheet;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// HE3, outgoing stats as TF attributes: through TF2's own attribute walk
/// over the player's manager, the maximum health is the sheet's
/// <see cref="HeroStats.MaxLife"/> and the maximum speed the sheet's
/// <see cref="HeroStats.MoveSpeed"/>, and both follow the stats without a
/// respawn and without a heal.
/// </summary>
public sealed class HeroAttributesTests
{
    [Theory]
    [InlineData(Sheet.Level1, 263)]      // 200 + 18 a level (RU14: 2 was) + 3 a point of Grit 15
    [InlineData(Sheet.Level10, 485)]     // 200 + 18 x 10 + 3 x Grit 35
    [InlineData(Sheet.Level10Contributions, 485)]
    public void TFs_maximum_health_is_the_sheets_maximum_life(Sheet sheet, int life)
    {
        var rig = new HeroStatsRig(sheet);

        Assert.Equal(life, (int)Math.Round(rig.Hero.Stats!.MaxLife));
        Assert.Equal(life, rig.TFNumbers().MaxHealth);
    }

    [Fact]
    public void TFs_maximum_speed_is_the_sheets_move_speed()
    {
        var rig = new HeroStatsRig();
        rig.Dev.Add(HeroRig.Index, "move_speed_pct", 10);
        rig.Hero.Recompute();

        Assert.Equal(264.0, rig.Hero.Stats!.MoveSpeed, 3);
        Assert.Equal(264f, rig.TFNumbers().MaxSpeed, 3);
    }

    [Fact]
    public void A_soldier_with_no_speed_bonus_keeps_TFs_speed()
    {
        var rig = new HeroStatsRig();

        Assert.Equal(HeroStatsRig.SoldierSpeed, rig.TFNumbers().MaxSpeed, 3);
    }

    [Fact]
    public void A_level_up_raises_maximum_health_at_once_without_healing()
    {
        var rig = new HeroStatsRig();
        var container = rig.Attributes.AttributesOf(rig.Player);
        Assert.Equal(263, rig.TFNumbers().MaxHealth);

        Assert.True(rig.Hero.Apply(s => rig.Rig.AtLevel(s, 2)).Ok);

        // The same container, a new value: no rebuild, no respawn.
        Assert.Same(container, rig.Attributes.AttributesOf(rig.Player));
        Assert.Equal(281, rig.TFNumbers().MaxHealth);
        Assert.DoesNotContain(rig.Rig.Engine.Calls, c => c.Member == "Entities.SetHealth");
    }

    [Fact]
    public void A_contribution_changes_TFs_maximum_health_without_a_rebuild()
    {
        var rig = new HeroStatsRig();
        // The manager the kit built before the change: the next walk over it reads the new value.
        var manager = new TF2.Shared.Econ.AttributeContainer();
        rig.Attributes.AttributesOf(rig.Player)!.ProvideTo(manager);
        var hooks = new TF2.Shared.Econ.AttributeHooks();
        Assert.Equal(263, hooks.Int(HeroStatsRig.SoldierHealth, HeroAttributes.MaxHealthClass, manager));

        rig.Dev.Add(HeroRig.Index, "max_life", 30);
        rig.Hero.Recompute();

        Assert.Equal(293, hooks.Int(HeroStatsRig.SoldierHealth, HeroAttributes.MaxHealthClass, manager));
    }

    [Theory]
    [InlineData(SourceState.Inactive)]
    [InlineData(SourceState.Broken)]
    public void An_inactive_or_broken_contribution_changes_nothing(SourceState state)
    {
        var rig = new HeroStatsRig();
        var before = rig.TFNumbers();

        rig.Dev.Add(HeroRig.Index, "max_life", 100, state);
        rig.Dev.Add(HeroRig.Index, "move_speed_pct", 50, state);
        rig.Hero.Recompute();

        Assert.Equal(before, rig.TFNumbers());
    }

    [Fact]
    public void A_hero_who_leaves_takes_the_attributes_off_the_slot()
    {
        var rig = new HeroStatsRig();
        var container = rig.Attributes.AttributesOf(rig.Player)!;
        Assert.NotNull(rig.Attributes.Of(HeroRig.Index));

        rig.Rig.Leave();

        Assert.Null(rig.Attributes.Of(HeroRig.Index));
        Assert.Equal(0, container.Attributes.Length);
    }
}
