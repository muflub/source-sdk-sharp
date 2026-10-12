using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>ShatterSurface_t</c> (<c>public/shattersurfacetypes.h</c>), which has two
/// values and a comment saying it must match the client entity. Two bits on the
/// wire.
/// </summary>
public enum ShatterSurfaceType
{
    Glass = 0,
    Tile  = 1,
}

/// <summary>
/// <c>DT_TEShatterSurface</c> — a pane of glass or a tile wall coming apart,
/// with the force that did it and the colours of both faces.
///
/// <para>
/// STOCK'S FILE IS <c>te_glassshatter.cpp</c>, not <c>te_shattersurface.cpp</c>.
/// The class, the table, the <c>TE_</c> function and both client halves are all
/// spelled <c>ShatterSurface</c>; only the filename is not, which is why a lane
/// scanning by filename does not find it. From <c>te_glassshatter.cpp:103</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEShatterSurface, DT_TEShatterSurface)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecAngles), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecForce), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecForcePos), -1, SPROP_COORD),
///     SendPropFloat( SENDINFO(m_flWidth), 0, SPROP_NOSCALE ),
///     SendPropFloat( SENDINFO(m_flHeight), 0, SPROP_NOSCALE ),
///     SendPropFloat( SENDINFO(m_flShardSize), 0, SPROP_NOSCALE ),
///     SendPropInt( SENDINFO(m_nSurfaceType), 2, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO_ARRAYELEM( m_uchFrontColor, 0 ), 8, SPROP_UNSIGNED ),
///     ... [1], [2], then m_uchBackColor[0..2] the same way
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b><c>m_vecAngles</c> IS A <c>SendPropVector</c>, THOUGH THE MEMBER IS A
/// <c>CNetworkQAngle</c>.</b> Stock declares the storage as angles and sends it
/// as a COORD vector — no <c>anglemod</c>, no [0,360] range, no
/// <c>SendProxy_QAngles</c> — and <c>c_te_glassshatter.cpp:76</c> receives it
/// with <c>RecvPropVector</c> to match. That is a deliberate reproduction and
/// not a transcription slip: "the member is a QAngle so the prop must be
/// <see cref="TePropKind.QAngles"/>" is exactly the reasoning that would put
/// this table one prop-count and one proxy away from the client's, and the
/// symptom would be a bitstream that stops making sense partway through.
/// </para>
///
/// <para>
/// <b>SIX COLOUR BYTES, DECLARED ONE FIELD PER PROP.</b>
/// <c>SENDINFO_ARRAYELEM</c> (<c>dt_send.h:624</c>) is <b>not</b>
/// <c>SENDINFO_VECTORELEM</c> (<c>:631</c>): the array form takes a POSITIVE
/// offset into <c>varName[i]</c> and the vector form NEGATES it so the engine
/// can set <c>SPROP_IS_A_VECTOR_ELEM</c> itself. A colour byte carries no such
/// flag, so <see cref="TeFlags.VectorElem"/> must not appear anywhere in this
/// file.
/// </para>
///
/// <para>
/// The <c>Elems</c> knob <see cref="TeBreakModel"/> uses for its three angles
/// is deliberately NOT used here, and the reason is worth writing down because
/// it looks like the same problem. That knob turns ONE C# VALUE into N props —
/// a <see cref="Vector"/> is twelve bytes that mean one thing, and the writer
/// sets it in one call. This codebase has no three-byte type: a colour declared
/// that way would have to be stored in a four-byte <c>int</c> nobody ever reads
/// as an int, with a padding byte on the end, and BOTH writers — managed and
/// the native generic one in <c>shell_tempents.cpp</c> — address a field at its
/// own offset and would need a new element-indexed setter to reach bytes 1 and
/// 2. Six fields at six consecutive offsets produce the identical six props,
/// the identical names and no padding, and need nothing new on either side.
/// <c>ColourBytesAreSixConsecutiveProps</c> in the tests pins that.
/// </para>
///
/// <para>
/// The three <c>SPROP_NOSCALE</c> floats are declared with a width of
/// <b>0</b>, which is stock's own literal and not the <c>-1</c> the COORD
/// vectors use. <c>SendPropFloat</c> ORs <c>SPROP_NOSCALE</c> into anything
/// declared <c>&lt;= 0</c> anyway (<c>dt_send.cpp:274</c>), so the two spell the
/// same encoding — but the dump prints what was declared, and the parity check
/// against stock's table compares the number.
/// </para>
///
/// <para>
/// <b>NO LIVE NATIVE CALLER.</b> <c>func_breakablesurf.cpp</c> is this table's
/// only stock caller and it is excluded from this build
/// (<c>server_csharp.vpc:115</c>), so nothing fires it today except
/// <c>te-&gt;ShatterSurface</c> from <c>te.cpp</c>'s system. Nothing that fires
/// stops firing; what changes is which table the wire is built from.
/// </para>
/// </summary>
[TempEntity]
public class TeShatterSurface : TeBaseTempEntity
{
    /// <summary>The middle of the pane.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// How the pane is oriented. A COORD VECTOR, not a
    /// <see cref="TePropKind.QAngles"/> — see the class note.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Angles;

    /// <summary>What hit it, as a direction and magnitude.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Force;

    /// <summary>Where it was hit, so the shards fly off the right point.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector ForcePos;

    /// <summary>How wide the pane is. A whole float — width 0, <c>SPROP_NOSCALE</c>.</summary>
    [TeProp(Bits = 0, Flags = TeFlags.NoScale)]
    public float Width;

    /// <summary>How tall.</summary>
    [TeProp(Bits = 0, Flags = TeFlags.NoScale)]
    public float Height;

    /// <summary>How big the pieces come off.</summary>
    [TeProp(Bits = 0, Flags = TeFlags.NoScale)]
    public float ShardSize;

    /// <summary>
    /// Glass or tile — <c>ShatterSurface_t</c>, of which there are two. Two
    /// bits unsigned.
    /// </summary>
    [TeProp(Bits = 2, Flags = TeFlags.Unsigned)]
    public ShatterSurfaceType SurfaceType;

    /// <summary>The near face's red. Stock's <c>m_uchFrontColor[0]</c>, one byte.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte FrontColorR;

    /// <summary>Green.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte FrontColorG;

    /// <summary>Blue.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte FrontColorB;

    /// <summary>The far face's red — a pane is drawn from both sides.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte BackColorR;

    /// <summary>Green.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte BackColorG;

    /// <summary>Blue.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte BackColorB;
}
