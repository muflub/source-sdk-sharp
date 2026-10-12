using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_EffectData</c> — the payload every <c>DispatchEffect()</c> in the build
/// rides on, and the widest table in the inventory.
///
/// <para>
/// From <c>game/shared/effect_dispatch_data.cpp:81</c>, taking the
/// <c>#else</c> of both preprocessor branches, because <c>HL2_DLL</c> and
/// <c>TF_DLL</c> appear in NO <c>$PreprocessorDefinitions</c> in this build's
/// own <c>$Include</c> chain (plan_tmp.md 2G, and
/// <see cref="TeEffectDispatch"/> for where that is asserted rather than
/// believed — the tree carries five <c>.vpc</c> files that DO define them and
/// every one of them builds a different mod):
/// </para>
/// <code>
/// BEGIN_SEND_TABLE_NOBASE( CEffectData, DT_EffectData )
///     SendPropFloat( SENDINFO_NOCHECK( m_vOrigin[0] ), -1, SPROP_COORD_MP_INTEGRAL ),
///     ... [1], [2], then m_vStart[0..2] the same way ...
///     SendPropQAngles( SENDINFO_NOCHECK( m_vAngles ), 7 ),
///     SendPropVector( SENDINFO_NOCHECK( m_vNormal ), 0, SPROP_NORMAL ),
///     SendPropInt( SENDINFO_NOCHECK( m_fFlags ), MAX_EFFECT_FLAG_BITS, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO_NOCHECK( m_flMagnitude ), 12, SPROP_ROUNDDOWN, 0.0f, 1023.0f ),
///     SendPropFloat( SENDINFO_NOCHECK( m_flScale ), 0, SPROP_NOSCALE ),
///     SendPropInt( SENDINFO_NOCHECK( m_nAttachmentIndex ), 5 ),
///     SendPropIntWithMinusOneFlag( SENDINFO_NOCHECK( m_nSurfaceProp ), 8, SendProxy_ShortAddOne ),
///     SendPropInt( SENDINFO_NOCHECK( m_iEffectName ), MAX_EFFECT_DISPATCH_STRING_BITS, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO_NOCHECK( m_nMaterial ), MAX_MODEL_INDEX_BITS, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO_NOCHECK( m_nDamageType ), 32, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO_NOCHECK( m_nHitBox ), 13, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO_NAME( m_nEntIndex, entindex ), MAX_EDICT_BITS, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO_NOCHECK( m_nColor ), 8, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO_NOCHECK( m_flRadius ), 10, SPROP_ROUNDDOWN, 0.0f, 1023.0f ),
///     SendPropBool( SENDINFO_NOCHECK( m_bCustomColors) ),
///     SendPropVector( SENDINFO_NOCHECK( m_CustomColors.m_vecColor1 ), 8, 0, 0, 1 ),
///     SendPropVector( SENDINFO_NOCHECK( m_CustomColors.m_vecColor2 ), 8, 0, 0, 1 ),
///     SendPropBool( SENDINFO_NOCHECK( m_bControlPoint1) ),
///     SendPropInt( SENDINFO_NOCHECK( m_ControlPoint1.m_eParticleAttachment ), 5, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO_NOCHECK( m_ControlPoint1.m_vecOffset[0] ), -1, SPROP_COORD ),
///     ... [1], [2] ...
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// TWENTY-EIGHT PROPS FROM TWENTY-TWO C# FIELDS, and the count is worth stating
/// because plan_tmp.md Part 6 says thirty-one. Its own enumeration lists
/// twenty-eight and stock's table declares twenty-eight `SendProp` lines; the
/// figure in the prose is wrong and the table on disk is what
/// <c>TempEntEffectDataTests</c> checks against. Three of the twenty-two fields
/// expand into three props each (<c>m_vOrigin</c>, <c>m_vStart</c>,
/// <c>m_ControlPoint1.m_vecOffset</c>), which is 22 + 6 = 28.
/// </para>
///
/// <para>
/// IT IS A TABLE AND NOT A ServerClass — <c>RegistersClass = false</c> — for a
/// reason the other <c>false</c>s in this directory do not share.
/// <see cref="TeBaseTempEntity"/> and <see cref="TeParticleSystem"/> abstain
/// because STOCK still registers a class of that name. <c>CEffectData</c> never
/// was a class: <c>BEGIN_SEND_TABLE_NOBASE</c> builds a table and nothing else,
/// and <c>CEffectData</c> is a plain struct that lives inside other things. So
/// this one stays a table forever, and the day <c>basetempentity.cpp</c> is
/// evicted and the other two flip to <c>true</c>, this line does not move.
/// </para>
///
/// <para>
/// THE ORDER IS STOCK'S TABLE ORDER AND NOT PART 6's PROSE ORDER. Part 6 lists
/// <c>m_vNormal</c> before <c>m_vAngles</c>; the table declares
/// <c>m_vAngles</c> first (line 102) and <c>m_vNormal</c> second (line 107).
/// Declaration order is wire order, so the table wins. That single swap is
/// exactly the class of transcription error <c>cs_sv_te_dump</c> cannot see —
/// both its columns come from this file — which is why the parity half of the
/// test reads the <c>.cpp</c> off disk.
/// </para>
///
/// <para>
/// THE FIELD THAT PLAYS THE WRONG EFFECT RATHER THAN NONE.
/// <see cref="EffectName"/> is an index into the <c>EffectDispatch</c> network
/// string table, and the client turns it straight back into a name
/// (<c>c_te_effect_dispatch.cpp:153</c>) to pick a callback. Every other field
/// in this port fails visibly — an effect in the wrong place, or nothing at all.
/// A wrong index here fires a DIFFERENT effect, which reads as a bug in that
/// effect. Its width is therefore a symbol
/// (<see cref="TeBits.EffectDispatchString"/>) and not a 10, and the value is
/// the index and nothing else — no <c>+1</c>, unlike its neighbour
/// <see cref="SurfaceProp"/> three lines away.
/// </para>
///
/// <para>
/// It doubles as the argument to <c>ITempEnts.DispatchEffect</c>. That is not
/// an economy: <c>CEffectData</c> is stock's own argument type for the same
/// call, and giving the managed API a second struct of the same twenty-two
/// values would be two things to keep in step for one payload.
/// </para>
///
/// <para>
/// <b>IT IS THE ONE CONVERTED DECLARATION THAT STAYS A <c>struct</c>, AND THAT
/// IS NOT AN OMISSION.</b> Every other one became a <c>class</c> deriving
/// <c>TempEntState</c> so a derived declaration could inherit its base's
/// fields. This table has no base and no derived class; what it has is a
/// containing FIELD — <c>TeEffectDispatch.EffectData</c> — and
/// <c>EntityData.IsNestedBlob</c> requires a value type there for exactly the
/// reason <c>TeSplinePoints</c> is an <c>[InlineArray]</c> rather than a
/// <c>Vector[]</c>: a class is a REFERENCE, so laying one out would put eight
/// bytes of pointer where <c>DT_TEEffectDispatch</c>'s blob has to hold
/// <c>CEffectData</c>'s hundred and thirty-two. So the wire names and the
/// inferable kinds go, as everywhere else, and the <c>struct</c> stays.
/// </para>
/// </summary>
[TempEntity("CEffectData", "DT_EffectData", RegistersClass = false)]
public struct TeEffectData
{
    /// <summary>
    /// Where the effect happens.
    ///
    /// <para>
    /// THE NON-HL2 FORM (plan 2G): three floats with
    /// <see cref="TeFlags.CoordMpIntegral"/>, which is fourteen integer bits
    /// and NO fraction. The <c>#ifdef HL2_DLL</c> arm above it asks for
    /// <c>COORD_INTEGER_BITS+SUBINCH_PRECISION</c> over an explicit range —
    /// sub-inch precision — and a reader who took that arm would produce a
    /// table that decodes to plausible coordinates a few inches out.
    /// </para>
    ///
    /// <para>
    /// Declared with <c>SENDINFO_NOCHECK</c> and a POSITIVE offset, so unlike
    /// <see cref="TeParticleSystem"/>'s origin this carries no
    /// <see cref="TeFlags.VectorElem"/>: stock spells the three elements out by
    /// hand as <c>m_vOrigin[0]</c>..<c>[2]</c> rather than through
    /// <c>SENDINFO_VECTORELEM</c>, so the engine never sets
    /// <c>SPROP_IS_A_VECTOR_ELEM</c> on them and neither does this.
    /// </para>
    /// </summary>
    [TeProp(TePropKind.Float, Bits = -1,
            Flags = TeFlags.CoordMpIntegral, Elems = 3, ElemStride = 4)]
    public Vector Origin;

