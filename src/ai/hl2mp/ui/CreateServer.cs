using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.Hl2mp.Ui;

/// <summary>
/// Create server (plan_hl2mp_ai §3.5): the map, checked on the game's paths
/// as it is typed; the player count; preset bots, with the difficulty slider's
/// three stops (Easy, Medium, Hard, each with the points it spends); the
/// custom bots, each with its spend of the pool and its style; and Start,
/// which the source refuses with a reason while the map is missing or the
/// bots outnumber the slots. A window: it answers the name <c>createserver</c>
/// (<see cref="Aliases"/>), so the main menu's Create server (stock's
/// OpenCreateMultiplayerGameDialog), <c>gamemenucommand</c> and
/// <c>cs_ui_open createserver</c> open it in place of the core's create-server
/// dialog wherever the HL2MP AI's UI is loaded. Narrow: the form's labels go
/// above their fields and a custom bot's buttons under its name.
/// </summary>
public sealed class CreateServer : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly ICreateServerSource _source;
    private readonly ICreateServerActions _actions;
    private readonly CustomBotList _list;

    public CreateServer(UiWindows windows, ICreateServerSource source, ICreateServerActions actions, CustomBotList list)
    {
        _windows = windows;
        _source = source;
        _actions = actions;
        _list = list;
    }

    /// <summary>Its own name, apart from the core's <c>createserver</c> screen (both are on the test mod's menu layer).</summary>
    public override string Name => "createserverbots";

    /// <summary>The core's screen name, which this one answers in its place.</summary>
    public override IReadOnlyList<string> Aliases => _aliases;

    private readonly string[] _aliases = ["createserver"];

    public string WindowId => "createserverbots";

    /// <summary>The difficulty stops' keys start here, clear of the custom list's row keys (0..n).</summary>
    private const int StopKeys = 1000;

    /// <summary>The label column of the form, du.</summary>
    public const float LabelDu = 160;

    /// <summary>
    /// 760 x 760 du by default, at least 560 x 760: the form's four rows, the
    /// custom list's header and three rows (two narrow, where the form's
    /// labels stand above their fields), and the buttons; at 600 tall the
    /// narrow list showed half a row (seen in the minimum sample). Narrow
    /// below 680 du, where a row's name, meter and two buttons stop fitting.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(760, screenW - 32), MathF.Min(760, screenH - 32), 560, 760, screenW, screenH, 680);

    public override void Describe(ref UiFrame ui)
    {
        var s = _source;
        using (ui.Window(_windows, WindowId, "#Ai_CreateServer_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            bool narrow = ui.Narrow;
            using (narrow ? ui.Column(Space.S, Align.Stretch).Width(Size.Fill) : ui.FormGrid(LabelDu).Width(Size.Fill))
            {
                ui.Label("#Ai_CreateServer_Map");
                using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                {
                    string typed = ui.EditText("map", s.MapName, "#Ai_CreateServer_MapPlaceholder");
                    if (!ReferenceEquals(typed, s.MapName))
                        _actions.SetMapName(typed);
                    using (ui.Row(Space.XS, Align.Centre).Width(Size.Du(132)))
                    {
                        bool found = s.MapFound;
                        ui.Icon(found ? UiShape.Check : UiShape.Cross45, found ? Tone.Positive : Tone.Danger, 16);
                        ui.Text(found ? "#Ai_CreateServer_MapFound" : "#Ai_CreateServer_MapMissing", Text.Body,
                                found ? Tone.Positive : Tone.Danger);
                    }
                }

                ui.Label("#Ai_CreateServer_MaxPlayers");
                using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                {
                    int players = (int)ui.Slider("players", s.MaxPlayers, s.MaxPlayersMin, s.MaxPlayersMax, 1);
                    if (players != s.MaxPlayers)
                        _actions.SetMaxPlayers(players);
                    ui.Number(s.MaxPlayers, Text.Heading).Width(Size.Du(40)).TextAlign(Align.End);
                }

                ui.Label("#Ai_CreateServer_Bots");
                using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                {
                    int bots = (int)ui.Slider("bots", s.BotCount, 0, s.BotCountMax, 1);
                    if (bots != s.BotCount)
                        _actions.SetBotCount(bots);
                    ui.Number(s.BotCount, Text.Heading).Width(Size.Du(40)).TextAlign(Align.End);
                }

                ui.Label("#Ai_CreateServer_Difficulty");
                using (ui.Column(Space.XS, Align.Stretch).Width(Size.Fill))
                {
                    using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                    {
                        int stop = (int)ui.Slider("difficulty", s.Difficulty, 0, s.DifficultyStops - 1, 1);
                        if (stop != s.Difficulty)
                            _actions.SetDifficulty(stop);
                        ui.Spacer(Size.Du(40), Size.Du(1));
                    }

                    // The stops under the track, the chosen one lit, each with what it spends:
                    // the same row shape as the slider's, so the labels span the track.
                    using (ui.Row(Space.M, Align.Start).Width(Size.Fill))
                    {
                        using (ui.Row(Space.None, Align.Start).Width(Size.Fill))
                        {
                            for (int i = 0; i < s.DifficultyStops; i++)
                            {
                                bool on = i == s.Difficulty;
                                var align = i == 0 ? Align.Start : i == s.DifficultyStops - 1 ? Align.End : Align.Centre;
                                using (ui.Column(Space.None, align).Key(StopKeys + i).Width(Size.Fr()))
                                {
                                    ui.Text(s.DifficultyToken(i), Text.Body, on ? Tone.Accent : Tone.TextMuted);
                                    using (ui.Row(Space.XS, Align.Centre))
                                    {
                                        ui.Number(s.DifficultySpent(i), Text.Label, on ? Tone.Text : Tone.TextMuted);
                                        ui.Label("#Ai_CreateServer_Points");
                                    }
                                }
                            }
                        }

                        ui.Spacer(Size.Du(40), Size.Du(1));
                    }
                }
            }

            ui.Divider();

            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
            {
                ui.Text("#Ai_CreateServer_Custom", Text.Heading);
                ui.Number(s.CustomCount, Text.Heading, Tone.TextMuted);
                ui.Flex();
                if (ui.Button("add", "#Ai_CreateServer_AddCustom").Clicked)
                    _actions.AddCustom();
            }

            using (ui.Box().Size(Size.Fill, Size.Fill).Fill(Tone.Surface).Round(Radius.M).Clip())
            {
                if (s.CustomCount == 0)
                    ui.EmptyState(UiShape.Person, "#Ai_CreateServer_NoCustom_Title", "#Ai_CreateServer_NoCustom_Body").At(UiAnchor.Centre);
                else
                    ui.VirtualList("custom", s.CustomCount, narrow ? CustomBotList.NarrowRowDu : CustomBotList.RowDu, _list)
                      .Size(Size.Fill, Size.Fill);
            }

            string refusal = s.StartRefusalToken;
            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (refusal.Length > 0)
                    ui.Text(refusal, Text.Body, Tone.Danger).Elide();
                ui.Flex();
                if (ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                    _windows.RequestClose(WindowId);
                if (ui.Button("start", "#Ai_CreateServer_Start", primary: true, enabled: refusal.Length == 0).Clicked)
                {
                    _actions.Start();
                    _windows.RequestClose(WindowId);
                }
            }
        }
    }
}

