using System.Reflection;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// What kind of <c>SendProp</c>/<c>RecvProp</c> a temp entity field becomes.
///
/// <para>
/// Spelled <c>TePropKind</c> rather than plan 3.1's <c>TeProp</c>, which would
/// have collided with <c>[TeProp(...)]</c> — the attribute's own name with its
/// suffix dropped. Two things called TeProp, one of which is written at every
/// field in the port, is not a name worth defending.
/// </para>
///
/// <para>
/// Five would do — plan_tmp.md 2B counts int, float and vector, plus datatable
/// nesting and one array, and says every custom proxy disappears. Two do not:
/// <c>SendPropAngle</c> and <c>SendPropQAngles</c> install
/// <c>SendProxy_AngleToFloat</c> and <c>SendProxy_QAngles</c>, which run
/// <c>anglemod()</c> on the way out. That is not a proxy this port may drop —
/// a negative pitch through a plain float proxy encodes outside its declared
/// [0,360] range — so the two keep their own kinds.
/// </para>
/// </summary>
public enum TePropKind
{
    Int       = 0,
    Float     = 1,
    Vector    = 2,
    Angle     = 3,
    QAngles   = 4,
    DataTable = 5,
    Array     = 6,
}

/// <summary>
/// A width that is a SYMBOL in stock's table rather than a literal.
///
/// <para>
/// NONE OF THESE MAY BE RESTATED HERE (plan 3.1). Each is resolved natively from
/// the header that owns it — <c>MAX_EDICT_BITS</c> and
/// <c>SP_MODEL_INDEX_BITS</c> from <c>public/const.h</c>, and so on — because a
/// prop one bit narrower than the other side expects is a bitstream that stops
/// making sense partway through, and produces no log line anywhere. A second
/// copy of a width is how that happens. <c>NetTierInfo</c> set the precedent:
/// a rule with two implementations drifts.
/// </para>
///
/// <para>
/// Widths that are literals in stock's own table stay literals here. This is
/// only for the ones that are not.
/// </para>
/// </summary>
public enum TeBits
{
    Literal              = 0,
    MaxEdict             = 1,
    ModelIndex           = 2,
    EfMax                = 3,
    AnimationSkin        = 4,
    MaxPlayersLog2       = 5,
    EffectFlag           = 6,
    EffectDispatchString = 7,
    NetworkedEHandle     = 8,
}

/// <summary>
/// Encoding flags. THE VALUES ARE <c>SPROP_*</c>'s OWN, mirrored in
/// <c>sourcesharp.h</c> and COMPILE_TIME_ASSERTed against <c>dt_common.h</c> in
/// <c>shell_tempents.cpp</c> — so the translation into a SendProp is the
/// identity and the agreement fails a build rather than a wire.
/// </summary>
[Flags]
public enum TeFlags
{
    None      = 0,
    Unsigned  = 1 << 0,
    Coord     = 1 << 1,
    NoScale   = 1 << 2,
    RoundDown = 1 << 3,
    RoundUp   = 1 << 4,
    Normal    = 1 << 5,

    /// <summary>
    /// Declare this the way <c>SENDINFO_VECTORELEM</c> does: hand the prop
    /// constructor a NEGATED offset. The engine flips it back and sets
    /// <c>SPROP_IS_A_VECTOR_ELEM</c> itself (<c>dt_send.h:281</c>), which is
    /// why this borrows that flag's bit and is never passed through as one.
    /// </summary>
    VectorElem = 1 << 11,

    CoordMp              = 1 << 13,
    CoordMpLowPrecision  = 1 << 14,
    CoordMpIntegral      = 1 << 15,
}

