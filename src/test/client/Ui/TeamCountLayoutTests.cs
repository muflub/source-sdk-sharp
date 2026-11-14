using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Team deathmatch with 2, 3 or 4 teams (user 2026-11-08): the scoreboard draws a
/// table per team, two to a row on a wide screen, and the team menu a card per
/// team, each in its team's colour.
/// </summary>
public class TeamCountLayoutTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static UiContext Render(UiScreen screen, int w = 1920, int h = 1080)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(w, h), 100.0);
        UiSamples.Render(ctx, screen, UiSamples.Screen(w, h), 110.0);
        return ctx;
    }

    private static int At(UiContext ctx, string text)
    {
        var els = UiTestKit.Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (string.Equals(UiTestKit.RunText(ctx, i), text, StringComparison.Ordinal))
                return i;
        throw new Xunit.Sdk.XunitException($"nothing drew {text}");
    }

    private static UiScreen Board(int teams)
        => new UiSampleServices().Add<IScoreboardSource>(FakeScoreboard.Teams(3, seed: 2, teamCount: teams)).Create<Scoreboard>();

    private static UiScreen Menu(params TeamChoice[] choices)
        => new UiSampleServices().Add<ITeamMenuSource>(new FakeTeamMenu { Choices = choices }).Create<TeamMenu>();

    private static readonly TeamChoice[] Four =
    [
        new(2, "Combine", 1, 1, false), new(3, "Rebels", 1, 1, false),
        new(4, "Metro Police", 1, 1, false), new(5, "Resistance", 1, 1, false),
    ];

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void TheScoreboardDrawsATablePerTeamInItsColour(int teams)
    {
        var ctx = Render(Board(teams));
        string last = teams == 3 ? "Metro Police" : "Resistance";

        Assert.Equal(teams == 3 ? Tone.TeamC : Tone.TeamD, ctx.Report.TextToneOf(At(ctx, last)));
    }

    [Fact]
    public void FourTablesAreTwoToARowOnAWideScreen()
    {
        var ctx = Render(Board(4));
        var els = UiTestKit.Elements(ctx);
        var combine = els[At(ctx, "Combine")].Rect;
        var rebels = els[At(ctx, "Rebels")].Rect;
        var police = els[At(ctx, "Metro Police")].Rect;

        Assert.True(MathF.Abs(combine.Y - rebels.Y) < 1 && police.Y > combine.Y + 40 && MathF.Abs(police.X - combine.X) < 1,
                    $"combine {combine} rebels {rebels} police {police}");
    }

    [Fact]
    public void TheTeamMenuDrawsACardPerTeamInItsColour()
    {
        var ctx = Render(Menu(Four));

        Assert.Equal((Tone.TeamC, Tone.TeamD), (ctx.Report.TextToneOf(At(ctx, "Metro Police")), ctx.Report.TextToneOf(At(ctx, "Resistance"))));
    }

    [Fact]
    public void FourCardsStandInOneRowOnAWideScreen()
    {
        var ctx = Render(Menu(Four));
        var els = UiTestKit.Elements(ctx);

        Assert.True(MathF.Abs(els[At(ctx, "Combine")].Rect.Y - els[At(ctx, "Resistance")].Rect.Y) < 1);
    }
}
