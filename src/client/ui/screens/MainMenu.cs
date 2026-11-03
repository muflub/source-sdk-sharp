using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The main menu (the launch's, and the pause menu stock's ESC menu:
/// ClientModeSingle::OnKeyDown shows the main panel, and Escape again hides
/// it). A dialog with the menu's buttons, stock HL2MP's game menu: in game
/// Resume (the primary action), Disconnect and Player list; then Play (the
/// mod's maplist), Find servers, Create server, Options, Quit (which asks).
/// The menu's commands go by stock's names through the GameUI's table.
/// </summary>
public sealed class MainMenu(IMainMenuSource menu, IMainMenuCommands commands, BuildStampPanel build) : UiScreen
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
            if (menu.InLevel)
            {
                if (ui.Button("resume", "#SS_UI_Main_Resume", primary: true, width: Size.Fill).Clicked)
                    menu.Resume();
                if (ui.Button("disconnect", "#SS_UI_Main_Disconnect", width: Size.Fill).Clicked)
                    commands.Run("Disconnect");
                if (ui.Button("players", "#SS_UI_Main_Players", width: Size.Fill).Clicked)
                    commands.Run("OpenPlayerListDialog");
            }
            if (menu.CanPlay && ui.Button("play", "#SS_UI_Main_Play", primary: !menu.InLevel, width: Size.Fill).Clicked)
                menu.Play();
            if (ui.Button("servers", "#SS_UI_Main_FindServers", width: Size.Fill).Clicked)
                commands.Run("OpenServerBrowser");
            if (ui.Button("create", "#SS_UI_Main_CreateServer", width: Size.Fill).Clicked)
                commands.Run("OpenCreateMultiplayerGameDialog");
            if (ui.Button("options", "#SS_UI_Main_Options", width: Size.Fill).Clicked)
                menu.OpenOptions();
            if (ui.Button("quit", "#SS_UI_Main_Quit", width: Size.Fill).Clicked)
                menu.Quit();
        }

        // The HUD's stamp and debug line, which go with the client viewport the GameUI
        // hides. An overlay of its own, so the dialog's scrim does not dim them; a plain
        // screen is laid out above the taskbar's band, so the stamp stands on the bar.
        using (ui.Overlay("build"))
            ui.Sub("build", build);
    }
}
