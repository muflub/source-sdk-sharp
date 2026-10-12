using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TESmoke</c> — a smoke puff. <see cref="TeSprite"/>'s table with a
/// frame rate where the brightness was.
///
/// <para>
/// From <c>te_smoke.cpp:97</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTESmoke, DT_TESmoke)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropFloat( SENDINFO(m_fScale ), 8, SPROP_ROUNDDOWN, 0.0, 25.6 ),
///     SendPropInt( SENDINFO(m_nFrameRate), 8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// THE ONE IN THIS LANE WITH A LIVE NATIVE CALLER ON THE LINK LINE:
/// <c>EffectsServer.cpp:128</c>'s <c>CEffectsServer::Smoke</c> is
/// <c>te-&gt;Smoke</c>, reached from <c>UTIL_Smoke</c> (<c>util.cpp:1771</c>)
/// through <c>g_pEffects</c>. Both files are still compiled here — the S1 shim
/// records why <c>te.cpp</c> cannot leave yet — so this table is on the wire
/// from the moment it registers, not only when something managed asks.
/// </para>
/// </summary>
[TempEntity]
public class TeSmoke : TeBaseTempEntity
{
    /// <summary>Where the puff is.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>Which sprite. Thirteen bits, no flags — see <see cref="TeSprite"/>.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex, Flags = TeFlags.None)]
    public int ModelIndex;

    /// <summary>How big, 0..25.6, ROUNDDOWN.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundDown, Low = 0.0f, High = 25.6f)]
    public float Scale;

    /// <summary>How fast the sprite's animation runs, 0..255.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int FrameRate;
}
