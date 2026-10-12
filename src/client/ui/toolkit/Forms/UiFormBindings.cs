using System.Globalization;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// Where a form's fields come from and go to (plan_client_ui §6): a convar by
/// name, a command's key binding, or a custom pair of load and save. A field
/// with no binding is the model's alone.
/// </summary>
public sealed class UiFormBindings<TModel>(IUiConVarStore? convars, IUiKeyBindingStore? keys = null)
    where TModel : class, IUiFormModel<TModel>
{
    private readonly Dictionary<int, (Action<TModel> Load, Action<TModel> Save)> _byField = [];
    private readonly Dictionary<int, string> _commands = [];

    /// <summary>Field <paramref name="field"/> is convar <paramref name="name"/>, as text in its kind's spelling.</summary>
    public UiFormBindings<TModel> ConVar(int field, string name)
    {
        if (convars is null)
            throw new InvalidOperationException($"field {field} is bound to convar {name}, and this form has no convar store");
        _byField[field] = (m => LoadConVar(m, field, name), m => SaveConVar(m, field, name));
        return this;
    }

    /// <summary>Field <paramref name="field"/> loads and saves through the form's own code.</summary>
    public UiFormBindings<TModel> Custom(int field, Action<TModel> load, Action<TModel> save)
    {
        _byField[field] = (load, save);
        return this;
    }

    /// <summary>
    /// Key field <paramref name="field"/> is the key bound to <paramref name="command"/>.
    /// Saving binds the new key; the old one is released first, by <see cref="Release"/>.
    /// </summary>
    public UiFormBindings<TModel> Key(int field, string command)
    {
        var k = keys ?? throw new InvalidOperationException($"field {field} is bound to {command}, and this form has no key store");
        _commands[field] = command;
        _byField[field] = (m => m.SetKey(field, new KeyBinding(k.KeyFor(command))),
                           m => { if (m.GetKey(field).Key is { Length: > 0 } key) k.Bind(key, command); });
        return this;
    }

    /// <summary>
    /// A key field's key as <paramref name="saved"/> holds it, unbound: run for
    /// every changed key field before any is saved, so a key moved from one
    /// command to another is bound once and never unbound after.
    /// </summary>
    public void Release(TModel saved, int field)
    {
        if (keys is not null && _commands.ContainsKey(field) && saved.GetKey(field).Key is { Length: > 0 } key)
            keys.Unbind(key);
    }

    /// <summary>The engine's name for a button code, from the key store; null without one.</summary>
    public string? KeyName(int buttonCode) => keys?.KeyName(buttonCode);

    public bool Has(int field) => _byField.ContainsKey(field);

    /// <summary>Every bound field, from where it lives into <paramref name="model"/>.</summary>
    public void Load(TModel model)
    {
        foreach (var (_, b) in _byField)
            b.Load(model);
    }

    /// <summary>One field, from <paramref name="model"/> to where it lives.</summary>
    public void Save(TModel model, int field)
    {
        if (_byField.TryGetValue(field, out var b))
            b.Save(model);
    }

    private void LoadConVar(TModel m, int field, string name)
    {
        string? text = convars!.Get(name);
        if (text is null)
            return;
        var info = m.Field(field);
        switch (info.Kind)
        {
            case UiFieldKind.Toggle:
                m.SetBool(field, float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float b) ? b != 0f : text.Length > 0);
                break;
            case UiFieldKind.Slider:
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    m.SetNumber(field, v);
                break;
            case UiFieldKind.Choice:
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
                    m.SetChoice(field, c);
                break;
            default:
                m.SetText(field, text);
                break;
        }
    }

    private void SaveConVar(TModel m, int field, string name)
    {
        var info = m.Field(field);
        string text = info.Kind switch
        {
            UiFieldKind.Toggle => m.GetBool(field) ? "1" : "0",
            UiFieldKind.Slider => info.Whole
                ? ((int)MathF.Round(m.GetNumber(field))).ToString(CultureInfo.InvariantCulture)
                : m.GetNumber(field).ToString("0.###", CultureInfo.InvariantCulture),
            UiFieldKind.Choice => m.GetChoice(field).ToString(CultureInfo.InvariantCulture),
            _ => m.GetText(field),
        };
        convars!.Set(name, text);
    }
}
