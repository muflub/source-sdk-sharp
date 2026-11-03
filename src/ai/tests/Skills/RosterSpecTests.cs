using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

/// <summary>The roster spec the create-server screen writes and the server reads (plan_hl2mp_ai §3.5).</summary>
public class RosterSpecTests
{
    [Fact]
    public void PresetsAndANamedCustomBotFormatAsThePlanSays()
        => Assert.Equal("easy;easy;name=Deadeye:skills=8,7,6,6,6,6,7,5,5,4",
                        RosterSpec.Format([new(null, "easy"), new(null, "easy"), new("Deadeye", "skills=8,7,6,6,6,6,7,5,5,4")]));

    [Fact]
    public void AFormattedSpecParsesBackToItsEntries()
    {
        RosterEntry[] entries = [new(null, "hard"), new("Deadeye", "skills=8,7,6,6,6,6,7,5,5,4"), new(null, "profile=Sniper")];
        var refusals = new List<string>();
        Assert.Equal(entries, RosterSpec.Parse(RosterSpec.Format(entries), refusals));
        Assert.Empty(refusals);
    }

    [Fact]
    public void ANameMayComeAfterTheSkills()
        => Assert.Equal([new RosterEntry("Bob", "easy")], RosterSpec.Parse("easy:name=Bob", new List<string>()));

    [Fact]
    public void EmptyEntriesAreSkipped()
        => Assert.Equal([new RosterEntry(null, "easy")], RosterSpec.Parse(" ;easy;; ", new List<string>()));

    [Fact]
    public void AnEntryWithoutSkillsIsRefusedByPosition()
    {
        var refusals = new List<string>();
        Assert.Equal([new RosterEntry(null, "hard")], RosterSpec.Parse("hard;name=Bob", refusals));
        Assert.Equal(["roster entry 2 'name=Bob': no skills (a preset, skills=a,b,... or profile=<name>)"], refusals);
    }

    [Fact]
    public void ABadNameIsRefused()
    {
        var refusals = new List<string>();
        Assert.Empty(RosterSpec.Parse("name=a/b:easy", refusals));
        Assert.Equal(["roster entry 1 'name=a/b:easy': 'a/b' has '/': a profile's name is letters, digits, _ and -"], refusals);
    }

    [Fact]
    public void TwoSkillsFieldsAreRefused()
    {
        var refusals = new List<string>();
        Assert.Empty(RosterSpec.Parse("easy:hard", refusals));
        Assert.Equal(["roster entry 1 'easy:hard': two skills fields ('easy' and 'hard')"], refusals);
    }

    [Fact]
    public void FormatRefusesANameThatCouldBreakTheConsoleLine()
        => Assert.Throws<ArgumentException>(() => RosterSpec.Format([new("a\";quit", "easy")]));
}
