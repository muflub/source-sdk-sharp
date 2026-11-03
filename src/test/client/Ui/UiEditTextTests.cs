using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The editable text field (<see cref="UiFormWidgets.EditText"/>): a click on
/// it takes the focus, and the characters typed then are its text. The
/// HL2MP AI's create-server map field and the bot editor's Save as name are
/// the first fields typed into in game; the field's inner box took the
/// click's focus and the field never saw it (found live, 2026-11-02).
/// </summary>
public class UiEditTextTests
{
    private readonly UiContext _ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
    private string _text = "";
    private double _now = 1.0;

    private void Frame()
    {
        UiTestKit.Frame(_ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column(Space.L).Width(Size.Du(400)))
                _text = ui.EditText("name", _text, "#SS_UI_Form_TextPlaceholder");
        }, _now);
        _now += 1 / 60.0;
    }

    private void Click(float x, float y)
    {
        _ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        _ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Frame();
        _ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Frame();
    }

    private void Type(string s)
    {
        foreach (char c in s)
            _ctx.Input(new UiInputEvent(UiInputKind.Char, 0, 0, 0, c, 0));
        Frame();
    }

    [Fact]
    public void A_clicked_field_takes_what_is_typed()
    {
        Frame();
        var field = UiTestKit.Named(_ctx, "name");
        Click(field.Rect.X + field.Rect.W / 2, field.Rect.Y + field.Rect.H / 2);
        Type("Deadeye");
        Assert.Equal("Deadeye", _text);
    }

    [Fact]
    public void An_unclicked_field_takes_nothing()
    {
        Frame();
        Type("Deadeye");
        Assert.Equal("", _text);
    }
}
