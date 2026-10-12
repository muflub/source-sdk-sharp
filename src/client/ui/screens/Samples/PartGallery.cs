using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens.Samples;

/// <summary>The data-driven composites in the gallery: TeamTable and HudMeter in their states.</summary>
internal static class PartGallery
{
    [UiGallery("TeamTable", "six players")]
    private static UiScreen TeamFull(UiSampleServices s) => Table(s.Add<IScoreboardSource>(FakeScoreboard.Teams(6)), 0, Tone.TeamA);

    [UiGallery("TeamTable", "empty team")]
    private static UiScreen TeamEmpty(UiSampleServices s) => Table(s.Add<IScoreboardSource>(FakeScoreboard.Empty()), 1, Tone.TeamB);

    private static UiScreen Table(UiSampleServices s, int team, Tone tone)
    {
        var table = s.Create<TeamTable>();
        return new UiPartPreview("TeamTable", (ref UiFrame ui) =>
        {
            using (ui.Panel().Width(Size.Du(560)))
                ui.Sub("team", table, new TeamTableProps(team, tone));
        });
    }

    [UiGallery("HudMeter", "healthy")]
    private static UiScreen MeterHealthy(UiSampleServices s)
        => new UiPanelPreview<HudMeterProps>("HudMeter", new HudMeter(),
            new HudMeterProps("#SS_UI_Health", UiShape.Cross, 100, 100, 25, Tone.Accent), UiAnchor.Centre);

    [UiGallery("HudMeter", "low health")]
    private static UiScreen MeterLow(UiSampleServices s)
        => new UiPanelPreview<HudMeterProps>("HudMeter", new HudMeter(),
            new HudMeterProps("#SS_UI_Health", UiShape.Cross, 18, 100, 25, Tone.Accent), UiAnchor.Centre);

    [UiGallery("HudMeter", "armour")]
    private static UiScreen MeterArmour(UiSampleServices s)
        => new UiPanelPreview<HudMeterProps>("HudMeter", new HudMeter(),
            new HudMeterProps("#SS_UI_Suit", UiShape.Shield, 60, 100, 0, Tone.Accent), UiAnchor.Centre);
}

/// <summary>The icon set (src/client/ui/icons) as a sheet: every icon with its name, at 1× (36 du: 24 px on this 720p sheet) and 2×.</summary>
internal static class IconGallery
{
    [UiGallery("Icons", "1x")]
    private static UiScreen One(UiSampleServices s) => Sheet(36);

    [UiGallery("Icons", "2x")]
    private static UiScreen Two(UiSampleServices s) => Sheet(72);

    private static UiScreen Sheet(float heightDu) => new UiPartPreview("Icons", (ref UiFrame ui) =>
    {
        var icons = ui.Icons;
        int perRow = heightDu > 40 ? 5 : 6;
        float cell = heightDu > 40 ? 340 : 280;
        using (ui.Panel(Space.XL, Space.XL))
        {
            for (int row = 0; row * perRow < icons.Count; row++)
            {
                using (ui.Row(Space.XL, Align.End).Key(row))
                {
                    for (int i = row * perRow; i < Math.Min(icons.Count, (row + 1) * perRow); i++)
                    {
                        var icon = icons.Icon(i);
                        using (ui.Column(Space.S, Align.Start).Key(1000 + i).Width(Size.Du(cell)))
                        {
                            ui.Icon(icon.Name, Tone.Text, heightDu).Name(icon.Name);
                            ui.Data(icon.Name, Text.Caption, Tone.TextMuted);
                        }
                    }
                }
            }
        }
    });
}
