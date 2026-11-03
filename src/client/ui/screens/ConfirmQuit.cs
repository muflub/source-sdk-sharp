using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// Quit's question (stock GameUI's quit confirmation): "QUIT GAME", "Do you
/// wish to stop playing now?", Cancel and Quit game. A modal dialog over the
/// menu, up while the source asks.
/// </summary>
public sealed class ConfirmQuit(IConfirmQuitSource question) : UiScreen
{
    public override string Name => "confirmquit";

    public override bool OpenByDefault => true;

    public override bool WantsMouse => question.Asking;

    /// <summary>Above the menus, beside the disconnect dialog.</summary>
    public override int Order => 95;

    public override void Describe(ref UiFrame ui)
    {
        if (!question.Asking)
            return;
        using (ui.Dialog("confirmquit", "#SS_UI_Quit_Title", 440))
        {
            ui.Text("#SS_UI_Quit_Text", Text.Body).Wrap(392);
            using (ui.DialogButtons())
            {
                if (ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                    question.Cancel();
                if (ui.Button("quit", "#SS_UI_Quit_Ok", primary: true).Clicked)
                    question.Quit();
            }
        }
    }
}