/// <summary>
/// The wire encoding of one temp entity field.
///
/// <para>
/// IT IS AN <see cref="EntityFieldAttribute"/>, which is the whole of how a TE
/// avoids declaring an offset. <c>EntityLayout.Build</c> already walks
/// <c>[EntityField]</c> members in declaration order and computes each one's
/// offset and size; a temp entity's blob is exactly that problem, so it inherits
/// the answer rather than restating it. What is left for this attribute is the
/// part <c>EntityLayout</c> has no opinion about: how many bits, with which
/// flags, between which bounds.
/// </para>
///
/// <para>
/// Declaration order is wire order, and reordering the members is a wire-format
/// change. That is <c>EntityLayout.Build</c>'s existing rule and temp entities
/// inherit it rather than inventing one.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class TePropAttribute : EntityFieldAttribute
{
    /// <summary>
    /// The kind stated explicitly, for the few fields whose C# type does not
    /// decide it — an <c>int</c> that is an <see cref="TePropKind.EHandle"/>,
    /// or a field that is a <see cref="TePropKind.DataTable"/> or an
    /// <see cref="TePropKind.Array"/>.
    /// </summary>
    public TePropAttribute(TePropKind kind)
    {
        Kind = kind;
        Inferred = false;
    }

    /// <summary>
    /// The usual case: the kind comes from the field's type.
    ///
    /// <para>
    /// A <c>Vector</c> is a vector, a <c>float</c> is a float, an <c>int</c>,
    /// <c>short</c>, <c>byte</c>, <c>bool</c> or <c>enum</c> is an int. Saying
    /// it again in the attribute was a second copy of a fact the type already
    /// carries, and two copies of one fact is the drift this tree keeps writing
    /// memories about — a field retyped without its attribute following would
    /// have gone on the wire at its old width.
    /// </para>
    /// </summary>
    public TePropAttribute()
    {
        Inferred = true;
    }

    /// <summary>Whether <see cref="Kind"/> is still to be taken from the member's type.</summary>
    public bool Inferred { get; }

    public TePropKind Kind { get; private set; }

    /// <summary>
    /// Fills in <see cref="Kind"/> from the member's type, for the parameterless
    /// form. Refuses rather than guesses: a type with no obvious prop kind is a
    /// field that must say which it is.
    /// </summary>
    public TePropAttribute Resolve(Type memberType, string memberName)
    {
        if (!Inferred)
            return this;

        // A Vector infers VECTOR, never Angle or QAngles: those are stored in a
        // Vector too and only the table knows which, so they stay explicit. An
        // EntityHandle is an Int with a symbolic width (plan 2B), not a kind of
        // its own.
        //
        // A QAngle DOES infer, and that is the point of it being a distinct
        // type: SendPropQAngles is what stock declares for a facing, the engine
        // writes it through bf_write's angle path rather than three coords, and
        // a member typed QAngle has said which of the two it is. Declaring the
        // same three floats as a Vector still leaves the table to say.
        //
        // A Color32 infers INT because the kind here is each COMPONENT's:
        // there is no SendPropColor, so TeClassBuilder expands the one member
        // into four eight-bit unsigned props.
        Kind = memberType == typeof(Vector)       ? TePropKind.Vector
             : memberType == typeof(QAngle)       ? TePropKind.QAngles
             : memberType == typeof(Color32)      ? TePropKind.Int
             : memberType == typeof(float)        ? TePropKind.Float
             : memberType == typeof(EntityHandle) ? TePropKind.Int
             : memberType == typeof(int)          ? TePropKind.Int
             : memberType == typeof(short)        ? TePropKind.Int
             : memberType == typeof(byte)         ? TePropKind.Int
             : memberType == typeof(bool)         ? TePropKind.Int
             : memberType.IsEnum                  ? TePropKind.Int
             : throw new NotSupportedException(
                   $"[TeProp] on {memberName}: no prop kind can be inferred from " +
                   $"{memberType.Name}. Say it — [TeProp(TePropKind.X)] — rather than " +
                   "letting a guess decide what goes on the wire.");

        return this;
    }

    /// <summary>
    /// The name on the wire, when it differs from the C# member's. Stock's own
    /// spelling wins every time: <c>DT_EffectData</c> sends <c>entindex</c>, not
    /// <c>m_nEntIndex</c>, and the client matches send props to recv props by
    /// name. This is not a place to tidy up naming (plan 2E).
    /// </summary>
    public string? WireName { get; init; }

    /// <summary>
    /// The literal width, when <see cref="BitsFrom"/> is
    /// <see cref="TeBits.Literal"/>. Defaults to -1, which is what stock passes
    /// for a float or vector whose encoding flag decides the width.
    /// </summary>
    public int Bits { get; init; } = -1;

    public TeBits  BitsFrom { get; init; } = TeBits.Literal;
    public TeFlags Flags    { get; init; }
    public float   Low      { get; init; }
    public float   High     { get; init; }

    /// <summary>DataTable only: the table nested here, by name.</summary>
    public string? SubTable { get; init; }

    /// <summary>
    /// How many props this one field becomes, and how far apart they sit.
    ///
    /// <para>
    /// 0 or 1 is one prop. N emits <c>name[0]..name[N-1]</c> at
    /// <c>offset + i * ElemStride</c>, which is what
    /// <c>SENDINFO_VECTORELEM</c> and <c>SENDINFO_ARRAYELEM</c> both expand to
    /// — a Vector as three floats, a QAngle as three angles, a colour as three
    /// bytes. <see cref="TeFlags.VectorElem"/> picks which of the two.
    /// </para>
    ///
    /// <para>
    /// For <see cref="TePropKind.Array"/> they mean the <c>DPT_Array</c>'s own
    /// element count and stride instead.
    /// </para>
    /// </summary>
    public int Elems      { get; init; }
    public int ElemStride { get; init; }
}

