using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The message of the day (stock: CTextWindow, the server's VGUIMenu "info"):
/// a dialog with the server's title and its text, scrolling when long, and
/// OK. The HTML and URL kinds of stock's window are a later web arm; this shows
/// the text kind (and a URL as its text).
/// </summary>
public sealed class Motd : UiScreen
{
    private readonly IMotdSource _source;
    private readonly IMotdActions? _actions;

    /// <param name="actions">What OK does (the core: close it); none in a sample.</param>
    public Motd(IMotdSource source, IMotdActions? actions = null)
    {
        _source = source;
        _actions = actions;
    }

    private string? _text;
    private string[] _lines = [];

    public override string Name => "motd";

    public override void Describe(ref UiFrame ui)
    {
        bool wide = !ui.Narrow;
        float width = wide ? 720 : MathF.Min(560, ui.DesignWidth - 64);
        using (ui.Dialog("motd", "#SS_UI_Motd_Title", width))
        {
            ui.Data(_source.Title, Text.Heading);
            // A row per line, so the scroll clips rows and not one tall block.
            if (!ReferenceEquals(_text, _source.Text))
            {
                _text = _source.Text;
                _lines = _text.Split('\n');
            }
            using (ui.Scroll("text", Space.S).Width(Size.Fill).Height(Size.Du(wide ? 360 : 280)))
                for (int i = 0; i < _lines.Length; i++)
                    ui.Data(_lines[i], Text.Body).Wrap(width - 64).Key(i);

            using (ui.DialogButtons().Width(Size.Fill))
            {
                ui.Flex();
                if (ui.Button("ok", "#SS_UI_Ok", primary: true).Clicked)
                    _actions?.Close();
            }
        }
    }
}
