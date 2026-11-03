using SourceSharp.Ai.Hl2mp.Ui;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The bot editor's rows (plan_hl2mp_ai §3.5) read each parameter by its
/// label and unit from the mod's language file, not by the inspector's raw
/// name: "Aim error 5.8° · Lead 0.52", not "AimErrorDeg 5.8 · LeadQuality 0.52".
/// </summary>
public class AiSkillRowTests
{
    private sealed class FileLocalisation(UiLanguageFile file) : IUiLocalisation
    {
        public string? Find(string token) => file.Find(token);
    }

    private static readonly UiLanguageFile Mod = UiLanguageFile.Load(RepoLayout.Repo("game", "mod_test", "resource", "test_english.txt"));

    private static string Line(params BotParameter[] parameters) => SkillRowPanel.Line(parameters, new FileLocalisation(Mod));

    [Fact]
    public void ARowReadsItsParametersByLabelWithUnits()
    {
        Assert.Equal("Aim error 5.8° · Lead 0.52", Line(new("AimErrorDeg", 5.8f), new("LeadQuality", 0.52f)));
        Assert.Equal("Field of view 90° · Reaction 500 ms", Line(new("FovDeg", 90f), new("ReactionMs", 500f)));
    }

    [Fact]
    public void AShareIsAPercentage()
        => Assert.Equal("Reloads below 35% · Retreats at 40% health", Line(new("ReloadBelowPct", 0.35f), new("RetreatHealthPct", 0.4f)));

    [Fact]
    public void AKeyTheFileLacksShowsAsItself()
        => Assert.Equal("NoSuchParam 2", Line(new BotParameter("NoSuchParam", 2f)));

    /// <summary>The samples' labels are the language file's, so the previews show what the game shows.</summary>
    [Fact]
    public void TheSamplesLabelsAreTheLanguageFiles()
    {
        var sample = new AiParamLocalise();
        string[] keys = ["AimErrorDeg", "LeadQuality", "TrackingLagMs", "FlickOvershootPct", "FovDeg", "GlanceRate", "ReactionMs", "RecognizeS", "SightRange", "TurnPeakDegS",
                         "ChaseSeconds", "PreferredRangeScale", "CoverWeight", "ReloadBelowPct", "HabitHalfLifeS", "MemoryHalfLifeS",
                         "SearchSeconds", "AmbushWeight", "RouteAltWeight", "FireRangeGate", "PushMarginSeconds", "PushTrust",
                         "StrafeMeanSeconds", "StrafeWeight", "RetreatHealthPct", "EasyTargetDiscount", "PathCapMultiplier", "RankBonus"];
        foreach (string key in keys)
        {
            string token = "#Ai_Param_" + key;
            Assert.NotNull(sample.Find(token));
            Assert.Equal(Mod.Find(token), sample.Find(token));
        }
    }
}
