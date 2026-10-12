using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// What a field is on the wire.
///
/// Deliberately a small set. This describes what entities on the parallel
/// hierarchy actually send, not everything a SendProp can express — a wider
/// vocabulary is a wider surface for the two sides to disagree across, and the
/// point of this file is that they cannot.
/// </summary>
public enum NetFieldKind
{
    /// <summary>Three floats, unscaled. Positions and angles.</summary>
    Vector = 0,

    /// <summary>A signed integer. Its storage is its C# type's.</summary>
    Int,

    /// <summary>
    /// An unsigned integer. A separate kind rather than a flag, because it is
    /// the ENCODING that differs: a signed prop carrying a value above its
    /// signed range comes back negative, and nothing reports it.
    /// </summary>
    UInt,

    /// <summary>A fixed-length string. The length is the buffer, not the text.</summary>
    String,

    /// <summary>A single float, unscaled.</summary>
    Float,

    /// <summary>One bit.</summary>
    Bool,

    /// <summary>
    /// An RGBA colour in ONE WORD: four channels, eight bits each, R in the low
    /// byte.
    ///
    /// <para>
    /// WHY IT IS A KIND AND NOT FOUR BYTE FIELDS. Four byte fields is exactly
    /// what <c>spotlight_end</c> declared before this existed, and the packer
    /// did the right thing with them — one shared word, which is what a colour
    /// costs. What it could not do is make them ONE THING: four names, four
    /// range checks, four rows in the inspector, and four chances to write three
    /// of them and forget the fourth. Nothing in the type system said the four
    /// belonged together, and nothing checked that they stayed adjacent.
    /// </para>
    ///
    /// <para>
    /// STORAGE IS FOUR BYTES, WHICH IS NOT <c>sizeof(Color32)</c>. The C# type
    /// holds four <c>int</c>s and is sixteen bytes wide, because it is the type
    /// a call site passes to the HUD and an <c>int</c> is what a call site has.
    /// On the wire that would be four words to carry thirty-two bits, so this
    /// kind packs on the way in and unpacks on the way out — the one type here
    /// whose storage is not its C# type's, and the reason it needs its own
    /// accessors rather than <c>Read&lt;T&gt;</c>.
    /// </para>
    /// </summary>
    Color32,

    /// <summary>
    /// A facing: pitch, yaw and roll, three unscaled floats.
    ///
    /// <para>
    /// BIT-FOR-BIT A <see cref="Vector"/> ON THE WIRE, and that is stated rather
    /// than hidden: twelve bytes, three IEEE floats, no scaling, in declaration
    /// order. There is no encoding difference to find and inventing one would
    /// have been worse than having no kind at all.
    /// </para>
    ///
    /// <para>
    /// IT STILL EARNS ITS PLACE, for the reason <see cref="UInt"/> does. Three
    /// numbers that are a direction and three numbers that are a position look
    /// identical in a hex dump, in a .vmf and in a C# declaration —
    /// <c>pushdir</c>, <c>movedir</c> and <c>impulse_dir</c> are angles that get
    /// fed to <c>Angles.Forward</c>, and typing them <c>Vector</c> compiles,
    /// reads plausibly and is wrong about what the numbers mean. The
    /// <c>[ScriptVars]</c> work already drew this line for map keys; this is the
    /// same line for networked state.
    /// </para>
    ///
    /// <para>
    /// AND IT IS IN THE SCHEMA HASH, which is the part worth being deliberate
    /// about. The rule is that the hash covers what changes DECODING, and the
    /// bits are identical — so on a narrow reading it should not be. But a
    /// server that thinks a field is a position and a client that thinks it is a
    /// facing do not disagree about the bits, they disagree about the value, and
    /// the client will hand a position to <c>Angles.Vectors</c> and turn a
    /// trigger the wrong way with nothing in any log. Kinds are what the hash
    /// mixes, this is a kind, and it costs nothing to be refused instead.
    /// </para>
    /// </summary>
    QAngle,
}

