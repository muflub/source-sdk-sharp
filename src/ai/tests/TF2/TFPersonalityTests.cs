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

    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    /// <summary>Eight bots of varied skills: two aggressive, two tenacious, two timid, a disciplined one and a team player, attackers all.</summary>
    private static readonly (string Skill, int Level, int Slot, ulong Seed)[] Eight =
    [
        ("aggression", 10, 1, 11), ("aggression", 9, 2, 12), ("tenacity", 10, 4, 13), ("tenacity", 9, 5, 14),
        ("nerve", 0, 7, 15), ("nerve", 1, 8, 16), ("discipline", 10, 10, 17), ("teamwork", 10, 11, 18),
    ];

    /// <summary>
    /// One view for all: at mid field on half its health, an enemy 400 u off who is in sight, its own flag
    /// carried 300 u away and the enemy's at home. The tree scores chasing the carrier 0.85, leaving for
    /// health 0.82 (if the bot wants it), the fight 0.8 and the fetch 0.78: the branch each bot takes.
    /// </summary>
    private static string[] Branches(Func<DecisionParams, DecisionParams>? tune)
        => [.. Eight.Select(b =>
        {
            var f = new TFField(slot: b.Slot, seed: b.Seed, profile: TFSkillCurvesTests.Profile(b.Skill, b.Level), curves: TFSkillCurvesTests.ShippedCurves(), tune: tune);
            f.Step(TFField.Self(Mid, health: 100), TFField.Ctf(red: ObjectiveState.Carried, redAt: Mid + new AiVector(300f, 0f, 0f), redCarrier: Enemy.Slot),
                   (Enemy, Mid + new AiVector(400f, 0f, 0f), AiVector.Zero, TFField.Blue));
            return f.Branch;
        })];

    /// <summary>
    /// Eight bots of varied skills given one view split across three branches at least (G.2.2's fact), by
    /// their traits alone: the noise is off in both arms, so it is not luck that parts them. The control is
    /// the same eight with their traits ignored, as every bot was before: one branch.
    /// </summary>
    [Fact]
    public void EightBotsOfVariedSkillsGivenOneViewSplitAcrossThreeBranches()
    {
        string[] alike = Branches(p => p with { FightDrive = 1f, ChaseDrive = 1f, HoldDrive = 1f, EscortDrive = 1f, SurviveDrive = 1f, ScoreNoise = 0f });
        string[] own = Branches(p => p with { ScoreNoise = 0f });
        Assert.Equal([TFDecider.ChaseCarrier], alike.Distinct());
        Assert.True(own.Distinct().Count() >= 3, $"the eight took {string.Join(", ", own)}");
    }

    /// <summary>Each of the eight goes where its trait leans: the aggressive to the fight or the flag, the tenacious after the carrier, the timid for health.</summary>
    [Fact]
    public void EachBotGoesWhereItsTraitLeans()
    {
        string[] own = Branches(null);
        Assert.All(own[..2], b => Assert.Contains(b, new[] { TFDecider.Fight, TFDecider.Fetch }));
        Assert.All(own[2..4], b => Assert.Equal(TFDecider.ChaseCarrier, b));
        Assert.All(own[4..6], b => Assert.Equal(TFDecider.Survive, b));
    }

    /// <summary>Two bots of the same skills and seed, shown the same two hundred views, take the same branch at every one.</summary>
    [Fact]
    public void TwoBotsOfTheSameSkillsAndSeedDecideAlike()
    {
        string[] Play()
        {
            var f = new TFField(seed: 21, profile: TFSkillCurvesTests.Profile("aggression", 7), curves: TFSkillCurvesTests.ShippedCurves());
            var took = new string[200];
            for (int i = 0; i < took.Length; i++)
            {
                // The enemy's flag at home, then in a teammate's hands: something to fetch, then someone to go with.
                ObjectiveKnowledge hud = i % 50 < 25 ? TFField.Ctf() : TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid + new AiVector(-600f, 0f, 0f), blueCarrier: 4);
                if (i % 40 < 20)
                    f.Step(TFField.Self(Mid, health: 100), hud, (Enemy, Mid + new AiVector(400f + i, 0f, 0f), AiVector.Zero, TFField.Blue));
                else
                    f.Step(TFField.Self(Mid, health: 100), hud);
                took[i] = f.Branch;
            }

            return took;
        }

        string[] first = Play();
        Assert.Equal(first, Play());
        Assert.True(first.Distinct().Count() >= 2, "the control: the views move the bot between branches");
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
