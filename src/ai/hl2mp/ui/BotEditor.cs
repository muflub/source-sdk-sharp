using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.Hl2mp.Ui;

/// <summary>
/// The custom bot editor (plan_hl2mp_ai §3.5): the pool's limits in sight —
/// the points left as the biggest number, the spend against the pool, the
/// cap of a skill as ten pips a row — then the ten skills, each with − and
/// + (a step of 1 or 5, the Descent sheet's toggle), its level and what the
/// level makes of the bot's parameters; the style name; Undo and Redo; and
/// Load (the built-in presets as starting points, the saved profiles),
/// Revisions (restore), Save as and Save. A + that would overspend or pass
/// the cap is not offered, so an overspent profile cannot be made, and so
/// cannot be saved. Opened by the create-server screen's Add custom and Edit.
/// Narrow: the side panel takes the skills' place.
/// </summary>
public sealed class BotEditor : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IBotEditorSource _source;
    private readonly IBotEditorActions _actions;
    private readonly SkillRowPanel _row;
    private readonly RevisionList _revisions;
    private readonly ProfileChoiceList _choices;

    /// <summary>The − and + step: 1, or 5 after the step toggle.</summary>
    private int _step = 1;

    public BotEditor(UiWindows windows, IBotEditorSource source, IBotEditorActions actions, SkillRowPanel row,
                     RevisionList revisions, ProfileChoiceList choices)
    {
        _windows = windows;
        _source = source;
        _actions = actions;
        _row = row;
        _revisions = revisions;
        _choices = choices;
    }

    public override string Name => "boteditor";

    public string WindowId => "boteditor";

    /// <summary>The skill rows' keys start here, clear of the side lists' row keys (0..n).</summary>
    private const int SkillKeys = 1000;

    /// <summary>The side panel's width, du.</summary>
    public const float SideDu = 360;

    /// <summary>
    /// 1040 x 800 du by default, at least 600 x 800: the header, ten 48 du
    /// rows and the footer. Narrow below 900 du, where the side panel beside
    /// the rows would cut their parameters to nothing.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(1040, screenW - 32), MathF.Min(800, screenH - 32), 600, 800, screenW, screenH, 900);

    public override void Describe(ref UiFrame ui)
    {
        var s = _source;
        using (ui.Window(_windows, WindowId, "#Ai_Editor_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            bool narrow = ui.Narrow;
            Header(ref ui, s);

            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
            {
                if (ui.Button("undo", "#Ai_Editor_Undo", enabled: s.CanUndo).Clicked)
                    _actions.Undo();
                if (ui.Button("redo", "#Ai_Editor_Redo", enabled: s.CanRedo).Clicked)
                    _actions.Redo();
                if (ui.Button("step", _step == 1 ? "#Ai_Editor_Step1" : "#Ai_Editor_Step5").Clicked)
                    _step = _step == 1 ? 5 : 1;
                ui.Flex();
                if (s.StatusToken.Length > 0)
                {
                    bool refused = s.StatusToken == "#Ai_Status_Refused";
                    ui.Text(s.StatusToken, Text.Body, refused ? Tone.Danger : Tone.Positive);
                    ui.Data(s.StatusData, Text.Body, Tone.TextMuted).MaxWidth(360).Elide();
                }
            }

            var panel = s.Panel;
            using (ui.Row(Space.L, Align.Stretch).Size(Size.Fill, Size.Fill))
            {
                if (panel == BotEditorPanel.None || !narrow)
                {
                    using (ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Fill))
                    {
                        for (int i = 0; i < s.SkillCount; i++)
                            ui.Sub(SkillKeys + i, _row, new SkillRowProps(i, _step)).Width(Size.Fill);
                    }
                }

                if (panel != BotEditorPanel.None)
                    Side(ref ui, s, panel, narrow);
            }

            // Narrow, the five buttons take two rows: the panels' above the saves'.
            if (narrow)
            {
                using (ui.DialogButtons().Width(Size.Fill))
                {
                    PanelButtons(ref ui);
                    ui.Flex();
                }
            }

            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (!narrow)
                    PanelButtons(ref ui);
                ui.Flex();
                if (ui.Button("saveas", "#Ai_Editor_SaveAs").Clicked)
                    _actions.ShowPanel(BotEditorPanel.SaveAs);
                // A bot without a name of its own is saved through Save as.
                if (ui.Button("save", "#Ai_Editor_Save", enabled: s.Unsaved).Clicked)
                {
                    if (s.CanSave)
                        _actions.Save();
                    else
                        _actions.ShowPanel(BotEditorPanel.SaveAs);
                }
                if (ui.Button("done", "#Ai_Editor_Done", primary: true).Clicked)
                {
                    _actions.Done();
                    _windows.RequestClose(WindowId);
                }
            }
        }
    }

    private void PanelButtons(ref UiFrame ui)
    {
        if (ui.Button("load", "#Ai_Editor_Load").Clicked)
            _actions.ShowPanel(BotEditorPanel.Load);
        if (ui.Button("revisions", "#Ai_Editor_Revisions").Clicked)
            _actions.ShowPanel(BotEditorPanel.Revisions);
    }

    /// <summary>The name and style on the left; the pool's state on the right, points left the biggest number.</summary>
    private static void Header(ref UiFrame ui, IBotEditorSource s)
    {
        using (ui.Row(Space.L, Align.Centre).Width(Size.Fill))
        {
            using (ui.Column(Space.XS, Align.Start).Width(Size.Fill))
            {
                if (s.ProfileName.Length > 0)
                    ui.Data(s.ProfileName, Text.Heading).Elide();
                else
                    ui.Text("#Ai_Editor_New", Text.Heading, Tone.TextMuted);
                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Label("#Ai_Editor_Style");
                    ui.Label(s.StyleFirstToken, Tone.Accent);
                    ui.Label(s.StyleSecondToken, Tone.Accent);
                    if (s.Unsaved)
                        ui.Label("#Ai_Editor_Unsaved", Tone.Warning);
                }
            }

            using (ui.Column(Space.XS, Align.End))
            {
                using (ui.Row(Space.S, Align.End))
                {
                    ui.Number(s.PointsLeft, Text.Title, s.PointsLeft > 0 ? Tone.Accent : Tone.TextMuted);
                    ui.Label("#Ai_Editor_PointsLeft");
                }

                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Meter(s.Pool > 0 ? s.Spent / (float)s.Pool : 0f, Tone.Accent, Size.Du(160));
                    ui.Text($"{s.Spent} / {s.Pool}", Text.Body);
                    ui.Label("#Ai_Editor_Spent");
                }

                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Label("#Ai_Editor_PerSkill");
                    ui.Number(s.Cap, Text.Label, Tone.TextMuted);
                }
            }
        }
    }

    private void Side(ref UiFrame ui, IBotEditorSource s, BotEditorPanel panel, bool narrow)
    {
        using (ui.Column(Space.M, Align.Stretch).Size(narrow ? Size.Fill : Size.Du(SideDu), Size.Fill)
                 .Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.M))
        {
            switch (panel)
            {
                case BotEditorPanel.Revisions:
                    ui.Text("#Ai_Editor_Revisions", Text.Heading);
                    if (s.RevisionCount == 0)
                        ui.EmptyState(UiShape.Star, "#Ai_Editor_NoRevisions_Title", "#Ai_Editor_NoRevisions_Body");
                    else
                        ui.VirtualList("revisionlist", s.RevisionCount, RevisionList.RowDu, _revisions).Size(Size.Fill, Size.Fill);
                    break;
                case BotEditorPanel.Load:
                    ui.Text("#Ai_Editor_Load", Text.Heading);
                    ui.VirtualList("choicelist", s.ChoiceCount, ProfileChoiceList.RowDu, _choices).Size(Size.Fill, Size.Fill);
                    break;
                default:
                    ui.Text("#Ai_Editor_SaveAs", Text.Heading);
                    ui.Label("#Ai_Editor_Name");
                    string typed = ui.EditText("name", s.NameDraft, "#Ai_Editor_NamePlaceholder");
                    if (!ReferenceEquals(typed, s.NameDraft))
                        _actions.SetNameDraft(typed);
                    string refusal = s.NameRefusalToken;
                    if (refusal.Length > 0)
                        ui.Text(refusal, Text.Body, Tone.Danger).Wrap(SideDu - 32);
                    using (ui.Row(Space.M).Width(Size.Fill))
                    {
                        ui.Flex();
                        if (ui.Button("saveasok", "#Ai_Editor_Save", primary: true, enabled: refusal.Length == 0).Clicked)
                            _actions.SaveAs();
                    }
                    break;
            }
        }
    }
}

