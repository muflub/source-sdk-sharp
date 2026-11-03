using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The key bindings (stock: GameUI's COptionsSubKeyboard): each row a command
/// from the mod's key actions (<see cref="IUiKeyActions"/>), so each game
/// lists its own, under its own headings; its
/// field the key bound to it. Clicked, a field takes the next key or mouse
/// button; Esc gives up, Delete clears. The form that proves the key capture
/// kind (plan_client_ui §6).
/// </summary>
public sealed class KeyBindings(UiWindows windows, IUiKeyBindingStore keys, IUiKeyActions actions)
    : UiForm<KeyBindings.Model>(windows, null, keys)
{
    /// <summary>
    /// The rows, known only at run time, so written by hand where a form of
    /// fixed fields has them generated. Every field is a key capture.
    /// </summary>
    public sealed class Model : IUiFormModel<Model>
    {
        private UiFieldInfo[] _fields = [];
        private KeyBinding[] _keys = [];

        internal void Rows(IReadOnlyList<UiKeyAction> rows)
        {
            _fields = new UiFieldInfo[rows.Count];
            _keys = new KeyBinding[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                _fields[i] = new UiFieldInfo(rows[i].Command, rows[i].Label, UiFieldKind.Capture, 0f, 0f, 0f, rows[i].Section, 0, false);
        }

        /// <summary>The row of a command, or -1.</summary>
        public int Row(string command) => Array.FindIndex(_fields, f => f.Name == command);

        public int FieldCount => _fields.Length;

        public UiFieldInfo Field(int i) => _fields[i];

        public KeyBinding GetKey(int i) => _keys[i];

        public void SetKey(int i, KeyBinding value) => _keys[i] = value;

        public void CopyFrom(Model other) => other._keys.CopyTo(_keys, 0);

        public bool Same(int i, Model other) => _keys[i] == other._keys[i];

        public bool GetBool(int i) => throw NotThatKind(i);

        public void SetBool(int i, bool value) => throw NotThatKind(i);

        public float GetNumber(int i) => throw NotThatKind(i);

        public void SetNumber(int i, float value) => throw NotThatKind(i);

        public int GetChoice(int i) => throw NotThatKind(i);

        public void SetChoice(int i, int value) => throw NotThatKind(i);

        public string ChoiceName(int i, int choice) => throw NotThatKind(i);

        public string GetText(int i) => throw NotThatKind(i);

        public void SetText(int i, string value) => throw NotThatKind(i);

        private InvalidOperationException NotThatKind(int i) => new($"{_fields[i].Name} is a key capture");
    }

    public override string Name => "keybindings";

    public override string WindowId => "keybindings";

    protected override string TitleToken => "#SS_UI_Keys_Title";

    public override string TabToken => "#SS_UI_Keys_Tab";

    protected override UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(640, screenW), MathF.Min(720, screenH), 420, 320, screenW, screenH, 520);

    protected override void Bind(UiFormBindings<Model> b)
    {
        var rows = actions.Actions();
        Saved.Rows(rows);
        Working.Rows(rows);
        for (int i = 0; i < rows.Count; i++)
            b.Key(i, rows[i].Command);
    }

    private bool _confirming;
    private long _footerFrame = -1;

    /// <summary>
    /// Every row back to the key a fresh player gets, as stock's Use Defaults: into the
    /// form, for Apply or OK to write and Cancel to drop. A command with several keys
    /// shows the first, a key on several rows stays with the last, and a key no row names
    /// is left alone. The console and Escape are always given their keys.
    /// </summary>
    public void UseDefaults()
    {
        var m = Working;
        for (int i = 0; i < m.FieldCount; i++)
            m.SetKey(i, default);
        foreach (var (key, command) in actions.DefaultBinds)
            Give(m, command, key, keepFirst: true);
        Give(m, "toggleconsole", "`", keepFirst: false);
        Give(m, "cancelselect", "ESCAPE", keepFirst: false);
    }

    private static void Give(Model m, string command, string key, bool keepFirst)
    {
        int row = m.Row(command);
        if (row < 0 || (keepFirst && m.GetKey(row).IsBound))
            return;
        for (int j = 0; j < m.FieldCount; j++)
            if (string.Equals(m.GetKey(j).Key, key, StringComparison.OrdinalIgnoreCase))
                m.SetKey(j, default);
        // Upper case, as the store names keys, so a key already on its default is no change.
        m.SetKey(row, new KeyBinding(key.ToUpperInvariant()));
    }

    /// <summary>The button pressed: the page asks before anything changes.</summary>
    public void AskUseDefaults() => _confirming = true;

    protected override void DescribeFooter(ref UiFrame ui)
    {
        // A page just opened asks nothing it was asked last time.
        if (_footerFrame >= 0 && ui.FrameNumber != _footerFrame + 1)
            _confirming = false;
        _footerFrame = ui.FrameNumber;

        using (ui.Column(Space.S, Align.Start).Width(Size.Fill).Name("keysfooter"))
        {
            if (!_confirming)
            {
                if (ui.Button("keysdefaults", "#SS_UI_Keys_UseDefaults").Clicked)
                    AskUseDefaults();
                return;
            }
            ui.Text("#SS_UI_Keys_UseDefaultsAsk", Text.Body, Tone.Text).Wrap(KeysAskWrapDu);
            using (ui.Row(Space.S))
            {
                if (ui.Button("keysdefaultsno", "#SS_UI_Keys_UseDefaultsNo").Clicked)
                    _confirming = false;
                if (ui.Button("keysdefaultsyes", "#SS_UI_Keys_UseDefaults").Clicked)
                {
                    _confirming = false;
                    UseDefaults();
                }
            }
        }
    }

    private const float KeysAskWrapDu = 380;
}