/// <summary>
/// Marks a type as one temp entity's declaration.
///
/// <para>
/// DISCOVERED BY THIS ATTRIBUTE and by nothing else. There is no shared list
/// file, because a shared list is forty-three lanes editing one line and a
/// guaranteed conflict — the same reason <c>[ScriptedEntity]</c> and
/// <c>[UserMessage]</c> are found this way.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class TempEntityAttribute : Attribute
{
    /// <summary>
    /// Both names are OPTIONAL and derived from the class when omitted:
    /// <c>TeSparks</c> is <c>CTESparks</c> on <c>DT_TESparks</c>. Forty of the
    /// forty-four declarations follow that exactly, so spelling them out was
    /// eighty strings restating the class name in two other alphabets.
    ///
    /// <para>
    /// THE FOUR THAT DO NOT STILL SAY SO, and they are not typos to be tidied —
    /// they are what stock declares, and the two sides pair on these strings
    /// (plan 2E):
    /// </para>
    /// <list type="bullet">
    /// <item><c>TeBaseTempEntity</c> is <c>CBaseTempEntity</c> on
    ///       <c>DT_BaseTempEntity</c> — no TE anywhere, because it is the base
    ///       every temp entity nests and not one itself.</item>
    /// <item><c>TeEffectData</c> is <c>CEffectData</c> on <c>DT_EffectData</c>,
    ///       a nested table rather than an effect.</item>
    /// <item><c>TeBaseBeam</c> is <c>CTEBaseBeam</c> on <c>DT_BaseBeam</c> —
    ///       the class keeps the TE and the table drops it.</item>
    /// <item><c>TeBspDecal</c> is <c>CTEBSPDecal</c>: stock capitalises the
    ///       acronym and C# does not.</item>
    /// </list>
    ///
    /// <para>
    /// A WRONG DERIVATION CANNOT PASS QUIETLY. The parity tests read stock's own
    /// <c>.cpp</c> off disk and assert both names, so a class whose real
    /// spelling this rule does not produce fails there rather than at a join.
    /// </para>
    /// </summary>
    public TempEntityAttribute(string? className = null, string? tableName = null)
    {
        DeclaredClassName = className;
        DeclaredTableName = tableName;
    }

    /// <summary>What the declaration SAID, or null to derive it from the class.</summary>
    public string? DeclaredClassName { get; }

    /// <summary>Likewise for the table.</summary>
    public string? DeclaredTableName { get; }

    /// <summary>
    /// What the two sides pair on — <c>"CTESparks"</c> — resolved from the
    /// declaration or from the class name.
    ///
    /// <para>
    /// STATIC AND TAKING THE TYPE, because an attribute cannot see what it is
    /// on. Every reader must come through here: one that reads
    /// <see cref="DeclaredClassName"/> directly gets null for a derived
    /// declaration, which is the shape of the bug three lanes found
    /// independently in <c>BaseTable</c> — a null read as "there isn't one"
    /// rather than as "ask the other rule".
    /// </para>
    /// </summary>
    public static string ClassNameOf(Type type)
        => type.GetCustomAttribute<TempEntityAttribute>()?.DeclaredClassName ?? "CTE" + StemOf(type);

    /// <summary>The table name, resolved the same way: <c>"DT_TESparks"</c>.</summary>
    public static string TableNameOf(Type type)
        => type.GetCustomAttribute<TempEntityAttribute>()?.DeclaredTableName ?? "DT_TE" + StemOf(type);

    /// <summary>
    /// <c>TeSparks</c> becomes <c>Sparks</c>. The <c>Te</c> prefix is the
    /// convention every declaration in the inventory follows, and a class
    /// without it is refused rather than silently given a name with a doubled
    /// prefix in it.
    /// </summary>
    private static string StemOf(Type type)
        => type.Name.StartsWith("Te", StringComparison.Ordinal) && type.Name.Length > 2
               ? type.Name[2..]
               : throw new InvalidOperationException(
                     $"[TempEntity] on {type.Name}: a declaration that does not begin with 'Te' " +
                     "cannot have its wire names derived. Name it TeXxx, or say both names in " +
                     "the attribute.");

    /// <summary>The table nested first, as BEGIN_SEND_TABLE puts it, or null for NOBASE.</summary>
    public string? BaseTable { get; init; }

    /// <summary>
    /// Whether this declaration claims a <c>ServerClass</c> of its own, or is
    /// only a table for others to nest.
    ///
    /// <para>
    /// FALSE IS THE TRANSITIONAL STATE AND IT IS NOT A HACK. While stock's own
    /// <c>CBaseTempEntity</c> and <c>CTEParticleSystem</c> are still registered
    /// — they must be, because thirty-eight native <c>te_*.cpp</c> derive from
    /// them — a managed class of the same name would put two ServerClasses of
    /// one name in the list the engine negotiates at connect, and the client
    /// would begin instantiating and stop with nothing in any log (plan 2E).
    /// The table is still needed, because a derived table has to nest one whose
    /// offsets address the blob C# packed. So: table yes, class no, until the
    /// native file is evicted.
    /// </para>
    /// </summary>
    public bool RegistersClass { get; init; } = true;
}

