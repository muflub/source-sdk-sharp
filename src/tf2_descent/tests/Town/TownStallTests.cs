using SourceSharp.Abi;

using TF2.Descent.Server.Town;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Town;

/// <summary>
/// The town's stalls stand, open and close (plan_descent_logic WO8): at their markers in the town
/// only; +USE opens a stall's window for its hero alone, its state and what it shows before the
/// panel; walking away closes it; a dev open does not.
/// </summary>
public sealed class TownStallTests
{
    [Fact]
    public void In_the_town_every_stall_stands_at_its_marker_or_beside_the_smithy()
    {
        var rig = new TownRig();

        Assert.Equal(TownStalls.All.Count, rig.World.Made.Count);
        foreach (var (index, stall, at, _) in rig.World.Made)
        {
            var marker = rig.World.Markers[stall.Marker];
            Assert.Equal(new Vector(marker.X + stall.Offset.X, marker.Y + stall.Offset.Y, 0f), at);
        }
        Assert.Equal(-TownStalls.Beside, TownStalls.Find("engineer")!.Offset.X);
        Assert.Equal(TownStalls.Beside, TownStalls.Find("bench")!.Offset.X);
    }

    [Fact]
    public void A_stall_faces_the_street()
    {
        var rig = new TownRig();

        Assert.All(rig.World.Made, m => Assert.Equal(rig.World.Markers[m.Stall.Marker].Y > 0 ? 270f : 90f, m.Yaw));
    }

    [Fact]
    public void Off_the_town_no_stall_stands()
    {
        var rig = new TownRig(place: false);

        rig.Service.Place("tf2_descent-1-s1");

        Assert.Empty(rig.World.Made);
        Assert.False(rig.Service.InTown);
    }

    [Fact]
    public void A_stall_whose_marker_the_map_lacks_stands_nowhere_and_the_rest_stand()
    {
        var rig = new TownRig(place: false);
        rig.World.Markers.Remove("shop_saloon");

        rig.Service.Place(TownRig.Town);

        Assert.DoesNotContain(rig.World.Made, m => m.Stall.Id == "medic");
        Assert.Equal(TownStalls.All.Count - 1, rig.World.Made.Count);
        Assert.Contains(rig.Owner.Gear.Log.Lines, l => l.Warning && l.Text.Contains("shop_saloon", StringComparison.Ordinal));
    }

    [Fact]
    public void Using_a_stall_sends_its_window_before_the_panel_to_its_hero_alone()
    {
        var rig = new TownRig();
        int mark = rig.Mark;

        rig.Use("saxton");

        var sent = rig.Sent(mark);
        Assert.NotEmpty(sent);
        Assert.All(sent, s => Assert.Equal(TownRig.Index, s.Player));
        Assert.Equal(TownWindowMessage.Name, sent[0].Key.Split(':')[^1]);
        var window = Assert.Single(rig.Of<TownWindow>(mark)).View();
        Assert.Equal(TownScreen.Vendor, window.Screen);
        Assert.Equal(TownStance.Weapons, window.Stance);
        Assert.Equal(TownRig.StallOf("saxton"), window.Stall);
        Assert.Equal((TownRig.Index, "descent_vendor", true), rig.World.Panels.Last());
    }

    [Fact]
    public void Walking_away_from_a_stall_closes_its_window_on_the_client()
    {
        var rig = new TownRig();
        rig.Use("medic");
        rig.Service.CheckReach();
        Assert.NotNull(rig.Service.Open(TownRig.Index));

        rig.World.Feet = rig.World.Feet!.Value + new Vector(TownService.Reach + 1f, 0f, 0f);
        rig.Service.CheckReach();

        Assert.Null(rig.Service.Open(TownRig.Index));
        Assert.Equal((TownRig.Index, "descent_vendor", false), rig.World.Panels.Last());
    }

    [Fact]
    public void A_window_opened_by_the_dev_command_stays_open_however_far_the_hero_is()
    {
        var rig = new TownRig();
        rig.World.Feet = new Vector(5000f, 5000f, 0f);

        Assert.True(rig.Service.OpenStall(TownRig.Index, TownRig.StallOf("scout"), reach: false));
        rig.Service.CheckReach();

        Assert.Equal(TownScreen.Crate, rig.Service.Open(TownRig.Index)?.Screen);
    }

    [Fact]
    public void Each_screen_opens_its_own_panel()
    {
        var rig = new TownRig();

        foreach (var (stall, panel) in new[] { ("stash", "descent_stash"), ("bench", "descent_craftbench"), ("scout", "descent_crate"),
                                               ("spy", "descent_vendor") })
        {
            rig.Use(stall);
            Assert.Equal((TownRig.Index, panel, true), rig.World.Panels.Last());
        }
    }

    [Fact]
    public void Opening_another_stall_closes_the_last_ones_panel_first()
    {
        var rig = new TownRig();
        rig.Use("stash");

        rig.Use("bench");

        Assert.Equal([(TownRig.Index, "descent_stash", false), (TownRig.Index, "descent_craftbench", true)], rig.World.Panels.TakeLast(2));
    }
}