/// <summary>
/// Marks a field as networked from the server to the client.
///
/// <para>
/// The declaring class is SHARED between the two sides — one type, in
/// SourceSharp.Shared, that the server fills in and the client reads. That is
/// the whole idea: a SendTable and its RecvTable are paired by name during the
/// connect handshake, and a prop one side has and the other does not is not a
/// missing value, it is a bitstream that stops making sense partway through.
/// The symptom is a client that begins joining and stops, with nothing in any
/// log.
/// </para>
///
/// <para>
/// Two hand-written lists have drifted twice in this project already. Declaring
/// the fields once, on a type both sides compile against, is better than a test
/// that catches the drift afterwards.
/// </para>
/// </summary>
// Properties as well as fields: a [StandaloneNetState] class declares partial
// PROPERTIES and the generator moves the attribute onto the backing field it
// writes, which is what NetTable.Describe below still reads.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public class NetFieldAttribute : Attribute
{
    /// <summary>
    /// The usual form: the field's C# TYPE says what it is on the wire, so
    /// there is nothing to state and nothing to state wrongly. A short is
    /// sixteen bits because it is a short.
    /// </summary>
    public NetFieldAttribute()
    {
    }

    /// <summary>
    /// For a field whose kind cannot be read off its type. Rare, and worth
    /// being deliberate about.
    /// </summary>
    public NetFieldAttribute(NetFieldKind kind)
    {
        Kind = kind;
    }

    /// <summary>Null when the kind is to be inferred from the C# type.</summary>
    public NetFieldKind? Kind { get; }

    /// <summary>
    /// How many BITS an integer takes on the wire, or the buffer length in
    /// bytes for a string. Ignored otherwise.
    ///
    /// Zero means "as wide as the type", which is what almost every field wants
    /// — a short is sixteen bits without being told. Stating fewer narrows the
    /// WIRE only: the storage stays the C# type's size, so a value that does not
    /// fit is truncated on the wire rather than misaligning everything after it.
    /// </summary>
    public int Bits { get; init; }

    /// <summary>
    /// The name the two tables are paired by, or null to use the field's own.
    ///
    /// Given explicitly only when the wire name must differ from the C# one —
    /// which is rare and worth being deliberate about, since this is the string
    /// the handshake matches on.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Which fields this one CHANGES WITH, as a name the author picks.
    ///
    /// <para>
    /// The delta unit is the dword, so the wire cost of an entity is the number
    /// of words at least one changed field sits in. Two fields that always move
    /// together should therefore share a word — they cost 39 bits between them
    /// instead of 78 — and a field that moves on its own should not be sharing a
    /// word with one that never does, because a word is charged to every field
    /// in it.
    /// </para>
    ///
    /// <para>
    /// THAT CANNOT BE INFERRED FROM TYPES. It is a fact about gameplay: which
    /// values a think function writes in the same tick. So it is declared, and
    /// the generator packs each group separately and never lets one group's
    /// field share a word with another's.
    /// </para>
    ///
    /// <para>
    /// Ungrouped fields form one default group, which is what almost every class
    /// wants. A WRONG OR MISSING GROUP IS A BANDWIDTH COST, NEVER A CORRECTNESS
    /// BUG — both sides read the same declaration, so both pack the same way,
    /// and the schema hash catches a client built before the hint was changed.
    /// That is the right shape for a tuning knob: it can be added later, tuned
    /// against <c>./debug-api nettraffic</c>, and never breaks anything.
    /// </para>
    /// </summary>
    public string? Group { get; init; }
}

/// <summary>
/// A networked string, and how long its buffer is.
///
/// <para>
/// Its own attribute because a string is the one field type whose C# type does
/// NOT say how big it is: an int is four bytes and a Vector is twelve, and a
/// string is however many the author decides to reserve. Writing that as a bit
/// count on the general attribute meant the one number that is a LENGTH shared a
/// name with the one number that is a WIDTH.
/// </para>
///
/// <para>
/// The length is the buffer, not the text. A longer value is truncated to fit
/// and always terminated; the far side reads to the terminator.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class NetStringAttribute : NetFieldAttribute
{
    public NetStringAttribute(int length = NetTable.DefaultStringLength)
        : base(NetFieldKind.String)
    {
        Bits = length;
    }
}

/// <summary>
/// One networked field, resolved.
///
/// Bits and Bytes are genuinely different numbers and both have to travel. Bytes
/// is what the field occupies in the blob and therefore what every offset after
/// it depends on; Bits is how much of it goes on the wire. They agree for almost
/// every field and must not be conflated for the ones where they do not.
/// </summary>
public readonly record struct NetField(string Name, NetFieldKind Kind, int Bits, int Bytes);

