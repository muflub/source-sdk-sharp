
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Mode;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The class the mode places a hero as is the hero sheet's
/// (<see cref="SheetHeroClass"/>), and the default before there is a sheet.
/// </summary>
public sealed class HeroClassTests
{
    [Theory]
    [InlineData("scout", TFClass.Scout)]
    [InlineData("soldier", TFClass.Soldier)]
    [InlineData("pyro", TFClass.Pyro)]
    [InlineData("demoman", TFClass.Demoman)]
    [InlineData("heavy", TFClass.HeavyWeapons)]
    [InlineData("engineer", TFClass.Engineer)]
    [InlineData("medic", TFClass.Medic)]
    [InlineData("sniper", TFClass.Sniper)]
    [InlineData("spy", TFClass.Spy)]
    public void Each_rules_class_is_its_TF2_class(string id, TFClass expected)
    {
        Assert.NotNull(new TF2.Descent.Rules.TF2DescentRules().Heroes.FindClass(id));
        Assert.Equal(expected, HeroClasses.ToTF(id));
    }

    [Fact]
    public void An_unknown_class_id_is_no_TF2_class()
    {
        Assert.Equal(TFClass.Undefined, HeroClasses.ToTF("civilian"));
    }

    [Fact]
    public void An_unhosted_hero_is_placed_as_the_sheets_class()
    {
        var rig = new HeroRig();

        Assert.Equal(TFClass.Soldier, new SheetHeroClass(rig.Sheets).ClassOf(HeroRig.Index));
        Assert.NotNull(rig.Sheets.Of(HeroRig.Index));
    }

    [Fact]
    public void A_hosted_hero_is_placed_as_the_leased_sheets_class()
    {
        var rig = new HeroRig(HostingMode.Hosted);
        var medic = rig.Rules.SheetCodec.Write(rig.Rules.Heroes.New("medic", "m"));

        Assert.Null(rig.Sheets.Leased(HeroRig.Index, "765", medic, "medic", "m"));

        Assert.Equal(TFClass.Medic, new SheetHeroClass(rig.Sheets).ClassOf(HeroRig.Index));
    }

    [Fact]
    public void A_hosted_hero_whose_join_has_not_answered_is_placed_as_the_default_class()
    {
        var rig = new HeroRig(HostingMode.Hosted);

        Assert.Equal(DescentMode.DefaultClass, new SheetHeroClass(rig.Sheets).ClassOf(HeroRig.Index));
    }
}
