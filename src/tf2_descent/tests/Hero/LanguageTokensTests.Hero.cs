using TF2.Descent.Server.Mode;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>The hero's own lines: the refused class and the level-up.</summary>
public sealed partial class LanguageTokensTests
{
    [Fact]
    public void The_refused_class_line_is_in_the_language_file_and_reads_whole_without_a_class()
    {
        var tokens = Tokens();

        // joinclass random refused names no class (an empty %s1 read "class:  is not yours." live).
        Assert.True(tokens.TryGetValue(HeroClassGate.Reason, out string? text), HeroClassGate.Reason + " is missing");
        Assert.DoesNotContain("%s1", text);
    }

    [Fact]
    public void The_level_up_line_is_in_the_language_file_and_names_the_level()
    {
        var tokens = Tokens();

        Assert.True(tokens.TryGetValue(TF2.Descent.Server.Hero.HeroExperience.LevelUpToken, out string? text), "the level-up line is missing");
        Assert.Contains("%s1", text);
    }
}
