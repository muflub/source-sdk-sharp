//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using SourceSharp.Abi;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// A model's sequence graph (animation.cpp): sequences that start and end on
/// named nodes, and the transition sequences that carry a model from one node
/// to the next. A door model's "open" sits on one node and "close" on
/// another; going from one to the other plays the transition between them,
/// forwards or (a sequence with nodeflags) backwards.
/// </summary>
public static class SequenceGraph
{
    /// <summary>The next step towards a goal sequence.</summary>
    /// <param name="Sequence">The sequence to play next.</param>
    /// <param name="Cycle">Where to start it: 0, or 0.999 for one played backwards.</param>
    /// <param name="Direction">1 forwards, -1 backwards.</param>
    public readonly record struct Step(int Sequence, float Cycle, int Direction);

    /// <summary>
    /// <c>GotoSequence</c>: the next sequence on the way from the current one
    /// to the goal. Null when there is nothing to do yet: a transition still
    /// playing, a sequence out of range, or two parts of the graph that do not
    /// connect. A sequence on node 0 (outside the graph), at either end, goes
    /// straight to the goal.
    /// </summary>
    public static Step? GotoSequence(ISequenceNodes? hdr, int current, float cycle, float rate, int goal)
    {
        if (hdr is null || hdr.NumSequences == 0)
            return null;

        if (current < 0 || current >= hdr.NumSequences)
            return null;

        // asking for a bogus sequence.  Punt.
        if (goal < 0 || goal >= hdr.NumSequences)
            return null;

        // bail if we're going to or from a node 0
        if (hdr.EntryNode(current) == 0 || hdr.EntryNode(goal) == 0)
            return new Step(goal, 0f, 1);

        int endNode = hdr.ExitNode(current);

        // if we're in a transition sequence
        if (hdr.EntryNode(current) != hdr.ExitNode(current))
        {
            // are we done with it?
            if (rate > 0f && cycle >= 0.999f)
                endNode = hdr.ExitNode(current);
            else if (rate < 0f && cycle <= 0.001f)
                endNode = hdr.EntryNode(current);
            else
                return null;
        }

        // if both sequences are on the same node, just go there
        if (endNode == hdr.EntryNode(goal))
            return new Step(goal, 0f, 1);

        // if there is no transitional node, just go to the goal sequence
        int internNode = hdr.GetTransition(endNode, hdr.EntryNode(goal));
        if (internNode == 0)
            return new Step(goal, 0f, 1);

        // look for someone going from the entry node to next node it should hit
        // this may be the goal sequences node or an intermediate node
        for (int i = 0; i < hdr.NumSequences; i++)
        {
            if (hdr.EntryNode(i) == endNode && hdr.ExitNode(i) == internNode)
                return new Step(i, 0f, 1);

            if (hdr.NodeFlags(i) != 0 && hdr.ExitNode(i) == endNode && hdr.EntryNode(i) == internNode)
                return new Step(i, 0.999f, -1);
        }

        // this means that two parts of the node graph are not connected.
        return null;
    }

    /// <summary>
    /// <c>SelectWeightedSequence</c> (studio.cpp's activity mapping): the
    /// current sequence when it has the activity and a negative weight (a
    /// sequence a pick keeps); otherwise a roulette over the activity's
    /// sequences, each as wide as its weight. -1 when no sequence has it.
    /// </summary>
    /// <param name="randomInt">RandomInt(min, max), inclusive.</param>
    public static int SelectWeightedSequence(IReadOnlyList<ModelSequence> sequences, string activity, int current,
                                             Func<int, int, int> randomInt)
    {
        // is the current sequence appropriate?
        if (current >= 0 && current < sequences.Count
            && string.Equals(sequences[current].ActivityName, activity, StringComparison.OrdinalIgnoreCase)
            && sequences[current].ActivityWeight < 0)
        {
            return current;
        }

        int total = 0, first = -1;
        for (int i = 0; i < sequences.Count; i++)
        {
            if (!string.Equals(sequences[i].ActivityName, activity, StringComparison.OrdinalIgnoreCase))
                continue;

            if (first < 0)
                first = i;
            total += Math.Abs(sequences[i].ActivityWeight);
        }

        if (first < 0 || total <= 0)
            return first;

        // chug through the entries until we run out of random juice
        int value = randomInt(0, total - 1);
        int last = first;
        for (int i = first; i < sequences.Count; i++)
        {
            if (!string.Equals(sequences[i].ActivityName, activity, StringComparison.OrdinalIgnoreCase))
                continue;

            int weight = Math.Abs(sequences[i].ActivityWeight);
            if (value < weight)
                return i;

            value -= weight;
            last = i;
        }

        return last;
    }
}