/// <summary>One field of a temp entity: the layout's answer joined to the attribute's.</summary>
public sealed record TeField(
    string  Name,        // the WIRE name
    TePropKind Kind,
    int     Offset,      // into the blob, from EntityLayout -- never declared by hand
    int     SizeBytes,   // storage width, which decides SendPropInt's read proxy
    int     Bits,
    TeBits  BitsFrom,
    TeFlags Flags,
    float   Low,
    float   High,
    string? SubTable,
    int     Elems,
    int     ElemStride,

    /// <summary>
    /// The C# member this came from — <c>Position</c>, not <c>m_vecPos</c>.
    ///
    /// <para>
    /// Kept BESIDE the wire name rather than instead of it, because they answer
    /// different questions and only one of them is stock's. The wire name is
    /// what the two sides pair on and may not be tidied (plan 2E); the member
    /// name is what a caller in C# should be able to say, so that setting a
    /// field is a compiler-checked reference and not a string that is right
    /// until somebody renames the property.
    /// </para>
    ///
    /// <para>
    /// Empty only for a field built without a declaring member, which the
    /// builder does not do — it is defaulted so that adding this to a positional
    /// record did not have to touch every construction site in one commit.
    /// </para>
    /// </summary>
    string  MemberName = "");

/// <summary>
/// One temp entity, as both sides see it: a name, a table, a base, a blob size
/// and a field list.
///
/// <para>
/// THE SAME OBJECT BUILDS A SendTable ON THE SERVER AND A RecvTable ON THE
/// CLIENT, from one declaration in the shared assembly. The schema physically
/// cannot drift between the two sides, which is the failure this system is most
/// exposed to.
/// </para>
/// </summary>
public sealed record TeClass(
    string    ClassName,
    string    TableName,
    string?   BaseTable,
    int       BlobBytes,
    bool      RegistersClass,
    Type      Declaring,
    TeField[] Fields)
{
    /// <summary>
    /// Where this class's OWN fields start in the blob, which is where its base
    /// chain ends.
    ///
    /// <para>
    /// A derived table nests its base and the engine's
    /// <c>SendProxy_DataTableToDataTable</c> shifts the object pointer by the
    /// datatable prop's offset — which stock declares as zero, so the base's
    /// props read at their own offsets from the SAME blob. So the base's fields
    /// have to occupy the front of the derived blob, and this is how far in the
    /// derived's own fields begin.
    /// </para>
    /// </summary>
    public int BaseBytes { get; init; }
}