/// <summary>
/// One custom bot's row: its name (or that the server names it), its style's
/// two skills, its spend as a number and a meter against the pool, and Edit
/// (the bot editor) and Remove. Narrow, the buttons go under the name.
/// </summary>
public sealed class CustomBotList(ICreateServerSource source, ICreateServerActions actions) : UiPanel<int>
{
    public const float RowDu = 64;

    public const float NarrowRowDu = 112;

    public override void Describe(ref UiFrame ui, in int i)
    {
        CustomBotRow row = source.Custom(i);
        bool narrow = ui.Narrow;
        using (ui.Column(Space.S, Align.Stretch).Size(Size.Fill, Size.Fill).Pad(Space.M, Space.S))
        {
            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
            {
                using (ui.Column(Space.None, Align.Start).Width(Size.Fill))
                {
                    if (row.Name.Length > 0)
                        ui.Data(row.Name, Text.Body).Elide();
                    else
                        ui.Text("#Ai_CreateServer_Unnamed", Text.Body, Tone.TextMuted).Elide();
                    using (ui.Row(Space.XS, Align.Centre))
                    {
                        ui.Label(row.StyleFirstToken);
                        ui.Label(row.StyleSecondToken);
                    }
                }

                ui.Text($"{row.Spent} / {row.Pool}", Text.Body).Width(Size.Du(64)).TextAlign(Align.End);
                ui.Meter(row.Pool > 0 ? row.Spent / (float)row.Pool : 0f, Tone.Accent, Size.Du(80));
                if (!narrow)
                    Buttons(ref ui, i);
            }

            if (narrow)
            {
                using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                {
                    ui.Flex();
                    Buttons(ref ui, i);
                }
            }
        }
    }

    private void Buttons(ref UiFrame ui, int i)
    {
        if (ui.Button("edit", "#Ai_CreateServer_Edit").Clicked)
            actions.EditCustom(i);
        if (ui.Button("remove", "#Ai_CreateServer_Remove").Clicked)
            actions.RemoveCustom(i);
    }
}
