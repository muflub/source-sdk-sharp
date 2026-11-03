using System.Collections.Immutable;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

/// <summary>
/// Bot names (plan_hl2mp_ai §3.5.1, user rulings 2026-11-01): the most
/// distinctive trait, high or low, picks the pool; ties go by skill order
/// then the seed; the seed picks the entry; names are unique and fit the
/// engine's limit; the committed data file holds every skill and every entry
/// fits beside the shortest first name.
/// </summary>
public sealed class BotNamerTests
{
    private static readonly Lazy<BotNamePools> Pools = new(() => BotNamePools.Parse(File.ReadAllText(
        RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_bot_names.json"))));

    private static SkillProfile Profile(params int[] levels) => new("custom", StandardSkills.Ten, [.. levels]);

    private static string DescriptorOf(string name) => name[(name.IndexOf(' ', StringComparison.Ordinal) + 1)..];

    [Fact]
    public void AHighTraitDrawsFromItsHighPool()
    {
        // Accuracy 10 against 2 each elsewhere: far above the even spread.
        SkillProfile p = Profile(10, 2, 2, 2, 2, 2, 2, 2, 2, 2);
        Assert.Equal(new BotTrait(0, true), BotNamer.Trait(p, 1));
        for (ulong seed = 1; seed < 20; seed++)
            Assert.Contains(DescriptorOf(BotNamer.Name(Pools.Value, p, seed, new HashSet<string>())), Pools.Value.Traits["accuracy"].High);
    }

    [Fact]
    public void ALowTraitDrawsFromItsLowPool()
    {
        // An easy bot with nothing in aggression: "Tommy Camps Alot".
        SkillProfile p = Profile(3, 3, 0, 3, 3, 3, 3, 3, 3, 3);
        Assert.Equal(new BotTrait(2, false), BotNamer.Trait(p, 1));
        for (ulong seed = 1; seed < 20; seed++)
            Assert.Contains(DescriptorOf(BotNamer.Name(Pools.Value, p, seed, new HashSet<string>())), Pools.Value.Traits["aggression"].Low);
    }

    [Fact]
    public void ATieGoesBySkillOrderThenTheSeed()
    {
        // Accuracy and cover equally far above, the rest equal: two candidates.
        SkillProfile p = Profile(6, 2, 2, 6, 2, 2, 2, 2, 2, 2);
        Assert.Equal(new BotTrait(0, true), BotNamer.Trait(p, 0));
        Assert.Equal(new BotTrait(3, true), BotNamer.Trait(p, 1));
        Assert.Equal(BotNamer.Trait(p, 41), BotNamer.Trait(p, 41));
    }

    [Fact]
    public void TheSameSeedGivesTheSameName()
    {
        SkillProfile p = Profile(4, 4, 4, 3, 3, 3, 4, 3, 4, 3);
        Assert.Equal(BotNamer.Name(Pools.Value, p, 77, new HashSet<string>()), BotNamer.Name(Pools.Value, p, 77, new HashSet<string>()));
        Assert.NotEqual(BotNamer.Name(Pools.Value, p, 77, new HashSet<string>()), BotNamer.Name(Pools.Value, p, 78, new HashSet<string>()));
    }

    [Fact]
    public void ThirtyTwoBotsOfOneProfileHaveThirtyTwoNames()
    {
        SkillProfile p = Profile(6, 6, 5, 5, 5, 5, 6, 4, 5, 3);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int bot = 1; bot <= 32; bot++)
            Assert.True(taken.Add(BotNamer.Name(Pools.Value, p, AiRandom.SeedFor(9, bot), taken)));
        Assert.Equal(32, taken.Count);
    }

    [Fact]
    public void ThirtyTwoBotsHaveThirtyTwoFirstNames()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firsts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (ulong seed = 1; seed <= 32; seed++)
        {
            string name = BotNamer.Name(Pools.Value, Profile(9, 1, 1, 1, 1, 1, 1, 1, 1, 1), seed, taken);
            Assert.True(taken.Add(name));
            Assert.True(firsts.Add(Pools.Value.First.First(f => name.StartsWith(f + " ", StringComparison.Ordinal))), $"{name} has a first name in use");
        }
    }

    /// <summary>Past the pool the first names repeat, and the whole names still do not.</summary>
    [Fact]
    public void PastThePoolWholeNamesStayUnique()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (ulong seed = 1; seed <= (ulong)Pools.Value.First.Length + 10; seed++)
            Assert.True(taken.Add(BotNamer.Name(Pools.Value, Profile(9, 1, 1, 1, 1, 1, 1, 1, 1, 1), seed, taken)));
    }

