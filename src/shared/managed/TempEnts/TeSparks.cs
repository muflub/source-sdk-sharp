using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TESparks</c> — the first temp entity to go on the wire from C#.
///
/// <para>
/// From <c>te_sparks.cpp:70</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTESparks, DT_TESparks)
///     SendPropInt(    SENDINFO( m_nMagnitude ),   4,  SPROP_UNSIGNED ),
///     SendPropInt(    SENDINFO( m_nTrailLength ), 4,  SPROP_UNSIGNED ),
///     SendPropVector( SENDINFO( m_vecDir ),      -1,  SPROP_COORD ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// IT IS FIRST BECAUSE IT EXERCISES EVERYTHING AND HAS A LIVE CALLER
/// (plan Part 10, S-Phase 1): a base table, two narrow unsigned ints, a COORD
/// vector, and <c>env_spark</c>, which fires it in this build today through
/// <c>IEngineEffects.SparkEffect</c>. Its client half is still stock's
/// <c>c_te_sparks.cpp</c>, which makes the join gate the strongest possible
/// test of this design: a managed <c>SendTable</c> paired against a
/// hand-written stock <c>RecvTable</c>.
/// </para>
///
/// <para>
/// Declaration order is wire order. Reordering these three is a wire-format
/// change, which is <c>EntityLayout.Build</c>'s existing rule.
/// </para>
/// </summary>
[TempEntity]
public class TeSparks : TeParticleSystem
{
    /// <summary>How big. Four bits unsigned, so 0..15.</summary>
    [TeProp(Bits = 4, Flags = TeFlags.Unsigned)]
    public int Magnitude;

    /// <summary>How long the streaks are. Four bits unsigned.</summary>
    [TeProp(Bits = 4, Flags = TeFlags.Unsigned)]
    public int TrailLength;

    /// <summary>
    /// Which way they go. A COORD vector, and <c>vec3_origin</c> for "no
    /// particular direction" — which is what stock's <c>TE_Sparks</c> writes
    /// when its <c>pDir</c> is null.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Direction;
}
