//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>Which side of a pane an edge sits on.</summary>
public enum PaneSide
{
    Bottom = 0,
    Right  = 1,
    Top    = 2,
    Left   = 3,
}

/// <summary>
/// What shape a cracked edge is, which picks one of twelve materials.
/// </summary>
public enum PaneEdge
{
    /// <summary>No edge here at all. Never queued.</summary>
    None = -1,

    /// <summary>Neither end is against a hole.</summary>
    Flat = 0,

    /// <summary>Both ends are.</summary>
    Full = 1,

    Left  = 2,
    Right = 3,
}

/// <summary>One cracked edge, ready to draw.</summary>
public readonly record struct PaneEdgeDraw(byte W, byte H, PaneSide Side, PaneEdge Edge, byte Style);

/// <summary>
/// Which cracks a broken window shows, and in what order to draw them.
///
/// <para>
/// This is the last of the glass that was not a decision written down as one. It
/// was ported into the client shell along with the mesh building because it
/// lived in the same C++ file, and it never belonged there: it touches no
/// renderer, no material and no vertex. Given which panes are gone it answers
/// which edges are cracked and what each one looks like, which is a rule.
/// </para>
///
/// <para>
/// What stays native is the part that genuinely cannot come across: CMeshBuilder
/// writes vertices through inlined calls, four per quad and hundreds of quads a
/// frame, and there is no way to share that the way memory is shared. So native
/// asks nothing and decides nothing — it is handed this list and draws it.
/// </para>
///
/// <para>
/// Every rule here is stock's, including the asymmetries: the left/right sense
/// flips between the top edge and the bottom, because an edge is being looked at
/// from the other side of the pane.
/// </para>
/// </summary>
public static class PaneEdges
{
    /// <summary>How many crack patterns each edge shape has.</summary>
    public const int Styles = 3;

    /// <summary>Whether a pane is there, treating outside the grid as solid.</summary>
    public delegate bool Solid(int w, int h);

    /// <summary>
    /// Every cracked edge of a grid, sorted so that consecutive edges share a
    /// material.
    ///
    /// <para>
    /// The sort is not tidiness: the renderer binds a material whenever the
    /// shape or the style changes, so an unsorted list rebinds per edge. Stock
    /// keeps its list sorted by inserting each new edge next to one that already
    /// shares its texture; this sorts once at the end, which reaches the same
    /// place from a list that is rebuilt wholesale.
    /// </para>
    ///
    /// <para>
    /// <paramref name="styleOf"/> gives a pane its crack pattern. It is passed
    /// in rather than randomised here so that a pane keeps the same pattern as
    /// its neighbours come and go — a crack that reshuffled every time the
    /// window was shot would shimmer.
    /// </para>
    /// </summary>
    public static List<PaneEdgeDraw> Build(int wide, int high, Solid solid, Func<int, int, byte> styleOf)
    {
        var edges = new List<PaneEdgeDraw>();

        for (int w = 0; w < wide; w++)
        {
            for (int h = 0; h < high; h++)
            {
                // A pane that is still there has no edges of its own to draw.
                if (solid(w, h))
                    continue;

                byte style = styleOf(w, h);

                bool up    = solid(w, h + 1), down  = solid(w, h - 1);
                bool left  = solid(w - 1, h), right = solid(w + 1, h);
                bool upLeft   = solid(w - 1, h + 1), upRight   = solid(w + 1, h + 1);
                bool downLeft = solid(w - 1, h - 1), downRight = solid(w + 1, h - 1);

                void Add(PaneSide side, PaneEdge edge)
                {
                    if (edge != PaneEdge.None)
                        edges.Add(new PaneEdgeDraw((byte)w, (byte)h, side, edge, style));
                }

                Add(PaneSide.Top, up
                    ? Shape(!left && upLeft, !right && upRight, flip: false)
                    : PaneEdge.None);

                // The bottom's left and right are swapped against the top's: the
                // edge is being looked at from the other side of the pane.
                Add(PaneSide.Bottom, down
                    ? Shape(!left && downLeft, !right && downRight, flip: true)
                    : PaneEdge.None);

                Add(PaneSide.Left, left
                    ? Shape(!up && upLeft, !down && downLeft, flip: true)
                    : PaneEdge.None);

                Add(PaneSide.Right, right
                    ? Shape(!up && upRight, !down && downRight, flip: false)
                    : PaneEdge.None);
            }
        }

        // Grouped by what draws them. Stable, so panes keep their order within a
        // group and a window does not reshuffle as it comes apart.
        return edges
            .OrderBy(e => (int)e.Edge)
            .ThenBy(e => e.Style)
            .ToList();
    }

    /// <summary>
    /// An edge with a hole at one end, both ends, or neither.
    ///
    /// <paramref name="flip"/> swaps which end counts as "left", which is the
    /// difference between the top edge and the three that are read from the
    /// other side.
    /// </summary>
    private static PaneEdge Shape(bool first, bool second, bool flip)
    {
        if (first && second) return PaneEdge.Full;
        if (first)  return flip ? PaneEdge.Right : PaneEdge.Left;
        if (second) return flip ? PaneEdge.Left  : PaneEdge.Right;

        return PaneEdge.Flat;
    }

    /// <summary>
    /// Packs an edge into the four bytes native reads.
    ///
    /// Four rather than five: the shape is 0..3 and the style is 0..2, so they
    /// share a byte. The order here IS the wire format for this call, and the
    /// native side unpacks it the same way in one place.
    /// </summary>
    public static byte[] Pack(IReadOnlyList<PaneEdgeDraw> edges)
    {
        var bytes = new byte[edges.Count * 4];

        for (int i = 0; i < edges.Count; i++)
        {
            bytes[i * 4 + 0] = edges[i].W;
            bytes[i * 4 + 1] = edges[i].H;
            bytes[i * 4 + 2] = (byte)edges[i].Side;
            bytes[i * 4 + 3] = (byte)(((int)edges[i].Edge << 2) | edges[i].Style);
        }

        return bytes;
    }
}
