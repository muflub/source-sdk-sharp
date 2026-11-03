using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The win panel from fakes (the ui-screen loop's sample step). The screen
// draws the source's resolved sentences; the #Winpanel_* and #TF_Name_*
// tokens live in game/mod_tf2/resource/tf2_english.txt.

public static class TFWinPanelSamples
{
    public static UiScreen Build(UiSampleServices s, FakeWinPanelSource? source = null)
    {
        s.Add<ITFWinPanelSource>(source ?? new FakeWinPanelSource());
        return s.Create<TFWinPanel>();
    }

    // A complete round: RED won on the flag limit, the scores still on the
    // previous ones, three MVPs and a killstreak row (winpanel.res's full
    // layout).
    [UiSample("red wins the round, scores pending")]
    private static UiScreen RedWins(UiSampleServices s) => Build(s, source: new FakeWinPanelSource
    {
        WinningTeam = Red,
        RoundComplete = true,
        Banner = "RED wins the round!",
        WinReason = "RED captured the flag 3 times",
        Details = "Captured by Jane Doe, Tavish DeGroot",
        TopPlayersLabel = "RED MVPs",
        BlueScore = 1, RedScore = 2,      // the tick has not landed: the previous ones
        Rows =
        [
            new("Jane Doe", "Soldier", 6, Red),
            new("Tavish DeGroot", "Demoman", 4, Red),
            new("Misha Misha", "Heavy", 2, Red),
        ],
        KillStreak = new("Jane Doe", "Soldier", 5, Red),
    });

    // The other winner with the scores ticked up (the widest names, no
    // killstreak row).
    [UiSample("blue wins, scores ticked")]
    private static UiScreen BlueWins(UiSampleServices s) => Build(s, source: new FakeWinPanelSource
    {
        WinningTeam = Blue,
        RoundComplete = true,
        Banner = "BLU wins the round!",
        WinReason = "BLU was ahead on score when time ran out",
        Details = "Captured by Ludwig",
        TopPlayersLabel = "BLU MVPs",
        BlueScore = 5, RedScore = 2,
        Rows =
        [
            new("Bartholomew Reginald Fitzwilliam III", "Medic", 8, Blue),
            new("Dell Conagher", "Engineer", 5, Blue),
        ],
    });

    // A stalemate: no scores panel, the plain top players, a streak row
    // alone.
    [UiSample("stalemate")]
    private static UiScreen Stalemate(UiSampleServices s) => Build(s, source: new FakeWinPanelSource
    {
        WinningTeam = 0,
        RoundComplete = true,
        Banner = "Stalemate!",
        WinReason = "No winners",
        TopPlayersLabel = "Top players",
        Rows = [new("Jeremy", "Scout", 3, Red), new("Tavish DeGroot", "Demoman", 3, Blue)],
        KillStreak = new("Jeremy", "Scout", 4, Red),
    });

    // A mini-round: the advancing banner and the points left, no score
    // cards.
    [UiSample("mini-round, BLU advances")]
    private static UiScreen MiniRound(UiSampleServices s) => Build(s, source: new FakeWinPanelSource
    {
        WinningTeam = Blue,
        RoundComplete = false,
        Banner = "BLU advances!",
        WinReason = "BLU captured all the points",
        Details = "2 capture points remaining",
        TopPlayersLabel = "BLU MVPs",
        Rows = [new("Ludwig", "Medic", 2, Blue)],
    });

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;
}

/// <summary>The win panel as the screen reads it; the decode, the name and
/// class resolution and the score tick live in the adapter's facts.</summary>
public sealed class FakeWinPanelSource : ITFWinPanelSource
{
    public bool Showing { get; init; } = true;

    public string Banner { get; init; } = "";

    public string WinReason { get; init; } = "";

    public string Details { get; init; } = "";

    public string TopPlayersLabel { get; init; } = "";

    public int WinningTeam { get; init; }

    public bool RoundComplete { get; init; }

    public int BlueScore { get; init; }

    public int RedScore { get; init; }

    public bool ScoresTick { get; init; } = true;

    public TFWinPanelPlayerView[] Rows { get; init; } = [];

    public TFWinPanelPlayerView KillStreak { get; init; }

    public TFWinPanelPlayerView Player(int position)
        => position < Rows.Length ? Rows[position] : default;
}
