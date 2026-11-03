using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The team band's head count: stock renders it from a token pair
/// (TF_ScoreBoard_Player "%s1 player" / TF_ScoreBoard_Players "%s1 players",
/// tf_english.txt:59-60), so one player is "1 player", never "1 players".
/// </summary>
public class TeamTableCountTests
{
    [Theory]
    [InlineData(1, "1 PLAYER")]
    [InlineData(2, "2 PLAYERS")]
    public void The_count_takes_the_singular_for_one_player(int perTeam, string expected)
    {
        var ctx = UiTestKit.Context(localiser: UiSamplesTests.Language());
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Sub("t", new TeamTable(FakeScoreboard.Teams(perTeam)), new TeamTableProps(0, Tone.TeamA)));
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == "count")
            {
                Assert.Equal(expected, UiTestKit.RunText(ctx, i));
                return;
            }
        Assert.Fail("no count drew");
    }
}
