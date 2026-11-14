using System.Text;
using System.Text.Json.Nodes;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// The furniture's numbers the world carries out (plan_descent_logic WO3 over RU11): an
/// explosive's health and the delay down its chain, a trap's trigger, fuse and re-arm, the
/// Dispenser's reach, and the load errors that keep them in range.
/// </summary>
public sealed class FixtureClockTests
{
    private static readonly TF2DescentRules Rules = new();
    private static FurnitureRules F => Rules.Furniture;
    private static FurnitureSettings Table => Rules.Content.Furniture;

    //-------------------------------------------------------------------------
    // Explosives
    //-------------------------------------------------------------------------

    [Fact]
    public void A_barrel_stands_with_its_rows_health()
    {
        var barrel = F.Explosives.New("b1", "barrel");

        Assert.Equal(40, Table.Explosive("barrel").Health);
        Assert.Equal(40, barrel.Health);
        Assert.False(barrel.Exploded);
    }

    [Fact]
    public void A_barrel_goes_off_on_the_hit_that_takes_its_last_health()
    {
        var first = F.Explosives.Hit(F.Explosives.New("b1", "barrel"), 39);
        var second = F.Explosives.Hit(first.After, 1);

        Assert.False(first.SetOff);
        Assert.Equal(1, first.After.Health, 6);
        Assert.True(second.SetOff);
        Assert.True(second.After.Exploded);
    }

    [Fact]
    public void A_barrel_that_went_off_is_not_set_off_again()
    {
        var gone = F.Explosives.Hit(F.Explosives.New("b1", "barrel"), 500).After;

        var again = F.Explosives.Hit(gone, 500);

        Assert.True(gone.Exploded);
        Assert.False(again.SetOff);
        Assert.Same(gone, again.After);
    }

    [Fact]
    public void A_pumpkin_bomb_goes_off_on_any_hit()
    {
        Assert.True(F.Explosives.Hit(F.Explosives.New("p", "pumpkin_bomb"), 1).SetOff);
    }

    [Fact]
    public void Each_explosive_of_a_chain_goes_off_its_setter_offs_delay_after_it()
    {
        // a sets off b (100 away) and c (170 away); b sets off d (150 beyond b, 250 from a).
        ExplosiveAt[] standing =
        [
            new("a", "barrel", new Point3(0, 0, 0)), new("b", "barrel", new Point3(100, 0, 0)),
            new("c", "pumpkin_bomb", new Point3(0, 170, 0)), new("d", "barrel", new Point3(250, 0, 0)),
        ];

        var steps = F.Explosives.Schedule("a", standing);

        double delay = Table.Explosive("barrel").ChainDelay;
        Assert.Equal(["a", "b", "c", "d"], steps.Select(s => s.Marker));
        Assert.Equal([0, delay, delay, 2 * delay], steps.Select(s => s.After));
        Assert.Equal(F.Explosives.Detonate("a", standing, []).Exploded, steps.Select(s => s.Marker));
    }

