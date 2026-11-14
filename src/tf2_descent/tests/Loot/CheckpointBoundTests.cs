using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Sheets;
using Xunit;

namespace TF2.Descent.Tests.Loot;

/// <summary>
/// The bound RU13 left open (plan_descent_logic RU4): a checkpoint's evidence may claim no more
/// XP than its kills could have paid, each kill at most <c>MostXpPerKill</c> of its template.
/// </summary>
public sealed class CheckpointBoundTests
{
    private static readonly TF2DescentRules Rules = new();
    private static HeroRules Heroes => Rules.Heroes;

    private static CheckpointEvidence Earned(long xp, params (string Template, int Kills)[] kills) =>
        Rules.Evidence.Write(new CheckpointProof(xp, kills.ToImmutableSortedDictionary(k => k.Template, k => k.Kills, StringComparer.Ordinal)));

    private static ValidationResult Check(HeroSheet before, HeroSheet after, CheckpointEvidence evidence) =>
        Rules.ValidateCheckpoint(Rules.SheetCodec.Write(before), Rules.SheetCodec.Write(after), evidence);

    private static (HeroSheet Before, HeroSheet After) Gain(long xp)
    {
        var hero = Heroes.New("soldier", "s");
        return (hero, Heroes.GainXp(hero, xp).Sheet);
    }

    [Fact]
    public void Experience_beyond_what_the_kills_could_pay_is_refused()
    {
        long most = 3 * Rules.Experience.MostXpPerKill("scout");
        var (before, after) = Gain(most + 1);

        Assert.Equal("xp_beyond_kills", Check(before, after, Earned(most + 1, ("scout", 3))).FailedRule);
    }

    [Fact]
    public void Experience_within_what_the_kills_could_pay_passes()
    {
        long most = 3 * Rules.Experience.MostXpPerKill("scout");
        var (before, after) = Gain(most);

        Assert.True(Check(before, after, Earned(most, ("scout", 3))).Ok);
    }

    [Fact]
    public void Experience_with_no_kills_is_refused()
    {
        var (before, after) = Gain(50);

        Assert.Equal("xp_beyond_kills", Check(before, after, Earned(50)).FailedRule);
    }

    [Fact]
    public void Kills_of_names_that_are_no_template_pay_nothing()
    {
        var (before, after) = Gain(50);

        Assert.Equal("xp_beyond_kills", Check(before, after, Earned(50, ("soldier_bot", 1000))).FailedRule);
    }

    [Fact]
    public void The_bound_adds_each_templates_kills()
    {
        var proof = new CheckpointProof(0, ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            [new KeyValuePair<string, int>("pyro", 2), new KeyValuePair<string, int>("heavy", 5)]));

        Assert.Equal(2 * Rules.Experience.MostXpPerKill("pyro") + 5 * Rules.Experience.MostXpPerKill("heavy"), Rules.Checkpoints.MostXpFor(proof));
    }

    /// <summary>A real descent's evidence passes: the XP 200 rolled packs of depth 4 pay a level 1 hero, claimed with their kills.</summary>
    [Fact]
    public void The_xp_rolled_kills_pay_passes_with_those_kills()
    {
        var kills = new Dictionary<string, int>(StringComparer.Ordinal);
        long xp = 0;
        for (int i = 0; i < 200; i++)
            foreach (var r in Rules.Packs.RollPack((ulong)i, 1, 4, 1, 3).Robots)
            {
                xp += Rules.Experience.KillXp(r.Stats, 1);
                kills[r.Template] = kills.GetValueOrDefault(r.Template) + 1;
            }
        var (before, after) = Gain(xp);

        Assert.True(Check(before, after, Earned(xp, [.. kills.Select(k => (k.Key, k.Value))])).Ok);
    }
}
