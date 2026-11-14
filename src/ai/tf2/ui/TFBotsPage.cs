using SourceSharp.Ai.Ui;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.TF2.Ui;

/// <summary>
/// TF2's bots page of the core's create-server window (plan_bots_teams_tf2 P6 step 8): a tab
/// after the window's Server and Game pages, in the form those pages draw (a label column,
/// sentence-case labels, TF2's fonts and colours from the mod's theme). How many bots and how
/// hard (the three preset stops, each with the points it spends); RED against BLU, split on
/// one slider when the teams are picked, else each bot joining the smaller team; the class
/// they play (only the classes bots can play are listed, the one played lit); names such as
/// the server draws them from TF2's pool; new bots each map; and the custom bots, each with its
/// spend and its style, Edit opening the game-neutral bot editor. Start runs the roster line
/// after the server's size and before the map. Narrow (under 680 du): the labels go above
/// their fields and a custom bot's buttons under its name.
/// </summary>
public sealed class TFBotsPage : CreateServerPage
{
    private readonly IBotSetupSource _source;
    private readonly IBotSetupActions _actions;
    private readonly IBotClassSource _classes;
    private readonly IBotNamePoolSource _names;
    private readonly CustomBotList _list;

    public TFBotsPage(IBotSetupSource source, IBotSetupActions actions, IBotClassSource classes, IBotNamePoolSource names, CustomBotList list)
    {
        _source = source;
        _actions = actions;
        _classes = classes;
        _names = names;
        _list = list;
    }

    public override string TabToken => "#Ai_CreateServer_BotsTab";

    /// <summary>
    /// 560 x 920 du: the form's six rows (twelve narrow, where the labels stand above their
    /// fields), the custom list's header and two narrow rows whole, and the buttons, under
    /// the window's title and tabs.
    /// </summary>
    public override float MinWidthDu => 560;

    public override float MinHeightDu => 920;

    /// <summary>The label column, du: the window's own pages' (UiForm.LabelColumnDu).</summary>
    public const float LabelDu = 220;

    /// <summary>Narrower than this, the page stacks (the window's own breakpoint is 600), as its custom bots' rows do.</summary>
    public const float NarrowDu = CustomBotList.NarrowDu;

    /// <summary>The width of a count beside a slider, du.</summary>
    public const float CountDu = 40;

    // Keys in loops, clear of each other's ranges and of the custom list's rows (0..n).
    private const int StopKeys = 1000, ClassKeys = 2000, NameKeys = 3000, LabelKeys = 4000, TeamKeys = 5000;

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
        using (narrow ? ui.Column(Space.M, Align.Stretch).Width(Size.Fill) : ui.FormGrid(LabelDu).Width(Size.Fill))
        {
            Label(ref ui, 0, "#Ai_CreateServer_Bots");
            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
            {
                int bots = (int)ui.Slider("bots", s.BotCount, 0, s.BotCountMax, 1);
                if (bots != s.BotCount)
                    _actions.SetBotCount(bots);
                ui.Number(s.BotCount, Text.Body, Tone.Text).Width(Size.Du(CountDu)).TextAlign(Align.End);
            }

            Label(ref ui, 1, "#Ai_CreateServer_Difficulty");
            Difficulty(ref ui);

            Label(ref ui, 2, "#Ai_CreateServer_Teams");
            Teams(ref ui);

            Label(ref ui, 3, "#Ai_TF_Class");
            Classes(ref ui);

            Label(ref ui, 4, "#Ai_TF_Names");
            Names(ref ui);

            Label(ref ui, 5, "#Ai_CreateServer_EachMap");
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

    /// <summary>A form label as the window's own pages draw theirs.</summary>
    private static void Label(ref UiFrame ui, int key, string token)
        => ui.Text(token, Text.Body, Tone.Text).Wrap(LabelDu).Key(LabelKeys + key);

    /// <summary>The slider's three stops, and under the track each stop's name and spend, the chosen one lit.</summary>
    private void Difficulty(ref UiFrame ui)
    {
        var s = _source;
        using (ui.Column(Space.XS, Align.Stretch).Width(Size.Fill))
        {
            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
            {
                int stop = (int)ui.Slider("difficulty", s.Difficulty, 0, s.DifficultyStops - 1, 1);
                if (stop != s.Difficulty)
                    _actions.SetDifficulty(stop);
                ui.Spacer(Size.Du(CountDu), Size.Du(1));
            }

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

                ui.Spacer(Size.Du(CountDu), Size.Du(1));
            }
        }
    }

    /// <summary>
    /// RED against BLU, on one line: picked, one slider splits the bots, RED's count on its
    /// left in RED's colour and BLU's (what is left) on its right in BLU's; else each bot joins
    /// the smaller team, as a player's auto-assign does.
    /// </summary>
    private void Teams(ref UiFrame ui)
    {
        var s = _source;
        using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
        {
            if (ui.Checkbox("teams", "#Ai_CreateServer_TeamsPick", s.TeamsPicked).Clicked)
                _actions.SetTeamsPicked(!s.TeamsPicked);
            if (!s.TeamsPicked)
            {
                ui.Text("#Ai_TF_TeamsEven", Text.Body, Tone.TextMuted).Width(Size.Fill).Elide();
                return;
            }

            Team(ref ui, 0);
            int red = (int)ui.Slider("red", s.BotsOn(0), 0, s.BotCount, 1);
            if (red != s.BotsOn(0))
                _actions.SetBotsOn(0, red);
            Team(ref ui, 1);
        }
    }

    /// <summary>A team's count over its name, in its colour.</summary>
    private void Team(ref UiFrame ui, int index)
    {
        Tone tone = UiTeams.ToneOf(index + 2);
        using (ui.Column(Space.None, Align.Centre).Key(TeamKeys + index).Width(Size.Du(CountDu)))
        {
            ui.Number(_source.BotsOn(index), Text.Heading, tone);
            ui.Text(_source.TeamToken(index), Text.Label, tone);
        }
    }

    /// <summary>The classes the bots can play, as chips, the one they play lit.</summary>
    private void Classes(ref UiFrame ui)
    {
        using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
        {
            for (int i = 0; i < _classes.ClassCount; i++)
            {
                bool on = i == _classes.ChosenClass;
                using (ui.Box().Key(ClassKeys + i).Pad(Space.M, Space.XS).Round(Radius.M)
                         .Fill(on ? ((UiPaint)Tone.Accent).WithOpacity(0.2f) : (UiPaint)Tone.SurfaceRaised)
                         .Border(on ? Tone.Accent : Tone.Line))
                    ui.Text(_classes.ClassToken(i), Text.Body, on ? Tone.Text : Tone.TextMuted);
            }

            ui.Text("#Ai_TF_ClassEvery", Text.Body, Tone.TextMuted).Width(Size.Fill).Elide();
        }
    }

    /// <summary>A few names drawn from TF2's pool as the server draws them, at the chosen difficulty (the label says "such as").</summary>
    private void Names(ref UiFrame ui)
    {
        using (ui.Row(Space.L, Align.Centre).Width(Size.Fill))
        {
            for (int i = 0; i < _names.ExampleCount; i++)
                ui.Data(_names.Example(i), Text.Body).Key(NameKeys + i).Elide().MaxWidth(240);
        }
    }
}
