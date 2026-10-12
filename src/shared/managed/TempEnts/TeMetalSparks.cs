using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEMetalSparks</c> — a bullet off metal, and THE ONLY TABLE IN THE
/// INVENTORY THAT IS BOTH A CONCRETE EFFECT AND SOMEBODY ELSE'S BASE.
///
/// <para>
/// From <c>te_armorricochet.cpp:80</c> — the file is named for its second class,
/// which is one of the two reasons a lane scanning by filename misses this one:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST_NOBASE( CTEMetalSparks, DT_TEMetalSparks)
///     SendPropVector( SENDINFO(m_vecPos), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecDir), -1, SPROP_COORD),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <c>RegistersClass</c> IS LEFT AT ITS DEFAULT, <c>true</c>, AND THAT IS THE
/// DECISION THIS FILE EXISTS TO RECORD. <see cref="TeBaseTempEntity"/> and
/// <see cref="TeParticleSystem"/> both pass <c>RegistersClass = false</c>,
/// because each is PURELY a base whose native ServerClass is still registered by
/// a file this port has not evicted, and two ServerClasses of one name make the
/// client begin instantiating and stop with nothing in any log (plan 2E). Neither
/// half of that reasoning applies here:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>CTEMetalSparks</c> is a REAL EFFECT with a live caller —
/// <c>CEffectsServer::MetalSparks</c> at <c>EffectsServer.cpp:164</c> — so a
/// table with no ServerClass would be an effect that stopped firing, which is
/// the one thing this port is not allowed to do; and
/// </description></item>
/// <item><description>
/// its native class is NOT still registered: <c>te_armorricochet.cpp</c> holds
/// both <c>CTEMetalSparks</c> and <c>CTEArmorRicochet</c> and is evicted whole,
/// so the name is free.
/// </description></item>
/// </list>
/// <para>
/// Being a base costs it nothing: <see cref="TeArmorRicochet"/> nests
/// <c>DT_TEMetalSparks</c> by NAME, and <c>shell_tempents.cpp</c>'s
/// <c>FindRegisteredTable</c> finds a managed table whether or not it also
/// claims a class. Base and class are two independent bits and this is the one
/// row in Part 6 where they differ.
/// </para>
///
/// <para>
/// <b>THE WIRE NAMES ARE GONE AND <c>m_vecDir</c> IS WHY THAT NEEDED CARE.</b>
/// Four tables send a prop of that name — this one, <c>DT_TEArmorRicochet</c>
/// through this one, <c>DT_TESparks</c> and <c>DT_TEExplosion</c> — and only
/// the first two are renamed by this file. The other two are their own
/// declarations and rename on their own schedule; a search-and-replace across
/// the shared test files would have renamed theirs from here and gone red as
/// their bug. The prop is now called <c>Direction</c> on THESE two tables and
/// <c>m_vecDir</c> on the other two, and the two sides of each table agree with
/// each other, which is the only agreement that has ever been on the wire.
/// </para>
/// </summary>
[TempEntity]
public class TeMetalSparks : TempEntState
{
    /// <summary>
    /// Where the round hit. Stock spelt it <c>m_vecPos</c> and not
    /// <c>m_vecOrigin</c>; the member says the same thing without the Hungarian
    /// and the prop is called <c>Position</c> on both sides now.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Position;

    /// <summary>
    /// The surface normal it came off. <c>TE_MetalSparks</c> asserts this is
    /// unit length; the assert is the caller's contract and is not a wire
    /// property, so it stays in the forward rather than here.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Direction;
}
