using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The disconnect dialog (stock GameUI's loading dialog in its error form):
/// "Disconnected", the reason, Close. A modal dialog over the menu.
/// </summary>
public sealed class DisconnectMessage(IDisconnectMessageSource message) : UiScreen
{
    public override string Name => "disconnectmessage";

    public override bool OpenByDefault => true;

    public override bool WantsMouse => message.Showing;

    /// <summary>Above the menus and the loading dialog.</summary>
    public override int Order => 95;

    public override void Describe(ref UiFrame ui)
    {
        if (!message.Showing)
            return;
        using (ui.Dialog("disconnected", "#SS_UI_Disconnected", 520))
        {
            ui.Data(message.Body, Text.Body).Wrap(472);
            using (ui.DialogButtons())
                if (ui.Button("close", "#SS_UI_Close", primary: true).Clicked)
                    message.Close();
        }
    }
}
