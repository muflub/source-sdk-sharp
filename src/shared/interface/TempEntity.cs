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
    /// <c>CRecipientFilter::IgnorePredictionCull</c>: the server's
    /// <c>SuppressTE</c> (SourceSharp.Server's <c>PredictionSuppression</c>)
    /// leaves the predicting player in this filter rather than taking it out.
    /// </summary>
    bool IgnorePredictionCull);