/// <summary>Who hears a temp entity (plan 3.4).</summary>
public enum TeFilterKind
{
    Broadcast      = 0,
    Pvs            = 1,
    Pas            = 2,
    PasAttenuation = 3,
    SinglePlayer   = 4,
}

/// <summary>
/// The recipient filter, as a value.
///
/// <para>
/// Every call site in this build today is <see cref="TeFilterKind.Broadcast"/>
/// or <see cref="TeFilterKind.PasAttenuation"/>. The other three are here
/// because <c>ITempEntsSystem</c> takes an <c>IRecipientFilter</c> and their
/// absence would be discovered at the worst possible time.
/// </para>
/// </summary>
public readonly record struct TeFilter(
    TeFilterKind Kind,
    Vector       Origin,
    int          PlayerIndex,
    float        Attenuation,

    /// <summary>
    /// CARRIED AND NEVER CONSULTED (plan 2H). Prediction suppression is already
    /// dead in this build: <c>IPredictionSystem::m_pSuppressHost</c> is only
    /// ever written with an SDK <c>CBaseEntity</c>, none exists here, so
    /// <c>SuppressTE</c> always answers false on both sides. The bit is here so
    /// the shape survives the port.
    /// </summary>
    bool IgnorePredictionCull);

/// <summary>
/// The <see cref="TeFilter"/> each audience is, by name.
///
/// <para>
/// A class of its own rather than static members of the filter since
/// plan_shim_areas_split phase 4: the filter is in a seam's signature
/// (<c>IEngineEffects</c>), so it lives with the seam in an interface assembly,
/// which holds only data, and a factory has a body.
/// </para>
/// </summary>
public static class TeFilters
{
    /// <summary>Every client.</summary>
    public static readonly TeFilter Broadcast = new(TeFilterKind.Broadcast, Vector.Zero, 0, 0f, false);

    /// <summary>The clients whose PVS holds <paramref name="origin"/>.</summary>
    public static TeFilter Pvs(Vector origin)
        => new(TeFilterKind.Pvs, origin, 0, 0f, false);

    /// <summary>The clients whose PAS holds <paramref name="origin"/>.</summary>
    public static TeFilter Pas(Vector origin)
        => new(TeFilterKind.Pas, origin, 0, 0f, false);

    /// <summary>Attenuation defaults to ATTN_NORM, which native substitutes for a zero.</summary>
    public static TeFilter PasAttenuation(Vector origin, float attenuation = 0f)
        => new(TeFilterKind.PasAttenuation, origin, 0, attenuation, false);

    /// <summary>One client, by player index.</summary>
    public static TeFilter SinglePlayer(int playerIndex)
        => new(TeFilterKind.SinglePlayer, Vector.Zero, playerIndex, 0f, false);
}

