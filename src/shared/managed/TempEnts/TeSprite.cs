using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TESprite</c> — one sprite, at a point, for one frame's worth of an
/// animation the client plays out.
///
/// <para>
/// From <c>te_sprite.cpp:101</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTESprite, DT_TESprite)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropFloat( SENDINFO(m_fScale ), 8, SPROP_ROUNDDOWN, 0.0, 25.6 ),
///     SendPropInt( SENDINFO(m_nBrightness), 8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <c>SendPropModelIndex</c> IS NOT UNSIGNED, and that is the detail in this
/// table most likely to decode as nearly right. <c>dt_send.h:750</c> spells it
/// <c>SendPropInt( pVarName, offset, sizeofVar, SP_MODEL_INDEX_BITS, 0 )</c> —
/// thirteen bits and flags of <b>zero</b>, not <c>SPROP_UNSIGNED</c>. A signed
/// thirteen-bit field spends its top bit on a sign, so an unsigned declaration
/// on one side and stock's signed one on the other is a bitstream that stops
/// making sense partway through. Only <c>m_nBrightness</c> below is unsigned.
/// </para>
///
/// <para>
/// The width comes from <see cref="TeBits.ModelIndex"/> rather than the literal
/// 13, because <c>SP_MODEL_INDEX_BITS</c> lives in <c>public/const.h</c> and a
/// second copy of a width drifts (plan 3.1).
/// </para>
///
/// <para>
/// Declaration order is wire order. Reordering these four is a wire-format
/// change, which is <c>EntityLayout.Build</c>'s existing rule.
/// </para>
/// </summary>
[Precache(PrecacheKind.Model, "sprites/gunsmoke.vmt")]   // CTESprite::Precache, te_sprite.cpp:70
[TempEntity]
public class TeSprite : TeBaseTempEntity
{
    /// <summary>Where it appears. One COORD vector prop, not three elements.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// Which sprite. Thirteen bits and NO flags — see the note above; this is
    /// the one int in this table that is not unsigned.
    /// </summary>
    [TeProp(BitsFrom = TeBits.ModelIndex, Flags = TeFlags.None)]
    public int ModelIndex;

    /// <summary>
    /// How big, 0..25.6 in eight bits, ROUNDDOWN. Not ROUNDUP: the two are one
    /// token apart in stock's table and pick different endpoints of the same
    /// range, so this is transcribed from <c>DT_TESprite</c>'s own line rather
    /// than from a neighbouring table's.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundDown, Low = 0.0f, High = 25.6f)]
    public float Scale;

    /// <summary>How bright, 0..255.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Brightness;
}
