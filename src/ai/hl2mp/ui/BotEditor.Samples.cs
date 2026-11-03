using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.Hl2mp.Ui;

/// <summary>The bot editor's state for samples: a profile, its revisions, what can be loaded, recording what it was asked.</summary>
public sealed class FakeBotEditor : IBotEditorSource, IBotEditorActions
{
    public string ProfileName { get; set; } = "Deadeye";

    public int Pool => 60;

    public int Cap => 10;

    public int[] Levels { get; set; } = [8, 7, 6, 6, 6, 6, 1, 1, 2, 2];

    public int Spent => Levels.Sum();

    public int PointsLeft => Pool - Spent;

    public int SkillCount => Levels.Length;

    public BotSkillRow Skill(int i)
        => new(Token(i), Levels[i], Levels[i] < Cap && PointsLeft > 0, Levels[i] > 0, Derived(i));

    private static string Token(int i) => i switch
    {
        0 => "#Ai_Skill_Accuracy",
        1 => "#Ai_Skill_Alertness",
        2 => "#Ai_Skill_Aggression",
        3 => "#Ai_Skill_Cover",
        4 => "#Ai_Skill_Tenacity",
        5 => "#Ai_Skill_Cunning",
        6 => "#Ai_Skill_Discipline",
        7 => "#Ai_Skill_Agility",
        8 => "#Ai_Skill_Nerve",
        _ => "#Ai_Skill_Headhunter",
    };

    /// <summary>The parameters a row shows (keys as the model gives them, values from the committed curves at these levels).</summary>
    private static BotParameter[] Derived(int i) => i switch
    {
        0 => [new("AimErrorDeg", 2.6f), new("LeadQuality", 0.84f)],
        1 => [new("FovDeg", 85), new("GlanceRate", 0.26f), new("ReactionMs", 200), new("RecognizeS", 0.25f), new("SightRange", 3000)],
        2 => [new("ChaseSeconds", 5.2f), new("PreferredRangeScale", 0.92f)],
        3 => [new("CoverWeight", 0.6f), new("ReloadBelowPct", 0.36f)],
        4 => [new("HabitHalfLifeS", 300), new("MemoryHalfLifeS", 18), new("SearchSeconds", 16.2f)],
        5 => [new("AmbushWeight", 0.48f), new("RouteAltWeight", 0.36f)],
        6 => [new("FireRangeGate", 1.9f), new("PushMarginSeconds", 0.92f), new("PushTrust", 0.6f)],
        7 => [new("StrafeMeanSeconds", 1.12f), new("StrafeWeight", 0.1f)],
        8 => [new("RetreatHealthPct", 0.5f)],
        _ => [new("EasyTargetDiscount", 0.86f), new("PathCapMultiplier", 1.4f), new("RankBonus", 0.2f)],
    };

    public string StyleFirstToken { get; set; } = "#Ai_Skill_Accuracy";

    public string StyleSecondToken { get; set; } = "#Ai_Skill_Alertness";

    public bool CanUndo { get; set; } = true;

    public bool CanRedo { get; set; }

    public bool Unsaved { get; set; } = true;

    public bool CanSave => ProfileName.Length > 0;

    public BotEditorPanel Panel { get; set; }

    public List<BotRevisionRow> Revisions { get; init; } = [];

    public int RevisionCount => Revisions.Count;

    public BotRevisionRow Revision(int i) => Revisions[i];

    public List<BotProfileChoice> Choices { get; init; } = [];

    public int ChoiceCount => Choices.Count;

    public BotProfileChoice Choice(int i) => Choices[i];

    public string NameDraft { get; set; } = "";

    public string NameRefusalToken { get; set; } = "";

    public string StatusToken { get; set; } = "";

    public string StatusData { get; set; } = "";

    public List<string> Asked { get; } = [];

    public void Stage(int skill, int delta) => Asked.Add($"stage {skill} {delta}");

    public void Undo() => Asked.Add("undo");

    public void Redo() => Asked.Add("redo");

    public void Save() => Asked.Add("save");

    public void SaveAs() => Asked.Add("saveas");

    public void SetNameDraft(string name) => NameDraft = name;

    public void ShowPanel(BotEditorPanel panel) => Panel = panel;

    public void Load(int choice) => Asked.Add($"load {choice}");

    public void Restore(int number) => Asked.Add($"restore {number}");

    public void Done() => Asked.Add("done");

    public static List<BotRevisionRow> FourRevisions() =>
    [
        new(4, "2026-11-02 10:41", "restored #1", 45),
        new(3, "2026-11-02 10:30", "saved", 60),
        new(2, "2026-11-02 10:12", "saved", 52),
        new(1, "2026-11-02 10:00", "first", 45),
    ];

    public static List<BotProfileChoice> Loadable() =>
    [
        new("easy", 20, true),
        new("normal", 35, true),
        new("hard", 50, true),
        new("expert", 60, true),
        new("Deadeye", 45, false),
        new("Sniper", 40, false),
    ];

