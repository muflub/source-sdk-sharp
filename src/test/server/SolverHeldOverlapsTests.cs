using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>plan_physics_managed B2.2: the trigger pairs vphysics holds open, in managed.</summary>
public class SolverHeldOverlapsTests
{
    private static readonly EntityHandle Trigger = new(0x0001_0010u);
    private static readonly EntityHandle Crate = new(0x0001_0011u);
    private static readonly EntityHandle Can = new(0x0001_0012u);

    [Fact]
    public void A_pair_is_held_once_in_either_order_and_released_in_either()
    {
        var held = new SolverHeldOverlaps();
        held.Add(Trigger, Crate);
        held.Add(Crate, Trigger);
        Assert.Equal(1, held.Count);

        held.Remove(Crate, Trigger);
        Assert.Equal(0, held.Count);
    }

    [Fact]
    public void A_held_pair_is_appended_to_the_frames_report()
    {
        var held = new SolverHeldOverlaps();
        held.Add(Trigger, Crate);
        var buffer = new OverlapPair[4];
        buffer[0] = new OverlapPair(Can, Crate);

        int total = held.Merge(buffer, found: 1);

        Assert.Equal(2, total);
        Assert.Equal(new OverlapPair(Trigger, Crate), buffer[1]);
    }

    [Fact]
    public void A_pair_the_engine_already_reports_is_not_reported_twice()
    {
        var held = new SolverHeldOverlaps();
        held.Add(Trigger, Crate);
        var buffer = new OverlapPair[4];
        buffer[0] = new OverlapPair(Crate, Trigger);

        Assert.Equal(1, held.Merge(buffer, found: 1));
    }

    [Fact]
    public void Past_the_buffer_the_total_is_the_size_to_grow_to()
    {
        var held = new SolverHeldOverlaps();
        held.Add(Trigger, Crate);
        held.Add(Trigger, Can);
        var buffer = new OverlapPair[2];
        buffer[0] = new OverlapPair(Crate, Can);

        Assert.Equal(3, held.Merge(buffer, found: 1));
    }
}
