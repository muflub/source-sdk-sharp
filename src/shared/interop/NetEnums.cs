using System.Globalization;
using System.Reflection;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>One named value of an enum-typed field, as the schema carries it.</summary>
public readonly record struct NetEnumMember(string Name, long Value);

/// <summary>
/// What a networked or map-bound enum IS, for the two things that need to know:
/// the schema hash, and anything printing a value a person will read.
///
/// <para>
/// AN ENUM IS ITS UNDERLYING INTEGER ON THE WIRE and nothing else — no new type
/// code, no new prop, nothing for native to learn (plan_nettable_generic.md
/// Step 11). What this file adds is the half that never reaches native: the
/// member names, so <c>PrecipType = 3</c> can be reported as
/// <c>PrecipType = Snowfall</c>, and the member VALUES, so a build that
/// renumbered them is refused rather than believed.
/// </para>
///
/// <para>
/// ONE ORDER FOR THE MEMBERS, and it is neither reflection's nor the source's.
/// The two shells reach an enum by different routes — <c>EntityLayout</c> has a
/// <see cref="Type"/> and reflects, a generated <c>NetFieldLayout</c> carries a
/// <c>typeof</c> — and the schema hash has to be the same function over the same
/// values whichever route it came by. <see cref="MembersOf"/> sorts by value and
/// then by name, so both routes produce one sequence and neither depends on the
/// order the runtime happened to hand back.
/// </para>
///
/// <para>
/// NOTHING HERE THROWS ON AN UNKNOWN VALUE. An integer on the wire can hold
/// anything, and a reader must not assume the value names a member — a throw on
/// the receiving end turns a cosmetic desync into a dropped client. An
/// unrecognised value formats as its number, said plainly, and is still a number
/// to every numeric view.
/// </para>
/// </summary>
public static class NetEnums
{
    /// <summary>
    /// The integer an enum is stored and sent as, or the type itself when it is
    /// not an enum.
    ///
    /// The whole of the layout change Step 11 asks for: <c>SizeOf</c>,
    /// <c>AlignmentOf</c> and the kind resolvers all go through this and are
    /// otherwise unchanged, so an enum backed by <c>int</c> lays out and sends
    /// exactly as an <c>int</c> does and one backed by <c>byte</c> costs a byte.
    /// </summary>
    public static Type Underlying(Type type)
        => type.IsEnum ? Enum.GetUnderlyingType(type) : type;

    /// <summary>Whether the enum's members are bits rather than alternatives.</summary>
    public static bool IsFlags(Type enumType)
        => enumType.IsEnum && enumType.GetCustomAttribute<FlagsAttribute>() is not null;

    /// <summary>
    /// Every member, by value and then by name. See the class remarks for why
    /// the order is fixed here rather than taken from either caller.
    ///
    /// <para>
    /// Empty for anything that is not an enum, so a caller can ask without
    /// testing first and a field with no enum behind it mixes nothing into the
    /// hash — which is what keeps every schema hash in the tree exactly what it
    /// was before enums existed.
    /// </para>
    /// </summary>
    public static IReadOnlyList<NetEnumMember> MembersOf(Type type)
    {
        if (!type.IsEnum)
            return Array.Empty<NetEnumMember>();

        string[] names = Enum.GetNames(type);
        var members = new List<NetEnumMember>(names.Length);

        foreach (string name in names)
            members.Add(new NetEnumMember(name, ValueOf(Enum.Parse(type, name))));

        members.Sort(static (a, b) =>
        {
            int byValue = a.Value.CompareTo(b.Value);
            return byValue != 0 ? byValue : string.CompareOrdinal(a.Name, b.Name);
        });

        return members;
    }

    /// <summary>
    /// The part of an enum the SCHEMA HASH covers: its distinct values, sorted.
    ///
    /// <para>
    /// THE RULE IS THAT THE HASH COVERS WHAT CHANGES DECODING AND NOTHING THAT
    /// DOES NOT. Three consequences, and each of them is a decision rather than
    /// a consequence of how this happened to be written:
    /// </para>
    ///
    /// <para>
    /// THE VALUES ARE IN, because they are the only thing here that a mismatch
    /// can change silently. A build where <c>Snowfall = 3</c> became
    /// <c>Snowfall = 4</c> has the same field name, the same type code, the same
    /// offset and the same width as the build sending it 3 — so a server sending
    /// 3 and a client reading 3 would disagree about what it MEANS with nothing
    /// anywhere to say so. That is exactly the class of omission Step 8 found in
    /// <c>Bits</c>, which had never been hashed at all, and it is why this wants
    /// writing down rather than inferring.
    /// </para>
    ///
    /// <para>
    /// THE NAMES ARE OUT, and this is the half that is easy to get wrong in the
    /// other direction. Renaming a member moves not one bit on the wire, so a
    /// hash that refused every client over a cosmetic refactor would be a guard
    /// people learn to route around — and a guard routed around is worse than no
    /// guard. The inspector decodes by member NAME while the hash ignores names
    /// entirely; that is deliberate and it is why the two are different lists.
    /// </para>
    ///
    /// <para>
    /// THE VALUES ARE A SORTED SET, AND THAT HAS A GAP. Swapping two members'
    /// values — <c>Rain = 1, Snow = 2</c> becoming <c>Rain = 2, Snow = 1</c> —
    /// leaves the set identical and slips through, though every decoded value
    /// now means the opposite. The alternative is hashing in declaration order,
    /// which catches the swap and then refuses a build that merely REORDERED two
    /// declarations whose explicit values never moved — the same disease as
    /// names, in a less obvious place. A swap is pathological and rare; renames
    /// and cosmetic reorders are routine. Stated here rather than papered over.
    /// </para>
    ///
    /// <para>
    /// DISTINCT, so an alias — <c>None = Generic</c>, which this tree writes —
    /// can be added or removed without moving anything. It names a value that is
    /// already covered, so it changes no decode.
    /// </para>
    ///
    /// <para>
    /// The UNDERLYING TYPE needs nothing here: it decides the field's width, and
    /// the width is hashed already as <c>Bytes</c> and the placement.
    /// </para>
    /// </summary>
    public static IReadOnlyList<long> HashValuesOf(Type type)
    {
        if (!type.IsEnum)
            return Array.Empty<long>();

        var values = new List<long>();

        foreach (var member in MembersOf(type))
        {
            // MembersOf is already sorted by value, so a duplicate is adjacent.
            if (values.Count == 0 || values[values.Count - 1] != member.Value)
                values.Add(member.Value);
        }

        return values;
    }

