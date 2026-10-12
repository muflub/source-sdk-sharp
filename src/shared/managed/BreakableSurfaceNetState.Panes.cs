namespace SourceSharp.Shared;

/// <summary>
/// The pane bit vector, behind an interface that speaks in panes.
///
/// The eight words next door are storage and nothing else. Everything that
/// reads or writes a pane — the server breaking one, the client deciding
/// whether to draw one — goes through here, so the packing is in one place and
/// the two sides cannot pack it differently.
/// </summary>
public partial class BreakableSurfaceNetState
{
    /// <summary>The grid is at most this on a side. Stock says so, and the wire agrees.</summary>
    public const int MaxPanels = 16;

    /// <summary>Whether a pane is still there.</summary>
    public bool IsPaneSolid(int w, int h)
    {
        int bit = Index(w, h);
        return bit >= 0 && (PaneBits[bit >> 5] & (1u << (bit & 31))) != 0u;
    }

    /// <summary>
    /// Puts a pane in or takes it out.
    ///
    /// Writes through the generated property rather than to a field, because
    /// that is what reaches the entity's blob — assigning storage directly would
    /// change what this side believes and nothing else.
    /// </summary>
    public void SetPaneSolid(int w, int h, bool solid)
    {
        int bit = Index(w, h);
        if (bit < 0)
            return;

        int word = bit >> 5;
        uint mask = 1u << (bit & 31);

        PaneBits[word] = solid ? (PaneBits[word] | mask) : (PaneBits[word] & ~mask);
    }

    /// <summary>Puts every pane in, which is what an unbroken sheet is.</summary>
    public void FillPanes() => PaneBits.Fill(uint.MaxValue);

    /// <summary>
    /// Where a pane sits in the vector.
    ///
    /// ROW-MAJOR BY THE FULL GRID WIDTH, not by this surface's — 16, always.
    /// Stock indexes by the surface's own width, which means a pane's bit moves
    /// when the grid is derived, and the client has to have both the grid and
    /// the bits from the same update to read either. A fixed stride costs
    /// nothing and removes the ordering problem entirely.
    /// </summary>
    private static int Index(int w, int h)
        => (w < 0 || w >= MaxPanels || h < 0 || h >= MaxPanels) ? -1 : w + h * MaxPanels;
}
