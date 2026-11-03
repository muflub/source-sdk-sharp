using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The HUD's first-paint stamp is per level, like the level-init stamps beside
/// it (the <c>_stats.ResetFrames()</c> ruling at <c>ClientModule.LevelInit</c>):
/// the tf2-gate's changelevel step measures the second map load by the line
/// appearing again, and the UI runtime is carried across levels
/// (<c>UiCarryReload</c>). A new map reaches the host through
/// <see cref="IUiRuntime.NewMap"/> — the client fakes game_newmap in the
/// viewport's <c>LevelInit</c> (<c>shell_client.cpp</c>) as stock's viewport
/// fired it, so every map load, first or second, calls it.
/// </summary>
public class UiFirstPaintPerLevelTests
{
    [Fact]
    public void Many_paints_log_the_first_paint_once()
    {
        var log = new RecordingLog();
        var host = UiHostKit.Build(new UiFidelityTests.RecordingSurface(), [], log: log);
        host.Paint(0, 1920, 1080);
        host.Paint(0, 1920, 1080);
        host.Paint(0, 1920, 1080);
        Assert.Equal(1, PaintLines(log));
    }

    [Fact]
    public void A_new_map_arms_the_stamp_again_so_the_next_paint_logs_it()
    {
        var log = new RecordingLog();
        var host = UiHostKit.Build(new UiFidelityTests.RecordingSurface(), [], log: log);
        host.Paint(0, 1920, 1080);
        host.NewMap();
        host.Paint(0, 1920, 1080);
        Assert.Equal(2, PaintLines(log));   // the second level's first paint, measured by the gate
    }

    private static int PaintLines(RecordingLog log) =>
        log.Lines.Count(l => l.Text.Contains("[SourceSharp/Hud] first paint"));
}
