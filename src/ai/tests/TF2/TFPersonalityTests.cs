using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Personality in a TF2 bot's scores (plan_bots_teams_tf2_part_g G.2.2, <see cref="TFDecider.Weigh"/>):
/// each trait leans the branches it is said to, by the tf2 mod's own curves; two bots of the same
/// skills and seed weigh alike and another seed's noise is its own; delivering the flag is never
/// weighed and nothing is weighed over it.
/// </summary>
public sealed class TFPersonalityTests
{
    private static TFField Bot(string skill, int level, ulong seed = 7, bool noise = false)
        => new(seed: seed, profile: TFSkillCurvesTests.Profile(skill, level), curves: TFSkillCurvesTests.ShippedCurves(),
               tune: noise ? null : p => p with { ScoreNoise = 0f });

    /// <summary>The score a bot makes of <paramref name="branch"/> scored 0.5 by the tree, some decides in.</summary>
    private static float Weighed(TFField f, string branch, int decides = 1)
    {
        for (int i = 0; i < decides; i++)
            f.Step(TFField.Self(new AiVector(2048f, 256f, 0f)), TFField.Ctf());
        return f.Decider.Weigh(branch, 0.5f);
    }

    [Theory]
    [InlineData("aggression", TFDecider.Fight)]
    [InlineData("aggression", TFDecider.Fetch)]
    [InlineData("tenacity", TFDecider.ChaseCarrier)]
    [InlineData("discipline", TFDecider.Defend)]
    [InlineData("discipline", TFDecider.Guard)]
    [InlineData("teamwork", TFDecider.Escort)]
    public void ATraitRaisesTheScoreOfItsBranch(string skill, string branch)
    {
        (float low, float high) = (Weighed(Bot(skill, 0), branch), Weighed(Bot(skill, 10), branch));
        Assert.True(low < 0.5f && high > 0.5f, $"{branch} scored 0.5: {low} at {skill} 0, {high} at 10");
    }

    [Fact]
    public void NerveLowersTheScoreOfLeavingForHealth()
    {
        (float timid, float bold) = (Weighed(Bot("nerve", 0), TFDecider.Survive), Weighed(Bot("nerve", 10), TFDecider.Survive));
        Assert.True(timid > 0.5f && bold < 0.5f, $"Survive scored 0.5: {timid} at Nerve 0, {bold} at 10");
    }

    /// <summary>A trait leans its own branches only: Aggression moves neither the chase nor the post.</summary>
    [Theory]
    [InlineData(TFDecider.ChaseCarrier)]
    [InlineData(TFDecider.Defend)]
    [InlineData(TFDecider.Escort)]
    [InlineData(TFDecider.Survive)]
    [InlineData(TFDecider.Search)]
    [InlineData(TFDecider.Roam)]
    public void AggressionMovesNoOtherBranch(string branch)
    {
        // Aggression's own level moves the style of the others a little (a style is read against the bot's mean), never by a fifth of its own lean.
        (float low, float high) = (Weighed(Bot("aggression", 0), branch), Weighed(Bot("aggression", 10), branch));
        Assert.InRange(MathF.Abs(high - low), 0f, 0.05f);
    }

    [Fact]
    public void DeliveringTheFlagIsNotWeighed()
    {
        TFField f = Bot("aggression", 0, noise: true);
        f.Step(TFField.Self(new AiVector(2048f, 256f, 0f)), TFField.Ctf());
        Assert.Equal(1f, f.Decider.Weigh(TFDecider.Deliver, 1f));
    }

    [Fact]
    public void NothingIsWeighedOverDeliveringTheFlag()
    {
        TFField f = Bot("aggression", 10, noise: true);
        f.Step(TFField.Self(new AiVector(2048f, 256f, 0f)), TFField.Ctf());
        Assert.Equal(TFDecider.ScoreCeiling, f.Decider.Weigh(TFDecider.Fight, 0.95f));
    }

    [Fact]
    public void AScoreOfNothingStaysNothing() => Assert.Equal(0f, Bot("aggression", 10, noise: true).Decider.Weigh(TFDecider.Fight, 0f));

    /// <summary>Curves without the personality parameters (the fixture's default, HL2MP's): a score is exactly the tree's.</summary>
    [Fact]
    public void WithoutThePersonalityCurvesAScoreIsTheTrees()
    {
        var f = new TFField(profile: TFSkillCurvesTests.Profile("aggression", 10));
        Assert.Equal(0.5f, Weighed(f, TFDecider.Fight));
    }

    [Fact]
    public void TwoBotsOfTheSameSkillsAndSeedWeighAlike()
    {
        TFField a = Bot("aggression", 6, seed: 21, noise: true), b = Bot("aggression", 6, seed: 21, noise: true);
        for (int i = 0; i < 40; i++)
            Assert.Equal(Weighed(a, TFDecider.Fight), Weighed(b, TFDecider.Fight));
    }

    [Fact]
    public void AnotherSeedsNoiseIsItsOwn()
    {
        TFField a = Bot("aggression", 6, seed: 21, noise: true), b = Bot("aggression", 6, seed: 22, noise: true);
        int apart = 0;
        for (int i = 0; i < 40; i++)
            apart += MathF.Abs(Weighed(a, TFDecider.Fight, 25) - Weighed(b, TFDecider.Fight, 25)) > 0.002f ? 1 : 0;
        Assert.True(apart > 20, $"two seeds' scores of one branch were apart at {apart} of 40 moments");
    }
}
