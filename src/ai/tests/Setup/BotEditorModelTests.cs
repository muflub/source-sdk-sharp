extern alias client;

using System.Reflection;
using Xunit;

using BotEditorModel = client::SourceSharp.Ai.Hl2mp.Client.Setup.BotEditorModel;
using BotProfileStore = client::SourceSharp.Ai.Hl2mp.Client.Profiles.BotProfileStore;
using ClientSkillCurves = client::SourceSharp.Ai.Hl2mp.Client.Setup.ClientSkillCurves;
using CustomBot = client::SourceSharp.Ai.Hl2mp.Client.Setup.CustomBot;
using CustomBots = client::SourceSharp.Ai.Hl2mp.Client.Setup.CustomBots;
using SkillDerived = client::SourceSharp.Ai.Hl2mp.Client.Setup.SkillDerived;
using IEngineFiles = client::SourceSharp.Abi.IEngineFiles;
using SourceSharp.Ai.Hl2mp.Ui;

namespace SourceSharp.Ai.Tests.Setup;

/// <summary>
/// The bot editor's model (plan_hl2mp_ai §3.5, §8.2): the pool's limits as
/// the rows offer them, undo/redo, save and load through the store, the
/// revisions and restore, Save as's name, and Done putting the bot on the
/// create-server list.
/// </summary>
public class BotEditorModelTests
{
    /// <summary>The game's files as the client reads them: the committed skill curves, nothing else.</summary>
    public class GameFiles : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == "ReadGameFile" && (string)args![0]! == ClientSkillCurves.Path
                ? File.ReadAllText(SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_skills.json"))
                : null;
    }

    /// <summary>A clock that moves a minute each time it is read.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now = _now.AddMinutes(1);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    internal static BotEditorModel Editor(CustomBots customs, BotProfileStoreTests.MemoryDisk? disk = null)
        => new(new BotProfileStore(disk ?? new BotProfileStoreTests.MemoryDisk()), customs,
               new ClientSkillCurves(DispatchProxy.Create<IEngineFiles, GameFiles>()), new Clock());

    private readonly CustomBots _customs = new();
    private readonly BotProfileStoreTests.MemoryDisk _disk = new();
    private readonly BotEditorModel _editor;

    public BotEditorModelTests()
    {
        _editor = Editor(_customs, _disk);
    }

    private string Levels() => string.Join(",", Enumerable.Range(0, _editor.SkillCount).Select(i => _editor.Skill(i).Level));

    [Fact]
    public void ANewBotStartsAtMediumUnnamed()
    {
        _editor.Begin(-1);
        Assert.Equal(("4,4,4,3,3,3,4,3,4,3", 25, ""), (Levels(), _editor.PointsLeft, _editor.ProfileName));
    }

    [Fact]
    public void TenEditsThreeUndosTwoRedosLandWhereExpected()
    {
        _editor.Begin(-1);
        _editor.Load(-1);
        for (int i = 0; i < 10; i++)
            _editor.Stage(i, i % 2 == 0 ? 1 : -1);
        _editor.Undo();
        _editor.Undo();
        _editor.Undo();
        _editor.Redo();
        _editor.Redo();
        // Medium's levels with edits 1..9 applied (+1 on the even skills, -1 on the odd), the tenth undone.
        Assert.Equal("5,3,5,2,4,2,5,2,5,3", Levels());
    }

    [Fact]
    public void ANewEditClearsRedo()
    {
        _editor.Begin(-1);
        _editor.Stage(0, 1);
        _editor.Undo();
        Assert.True(_editor.CanRedo);
        _editor.Stage(1, 1);
        Assert.False(_editor.CanRedo);
    }

    [Fact]
    public void APlusIsNotOfferedWithNoPointsLeft()
    {
        _editor.Begin(-1);
        _editor.Stage(0, 6);
        _editor.Stage(1, 6);
        _editor.Stage(2, 6);
        _editor.Stage(3, 5);
        _editor.Stage(4, 2);
        Assert.Equal(0, _editor.PointsLeft);
        Assert.All(Enumerable.Range(0, 10), i => Assert.False(_editor.Skill(i).CanRaise));
    }

    [Fact]
    public void APlusIsNotOfferedAtTheCap()
    {
        _editor.Begin(-1);
        _editor.Stage(0, 6);
        Assert.Equal((10, false, true), (_editor.Skill(0).Level, _editor.Skill(0).CanRaise, _editor.Skill(1).CanRaise));
    }