    /// <summary>
    /// Where it came FROM — the shot's origin for an impact, so the client can
    /// re-trace. Encoded exactly as <see cref="Origin"/>.
    /// </summary>
    [TeProp(TePropKind.Float, Bits = -1,
            Flags = TeFlags.CoordMpIntegral, Elems = 3, ElemStride = 4)]
    public Vector Start;

    /// <summary>
    /// The facing, as angles. ONE <see cref="TePropKind.QAngles"/> prop of
    /// seven bits per component, which is not a vector with [0,360] written
    /// out: <c>SendPropQAngles</c> (<c>dt_send.cpp:449</c>) installs
    /// <c>SendProxy_QAngles</c>, and <c>anglemod()</c> is what keeps a negative
    /// pitch inside the declared range instead of encoding outside it.
    ///
    /// <para>
    /// Stored as a <see cref="Vector"/> and not a <c>QAngle</c> because
    /// <c>EntityLayout</c> lays out three floats either way and has no size for
    /// a <c>QAngle</c>; the difference between the two is the ENCODING, which
    /// is this attribute's business. <see cref="TeProjectedDecal"/> says the
    /// same thing about the inventory's other qangles prop.
    /// </para>
    /// </summary>
    [TeProp(TePropKind.QAngles, Bits = 7)]
    public Vector Angles;

