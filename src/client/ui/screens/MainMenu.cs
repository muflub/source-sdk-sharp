using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The main menu (the launch's, and the pause menu stock's ESC menu:
/// ClientModeSingle::OnKeyDown shows the main panel, and Escape again hides
/// it). A dialog on the right with the menu's buttons: Resume first in game as
/// the primary action, then Play, Options, Quit. The full TF2 main menu (class
/// select, servers, the friends list) is a later arm.
/// </summary>
public sealed class MainMenu(IMainMenuSource menu) : UiScreen
{
    public override string Name => "mainmenu";

    /// <summary>Drawn above the other menu screens (stock's GameUI main panel covers every
    /// in-game panel: the MOTD window, TF2's text window), below the console and the loading
    /// disc (their Order) and below the raised windows (their rank).</summary>
    public override int Order => 50;

    public override void Describe(ref UiFrame ui)
    {
        bool wide = !ui.Narrow;
        using (ui.Dialog("mainmenu", "#SS_UI_Main_Title", wide ? 360 : MathF.Min(320, ui.DesignWidth - 64)))
        {
            // The column, not the button row: three 120 du-minimum buttons side
            // by side overflow the 360 du panel and the last clips (seen in the
            // fact below); a menu is a stack of full-width actions anyway.
            if (menu.InLevel && ui.Button("resume", "#SS_UI_Main_Resume", primary: true, width: Size.Fill).Clicked)
                menu.Resume();
            if (menu.CanPlay && ui.Button("play", "#SS_UI_Main_Play", primary: !menu.InLevel, width: Size.Fill).Clicked)
                menu.Play();
            if (ui.Button("options", "#SS_UI_Main_Options", width: Size.Fill).Clicked)
                menu.OpenOptions();
            if (ui.Button("quit", "#SS_UI_Main_Quit", width: Size.Fill).Clicked)
                menu.Quit();
        }
    }
}
