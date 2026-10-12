using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBreakModel</c> — a model smashed into gibs.
///
/// <para>
/// From <c>te_breakmodel.cpp:111</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBreakModel, DT_TEBreakModel)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropAngle( SENDINFO_VECTORELEM(m_angRotation, 0), 13 ),
///     SendPropAngle( SENDINFO_VECTORELEM(m_angRotation, 1), 13 ),
///     SendPropAngle( SENDINFO_VECTORELEM(m_angRotation, 2), 13 ),
///     SendPropVector( SENDINFO(m_vecSize), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecVelocity), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropInt( SENDINFO(m_nRandomization), 9, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nCount), 8, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO(m_fTime), 10, 0, 0, 102.4 ),
///     SendPropInt( SENDINFO(m_nFlags), 8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>THE FIRST <c>SendPropAngle</c> IN THE PORT, AND IT IS NOT A QANGLES.</b>
/// <c>dt_send.cpp:416</c> builds <c>SendPropAngle</c> as <c>DPT_Float</c> with
/// low 0 and high 360 — ONE COMPONENT AT A TIME. <c>SendPropQAngles</c>
/// (<c>:449</c>) is <c>DPT_Vector</c> with the same range, all three at once,
/// and <c>DT_TEProjectedDecal</c> is the inventory's only user of it. The two
/// differ on the wire in TYPE and in PROP COUNT — one prop against three — so
/// a table that used the vector form here would present eleven props where
/// <c>c_te_breakmodel.cpp</c>'s <c>RecvTable</c> expects thirteen, and the
/// engine's prop matching would run off the end of the list. Three
/// <c>RecvPropFloat( RECVINFO( m_angRotation[0] ) )</c> lines on the client
/// are what this has to pair with, and they are floats because the sender's
/// props are.
/// </para>
///
/// <para>
/// ONE C# FIELD, THREE PROPS, which is <see cref="TeParticleSystem"/>'s
/// arrangement reused rather than three hand-rolled floats:
/// <c>Elems = 3, ElemStride = 4</c> emits <c>m_angRotation[0..2]</c> at
/// <c>offset + 4i</c>, and <see cref="TeFlags.VectorElem"/> is the instruction
/// to hand the prop constructor a NEGATED offset the way
/// <c>SENDINFO_VECTORELEM</c> does (<c>dt_send.h:631</c>). Three separate
/// fields would be three things to keep in step for a value that has a type,
/// and the blob would not know they belonged together.
/// </para>
///
/// <para>
/// THIRTEEN BITS OVER [0,360] is about 0.044 degrees of resolution. It is not
/// the same width as <c>DT_TEProjectedDecal</c>'s ten and not the same as
/// <c>DT_EffectData</c>'s seven; each table chose its own and the port
/// reproduces each.
/// </para>
///
/// <para>
/// <c>m_fTime</c>'s flags are a literal <b>0</b> — not <c>SPROP_ROUNDDOWN</c>,
/// which five other floats in this port carry. Ten bits over [0, 102.4].
/// </para>
///
/// <para>
/// PRECACHE: <c>CTEBreakModel::Precache</c> (<c>te_breakmodel.cpp:78</c>) takes
/// <c>models/gibs/hgibs.mdl</c>. Evicting <c>te_breakmodel.cpp</c> takes
/// <c>g_TEBreakModel</c> off <c>CBaseTempEntity::PrecacheTempEnts</c>' list and
/// the model would simply stop being precached, with nothing anywhere saying
/// so. It is on <c>SourceSharp.Server.TempEnts.Precache</c>'s list now
/// (plan 3.6), which <see cref="TePhysicsProp"/> shares.
/// </para>
/// </summary>
[Precache(PrecacheKind.Model, "models/gibs/hgibs.mdl")]   // CTEBreakModel::Precache, te_breakmodel.cpp:78
[TempEntity]
public class TeBreakModel : TeBaseTempEntity
{
    /// <summary>Where the model was when it broke.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// Which way it was facing. THREE <c>SendPropAngle</c> props of thirteen
    /// bits each over the [0,360] the kind supplies — see the class note for
    /// why this is not one <see cref="TePropKind.QAngles"/>.
    /// </summary>
    [TeProp(TePropKind.Angle, Bits = 13, Flags = TeFlags.VectorElem, Elems = 3, ElemStride = 4)]
    public Vector Rotation;

    /// <summary>How big the pieces are.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Size;

    /// <summary>How fast they leave.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Velocity;

    /// <summary>The model that broke, in <c>SP_MODEL_INDEX_BITS</c>, flags zero.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>How much the velocities are scattered. NINE bits, not eight.</summary>
    [TeProp(Bits = 9, Flags = TeFlags.Unsigned)]
    public int Randomization;

    /// <summary>How many gibs.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Count;

    /// <summary>
    /// How long they last, 0..102.4 seconds in ten bits. FLAGS OF ZERO — no
    /// rounding direction at all, which is stock's own literal 0 and not the
    /// <c>SPROP_ROUNDDOWN</c> its neighbours across the inventory carry.
    /// </summary>
    [TeProp(Bits = 10, Flags = TeFlags.None, Low = 0.0f, High = 102.4f)]
    public float Time;

    /// <summary>What kind of debris — <c>BREAK_GLASS</c>, <c>BREAK_METAL</c> and so on.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Flags;
}