    /// <summary>
    /// The surface normal. <c>SPROP_NORMAL</c>, which sends x and y and one
    /// SIGN BIT for z — twenty-five bits for a unit vector against the
    /// sixty-six three COORD floats would cost.
    ///
    /// <para>
    /// Bits is 0 and not -1, because that is the literal stock passes. It makes
    /// no difference to the encoding (the flag decides) and every difference to
    /// the parity check, which compares this number with the one in the table.
    /// </para>
    /// </summary>
    [TeProp(Bits = 0, Flags = TeFlags.Normal)]
    public Vector Normal;

    /// <summary>
    /// The effect's own flag word — <c>MAX_EFFECT_FLAG_BITS</c>, which
    /// <c>effect_dispatch_data.h:35</c> owns and native resolves. Note
    /// <c>EFFECTDATA_NO_RECORD</c> (0x80000000) is deliberately ABOVE those
    /// eight bits and is never networked, which is stock's own comment on the
    /// line above the define.
    /// </summary>
    [TeProp(BitsFrom = TeBits.EffectFlag, Flags = TeFlags.Unsigned)]
    public int Flags;

    /// <summary>How big the effect is. Twelve bits over [0,1023], rounded DOWN.</summary>
    [TeProp(Bits = 12, Flags = TeFlags.RoundDown, Low = 0f, High = 1023f)]
    public float Magnitude;

    /// <summary>
    /// A whole float, unscaled and unclamped — the one prop here that spends
    /// thirty-two bits. Stock declares it <c>(0, SPROP_NOSCALE)</c>.
    /// </summary>
    [TeProp(Bits = 0, Flags = TeFlags.NoScale)]
    public float Scale;

    /// <summary>
    /// Which attachment on the entity. Five bits and SIGNED — stock passes no
    /// flags argument at all, so -1 ("no attachment") survives the wire.
    /// </summary>
    [TeProp(Bits = 5)]
    public int AttachmentIndex;

