using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// TF2's skill curves (plan_bots_teams_tf2 C.5, P6 step 6): the tf2 mod ships its own file, which
/// parses and gives every preset its decision parameters, so a TF2 bot's brain has a decider; a
/// missing file is refused with its path.
/// </summary>
public sealed class TFSkillCurvesTests
{
    public class Files : DispatchProxy
    {
        public string? Text;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IEngineFiles.ReadGameFile) && (string)args![0]! == TFSkillCurves.Path ? Text : null;
    }

    private static TFSkillCurves Source(string? text)
    {
        IEngineFiles files = DispatchProxy.Create<IEngineFiles, Files>();
        ((Files)(object)files).Text = text;
        return new TFSkillCurves(files);
    }

    /// <summary>The tf2 mod's file at the path the source reads.</summary>
    private static string Shipped => File.ReadAllText(SourceSharp.Tests.RepoLayout.Repo(["game", "mod_tf2", .. TFSkillCurves.Path.Split('/')]));

    [Theory]
    [InlineData("easy")]
    [InlineData("normal")]
    [InlineData("hard")]
    [InlineData("expert")]
    public void TheShippedCurvesGiveEveryPresetItsDecisions(string preset)
    {
        SkillCurves curves = Source(Shipped).Curves ?? throw new Xunit.Sdk.XunitException("no curves");
        Assert.True(SkillPresets.TryGet(preset, out SkillProfile? p));
        DecisionParams d = curves.For(p!);
        Assert.True(d.AimErrorDeg > 0f && d.ReactionMs > 0f, $"{preset}: aim error {d.AimErrorDeg}, reaction {d.ReactionMs}");
    }

    /// <summary>The curves the tf2 mod ships, for the facts that read TF2's own numbers.</summary>
    internal static SkillCurves ShippedCurves() => SkillCurves.Parse(Shipped);

    /// <summary>A profile of the eleven at <paramref name="all"/> but for <paramref name="skill"/> at <paramref name="level"/>.</summary>
    internal static SkillProfile Profile(string skill, int level, int all = 4)
    {
        SkillProfile flat = new("fact", StandardSkills.Eleven, [.. Enumerable.Repeat(all, StandardSkills.Eleven.Length)]);
        return skill.Length == 0 ? flat : flat.With(flat.IndexOf(skill), level);
    }

    [Fact]
    public void TheShippedCurvesCarryThePersonalityParameters()
    {
        string[] have = [.. ShippedCurves().All.Select(c => c.Parameter)];
        Assert.Empty(SkillCurves.Drives.Except(have));
    }

    [Fact]
    public void TheShippedCurvesCarryTheReactionParameters()
    {
        string[] have = [.. ShippedCurves().All.Select(c => c.Parameter)];
        Assert.Empty(SkillCurves.Reactions.Except(have));
    }

    /// <summary>Agility is what gets a bot out of a blast's way (plan_bots_teams_tf2_part_g G.2.3): a clumsy bot seldom and late, a nimble one nearly always and at once.</summary>
    [Fact]
    public void AgilityMakesTheDodgeLikelierAndSooner()
    {
        SkillCurves curves = ShippedCurves();
        DecisionParams clumsy = curves.For(Profile("agility", 0)), nimble = curves.For(Profile("agility", 10));
        Assert.True(clumsy.DodgeChance < 0.2f && nimble.DodgeChance > 0.8f && clumsy.DodgeLateMs > nimble.DodgeLateMs + 200f,
                    $"dodge {clumsy.DodgeChance} late {clumsy.DodgeLateMs} ms at Agility 0, {nimble.DodgeChance} late {nimble.DodgeLateMs} ms at 10");
    }

    /// <summary>Teamwork is what makes a bot talk (G.2.4): a lone wolf leaves far longer between two voice lines.</summary>
    [Fact]
    public void TeamworkShortensTheWaitBetweenVoiceLines()
    {
        SkillCurves curves = ShippedCurves();
        (float lone, float glued) = (curves.For(Profile("teamwork", 0)).VoiceEverySeconds, curves.For(Profile("teamwork", 10)).VoiceEverySeconds);
        Assert.True(glued >= 5f && lone > glued * 3f, $"a voice line every {lone} s at Teamwork 0, {glued} s at 10");
    }

    /// <summary>Curves without the reaction parameters (HL2MP's) still give a bot its reactions by its skills: they are behaviour, not a weight.</summary>
    [Fact]
    public void CurvesWithoutTheReactionParametersReadThemFromTheSkills()
    {
        SkillCurves hl2mp = Skills.Curves.Game();
        Assert.True(hl2mp.For(Profile("agility", 0)).DodgeChance < hl2mp.For(Profile("agility", 10)).DodgeChance);
    }

    /// <summary>Each trait leans the scores it is said to (plan_bots_teams_tf2_part_g G.2.2): its drive is under 1 with the trait at 0 and over 1 at 10, Nerve's the other way.</summary>
    [Theory]
    [InlineData("aggression", nameof(DecisionParams.FightDrive), true)]
    [InlineData("tenacity", nameof(DecisionParams.ChaseDrive), true)]
    [InlineData("discipline", nameof(DecisionParams.HoldDrive), true)]
    [InlineData("teamwork", nameof(DecisionParams.EscortDrive), true)]
    [InlineData("nerve", nameof(DecisionParams.SurviveDrive), false)]
    public void ATraitMovesItsDriveAboutOne(string skill, string drive, bool rises)
    {
        SkillCurves curves = ShippedCurves();
        float Read(int level) => (float)typeof(DecisionParams).GetProperty(drive)!.GetValue(curves.For(Profile(skill, level)))!;
        (float low, float high) = (Read(0), Read(10));
        Assert.True(rises ? low < 0.9f && high > 1.1f : low > 1.1f && high < 0.9f, $"{drive}: {low} at {skill} 0, {high} at 10");
    }

    [Fact]
    public void TheShippedNoiseIsSmallAndLessForADisciplinedBot()
    {
        SkillCurves curves = ShippedCurves();
        (float loose, float strict) = (curves.For(Profile("discipline", 0)).ScoreNoise, curves.For(Profile("discipline", 10)).ScoreNoise);
        Assert.True(strict > 0f && strict < loose && loose <= 0.15f, $"noise {loose} at Discipline 0, {strict} at 10");
    }

    /// <summary>Curves without the personality parameters (HL2MP's, and any written before them) weigh nothing: every drive is 1 and there is no noise.</summary>
    [Fact]
    public void CurvesWithoutThePersonalityParametersAreNeutral()
    {
        DecisionParams d = Skills.Curves.Game().For(Profile("aggression", 10));
        Assert.Equal((1f, 1f, 1f, 1f, 1f, 0f), (d.FightDrive, d.ChaseDrive, d.HoldDrive, d.EscortDrive, d.SurviveDrive, d.ScoreNoise));
    }

    [Fact]
    public void AMissingFileIsRefusedWithItsPath()
    {
        TFSkillCurves source = Source(null);
        Assert.Null(source.Curves);
        Assert.Contains(TFSkillCurves.Path, source.Refusal, StringComparison.Ordinal);
    }
}