    [Fact]
    public void AnOverspendingStepIsRefusedAndChangesNothing()
    {
        _editor.Begin(-1);
        _editor.Stage(0, 6);
        _editor.Stage(1, 6);
        _editor.Stage(2, 6);
        _editor.Stage(3, 5);
        _editor.Stage(4, 1);
        _editor.Stage(5, 5);
        Assert.Equal((1, "#Ai_Status_Refused"), (_editor.PointsLeft, _editor.StatusToken));
        Assert.Contains("spends 64 of a 60-point pool", _editor.StatusData, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnnamedBotsSaveAsksForAName()
    {
        _editor.Begin(-1);
        _editor.Save();
        Assert.Equal((BotEditorPanel.SaveAs, 0), (_editor.Panel, _disk.Files.Count));
    }

    [Fact]
    public void SaveAsWritesTheProfileWithAFirstRevision()
    {
        _editor.Begin(-1);
        _editor.Stage(0, 4);
        _editor.SetNameDraft("Deadeye");
        _editor.SaveAs();
        Assert.Equal(("Deadeye", false, 1), (_editor.ProfileName, _editor.Unsaved, _editor.RevisionCount));
        Assert.True(new BotProfileStore(_disk).Load("Deadeye", out BotProfileDocument? doc).Accepted);
        Assert.Equal(("8,4,4,3,3,3,4,3,4,3", "saved as Deadeye"), (string.Join(",", doc!.Profile.Levels), doc.Revisions[0].Note));
    }

    [Theory]
    [InlineData("dead eye", "#Ai_Name_Invalid")]
    [InlineData("a/b", "#Ai_Name_Invalid")]
    [InlineData("", "#Ai_Name_Invalid")]
    [InlineData("Expert", "#Ai_Name_BuiltIn")]
    [InlineData("Dead_Eye-2", "")]
    public void SaveAsNamesAreJudgedBeforeTheDisk(string name, string refusal)
    {
        _editor.Begin(-1);
        _editor.SetNameDraft(name);
        Assert.Equal(refusal, _editor.NameRefusalToken);
        _editor.SaveAs();
        Assert.Equal(refusal.Length == 0 ? 1 : 0, _disk.Files.Count);
    }

    [Fact]
    public void SaveAsRefusesAnotherSavedProfilesName()
    {
        new BotProfileStore(_disk).Save(new SkillProfile("Sniper", StandardSkills.Ten, [1, 0, 0, 0, 0, 0, 0, 0, 0, 0]), []);
        _editor.Begin(-1);
        _editor.SetNameDraft("sniper");
        Assert.Equal("#Ai_Name_Taken", _editor.NameRefusalToken);
    }

    [Fact]
    public void ASavedProfileLoadsWithItsNameAndRevisions()
    {
        _editor.Begin(-1);
        _editor.SetNameDraft("Deadeye");
        _editor.SaveAs();
        _editor.Stage(0, 1);
        _editor.Save();

        BotEditorModel other = Editor(new CustomBots(), _disk);
        other.Begin(-1);
        other.ShowPanel(BotEditorPanel.Load);
        int deadeye = Enumerable.Range(0, other.ChoiceCount).Single(i => other.Choice(i).Name == "Deadeye");
        other.Load(deadeye);
        Assert.Equal(("Deadeye", 2, "5,4,4,3,3,3,4,3,4,3"), (other.ProfileName, other.RevisionCount, string.Join(",", Enumerable.Range(0, 10).Select(i => other.Skill(i).Level))));
    }

    [Fact]
    public void TheLoadListOffersThePresetsFirstExpertAmongThem()
    {
        _editor.Begin(-1);
        _editor.ShowPanel(BotEditorPanel.Load);
        Assert.Equal(["easy", "normal", "hard", "expert"], Enumerable.Range(0, 4).Select(i => _editor.Choice(i).Name));
        Assert.All(Enumerable.Range(0, 4), i => Assert.True(_editor.Choice(i).BuiltIn));
    }

    [Fact]
    public void LoadingExpertIsAStartingPointOneUndoAway()
    {
        _editor.Begin(-1);
        _editor.ShowPanel(BotEditorPanel.Load);
        _editor.Load(3);
        Assert.Equal(("8,7,6,6,6,6,7,5,5,4", 0, ""), (Levels(), _editor.PointsLeft, _editor.ProfileName));
        _editor.Undo();
        Assert.Equal("4,4,4,3,3,3,4,3,4,3", Levels());
    }

    [Fact]
    public void RestoringRevisionOneMakesItCurrentAndRecordsIt()
    {
        _editor.Begin(-1);
        _editor.SetNameDraft("Deadeye");
        _editor.SaveAs();
        _editor.Stage(0, 3);
        _editor.Save();
        _editor.Restore(1);
        Assert.Equal(("4,4,4,3,3,3,4,3,4,3", 3, 3, "restored #1"),
                     (Levels(), _editor.RevisionCount, _editor.Revision(0).Number, _editor.Revision(0).Note));
        Assert.True(new BotProfileStore(_disk).Load("Deadeye", out BotProfileDocument? doc).Accepted);
        Assert.Equal(3, doc!.Revisions.Length);
    }

    [Fact]
    public void RevisionsAreListedNewestFirst()
    {
        _editor.Begin(-1);
        _editor.SetNameDraft("Deadeye");
        _editor.SaveAs();
        _editor.Stage(0, 1);
        _editor.Save();
        Assert.Equal([2, 1], Enumerable.Range(0, 2).Select(i => _editor.Revision(i).Number));
        Assert.Equal("2026-11-02 10:02", _editor.Revision(0).At);
    }

    [Fact]
    public void DoneAddsANewBotToTheList()
    {
        _editor.Begin(-1);
        _editor.Stage(0, 2);
        _editor.Done();
        CustomBot bot = Assert.Single(_customs.All);
        Assert.Equal(("", 37), (bot.Profile.Name, bot.Profile.Spent));
    }

    [Fact]
    public void DoneReplacesTheBotBeingEdited()
    {
        _customs.Add(new CustomBot(new SkillProfile("Deadeye", StandardSkills.Ten, [8, 7, 6, 6, 6, 6, 1, 1, 2, 2]), []));
        _editor.Begin(0);
        _editor.Stage(9, 3);
        _editor.Done();
        Assert.Equal(("Deadeye", 48), (Assert.Single(_customs.All).Profile.Name, _customs[0].Profile.Spent));
    }

    [Fact]
    public void ARowShowsWhatItsLevelMakesOfTheParameters()
    {
        _editor.Begin(-1);
        SkillPresets.TryGet("normal", out SkillProfile? medium);
        float aim = Skills.Curves.Game()["aimErrorDeg"].At(medium!.Levels[medium.IndexOf("accuracy")]);
        Assert.Contains(_editor.Skill(0).Parameters, p => p.Key == "AimErrorDeg" && p.Value == aim);
        Assert.Contains(_editor.Skill(1).Parameters, p => p.Key == "ReactionMs");
    }

    /// <summary>
    /// Every parameter a row can show, at every preset, has its readable label
    /// with a value in the mod's language file (<c>#Ai_Param_&lt;Key&gt;</c>, "%s1"),
    /// so no row falls back to a raw name.
    /// </summary>
    [Fact]
    public void EveryParameterARowShowsHasALabelInTheLanguageFile()
    {
        string file = System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(
            SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "resource", "test_english.txt")));
        var labels = System.Text.RegularExpressions.Regex.Matches(file, "\"Ai_Param_(\\w+)\"\\t+\"([^\"]*)\"")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var keys = SkillPresets.Names.SelectMany(n => SkillPresets.TryGet(n, out SkillProfile? p) ? SkillDerived.Parameters(p!, Skills.Curves.Game()) : [])
            .SelectMany(row => row).Select(p => p.Key).Distinct().ToList();
        Assert.True(keys.Count >= 20, $"the rows show {keys.Count} parameters");
        var missing = keys.Where(k => !labels.TryGetValue(k, out string? text) || !text.Contains("%s1", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "no label with %s1 for: " + string.Join(", ", missing));
    }

    [Fact]
    public void AnInspectorNameIsItsPascalCaseKey()
        => Assert.Equal(["FovDeg", "MemoryHalfLifeS", "AimErrorDeg"], ((string[])["fov_deg", "memory_half_life_s", "AimErrorDeg"]).Select(SkillDerived.Key));
}
