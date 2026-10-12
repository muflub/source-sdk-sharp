using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysicsPenetrationLedger against stock's penetration events (physics.cpp):
/// the cases of native penetration_ledger_test.cpp, which it replaces.
/// </summary>
public class PhysicsPenetrationLedgerTests
{
    private static bool BothAlive(uint a, uint b) => true;

    private static bool Has(List<PhysicsPenetrationLedger.Pair> pairs, uint a, uint b)
        => pairs.Any(p => (p.Key0 == a && p.Key1 == b) || (p.Key0 == b && p.Key1 == a));

    [Fact]
    public void The_first_report_adds_the_pair_and_solves_it()
    {
        var ledger = new PhysicsPenetrationLedger();
        var a = ledger.Report(10, 20, 0f, false);
        Assert.True(a.Solve);
        Assert.True(a.Added, "the first report sets FVPHYSICS_PENETRATING on both");
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void A_later_report_is_the_same_pair_in_either_order()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        var a = ledger.Report(20, 10, 0.5f, false);
        Assert.True(a.Solve);
        Assert.False(a.Added);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void Stuck_for_three_seconds_it_is_told_not_to_solve_once_and_slept_next_frame()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        ledger.Report(10, 20, 2.9f, false);
        Assert.False(ledger.Report(10, 20, 3.1f, false).Solve, "stock returns false as it moves the pair to TRYDISABLE");
        Assert.True(Has(ledger.Advance(3.1f, BothAlive).Sleep, 10, 20), "and the next update puts both to sleep");
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void A_player_or_shadow_pair_is_never_slept()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, true);
        Assert.True(ledger.Report(10, 20, 3.1f, true).Solve);
        Assert.Empty(ledger.Advance(3.1f, BothAlive).Sleep);
    }

    [Fact]
    public void The_three_seconds_start_again_after_a_give_up()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, true);
        ledger.Report(10, 20, 3.1f, true);
        Assert.True(ledger.Report(10, 20, 5f, false).Solve, "stock sets startTime = curtime when it gives up");
    }

    [Fact]
    public void Quiet_for_a_second_it_is_dropped_and_its_flag_cleared()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        Assert.Empty(ledger.Advance(0.9f, BothAlive).Removed);
        Assert.True(Has(ledger.Advance(1.1f, BothAlive).Removed, 10, 20));
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void A_slept_pair_is_re_enabled_first_then_dropped()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        ledger.Report(10, 20, 3.1f, false);
        ledger.Advance(3.1f, BothAlive);
        Assert.Empty(ledger.Advance(4.2f, BothAlive).Removed);
        Assert.Equal(1, ledger.Count);
        Assert.True(Has(ledger.Advance(4.3f, BothAlive).Removed, 10, 20), "ENABLED and still quiet: dropped");
    }

    [Fact]
    public void A_gone_entity_drops_its_pair()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        ledger.Report(10, 20, 3.1f, false);
        var u = ledger.Advance(3.1f, (_, _) => false);
        Assert.Empty(u.Sleep);
        Assert.True(Has(u.Removed, 10, 20));
    }

    [Fact]
    public void A_partner_walk_names_every_other_key_of_the_entitys_pairs()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        ledger.Report(30, 10, 0f, false);
        ledger.Report(30, 40, 0f, false);
        Assert.Equal(new uint[] { 10, 40 }, ledger.Partners(30).Order());
        Assert.Empty(ledger.Partners(99));
    }

    [Fact]
    public void The_partner_walk_runs_last_event_first_as_stocks_does()
    {
        // penetration_ledger_test's ThePartnerWalkRunsLastEventFirstAsStocksDoes:
        // GetListOfPenetratingEntities walks m_penetrateEvents from Count()-1
        // down to 0 (physics.cpp:823).
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        ledger.Report(10, 30, 0f, false);
        ledger.Report(10, 40, 0f, false);
        Assert.Equal(3, ledger.Count);
        Assert.Equal(new uint[] { 40, 30, 20 }, ledger.Partners(10).ToArray());
    }

    [Fact]
    public void Debris_stuck_past_half_a_second_goes_to_the_entity_solver_and_is_dropped()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false, debrisStuck: true);
        Assert.True(ledger.Report(10, 20, 0.6f, false, debrisStuck: true).Solve);
        var u = ledger.Advance(0.6f, BothAlive);
        Assert.True(Has(u.Solve, 10, 20));
        Assert.True(Has(u.Removed, 10, 20));
        Assert.Empty(u.Sleep);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void Debris_stuck_under_half_a_second_is_not_yet_solved()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false, debrisStuck: true);
        ledger.Report(10, 20, 0.5f, false, debrisStuck: true);
        Assert.Empty(ledger.Advance(0.5f, BothAlive).Solve);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void A_pair_with_no_debris_never_goes_to_the_entity_solver()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false);
        ledger.Report(10, 20, 0.6f, false);
        Assert.Empty(ledger.Advance(0.6f, BothAlive).Solve);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void Past_the_give_up_the_sleep_overrides_the_solver_as_in_stock()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false, debrisStuck: true);
        Assert.False(ledger.Report(10, 20, 3.1f, false, debrisStuck: true).Solve);
        var u = ledger.Advance(3.1f, BothAlive);
        Assert.True(Has(u.Sleep, 10, 20));
        Assert.Empty(u.Solve);
    }

    [Fact]
    public void A_gone_entity_is_not_handed_to_the_entity_solver()
    {
        var ledger = new PhysicsPenetrationLedger();
        ledger.Report(10, 20, 0f, false, debrisStuck: true);
        ledger.Report(10, 20, 0.6f, false, debrisStuck: true);
        var u = ledger.Advance(0.6f, (_, _) => false);
        Assert.Empty(u.Solve);
        Assert.True(Has(u.Removed, 10, 20));
    }
}