/// <summary>
/// Turns a declaration into a <see cref="TeClass"/> by joining what
/// <see cref="EntityLayout"/> computed with what <see cref="TePropAttribute"/>
/// declared.
/// </summary>
public static class TeClassBuilder
{
    /// <summary>
    /// Builds one, given the base chain's total blob size.
    ///
    /// <para>
    /// <paramref name="baseBytes"/> is where this class's own fields start.
    /// A derived table nests its base at datatable offset 0, so the base's props
    /// read at their own offsets out of the SAME blob — which means the base's
    /// fields occupy the front of it and this class's begin after them. The
    /// registry resolves the chain and passes the answer in; nothing here or in
    /// any declaration writes an offset down.
    /// </para>
    ///
    /// <para>
    /// <c>BlobBytes</c> is <c>baseBytes + EntityLayout.Size</c>. Never a
    /// maintained constant: a field added to a declaration moves it, and a
    /// constant that did not move would leave the new field off the end of the
    /// blob and network whatever follows it.
    /// </para>
    /// </summary>
    public static TeClass Build(Type type, int baseBytes = 0)
    {
        var attr = type.GetCustomAttribute<TempEntityAttribute>()
            ?? throw new InvalidOperationException(
                $"{type.Name} has no [TempEntity]; it is not a temp entity declaration");

        // DECLARED ONLY. With declarations inheriting, the full chain is the
        // BLOB's shape but not this table's: a derived class's SendTable nests
        // "baseclass" and the base's props arrive through it. Listing them here
        // as well would put every inherited field on the wire twice.
        var layout = EntityLayout.Build(type, TempEntityAttribute.ClassNameOf(type), declaredOnly: true);
        var fields = new List<TeField>(layout.Fields.Count);

        int pad = NestedTablePad(type, layout, baseBytes);

        baseBytes += pad;

        foreach (var slot in layout.Fields)
        {
            var member = FindMember(type, slot.Name);

            var prop = member?.GetCustomAttribute<TePropAttribute>()
                ?? throw new InvalidOperationException(
                    $"[TempEntity({TempEntityAttribute.ClassNameOf(type)})] {type.Name}.{slot.Name} is an entity field " +
                    "with no [TeProp] on it: it would take a blob slot and send nothing, " +
                    "which shifts every field after it");

            // A parameterless [TeProp] takes its kind from the member's type.
            prop = prop.Resolve(slot.Type, $"{type.Name}.{slot.Name}");

            // ONE MEMBER, FOUR PROPS. There is no SendPropColor: stock sends a
            // colour as four separate eight-bit unsigned ints, and every table
            // in the inventory that carries one spells out r, g, b and a as
            // four fields. A Color32 member says the same thing once, and this
            // is where it becomes the four the wire actually has -- named for
            // the member so both sides derive the same spelling from the same
            // declaration, at the member's offset plus 0, 1, 2 and 3 -- four
            // BYTES, which is stock's color32 and what EntityData.SizeOf says a
            // Color32 costs in a blob. Each component is a byte-wide slot, so
            // the reader's unsigned accessor is the one that has to run: a
            // signed read of a one-byte field sign-extends, and 200 comes back
            // as -56.
            if (slot.Type == typeof(Color32))
            {
                string[] parts = ["R", "G", "B", "A"];

                for (int c = 0; c < parts.Length; c++)
                    fields.Add(new TeField(
                        Name:       (prop.WireName ?? slot.Name) + parts[c],
                        MemberName: slot.Name + parts[c],
                        Kind:       TePropKind.Int,
                        Offset:     baseBytes + slot.Offset + c,
                        SizeBytes:  1,
                        Bits:       prop.Bits > 0 ? prop.Bits : 8,
                        BitsFrom:   prop.BitsFrom,
                        Flags:      prop.Flags | TeFlags.Unsigned,
                        Low:        prop.Low,
                        High:       prop.High,
                        SubTable:   null,
                        Elems:      0,
                        ElemStride: 0));

                continue;
            }

            fields.Add(new TeField(
                Name:       prop.WireName ?? slot.Name,
                MemberName: slot.Name,
                Kind:       prop.Kind,
                Offset:     baseBytes + slot.Offset,
                SizeBytes:  SizeOfOneProp(prop, slot),
                Bits:       prop.Bits,
                BitsFrom:   prop.BitsFrom,
                Flags:      prop.Flags,
                Low:        prop.Low,
                High:       prop.High,
                SubTable:   prop.SubTable,
                Elems:      prop.Elems,
                ElemStride: prop.ElemStride));
        }

        return new TeClass(
            ClassName:      TempEntityAttribute.ClassNameOf(type),
            TableName:      TempEntityAttribute.TableNameOf(type),
            BaseTable:      attr.BaseTable ?? BaseTableOf(type),
            BlobBytes:      baseBytes + layout.Size,
            RegistersClass: attr.RegistersClass,
            Declaring:      type,
            Fields:         fields.ToArray())
        {
            BaseBytes = baseBytes,
        };
    }

