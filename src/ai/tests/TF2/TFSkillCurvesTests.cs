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

    [Fact]
    public void AMissingFileIsRefusedWithItsPath()
    {
        TFSkillCurves source = Source(null);
        Assert.Null(source.Curves);
        Assert.Contains(TFSkillCurves.Path, source.Refusal, StringComparison.Ordinal);
    }
}