    [Fact]
    public void A_chain_radius_below_the_blast_radius_is_a_load_error()
    {
        var errors = Errors(n => n["explosives"]![0]!["chainRadius"] = 100);

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Furniture, ContentRule.OutOfRange), (e.Table, e.Rule));
        Assert.Contains("chainRadius", e.Message);
    }

    [Fact]
    public void An_explosive_with_no_health_is_a_load_error()
    {
        var e = Assert.Single(Errors(n => n["explosives"]![0]!["health"] = 0));

        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("health", e.Message);
    }

    //-------------------------------------------------------------------------
    // Traps
    //-------------------------------------------------------------------------

    [Fact]
    public void A_trap_trips_and_fires_its_fuse_later()
    {
        var row = Table.Trap("dart");
        var tripped = F.Traps.Trip(F.Traps.New("t", "dart"), 10, robot: false);

        var early = F.Traps.Tick(tripped, 10 + row.Fuse / 2);
        var due = F.Traps.Tick(tripped, 10 + row.Fuse);

        Assert.Equal(TrapPhase.Tripped, tripped.Phase);
        Assert.Null(early.Fired);
        Assert.Equal(F.Traps.Fire("dart"), due.Fired);
    }

    [Fact]
    public void A_trap_re_arms_its_rearm_time_after_it_fires()
    {
        var row = Table.Trap("dart");
        var fired = F.Traps.Tick(F.Traps.Trip(F.Traps.New("t", "dart"), 0, false), row.Fuse).After;

        var still = F.Traps.Tick(fired, row.Fuse + row.Rearm / 2).After;
        var armed = F.Traps.Tick(fired, row.Fuse + row.Rearm).After;

        Assert.Equal(TrapPhase.Rearming, fired.Phase);
        Assert.Equal(TrapPhase.Rearming, still.Phase);
        Assert.Equal(TrapPhase.Armed, armed.Phase);
    }

    [Fact]
    public void A_trap_that_does_not_re_arm_is_spent_and_cannot_be_tripped()
    {
        Assert.Equal(0, Table.Trap("charge").Rearm);
        var spent = F.Traps.Tick(F.Traps.Trip(F.Traps.New("t", "charge"), 0, false), 100).After;

        Assert.Equal(TrapPhase.Spent, spent.Phase);
        Assert.Same(spent, F.Traps.Trip(spent, 200, false));
    }

    [Fact]
    public void A_tripped_trap_is_not_tripped_again_before_it_fires()
    {
        var tripped = F.Traps.Trip(F.Traps.New("t", "dart"), 0, false);

        Assert.Same(tripped, F.Traps.Trip(tripped, 0.1, false));
    }

    [Fact]
    public void A_robot_trips_a_trap_whose_row_says_so()
    {
        Assert.True(Table.Trap("dart").Robots);
        Assert.Equal(TrapPhase.Tripped, F.Traps.Trip(F.Traps.New("t", "dart"), 0, robot: true).Phase);
    }

    [Fact]
    public void A_robot_does_not_trip_a_trap_whose_row_says_not()
    {
        var content = Planted(n => n["traps"]![0]!["robots"] = false);
        var traps = new TrapRules(content);

        Assert.Equal(TrapPhase.Armed, traps.Trip(traps.New("t", "dart"), 0, robot: true).Phase);
        Assert.Equal(TrapPhase.Tripped, traps.Trip(traps.New("t", "dart"), 0, robot: false).Phase);
    }

    [Fact]
    public void A_trap_with_no_trigger_area_is_a_load_error()
    {
        var e = Assert.Single(Errors(n => n["traps"]![1]!["trigger"] = 0));

        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("trigger", e.Message);
    }

    //-------------------------------------------------------------------------
    // The Dispenser
    //-------------------------------------------------------------------------

    [Fact]
    public void A_dispenser_reaches_as_far_as_stocks()
    {
        Assert.Equal(64, Table.Dispenser.Radius);
    }

    [Fact]
    public void A_dispenser_with_no_reach_is_a_load_error()
    {
        var e = Assert.Single(Errors(n => n["dispenser"]!["radius"] = 0));

        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("radius", e.Message);
    }

    //-------------------------------------------------------------------------

    private static JsonNode Shipped(out MemoryContentSource source)
    {
        source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(ContentTables.Furniture, out var bytes));
        return JsonNode.Parse(bytes)!;
    }

    private static IReadOnlyList<ContentError> Errors(Action<JsonNode> mutate)
    {
        var node = Shipped(out var source);
        mutate(node);
        return new ContentLoader().Load(source.Set(ContentTables.Furniture, Encoding.UTF8.GetBytes(node.ToJsonString()))).Errors;
    }

    private static DescentContent Planted(Action<JsonNode> mutate)
    {
        var node = Shipped(out var source);
        mutate(node);
        return new ContentLoader().Load(source.Set(ContentTables.Furniture, Encoding.UTF8.GetBytes(node.ToJsonString()))).OrThrow();
    }
}
