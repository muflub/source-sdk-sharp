using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The player list (plan_gameui_managed.md G3; stock: GameUI's player list
/// dialog, also its mute dialog): every player in the game by name, with
/// what they are to this client (muted, a bot); one is selected, and the
/// button mutes or unmutes the selected player's voice. A bot or this
/// client cannot be muted. Refreshed when the window opens.
/// </summary>
public sealed class PlayerList(UiWindows windows, IPlayerListSource players) : UiScreen, IUiWindowed
{
    private long _lastFrame = long.MinValue;

    public override string Name => "playerlist";

    public string WindowId => "playerlist";

    /// <summary>The selected row, or -1.</summary>
    public int Selected { get; set; } = -1;

    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(560, screenW), MathF.Min(520, screenH), 380, 320, screenW, screenH, 480);

    /// <summary>Whether the selected player can be muted (a player, not a bot or this client).</summary>
    public bool CanMute
        => (uint)Selected < (uint)players.Count && players.Player(Selected) is { Bot: false, Local: false };

    public void Open()
    {
        players.Refresh();
        Selected = players.Count > 0 ? 0 : -1;
    }

    public void ToggleMute()
    {
        if (CanMute)
            players.ToggleMute(Selected);
    }

    public override void Describe(ref UiFrame ui)
    {
        if (ui.FrameNumber != _lastFrame + 1)
            Open();
        _lastFrame = ui.FrameNumber;

        using (ui.Window(windows, WindowId, "#SS_UI_Players_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            if (players.Count == 0)
                ui.EmptyState(UiShape.Person, "#SS_UI_Players_None", "#SS_UI_Players_NoneBody").Size(Size.Fill, Size.Fill);
            else
            {
                using (ui.Row(Space.M).Width(Size.Fill).Pad(Space.S, Space.None))
                {
                    ui.Text("#SS_UI_Players_Name", Text.Label, Tone.TextMuted).Width(Size.Fill);
                    ui.Text("#SS_UI_Players_Properties", Text.Label, Tone.TextMuted).Width(Size.Du(120));
                }
                using (ui.Scroll("players", Space.None).Size(Size.Fill, Size.Fill))
                    for (int i = 0; i < players.Count; i++)
                        Row(ref ui, i);
            }

            bool muted = CanMute && players.Player(Selected).Muted;
            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (ui.Button("mute", muted ? "#SS_UI_Players_Unmute" : "#SS_UI_Players_Mute", enabled: CanMute).Clicked)
                    ToggleMute();
                ui.Flex();
                if (ui.Button("close", "#SS_UI_Close", primary: true).Clicked)
                    windows.RequestClose(WindowId);
            }
        }
    }

    private void Row(ref UiFrame ui, int i)
    {
        var p = players.Player(i);
        var row = ui.Row(Space.M).Key(i).Size(Size.Fill, Size.Du(36)).Pad(Space.S, Space.None).Round(Radius.S)
                    .Fill(i == Selected ? Tone.SurfaceRaised : Tone.None);
        if (ui.Interact(row).Clicked)
            Selected = i;
        using (row)
        {
            ui.Data(p.Name, Text.Body, i == Selected ? Tone.Accent : Tone.Text).Width(Size.Fill).Elide();
            if (p.Bot)
                ui.Text("#SS_UI_Players_Bot", Text.Body, Tone.TextMuted).Width(Size.Du(120));
            else if (p.Muted)
                ui.Text("#SS_UI_Players_Muted", Text.Body, Tone.Warning).Width(Size.Du(120));
            else if (p.Local)
                ui.Text("#SS_UI_Players_You", Text.Body, Tone.TextMuted).Width(Size.Du(120));
            else
                ui.Spacer(Size.Du(120), Size.Du(1));
        }
    }
}
