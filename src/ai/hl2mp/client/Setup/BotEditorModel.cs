using System.Collections.Immutable;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Client.Profiles;
using SourceSharp.Ai.Hl2mp.Ui;

namespace SourceSharp.Ai.Hl2mp.Client.Setup;

/// <summary>
/// The custom bot editor's state (plan_hl2mp_ai §3.5): the bot being edited
/// as an <see cref="EditHistory"/> (undo/redo of point edits, the saved
/// revisions and restore), its name, what it makes of the parameters, the
/// profiles it can load, and Save / Save as through the store. Every edit is
/// judged by the pool before it lands, so the editor never holds an
/// overspent profile and Save can never write one (the server judges again).
/// </summary>
[SideService(Side.Client, As = [typeof(IBotEditorSource), typeof(IBotEditorActions)])]
public sealed class BotEditorModel(BotProfileStore store, CustomBots customs, ClientSkillCurves curves, TimeProvider time)
    : IBotEditorSource, IBotEditorActions
{
    private EditHistory? _history;
    private string _name = "";
    private SkillProfile? _shown;
    private BotSkillRow[] _rows = [];
    private (string First, string Second) _style = ("", "");
    private BotRevisionRow[] _revisions = [];
    private EditHistory? _revisionsOf;
    private int _revisionsLast;
    private BotProfileChoice[] _choices = [];

    private EditHistory History => _history ??= new EditHistory(store.Pool, Medium(), []);

    private SkillProfile Current => History.Current;

    /// <summary>The Medium preset, unnamed: where a new bot starts.</summary>
    private static SkillProfile Medium()
    {
        SkillPresets.TryGet("normal", out SkillProfile? medium);
        return medium!.Named("");
    }

    /// <summary>Starts editing custom bot <paramref name="index"/>, or a new one from the Medium preset for -1.</summary>
    public void Begin(int index)
    {
        customs.Editing = index;
        CustomBot? bot = (uint)index < (uint)customs.Count ? customs[index] : null;
        _name = bot?.Profile.Name ?? "";
        _history = new EditHistory(store.Pool, bot?.Profile ?? Medium(), bot?.Revisions ?? []);
        Unsaved = bot is null;
        Panel = BotEditorPanel.None;
        NameDraft = _name;
        StatusToken = "";
        StatusData = "";
        _shown = null;
    }

    public string ProfileName => _name;

    public int Pool => store.Pool.Points;

    public int Cap => store.Pool.Cap;

    public int Spent => Current.Spent;

    public int PointsLeft => History.PointsLeft;

    public int SkillCount => Current.Levels.Length;

    public BotSkillRow Skill(int i)
    {
        Rebuild();
        return _rows[i];
    }

    public string StyleFirstToken
    {
        get
        {
            Rebuild();
            return _style.First;
        }
    }

    public string StyleSecondToken
    {
        get
        {
            Rebuild();
            return _style.Second;
        }
    }

    public bool CanUndo => History.CanUndo;

    public bool CanRedo => History.CanRedo;

    public bool Unsaved { get; private set; } = true;

    public bool CanSave => _name.Length > 0 && store.Pool.Judge(Current).Accepted;

    public BotEditorPanel Panel { get; private set; }

    public int RevisionCount => History.Revisions.Count;

    public BotRevisionRow Revision(int i)
    {
        IReadOnlyList<ProfileRevision> all = History.Revisions;
        int last = all.Count > 0 ? all[^1].Number : 0;
        if (!ReferenceEquals(_revisionsOf, History) || _revisionsLast != last || _revisions.Length != all.Count)
        {
            _revisionsOf = History;
            _revisionsLast = last;
            _revisions = new BotRevisionRow[all.Count];
            for (int k = 0; k < all.Count; k++)
            {
                ProfileRevision r = all[all.Count - 1 - k];
                DateTimeOffset local = TimeZoneInfo.ConvertTime(r.At, time.LocalTimeZone);
                _revisions[k] = new BotRevisionRow(r.Number, local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.Note, r.Levels.Sum());
            }
        }

        return _revisions[i];
    }

    public int ChoiceCount => _choices.Length;

    public BotProfileChoice Choice(int i) => _choices[i];

    public string NameDraft { get; private set; } = "";

    public string NameRefusalToken
        => ProfileFile.NameRefusal(NameDraft) is not null ? "#Ai_Name_Invalid"
         : BotProfileStore.IsBuiltIn(NameDraft) ? "#Ai_Name_BuiltIn"
         : !string.Equals(NameDraft, _name, StringComparison.Ordinal) && store.SavedNames().Contains(NameDraft, StringComparer.OrdinalIgnoreCase)
            ? "#Ai_Name_Taken"
         : "";

    public string StatusToken { get; private set; } = "";

    public string StatusData { get; private set; } = "";

    public void Stage(int skill, int delta)
    {
        SkillVerdict verdict = History.Stage(skill, delta);
        if (verdict.Accepted)
        {
            Unsaved = true;
            Status("", "");
        }
        else
        {
            Status("#Ai_Status_Refused", verdict.Refusal);
        }
    }

    public void Undo()
    {
        if (History.Undo())
            Unsaved = true;
    }

    public void Redo()
    {
        if (History.Redo())
            Unsaved = true;
    }

    public void Save()
    {
        if (_name.Length == 0)
        {
            ShowPanel(BotEditorPanel.SaveAs);
            return;
        }

        History.Save(time.GetUtcNow(), History.Revisions.Count == 0 ? "first" : "saved");
        Write("#Ai_Status_Saved");
    }

    public void SaveAs()
    {
        if (NameRefusalToken.Length > 0)
            return;
        _name = NameDraft;
        History.Save(time.GetUtcNow(), $"saved as {_name}");
        if (Write("#Ai_Status_Saved"))
            Panel = BotEditorPanel.None;
    }

    public void SetNameDraft(string name) => NameDraft = name;

    public void ShowPanel(BotEditorPanel panel)
    {
        Panel = Panel == panel ? BotEditorPanel.None : panel;
        if (Panel == BotEditorPanel.Load)
            ListChoices();
        if (Panel == BotEditorPanel.SaveAs)
            NameDraft = _name;
    }

    public void Load(int choice)
    {
        if ((uint)choice >= (uint)_choices.Length)
            return;
        BotProfileChoice c = _choices[choice];
        SkillVerdict verdict = store.Load(c.Name, out BotProfileDocument? doc);
        if (!verdict.Accepted)
        {
            Status("#Ai_Status_Refused", verdict.Refusal);
            return;
        }

        if (c.BuiltIn)
        {
            // A preset is a starting point: its levels, as one undoable edit.
            History.Apply(doc!.Profile.Levels);
        }
        else
        {
            // A saved profile is that profile: its name and its revisions.
            _name = doc!.Profile.Name;
            _history = new EditHistory(store.Pool, doc.Profile, doc.Revisions);
        }

        Unsaved = c.BuiltIn;
        Panel = BotEditorPanel.None;
        Status("#Ai_Status_Loaded", c.Name);
    }

    public void Restore(int number)
    {
        History.Restore(number, time.GetUtcNow());
        Unsaved = true;
        if (_name.Length > 0)
            Write("#Ai_Status_Restored");
    }

    public void Done()
    {
        var bot = new CustomBot(Current.Named(_name), [.. History.Revisions]);
        int i = customs.Editing;
        if ((uint)i < (uint)customs.Count)
            customs.Replace(i, bot);
        else
            customs.Add(bot);
        customs.Editing = -1;
        Panel = BotEditorPanel.None;
    }

    private bool Write(string token)
    {
        SkillVerdict verdict = store.Save(Current.Named(_name), History.Revisions);
        if (!verdict.Accepted)
        {
            Status("#Ai_Status_Refused", verdict.Refusal);
            return false;
        }

        Unsaved = false;
        Status(token, _name);
        return true;
    }

    private void Status(string token, string data)
    {
        StatusToken = token;
        StatusData = data;
    }

    private void ListChoices()
    {
        var list = new List<BotProfileChoice>();
        foreach (string preset in SkillPresets.Names)
        {
            SkillPresets.TryGet(preset, out SkillProfile? p);
            list.Add(new BotProfileChoice(preset, p!.Spent, true));
        }

        foreach (string name in store.SavedNames())
        {
            if (store.Load(name, out BotProfileDocument? doc).Accepted)
                list.Add(new BotProfileChoice(name, doc!.Profile.Spent, false));
        }

        _choices = [.. list];
    }

    /// <summary>The rows, rebuilt only when the profile changed (an edit makes a new one).</summary>
    private void Rebuild()
    {
        SkillProfile p = Current;
        if (ReferenceEquals(p, _shown))
            return;
        _shown = p;
        BotParameter[][] derived = SkillDerived.Parameters(p, curves.Curves);
        int left = History.PointsLeft;
        _rows = new BotSkillRow[p.Levels.Length];
        for (int i = 0; i < _rows.Length; i++)
        {
            int level = p.Levels[i];
            _rows[i] = new BotSkillRow(SkillTokens.Of(i), level, level < Cap && left > 0, level > 0, derived[i]);
        }

        _style = SkillTokens.Style(p);
    }
}
