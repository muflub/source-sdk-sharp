using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

using TF2.Descent.Server;
using TF2.Descent.Server.Portal;
using TF2.Descent.Tests.Portal;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The town portal's route (plan_descent_logic WO4, <see cref="PortalRoute"/>) over the flows'
/// recording host link: hosted, up is the travel service's <c>IHostLink.Travel</c> with
/// <see cref="TravelDirection.Portal"/> and back is the host's teleporter anchor; unhosted, both
/// are this process's level change.
/// </summary>
public sealed partial class ServiceFlowTests
{
    private static PortalRecord PortalOn(string map = "tf2_descent-1-s1", string character = "c-1", string level = "lvl-a") => new()
    {
        Id = 7,
        Owner = "76561197960287930",
        Character = character,
        Map = map,
        Depth = 1,
        Seed = 1,
        LevelId = level,
        Position = new Vector(64f, 0f, 0f),
        Yaw = 0f,
    };

    private static (PortalRoute Route, World W, PortalRig.FakeAnchors Anchors) HostedRoute()
    {
        var (w, travel, net) = Travel(kind: "level");
        var anchors = new PortalRig.FakeAnchors();
        return (new PortalRoute(w.Host, travel, anchors, w.Console, new ClientMessages(net), w.Log), w, anchors);
    }

    private static (PortalRoute Route, World W) UnhostedRoute()
    {
        var (_, travel, net) = Travel(kind: "level");
        var w = Host(HostingMode.Unhosted);
        w.Host.OnPostInit();
        return (new PortalRoute(w.Host, travel, new PortalRig.FakeAnchors(), w.Console, new ClientMessages(net), w.Log), w);
    }

    [Fact]
    public async Task Hosted_going_up_through_a_portal_asks_the_host_to_travel_up()
    {
        var (route, w, _) = HostedRoute();

        var trip = await route.Up(3, "76561197960287930", PortalOn());

        Assert.True(trip.Started);
        // The depth is the travel service's default, which the host's town portal never reads.
        Assert.Contains("travel 76561197960287930 Portal level 1", w.Link.Calls);
    }

    [Fact]
    public async Task Hosted_a_trip_up_the_host_refuses_does_not_start_and_says_why()
    {
        var (route, w, _) = HostedRoute();
        w.Link.TravelAnswer = new TravelAnswer(TravelState.Refused, TimeSpan.Zero, "no_hub: no live hub");

        var trip = await route.Up(3, "76561197960287930", PortalOn());

        Assert.Equal((false, "no_hub: no live hub"), (trip.Started, trip.Why));
    }

    [Fact]
    public async Task Hosted_coming_back_goes_through_the_owners_anchor_on_its_level_copy()
    {
        var (route, w, anchors) = HostedRoute();

        var trip = await route.Back(3, "76561197960287930", PortalOn());

        Assert.True(trip.Started);
        Assert.Equal(["return 76561197960287930 c-1 lvl-a"], anchors.Calls);
        Assert.DoesNotContain(w.Console.Calls, c => c.Contains("changelevel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hosted_a_return_through_an_anchor_the_host_no_longer_has_is_gone()
    {
        var (route, _, anchors) = HostedRoute();
        anchors.ReturnAnswer = new TravelAnswer(TravelState.Refused, TimeSpan.Zero, "NotFound: no_anchor");

        var trip = await route.Back(3, "76561197960287930", PortalOn());

        Assert.Equal((false, true), (trip.Started, trip.Gone));
    }

    [Fact]
    public async Task Unhosted_going_up_changes_this_process_to_the_town()
    {
        var (route, w) = UnhostedRoute();

        var trip = await route.Up(3, "76561197960287930", PortalOn());

        Assert.True(trip.Started);
        Assert.Equal(["exec changelevel tf2_descent_town"], w.Console.Calls);
        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("travel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unhosted_coming_back_changes_this_process_to_the_portals_own_map()
    {
        var (route, w) = UnhostedRoute();

        var trip = await route.Back(3, "76561197960287930", PortalOn("tf2_descent-1-s1"));

        Assert.True(trip.Started);
        Assert.Equal(["exec changelevel tf2_descent-1-s1"], w.Console.Calls);
    }

    [Fact]
    public async Task Unhosted_a_portal_whose_map_is_not_a_map_name_is_refused_and_nothing_runs()
    {
        var (route, w) = UnhostedRoute();

        var trip = await route.Back(3, "76561197960287930", PortalOn("x; quit"));

        Assert.False(trip.Started);
        Assert.Empty(w.Console.Calls);
    }
}
