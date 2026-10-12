using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A form's model (plan_client_ui §6): a partial class whose <see cref="FieldAttribute"/>
/// fields the form generator turns into <see cref="IUiFormModel{TSelf}"/>, read once at
/// build time, so a frame never reflects and nothing static survives a reload.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UiFormModelAttribute : Attribute
{
}

/// <summary>
/// One field of a form model. Its kind comes from the C# type: bool a toggle,
/// an enum a choice, int or float a slider (Min and Max required), string a
/// text field, <see cref="KeyBinding"/> a key capture.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class FieldAttribute(string labelToken) : Attribute
{
    public string LabelToken { get; } = labelToken;

    public float Min { get; set; } = float.NaN;

    public float Max { get; set; } = float.NaN;

    /// <summary>A slider's step; 1 for an int when unset.</summary>
    public float Step { get; set; } = float.NaN;

    /// <summary>The section heading's token the field sits under, if any.</summary>
    public string? Section { get; set; }
}

/// <summary>What kind of control a field is drawn with.</summary>
public enum UiFieldKind : byte
{
    Toggle,
    Slider,
    Choice,
    Text,

    /// <summary>A key binding: clicked, the next key or button pressed is taken.</summary>
    Capture,
}

/// <summary>
/// The key a command is bound to, by the engine's name for it (<c>W</c>,
/// <c>SPACE</c>, <c>MOUSE1</c>), or none. A form field of this type is a key
/// capture (plan_client_ui §6).
/// </summary>
public readonly record struct KeyBinding(string? Key)
{
    public bool IsBound => !string.IsNullOrEmpty(Key);
}

/// <summary>One field as the generator describes it.</summary>
public readonly record struct UiFieldInfo(
    string Name, string LabelToken, UiFieldKind Kind, float Min, float Max, float Step, string? Section, int Choices,
    bool Whole);

/// <summary>
/// A form model's fields by index, typed accessors and copies: generated for a
/// <see cref="UiFormModelAttribute"/> class, never written by hand.
/// </summary>
public interface IUiFormModel<TSelf> where TSelf : IUiFormModel<TSelf>
{
    int FieldCount { get; }

    UiFieldInfo Field(int i);

    bool GetBool(int i);

    void SetBool(int i, bool value);

    float GetNumber(int i);

    void SetNumber(int i, float value);

    int GetChoice(int i);

    void SetChoice(int i, int value);

    /// <summary>A choice's name (an enum member's), as data.</summary>
    string ChoiceName(int i, int choice);

    string GetText(int i);

    void SetText(int i, string value);

    KeyBinding GetKey(int i);

    void SetKey(int i, KeyBinding value);

    void CopyFrom(TSelf other);

    /// <summary>Whether field <paramref name="i"/> holds the same value in both.</summary>
    bool Same(int i, TSelf other);
}