    public UiSampleServices Register(UiSampleServices s, MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        if (!s.Has<IUiLocalisation>())
            s.Add<IUiLocalisation>(new AiParamLocalise());
        return s.Add<IBotEditorSource>(this).Add<IBotEditorActions>(this);
    }
}

/// <summary>
/// The rows' parameter labels for samples, as the mod's language file has
/// them (game/mod_test/resource/test_english.txt; a fact holds the two
/// equal): the preview's own localiser answers the screens' tokens, and this
/// the ones the panel formats with a value.
/// </summary>
public sealed class AiParamLocalise : IUiLocalisation
{
    public string? Find(string token) => token switch
    {
        "#Ai_Param_AimErrorDeg" => "Aim error %s1°",
        "#Ai_Param_LeadQuality" => "Lead %s1",
        "#Ai_Param_TrackingLagMs" => "Tracking lag %s1 ms",
        "#Ai_Param_FlickOvershootPct" => "Flick overshoot %s1%",
        "#Ai_Param_FovDeg" => "Field of view %s1°",
        "#Ai_Param_GlanceRate" => "Glances %s1/s",
        "#Ai_Param_ReactionMs" => "Reaction %s1 ms",
        "#Ai_Param_RecognizeS" => "Recognise %s1 s",
        "#Ai_Param_SightRange" => "Sight %s1 u",
        "#Ai_Param_TurnPeakDegS" => "Turn %s1°/s",
        "#Ai_Param_ChaseSeconds" => "Chase %s1 s",
        "#Ai_Param_PreferredRangeScale" => "Range ×%s1",
        "#Ai_Param_CoverWeight" => "Seeks cover %s1",
        "#Ai_Param_ReloadBelowPct" => "Reloads below %s1%",
        "#Ai_Param_HabitHalfLifeS" => "Habits fade %s1 s",
        "#Ai_Param_MemoryHalfLifeS" => "Memory fades %s1 s",
        "#Ai_Param_SearchSeconds" => "Search %s1 s",
        "#Ai_Param_AmbushWeight" => "Ambush %s1",
        "#Ai_Param_RouteAltWeight" => "Flanking %s1",
        "#Ai_Param_FireRangeGate" => "Fire range ×%s1",
        "#Ai_Param_PushMarginSeconds" => "Push margin %s1 s",
        "#Ai_Param_PushTrust" => "Push trust %s1",
        "#Ai_Param_StrafeMeanSeconds" => "Strafe turn %s1 s",
        "#Ai_Param_StrafeWeight" => "Strafing %s1",
        "#Ai_Param_RetreatHealthPct" => "Retreats at %s1% health",
        "#Ai_Param_EasyTargetDiscount" => "Easy target ×%s1",
        "#Ai_Param_PathCapMultiplier" => "Detour ×%s1",
        "#Ai_Param_RankBonus" => "Rank bonus %s1",
        _ => null,
    };
}

internal static class BotEditorSamples
{
    [UiSample("mid-edit with points left")]
    private static UiScreen MidEdit(UiSampleServices s)
        => new FakeBotEditor { StatusToken = "#Ai_Status_Loaded", StatusData = "Deadeye" }.Register(s).Create<BotEditor>();

    [UiSample("full spend")]
    private static UiScreen Full(UiSampleServices s)
        => new FakeBotEditor { Levels = [10, 10, 10, 6, 6, 6, 10, 0, 0, 2], CanRedo = true, StyleSecondToken = "#Ai_Skill_Alertness" }
           .Register(s).Create<BotEditor>();

    [UiSample("revisions open")]
    private static UiScreen Revisions(UiSampleServices s)
        => new FakeBotEditor { Panel = BotEditorPanel.Revisions, Revisions = FakeBotEditor.FourRevisions(), Unsaved = false,
                               StatusToken = "#Ai_Status_Restored", StatusData = "Deadeye" }
           .Register(s).Create<BotEditor>();

    [UiSample("load open")]
    private static UiScreen Load(UiSampleServices s)
        => new FakeBotEditor { Panel = BotEditorPanel.Load, Choices = FakeBotEditor.Loadable() }.Register(s).Create<BotEditor>();

    [UiSample("save as, a bad name")]
    private static UiScreen SaveAs(UiSampleServices s)
        => new FakeBotEditor { ProfileName = "", Panel = BotEditorPanel.SaveAs, NameDraft = "dead eye/2", NameRefusalToken = "#Ai_Name_Invalid",
                               StyleFirstToken = "#Ai_Skill_Accuracy" }
           .Register(s).Create<BotEditor>();

    [UiSample("no revisions yet")]
    private static UiScreen NoRevisions(UiSampleServices s)
        => new FakeBotEditor { ProfileName = "", Panel = BotEditorPanel.Revisions, CanUndo = false }.Register(s).Create<BotEditor>();

    [UiSample("resized to its minimum, revisions open")]
    private static UiScreen Minimum(UiSampleServices s)
        => new FakeBotEditor { Panel = BotEditorPanel.Revisions, Revisions = FakeBotEditor.FourRevisions() }
           .Register(s, new MemoryWindowStore().With("boteditor", new UiWindowPlacement(100, 80, 10, 10))).Create<BotEditor>();
}
