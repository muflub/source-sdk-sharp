using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEMuzzleFlash</c> — the flash at the end of a barrel, and the one
/// table in this lane with live native callers on both sides of the port.
///
/// <para>
/// From <c>te_muzzleflash.cpp:68</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEMuzzleFlash, DT_TEMuzzleFlash )
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD ),
///     SendPropVector( SENDINFO(m_vecAngles), -1, SPROP_COORD ),
///     SendPropFloat( SENDINFO(m_flScale), -1, SPROP_NOSCALE ),
///     SendPropInt( SENDINFO(m_nType), 32, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b><c>m_vecAngles</c> IS A <c>CNetworkQAngle</c> SENT BY
/// <c>SendPropVector</c>, AND THAT IS NOT A MISTAKE TO TIDY UP.</b> Lane B's
/// <c>DT_TEProjectedDecal</c> sends its facing with <c>SendPropQAngles</c>,
/// which installs <c>SendProxy_QAngles</c> and runs <c>anglemod()</c> on the way
/// out; this one does not, so a negative pitch goes on the wire as a negative
/// COORD and comes back as one. The two tables disagree in stock and the port
/// reproduces both. Declaring this as <see cref="TePropKind.QAngles"/> would
/// give it the [0,360] range encode and turn -45 into 315 — a plausible number,
/// pointing the flash the other way, with nothing anywhere saying so.
/// </para>
///
/// <para>
/// <c>m_flScale</c> IS <c>SPROP_NOSCALE</c>: thirty-two bits, the whole float,
/// no range. It is one of the two things in this lane that costs a full word,
/// and it is what <c>MuzzleFlashEffect</c>'s callers already pass unrounded.
/// </para>
///
/// <para>
/// LIVE TODAY, WHICH DECIDES HOW THIS LANDS. <c>CEffectsServer::MuzzleFlash</c>
/// (<c>EffectsServer.cpp:155</c>) and <c>UTIL_MuzzleFlash</c>
/// (<c>util.cpp:3037</c>) both reach <c>te-&gt;MuzzleFlash</c>, and managed
/// weapons reach the first of those through
/// <c>IEngineEffects.MuzzleFlashEffect</c>. After the eviction the last link of
/// that chain is this table instead of the native one; nothing in between
/// changed and nothing stopped firing.
/// </para>
/// </summary>
[TempEntity]
public class TeMuzzleFlash : TeBaseTempEntity
{
    /// <summary>Where the flash is.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// Which way the barrel points. Three floats of blob whichever way it is
    /// encoded — the storage does not know it is an angle, and here the ENCODING
    /// does not either: a plain COORD vector, as stock's own table says.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Angles;

    /// <summary>How big. A whole float, unquantised.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.NoScale)]
    public float Scale;

    /// <summary>
    /// Which flash. THIRTY-TWO BITS UNSIGNED, which is stock's own width for a
    /// value whose whole range is <c>MUZZLEFLASH_TYPE_DEFAULT</c> and a handful
    /// of game-specific constants. Narrowing it would be an improvement this
    /// port is not allowed to make: the client half was compiled against 32.
    /// </summary>
    [TeProp(Bits = 32, Flags = TeFlags.Unsigned)]
    public int Type;
}
