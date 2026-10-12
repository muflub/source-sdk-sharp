using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEPhysicsProp</c> — a prop handed to the client to simulate and break
/// on its own.
///
/// <para>
/// From <c>te_physicsprop.cpp:113</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEPhysicsProp, DT_TEPhysicsProp)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropAngle( SENDINFO_VECTORELEM(m_angRotation, 0), 13 ),
///     SendPropAngle( SENDINFO_VECTORELEM(m_angRotation, 1), 13 ),
///     SendPropAngle( SENDINFO_VECTORELEM(m_angRotation, 2), 13 ),
///     SendPropVector( SENDINFO(m_vecVelocity), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropInt( SENDINFO(m_nSkin), ANIMATION_SKIN_BITS),
///     SendPropInt( SENDINFO(m_nFlags), 2, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nEffects), EF_MAX_BITS, SPROP_UNSIGNED),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>TWO SYMBOLIC WIDTHS, AND NEITHER IS RESTATED HERE.</b>
/// <c>ANIMATION_SKIN_BITS</c> is <c>baseanimating.h:617</c>'s and
/// <c>EF_MAX_BITS</c> is <c>const.h:298</c>'s; both happen to be ten today and
/// they are not the same ten. They travel as
/// <see cref="TeBits.AnimationSkin"/> and <see cref="TeBits.EfMax"/> and native
/// resolves each from its own header (plan 3.1), because a width with two
/// copies drifts and a prop one bit narrower than the other side expects is a
/// bitstream that stops making sense partway through — with no log line
/// anywhere. Writing <c>Bits = 10</c> twice here would compile, pass a dump
/// comparison, and be wrong the day either header moves.
/// </para>
///
/// <para>
/// <b><c>m_nSkin</c> IS SIGNED</b>, and it is the second such int in the port
/// after <c>DT_TEDynamicLight</c>'s <c>exponent</c>. Stock's line passes NO
/// third argument at all, so the flags are zero and not <c>SPROP_UNSIGNED</c>
/// — which the two lines under it both pass. Its callers hand it small
/// non-negative skin numbers, so an unsigned declaration would look correct in
/// every screenshot and disagree with the client on the sign bit.
/// </para>
///
/// <para>
/// THE ANGLES ARE <see cref="TeBreakModel"/>'S — three thirteen-bit
/// <c>SendPropAngle</c> props out of one <see cref="Vector"/>, negated offsets
/// and all. See that class for why they are not a
/// <see cref="TePropKind.QAngles"/>.
/// </para>
///
/// <para>
/// NOTE THE ORDER: the SendTable sends <c>m_nSkin</c> BEFORE <c>m_nFlags</c>,
/// while <c>c_te_physicsprop.cpp</c>'s RecvTable lists <c>m_nFlags</c> first.
/// That is legal and normal — the engine matches props by NAME when it links
/// the two tables — but it is why this file follows the SEND table and not the
/// receive one, and why nothing here may be reordered to "match the client".
/// </para>
///
/// <para>
/// PRECACHE: <c>CTEPhysicsProp::Precache</c> (<c>te_physicsprop.cpp:73</c>)
/// takes <c>models/gibs/hgibs.mdl</c>, the same model
/// <see cref="TeBreakModel"/> takes. One line on
/// <c>SourceSharp.Server.TempEnts.Precache</c>'s list serves both.
/// </para>
/// </summary>
[Precache(PrecacheKind.Model, "models/gibs/hgibs.mdl")]   // CTEPhysicsProp::Precache, te_physicsprop.cpp:73
[TempEntity]
public class TePhysicsProp : TeBaseTempEntity
{
    /// <summary>Where the prop starts.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>Which way it is facing. Three thirteen-bit angle props.</summary>
    [TeProp(TePropKind.Angle, Bits = 13, Flags = TeFlags.VectorElem, Elems = 3, ElemStride = 4)]
    public Vector Rotation;

    /// <summary>How fast it is going when it appears.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Velocity;

    /// <summary>The model, in <c>SP_MODEL_INDEX_BITS</c>, flags zero.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>
    /// Which skin the model wears. <c>ANIMATION_SKIN_BITS</c> wide, resolved
    /// natively, and <b>SIGNED</b> — stock passes no flags on this line.
    /// </summary>
    [TeProp(BitsFrom = TeBits.AnimationSkin, Flags = TeFlags.None)]
    public int Skin;

    /// <summary>Two bits of behaviour, unsigned.</summary>
    [TeProp(Bits = 2, Flags = TeFlags.Unsigned)]
    public int Flags;

    /// <summary>
    /// The render effect bits — <c>EF_NODRAW</c> and friends.
    /// <c>EF_MAX_BITS</c> wide, resolved natively, and unsigned.
    /// </summary>
    [TeProp(BitsFrom = TeBits.EfMax, Flags = TeFlags.Unsigned)]
    public int Effects;
}
