namespace SourceSharp.Abi;

/// <summary>
/// One dispatched effect's data, as the client's effect registry wants it.
///
/// <para>
/// Mirrors <c>SourceSharpEffectData_t</c> in sourcesharp.h field for field,
/// which in turn is stock's <c>CEffectData</c> (effect_dispatch_data.h) minus
/// <c>m_iEffectName</c>. That one member is an index into the
/// <c>EffectDispatch</c> network string table, and this boundary carries the
/// NAME instead — the registry the name resolves against
/// (<c>CClientEffectRegistration</c>'s list) stays native and matches by
/// string, so an index would be a number managed code had to keep in step with
/// a table it does not own.
/// </para>
///
/// <para>
/// IT CROSSES BY POINTER, filled here and read field by field in native code —
/// the same direction <see cref="GibShot"/> goes, and the same hazard: a field
/// inserted on one side only shifts every field after it, nothing fails to
/// compile, and the symptom is an effect at plausible but wrong values.
/// <c>AbiParityTests.TheEffectDataStructMatchesTheHeader</c> is what holds the
/// two halves together; it is the reason this type is a flat blittable struct
/// with no properties rather than something friendlier.
/// </para>
///
/// <para>
/// WHY THIS IS NOT <c>TeEffectData</c>, which is the obvious question and has a
/// concrete answer. That type (<c>SourceSharp.Shared/TempEnts/TeEffectData.cs</c>)
/// is the WIRE declaration of <c>DT_EffectData</c>: its field order is the wire's
/// (angles before normal, where <c>CEffectData</c> declares them the other way
/// round), it carries <c>EffectName</c> — the string-table INDEX this boundary
/// deliberately does not — and its widths are the wire's rather than C's:
/// <c>short SurfaceProp</c>, <c>byte Color</c>, and two <c>bool</c>s, none of
/// which has a fixed width across this boundary. Handing it to native code would
/// tie the ABI's layout to the schema, so a prop reordered to match stock's own
/// table would silently move every field the native side reads. They are two
/// different things that happen to describe one effect: one is what goes on the
/// wire, this is what the client's registry wants after the decode.
/// </para>
///
/// <para>
/// WHY IT IS A STRUCT AT ALL, when every other backend on this ABI passes its
/// arguments flat. Twenty-one fields is a call nobody can read and a
/// transposition nobody can see. The line has to be drawn somewhere and this is
/// where the rest of the ABI already draws it — <c>SourceSharpBreakPiece</c>
/// and <c>SourceSharpGibShot</c> are the precedent.
/// </para>
/// </summary>
public struct EffectData
{
    /// <summary>Where the effect happens. The dispatch's own position.</summary>
    public Vector Origin;

    /// <summary>
    /// Where whatever caused it started — a bullet's muzzle, a trace's start.
    /// Zero when the effect has no direction to it.
    /// </summary>
    public Vector Start;

    /// <summary>The surface normal at <see cref="Origin"/>, where there is one.</summary>
    public Vector Normal;

    /// <summary>Pitch-yaw-roll. An orientation, which is not the same thing as
    /// <see cref="Normal"/> and is carried separately because stock does.</summary>
    public Vector Angles;

    /// <summary>The effect's own flag word. Eight bits on the wire.</summary>
    public int Flags;

    /// <summary>
    /// The entity the effect belongs to, as an EDICT INDEX rather than a handle.
    ///
    /// <para>
    /// <c>CEffectData</c> holds a <c>ClientEntityHandle_t</c> on the client and
    /// an <c>int</c> on the server, and the wire carries an index (the prop is
    /// literally named <c>entindex</c>) — so the index is the thing both sides
    /// already agree on. Native turns it back into a handle exactly as
    /// <c>effect_dispatch_data.cpp:31</c> does. NEGATIVE MEANS NO ENTITY;
    /// zero is the world, which is a real answer.
    /// </para>
    /// </summary>
    public int EntIndex;

    /// <summary>How big. Stock defaults it to 1, not 0.</summary>
    public float Scale;

    /// <summary>How hard. Twelve bits, rounded down, 0..1023 on the wire.</summary>
    public float Magnitude;

    /// <summary>How far it reaches. Ten bits, rounded down, 0..1023.</summary>
    public float Radius;

    /// <summary>Which attachment on the entity, if any.</summary>
    public int AttachmentIndex;

    /// <summary>
    /// The surface the effect landed on, as a RAW surface-property index.
    ///
    /// <para>
    /// Not the wire's value: the wire carries this plus one, because
    /// <c>SendPropInt</c> is unsigned there and a -1 "no surface" has to fit
    /// (plan 2B). The +1 belongs to the encoding and this struct is downstream
    /// of the decode.
    /// </para>
    /// </summary>
    public int SurfaceProp;

    /// <summary>
    /// A model index most of the time, despite the name. Twelve bits on the
    /// wire — <c>MAX_MODEL_INDEX_BITS</c>, which is one narrower than the
    /// thirteen everything else in this port means by "model index".
    /// </summary>
    public int Material;

    /// <summary>The damage bits that produced it.</summary>
    public int DamageType;

    /// <summary>Which hitbox, for an effect that landed on a model.</summary>
    public int HitBox;

    /// <summary>A single byte of colour index, meaning whatever the effect says.</summary>
    public int Color;

    /// <summary>
    /// Whether <see cref="Color1"/> and <see cref="Color2"/> mean anything.
    /// An int rather than a bool because a bool has no fixed width in C.
    /// </summary>
    public int CustomColors;

    /// <summary>First custom colour, 0..1 per channel.</summary>
    public Vector Color1;

    /// <summary>Second custom colour, 0..1 per channel.</summary>
    public Vector Color2;

    /// <summary>Whether the control point below means anything. See
    /// <see cref="CustomColors"/> for why it is an int.</summary>
    public int ControlPoint1;

    /// <summary>How the control point attaches (<c>PATTACH_*</c>).</summary>
    public int ControlPointAttachment;

    /// <summary>Where the control point sits relative to its attachment.</summary>
    public Vector ControlPointOffset;
}
