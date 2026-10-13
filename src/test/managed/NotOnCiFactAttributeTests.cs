using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The CI skip (src/test/common/NotOnCiFactAttribute.cs): it skips only on
/// CI's exact setting, says why, and runs everywhere else.
/// </summary>
public sealed class NotOnCiFactAttributeTests
{
    [Fact]
    public void Under_ci_a_fact_skips_naming_its_reason()
    {
        var fact = new NotOnCiFactAttribute("a golden recorded on the owner's CPU", "1");
        Assert.Equal("not on CI: a golden recorded on the owner's CPU", fact.Skip);
    }

    [Fact]
    public void Under_ci_a_theory_skips_naming_its_reason()
    {
        var theory = new NotOnCiTheoryAttribute("needs a Steam install", "1");
        Assert.Equal("not on CI: needs a Steam install", theory.Skip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData(" 1")]
    public void Anywhere_else_the_fact_and_the_theory_run(string? value)
    {
        Assert.Null(new NotOnCiFactAttribute("r", value).Skip);
        Assert.Null(new NotOnCiTheoryAttribute("r", value).Skip);
    }

    [Fact]
    public void The_reason_is_kept_whether_or_not_it_skips()
    {
        Assert.Equal("why", new NotOnCiFactAttribute("why", null).Reason);
        Assert.Equal("why", new NotOnCiTheoryAttribute("why", "1").Reason);
    }

    [Fact]
    public void The_variable_is_the_one_the_workflow_sets()
    {
        Assert.Equal("SOURCESHARP_CI", NotOnCi.Variable);
        string workflow = File.ReadAllText(RepoLayout.Repo(".github", "workflows", "ci.yml"));
        Assert.Contains("SOURCESHARP_CI: '1'", workflow);
    }
}