/// <summary>
/// One networked field with its place in the instance blob.
///
/// <para>
/// The native shell keeps a class's networked state in a single block of bytes
/// and a SendProp is an offset into it, so a field is not usable until it has
/// one. The offsets are computed HERE and handed to native rather than worked
/// out again on that side: two implementations of the same arithmetic is the
/// drift this whole design exists to prevent, and it would drift in the worst
/// possible way — silently, one field at a time, on the wire.
/// </para>
///
/// <para>
/// SINCE PACKING, A PLACE IS AN OFFSET AND A SHIFT. A field narrower than a word
/// shares one with its neighbours, so <see cref="Offset"/> is the byte offset of
/// the STORAGE the field sits in — the containing dword for a packed field, the
/// field itself for everything else — <see cref="Bytes"/> is how much storage
/// that is, and <see cref="BitShift"/> is how far into it the field's low bit
/// is. <see cref="Bits"/> is the width, which for a packed field is the width of
/// the storage as well as of the wire.
/// </para>
///
/// <para>
/// Offset and Bytes keep their old meaning for everything unpacked, which is
/// every vector, float, string and full-width integer — the shift is zero and
/// the two numbers describe the field exactly as they always did. That is what
/// lets the traffic view attribute a changed word to a field by the same
/// overlap test before and after packing.
/// </para>
/// </summary>
/// <param name="EnumType">
/// The enum the property was declared as, or null. NOT part of the wire — an
/// enum is sent as its underlying integer and native is told nothing about it
/// (plan_nettable_generic.md Step 11). It travels so that the schema hash can
/// cover the member NUMBERS, which is otherwise a renumbering nothing catches,
/// and so the inspector can decode through the member NAMES.
/// </param>
public readonly record struct NetFieldLayout(
    string Name, NetFieldKind Kind, int Bits, int Bytes, int Offset, int BitShift = 0,
    Type? EnumType = null)
{
    /// <summary>
    /// The C# type the author actually wrote, or empty when nobody said.
    ///
    /// <para>
    /// NOT ON THE WIRE, and not in the schema hash: <c>byte</c> and <c>uint</c>
    /// narrowed to eight bits decode identically, so a declaration that changes
    /// only this name changes nothing either side has to agree about. It is here
    /// for the one view whose whole job is to say what was declared.
    /// </para>
    ///
    /// <para>
    /// IT HAD TO BE CARRIED RATHER THAN INFERRED. The inspector used to guess
    /// the type back from the kind and the byte count, which worked exactly
    /// until packing: a packed <c>byte</c>'s Bytes is its containing WORD's, so
    /// the guess read four bytes of unsigned storage and answered <c>uint</c> —
    /// for spotlight_end's four colour channels, for every bool in a shared
    /// word, for everything the packer had done its job on. The declaration is
    /// known at generation time and nothing downstream can reconstruct it, so it
    /// travels.
    /// </para>
    /// </summary>
    public string CsType { get; init; } = "";

    /// <summary>
    /// Whether this field shares its storage, i.e. whether reading it needs a
    /// shift and a mask rather than a typed load.
    ///
    /// The test is on the WIDTH and not on the shift: the first field in a
    /// packed word sits at shift zero and is still packed, and reading it as a
    /// whole byte would pick up whatever moved in beside it.
    /// </summary>
    public bool IsPacked
        => Kind is NetFieldKind.Int or NetFieldKind.UInt or NetFieldKind.Bool
           && (BitShift != 0 || Bits < Bytes * 8);
}

/// <summary>
/// Shared constants for the declarations above.
///
/// <para>
/// WHAT USED TO BE HERE was the layout: Describe, Layout, SizeOf, the alignment
/// rule and the width rule, all worked out by reflection the first time a state
/// type was touched. That is done at COMPILE time now, by
/// SourceSharp.Generators, and the implementation went with it rather than
/// staying here as a second one — two implementations of this arithmetic
/// disagreeing is the failure this whole design exists to prevent, and a version
/// nobody calls is the easiest kind to let drift.
/// </para>
/// </summary>
public static class NetTable
{
    /// <summary>
    /// What a string's buffer is when it does not say. Here rather than only in
    /// the generator because <see cref="NetStringAttribute"/>'s default argument
    /// is the same number and they must be the same number.
    /// </summary>
    public const int DefaultStringLength = 64;
}
