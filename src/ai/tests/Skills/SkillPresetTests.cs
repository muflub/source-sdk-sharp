using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

public class SkillPresetTests
{
    private static SkillProfile Preset(string name)
    {
        Assert.True(SkillPresets.TryGet(name, out SkillProfile? p), name + " is a preset");
        return p!;
    }

    [Fact]
    public void EasySpendsTwenty()
        => Assert.Equal(20, Preset("easy").Spent);

    [Fact]
    public void NormalSpendsThirtyFive()
        => Assert.Equal(35, Preset("normal").Spent);

    [Fact]
    public void HardSpendsFifty()
        => Assert.Equal(50, Preset("hard").Spent);

    [Fact]
    public void ExpertSpendsTheWholePool()
        => Assert.Equal(StandardSkills.Pool, Preset("expert").Spent);

    [Fact]
    public void EveryPresetFitsThePool()
        => Assert.All(SkillPresets.Names, n => Assert.True(StandardSkills.StandardPool().Judge(Preset(n)).Accepted, n + " fits"));

    [Fact]
    public void EasyIsThePlansTable()
        => Assert.Equal([2, 2, 3, 1, 2, 1, 2, 2, 3, 2], Preset("easy").Levels.ToArray());

    [Fact]
    public void ExpertIsThePlansTable()
        => Assert.Equal([8, 7, 6, 6, 6, 6, 7, 5, 5, 4], Preset("expert").Levels.ToArray());

    [Fact]
    public void HeadhunterSitsAtTwoToFourInThePresets()
        => Assert.All(SkillPresets.Names, n => Assert.InRange(Preset(n)["headhunter"], 2, 4));

    [Fact]
    public void EachPresetSpendsMoreThanTheOneBelow()
    {
        int[] spends = SkillPresets.Names.Select(n => Preset(n).Spent).ToArray();
        Assert.Equal(spends.Order().ToArray(), spends);
    }

    [Fact]
    public void AnUnknownPresetIsNone()
        => Assert.False(SkillPresets.TryGet("insane", out _), "no such preset");
}
