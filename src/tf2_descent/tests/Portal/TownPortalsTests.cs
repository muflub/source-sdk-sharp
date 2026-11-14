using SourceSharp.Abi;

using TF2.Descent.Server;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Portal;
using TF2.Descent.Shared;
using Xunit;

namespace TF2.Descent.Tests.Portal;

/// <summary>
/// The town portal (plan_descent_logic WO4, ruling L10) over a fake world: opening, going up,
/// arriving on the exit, coming back once, the portal spent and a second return refused, a gone
/// level, the record across map changes, and the hosted arms over a recording anchor store.
/// </summary>
public sealed class TownPortalsTests
{
    //-------------------------------------------------------------------------
    // Opening
    //-------------------------------------------------------------------------

    [Fact]
    public void Opening_on_a_level_stands_an_entrance_where_the_placement_allows()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Level);
        rig.World.Placing = _ => PortalPlace.At(new Vector(40f, 30f, 8f), 45f);

        Assert.True(rig.Portals.Open(PortalRig.Slot).Opened);

        var pad = Assert.Single(rig.World.Pads.Values);
        Assert.Equal(new PortalRig.FakePad(1, PortalEnd.Entrance, new Vector(40f, 30f, 8f), 45f), pad);
    }

    [Fact]
    public void Opening_in_town_is_refused_and_builds_nothing()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Town);

        var answer = rig.Portals.Open(PortalRig.Slot);

        Assert.Equal(TownPortals.InTown, answer.Why);
        Assert.Empty(rig.World.Pads);
        Assert.Empty(rig.Portals.All);
    }

    [Fact]
    public void Opening_by_a_dead_hero_is_refused()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Level);
        rig.World.Heroes[PortalRig.Slot].Alive = false;

        Assert.Equal(TownPortals.Dead, rig.Portals.Open(PortalRig.Slot).Why);
        Assert.Empty(rig.World.Pads);
    }

    [Fact]
    public void Opening_with_no_room_is_refused_with_the_placements_reason()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Level);
        rig.World.Placing = _ => PortalPlace.Refused(PortalPlacement.NoRoom);

        Assert.Equal(PortalPlacement.NoRoom, rig.Portals.Open(PortalRig.Slot).Why);
        Assert.Empty(rig.World.Pads);
        Assert.Empty(rig.Portals.All);
    }

    [Fact]
    public void Opening_when_the_engine_refuses_the_pad_is_refused_and_remembers_nothing()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Level);
        rig.World.RefusePads = true;

        Assert.Equal(TownPortals.NotBuilt, rig.Portals.Open(PortalRig.Slot).Why);
        Assert.Empty(rig.Portals.All);
    }

    [Fact]
    public void Opening_outside_a_Descent_game_is_refused()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Level);
        rig.Mode.Active = false;

        Assert.Equal(TownPortals.NotHere, rig.Portals.Open(PortalRig.Slot).Why);
    }

    [Fact]
    public void The_record_holds_the_owner_the_level_its_depth_and_seed_and_the_pads_place_and_facing()
    {
        var rig = new PortalRig();
        rig.World.Heroes[PortalRig.Slot].Team = 3;
        rig.Load(PortalRig.Level);
        rig.World.Placing = _ => PortalPlace.At(new Vector(10f, 20f, 30f), 90f);
        rig.Portals.Open(PortalRig.Slot);

        var r = Assert.Single(rig.Portals.All);
        Assert.Equal((PortalRig.Hero, PortalRig.Level, 1, (ulong?)1UL, new Vector(10f, 20f, 30f), 90f, 3, PortalState.Open),
                     (r.Owner, r.Map, r.Depth, r.Seed, r.Position, r.Yaw, r.Team, r.State));
    }

    [Fact]
    public void A_second_scroll_moves_the_portal_the_first_is_gone_and_its_pad_removed()
    {
        var rig = new PortalRig();
        var first = rig.Opened();
        int firstPad = rig.Portals.PadOf(first);

        Assert.True(rig.Portals.Open(PortalRig.Slot).Opened);

        Assert.Equal(PortalState.Gone, first.State);
        Assert.Contains(firstPad, rig.World.Removed);
        Assert.Equal(PortalState.Open, rig.Portals.All[^1].State);
        Assert.Single(rig.World.Pads);
    }

    [Fact]
    public void Hosted_the_host_is_told_where_the_teleporter_stands()
    {
        var rig = new PortalRig(hosted: true);
        rig.Load(PortalRig.Level);
        rig.World.Placing = _ => PortalPlace.At(new Vector(64f, 0f, 0f), 0f);

        rig.Portals.Open(PortalRig.Slot);

        Assert.Equal([$"place {PortalRig.Hero} (64 0 0) 0"], rig.Anchors.Calls.Where(c => c.StartsWith("place", StringComparison.Ordinal)));
    }

    [Fact]
    public void Unhosted_opening_asks_nothing_of_the_host()
    {
        var rig = new PortalRig();
        rig.Opened();

        Assert.Empty(rig.Anchors.Calls);
    }

    //-------------------------------------------------------------------------
    // Going up
    //-------------------------------------------------------------------------

    [Fact]
    public void Standing_on_the_entrance_for_the_charge_time_goes_up()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds);

        Assert.Equal([$"up {PortalRig.Slot} {PortalRig.Hero} portal {portal.Id}"], rig.Route.Calls);
    }

    [Fact]
    public void Standing_on_it_for_less_than_the_charge_time_does_not_go_up()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds - TownPortals.TickSeconds);

        Assert.Empty(rig.Route.Calls);
    }

    [Fact]
    public void Stepping_off_starts_the_charge_again()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds - TownPortals.TickSeconds);
        rig.StepOff(PortalRig.Slot);
        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds - TownPortals.TickSeconds);

        Assert.Empty(rig.Route.Calls);
    }

    [Fact]
    public void Going_up_shows_the_departure_and_marks_the_traveller_for_the_exit()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        int pad = rig.Portals.PadOf(portal);

        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds);

        Assert.Contains((PortalShow.Departed, pad, PortalRig.Slot), rig.World.Shows);
        Assert.Equal((PortalRig.Hero, PortalEnd.Exit, 1), (portal.Arriving, portal.ArrivalEnd, portal.Trips));
    }

    [Fact]
    public void Another_hero_standing_on_the_owners_pad_is_refused()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.World.Heroes[PortalRig.Other] = new PortalRig.FakeHero(PortalRig.OtherHero);

        rig.StandOn(PortalRig.Other, portal.Position, TownPortals.ChargeSeconds);

        Assert.Empty(rig.Route.Calls);
        Assert.Contains((PortalRig.Other, TownPortals.NotYours, ""), rig.World.Notices);
    }

    [Fact]
    public void A_hero_the_party_allows_may_use_the_owners_pad()
    {
        var rig = new PortalRig(party: new Everyone());
        var portal = rig.Opened();
        rig.World.Heroes[PortalRig.Other] = new PortalRig.FakeHero(PortalRig.OtherHero);

        rig.StandOn(PortalRig.Other, portal.Position, TownPortals.ChargeSeconds);

        Assert.Equal([$"up {PortalRig.Other} {PortalRig.OtherHero} portal {portal.Id}"], rig.Route.Calls);
    }

    [Fact]
    public void A_refused_trip_up_keeps_the_portal_open_and_tells_why()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.Route.UpAnswer = new PortalTrip(false, "no live hub");

        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds);

        Assert.Equal(PortalState.Open, portal.State);
        Assert.Null(portal.Arriving);
        Assert.Contains((PortalRig.Slot, TownPortals.Refused, "no live hub"), rig.World.Notices);
    }

    [Fact]
    public async Task A_use_while_its_trip_is_on_the_way_is_refused_as_busy()
    {
        var rig = new PortalRig();
        rig.Opened();
        rig.Route.Hold = new TaskCompletionSource<PortalTrip>();
        var first = rig.Portals.Use(PortalRig.Slot);

        var second = await rig.Portals.Use(PortalRig.Slot);

        Assert.False(first.IsCompleted);
        Assert.Equal(TownPortals.Busy, second.Why);
        rig.Route.Hold.SetResult(PortalTrip.Go);
        Assert.True((await first).Started);
    }

    //-------------------------------------------------------------------------
    // The town
    //-------------------------------------------------------------------------

    [Fact]
    public void In_town_an_open_portal_has_its_exit_pad_at_the_towns_spot()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        rig.Load(PortalRig.Town);

        Assert.Equal(new PortalRig.FakePad(portal.Id, PortalEnd.Exit, new Vector(1000f, 0f, 0f), 180f), rig.PadOf(portal));
    }

    [Fact]
    public void The_hero_who_went_up_is_set_on_the_exit_pads_deck()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds);

        rig.Load(PortalRig.Town);
        rig.Portals.Tick(TownPortals.TickSeconds);

        Assert.Equal((PortalRig.Slot, new Vector(1000f, 0f, TownPortals.DeckHeight), 180f), Assert.Single(rig.World.Set));
        Assert.Null(portal.Arriving);
    }

    [Fact]
    public void A_hero_set_on_the_exit_is_not_sent_back_until_he_steps_off()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds);
        rig.Load(PortalRig.Town);
        rig.Portals.Tick(TownPortals.TickSeconds);

        for (int i = 0; i < 30; i++)
            rig.Portals.Tick(TownPortals.TickSeconds);

        Assert.DoesNotContain(rig.Route.Calls, c => c.StartsWith("back", StringComparison.Ordinal));
    }

    [Fact]
    public void A_town_with_no_place_for_an_exit_builds_none()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.World.NoTownSpot = true;

        rig.Load(PortalRig.Town);

        Assert.Null(rig.PadOf(portal));
    }

    //-------------------------------------------------------------------------
    // Coming back, once
    //-------------------------------------------------------------------------

    /// <summary>Opened on the level, gone up, set on the exit in town, stepped off.</summary>
    private static (PortalRig Rig, PortalRecord Portal) InTown()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.StandOn(PortalRig.Slot, portal.Position, TownPortals.ChargeSeconds);
        rig.Load(PortalRig.Town);
        rig.Portals.Tick(TownPortals.TickSeconds);
        rig.StepOff(PortalRig.Slot);
        rig.Route.Calls.Clear();
        return (rig, portal);
    }

    [Fact]
    public void Standing_on_the_exit_goes_back_to_the_remembered_map_depth_and_seed()
    {
        var (rig, portal) = InTown();

        rig.StandOn(PortalRig.Slot, rig.PadOf(portal)!.At, TownPortals.ChargeSeconds);

        Assert.Equal([$"back {PortalRig.Slot} {PortalRig.Hero} portal {portal.Id} to {PortalRig.Level} depth 1 seed 1 level "], rig.Route.Calls);
    }

    [Fact]
    public void After_the_return_the_portal_is_spent_and_its_exit_pad_is_gone()
    {
        var (rig, portal) = InTown();
        int exit = rig.Portals.PadOf(portal);

        rig.StandOn(PortalRig.Slot, rig.PadOf(portal)!.At, TownPortals.ChargeSeconds);

        Assert.Equal(PortalState.Spent, portal.State);
        Assert.Contains(exit, rig.World.Removed);
        Assert.Empty(rig.World.Pads);
    }

    [Fact]
    public void Back_on_the_level_the_hero_stands_at_the_entrances_place_and_no_entrance_is_built()
    {
        var (rig, portal) = InTown();
        rig.StandOn(PortalRig.Slot, rig.PadOf(portal)!.At, TownPortals.ChargeSeconds);

        rig.Load(PortalRig.Level);
        rig.Portals.Tick(TownPortals.TickSeconds);

        Assert.Equal((PortalRig.Slot, portal.Position + new Vector(0f, 0f, TownPortals.DeckHeight), portal.Yaw), rig.World.Set[^1]);
        Assert.Empty(rig.World.Pads);
    }

    [Fact]
    public void An_unhosted_return_says_the_level_was_loaded_anew()
    {
        var (rig, portal) = InTown();
        rig.StandOn(PortalRig.Slot, rig.PadOf(portal)!.At, TownPortals.ChargeSeconds);

        rig.Load(PortalRig.Level);
        rig.Portals.Tick(TownPortals.TickSeconds);

        Assert.Contains(rig.Log.Lines, l => l.StartsWith("[TF2Descent/Portal] unhosted: depth 1 seed 1 was loaded anew", StringComparison.Ordinal));
        Assert.Contains((PortalRig.Slot, TownPortals.Anew, ""), rig.World.Notices);
    }

    [Fact]
    public async Task A_second_return_through_a_spent_portal_is_refused()
    {
        var (rig, portal) = InTown();
        rig.StandOn(PortalRig.Slot, rig.PadOf(portal)!.At, TownPortals.ChargeSeconds);
        rig.Load(PortalRig.Town);
        rig.Route.Calls.Clear();

        var again = await rig.Portals.Use(PortalRig.Slot, portal.Id);

        Assert.Equal(TownPortals.Spent, again.Why);
        Assert.Empty(rig.Route.Calls);
    }

    [Fact]
    public async Task A_spent_portal_has_no_exit_in_town_and_the_hero_is_told_it_is_spent()
    {
        var (rig, portal) = InTown();
        rig.StandOn(PortalRig.Slot, rig.PadOf(portal)!.At, TownPortals.ChargeSeconds);

        rig.Load(PortalRig.Town);
        var again = await rig.Portals.Use(PortalRig.Slot);

        Assert.Null(rig.PadOf(portal));
        Assert.Equal(TownPortals.Spent, again.Why);
    }

    //-------------------------------------------------------------------------
    // A gone level
    //-------------------------------------------------------------------------

    [Fact]
    public void A_portal_whose_level_was_left_for_another_level_is_gone()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        rig.Load("tf2_descent-2-s1");

        Assert.Equal(PortalState.Gone, portal.State);
    }

    [Fact]
    public async Task A_gone_portal_has_no_exit_in_town_and_its_use_is_refused_with_a_notice()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.Load("tf2_descent-2-s1");

        rig.Load(PortalRig.Town);
        var used = await rig.Portals.Use(PortalRig.Slot, portal.Id);

        Assert.Null(rig.PadOf(portal));
        Assert.Equal(TownPortals.Gone, used.Why);
        Assert.Contains((PortalRig.Slot, TownPortals.Gone, ""), rig.World.Notices);
    }

    [Fact]
    public async Task A_return_the_host_refuses_as_no_anchor_closes_the_portal_and_removes_its_town_pad()
    {
        var (rig, portal) = InTown();
        int exit = rig.Portals.PadOf(portal);
        rig.Route.BackAnswer = new PortalTrip(false, "NotFound: no_anchor", Gone: true);

        var used = await rig.Portals.Use(PortalRig.Slot);

        Assert.Equal(TownPortals.Gone, used.Why);
        Assert.Equal(PortalState.Gone, portal.State);
        Assert.Contains(exit, rig.World.Removed);
    }

    //-------------------------------------------------------------------------
    // Across map changes
    //-------------------------------------------------------------------------

    [Fact]
    public void The_record_survives_a_reload_of_its_level_which_builds_its_entrance_again()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        int before = rig.Portals.PadOf(portal);

        rig.Load(PortalRig.Level);

        Assert.Same(portal, Assert.Single(rig.Portals.All));
        Assert.Equal(PortalState.Open, portal.State);
        Assert.NotEqual(before, rig.Portals.PadOf(portal));
        Assert.Equal(new PortalRig.FakePad(portal.Id, PortalEnd.Entrance, portal.Position, portal.Yaw), rig.PadOf(portal));
    }

    [Fact]
    public void No_pad_of_an_earlier_map_is_kept_after_the_map_goes()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        rig.Portals.OnLevelShutdown();

        Assert.Equal(0, rig.Portals.PadOf(portal));
    }

    [Fact]
    public void The_portal_outlives_its_owners_disconnect()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.World.Heroes.Remove(PortalRig.Slot);

        rig.Load(PortalRig.Town);

        Assert.Equal(PortalState.Open, portal.State);
        Assert.Equal(PortalEnd.Exit, rig.PadOf(portal)!.End);
    }

    [Fact]
    public void Closing_an_owners_portals_removes_the_pad_and_marks_them_gone()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        int pad = rig.Portals.PadOf(portal);

        Assert.Equal(1, rig.Portals.OwnerGone(PortalRig.Hero, "character deleted"));

        Assert.Equal((PortalState.Gone, "character deleted"), (portal.State, portal.Note));
        Assert.Contains(pad, rig.World.Removed);
    }

    //-------------------------------------------------------------------------
    // Hosted
    //-------------------------------------------------------------------------

    [Fact]
    public void Hosted_the_town_lays_an_exit_pad_for_each_teleporter_the_host_lists_for_the_party()
    {
        var rig = new PortalRig(hosted: true);
        rig.World.Heroes[PortalRig.Slot].Character = "c-1";
        rig.Anchors.PartyAnswer.Add(new PortalAnchor("c-1", "lvl-a", 2, new Vector(5f, 6f, 7f), 30f));
        rig.Anchors.PartyAnswer.Add(new PortalAnchor("c-2", "lvl-b", 3, new Vector(1f, 2f, 3f), 0f));
        rig.Load(PortalRig.Town);

        rig.Portals.Tick(TownPortals.TickSeconds);

        Assert.Equal([PortalEnd.Exit, PortalEnd.Exit], rig.World.Pads.Values.Select(p => p.End));
        Assert.Equal([PortalRig.Hero, "character:c-2"], rig.Portals.All.Select(r => r.Owner));
    }

    [Fact]
    public void Hosted_a_hero_the_town_sent_through_a_teleporter_spends_it_and_the_host_forgets_it()
    {
        var rig = new PortalRig(hosted: true);
        rig.World.LevelId = "lvl-a";
        rig.World.Heroes[PortalRig.Slot].Character = "c-1";
        var portal = rig.Opened();
        rig.Anchors.Arrivals[PortalRig.Hero] = "c-1";

        rig.Portals.Tick(TownPortals.TickSeconds);
        rig.Portals.Tick(TownPortals.TickSeconds);

        Assert.Equal(PortalState.Spent, portal.State);
        Assert.Contains("clear " + PortalRig.Hero, rig.Anchors.Calls);
        Assert.Equal((PortalRig.Slot, portal.Position + new Vector(0f, 0f, TownPortals.DeckHeight), portal.Yaw), rig.World.Set[^1]);
    }

    [Fact]
    public void Hosted_a_fresh_instance_builds_the_entrances_the_host_lists_for_its_level()
    {
        var rig = new PortalRig(hosted: true);
        rig.World.LevelId = "lvl-a";
        rig.Anchors.LevelAnswer.Add(new PortalAnchor("c-9", "lvl-a", 1, new Vector(5f, 6f, 7f), 30f));
        rig.Load(PortalRig.Level);

        rig.Portals.Tick(TownPortals.TickSeconds);

        var r = Assert.Single(rig.Portals.All);
        Assert.Equal(new PortalRig.FakePad(r.Id, PortalEnd.Entrance, new Vector(5f, 6f, 7f), 30f), rig.PadOf(r));
    }

    [Fact]
    public void Hosted_a_return_names_the_level_copy_the_portal_stands_on()
    {
        var rig = new PortalRig(hosted: true);
        rig.World.LevelId = "lvl-a";
        var portal = rig.Opened();

        Assert.Equal("lvl-a", portal.LevelId);
    }

    //-------------------------------------------------------------------------
    // Orders, the dev listing
    //-------------------------------------------------------------------------

    [Fact]
    public void The_portals_level_work_runs_after_the_levels_data_is_read()
        => Assert.True(TownPortals.Order > LevelService.Order, $"{TownPortals.Order} <= {LevelService.Order}");

    [Fact]
    public void The_listing_names_each_portal_its_state_and_this_maps_pad()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        string report = rig.Portals.Report();

        Assert.Contains($"portal {portal.Id}: Open, owner {PortalRig.Hero}, {PortalRig.Level} depth 1 seed 1", report);
        Assert.Contains($"here: entrance pad #{rig.Portals.PadOf(portal)}", report);
    }

    private sealed class Everyone : IPortalParty
    {
        public bool MayUse(string owner, string hero) => true;
    }
}