    /// <summary>
    /// The surface property index the effect hit, which decides the sound and
    /// the debris.
    ///
    /// <para>
    /// THE PROXY IS GONE AND THE <c>+1</c> IS NOT (plan 2B). Stock declares
    /// <c>SendPropIntWithMinusOneFlag( ..., 8, SendProxy_ShortAddOne )</c>,
    /// which <c>game/server/sendproxy.cpp:73</c> builds as a plain
    /// <c>SendPropInt( x, 8, SPROP_UNSIGNED )</c> carrying a proxy that reads
    /// the <c>short</c> and adds one. A send proxy never appears in the table
    /// the client pairs against, so the declaration here is that same plain
    /// int, and <c>TempEnts.DispatchEffect</c> writes <c>value + 1</c> — an
    /// identical wire AND an identical handshake. The client's
    /// <c>RecvProxy_ShortSubOne</c> takes it back off.
    /// </para>
    ///
    /// <para>
    /// A <c>short</c> and not an <c>int</c>, because <c>CEffectData</c>'s
    /// member is one and <c>SendPropInt</c> picks its read proxy from the
    /// declared storage width (<c>dt_send.cpp:492</c>). Eight unsigned bits
    /// means the useful range is a surface index of -1..254.
    /// </para>
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public short SurfaceProp;

    /// <summary>
    /// THE FIELD THIS WHOLE PHASE IS ABOUT: the entry in the
    /// <c>EffectDispatch</c> network string table, which is how one temp entity
    /// stands for fifty-two client effects.
    ///
    /// <para>
    /// <c>MAX_EFFECT_DISPATCH_STRING_BITS</c> (ten, so 1024 names) as a SYMBOL,
    /// resolved natively from <c>effect_dispatch_data.h:132</c>. A restated 10
    /// that drifted would not misplace an effect — it would play a different
    /// one, silently, because the client reads the string back by index and
    /// looks up a callback by name.
    /// </para>
    ///
    /// <para>
    /// NOTHING IS ADDED TO IT. Its neighbour <see cref="SurfaceProp"/> carries
    /// a <c>+1</c> and this does not, and the two sit three lines apart in
    /// stock's table for exactly the reason they sit three lines apart here.
    /// </para>
    /// </summary>
    [TeProp(BitsFrom = TeBits.EffectDispatchString, Flags = TeFlags.Unsigned)]
    public int EffectName;

    /// <summary>
    /// A model index most of the time, despite the name — stock's own comment
    /// on <c>CEffectData::m_nMaterial</c> says so.
    ///
    /// <para>
    /// TWELVE BITS, NOT THIRTEEN, AND THE SYMBOL IS NOT THE ONE PART 6 NAMES.
    /// The table says <c>MAX_MODEL_INDEX_BITS</c>, which is
    /// <c>common/qlimits.h:27</c> and is <b>12</b>. That is a DIFFERENT symbol
    /// from <c>SP_MODEL_INDEX_BITS</c> (<c>public/const.h:62</c>, <b>13</b>),
    /// which is what <c>SendPropModelIndex</c> uses and what
    /// <see cref="TeBits.ModelIndex"/> resolves to — so plan Part 6's
    /// "m_nMaterial ModelIndex u" would have made this one bit too wide, which
    /// is the failure with no log line anywhere.
    /// </para>
    ///
    /// <para>
    /// It is a literal here because <see cref="TeBits"/> has no member for
    /// <c>MAX_MODEL_INDEX_BITS</c> and adding one means editing the ABI header,
    /// which is the integrator's and not a lane's. A literal that nothing
    /// checks is the drift plan 3.1 forbids, so
    /// <c>TempEntEffectDataTests.TheMaterialWidthIsQlimitsTwelveAndNotConstsThirteen</c>
    /// READS both headers off disk and asserts this is the first and not the
    /// second. That is the same answer <c>TierParityTests</c> gives for the same
    /// problem: assert the agreement rather than maintaining it by hand.
    /// </para>
    /// </summary>
    [TeProp(Bits = 12, Flags = TeFlags.Unsigned)]
    public int Material;

    /// <summary>The damage bits that caused it. A whole unsigned word.</summary>
    [TeProp(Bits = 32, Flags = TeFlags.Unsigned)]
    public int DamageType;

    /// <summary>Which hitbox was struck. Thirteen bits unsigned.</summary>
    [TeProp(Bits = 13, Flags = TeFlags.Unsigned)]
    public int HitBox;

