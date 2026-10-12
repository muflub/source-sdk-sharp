using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a pane of glass tells the client about itself.
///
/// <para>
/// The grid it was divided into, where that grid starts and which way it faces,
/// and which of its up-to-256 panes are still there. The client needs all of it
/// to draw the cracks: it rebuilds the mesh from the corner, the two panel
/// dimensions and the normal, and the bit vector is what it consults per pane.
/// </para>
///
/// <para>
/// This is the state the surface was kept off the parallel hierarchy for.
/// "Adding 256 networked bools per window to the managed layout" was the
/// argument, and it was a fair one when a class could not declare a table at
/// all. It costs 56 bytes.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FuncBreakableSurf)]
public partial class BreakableSurfaceNetState
{
    /// <summary>Panes across. At most 16, so a byte, and stock sends 8 bits too.</summary>
    public partial byte Wide { get; set; }

    /// <summary>Panes up.</summary>
    public partial byte High { get; set; }

    public partial float PanelWidth { get; set; }
    public partial float PanelHeight { get; set; }

    /// <summary>Which way the broken face points.</summary>
    public partial Vector Normal { get; set; }

    /// <summary>The corner panes are counted from.</summary>
    public partial Vector Corner { get; set; }

    /// <summary>Whether the sheet has gone at all. Until it has, there is no grid.</summary>
    public partial bool Broken { get; set; }

    /// <summary>Glass, tile or computer. It decides the material and the shards.</summary>
    [NetField(Bits = 2)] public partial byte SurfaceType { get; set; }

    /// <summary>
    /// The pane grid: 16 x 16 bits, as eight unsigned words.
    ///
    /// <para>
    /// Eight words rather than 256 bools, which is what stock sends. The
    /// information is the same and so is the bit count on the wire; what differs
    /// is 32 bytes of storage against 256, and that matters here because every
    /// entity on the hierarchy is allocated for the greediest declared class.
    /// </para>
    ///
    /// <para>
    /// Nothing outside this class should index it. IsPaneSolid and SetPaneSolid
    /// in the hand-written half are the interface, so the packing has one copy
    /// and the two sides cannot pack it differently.
    /// </para>
    /// </summary>
    [NetArray(8)] public partial NetArray<uint> PaneBits { get; }
}
