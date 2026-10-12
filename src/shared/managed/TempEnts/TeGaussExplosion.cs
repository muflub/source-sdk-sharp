using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEGaussExplosion</c> — <b>BEHAVIOUR LEDGER ROW S3, RESTORED, AND
/// UNCONDITIONALLY.</b> The gauss gun's discharge burst.
///
/// <para>
/// From <c>hl2/te_gaussexplosion.cpp:43</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEGaussExplosion, DT_TEGaussExplosion )
///     SendPropInt( SENDINFO(m_nType), 2, SPROP_UNSIGNED ),
///     SendPropVector( SENDINFO(m_vecDirection), -1, SPROP_COORD ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>TWO SEPARATE THINGS TURNED THIS OFF AND A MANAGED DECLARATION ANSWERS
/// BOTH.</b> <c>hl2/te_gaussexplosion.cpp</c> is excluded from this build
/// (<c>server_csharp.vpc:1168</c>), so <c>CTEGaussExplosion</c> has not been in
/// the server's class list; and <c>ITempEntsSystem::GaussExplosion</c>'s BODY is
/// <c>#ifdef HL2_DLL</c> (<c>te.cpp:501</c>), while <c>HL2_DLL</c> appears
/// nowhere in this build's defines (<c>server_csharp.vpc:327</c>, plan 2G) — so
/// even with the file compiled in, <c>te-&gt;GaussExplosion</c> would have been a
/// call to an empty function. <b>A managed declaration has no <c>#ifdef</c>:
/// it registers whatever the native build defines</b>, and
/// <see cref="ITempEnts.GaussExplosion"/> is a method with a body on every
/// target.
/// </para>
///
/// <para>
/// <c>C_TEGaussExplosion</c> is in <c>c_impact_effects.cpp:61</c> — not in any
/// <c>c_te_*.cpp</c> — has been registered the whole time, and calls
/// <c>FX_GaussExplosion</c> the moment one arrives. (Step 7a moved that file's
/// 1,391 compiled lines, <c>FX_GaussExplosion</c> among them, to
/// <c>sourcesharp/game/client/shell_impact_effects.cpp</c> and took the stock TU
/// out of the build; the guarded temp entity stayed behind, which is why the
/// line number moved.) Nothing has ever arrived.
/// The live join is the first time <c>DT_TEGaussExplosion</c> has paired in this
/// build.
/// </para>
///
/// <para>
/// A PURE ADDITION, like <see cref="TeImpact"/>: nothing to evict, because the
/// file was already out, and no <c>TE_GaussExplosion</c> to forward, because the
/// one call to it is inside the <c>HL2_DLL</c> arm that this build does not
/// compile. Restoring it by defining that native function instead would restore
/// a symbol nothing calls.
/// </para>
///
/// <para>
/// The base is <c>DT_TEParticleSystem</c>, so the burst's ORIGIN is the base's
/// <c>m_vecOrigin</c> at the front of this class's blob — the field this table
/// does not declare and every caller has to set.
/// </para>
/// </summary>
[TempEntity]
public class TeGaussExplosion : TeParticleSystem
{
    /// <summary>
    /// Which of the burst's forms. TWO BITS, so 0..3 —
    /// <c>c_impact_effects.cpp</c>'s <c>FX_GaussExplosion</c> switches on it and
    /// has three cases.
    /// </summary>
    [TeProp(Bits = 2, Flags = TeFlags.Unsigned)]
    public int Type;

    /// <summary>
    /// Which way it sprays. A COORD vector, in stock's declared order: the type
    /// goes FIRST on the wire though <c>TE_GaussExplosion</c> assigns the
    /// direction before it.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Direction;
}
