using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The HUD under team play (plan_bots_teams_tf2 A.3 item 8): stock HL2MP's team
/// line (CTeamPlayHud, hl2mp_hud_team.cpp) above the vitals, in the team's
/// colour (Combine blue, Rebels red, c_playerresource.cpp:77-81), only under team
/// play and only while the aux power is full.
/// </summary>
public class TeamPlayHudTests
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
    public void UnderTeamPlayTheTeamLineNamesTheTeamInItsColour()
    {
        var ctx = Render(Hud(new FakeLocalPlayer { Teamplay = true, Team = 2, TeamName = "Combine" }));

        Assert.Equal(Tone.TeamA, ctx.Report.TextToneOf(At(ctx, "Combine")));
    }

    [Fact]
    public void TheRebelsLineIsInTheRebelsColour()
    {
        var ctx = Render(Hud(new FakeLocalPlayer { Teamplay = true, Team = 3, TeamName = "Rebels" }));

        Assert.Equal(Tone.TeamB, ctx.Report.TextToneOf(At(ctx, "Rebels")));
    }

    [Fact]
    public void TheTeamLineSitsAboveTheHealth()
    {
        var ctx = Render(Hud(new FakeLocalPlayer { Teamplay = true, Team = 2, TeamName = "Combine" }));
        var els = UiTestKit.Elements(ctx);

        var line = els[At(ctx, "Combine")].Rect;
        var health = els[At(ctx, "Health")].Rect;

        Assert.True(line.Y + line.H <= health.Y && line.X <= health.X + 32,
                    $"the line {line} is not above the health {health}");
    }

    [Fact]
    public void UnderDeathmatchThereIsNoTeamLine()
    {
        // The panel asks team play itself, whatever team the player is on.
        var ctx = Render(Hud(new FakeLocalPlayer { Teamplay = false, Team = 2, TeamName = "Combine" }));

        Assert.Equal((-1, -1), (Find(ctx, "Team"), Find(ctx, "Combine")));
    }

    [Fact]
    public void TheAuxPowerTakesTheTeamLinesPlace()
    {
        // Stock: FadeOutTeamLine while the suit power is below full.
        var ctx = Render(Hud(new FakeLocalPlayer { Teamplay = true, Team = 2, TeamName = "Combine", SuitPower = 62f, SuitDevices = 1 }));

        Assert.Equal(-1, Find(ctx, "Combine"));
        Assert.True(Find(ctx, "Aux power") >= 0, "the aux power shows in its place");
    }

    [Fact]
    public void AMetroPoliceLineIsInTheThirdTeamsColour()
    {
        var ctx = Render(Hud(new FakeLocalPlayer { Teamplay = true, Team = 4, TeamName = "Metro Police" }));

        Assert.Equal(Tone.TeamC, ctx.Report.TextToneOf(At(ctx, "Metro Police")));
    }

    [Fact]
    public void AResistanceLineIsInTheFourthTeamsColour()
    {
        var ctx = Render(Hud(new FakeLocalPlayer { Teamplay = true, Team = 5, TeamName = "Resistance" }));

        Assert.Equal(Tone.TeamD, ctx.Report.TextToneOf(At(ctx, "Resistance")));
    }
}