    /// <summary>
    /// How many bytes of nothing must sit in front of a blob whose FIRST field
    /// is a nested datatable — and it is four, not zero, for one line of engine
    /// code.
    ///
    /// <para>
    /// <c>dt_send.cpp</c>, in <c>SendPropDataTable</c>:
    /// </para>
    /// <code>
    /// if ( varProxy == SendProxy_DataTableToDataTable &amp;&amp; offset == 0 )
    ///     ret.SetFlags( SPROP_COLLAPSIBLE );
    /// </code>
    /// <para>
    /// A COLLAPSIBLE datatable prop is folded into its parent's node: the engine
    /// stops treating it as a level of indirection, so the receiving side
    /// applies the nested table's props at the OBJECT BASE instead of through
    /// the datatable prop's own offset. For <c>DT_TEEffectDispatch</c> that
    /// wrote <c>CEffectData</c>'s first twelve bytes over
    /// <c>C_TEEffectDispatch</c>'s vtable pointer and segfaulted the client
    /// inside <c>engine-&gt;FireEvents()</c> — a NULL virtual call — the first
    /// time one was sent. The join succeeded, the table dump agreed field for
    /// field, and every managed test was green.
    /// </para>
    ///
    /// <para>
    /// STOCK CANNOT HIT IT AND THAT IS WHY IT IS NOT WRITTEN DOWN ANYWHERE.
    /// <c>SENDINFO_DT( m_EffectData )</c> is an <c>offsetof</c> inside a class
    /// with a vtable, so it is never zero. A C# blob has no vtable and its first
    /// field is at zero, which arms the flag by accident.
    /// </para>
    ///
    /// <para>
    /// The <c>"baseclass"</c> prop is deliberately NOT affected: it is declared
    /// at offset zero and MUST be collapsible, because a base's props address
    /// the same object on both sides. That is what stock's own
    /// <c>BEGIN_SEND_TABLE</c> emits, and it is why the other forty-three
    /// tables in this port never met this.
    /// </para>
    ///
    /// <para>
    /// Four bytes rather than one, so nothing after it loses its alignment. The
    /// pad is never written and never sent; it exists to make one comparison in
    /// the engine come out false.
    /// </para>
    /// </summary>
    private static int NestedTablePad(Type type, EntityLayout layout, int baseBytes)
    {
        foreach (var slot in layout.Fields)
        {
            if (baseBytes + slot.Offset != 0)
                continue;

            var prop = FindMember(type, slot.Name)?.GetCustomAttribute<TePropAttribute>();

            if (prop is { Kind: TePropKind.DataTable })
                return 4;
        }

        return 0;
    }