    /// <summary>
    /// An enum value as the long every reader here works in.
    ///
    /// Through <see cref="IConvertible"/> in both directions rather than
    /// <c>Convert.ToInt64</c> alone: a <c>ulong</c>-backed member with its top
    /// bit set overflows that call, and an enum whose author chose an unsigned
    /// backing is exactly the kind carrying a bitmask.
    /// </summary>
    public static long ValueOf(object enumValue)
    {
        var underlying = Type.GetTypeCode(Enum.GetUnderlyingType(enumValue.GetType()));

        return underlying == TypeCode.UInt64
            ? unchecked((long)((IConvertible)enumValue).ToUInt64(CultureInfo.InvariantCulture))
            : ((IConvertible)enumValue).ToInt64(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A value as the author would recognise it: a member's name, a
    /// <c>|</c>-joined set of them for a flags enum, or the number.
    ///
    /// <para>
    /// AN UNNAMED VALUE SAYS SO, which is the decision Step 11 leaves open. It
    /// could format as the bare number and read like a member nobody had heard
    /// of, or throw and take a client down over a cosmetic disagreement. It does
    /// neither: it prints the number and marks it, so a reader can tell "the
    /// wire said 7 and this build has no 7" from "the wire said 7 and 7 is
    /// Snowfall". A flags value that is partly named prints the part that is —
    /// <c>Bullet|0x100000 (unnamed)</c> — because dropping the recognised bits
    /// would hide more than it tidied.
    /// </para>
    /// </summary>
    public static string Format(Type enumType, long value)
    {
        var members = MembersOf(enumType);

        foreach (var member in members)
        {
            if (member.Value == value)
                return member.Name;
        }

        if (!IsFlags(enumType))
            return Unnamed(value);

        var named = new StringBuilder();

        // MASKED TO THE BACKING TYPE'S WIDTH, or the leftover bits of a negative
        // are the sign extension rather than anything a map wrote: an int-backed
        // -1 would report a remainder of 0xFFFFFFFFDEF9BB10, of which the top
        // half is not a bit that exists.
        long left = value & WidthMask(enumType);

        // Widest first, so a member that is two bits is credited before either
        // of the single bits it contains. Otherwise a value naming a compound
        // member would print as its parts and read as a different thing.
        foreach (var member in Ordered(members))
        {
            if (member.Value == 0 || (left & member.Value) != member.Value)
                continue;

            if (named.Length > 0)
                named.Append('|');

            named.Append(member.Name);
            left &= ~member.Value;
        }

        if (named.Length == 0)
            return Unnamed(value);

        if (left != 0)
            named.Append('|').Append(Unnamed(left));

        return named.ToString();
    }

    /// <summary>Every bit the enum's backing type actually has.</summary>
    private static long WidthMask(Type enumType)
    {
        int bytes = Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte or TypeCode.Byte     => 1,
            TypeCode.Int16 or TypeCode.UInt16   => 2,
            TypeCode.Int32 or TypeCode.UInt32   => 4,
            _                                    => 8,
        };

        return bytes >= 8 ? -1L : (1L << (bytes * 8)) - 1L;
    }

    /// <summary>Flags members by how many bits each claims, widest first.</summary>
    private static IEnumerable<NetEnumMember> Ordered(IReadOnlyList<NetEnumMember> members)
    {
        var byWidth = new List<NetEnumMember>(members);

        byWidth.Sort(static (a, b) =>
        {
            int bits = System.Numerics.BitOperations.PopCount((ulong)b.Value)
                       .CompareTo(System.Numerics.BitOperations.PopCount((ulong)a.Value));

            return bits != 0 ? bits : string.CompareOrdinal(a.Name, b.Name);
        });

        return byWidth;
    }

    /// <summary>
    /// A value no member names. Hex for anything with more than one bit set,
    /// because an unnamed flags remainder is read against a bit list.
    /// </summary>
    private static string Unnamed(long value)
        => value is > 9 or < 0
            ? $"0x{value:X} (unnamed)"
            : $"{value.ToString(CultureInfo.InvariantCulture)} (unnamed)";
}
