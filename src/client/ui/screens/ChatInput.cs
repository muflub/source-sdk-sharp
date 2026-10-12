using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The chat's input line (stock messagemode / messagemode2): bottom left,
/// above the chat lines' place, "Say:" or "Say (team):" and the line with its
/// caret. It takes the keyboard while open; the menu root forwards the keys
/// and characters to the core's line, Enter sends, Esc closes.
/// </summary>
public sealed class ChatInput(IChatInputSource input) : UiScreen
{
    public override string Name => "chatinput";

    public override UiLayerKind Layer => UiLayerKind.Menu;

    public override bool WantsMouse => true;

    public override void Describe(ref UiFrame ui)
    {
        var p = input;
        float width = MathF.Min(652, ui.DesignWidth - 96);
        using (ui.Box().Size(Size.Fill, Size.Fill))
        using (ui.Panel(Space.M, Space.XS, Elevation.Flat).At(UiAnchor.BottomLeft)
                 .Inset(Space.XXL, Space.None).Width(Size.Du(width)).Pad(Space.M, Space.S))
        using (ui.Row(Space.M).Width(Size.Fill))
        {
            ui.Text(p.TeamOnly ? "#SS_UI_Chat_SayTeam" : "#SS_UI_Chat_Say", Text.Label, p.TeamOnly ? Tone.Accent : Tone.TextMuted);
            ui.TextField("line", p.Input, "#SS_UI_Chat_Placeholder", p.Caret, 0, 0, Size.Fill);
        }
    }
}
