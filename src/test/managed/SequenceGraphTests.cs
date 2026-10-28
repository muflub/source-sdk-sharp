using System;
using SourceSharp.Abi;
using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// GotoSequence against stock's (animation.cpp), over a door's graph: "closed"
/// and "open" nodes, an idle on each, the opening transition between them
/// (played backwards to close, as its nodeflags allow), and a sequence on no
/// node at all.
/// </summary>
public class SequenceGraphTests
{
    private const int IdleClosed = 0, IdleOpen = 1, Opening = 2, Free = 3, IdleStuck = 4;

    private static StudioHdr Door(bool reversible = true, byte closedToOpen = 2, byte openToClosed = 1, byte openToOpen = 0)
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0" });
        b.ModelName = "b0";
        b.NodeNames.AddRange(["closed", "open", "stuck"]);
        b.Transitions[(1, 2)] = closedToOpen;
        b.Transitions[(2, 1)] = openToClosed;
        b.Transitions[(1, 3)] = 3;
        b.Transitions[(2, 2)] = openToOpen;
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle_closed", AnimTable = [0], LocalEntryNode = 1, LocalExitNode = 1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle_open", AnimTable = [0], LocalEntryNode = 2, LocalExitNode = 2 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "opening", AnimTable = [0], LocalEntryNode = 1, LocalExitNode = 2, NodeFlags = reversible ? 1 : 0,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "free", AnimTable = [0] });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle_stuck", AnimTable = [0], LocalEntryNode = 3, LocalExitNode = 3 });
        return StudioHdr.TryCreate(b.Build(), null, out StudioHdr? h) ? h! : throw new InvalidOperationException("door rejected");
    }

    [Fact]
    public void Closed_to_open_plays_the_transition_forwards()
        => Assert.Equal(new SequenceGraph.Step(Opening, 0f, 1), SequenceGraph.GotoSequence(Door(), IdleClosed, 0.5f, 1f, IdleOpen));

    [Fact]
    public void Open_to_closed_plays_it_backwards_from_its_end()
        => Assert.Equal(new SequenceGraph.Step(Opening, 0.999f, -1), SequenceGraph.GotoSequence(Door(), IdleOpen, 0.5f, 1f, IdleClosed));

    [Fact]
    public void A_transition_without_nodeflags_is_not_played_backwards()
        => Assert.Null(SequenceGraph.GotoSequence(Door(reversible: false), IdleOpen, 0.5f, 1f, IdleClosed));

    [Fact]
    public void A_transition_still_playing_is_left_to_finish()
    {
        Assert.Null(SequenceGraph.GotoSequence(Door(), Opening, 0.5f, 1f, IdleOpen));
        Assert.Null(SequenceGraph.GotoSequence(Door(), Opening, 0.5f, -1f, IdleClosed));
    }

    [Fact]
    public void A_finished_transition_goes_on_from_the_node_it_ended_on()
    {
        Assert.Equal(new SequenceGraph.Step(IdleOpen, 0f, 1), SequenceGraph.GotoSequence(Door(), Opening, 0.999f, 1f, IdleOpen));
        Assert.Equal(new SequenceGraph.Step(IdleClosed, 0f, 1), SequenceGraph.GotoSequence(Door(), Opening, 0.001f, -1f, IdleClosed));
    }

    [Fact]
    public void A_sequence_on_the_goals_node_goes_there_whatever_the_table_says()
        => Assert.Equal(new SequenceGraph.Step(IdleOpen, 0f, 1),
                        SequenceGraph.GotoSequence(Door(openToOpen: 1), Opening, 0.999f, 1f, IdleOpen));

    [Fact]
    public void A_sequence_on_no_node_goes_straight_to_the_goal_either_way()
    {
        Assert.Equal(new SequenceGraph.Step(IdleOpen, 0f, 1), SequenceGraph.GotoSequence(Door(), Free, 0.5f, 1f, IdleOpen));
        Assert.Equal(new SequenceGraph.Step(Free, 0f, 1), SequenceGraph.GotoSequence(Door(), IdleOpen, 0.5f, 1f, Free));
    }

    [Fact]
    public void No_route_between_the_nodes_goes_straight_to_the_goal()
        => Assert.Equal(new SequenceGraph.Step(IdleOpen, 0f, 1), SequenceGraph.GotoSequence(Door(closedToOpen: 0), IdleClosed, 0.5f, 1f, IdleOpen));

    [Fact]
    public void A_route_through_a_node_no_sequence_reaches_is_refused()
        => Assert.Null(SequenceGraph.GotoSequence(Door(), IdleClosed, 0.5f, 1f, IdleStuck));

    [Fact]
    public void A_sequence_out_of_range_is_refused()
    {
        Assert.Null(SequenceGraph.GotoSequence(Door(), -1, 0f, 1f, IdleOpen));
        Assert.Null(SequenceGraph.GotoSequence(Door(), IdleClosed, 0f, 1f, 9));
        Assert.Null(SequenceGraph.GotoSequence(null, IdleClosed, 0f, 1f, IdleOpen));
    }

    /// <summary>
    /// The model descriptor, which entities read, carries the graph as the
    /// header answers it: every sequence's nodes and flags, every transition,
    /// and so every walk.
    /// </summary>
    [Fact]
    public void The_descriptor_answers_the_graph_as_the_header_does()
    {
        StudioHdr hdr = Door(openToOpen: 1);
        ModelDescriptor d = StudioModelDescriptor.Describe(hdr);

        Assert.Equal(hdr.NumSequences, d.NumSequences);
        for (int i = 0; i < hdr.NumSequences; i++)
            Assert.Equal((hdr.EntryNode(i), hdr.ExitNode(i), hdr.NodeFlags(i)), (d.EntryNode(i), d.ExitNode(i), d.NodeFlags(i)));
        for (int from = 1; from <= 3; from++)
            for (int to = 1; to <= 3; to++)
                Assert.Equal(hdr.GetTransition(from, to), d.GetTransition(from, to));

        Assert.Equal(new SequenceGraph.Step(Opening, 0.999f, -1), SequenceGraph.GotoSequence(d, IdleOpen, 0.5f, 1f, IdleClosed));
    }

    private static ModelSequence Seq(int index, string activity, int weight)
        => new(index, "s" + index, activity, 0, 1f, Array.Empty<ModelSequenceEvent>(), weight);

    private static readonly ModelSequence[] Idles =
    [
        Seq(0, "ACT_WALK", 1), Seq(1, "ACT_IDLE", 1), Seq(2, "ACT_IDLE", 3), Seq(3, "ACT_IDLE", -2), Seq(4, "ACT_RUN", 1),
    ];

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    public void A_weighted_pick_lands_where_the_roulette_says(int roll, int picked)
        => Assert.Equal(picked, SequenceGraph.SelectWeightedSequence(Idles, "ACT_IDLE", 0, (min, max) =>
        {
            Assert.Equal((0, 5), (min, max));
            return roll;
        }));

    [Fact]
    public void A_current_sequence_with_a_negative_weight_is_kept()
        => Assert.Equal(3, SequenceGraph.SelectWeightedSequence(Idles, "ACT_IDLE", 3, (_, _) => throw new InvalidOperationException("no roll")));

    [Fact]
    public void An_activity_no_sequence_has_is_none()
        => Assert.Equal(-1, SequenceGraph.SelectWeightedSequence(Idles, "ACT_JUMP", 0, (_, _) => 0));
}
