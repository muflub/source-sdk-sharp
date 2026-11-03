using System.Globalization;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Skills;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// A bot's strongest trait shows at any difficulty (plan_hl2mp_ai §10, ruling
/// 2026-11-02: "Can fix bot strongest trait to show at lower difficulty"): style reads
/// how far a skill sits above the bot's own mean; competence reads the level itself, so
/// difficulty still means skill.
/// </summary>
public sealed class StyleTests
{
    private const int Draws = 500;

    private static readonly string[] CompetenceCurves = ["aimErrorDeg", "leadQuality", "trackingLagMs", "flickOvershootPct", "reactionMs", "glanceRate", "fireRangeGate", "pushTrust", "pushMarginSeconds", "strafeWeight", "strafeMeanSeconds"];

    private static IEnumerable<SkillProfile> Drawn(string preset)
    {
        for (int i = 0; i < Draws; i++)
            yield return SkillSpread.Draw(Trees.Duel.Preset(preset), AiRandom.SeedFor(0x5EED0001, i));
    }

    [Fact]
    public void TheStyleLevelIsTheHeightAboveTheBotsOwnMean()
    {
        Assert.Equal(SkillCurves.StyleMid, SkillCurves.StyleLevel(3, 3f, 2f));
        Assert.Equal(SkillCurves.StyleMid + SkillCurves.StyleGain, SkillCurves.StyleLevel(5, 3f, 2f));
        // A spread under 1 counts as 1: a nearly even bot has no favourite to speak of.
        Assert.Equal(SkillCurves.StyleMid + SkillCurves.StyleGain, SkillCurves.StyleLevel(5, 4f, 0.2f));
        Assert.Equal(0f, SkillCurves.StyleLevel(0, 9f, 1f));
        Assert.Equal(StandardSkills.Cap, SkillCurves.StyleLevel(10, 1f, 1f));
    }

    [Fact]
    public void ACurveBetweenItsLevelsIsInterpolated()
    {
        SkillCurves.Curve c = Curves.Game()["coverWeight"];
        Assert.Equal(0.25f, c.At(2.5f), 3);
        Assert.Equal(c.Levels[^1], c.At(99f));
    }

