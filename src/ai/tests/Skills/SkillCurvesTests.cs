using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

/// <summary>
/// The skill curves (plan_hl2mp_ai §10, Phase 5 step 2): the game's file
/// reads, every parameter moves with its skill the way it says, the presets
/// make the parameters their levels say, and a bad file is refused by name.
/// </summary>
public sealed class SkillCurvesTests
{
    [Fact]
    public void TheGamesFileHasEveryParameterTheBrainReads()
    {
        SkillCurves c = Curves.Game();
        Assert.Equal(SkillCurves.Required.Order(), c.All.Select(x => x.Parameter).Order());
    }

    /// <summary>Each knob moves: monotonic in its skill's level and apart at 0 and 10, with the other nine skills held.</summary>
    [Fact]
    public void EveryParameterMovesWithItsSkillAndNoOther()
    {
        SkillCurves c = Curves.Game();
        Assert.True(SkillPresets.TryGet("normal", out SkillProfile? normal));
        foreach (SkillCurves.Curve curve in c.All)
        {
            int i = normal!.IndexOf(curve.Skill);
            float[] values = [.. Enumerable.Range(0, 11).Select(l => Param(c.For(normal.With(i, l)), curve.Parameter))];
            for (int l = 1; l < values.Length; l++)
                Assert.True(curve.Rises ? values[l] >= values[l - 1] : values[l] <= values[l - 1], $"{curve.Parameter} at {curve.Skill} {l}");
            Assert.NotEqual(values[0], values[10]);
            // A style reads the skill's height above the bot's other levels (StyleTests), so only a level's reading is free of them.
            foreach (int other in Enumerable.Range(0, 10).Where(o => o != i && !curve.Style))
                Assert.Equal(Param(c.For(normal), curve.Parameter), Param(c.For(normal.With(other, normal.Levels[other] == 0 ? 10 : 0)), curve.Parameter));
        }
    }

    private static float Param(DecisionParams d, string name)
        => (float)typeof(DecisionParams).GetProperty(name, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(d)!;

    [Fact]
    public void HeadhuntersRankBonusIsNothingAtZeroAndWholeAtTen()
    {
        SkillCurves.Curve c = Curves.Game()["rankBonus"];
        Assert.Equal(("headhunter", 0f, 1f), (c.Skill, c.At(0), c.At(10)));
    }

    [Fact]
    public void ThePresetsReadTheirLevelsOffTheCurves()
    {
        SkillCurves c = Curves.Game();
        Assert.True(SkillPresets.TryGet("easy", out SkillProfile? easy));
        Assert.True(SkillPresets.TryGet("expert", out SkillProfile? expert));
        Assert.Equal(c["aimErrorDeg"].At(2), c.For(easy!).AimErrorDeg);
        Assert.Equal(c["aimErrorDeg"].At(8), c.For(expert!).AimErrorDeg);
        Assert.True(c.For(expert!).AimErrorDeg < c.For(easy!).AimErrorDeg);
    }

    [Theory]
    [InlineData("\"format\": 1", "\"format\": 2", "format 1 expected")]
    [InlineData("\"skill\": \"nerve\"", "\"skill\": \"luck\"", "names no skill 'luck'")]
    [InlineData("\"direction\": \"down\"", "\"direction\": \"sideways\"", "goes 'sideways'")]
    public void ABadFileIsRefusedByName(string from, string to, string said)
    {
        string json = File.ReadAllText(Curves.Path);
        Assert.Contains(from, json);
        var e = Assert.Throws<InvalidDataException>(() => SkillCurves.Parse(ReplaceFirst(json, from, to)));
        Assert.Contains(said, e.Message);
    }

    [Fact]
    public void AFlatCurveAndAMissingOneAreRefused()
    {
        string json = File.ReadAllText(Curves.Path);
        System.Text.Json.Nodes.JsonNode root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        root["parameters"]!["rankBonus"]!["levels"] = new System.Text.Json.Nodes.JsonArray([.. Enumerable.Range(0, 11).Select(_ => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(0.5))]);
        Assert.Contains("does not go up with headhunter", Assert.Throws<InvalidDataException>(() => SkillCurves.Parse(root.ToJsonString())).Message);
        root["parameters"]!.AsObject().Remove("rankBonus");
        Assert.Contains("no 'rankBonus'", Assert.Throws<InvalidDataException>(() => SkillCurves.Parse(root.ToJsonString())).Message);
    }

    private static string ReplaceFirst(string s, string from, string to)
    {
        int i = s.IndexOf(from, StringComparison.Ordinal);
        return s[..i] + to + s[(i + from.Length)..];
    }
}
