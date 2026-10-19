using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TELargeFunnel</c> — the cone of sprites that pours into or out of a
/// point. Two props, and one of them is two bits wide.
///
/// <para>
/// From <c>te_largefunnel.cpp:81</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTELargeFunnel, DT_TELargeFunnel)
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropInt( SENDINFO(m_nReversed), 2, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// IT NESTS <see cref="TeParticleSystem"/>, so the point the funnel converges
/// on — <c>m_vecOrigin</c> — is the base's field and lives at the front of this
/// class's blob. Setting only the two props below would draw every funnel at
/// the world origin.
/// </para>
///
/// <para>
/// <c>m_nReversed</c> IS TWO BITS FOR A BOOLEAN, which is stock's own choice
/// and not a rounding of one. <c>ITempEntsSystem::LargeFunnel</c> takes an
/// <c>int reversed</c> and <c>c_te_largefunnel.cpp</c> tests it for non-zero,
/// so nothing depends on the second bit — but the RecvTable was compiled
/// against two and a one-bit prop here would leave every prop after it in the
/// message one bit out of place. There is nothing after it in this table today,
/// which is exactly the kind of accident that makes a narrowing look harmless
/// until the table grows.
/// </para>
///
/// <para>
/// <b>A LIVE CALLER.</b> The server's <c>EffectsOverTempEnts.FunnelEffect</c>,
/// behind <c>IEngineEffects.FunnelEffect</c> (the native
/// <c>SourceSharp_FunnelEffect</c> went in plan_server_native_v2 T4d).
/// Stock's other caller, <c>effects.cpp:1323</c>'s <c>CEnvFunnel</c>, is
/// excluded from this build (<c>server_csharp.vpc:1477</c>), so the shell is
/// the whole of it and it must not stop working for a moment.
/// </para>
/// </summary>
[TempEntity]
public class TeLargeFunnel : TeParticleSystem
{
    /// <summary>The sprite the funnel is made of. <c>SP_MODEL_INDEX_BITS</c>, flags zero.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>
    /// Whether it pours outward instead of inward. TWO bits unsigned, which is
    /// stock's width for a value only ever tested for non-zero.
    /// </summary>
    [TeProp(Bits = 2, Flags = TeFlags.Unsigned)]
    public int Reversed;
}
