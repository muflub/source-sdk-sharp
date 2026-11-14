using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The create-server game page reads the game-mode catalog (plan_bots_teams_tf2
/// D.2, D.5 step 7): a mode row, the chosen mode's own settings (deathmatch has
/// no team rows), the script's rows the catalog covers drawn by it alone, and a
/// start that saves the mode and its settings so they are set before the map.
/// </summary>
public class CreateServerModeTests
{
    private static (CreateServer Screen, MemoryServerSettings Settings) Open(string? mode = null)
    {
        var services = new UiSampleServices();
        var settings = mode is null ? CreateServerSamples.Script() : CreateServerSamples.ScriptWithMode(mode);
        services.Add<IServerSettingsSource>(settings);
        var screen = CreateServerSamples.Build(services, 1);
        screen.Load();
        return (screen, settings);
    }

    private static List<string> Rows(ScriptRows rows) => [.. rows.Entries.Select(e => e.Name)];

    [Fact]
    public void WithNoModeSavedTheMapDecidesAndNoModeRowsShow()
    {
        var (screen, _) = Open();

        Assert.Equal(("", 0), (screen.ModeId, screen.Mode));
        Assert.Empty(screen.ModeSettings.Entries);
    }

    [Fact]
    public void DeathmatchShowsItsLimitsAndNoTeamRows()
    {
        var (screen, _) = Open("null");

        Assert.Equal("null", screen.ModeId);
        Assert.Equal(["mp_timelimit", "mp_fraglimit"], Rows(screen.ModeSettings));
    }

    [Fact]
    public void TeamDeathmatchShowsTheTeamRows()
    {
        var (screen, _) = Open();

        screen.Mode = 2;
        screen.ShowMode();

        Assert.Contains("mp_tdm_teams", Rows(screen.ModeSettings));
        Assert.Contains("mp_friendlyfire", Rows(screen.ModeSettings));
    }

    [Fact]
    public void TheScriptsRowsTheCatalogDrawsAreNotDrawnTwice()
    {
        var (screen, _) = Open();

        var script = Rows(screen.Game);
        Assert.DoesNotContain("mp_friendlyfire", script);
        Assert.DoesNotContain("mp_timelimit", script);
        Assert.Contains("mp_winlimit", script);
    }

    [Fact]
    public void AValueCarriesToAnotherModeThatSharesTheSetting()
    {
        var (screen, _) = Open("null");
        screen.ModeSettings.Set("mp_fraglimit", "30");

        screen.Mode = 2;
        screen.ShowMode();

        Assert.Equal("30", screen.ModeSettings.Value("mp_fraglimit"));
    }

    [Fact]
    public void StartSavesTheModeAndItsSettingsBeforeTheMap()
    {
        var (screen, settings) = Open("tdm");
        screen.ModeSettings.Set("mp_tdm_teams", "3");

        screen.Start(new Random(1));

        string[] names = settings.SavedNames.Single(), values = settings.Saved.Single();
        Assert.Equal("tdm", values[Array.IndexOf(names, CreateServer.ModeVar)]);
        Assert.Equal("3", values[Array.IndexOf(names, "mp_tdm_teams")]);
    }
}
