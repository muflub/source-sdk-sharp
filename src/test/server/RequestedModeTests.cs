using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The mode asked for by name (plan_bots_teams_tf2 D.2): <c>sv_mode_next</c>
/// picks the next level's mode where its map can play it.
/// </summary>
public class RequestedModeTests
{
    private sealed class Level(string map) : ILevelInfo
    {
        public string? Map => map;
        public bool IsReady => true;
    }

    private sealed class FlagMode(IRoundFlow rounds) : IGameMode
    {
        public string Id => "ctf";
        public IRoundFlow Rounds => rounds;
        public IModeSettings Settings => new ModeSettings("#CTF", [], ["item_teamflag"]);
    }

    private static (RequestedModeSelector Selector, SvModeNextConVar Next, RecordingLog Log) Rig(string map, string next)
    {
        var rig = new TeamDeathmatchTests.Rig();
        rig.Services.AddInstance(new FlagMode(rig.Rules));
        var files = new GameModeCatalogTests.Bsps();
        files.Files["maps/dm_lockdown.bsp"] = GameModeCatalogTests.Bsp("info_player_deathmatch");
        var catalog = new GameModeCatalog(rig.Modes, new BspEntityClasses(files), new Level(map));
        var convar = new ConVarRegistry(new NamedConVars(new Dictionary<string, string> { ["sv_mode_next"] = next })).Declare<SvModeNextConVar>();
        var log = new RecordingLog();
        return (new RequestedModeSelector(convar, catalog, log), convar, log);
    }

    [Fact]
    public void EmptyLeavesThePickToTheMap()
        => Assert.Null(Rig("dm_lockdown", "").Selector.PickMode());

    [Fact]
    public void AModeTheMapCanPlayIsPicked()
        => Assert.Equal("tdm", Rig("dm_lockdown", "tdm").Selector.PickMode());

    [Fact]
    public void AModeTheMapCannotPlayIsPassedOverAndSaysWhy()
    {
        var (selector, _, log) = Rig("dm_lockdown", "ctf");

        Assert.Null(selector.PickMode());
        Assert.Contains(log.Lines, l => l.Text.Contains("sv_mode_next 'ctf' passed over: dm_lockdown has no item_teamflag"));
    }
}
