using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEProjectedDecal</c> — a decal thrown along a facing rather than
/// stamped at a point, which is <c>info_projecteddecal</c>.
///
/// <para>
/// From <c>te_projecteddecal.cpp:91</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEProjectedDecal, DT_TEProjectedDecal)
///     SendPropVector(  SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropQAngles( SENDINFO(m_angRotation), 10 ),
///     SendPropFloat(   SENDINFO(m_flDistance), 10, SPROP_ROUNDUP, 0, 1024 ),
///     SendPropInt(     SENDINFO(m_nIndex), 9, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// THE INVENTORY'S ONLY <c>SendPropQAngles</c>, and it is NOT a vector with
/// [0,360] written out. <c>dt_send.cpp:449</c> builds it as <c>DPT_Vector</c>
/// with low 0 and high 360 AND installs <c>SendProxy_QAngles</c>, which runs
/// <c>anglemod()</c> on each component on the way out;
/// <c>SendPropAngle</c> (<c>:416</c>) is the <c>DPT_Float</c> form of the same
/// idea, one component at a time. A negative pitch — which is every downward
/// facing — through a plain vector proxy encodes outside its own declared
/// range, and what arrives is not an angle. That is the failure mode this kind
/// exists for: it decodes as garbage rather than failing.
/// </para>
///
/// <para>
/// Ten bits per component, and no flags: the range IS the encoding.
/// </para>
///
/// <para>
/// LEDGER ROW C3 IS THE CLIENT'S AND IS STILL OPEN, and it is BIGGER than C2's
/// rather than the same gap: the arm is stock's <i>"Only decal the world +
/// brush models"</i> branch, which is the ORDINARY case for an
/// <c>info_projecteddecal</c>. Lane CX has since ported the client half —
/// <c>ClientTeProjectedDecal</c>, over a client trace this build did not have —
/// so the row's arm is
/// <c>ClientTeProjectedDecal.WorldAndBrushDecalIsMissing</c> rather than
/// <c>c_te_projecteddecal.cpp:152</c>, and it now warns once per level. The
/// static-prop arm draws. Nothing on this side can have moved the row either
/// way.
/// </para>
/// </summary>
[TempEntity]
public class TeProjectedDecal : TeBaseTempEntity
{
    /// <summary>Where the projection starts.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// Which way it is thrown. Three floats of blob, one
    /// <see cref="TePropKind.QAngles"/> prop of ten bits per component — see the
    /// class note for why this is not a <see cref="TePropKind.Vector"/>.
    /// </summary>
    [TeProp(TePropKind.QAngles, Bits = 10)]
    public Vector Rotation;

    /// <summary>
    /// How far the mark is thrown. Ten bits over [0,1024] rounded UP, which is
    /// stock's own direction and not a detail: a distance that rounds DOWN can
    /// stop short of the surface and paint nothing at all.
    /// </summary>
    [TeProp(Bits = 10, Flags = TeFlags.RoundUp, Low = 0f, High = 1024f)]
    public float Distance;

    /// <summary>The engine's decal index. Nine bits unsigned.</summary>
    [TeProp(Bits = 9, Flags = TeFlags.Unsigned)]
    public int Index;
}
