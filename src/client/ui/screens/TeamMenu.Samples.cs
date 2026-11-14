using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class TeamMenuSamples
{
    [UiSample("joining")]
    private static UiScreen Joining(UiSampleServices s) => s.Add<ITeamMenuSource>(new FakeTeamMenu()).Create<TeamMenu>();

    [UiSample("on Combine, Rebels full")]
    private static UiScreen RebelsFull(UiSampleServices s) => s.Add<ITeamMenuSource>(new FakeTeamMenu
    {
        CurrentTeam = 2,
        Choices = [new TeamChoice(2, "Combine", 7, 88, false), new TeamChoice(3, "Rebels", 8, 102, true)],
    }).Create<TeamMenu>();

    [UiSample("team deathmatch, three teams")]
    private static UiScreen ThreeTeams(UiSampleServices s) => s.Add<ITeamMenuSource>(new FakeTeamMenu
    {
        CurrentTeam = 4,
        Choices = [new TeamChoice(2, "Combine", 3, 21, false), new TeamChoice(3, "Rebels", 2, 18, false),
                   new TeamChoice(4, "Metro Police", 3, 24, false)],
    }).Create<TeamMenu>();

    [UiSample("team deathmatch, four teams")]
    private static UiScreen FourTeams(UiSampleServices s) => s.Add<ITeamMenuSource>(new FakeTeamMenu
    {
        Choices = [new TeamChoice(2, "Combine", 3, 21, false), new TeamChoice(3, "Rebels", 2, 18, false),
                   new TeamChoice(4, "Metro Police", 3, 24, false), new TeamChoice(5, "Resistance", 0, 0, false)],
    }).Create<TeamMenu>();
}
