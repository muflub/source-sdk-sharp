using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBloodSprite</c> — the sprite pair a bullet wound throws.
///
/// <para>
/// From <c>te_bloodsprite.cpp:101</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST_NOBASE(CTEBloodSprite, DT_TEBloodSprite)
///     SendPropVector( SENDINFO(m_vecOrigin),    -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecDirection), -1, SPROP_COORD),
///     SendPropInt( SENDINFO(r), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(g), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(b), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(a), 8, SPROP_UNSIGNED ),
///     SendPropModelIndex( SENDINFO(m_nSprayModel) ),
///     SendPropModelIndex( SENDINFO(m_nDropModel) ),
///     SendPropInt( SENDINFO(m_nSize), 8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// NOBASE, and it is worth saying why the word is not decoration here.
/// <c>CTEBloodSprite</c> derives from <c>CBaseTempEntity</c> in C++ — its
/// constructor calls that constructor — but its SEND TABLE nests nothing, which
/// is what <c>_NOBASE</c> means. The wire and the class hierarchy are two
/// different statements and only the first one is this file's business. Its
/// sibling <see cref="TeBloodStream"/>, three feet away in the same lane, is
/// the other way round.
/// </para>
///
/// <para>
/// THE TWO MODEL INDICES ARE THE SENDER'S, NOT THE CALLER'S.
/// <c>ITempEntsSystem::BloodSprite</c> has no model parameters at all;
/// <c>TE_BloodSprite</c> fills <c>m_nSprayModel</c> and <c>m_nDropModel</c> from
/// <c>g_sModelIndexBloodSpray</c> / <c>g_sModelIndexBloodDrop</c>, which
/// <c>world_adapter.cpp:111</c> precaches each level. The forward in
/// <c>shell_te_fwd_d.cpp</c> does the same thing at the same point, so the two
/// props carry the same numbers they did before this port.
/// </para>
/// </summary>
[TempEntity]
public class TeBloodSprite : TempEntState
{
    /// <summary>Where the wound is. Stock's <c>m_vecOrigin</c>.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>Which way the spray goes. Stock's <c>m_vecDirection</c>.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Direction;

    /// <summary>
    /// The spray's colour. ONE MEMBER, FOUR PROPS — <c>ColorR</c> through
    /// <c>ColorA</c>, eight bits unsigned each, which is what stock's four
    /// separate props are.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public Color32 Color;

    /// <summary>
    /// The splatter sprite. A <c>SendPropModelIndex</c>, which is
    /// <c>SendPropInt( x, SP_MODEL_INDEX_BITS, 0 )</c> — flags ZERO, so it is
    /// SIGNED, and the width is a symbol native resolves rather than a 13 typed
    /// here (plan 2B).
    /// </summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int SprayModel;

    /// <summary>The dripping sprite. Same encoding.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int DropModel;

    /// <summary>How big. Eight bits unsigned. Stock's <c>m_nSize</c>.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Size;
}
