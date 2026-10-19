using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEExplosion</c> — the fireball, and the busiest temp entity this lane
/// owns.
///
/// <para>
/// From <c>te_explosion.cpp:100</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEExplosion, DT_TEExplosion)
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropFloat( SENDINFO(m_fScale ), 9, 0, 0.0, 51.2 ),
///     SendPropInt( SENDINFO(m_nFrameRate), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nFlags), 8, SPROP_UNSIGNED ),
///     SendPropVector( SENDINFO(m_vecNormal), -1, SPROP_COORD),
///     SendPropInt( SENDINFO(m_chMaterialType), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nRadius), 32, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nMagnitude), 32, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// IT NESTS <see cref="TeParticleSystem"/>, so <c>m_vecOrigin</c> — where the
/// explosion IS — is not in this table at all. It is in the base's, at the
/// front of this class's blob, and the writer finds it by walking the chain.
/// A caller that set only the fields below would put every explosion at the
/// world origin, which looks like an explosion rather than like nothing.
/// </para>
///
/// <para>
/// <b>THE ONLY LIVE CALLER</b> is the server's
/// <c>EffectsOverTempEnts.ExplosionEffect</c> (the native
/// <c>SourceSharp_ExplosionEffect</c> it replaced went in plan_server_native_v2
/// T4d), which is how every managed explosion in this build happens.
/// <c>explode.cpp</c> and the
/// <c>hl2/grenade_*.cpp</c> set also call <c>te-&gt;Explosion</c> and are all
/// excluded from this build (<c>server_csharp.vpc:1394</c> and the weapon
/// evictions), so the shell is the whole of it — and it is not a path that may
/// stop working for a moment.
/// </para>
///
/// <para>
/// <c>m_fScale</c>'s flags are a literal <b>0</b>: nine bits over [0, 51.2],
/// no rounding direction. <c>m_nRadius</c> and <c>m_nMagnitude</c> are
/// THIRTY-TWO bits each, which is the widest int in the whole inventory and
/// costs 64 of this table's bits by itself; stock spends them and so does this.
/// </para>
///
/// <para>
/// <c>m_chMaterialType</c> is an <c>unsigned char</c> in stock's class, so its
/// prop's read proxy is chosen for ONE byte of storage
/// (<c>dt_send.cpp:492</c>) and the C# field is a <see cref="byte"/>. Declaring
/// it an int would read three bytes of whatever follows it.
/// </para>
/// </summary>
[TempEntity]
public class TeExplosion : TeParticleSystem
{
    /// <summary>The fireball sprite, in <c>SP_MODEL_INDEX_BITS</c>, flags zero.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>
    /// How big, 0..51.2 in nine bits. FLAGS OF ZERO — no rounding direction,
    /// which is stock's own literal 0.
    /// </summary>
    [TeProp(Bits = 9, Flags = TeFlags.None, Low = 0.0f, High = 51.2f)]
    public float Scale;

    /// <summary>How fast the sprite animates.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int FrameRate;

    /// <summary>
    /// <c>TE_EXPLFLAG_*</c> — no additive, no dynamic light, no sound, and so
    /// on. Eight bits unsigned.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Flags;

    /// <summary>
    /// The surface normal the blast came off, so the client can orient the
    /// decal and the debris. <c>TE_Explosion</c> substitutes <c>(0,0,1)</c> for
    /// a null, which is up rather than a zero-length normal.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Normal;

    /// <summary>
    /// What the surface is made of — <c>'C'</c> for concrete by default. ONE
    /// BYTE of blob, because stock's member is an <c>unsigned char</c>.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte MaterialType;

    /// <summary>How far the blast reaches. A whole unsigned int on the wire.</summary>
    [TeProp(Bits = 32, Flags = TeFlags.Unsigned)]
    public int Radius;

    /// <summary>How hard it hits. Likewise thirty-two bits.</summary>
    [TeProp(Bits = 32, Flags = TeFlags.Unsigned)]
    public int Magnitude;
}
