using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The kill feed's and the chat's names in their team's colour under team play
/// (plan_bots_teams_tf2 A.3 item 8; stock hud_deathnotice.cpp:143,176-177 and the
/// chat's GetClientColor; Combine blue, Rebels red, c_playerresource.cpp:77-81),
/// and as before with no team (deathmatch).
/// </summary>
public class TeamColourNamesTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static UiContext Render(UiScreen screen)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        // A second frame, late: the line fades in (and the aux power, where it shows).
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 100.0);
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 110.0);
        return ctx;
    }

    private static int Find(UiContext ctx, string text)
    {
        var els = UiTestKit.Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (string.Equals(UiTestKit.RunText(ctx, i), text, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static int At(UiContext ctx, string text)
        => Find(ctx, text) is >= 0 and var i ? i : throw new Xunit.Sdk.XunitException($"nothing drew {text}");

    private static UiScreen Hud(FakeLocalPlayer player, FakeKillFeed? kills = null)
        => HudSamples.Build(new UiSampleServices(), player, kills: kills);

    [Fact]
    public void AKillFeedNameIsInItsTeamsColour()
    {
        var ctx = Render(Hud(new FakeLocalPlayer(), new FakeKillFeed { Lines = [FakeKillFeed.Kill("Alyx", 3, "Barney", 2, "smg1")] }));

        Assert.Equal((Tone.TeamB, Tone.TeamA), (ctx.Report.TextToneOf(At(ctx, "Alyx")), ctx.Report.TextToneOf(At(ctx, "Barney"))));
    }

    [Fact]
    public void AKillFeedNameWithNoTeamIsPlainText()
    {
        var ctx = Render(Hud(new FakeLocalPlayer(), new FakeKillFeed { Lines = [FakeKillFeed.Kill("Alyx", 0, "Barney", 0, "smg1")] }));

        Assert.Equal((Tone.Text, Tone.Text), (ctx.Report.TextToneOf(At(ctx, "Alyx")), ctx.Report.TextToneOf(At(ctx, "Barney"))));
    }

    [Fact]
    public void AChatSpeakerIsInItsTeamsColour()
    {
        var s = new UiSampleServices();
        s.Add<IChatSource>(new FakeChat { Lines = [new ChatLine("Alyx", 3, "hello", false, 1f), new ChatLine("Barney", 2, "hi", true, 1f)] });
        var ctx = Render(s.Create<Chat>());

        Assert.Equal((Tone.TeamB, Tone.TeamA), (ctx.Report.TextToneOf(At(ctx, "Alyx")), ctx.Report.TextToneOf(At(ctx, "Barney"))));
    }

    // ---- team deathmatch's third and fourth teams (mp_tdm_teams 3, 4) ----

    [Theory]
    [InlineData(2, Tone.TeamA)]
    [InlineData(3, Tone.TeamB)]
    [InlineData(4, Tone.TeamC)]
    [InlineData(5, Tone.TeamD)]
    [InlineData(0, Tone.Line)]
    [InlineData(1, Tone.Line)]
    public void EachPlayingTeamHasItsOwnTone(int team, Tone tone)
        => Assert.Equal(tone, UiTeams.ToneOf(team));

    [Fact]
    public void TheThemeDrawsFourDistinctTeamColours()
    {
        var theme = SourceSharpTheme.Create();
        var colours = new[] { Tone.TeamA, Tone.TeamB, Tone.TeamC, Tone.TeamD }.Select(t => theme.Color(t)).ToList();

        Assert.Equal(4, colours.Distinct().Count());
        Assert.DoesNotContain(default(UiColor), colours);
    }

    [Fact]
    public void AMetroPoliceKillOfAResistanceMemberIsGreenAndYellow()
    {
        var ctx = Render(Hud(new FakeLocalPlayer(), new FakeKillFeed { Lines = [FakeKillFeed.Kill("Alyx", 4, "Barney", 5, "smg1")] }));

        Assert.Equal((Tone.TeamC, Tone.TeamD), (ctx.Report.TextToneOf(At(ctx, "Alyx")), ctx.Report.TextToneOf(At(ctx, "Barney"))));
    }

    [Fact]
    public void AThirdAndFourthTeamsChatSpeakersAreInTheirColours()
    {
        var s = new UiSampleServices();
        s.Add<IChatSource>(new FakeChat { Lines = [new ChatLine("Alyx", 4, "hello", false, 1f), new ChatLine("Barney", 5, "hi", true, 1f)] });
        var ctx = Render(s.Create<Chat>());

        Assert.Equal((Tone.TeamC, Tone.TeamD), (ctx.Report.TextToneOf(At(ctx, "Alyx")), ctx.Report.TextToneOf(At(ctx, "Barney"))));
    }
}
