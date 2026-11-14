using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// TF2's bots page and the bot editor it opens speak TF2's own words (plan_bots_teams_tf2 P6
/// step 8). The preview's language chain answers from the test mod's file first, so a token
/// TF2's file lacks would still read in every preview; in game TF2 reads only its own file
/// (and the core's). These lint each sample of the two screens against that chain alone.
/// </summary>
public class TFBotsPageTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>TF2's language files and the core's #SS_UI_ strings: what TF2's UI answers from in game, the test mod's file left out.</summary>
    private static UiLanguageFile Tf2Chain()
    {
        string dir = RepoLayout.Repo("game", "mod_tf2", "resource");
        string[] files = Directory.GetFiles(dir, "*_english.txt");
        Array.Sort(files, StringComparer.Ordinal);
        return UiLanguageFile.Chain([.. files, RepoLayout.Repo(UiLanguageFile.CoreRepoPath)]);
    }

    /// <summary>The localise findings of every sample of <paramref name="group"/> at 1920x1080 under <paramref name="language"/>.</summary>
    private static List<string> Unlocalised(string group, UiLanguageFile language)
    {
        var samples = PreviewRun.AllSamples().Where(s => !s.Gallery && s.Group == group).ToList();
        Assert.NotEmpty(samples);
        var findings = new List<string>();
        foreach (var sample in samples)
        {
            var theme = UiGoldenTests.ThemeOf(sample);
            var ctx = UiSamples.PreviewContext(theme, Fonts, language);
            UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
            findings.AddRange(new UiLint(theme, Fonts, []).Check(ctx, $"{group}/{sample.Slug}")
                .Where(f => f.Rule == "localise").Select(f => f.ToString()));
        }

        return findings;
    }

    [Fact]
    public void TF2s_file_holds_every_word_its_bots_page_shows()
    {
        var missing = Unlocalised("createserver-tfbotspage", Tf2Chain());
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void TF2s_file_holds_every_word_the_bot_editor_shows()
    {
        var missing = Unlocalised("boteditor", Tf2Chain());
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    /// <summary>The control: the core's strings alone lack the page's words, and the check names them.</summary>
    [Fact]
    public void A_chain_without_TF2s_file_is_reported()
    {
        var core = UiLanguageFile.Chain([RepoLayout.Repo(UiLanguageFile.CoreRepoPath)]);
        Assert.Contains(Unlocalised("createserver-tfbotspage", core), f => f.Contains("#Ai_TF_Class", StringComparison.Ordinal));
    }

    /// <summary>
    /// The editor's parameter rows are localised by the side's IUiLocalisation, which the
    /// samples answer from the test mod's file: TF2's file must carry every parameter word
    /// the test mod's does, since TF2's curves name the same parameters.
    /// </summary>
    [Fact]
    public void TF2s_file_holds_every_parameter_word()
    {
        var tf2 = Tf2Chain();
        var test = UiLanguageFile.Load(RepoLayout.Repo("game", "mod_test", "resource", "test_english.txt"));
        string[] parameters = [.. File.ReadAllLines(RepoLayout.Repo("game", "mod_test", "resource", "test_english.txt"), System.Text.Encoding.Unicode)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("\"Ai_Param_", StringComparison.Ordinal))
            .Select(l => "#" + l[1..l.IndexOf('"', 1)])];
        Assert.True(parameters.Length >= 40, $"{parameters.Length} parameter words");   // the stimulus reaches the subject
        Assert.All(parameters, p => Assert.NotNull(test.Find(p)));
        var missing = parameters.Where(p => tf2.Find(p) is null).ToList();
        Assert.True(missing.Count == 0, "TF2's file lacks " + string.Join(", ", missing));
    }
}
