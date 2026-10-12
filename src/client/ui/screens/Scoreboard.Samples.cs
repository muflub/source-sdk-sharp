using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class ScoreboardSamples
{
    [UiSample("12 players, two teams")]
    private static UiScreen TwelvePlayers(UiSampleServices s) => s.Add<IScoreboardSource>(FakeScoreboard.Teams(6)).Create<Scoreboard>();

    [UiSample("empty server")]
    private static UiScreen Empty(UiSampleServices s) => s.Add<IScoreboardSource>(FakeScoreboard.Empty()).Create<Scoreboard>();

    [UiSample("full server, 16 players")]
    private static UiScreen Full(UiSampleServices s) => s.Add<IScoreboardSource>(FakeScoreboard.Teams(8, seed: 7)).Create<Scoreboard>();
}
