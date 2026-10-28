namespace SourceSharp.Abi;

/// <summary>
/// A model's sequence graph as data: each sequence's entry and exit nodes
/// (counted from 1, 0 for none) and node flags, and the node-to-node
/// transition table. What a walk towards a goal sequence reads, answered by
/// the studio header and by the descriptor read from it.
/// </summary>
public interface ISequenceNodes
{
    int NumSequences { get; }

    int EntryNode(int sequence);

    int ExitNode(int sequence);

    /// <summary>Non-zero when the sequence may be played backwards between its nodes.</summary>
    int NodeFlags(int sequence);

    /// <summary>The node to pass through from one node to another; 0 for no route.</summary>
    int GetTransition(int from, int to);
}
