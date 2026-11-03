using System.Globalization;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// An options script's entries as rows of a form grid (stock's advanced
/// multiplayer dialog and the create-server game page, which both list a
/// CDescription): a category a heading, a bool a toggle, a slider a slider, a
/// list a choice, a button a button, the rest a text field. Prompts are
/// tokens of the mod's language file or the script's own words (shown as
/// data). Entries named as drawn elsewhere are kept and saved, not shown.
/// The screen owning the rows reads and saves the script; the rows hold the
/// entries and their working values.
/// </summary>
public sealed class ScriptRows(params string[] drawnElsewhere)
{
    private const float LabelColumnDu = UiForm<MouseOptions.Model>.LabelColumnDu;

    // A row's label is keyed keyBase + i, its control column ControlKey past it.
    private const int ControlKey = 1 << 18;

    private IReadOnlyList<UiScriptEntry> _entries = [];
    private readonly List<List<string>> _itemNames = [];
    private string[] _saved = [];
    private string[] _values = [];

    public IReadOnlyList<UiScriptEntry> Entries => _entries;

    /// <summary>The working values, by entry.</summary>
    public string[] Values => _values;

    /// <summary>Whether any entry is shown here (some may all be drawn elsewhere).</summary>
    public bool AnyShown
    {
        get
        {
            // By index: a foreach over the interface would box its enumerator every frame.
            for (int i = 0; i < _entries.Count; i++)
                if (Shown(_entries[i]))
                    return true;
            return false;
        }
    }

    public bool AnyDirty
    {
        get
        {
            for (int i = 0; i < _values.Length; i++)
                if (!string.Equals(_values[i], _saved[i], StringComparison.Ordinal))
                    return true;
            return false;
        }
    }

    /// <summary>An entry's working value by convar name, or null when the script has none.</summary>
    public string? Value(string name)
    {
        int i = IndexOf(name);
        return i >= 0 ? _values[i] : null;
    }

    /// <summary>Sets an entry's working value by convar name, when the script has it.</summary>
    public void Set(string name, string value)
    {
        int i = IndexOf(name);
        if (i >= 0)
            _values[i] = value;
    }

    /// <summary>The entry named, if the script has it.</summary>
    public UiScriptEntry? Entry(string name)
    {
        int i = IndexOf(name);
        return i >= 0 ? _entries[i] : null;
    }

    /// <summary>The script's entries, their values taken as saved.</summary>
    public void Load(IReadOnlyList<UiScriptEntry> entries)
    {
        _entries = entries;
        _itemNames.Clear();
        _saved = new string[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
        {
            var names = new List<string>(_entries[i].Items.Count);
            foreach (var item in _entries[i].Items)
                names.Add(item.Label);
            _itemNames.Add(names);
            _saved[i] = _entries[i].Value;
        }
        _values = (string[])_saved.Clone();
    }

    /// <summary>The working values, once the owner has saved them, taken as saved.</summary>
    public void TakeSaved() => _saved = (string[])_values.Clone();

    /// <summary>The shown entries' rows, into the enclosing form grid: answers the button entry pressed, or -1.</summary>
    public int Describe(ref UiFrame ui, int keyBase)
    {
        int pressed = -1;
        for (int i = 0; i < _entries.Count; i++)
            if (Shown(_entries[i]) && Row(ref ui, i, keyBase + i))
                pressed = i;
        return pressed;
    }

    private bool Shown(UiScriptEntry e) => Array.IndexOf(drawnElsewhere, e.Name) < 0;

    private int IndexOf(string name)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // Answers whether the row was a button that was pressed.
    private bool Row(ref UiFrame ui, int i, int key)
    {
        var e = _entries[i];
        bool token = e.Prompt.StartsWith('#');
        if (e.Kind == UiScriptKind.Category)
        {
            (token ? ui.Text(e.Prompt, Text.Label, Tone.Accent) : ui.Data(e.Prompt, Text.Label, Tone.Accent)).Key(key);
            ui.Spacer(Size.Du(1), Size.Du(1)).Key(ControlKey + key);
            return false;
        }
        // A button carries its prompt itself; its label cell stays empty.
        if (e.Kind == UiScriptKind.Button)
            ui.Spacer(Size.Du(1), Size.Du(1)).Key(key);
        else
            (token ? ui.Text(e.Prompt, Text.Body, Tone.Text) : ui.Data(e.Prompt, Text.Body, Tone.Text)).Wrap(LabelColumnDu).Key(key);
        bool pressed = false;
        using (ui.Column(Space.XS, Align.Start).Key(ControlKey + key).Width(Size.Fill))
        {
            switch (e.Kind)
            {
                case UiScriptKind.Bool:
                    bool on = ui.Toggle(e.Name, Number(_values[i]) != 0f);
                    if (on != (Number(_values[i]) != 0f))
                        _values[i] = on ? "1" : "0";
                    break;
                case UiScriptKind.Slider:
                    float v = Number(_values[i]);
                    float set = ui.Slider(e.Name, v, e.Min, e.Max, (e.Max - e.Min) / 100f);
                    if (set != v)
                        _values[i] = set.ToString("0.##", CultureInfo.InvariantCulture);
                    break;
                case UiScriptKind.List:
                    int chosen = IndexOfItem(e, _values[i]);
                    int picked = ui.Choice(e.Name, chosen, e.Items.Count, new UiChoiceList(_itemNames[i]), tokens: true);
                    if (picked != chosen && (uint)picked < (uint)e.Items.Count)
                        _values[i] = e.Items[picked].Value;
                    break;
                case UiScriptKind.Button:
                    pressed = ui.Button(e.Name, e.Prompt, data: !token).Clicked;
                    break;
                default:
                    string text = ui.EditText(e.Name, _values[i], "#SS_UI_Form_TextPlaceholder");
                    if (!ReferenceEquals(text, _values[i]))
                        _values[i] = text;
                    break;
            }
        }
        return pressed;
    }

    private static int IndexOfItem(UiScriptEntry e, string value)
    {
        for (int i = 0; i < e.Items.Count; i++)
            if (e.Items[i].Value.Equals(value, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    public static float Number(string? text)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
}
