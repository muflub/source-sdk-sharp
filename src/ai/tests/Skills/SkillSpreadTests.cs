using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

/// <summary>
/// A preset bot's own allocation (plan_hl2mp_ai §10; user ruling 2026-11-02:
/// "seems all the bot skills are exactly the same for med bots, not
/// variance"): the seeded walk spends the preset's exact total, within the
/// cap and over the floor, differs bot to bot, is the same for the same seed,
/// and gives more than one best trait, so names draw on more than one.
/// </summary>
public sealed class SkillSpreadTests
{
    private static SkillProfile Preset(string name)
    {
        Assert.True(SkillPresets.TryGet(name, out SkillProfile? p));
        return p!;
    }

    public static TheoryData<string> Presets => new() { "easy", "normal", "hard", "expert" };

    private static IEnumerable<ulong> Seeds(int n) => Enumerable.Range(1, n).Select(i => AiRandom.SeedFor(0x5EED_0001UL, i));

    [Fact]
    public void TwoMediumBotsWithDifferentSeedsHaveDifferentAllocations()
    {
        SkillProfile a = SkillSpread.Draw(Preset("normal"), Seeds(2).First()), b = SkillSpread.Draw(Preset("normal"), Seeds(2).Last());
        Assert.False(a.SameLevels(b), $"both drew {string.Join(",", a.Levels)}");
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryDrawSpendsTheExactTotalWithinTheCapAndOverTheFloor(string preset)
    {
        SkillProfile p = Preset(preset);
        foreach (ulong seed in Seeds(500))
        {
            SkillProfile d = SkillSpread.Draw(p, seed);
            Assert.Equal(p.Spent, d.Spent);
            Assert.All(d.Levels, l => Assert.InRange(l, SkillSpread.Floor, StandardSkills.Cap));
            Assert.True(StandardSkills.StandardPool().Judge(d).Accepted);
            Assert.Equal(preset, d.Name);
        }
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void TheSameSeedDrawsTheSameAllocation(string preset)
    {
        foreach (ulong seed in Seeds(20))
            Assert.True(SkillSpread.Draw(Preset(preset), seed).SameLevels(SkillSpread.Draw(Preset(preset), seed)));
    }

    /// <summary>Threshold: 28 of 32 distinct at every difficulty (measured at the commit: see §10).</summary>
    [Theory]
    [MemberData(nameof(Presets))]
    public void ThirtyTwoSeedsDrawAtLeastTwentyEightDistinctAllocations(string preset)
    {
        int distinct = Seeds(32).Select(s => string.Join(",", SkillSpread.Draw(Preset(preset), s).Levels)).Distinct().Count();
        Assert.True(distinct >= 28, $"{preset}: {distinct} distinct of 32");
    }

    /// <summary>Names read the most distinctive skill: over 32 seeds it is at least five different skills, and it stands out (at least 2 above the bot's even spread).</summary>
    [Theory]
    [MemberData(nameof(Presets))]
    public void TheMostDistinctiveSkillVariesAcrossSeeds(string preset)
    {
        var traits = new HashSet<int>();
        foreach (ulong seed in Seeds(32))
        {
            SkillProfile d = SkillSpread.Draw(Preset(preset), seed);
            traits.Add(BotNamer.Trait(d, seed).Skill);
            Assert.True(d.Levels.Max() >= d.Spent / (float)d.Levels.Length + 2f || d.Levels.Min() <= d.Spent / (float)d.Levels.Length - 2f,
                        $"{preset} {string.Join(",", d.Levels)} has no trait that stands out");
        }

        Assert.True(traits.Count >= 5, $"{preset}: only {string.Join(", ", traits)}");
    }

    [Fact]
    public void AnAllocationThatHoldsAZeroIsPaidUpToTheFloorAndKeepsItsTotal()
    {
        var custom = new SkillProfile("custom", StandardSkills.Ten, [10, 10, 10, 10, 0, 0, 0, 0, 0, 0]);
        foreach (ulong seed in Seeds(50))
        {
            SkillProfile d = SkillSpread.Draw(custom, seed);
            Assert.Equal(40, d.Spent);
            Assert.All(d.Levels, l => Assert.InRange(l, 1, StandardSkills.Cap));
        }
    }

    [Fact]
    public void ATotalTooSmallForTheFloorKeepsItsTotalAtFloorZero()
    {
        var thin = new SkillProfile("custom", StandardSkills.Ten, [3, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Equal(0, SkillSpread.FloorFor(3, 10));
        Assert.All(Seeds(20), s => Assert.Equal(3, SkillSpread.Draw(thin, s).Spent));
    }

    [Fact]
    public void TheMovesAreStatedByDifficulty()
        => Assert.Equal([4, 7, 10, 12], new[] { 20, 35, 50, 60 }.Select(SkillSpread.Moves));

    [Fact]
    public void AMediumBotsBestSkillAveragesBetweenSixAndEight()
    {
        // §10's number: the mean of the best skill over 500 seeds (the preset's own best is 4).
        double best = Seeds(500).Average(s => SkillSpread.Draw(Preset("normal"), s).Levels.Max());
        Assert.InRange(best, 6.0, 8.0);
    }
}
