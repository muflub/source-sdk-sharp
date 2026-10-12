using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A FORM (plan_client_ui §6): a window whose body is a model's fields, drawn
/// from what the generator says of them, bound to convars or to the form's own
/// code. Opened, it loads the bindings into a saved copy and a working copy;
/// editing changes the working copy (a field differing from the saved one is
/// dirty); Apply writes the dirty fields and takes them as saved; OK applies and
/// closes; Cancel discards and closes. A field whose <see cref="Validate"/>
/// answers a message shows it under itself, and Apply and OK are disabled while
/// any does.
/// </summary>
public abstract class UiForm<TModel> : UiScreen, IUiWindowed
    where TModel : class, IUiFormModel<TModel>, new()
{
    private readonly UiWindows _windows;
    private readonly UiFormBindings<TModel> _bindings;
    private readonly ChoiceNames _names = new();
    private long _lastFrame = long.MinValue;

    // Each key field's capture state (KeyCapture's): 0 idle, 1 armed, 2 just took a mouse button.
    private byte[] _capture = [];

    // A field's label row is keyed by its index, its control column by this
    // plus the index, so the two never share an id.
    private const int FieldKeyBase = 10000;

    protected UiForm(UiWindows windows, IUiConVarStore convars)
        : this(windows, convars, null)
    {
    }

    /// <summary>A form over key bindings, and convars if it has any.</summary>
    protected UiForm(UiWindows windows, IUiConVarStore? convars, IUiKeyBindingStore? keys)
    {
        _windows = windows;
        _bindings = new UiFormBindings<TModel>(convars, keys);
    }

    /// <summary>The fields as they were loaded or last applied.</summary>
    public TModel Saved { get; } = new();

    /// <summary>The fields as edited.</summary>
    public TModel Working { get; } = new();

    public abstract string WindowId { get; }

    /// <summary>The window's title token.</summary>
    protected abstract string TitleToken { get; }

    /// <summary>The window's sizes, in design units, against the screen's.</summary>
    protected virtual UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(640, screenW), MathF.Min(520, screenH), 420, 320, screenW, screenH, 520);

    /// <summary>Where each field comes from and goes to; called once, before the first load.</summary>
    protected abstract void Bind(UiFormBindings<TModel> bindings);

    /// <summary>A field's problem, as a token to show under it, or null when it is fine.</summary>
    protected virtual string? Validate(int field, TModel model) => null;

    private bool _bound;

    private UiFormBindings<TModel> Bindings
    {
        get
        {
            if (!_bound)
            {
                _bound = true;
                Bind(_bindings);
            }
            return _bindings;
        }
    }

    public bool IsDirty(int field) => !Working.Same(field, Saved);

    public bool AnyDirty
    {
        get
        {
            for (int i = 0; i < Working.FieldCount; i++)
                if (IsDirty(i))
                    return true;
            return false;
        }
    }

    public bool IsValid
    {
        get
        {
            for (int i = 0; i < Working.FieldCount; i++)
                if (Validate(i, Working) is not null)
                    return false;
            return true;
        }
    }

    /// <summary>From the bindings into both copies.</summary>
    public void Load()
    {
        Bindings.Load(Saved);
        Working.CopyFrom(Saved);
        if (_capture.Length != Working.FieldCount)
            _capture = new byte[Working.FieldCount];
        Array.Clear(_capture);
    }

    /// <summary>The dirty fields to their bindings, then taken as saved. Nothing while any field is invalid.</summary>
    public bool Apply()
    {
        if (!IsValid)
            return false;
        // Every changed key field's old key first, so a key moved between two
        // commands is never unbound after it was bound.
        for (int i = 0; i < Working.FieldCount; i++)
            if (IsDirty(i))
                Bindings.Release(Saved, i);
        for (int i = 0; i < Working.FieldCount; i++)
            if (IsDirty(i))
                Bindings.Save(Working, i);
        Saved.CopyFrom(Working);
        return true;
    }

    public void Ok()
    {
        if (Apply())
            _windows.RequestClose(WindowId);
    }

    public void Cancel()
    {
        Working.CopyFrom(Saved);
        _windows.RequestClose(WindowId);
    }

    public override void Describe(ref UiFrame ui)
    {
        // A form not described last frame has just been opened: it starts from
        // what the bindings hold now.
        if (ui.FrameNumber != _lastFrame + 1)
            Load();
        _lastFrame = ui.FrameNumber;

        // Esc while a key field is armed gives up the capture; it does not close the window.
        if (Array.IndexOf(_capture, (byte)1) >= 0 && ui.ConsumeKey((int)UiKey.Escape))
            Array.Clear(_capture);

        var m = Working;
        using (ui.Window(_windows, WindowId, TitleToken, Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            using (ui.Scroll("fields", Space.M).Size(Size.Fill, Size.Fill))
            using (ui.FormGrid(220).Width(Size.Fill))
            {
                string? section = null;
                for (int i = 0; i < m.FieldCount; i++)
                {
                    var f = m.Field(i);
                    if (f.Section is { } s && s != section)
                    {
                        section = s;
                        ui.Text(s, Text.Label, Tone.Accent).Key(-1 - i);
                        ui.Spacer(Size.Du(1), Size.Du(1)).Key(-1000 - i);
                    }
                    Row(ref ui, i, f, m);
                }
            }

            bool valid = IsValid;
            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                    Cancel();
                ui.Flex();
                if (ui.Button("apply", "#SS_UI_Apply", enabled: valid && AnyDirty).Clicked)
                    Apply();
                if (ui.Button("ok", "#SS_UI_Ok", primary: true, enabled: valid).Clicked)
                    Ok();
            }
        }
    }

    private void Row(ref UiFrame ui, int i, in UiFieldInfo f, TModel m)
    {
        using (ui.Row(Space.XS).Key(i))
        {
            ui.Text(f.LabelToken, Text.Body, Tone.Text);
            if (IsDirty(i))
                ui.Rect(Tone.Accent, Size.Du(8), Size.Du(8)).Round(Radius.S).Name("dirty");
        }

        using (ui.Column(Space.XS, Align.Start).Key(FieldKeyBase + i).Width(Size.Fill))
        {
            switch (f.Kind)
            {
                case UiFieldKind.Toggle:
                    bool on = ui.Toggle(f.Name, m.GetBool(i));
                    if (on != m.GetBool(i))
                        m.SetBool(i, on);
                    break;
                case UiFieldKind.Slider:
                    using (ui.Row(Space.M).Width(Size.Fill))
                    {
                        float v = ui.Slider(f.Name, m.GetNumber(i), f.Min, f.Max, f.Step);
                        if (v != m.GetNumber(i))
                            m.SetNumber(i, v);
                        if (f.Whole)
                            ui.Number((int)MathF.Round(m.GetNumber(i)), Text.Body, Tone.Text).MinWidth(56);
                        else if (f.Step >= 0.1f)
                        {
                            // Tenths as two whole numbers: the frame's text handler formats
                            // an int without allocating and a float with a format string does not.
                            int tenths = (int)MathF.Round(m.GetNumber(i) * 10f);
                            ui.Text($"{(tenths < 0 ? "-" : "")}{Math.Abs(tenths) / 10}.{Math.Abs(tenths) % 10}", Text.Body, Tone.Text).MinWidth(56);
                        }
                        else
                        {
                            int hundredths = (int)MathF.Round(m.GetNumber(i) * 100f);
                            ui.Text($"{(hundredths < 0 ? "-" : "")}{Math.Abs(hundredths) / 100}.{Math.Abs(hundredths) % 100:00}", Text.Body, Tone.Text).MinWidth(56);
                        }
                    }
                    break;
                case UiFieldKind.Choice:
                    _names.Model = m;
                    _names.Field = i;
                    int c = ui.Choice(f.Name, m.GetChoice(i), f.Choices, _names);
                    if (c != m.GetChoice(i))
                        m.SetChoice(i, c);
                    break;
                case UiFieldKind.Capture:
                {
                    bool wasArmed = _capture[i] == 1;
                    int code = ui.KeyCapture(f.Name, m.GetKey(i).Key, ref _capture[i]);
                    if (!wasArmed && _capture[i] == 1)
                    {
                        // One field armed at a time.
                        Array.Clear(_capture);
                        _capture[i] = 1;
                    }
                    if (code != 0)
                        TakeKey(m, i, code);
                    break;
                }
                default:
                    string t = ui.EditText(f.Name, m.GetText(i), "#SS_UI_Form_TextPlaceholder");
                    if (!ReferenceEquals(t, m.GetText(i)))
                        m.SetText(i, t);
                    break;
            }
            if (Validate(i, m) is { } problem)
                ui.Text(problem, Text.Caption, Tone.Danger).Name("invalid");
        }
    }

    // A key taken by field i, or cleared (-1). A key holds one command, so a
    // row that had it is left empty, as stock's keyboard options do.
    private void TakeKey(TModel m, int i, int code)
    {
        if (code < 0)
        {
            m.SetKey(i, default);
            return;
        }
        if (Bindings.KeyName(code) is not { Length: > 0 } name)
            return;
        for (int j = 0; j < m.FieldCount; j++)
            if (j != i && m.Field(j).Kind == UiFieldKind.Capture
                && string.Equals(m.GetKey(j).Key, name, StringComparison.OrdinalIgnoreCase))
                m.SetKey(j, default);
        m.SetKey(i, new KeyBinding(name));
    }

    private sealed class ChoiceNames : IUiChoiceNames
    {
        public TModel? Model;
        public int Field;

        public string Name(int i) => Model!.ChoiceName(Field, i);
    }
}