/// <summary>A skill row's per-call decisions: which skill, and the − / + step.</summary>
public readonly record struct SkillRowProps(int Index, int Step);

/// <summary>
/// One skill: its name, − and +, its level, ten pips against the cap, and
/// what the level makes of the bot's parameters, each by its readable label
/// with its unit from the language file (<c>#Ai_Param_AimErrorDeg</c>
/// "Aim error %s1°"); the web inspector keeps the raw names. A − at 0 and a
/// + at the cap or with no points left are drawn dead and do nothing.
/// </summary>
public sealed class SkillRowPanel(IBotEditorSource source, IBotEditorActions actions, IUiLocalisation localise) : UiPanel<SkillRowProps>
{
    public const float RowDu = 48;

    /// <summary>Each row's parameters as last shown and their line: rebuilt only when the row's parameters change (an edit).</summary>
    private BotParameter[]?[] _shown = [];
    private string[] _lines = [];

    public override void Describe(ref UiFrame ui, in SkillRowProps props)
    {
        BotSkillRow row = source.Skill(props.Index);
        int cap = source.Cap;
        using (ui.Row(Space.S, Align.Centre).Size(Size.Fill, Size.Du(RowDu)))
        {
            ui.Text(row.NameToken, Text.Body).Width(Size.Du(120)).Elide();
            if (Step(ref ui, "minus", false, row.CanLower))
                actions.Stage(props.Index, -props.Step);
            ui.Number(row.Level, Text.Heading).Width(Size.Du(32)).TextAlign(Align.Centre);
            if (Step(ref ui, "plus", true, row.CanRaise))
                actions.Stage(props.Index, props.Step);

            using (ui.Row(Space.XS, Align.Centre))
            {
                for (int p = 0; p < cap; p++)
                    ui.Rect(p < row.Level ? Tone.Accent : Tone.SurfaceRaised, Size.Du(12), Size.Du(12)).Key(p).Round(Radius.S);
            }

            ui.Data(LineOf(props.Index, row.Parameters), Text.Body, Tone.TextMuted).Width(Size.Fill).Elide();
        }
    }

