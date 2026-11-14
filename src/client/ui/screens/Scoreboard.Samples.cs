using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class ScoreboardSamples
{
    [UiSample("12 players, two teams")]
    private static UiScreen TwelvePlayers(UiSampleServices s) => s.Add<IScoreboardSource>(FakeScoreboard.Teams(6)).Create<Scoreboard>();

    [UiSample("deathmatch with bots")]
    private static UiScreen Bots(UiSampleServices s) => s.Add<IScoreboardSource>(FakeScoreboard.Bots()).Create<Scoreboard>();

    [UiSample("empty server")]
    private static UiScreen Empty(UiSampleServices s) => s.Add<IScoreboardSource>(FakeScoreboard.Empty()).Create<Scoreboard>();

    [UiSample("team deathmatch, three teams")]
    private static UiScreen ThreeTeams(UiSampleServices s)
        => s.Add<IScoreboardSource>(FakeScoreboard.Teams(4, seed: 3, teamCount: 3)).Create<Scoreboard>();

    [UiSample("team deathmatch, four teams of four")]
    private static UiScreen FourTeams(UiSampleServices s)
        => s.Add<IScoreboardSource>(FakeScoreboard.Teams(4, seed: 5, teamCount: 4)).Create<Scoreboard>();

    [UiSample("full server, 16 players")]
    private static UiScreen Full(UiSampleServices s) => s.Add<IScoreboardSource>(FakeScoreboard.Teams(8, seed: 7)).Create<Scoreboard>();
}
