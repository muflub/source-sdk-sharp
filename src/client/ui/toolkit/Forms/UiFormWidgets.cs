using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The interactive fields a form draws (plan_client_ui §6): each takes the
/// value it shows and answers the value after this frame's input. They hold no
/// value of their own; what is open (a choice's list) is keyed state.
/// </summary>
public static partial class UiFormWidgets
{
    /// <summary>
    /// A slider: a track, a fill and a thumb. Pressed, it follows the mouse
    /// across the track; focused, Left and Right step it. Snapped to
    /// <paramref name="step"/> and kept inside min..max.
    /// </summary>
    public static float Slider(this ref UiFrame ui, string key, float value, float min, float max, float step,
                               Tone tone = Tone.Accent)
    {
        float span = max - min;
        var track = ui.Box(key).Size(Size.Fill, Size.Du(32));
        var hit = ui.Interact(track);
        bool focused = ui.IsFocused(track);

        if (hit.Pressed && ui.TryLastRect(track, out var r) && r.W > 0 && span > 0)
            value = min + Math.Clamp((ui.MouseX - r.X) / r.W, 0f, 1f) * span;
        if (focused && ui.KeyPressed((int)UiKey.Left))
            value -= step > 0 ? step : span / 100f;
        if (focused && ui.KeyPressed((int)UiKey.Right))
            value += step > 0 ? step : span / 100f;
        value = Snap(value, min, max, step);

        float f = span > 0 ? (value - min) / span : 0f;
        using (track)
        {
            using (ui.Box().Size(Size.Fill, Size.Du(8)).At(UiAnchor.Left).Fill(Tone.SurfaceRaised).Round(Radius.S).Layered())
                ui.Rect(tone, Size.Percent(f * 100f), Size.Fill).Round(Radius.S).At(UiAnchor.Left);
            var thumb = ui.Rect(focused || hit.Hovered ? Tone.Text : tone, Size.Du(12), Size.Du(24)).Round(Radius.S)
                          .At(UiAnchor.Left).PlaceFraction(f, 0.5f).Layered();
            // A handle: where it sits is the value, so the alignment rule does not apply.
            ui.Context.Node(thumb.Index).Handle = true;
        }
        return value;
    }

    /// <summary>
    /// A toggle with no label of its own (the form's label column says what it
    /// is): a box that shows a check when on. Answers the value after a click.
    /// </summary>
    public static bool Toggle(this ref UiFrame ui, string key, bool on)
    {
        var box = ui.Box(key).Size(Size.Du(32), Size.Du(32));
        var hit = ui.Interact(box);
        if (hit.Clicked || (ui.IsFocused(box) && ui.KeyPressed((int)UiKey.Space)))
            on = !on;
        using (box)
        using (ui.Box().Size(Size.Du(20), Size.Du(20)).At(UiAnchor.Centre).Round(Radius.S)
                 .Fill(on ? Tone.Accent : Tone.Surface).Border(on ? Tone.Accent : hit.Hovered ? Tone.Text : Tone.TextMuted, 1))
        {
            if (on)
                ui.Shape(UiShape.Check, Tone.OnAccent, 16).At(UiAnchor.Centre);
        }
        return on;
    }

    /// <summary>
    /// A key capture: the key's name in a field. Clicked, it is armed and says
    /// so; armed, the next key or mouse button down (the wheel as its two fake
    /// buttons) is answered as its ButtonCode_t, Delete or Backspace as -1
    /// (clear), Escape as nothing. Otherwise 0. <paramref name="state"/> is the
    /// caller's: 0 idle, 1 armed, 2 took a mouse button pressed on the field,
    /// whose release is then not a click that arms it again.
    /// </summary>
    public static int KeyCapture(this ref UiFrame ui, string key, string? shown, ref byte state)
    {
        int taken = 0;
        if (state == 1)
        {
            foreach (int code in ui.ButtonsPressed)
            {
                state = 0;
                if (code == (int)UiKey.Delete || code == (int)UiKey.Backspace)
                    taken = -1;
                else if (code != (int)UiKey.Escape)
                {
                    taken = code;
                    if (code is >= (int)UiKey.MouseLeft and <= MouseLastButton)
                        state = 2;
                }
                break;
            }
        }

        bool armed = state == 1;
        var box = ui.Row(Space.S).Key(key).Size(Size.Fill, Size.Du(40)).Fill(armed ? Tone.SurfaceRaised : Tone.Surface)
                    .Round(Radius.M).Border(armed ? Tone.Accent : Tone.Line, armed ? 2 : 1).Pad(Space.M, Space.None);
        var hit = ui.Interact(box);
        if (hit.Clicked)
            state = state == 2 ? (byte)0 : (byte)1;
        else if (state == 2 && !ui.Holding)
            state = 0;

        using (box)
        {
            if (armed)
                ui.Text("#SS_UI_Keys_Press", Text.Body, Tone.Accent);
            else if (string.IsNullOrEmpty(shown))
                ui.Text("#SS_UI_Keys_None", Text.Body, Tone.TextMuted);
            else
                ui.Data(shown, Text.Body);
        }
        return taken;
    }

    // ButtonCode.h MOUSE_5: the last real mouse button; the wheel's fake buttons follow it.
    private const int MouseLastButton = 111;

    /// <summary>A value inside min..max, on the step's grid from min.</summary>
    public static float Snap(float value, float min, float max, float step)
    {
        if (float.IsNaN(value))
            value = min;
        if (step > 0)
            value = min + MathF.Round((value - min) / step) * step;
        return Math.Clamp(value, min, max);
    }

    /// <summary>
    /// An editable text field: clicked, it takes the focus; focused, typed
    /// characters are appended and Backspace takes the last off. The caret sits
    /// at the end.
    /// </summary>
    public static string EditText(this ref UiFrame ui, string key, string text, string placeholderToken)
    {
        var box = ui.Box(key).Size(Size.Fill, Size.Du(40));
        ui.Interact(box);
        bool focused = ui.IsFocused(box);
        if (focused)
        {
            var typed = ui.Typed;
            if (ui.KeyPressed((int)UiKey.Backspace) && text.Length > 0)
                text = text[..^1];
            int printable = 0;
            foreach (char c in typed)
                if (!char.IsControl(c))
                    printable++;
            if (printable > 0)
            {
                Span<char> add = stackalloc char[printable];
                int k = 0;
                foreach (char c in typed)
                    if (!char.IsControl(c))
                        add[k++] = c;
                text = string.Concat(text.AsSpan(), add);
            }
        }
        using (box)
            // The field draws; this box takes the click, so the focus a click
            // gives is the one checked above (an interactive inner field took it).
            ui.TextField(null, text, placeholderToken, focused ? text.Length : -1, interactive: false);
        return text;
    }
}

/// <summary>A choice's names by index, without a delegate or an array per frame.</summary>
public interface IUiChoiceNames
{
    string Name(int i);
}

/// <summary>Choice names from a list built outside a describe (an options page's run-time list).</summary>
public readonly struct UiChoiceList(IReadOnlyList<string> names) : IUiChoiceNames
{
    public int Count => names.Count;

    public string Name(int i) => (uint)i < (uint)names.Count ? names[i] : "";
}