    private string LineOf(int index, BotParameter[] parameters)
    {
        if ((uint)index >= (uint)_lines.Length)
        {
            Array.Resize(ref _shown, index + 1);
            Array.Resize(ref _lines, index + 1);
        }

        if (!ReferenceEquals(_shown[index], parameters))
        {
            _shown[index] = parameters;
            _lines[index] = Line(parameters, localise);
        }

        return _lines[index];
    }

    /// <summary>
    /// A row's parameters as the player reads them: each <c>#Ai_Param_&lt;Key&gt;</c>
    /// with its value for <c>%s1</c> ("Aim error 5.8° · Lead 0.52"), a share
    /// (a key ending <c>Pct</c>) as a percentage. A key the language file lacks
    /// shows as itself.
    /// </summary>
    public static string Line(BotParameter[] parameters, IUiLocalisation localise)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(localise);
        var line = new System.Text.StringBuilder();
        foreach (BotParameter p in parameters)
        {
            string value = p.Key.EndsWith("Pct", StringComparison.Ordinal)
                ? MathF.Round(p.Value * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : p.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string label = localise.Find("#Ai_Param_" + p.Key) ?? p.Key + " %s1";
            line.Append(line.Length > 0 ? " · " : "").Append(label.Replace("%s1", value, StringComparison.Ordinal));
        }

        return line.ToString();
    }