    /// <summary>
    /// The entity the effect belongs to.
    ///
    /// <para>
    /// STOCK'S WIRE NAME WAS <c>entindex</c> AND NOT <c>m_nEntIndex</c>, which
    /// is the one prop in this table whose name was never its member's.
    /// <c>SENDINFO_NAME( m_nEntIndex, entindex )</c> exists because the
    /// CLIENT's <c>CEffectData</c> has no <c>m_nEntIndex</c> at all: it keeps a
    /// <c>ClientEntityHandle_t</c> and fills it through
    /// <c>RecvProxy_EntIndex</c>, declared as
    /// <c>RecvPropInt( "entindex", 0, SIZEOF_IGNORE, 0, RecvProxy_EntIndex )</c>.
    /// Two hand-written tables that had to agree, and the name was the whole of
    /// the agreement.
    /// </para>
    ///
    /// <para>
    /// IT IS <c>EntIndex</c> NOW, AND THE THING THAT MADE THAT SAFE IS THAT
    /// BOTH RecvTables ARE GONE. <c>c_te_effect_dispatch.cpp</c>'s
    /// <c>IMPLEMENT_CLIENTCLASS</c> is inside a <c>#ifndef SOURCESHARP</c> and
    /// compiled out; the client builds its props from THIS declaration, so the
    /// two sides pair on whatever this file says. It would not have been safe
    /// at any earlier point in the port, and the position — fourteenth of
    /// twenty-two fields, fourteenth of twenty-eight props — is what
    /// <c>TempEntEffectDataTests</c> still checks against stock's <c>.cpp</c>,
    /// because ORDER is what is left on the wire once the name is ours.
    /// </para>
    /// </summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int EntIndex;

    /// <summary>
    /// A single colour byte, and a <c>byte</c> in the blob because
    /// <c>CEffectData::m_nColor</c> is an <c>unsigned char</c> — a four-byte
    /// store here would be three bytes of the field after it.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte Color;

    /// <summary>How far the effect reaches. Ten bits over [0,1023], rounded DOWN.</summary>
    [TeProp(Bits = 10, Flags = TeFlags.RoundDown, Low = 0f, High = 1023f)]
    public float Radius;

    /// <summary>
    /// Whether the two colours below mean anything. <c>SendPropBool</c>, which
    /// is <c>SendPropInt( x, 1, SPROP_UNSIGNED )</c> over a one-byte
    /// <c>bool</c> (plan 2B).
    /// </summary>
    [TeProp(Bits = 1, Flags = TeFlags.Unsigned)]
    public bool CustomColors;

    /// <summary>
    /// A particle colour, as three eight-bit components over [0,1] — NOT a
    /// coordinate, and the flags argument really is a zero. Stock's own name for
    /// the pair is <c>te_tf_particle_effects_colors_t</c> and the prop names
    /// keep the struct qualifier, dot and all: the client's
    /// <c>RECVINFO( m_CustomColors.m_vecColor1 )</c> stringifies to exactly
    /// this, and the two sides pair on it.
    /// </summary>
    [TeProp(Bits = 8, Low = 0f, High = 1f)]
    public Vector Color1;

    /// <summary>The second custom colour, encoded identically.</summary>
    [TeProp(Bits = 8, Low = 0f, High = 1f)]
    public Vector Color2;

    /// <summary>Whether the control point below means anything.</summary>
    [TeProp(Bits = 1, Flags = TeFlags.Unsigned)]
    public bool ControlPoint1;

    /// <summary>
    /// Where the control point hangs off — a <c>ParticleAttachment_t</c>, which
    /// is a plain enum and therefore four bytes of blob and five unsigned bits
    /// of wire.
    /// </summary>
    [TeProp(Bits = 5, Flags = TeFlags.Unsigned)]
    public int ParticleAttachment;

    /// <summary>
    /// The control point's offset from the attachment. Three <c>SPROP_COORD</c>
    /// floats — the ORDINARY coordinate encoding, unlike <see cref="Origin"/>
    /// and <see cref="Start"/> at the top of this table, which take the
    /// multiplayer integral form. Two coordinate encodings in one table is
    /// stock's, and copying it is the whole job.
    /// </summary>
    [TeProp(TePropKind.Float, Bits = -1,
            Flags = TeFlags.Coord, Elems = 3, ElemStride = 4)]
    public Vector ControlPointOffset;
}
