using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEEnergySplash</c> — a round off an energy shield.
///
/// <para>
/// From <c>te_energysplash.cpp:82</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST_NOBASE( CTEEnergySplash, DT_TEEnergySplash)
///     SendPropVector( SENDINFO(m_vecPos), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecDir), -1, SPROP_COORD),
///     SendPropInt( SENDINFO(m_bExplosive), 1, SPROP_UNSIGNED),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <c>m_bExplosive</c> IS DECLARED <c>bool</c> HERE AND THE BYTE MATTERS.
/// Stock's member is a <c>CNetworkVar( bool )</c>, so <c>SENDINFO</c> hands
/// <c>SendPropInt</c> a size of ONE, and <c>dt_send.cpp:492</c> picks its read
/// proxy from exactly that number. A four-byte <c>int</c> here would describe a
/// one-byte value as four and the prop would read three bytes of whatever the
/// layout put after it — which for the last field of a blob is past the end.
/// <c>EntityLayout</c> gives a C# <c>bool</c> one byte, so the declaration
/// carries the fact rather than a comment about it.
/// </para>
///
/// <para>
/// One bit on the wire, which is plan 2B's <c>SendPropBool</c> shape written out
/// the way stock itself wrote it — <c>te_energysplash.cpp</c> uses
/// <c>SendPropInt(..., 1, SPROP_UNSIGNED)</c> directly and never the helper.
/// </para>
/// </summary>
[TempEntity]
public class TeEnergySplash : TempEntState
{
    /// <summary>Where it hit. Stock's <c>m_vecPos</c>.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Position;

    /// <summary>The surface normal. Stock's <c>m_vecDir</c>.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Direction;

    /// <summary>
    /// Whether the client plays the bigger version. One bit unsigned, one byte
    /// of blob — see the class note for why the byte is not an int.
    ///
    /// <para>
    /// THE KIND IS INFERRED AND THE BYTE SURVIVES IT. <c>[TeProp]</c> with no
    /// kind reads <c>bool</c> and answers <see cref="TePropKind.Int"/>, which
    /// is what <c>TePropKind.Int</c> said here by hand; the SIZE is
    /// <c>EntityLayout</c>'s and always was, and it is the size — not the kind
    /// — that <c>dt_send.cpp:492</c> picks the read proxy from. Dropping the
    /// kind cannot widen this field, which is the thing the class note above is
    /// about.
    /// </para>
    /// </summary>
    [TeProp(Bits = 1, Flags = TeFlags.Unsigned)]
    public bool Explosive;
}
