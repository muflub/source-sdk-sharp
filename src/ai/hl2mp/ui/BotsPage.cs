using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.Hl2mp.Ui;

/// <summary>
/// The bots page of the core's create-server window (plan_hl2mp_ai §3.5): a
/// tab after the core's Server and Game pages, so the window keeps its map,
/// hostname, size and password. Preset bots, with the difficulty slider's
/// three stops (Easy, Medium, Hard, each with the points it spends); the
/// custom bots, each with its spend of the pool and its style. The page
/// refuses Start while the bots outnumber the slots the window's size leaves,
/// and Start runs its roster line after the size and before the map. Narrow
/// (under 680 du, where a row's name, meter and two buttons stop fitting):
/// the form's labels go above their fields and a custom bot's buttons under
/// its name.
/// </summary>
public sealed class BotsPage : CreateServerPage
{
    private readonly ICreateServerSource _source;
    private readonly ICreateServerActions _actions;
    private readonly CustomBotList _list;

    public BotsPage(ICreateServerSource source, ICreateServerActions actions, CustomBotList list)
    {
        _source = source;
        _actions = actions;
        _list = list;
    }

    public override string TabToken => "#Ai_CreateServer_BotsTab";

    /// <summary>
    /// 560 x 800 du: the form's three rows, the custom list's header and three
    /// rows (two narrow, where the form's labels stand above their fields),
    /// and the buttons, under the window's title and tabs (at 380 tall the
    /// list ran under the buttons, seen in the minimum sample).
    /// </summary>
    public override float MinWidthDu => 560;

    public override float MinHeightDu => 800;

    /// <summary>The difficulty stops' keys start here, clear of the custom list's row keys (0..n).</summary>
    private const int StopKeys = 1000;

    /// <summary>The label column of the form, du.</summary>
    public const float LabelDu = 160;

    /// <summary>Narrower than this, the page stacks (its own breakpoint: the window's is 600).</summary>
    public const float NarrowDu = 680;

    public override string StartRefusalToken(in CreateServerChoices choices) => _source.StartRefusalToken(choices.MaxPlayers);

    public override void BeforeMap(in CreateServerChoices choices, List<string> lines)
    {
        _actions.SetMaxPlayers(choices.MaxPlayers);
        _actions.BeforeMap(lines);
    }

    public override void Describe(ref UiFrame ui, in CreateServerChoices choices)
    {
        var s = _source;
        if (choices.MaxPlayers != s.MaxPlayers)
            _actions.SetMaxPlayers(choices.MaxPlayers);
        bool narrow = ui.DesignWidth < NarrowDu;
        using (narrow ? ui.Column(Space.S, Align.Stretch).Width(Size.Fill) : ui.FormGrid(LabelDu).Width(Size.Fill))
        {
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

            ui.Label("#Ai_CreateServer_EachMap");
            if (ui.Checkbox("newbots", "#Ai_CreateServer_NewBots", s.NewBotsEachMap).Clicked)
                _actions.SetNewBotsEachMap(!s.NewBotsEachMap);
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
        bool narrow = ui.DesignWidth < BotsPage.NarrowDu;
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
