using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

using TF2.Descent.Server;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// A level's way down with no host (the whole run, 2026-11-14): the town's cage changes level
/// to an installed level, and so must a level's own cage, or an unhosted run stops at depth 1.
/// </summary>
public sealed partial class ServiceFlowTests
{
    /// <summary>An unhosted travel service on a level of <paramref name="depth"/> (0: the town) over a pool holding <paramref name="installed"/>.</summary>
    private static (TravelService Travel, FakeConsole Console, RecordingEngine Net, string Pool) UnhostedTravel(int depth, params string[] installed) =>
        UnhostedTravel(out _, depth, installed);

    private static (TravelService Travel, FakeConsole Console, RecordingEngine Net, string Pool) UnhostedTravel(out ClientMessages messages, int depth,
                                                                                                             params string[] installed)
    {
        var (_, players, _, net) = Players("level", levelDepth: depth);
        var w = Host(HostingMode.Unhosted);
        w.Host.OnPostInit();
        string game = Directory.CreateTempSubdirectory("tf2_descent-unhosted-").FullName;
        var hop = new LevelHop(new PoolMod(game), new FakeConsole(), new ListLog());
        Directory.CreateDirectory(hop.PoolMaps);
        foreach (string map in installed)
            File.WriteAllText(Path.Combine(hop.PoolMaps, map + ".bsp"), map);
        var level = new Floor.RigLevel(depth, 1);
        if (depth == 0)
            level.Current = null;
        messages = new ClientMessages(net);
        var travel = new TravelService(w.Link, w.Host, players, messages, new Kills(), new AnyoneTravels(),
                                       level, hop, w.Console, w.Log);
        return (travel, w.Console, net, game);
    }

    [Fact]
    public void Unhosted_a_levels_way_down_changes_level_to_the_next_depths_installed_level()
    {
        var (travel, console, net, game) = UnhostedTravel(out var messages, 1, "tf2_descent-2-s3", "tf2_descent-2-s1", "tf2_descent-3-s1");
        try
        {
            travel.Request(3, TravelDirection.Down);
            var before = NoticesSent(net);
            messages.LevelBegan();
            messages.SendArrivalNotices(3);

            Assert.Equal(["exec changelevel tf2_descent-2-s1"], console.Calls.Where(c => c.StartsWith("exec changelevel", StringComparison.Ordinal)));
            // The cage's line goes on the next level (F28): sent before the changelevel it never reached the HUD.
            Assert.Empty(before);
            Assert.Equal([(WaysDown.Token(WayDown.Cage), "")], NoticesSent(net));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void Unhosted_a_levels_way_down_with_no_level_below_tells_the_hero_and_stays()
    {
        var (travel, console, net, game) = UnhostedTravel(4, "tf2_descent-4-s1");
        try
        {
            travel.Request(3, TravelDirection.Down);

            Assert.DoesNotContain(console.Calls, c => c.StartsWith("exec changelevel", StringComparison.Ordinal));
            Assert.Equal([(TravelService.NoLevelToken, "5")], NoticesSent(net));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void Unhosted_a_levels_way_up_changes_level_to_the_town()
    {
        var (travel, console, net, game) = UnhostedTravel(out var messages, 5, "tf2_descent-4-s1");
        try
        {
            travel.Request(3, TravelDirection.Up);
            var before = NoticesSent(net);
            messages.LevelBegan();
            messages.SendArrivalNotices(3);

            Assert.Equal(["exec changelevel " + TF2.Descent.Server.Portal.PortalRoute.TownMap],
                         console.Calls.Where(c => c.StartsWith("exec changelevel", StringComparison.Ordinal)));
            Assert.Empty(before);
            Assert.Equal([(TravelService.UpToken, "")], NoticesSent(net));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void Unhosted_off_a_level_travel_still_changes_nothing()
    {
        var (travel, console, net, game) = UnhostedTravel(0, "tf2_descent-1-s1");
        try
        {
            travel.Request(3, TravelDirection.Down);

            Assert.Equal((0, 0), (console.Calls.Count(c => c.StartsWith("exec changelevel", StringComparison.Ordinal)), Notices(net)));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }
}
