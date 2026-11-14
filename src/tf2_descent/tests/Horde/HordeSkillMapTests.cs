using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Descent's temperament as the AI core's skills and curves (plan_descent_logic
/// 3.6, plan_tf2_rpg B.11): Soloness is Teamwork's inverse, Headhunter is 0, the
/// rest go by name; the embedded curves parse; each parameter a horde decider or
/// sense reads moves with its skill the way B.11 says.
/// </summary>
public sealed class HordeSkillMapTests
{
    private static readonly HordeSkillMap Map = HordeSkillMap.For(TheRules.Temperaments.Skills);
    private static readonly SkillCurves Curves = HordeSkillMap.Curves();

    private static int Core(string key) => StandardSkills.Eleven.IndexOf(StandardSkills.Eleven.First(s => s.Key == key));

    private static byte[] Eleven(System.Collections.Immutable.ImmutableArray<int> descent)
    {
        var e = new byte[Map.Count];
        Map.Fill(descent, e);
        return e;
    }

    /// <summary>Descent's level of <paramref name="skill"/> at 0..10, everything else at 4: the parameter at each.</summary>
    private static float[] Sweep(string skill, Func<SkillProfile, float> read) =>
        [.. Enumerable.Range(0, 11).Select(l => read(Map.Profile(Levels.Descent(4, (skill, l)))))];

    [Fact]
    public void Teamwork_is_the_inverse_of_Soloness()
    {
        var e = Eleven(Levels.Descent(4, ("soloness", 9)));
        Assert.Equal(1, e[Core("teamwork")]);
        Assert.Equal(10, Eleven(Levels.Descent(4, ("soloness", 0)))[Core("teamwork")]);
    }

    [Fact]
    public void Headhunter_is_zero_and_the_nine_others_go_by_name()
    {
        string[] named = ["accuracy", "alertness", "aggression", "cover", "tenacity", "cunning", "discipline", "agility", "nerve"];
        for (int i = 0; i < named.Length; i++)
        {
            var e = Eleven(Levels.Descent(2, (named[i], 7)));
            Assert.Equal(7, e[Core(named[i])]);
            Assert.Equal(0, e[Core("headhunter")]);
        }
    }

    [Fact]
    public void The_embedded_curves_parse_and_carry_the_senses_scales()
    {
        Assert.Contains(Curves.All, c => c.Parameter == "searchSeconds" && c.Skill == "tenacity");
        foreach (string p in new[] { "sightRangeScale", "sightConeScale", "hearingScale" })
            Assert.Equal("alertness", Curves[p].Skill);
        Assert.All(Curves.All, c => Assert.False(c.Style, $"{c.Parameter} reads the style, not the level"));
    }

    [Fact]
    public void Search_time_rises_with_Tenacity()
    {
        var s = Sweep("tenacity", p => Curves.For(p).SearchSeconds);
        for (int l = 1; l < s.Length; l++)
            Assert.True(s[l] > s[l - 1], $"tenacity {l}: {s[l]} s, not above {s[l - 1]}");
        Assert.True(s[10] >= 60f, $"B.11: Tenacity 10 hunts for a minute; {s[10]} s");
    }

    [Fact]
    public void The_range_a_robot_closes_to_falls_with_Aggression()
    {
        var s = Sweep("aggression", p => Curves.For(p).PreferredRangeScale);
        for (int l = 1; l < s.Length; l++)
            Assert.True(s[l] < s[l - 1], $"aggression {l}: {s[l]}, not below {s[l - 1]}");
    }

    [Fact]
    public void Sight_reach_width_and_hearing_rise_with_Alertness()
    {
        foreach (string p in new[] { "sightRangeScale", "sightConeScale", "hearingScale" })
        {
            var s = Sweep("alertness", prof => HordeSkillMap.Extra(Curves, prof, p));
            for (int l = 1; l < s.Length; l++)
                Assert.True(s[l] > s[l - 1], $"{p} at alertness {l}: {s[l]}, not above {s[l - 1]}");
        }
    }

    [Fact]
    public void Recognition_shortens_with_Alertness()
    {
        var s = Sweep("alertness", p => VisionCurves.RecognizeAt(p["alertness"]));
        for (int l = 1; l < s.Length; l++)
            Assert.True(s[l] <= s[l - 1], $"alertness {l}: {s[l]} s, longer than {s[l - 1]}");
        Assert.True(s[10] < s[0]);
    }
}