    /// <summary>Only "level" and "style" are ways to read a curve.</summary>
    [Fact]
    public void AnUnknownReadingIsRefusedByName()
    {
        string json = Curves.Game().Json.Replace("\"reads\": \"style\"", "\"reads\": \"sideways\"", StringComparison.Ordinal);
        Assert.NotEqual(Curves.Game().Json, json);
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => SkillCurves.Parse(json));
        Assert.Contains("sideways", e.Message, StringComparison.Ordinal);
    }

    /// <summary>The ruling's line between the two: these read their level, whatever the rest of the bot is.</summary>
    [Fact]
    public void CompetenceReadsItsLevel()
    {
        SkillCurves curves = Curves.Game();
        foreach (string name in CompetenceCurves)
            Assert.False(curves[name].Style, $"{name} would read a height, not a level");
        foreach (SkillCurves.Curve c in curves.All)
        {
            if (c.Skill is "accuracy" or "alertness" or "agility")
                Assert.False(c.Style, $"{c.Parameter}: {c.Skill} is competence");
        }
    }

    /// <summary>The Medium calibration stays where it is: aim at the Medium preset is the curve's own value at its level, whatever else the bot's points are spent on.</summary>
    [Fact]
    public void AimDoesNotMoveWithTheRestOfTheBot()
    {
        SkillCurves curves = Curves.Game();
        SkillProfile medium = Trees.Duel.Preset("normal");
        DecisionParams p = curves.For(medium);
        Assert.Equal(curves["aimErrorDeg"].At(4), p.AimErrorDeg);
        Assert.Equal(curves["reactionMs"].At(4), p.ReactionMs);
        Assert.Equal(curves["trackingLagMs"].At(4), p.TrackingLagMs);
        SkillProfile lopsided = medium.With(medium.IndexOf("cover"), 10);
        Assert.Equal(p.AimErrorDeg, curves.For(lopsided).AimErrorDeg);
        Assert.Equal(p.StrafeWeight, curves.For(lopsided).StrafeWeight);
        Assert.Equal(p.FireRangeGate, curves.For(lopsided).FireRangeGate);
    }

    /// <summary>The same shape at two difficulties has the same style and different aim: every level two higher moves competence and not the style.</summary>
    [Fact]
    public void AShapeHasOneStyleAtAnyDifficulty()
    {
        SkillCurves curves = Curves.Game();
        int[] low = [2, 2, 0, 5, 1, 5, 2, 1, 1, 1];
        int[] high = [.. low.Select(l => l + 2)];
        DecisionParams a = curves.For(new SkillProfile("a", StandardSkills.Ten, [.. low]));
        DecisionParams b = curves.For(new SkillProfile("b", StandardSkills.Ten, [.. high]));
        Assert.Equal(a.CoverWeight, b.CoverWeight);
        Assert.Equal(a.AmbushWeight, b.AmbushWeight);
        Assert.Equal(a.ChaseSeconds, b.ChaseSeconds);
        Assert.Equal(a.RetreatHealthPct, b.RetreatHealthPct);
        Assert.True(b.AimErrorDeg < a.AimErrorDeg, "difficulty still means aim");
    }

    /// <summary>A bot named for camping camps, at every difficulty, and it is not simply what everyone does.</summary>
    [Theory]
    [InlineData("easy")]
    [InlineData("normal")]
    [InlineData("hard")]
    [InlineData("expert")]
    public void ACamperNamedBotCampsAtEveryDifficulty(string preset)
    {
        StyleShares s = Hl2mpStyleSurvey.Of(Curves.Game(), Trees.Duel.Preset(preset));
        string all = string.Create(CultureInfo.InvariantCulture, $"{preset}: campers {s.Campers} hold {s.CampersHold:P0}, over a hunt {s.CampersOverHunt:P0}; rest hold {s.RestHold:P0}, over a hunt {s.RestOverHunt:P0}");
        Assert.True(s.Campers >= 50, all);
        Assert.True(s.CampersHold >= 0.9, all);
        Assert.True(s.CampersOverHunt >= 0.6, all);
        Assert.True(s.RestOverHunt <= 0.35, all);
        Assert.True(s.RestHold <= 0.6, all);
    }

    /// <summary>What each skill's name promises, read from its curves: among one difficulty's bots, those named by a skill stand apart from the rest in that direction, high and low.</summary>
    [Theory]
    [InlineData("easy")]
    [InlineData("normal")]
    [InlineData("hard")]
    [InlineData("expert")]
    public void ANamesSkillIsTheOneThatShows(string preset)
    {
        SkillCurves curves = Curves.Game();
        // The curve each name's phrases are about: "Stands In Open" is coverWeight low, "Runs Away" giveUpHealth high.
        (string Skill, Func<DecisionParams, float> Of)[] promises =
        [
            ("accuracy", p => -p.AimErrorDeg),
            ("alertness", p => p.GlanceRate),
            ("aggression", p => p.ChaseSeconds),
            ("cover", p => p.CoverWeight),
            ("tenacity", p => p.SearchSeconds),
            ("cunning", p => p.RouteAltWeight),
            ("discipline", p => -p.FireRangeGate),
            ("agility", p => p.StrafeWeight),
            ("nerve", p => -p.RetreatHealthPct),
            ("headhunter", p => p.RankBonus),
        ];
        (DecisionParams P, BotTrait T)[] bots = [.. Drawn(preset).Select((p, i) => (curves.For(p), BotNamer.Trait(p, (ulong)i)))];
        foreach ((string skill, Func<DecisionParams, float> of) in promises)
        {
            int index = StandardSkills.Ten.Select((s, i) => (s.Key, i)).First(x => x.Key == skill).i;
            float everyone = bots.Average(b => of(b.P));
            float[] high = [.. bots.Where(b => b.T.Skill == index && b.T.High).Select(b => of(b.P))];
            float[] low = [.. bots.Where(b => b.T.Skill == index && !b.T.High).Select(b => of(b.P))];
            if (high.Length >= 5)
                Assert.True(high.Average() > everyone, $"{preset}: {high.Length} named high by {skill} read {high.Average()}, everyone {everyone}");
            if (low.Length >= 5)
                Assert.True(low.Average() < everyone, $"{preset}: {low.Length} named low by {skill} read {low.Average()}, everyone {everyone}");
        }
    }

    /// <summary>"Runs Away", in the tree: hurt with someone on it, a quitter is out of the fight where the same bot at its own mean stays.</summary>
    [Fact]
    public void AQuitterLeavesAFightItsNerveWouldStayIn()
    {
        SkillProfile basis = Trees.Duel.Preset("normal");
        basis = basis.With(basis.IndexOf("nerve"), 10);
        string Branch(SkillProfile p)
        {
            var d = new Trees.Duel(p);
            var at = new AiVector(300f, 0f, 0f);
            d.Step(d.Self(health: 35), [(Trees.Duel.Opponent, at)], d.SeeAt(Trees.Duel.Opponent, at));
            d.Step(d.Self(health: 35), [(Trees.Duel.Opponent, at)]);
            return d.Decider.Doing.Branch;
        }

        Assert.Equal("Fight", Branch(basis.With(basis.IndexOf("tenacity"), 5)));
        Assert.Equal("Survive", Branch(basis.With(basis.IndexOf("tenacity"), 0)));
    }

    /// <summary>"Camps Alot" is a low Aggression: with nothing else to go on it holds a place, and the same bot at its own mean does not.</summary>
    [Fact]
    public void CampsAlotCamps()
    {
        SkillCurves curves = Curves.Game();
        SkillProfile basis = Trees.Duel.Preset("normal");
        DecisionParams low = curves.For(basis.With(basis.IndexOf("aggression"), 0));
        DecisionParams even = curves.For(basis);
        Assert.True(Hl2mpStyle.Camp(in low) >= Hl2mpStyle.CampFrom, $"{Hl2mpStyle.Camp(in low)}");
        Assert.True(Hl2mpStyle.Camp(in even) < Hl2mpStyle.CampFrom, $"{Hl2mpStyle.Camp(in even)}");
    }

    /// <summary>"Runs Away": a bot well under its own mean in Tenacity leaves a fight sooner than its nerve alone says; at its mean it does not.</summary>
    [Fact]
    public void ALowTenacityGivesUp()
    {
        SkillCurves curves = Curves.Game();
        SkillProfile medium = Trees.Duel.Preset("normal");
        DecisionParams quitter = curves.For(medium.With(medium.IndexOf("tenacity"), 0));
        DecisionParams even = curves.For(medium);
        Assert.True(quitter.GiveUpHealth > quitter.RetreatHealthPct, $"{quitter.GiveUpHealth}, {quitter.RetreatHealthPct}");
        Assert.True(even.GiveUpHealth < even.RetreatHealthPct, $"{even.GiveUpHealth}, {even.RetreatHealthPct}");
    }
}
