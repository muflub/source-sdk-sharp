using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEWorldDecal</c> — a decal on world brushwork, with no entity and no
/// trace: the client stamps it where it is told.
///
/// <para>
/// From <c>te_worlddecal.cpp:92</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEWorldDecal, DT_TEWorldDecal)
/// #if defined( TF_DLL )
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD_MP_INTEGRAL ),
/// #else
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
/// #endif
///     SendPropInt( SENDINFO(m_nIndex), 9, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// THE <c>#else</c> BRANCH, and plan 2G is why: <c>TF_DLL</c> appears in
/// neither target's defines here, so the origin is <c>SPROP_COORD</c> and not
/// <c>SPROP_COORD_MP_INTEGRAL</c>. Both sides take the same branch because
/// neither define is set in either, and the wrong one would decode to plausible
/// whole numbers rather than to nothing — which is the failure that gets blamed
/// on the effect. It is one of the three tables that finding decides.
/// </para>
/// </summary>
[TempEntity]
public class TeWorldDecal : TeBaseTempEntity
{
    /// <summary>Where the mark goes. COORD, not COORD_MP_INTEGRAL — see the class note.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>The engine's decal index. Nine bits unsigned.</summary>
    [TeProp(Bits = 9, Flags = TeFlags.Unsigned)]
    public int Index;
}