    /// <summary>
    /// The storage width of ONE of a field's props, which is not always the
    /// member's own size.
    ///
    /// <para>
    /// A Vector sent as three <c>SendPropFloat</c>s is twelve bytes of member
    /// and four bytes per prop, and <c>SendPropInt</c> picks its read proxy from
    /// exactly this number (<c>dt_send.cpp:492</c>) — a byte field described as
    /// four reads three bytes of whatever comes after it.
    /// </para>
    /// </summary>
    private static int SizeOfOneProp(TePropAttribute prop, EntityFieldLayout slot)
    {
        // An array's ONE ELEMENT, not the whole run, and not zero.
        //
        // Native expands an array field into the two props stock's
        // SendPropArray macro expands to (plan 3.3): an element template built
        // by BuildOneProp with nKind forced to VECTOR, then the DPT_Array prop
        // itself. The template goes through SendPropVector, which passes this
        // number as sizeofVar and asserts it equals sizeof(Vector)
        // (shell_tempents.cpp:265). Zero trips that assert on every debug
        // build the moment a spline is registered -- inert at -O2, which is
        // why it survived a release-only gate.
        //
        // The stride IS the element size here, and it is the same rule the
        // Elems > 1 branch below already applies to per-component props.
        if (prop.Kind == TePropKind.Array)
            return prop.ElemStride > 0 ? prop.ElemStride
                 : prop.Elems     > 0 ? slot.Size / prop.Elems
                 : slot.Size;

        if (prop.Elems > 1)
            return prop.ElemStride > 0 ? prop.ElemStride : slot.Size / prop.Elems;

        return slot.Size;
    }

    /// <summary>
    /// The table this declaration nests, taken from the C# class it derives
    /// from.
    ///
    /// <para>
    /// PUBLIC BECAUSE THE ATTRIBUTE IS NOT THE ANSWER ANY MORE. A converted
    /// declaration drops <c>BaseTable</c> and lets the C# base supply it, so
    /// <b>anything</b> that reads <c>attr.BaseTable</c> raw to decide where a
    /// blob starts now reads null and lays the derived fields out from offset
    /// zero — on top of the base's. <c>TempEntRegistry.RegisterAll</c> did
    /// exactly that, and it compiles, registers, sends and logs nothing. Every
    /// caller that wants a base table asks THIS, or asks the built
    /// <see cref="TeClass"/>, and never the attribute alone.
    /// </para>
    ///
    /// <para>
    /// The inheritance already says it — <c>TeSparks : TeParticleSystem</c> —
    /// so naming it again in the attribute was a second copy that could
    /// disagree with the first. A declaration whose base is not itself a
    /// <c>[TempEntity]</c> nests nothing, which is stock's NOBASE.
    /// </para>
    ///
    /// <para>
    /// PUBLIC BECAUSE <see cref="TempEntRegistry"/> HAS TO ASK THE SAME
    /// QUESTION, and asked the attribute directly until lane RA converted the
    /// first declarations to drop their <c>BaseTable</c>. Registration orders
    /// base before derived and takes the derived class's <c>BaseBytes</c> from
    /// the base it names; a null there is read as NOBASE, so seven beam tables
    /// registered with <c>BaseBytes = 0</c> and their own props landed on top
    /// of <c>DT_BaseBeam</c>'s fifteen — the same blob, plausible numbers,
    /// nothing in any log. One answer to "what does this nest", used by both.
    /// </para>
    /// </summary>
    public static string? BaseTableOf(Type type)
        => type.BaseType is { } b && b.GetCustomAttribute<TempEntityAttribute>() is not null
               // RESOLVED, NOT READ. A base whose names are derived declares
               // neither string, so reading DeclaredTableName here answers null
               // -- and null means NOBASE, which lays the derived blob out from
               // offset zero over its base's props. That is the bug three lanes
               // found independently in this exact expression, one attribute
               // change away from coming back.
               ? TempEntityAttribute.TableNameOf(b)
               : null;

    private static MemberInfo? FindMember(Type type, string name)
    {
        const BindingFlags Where = BindingFlags.Public | BindingFlags.Instance;

        return (MemberInfo?)type.GetField(name, Where) ?? type.GetProperty(name, Where);
    }
}