    /// <summary>A 40 du square − or + button; dead (muted, no click) when it may not move the level.</summary>
    private static bool Step(ref UiFrame ui, string key, bool plus, bool enabled)
    {
        var box = ui.Box(key).Size(Size.Du(40), Size.Du(40)).Round(Radius.M)
                    .Fill(Tone.SurfaceRaised).Border(enabled ? Tone.Line : Tone.SurfaceRaised, 1);
        var hit = ui.Interact(box);
        Tone ink = enabled ? (hit.Hovered ? Tone.Accent : Tone.Text) : Tone.TextMuted;
        using (box)
        {
            if (plus)
                ui.Shape(UiShape.Cross, ink, 16).At(UiAnchor.Centre);
            else
                ui.Rect(ink, Size.Du(16), Size.Du(4)).At(UiAnchor.Centre);
        }

        return enabled && hit.Clicked;
    }
}

/// <summary>One saved revision, newest first: its number, when, its note, its spend, and Restore.</summary>
public sealed class RevisionList(IBotEditorSource source, IBotEditorActions actions) : UiPanel<int>
{
    public const float RowDu = 64;

    public override void Describe(ref UiFrame ui, in int i)
    {
        BotRevisionRow r = source.Revision(i);
        using (ui.Row(Space.S, Align.Centre).Size(Size.Fill, Size.Fill))
        {
            using (ui.Column(Space.None, Align.Start).Width(Size.Fill))
            {
                using (ui.Row(Space.XS, Align.Centre))
                {
                    ui.Number(r.Number, Text.Label, Tone.Accent);
                    ui.Data(r.At, Text.Label, Tone.TextMuted);
                }

                if (r.Note.Length > 0)
                    ui.Data(r.Note, Text.Body).Elide();
                ui.Text($"{r.Spent} / {source.Pool}", Text.Label, Tone.TextMuted);
            }

            if (ui.Button("restore", "#Ai_Editor_Restore", width: Size.Du(120)).Clicked)
                actions.Restore(r.Number);
        }
    }
}

/// <summary>A profile to load: a built-in preset (marked) or a saved one, with its spend.</summary>
public sealed class ProfileChoiceList(IBotEditorSource source, IBotEditorActions actions) : UiPanel<int>
{
    public const float RowDu = 48;

    public override void Describe(ref UiFrame ui, in int i)
    {
        BotProfileChoice c = source.Choice(i);
        using (ui.Row(Space.S, Align.Centre).Size(Size.Fill, Size.Fill))
        {
            using (ui.Column(Space.None, Align.Start).Width(Size.Fill))
            {
                if (c.BuiltIn)
                    ui.Text(PresetToken(c.Name), Text.Body);
                else
                    ui.Data(c.Name, Text.Body).Elide();
                using (ui.Row(Space.XS, Align.Centre))
                {
                    ui.Text($"{c.Spent} / {source.Pool}", Text.Label, Tone.TextMuted);
                    if (c.BuiltIn)
                        ui.Label("#Ai_Editor_BuiltIn", Tone.Accent);
                }
            }

            if (ui.Button("load", "#Ai_Editor_Load", width: Size.Du(120)).Clicked)
                actions.Load(i);
        }
    }

    /// <summary>A built-in preset's name as the slider says it: <c>normal</c> is Medium; Expert is a custom bot's starting point only.</summary>
    private static string PresetToken(string name) => name switch
    {
        "easy" => "#Ai_Bots_Easy",
        "normal" => "#Ai_Bots_Medium",
        "hard" => "#Ai_Bots_Hard",
        _ => "#Ai_Bots_Expert",
    };
}
