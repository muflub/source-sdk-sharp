using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The centre print (stock: CCenterStringLabel, ICenterPrint): the server's
/// one message, upper third of the screen, on a surface so it reads over the
/// brightest wall, wrapped rather than run off a narrow screen, fading as the
/// game says.
/// </summary>
public sealed class CentrePrint : UiScreen
{
    private readonly ICentrePrintSource _text;

    public CentrePrint(ICentrePrintSource text) => _text = text;

    public override string Name => "centreprint";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override int Order => 10;

    public override void Describe(ref UiFrame ui)
    {
        if (_text.CenterPrint.Length == 0 || _text.CenterPrintAlpha <= 0f)
            return;

        float widthDu = ui.DesignWidth;
        using (ui.Box().Size(Size.Fill, Size.Percent(ui.Narrow ? 30 : 36)).Opacity(_text.CenterPrintAlpha))
        using (ui.Panel(Space.XL, Space.XS, Elevation.Raised).At(UiAnchor.Bottom).Pad(Space.XL, Space.M))
        {
            ui.Rect(Tone.Accent, Size.Fill, Size.Du(4)).Round(Radius.S);
            ui.Data(_text.CenterPrint, Text.Heading).Wrap(MathF.Min(820, widthDu - 160)).TextAlign(Align.Centre);
        }
    }
}

/// <summary>
/// Chat (stock: CHudChat): the latest lines bottom left, above the HUD's
/// vitals; the speaker in their team's colour, (Team) and *Dead* tags, each
/// line fading as the game says, the block on one translucent surface.
/// </summary>
public sealed class Chat : UiScreen
{
    private readonly IChatSource _chat;

    public Chat(IChatSource chat) => _chat = chat;

    public override string Name => "chat";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override int Order => 5;

    /// <summary>The newest lines shown (fewer on a narrow screen).</summary>
    public const int Lines = 8, NarrowLines = 5;

    public override void Describe(ref UiFrame ui)
    {
        if (_chat.Count == 0)
            return;

        // Narrow: chat moves to the top, across the screen, so the bottom stays
        // the HUD's; fewer lines.
        bool narrow = ui.Narrow;
        float widthDu = ui.DesignWidth;
        float lineWidth = narrow ? widthDu - 96 : MathF.Min(620, widthDu - 96);
        int first = Math.Max(0, _chat.Count - (narrow ? NarrowLines : Lines));

        // Above the HUD's bottom line (two lines of it on a narrow screen).
        using (ui.Column(Space.L, Align.Start).At(narrow ? UiAnchor.TopLeft : UiAnchor.BottomLeft).Inset(Space.XXL))
        {
            using (ui.Panel(Space.M, Space.XS, Elevation.Flat).Width(Size.Du(lineWidth + 32)))
            {
                for (int i = first; i < _chat.Count; i++)
                {
                    var line = _chat.Line(i);
                    var tone = line.Team == 2 ? Tone.TeamA : line.Team == 3 ? Tone.TeamB : Tone.Text;
                    using (ui.Row(Space.S, Align.Start).Opacity(line.Alpha).Key(i))
                    {
                        if (line.Dead)
                            ui.Text("#SS_UI_Chat_Dead", Text.Label, Tone.TextMuted);
                        if (line.TeamOnly)
                            ui.Text("#SS_UI_Chat_Team", Text.Label, Tone.TextMuted);
                        if (line.Speaker.Length > 0)
                            ui.Data(line.Speaker, Text.Body, tone);
                        ui.Data(line.Text, Text.Body).Wrap(lineWidth - 160);
                    }
                }
            }
            if (!narrow)
                ui.Spacer(Size.Du(1), Size.Du(VitalsPanel.HeightDu));
        }
    }
}
