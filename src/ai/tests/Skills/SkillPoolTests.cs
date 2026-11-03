using System.Collections.Immutable;
using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

public class SkillPoolTests
{
    private static readonly SkillPool Pool = StandardSkills.StandardPool();

    private static SkillProfile Profile(params int[] levels) => new("p", StandardSkills.Ten, [.. levels]);

    [Fact]
    public void ThereAreTenSkills()
        => Assert.Equal(10, StandardSkills.Ten.Length);

    [Fact]
    public void HeadhunterReplacesSoloness()
        => Assert.Equal((true, false), (StandardSkills.Ten.Any(s => s.Key == "headhunter"), StandardSkills.Ten.Any(s => s.Key == "soloness")));

    [Fact]
    public void ThePoolIsSixtyWithACapOfTen()
        => Assert.Equal((60, 10), (Pool.Points, Pool.Cap));

    [Fact]
    public void AnExactSpendIsAccepted()
        => Assert.True(Pool.Judge(Profile(10, 10, 10, 10, 10, 10, 0, 0, 0, 0)).Accepted, "60 of 60 fits");

    [Fact]
    public void AnOverspendIsRefusedWithTheSum()
    {
        SkillVerdict v = Pool.Judge(Profile(10, 10, 10, 10, 10, 10, 1, 0, 0, 0));
        Assert.Equal((false, "profile 'p' spends 61 of a 60-point pool"), (v.Accepted, v.Refusal));
    }

    [Fact]
    public void ALevelAboveTheCapIsRefusedNamingTheSkill()
    {
        SkillVerdict v = Pool.Judge(Profile(0, 11, 0, 0, 0, 0, 0, 0, 0, 0));
        Assert.Equal((false, "alertness is 11, above the cap of 10"), (v.Accepted, v.Refusal));
    }

    [Fact]
    public void ANegativeLevelIsRefused()
        => Assert.False(Pool.Judge(Profile(0, 0, -1, 0, 0, 0, 0, 0, 0, 0)).Accepted, "a level below 0 is refused");

    [Fact]
    public void AProfileOverOtherSkillsIsRefused()
    {
        ImmutableArray<SkillDefinition> eleven = StandardSkills.Ten.Add(new SkillDefinition("soloness", "Soloness"));
        Assert.False(Pool.Judge(new SkillProfile("p", eleven, [.. new int[11]])).Accepted, "eleven skills are not this pool's ten");
    }

    [Fact]
    public void ASkillsSpecParsesInTableOrder()
    {
        Pool.TryParse("c", "8,7,6,6,6,6,7,5,5,4", out SkillProfile? p);
        Assert.Equal((8, 4), (p!["accuracy"], p["headhunter"]));
    }

    [Fact]
    public void ASkillsSpecWithNineLevelsIsRefused()
        => Assert.False(Pool.TryParse("c", "1,1,1,1,1,1,1,1,1", out _).Accepted, "nine levels for ten skills");

    [Fact]
    public void ASkillsSpecWithAWordIsRefusedNamingTheSkill()
        => Assert.Equal("cover is 'x', not a number", Pool.TryParse("c", "1,1,1,x,1,1,1,1,1,1", out _).Refusal);

    [Fact]
    public void AnOverspentSkillsSpecGivesNoProfile()
    {
        SkillVerdict v = Pool.TryParse("c", "10,10,10,10,10,10,10,0,0,0", out SkillProfile? p);
        Assert.Equal((false, true), (v.Accepted, p is null));
    }

    [Fact]
    public void TheStyleIsTheTopTwoSkills()
        => Assert.Equal("Discipline Nerve", Profile(1, 1, 1, 1, 1, 1, 9, 1, 8, 1).StyleName);

    [Fact]
    public void AStyleTieGoesToTableOrder()
        => Assert.Equal("Accuracy Alertness", Profile(5, 5, 5, 5, 5, 5, 5, 5, 5, 5).StyleName);
}
