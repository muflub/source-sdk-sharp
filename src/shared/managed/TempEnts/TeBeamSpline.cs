using System.Runtime.CompilerServices;

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Sixteen <see cref="Vector"/>s in a row — <c>CNetworkArray( Vector,
/// m_vecPoints, MAX_SPLINE_POINTS )</c>, exactly.
///
/// <para>
/// IT EXISTS BECAUSE THE BLOB HAS TO BE 192 BYTES LONG. The array's element
/// props read at <c>offset + i * 12</c> out of the same blob every other prop
/// reads from, so the storage behind them has to be there: an offset past the
/// end of the declared blob is refused at registration
/// (<c>SOURCESHARP_TE_REFUSED_OFFSET</c>), and one that fits but is not backed
/// would network 180 bytes of whatever came after it.
/// </para>
///
/// <para>
/// <c>[InlineArray]</c> rather than a <c>Vector[]</c> because
/// <c>EntityLayout.Build</c> lays out STORAGE, and a managed array is a
/// reference: sixteen vectors somewhere else and a pointer here. An inline
/// array is the sixteen vectors, contiguous, in declaration position — which is
/// what <c>CNetworkArray</c> is on the C++ side and what the engine's
/// <c>m_ElementStride</c> of 12 addresses.
/// </para>
/// </summary>
[InlineArray(TeBeamSpline.MaxSplinePoints)]
public struct TeSplinePoints
{
    private Vector _element0;
}

/// <summary>
/// <c>DT_TEBeamSpline</c> — THE ONLY ARRAY IN ALL FORTY-FOUR TABLES, and the
/// table the 192-byte ceiling was written for.
///
/// <para>
/// From <c>te_beamspline.cpp:96</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST_NOBASE(CTEBeamSpline, DT_TEBeamSpline)
///     SendPropInt( SENDINFO( m_nPoints ), 5, SPROP_UNSIGNED ),
///
///     SendPropArray(
///         SendPropVector( SENDINFO_ARRAY(m_vecPoints), -1, SPROP_COORD),
///         m_vecPoints)
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>TWO PROPS, BOTH NAMED <c>m_vecPoints</c>, AND NEITHER LINKED HERE.</b>
/// <c>SendPropArray</c> expands to the element template followed by a
/// <c>DPT_Array</c> prop carrying <c>m_nElements = 16</c> and
/// <c>m_ElementStride = 12</c>, with <c>m_pArrayProp</c> left NULL —
/// <c>SendTable_InitTable</c> (<c>public/dt_send.cpp:671</c>) links the array
/// prop to the one immediately before it. Reproducing that shape is plan 3.3's
/// instruction word for word: emit both, link neither. Linking it here would
/// mean holding a pointer into a table the engine has not finished building.
/// </para>
///
/// <para>
/// <b>WITH A NULL LENGTH PROXY ALL SIXTEEN ELEMENTS GO EVERY TIME</b>, which is
/// why this table and <c>DT_EffectData</c> are the two priced against
/// the engine's 192-byte event payload cap and
/// why overflow here is a <c>Host_Error</c> rather than a dropped effect
/// (plan 2F). <c>TeSchemaRegistry</c> prices the worst case AT REGISTRATION —
/// a startup message with a console to print it on — instead of discovering it
/// at playback in the middle of a firefight. The measured answer for this table
/// is 1,112 bits, 139 of the 192 bytes: 32 of framing, 7 + 5 for
/// <c>m_nPoints</c>, and 7 + (16 x 66) + 5 for the array, where 66 is three
/// <c>WriteBitCoord</c>s at their worst (3 + 14 + 5 each). <b>That is also the
/// arithmetic that killed the generic-payload alternative</b>, which put the
/// same sixteen vectors on the wire as 49 unscaled dwords — 196 bytes, over the
/// cap before the first prop index (plan 2B-bis).
/// </para>
///
/// <para>
/// <c>NOBASE</c>, which is stock's own <c>IMPLEMENT_SERVERCLASS_ST_NOBASE</c>:
/// <c>CTEBeamSpline</c> derives from <c>CBaseTempEntity</c> in C++ but its table
/// nests nothing, so the blob starts at this class's own first field.
/// </para>
///
/// <para>
/// THE TWO PROPS STILL SHARE ONE NAME AFTER THE RENAME, which is the property
/// that makes <c>SendTable_InitTable</c> link them and is therefore the one
/// this conversion had to not break. Stock spells both <c>m_vecPoints</c>
/// because both come out of one <c>SendPropArray</c>; here both come out of one
/// <c>[TeProp]</c> on one member and are both called <c>Spline</c>, element
/// template and <c>DPT_Array</c> prop alike. One member, one name, two props —
/// unchanged, with a different spelling.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamSpline : TempEntState
{
    /// <summary>
    /// <c>MAX_SPLINE_POINTS</c>, <c>te_beamspline.cpp:20</c> — a literal 16 in
    /// stock's own file on both sides, not a symbol from a shared header, so it
    /// is a literal here. <c>c_te_beamspline.cpp:19</c> defines its own copy of
    /// exactly the same number.
    /// </summary>
    public const int MaxSplinePoints = 16;

    /// <summary>
    /// The stride the <c>DPT_Array</c> prop carries: <c>sizeof(Vector)</c>, the
    /// distance between one element's storage and the next.
    /// </summary>
    public const int PointStride = 12;

    /// <summary>
    /// How many of the sixteen are meaningful. FIVE BITS, so 0..31 — one bit
    /// wider than the sixteen elements need, which is stock's number and stays
    /// stock's number.
    /// </summary>
    [TeProp(Bits = 5, Flags = TeFlags.Unsigned)]
    public int Points;

    /// <summary>
    /// The spline itself. Sixteen COORD vectors, all of which go on every send
    /// whatever <see cref="Points"/> says — the length proxy stock leaves NULL
    /// is what would have shortened them, and it is NULL here too.
    ///
    /// <para>
    /// <c>Bits = -1</c> and <see cref="TeFlags.Coord"/> are the ELEMENT's
    /// encoding, which is what <c>SENDINFO_ARRAY</c> hands the template prop;
    /// <see cref="TePropAttribute.Elems"/> and
    /// <see cref="TePropAttribute.ElemStride"/> are the <c>DPT_Array</c> prop's
    /// own count and stride. One attribute describes both props because stock's
    /// one macro emits both.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="TePropKind.Array"/> STAYS WRITTEN OUT, WHERE EVERY OTHER
    /// KIND IN THIS FILE IS NOW INFERRED.</b> Inference reads the MEMBER'S TYPE
    /// and <see cref="TeSplinePoints"/> is a struct — <c>Resolve</c> has no case
    /// for one and throws rather than guessing, which is the right answer:
    /// nothing about sixteen inline <c>Vector</c>s says whether they are one
    /// array prop of sixteen elements or a single vector-elem field of three,
    /// and only the table knows. It is the same reason
    /// <see cref="TePropKind.Angle"/> and <see cref="TePropKind.QAngles"/> stay
    /// explicit one file over. Leaving it to inference is the sharpest mutation
    /// available against this declaration and it fails loudly, at registration,
    /// with the member named.
    /// </para>
    /// </summary>
    [TeProp(TePropKind.Array, Bits = -1, Flags = TeFlags.Coord,
            Elems = MaxSplinePoints, ElemStride = PointStride)]
    public TeSplinePoints Spline;
}
