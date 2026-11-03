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
}