    /// <summary>A name taken gets a numeral of ours; one free is left alone.</summary>
    [Fact]
    public void ATakenNameGetsANumeral()
    {
        var taken = new HashSet<string>(["Alyx", "Alyx 2"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("alyx 3", BotNamer.Distinct("alyx", taken));
        Assert.Equal("Barney", BotNamer.Distinct("Barney", taken));
    }

    [Fact]
    public void AStuffedPoolFallsBackToANumeral()
    {
        var pools = new BotNamePools(["Kai"], ImmutableDictionary<string, TraitPools>.Empty.Add("accuracy", new(["Sniper"], ["Wild"])));
        SkillProfile p = Profile(10, 2, 2, 2, 2, 2, 2, 2, 2, 2);
        var taken = new HashSet<string> { "Kai Sniper" };
        Assert.Equal("Kai Sniper 2", BotNamer.Name(pools, p, 5, taken));
    }

    [Fact]
    public void ANameFitsTheEnginesLimitCuttingTheFirstNameFirst()
    {
        string name = BotNamer.Fit("Bartholomew-Maximilian", "Never Reloads Early Ever");
        Assert.True(name.Length <= BotNamer.MaxLength, name);
        Assert.EndsWith(" Never Reloads Early Ever", name, StringComparison.Ordinal);
        Assert.StartsWith("Bart", name, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryEntryOfTheDataFileFitsBesideTheShortestFirstName()
    {
        BotNamePools pools = Pools.Value;
        int shortest = pools.First.Min(f => f.Length);
        Assert.Equal(StandardSkills.Ten.Select(s => s.Key).Order(), pools.Traits.Keys.Order());
        foreach ((string skill, TraitPools t) in pools.Traits)
        {
            foreach (ImmutableArray<string> pool in (ImmutableArray<string>[])[t.High, t.Low])
            {
                Assert.InRange(pool.Length, 6, 10);
                Assert.All(pool, e => Assert.True(shortest + 1 + e.Length <= BotNamer.MaxLength, $"{skill}: '{e}' does not fit"));
            }
        }
    }

    /// <summary>The words of the scoreboard's skill tags (§3.5.1): a name beside "(Hard)" must not say "Easy".</summary>
    private static readonly string[] TagWords = ["easy", "medium", "normal", "hard", "expert", "custom", "bot"];

    // A live bot came out "Pia Easy Prey (Hard)" (2026-11-01): a phrase with a
    // tag word reads as a second, contradicting skill label.
    [Fact]
    public void NoFirstNameOrPhraseOfTheDataFileContainsASkillTagWord()
    {
        BotNamePools pools = Pools.Value;
        IEnumerable<(string Where, string Entry)> entries = pools.First.Select(f => ("first", f))
            .Concat(pools.Traits.SelectMany(t => t.Value.High.Select(e => (t.Key + " high", e)).Concat(t.Value.Low.Select(e => (t.Key + " low", e)))));
        var bad = new List<string>();
        foreach ((string where, string entry) in entries)
        {
            foreach (string word in TagWords)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(entry, $@"\b{word}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    bad.Add($"{where}: '{entry}' says '{word}'");
            }
        }

        Assert.True(pools.First.Length > 0 && pools.Traits.Count == 10, "the data file was read");
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }
}
