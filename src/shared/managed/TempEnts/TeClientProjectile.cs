using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEClientProjectile</c> — a projectile the CLIENT owns and moves: the
/// server says where it starts and how fast, and never mentions it again.
/// THE ONLY <c>SendPropEHandle</c> IN THE INVENTORY.
///
/// <para>
/// From <c>te_clientprojectile.cpp:82</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEClientProjectile, DT_TEClientProjectile)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecVelocity), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropInt( SENDINFO(m_nLifeTime), 6, SPROP_UNSIGNED ),
///     SendPropEHandle(SENDINFO(m_hOwner)),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>THE EHANDLE IS A PROXY THAT DISAPPEARS, AND C# WRITES WHAT IT WOULD HAVE
/// PRODUCED.</b> <c>SendPropEHandle</c> is
/// <c>SendPropInt( x, NUM_NETWORKED_EHANDLE_BITS, SPROP_UNSIGNED|flags,
/// SendProxy_EHandleToInt )</c> — <c>game/server/sendproxy.cpp:70</c>, one line
/// long — and a send proxy never
/// appears in the table description the client pairs against (plan 2B) — so a
/// plain unsigned int of the same width is an identical wire AND an identical
/// handshake, provided the blob holds the value the proxy computed:
/// </para>
/// <code>
/// int iSerialNum = pHandle-&gt;GetSerialNumber() &amp; ( (1 &lt;&lt; NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS) - 1 );
/// pOut-&gt;m_Int    = pHandle-&gt;GetEntryIndex() | (iSerialNum &lt;&lt; MAX_EDICT_BITS);
/// </code>
/// <para>
/// — <c>game/server/sendproxy.cpp:24</c>, and it is
/// <see cref="TempEntWriter.EHandle"/> that transcribes it, with
/// <c>INVALID_NETWORKED_EHANDLE_VALUE</c> (all twenty-one bits set) for a handle
/// that resolves to nothing. <b>Getting the composition wrong sends a
/// valid-looking handle to the wrong entity</b>: index and serial are packed
/// into one number, so a shift of 12 instead of 11 halves the index space and
/// points at somebody else. Nothing on either side would say so.
/// </para>
///
/// <para>
/// THE WIDTH IS <see cref="TeBits.NetworkedEHandle"/> AND NOT A 21 TYPED HERE.
/// <c>NUM_NETWORKED_EHANDLE_BITS</c> is <c>MAX_EDICT_BITS + 10</c>
/// (<c>public/const.h</c>), so it moves if either half moves, and stock's
/// hand-written <c>RecvPropEHandle</c> on the client half computes the same
/// expression from the same header. A second copy in C# is the drift plan 3.1
/// forbids — and a prop one bit narrower than the other side expects is a
/// bitstream that stops making sense partway through.
/// </para>
/// </summary>
[TempEntity]
public class TeClientProjectile : TeBaseTempEntity
{
    /// <summary>Where it starts.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// How fast, in units per second — a COORD vector, so it is quantised the
    /// same way a position is and 2048 arrives as 2048.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Velocity;

    /// <summary>
    /// What it looks like. <c>SP_MODEL_INDEX_BITS</c> resolved natively, and
    /// flags of ZERO — <c>dt_send.h:750</c> passes 0, not <c>SPROP_UNSIGNED</c>.
    /// </summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>How long it lives. SIX bits unsigned, so 0..63 seconds.</summary>
    [TeProp(Bits = 6, Flags = TeFlags.Unsigned)]
    public int LifeTime;

    /// <summary>
    /// Who fired it, as the packed index-and-serial
    /// <c>SendProxy_EHandleToInt</c> would have produced. An <c>int</c> of blob
    /// and not an <c>EntityHandle</c>: what goes on the wire is one number, and
    /// the composition of it is <see cref="TempEntWriter.EHandle"/>'s single
    /// transcription of stock's proxy rather than a second one here.
    /// </summary>
    [TeProp(BitsFrom = TeBits.NetworkedEHandle, Flags = TeFlags.Unsigned)]
    public int Owner;

    /// <summary>
    /// <see cref="Owner"/>'s entity index, or -1 if the wire carried no entity.
    ///
    /// <para>
    /// The serial half is discarded, deliberately: the index is the half that
    /// does not go through row C8's path. Decomposed by
    /// <see cref="TempEntReader.EntryIndexOf"/> rather than here, so the taking
    /// apart and the putting together stay one pair.
    /// </para>
    /// </summary>
    public int OwnerEntityIndex => TempEntReader.EntryIndexOf(Owner);
}
