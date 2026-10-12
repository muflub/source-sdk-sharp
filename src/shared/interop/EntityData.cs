using System.Runtime.CompilerServices;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a property as C#-declared entity state, stored in the entity's native
/// data blob rather than in a managed object.
///
/// This is the storage model from plan.md Part 4: the data lives in native
/// memory the engine can read directly, so it can later be networked by a
/// runtime-generated SendTable with zero interop on the send path.
/// </summary>
/// <remarks>
/// NOT SEALED, and the one derived attribute is the reason (plan_tmp.md Part
/// 3.1). A temp entity's field needs a blob slot exactly as an entity's does —
/// same offsets, same declaration order, same <see cref="EntityLayout"/> — and
/// it needs an ENCODING beside it, which entity state has no use for.
/// <see cref="TePropAttribute"/> is therefore an entity field that also says
/// how it goes on the wire, and <c>EntityLayout.Build</c> finds it without
/// knowing temp entities exist. Two attributes on every one of four hundred
/// fields was the alternative, and the second one would have been the one
/// somebody forgot.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public class EntityFieldAttribute : Attribute
{
    // No ordering knob: the layout follows the order the properties are declared
    // in source. See EntityLayout.Build for how that is made deterministic, and
    // why reordering the declarations is a wire-format change.
}

/// <summary>One field's position and width inside an entity blob.</summary>
public readonly struct EntityFieldLayout
{
    public EntityFieldLayout(string name, Type type, int offset, int size)
    {
        Name = name; Type = type; Offset = offset; Size = size;
    }

    public string Name { get; }
    public Type Type { get; }
    public int Offset { get; }
    public int Size { get; }

    /// <summary>
    /// How wide the field is on the wire, which is not always its storage: a
    /// bool costs one bit whatever it is stored in, and a vector's width is PER
    /// COMPONENT.
    ///
    /// <para>
    /// ONE COPY OF THAT RULE, here. It was written out in
    /// <c>NetSchema.FromLayout</c> and needed a second reader when
    /// <see cref="EntityLayout.Fingerprint"/> started covering field widths, so
    /// it moved to the layout that knows the answer instead of being typed
    /// twice. Nothing on this shell packs — it has no generator and no Group
    /// hint — so the shift beside it is always zero.
    /// </para>
    /// </summary>
    public int Bits => Type == typeof(bool)   ? 1
                     : Type == typeof(Vector) ? 32
                     : Size * 8;

    public override string ToString() => $"{Name} @{Offset} ({Size}b)";
}

/// <summary>
/// Computes a blob layout from C# metadata.
///
/// Determinism matters more than compactness here: client and server generate
/// this independently, so identical metadata must produce byte-identical
/// offsets. Fields are therefore laid out in a defined order, not in whatever
/// order reflection happens to return, and each is naturally aligned.
/// </summary>
public sealed class EntityLayout
{
    private readonly Dictionary<string, EntityFieldLayout> _byName;

    private EntityLayout(IReadOnlyList<EntityFieldLayout> fields, int size, string className)
    {
        Fields = fields;
        Size = size;
        ClassName = className;
        _byName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<EntityFieldLayout> Fields { get; }

    /// <summary>
    /// Which entity class this layout describes.
    ///
    /// Native keys the table, the ServerClass and the ClientClass it builds by
    /// this, so a second class describing its own fields gets its own table
    /// rather than replacing the first one's.
    ///
    /// It is the ENTITY classname — the name the class is spawned by — and not
    /// the declaring type's, because that is the one name the entity itself can
    /// produce at runtime. A server entity answers GetServerClass by looking
    /// itself up under GetClassname(), so keying by anything else would mean
    /// native carrying a second copy of a string C# owns, which is the failure
    /// this whole thread is about. Both sides still take it from the same
    /// declaration in shared code, so they cannot spell it differently.
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// A fingerprint of this layout's exact shape.
    ///
    /// Covers every field's name, type, offset and size, in order — i.e. exactly
    /// the information both sides encode into their generated datatables. Two
    /// layouts with the same fingerprint produce compatible tables; two that
    /// differ produce tables that will silently misread each other's bytes.
    ///
    /// FNV-1a, chosen because it is short, dependency-free, and identical on both
    /// sides by construction. This is a divergence check, not a security one.
    /// </summary>
    public uint Fingerprint
    {
        get
        {
            const uint offsetBasis = 2166136261u;
            const uint prime       = 16777619u;

            uint hash = offsetBasis;

            void Mix(uint value)
            {
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= prime;
                }
            }

            void MixText(string text)
            {
                foreach (char c in text)
                {
                    hash ^= (byte)c;
                    hash *= prime;
                }
                hash ^= 0xFF;   // terminator, so "ab"+"c" and "a"+"bc" differ
                hash *= prime;
            }

            foreach (var f in Fields)
            {
                MixText(f.Name);
                Mix((uint)NetworkedLayout.TypeCodeOf(f.Type));
                Mix((uint)f.Offset);
                Mix((uint)f.Size);

                // Where the field sits inside its storage and how wide it is.
                // Nothing on this shell packs, so the shift is always zero — but
                // the value is mixed all the same, because this function and
                // NetSchema.SchemaHashOf have to stay the SAME function over the
                // same values, and a schema hash that disagreed with the
                // fingerprint already on the wire would be a second opinion
                // about whether two builds match.
                Mix(NetSchema.PlacementOf(0, f.Bits));

                // AN ENUM'S VALUES, and only its values (Step 11). Renumbering a
                // member changes nothing else mixed above — same name, same type
                // code, same offset, same width — and is a disagreement about
                // MEANING with no other symptom. Renaming one changes no bit on
                // the wire and is deliberately not covered. The rule and the one
                // thing it does not catch are written out in
                // NetEnums.HashValuesOf; nothing is mixed at all for a field
                // that is not an enum, so no hash already published moves.
                foreach (long value in NetEnums.HashValuesOf(f.Type))
                {
                    Mix(unchecked((uint)value));
                    Mix(unchecked((uint)(value >> 32)));
                }
            }

            Mix((uint)Size);
            return hash;
        }
    }

    /// <summary>Total blob bytes required, including alignment padding.</summary>
    public int Size { get; }

    public EntityFieldLayout this[string name] => _byName[name];
    public bool TryGet(string name, out EntityFieldLayout field) => _byName.TryGetValue(name, out field);

    /// <summary>
    /// Builds the layout for a type. Throws on an unsupported field type rather
    /// than silently skipping it — a missing field would desync client and
    /// server in a way that is very hard to trace back here.
    /// </summary>
    /// <param name="className">
    /// The entity classname this layout belongs to, or null to use the type's
    /// own name. Given explicitly when the C# type describing the state is not
    /// itself the entity — a state struct is not an entity class, and native
    /// keys by the name the entity is spawned under.
    /// </param>
    public static EntityLayout Build(Type type, string? className = null)
        => Build(type, className, declaredOnly: false);

    /// <summary>
    /// As <see cref="Build(Type, string?)"/>, but optionally only the members
    /// this type declares itself.
    ///
    /// <para>
    /// A temp entity needs BOTH readings and they are not the same. The BLOB is
    /// the whole object — a derived class's bytes start with its base's — so
    /// authoring wants the full chain. The derived class's own SendTable must
    /// carry only its OWN props, because the base's arrive through the nested
    /// <c>baseclass</c> table; listing them twice would put every base field on
    /// the wire twice and shift everything after it.
    /// </para>
    /// </summary>
    public static EntityLayout Build(Type type, string? className, bool declaredOnly)
    {
        // Fields and properties both count. A blittable struct of fields is the
        // form that can be read in one MemoryMarshal.Read, so it is the one that
        // matters most; properties are still supported for classes that only
        // ever go through the per-field accessors.
        var members = new List<(string Name, Type Type, int Token)>();

        // BASE FIRST, EXPLICITLY, AND THIS IS LOAD-BEARING.
        //
        // A metadata token is assigned in declaration order WITHIN a type, and
        // that is all it promises. Across types it is assignment order in the
        // compiled image, so a base declared in a later file can carry HIGHER
        // tokens than the class deriving from it — and sorting the whole
        // inherited set by token would then put derived fields before base ones.
        // Declaration order is wire order, so that is a desync with no symptom
        // until two machines disagree about where a field starts.
        //
        // So the chain is walked root-first and each level is sorted only
        // against itself. For a struct, or a class deriving from nothing, this
        // is exactly the single-level sort it replaces.
        var levels = new List<Type>();

        if (declaredOnly)
        {
            levels.Add(type);
        }
        else
        {
            for (Type? t = type;
                 t is not null && t != typeof(object) && t != typeof(ValueType);
                 t = t.BaseType)
                levels.Add(t);

            levels.Reverse();
        }

        foreach (Type level in levels)
        {
            var atThisLevel = new List<(string Name, Type Type, int Token)>();

            const BindingFlags Here = BindingFlags.Public | BindingFlags.Instance
                                    | BindingFlags.DeclaredOnly;

            foreach (var f in level.GetFields(Here))
            {
                if (f.GetCustomAttribute<EntityFieldAttribute>() is not null)
                    atThisLevel.Add((f.Name, f.FieldType, f.MetadataToken));
            }

            foreach (var p in level.GetProperties(Here))
            {
                if (p.GetCustomAttribute<EntityFieldAttribute>() is not null)
                    atThisLevel.Add((p.Name, p.PropertyType, p.MetadataToken));
            }

            atThisLevel.Sort((a, b) => a.Token.CompareTo(b.Token));
            members.AddRange(atThisLevel);
        }

        // Ordered by metadata token, which is source declaration order.
        // GetFields/GetProperties make no ordering guarantee and both sides must
        // agree byte for byte, so leaving this to reflection would be a latent
        // desync. The token is assigned by the compiler in declaration order and
        // fixed in the compiled image, and both sides load the same assembly — so
        // both derive the same layout from the same bytes. Reordering the members
        // changes the layout, which is the intended and only way to change it.

        var fields = new List<EntityFieldLayout>(members.Count);
        int offset = 0;

        foreach (var (name, memberType, _) in members)
        {
            int size = SizeOf(memberType)
                ?? throw new NotSupportedException(
                    $"[EntityField] on {type.Name}.{name}: unsupported type {memberType.Name}");

            // Align to the member's ELEMENT alignment, not its total size. A
            // Vector is 12 bytes but is three 4-byte floats, so it aligns to 4;
            // rounding to a multiple of 12 is not an alignment at all and just
            // leaves a hole.
            offset = Align(offset, AlignmentOf(memberType));
            fields.Add(new EntityFieldLayout(name, memberType, offset, size));
            offset += size;
        }

        return new EntityLayout(fields, Align(offset, 4), className ?? type.Name);
    }

    private static int Align(int value, int alignment)
        => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;

    /// <summary>
    /// Alignment requirement of a field, which is its element alignment rather
    /// than its size — the two only differ for composite types like Vector.
    /// </summary>
    private static int AlignmentOf(Type t)
        => t == typeof(Vector) || IsNestedBlob(t) ? sizeof(float)
         : ElementOfInlineArray(t) is { } element ? AlignmentOf(element)
         : (SizeOf(t) ?? 1);

    /// <summary>
    /// The element type of an <c>[InlineArray]</c> struct, or null for anything
    /// else.
    ///
    /// <para>
    /// An inline array has exactly one instance field by definition — the
    /// runtime lays out <c>Length</c> copies of it — so the single field IS the
    /// element. Written as a check rather than an assumption because a type
    /// carrying the attribute and two fields does not compile, and a reader
    /// should not have to know that to trust the <c>Single()</c>.
    /// </para>
    /// </summary>
    private static Type? ElementOfInlineArray(Type t)
    {
        if (t.GetCustomAttribute<InlineArrayAttribute>() is null)
            return null;

        var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        return fields.Length == 1 ? fields[0].FieldType : null;
    }

    /// <summary>
    /// Whether a field's type is itself a blob — a struct whose own members
    /// carry <see cref="EntityFieldAttribute"/>.
    ///
    /// <para>
    /// PURELY ADDITIVE: before this, such a field threw
    /// <c>NotSupportedException</c> from <see cref="SizeOf"/>, so nothing that
    /// laid out before lays out differently now. It exists for the one shape in
    /// the temp entity inventory that needs it —
    /// <c>DT_TEEffectDispatch</c>'s <c>m_EffectData</c>, a
    /// <c>SendPropDataTable</c> nested as a FIELD rather than as a base — where
    /// the nested table's props read at their own offsets out of the outer blob
    /// shifted by this field's offset. The outer blob therefore has to contain
    /// the inner one, and its size is the inner layout's size rather than a
    /// number anybody types.
    /// </para>
    ///
    /// <para>
    /// It says nothing about temp entities, deliberately: a composite field is
    /// a composite field, and <see cref="TePropAttribute"/> being an
    /// <see cref="EntityFieldAttribute"/> is what makes this reach it.
    /// </para>
    /// </summary>
    private static bool IsNestedBlob(Type type)
    {
        if (!type.IsValueType || type.IsPrimitive || type.IsEnum || type == typeof(Vector))
            return false;

        const BindingFlags Where = BindingFlags.Public | BindingFlags.Instance;

        return type.GetFields(Where).Any(f => f.GetCustomAttribute<EntityFieldAttribute>() is not null)
            || type.GetProperties(Where).Any(p => p.GetCustomAttribute<EntityFieldAttribute>() is not null);
    }

    /// <summary>
    /// How many bytes a field occupies, or null for a type this cannot lay out.
    ///
    /// <para>
    /// AN ENUM IS ITS UNDERLYING INTEGER (plan_nettable_generic.md Step 11), and
    /// that is the whole of the change: one resolution at the top, and every
    /// rule below is the rule for the integer the author chose. So an enum
    /// backed by <c>int</c> lays out and sends exactly as an <c>int</c> does —
    /// no new wire format, no new type code, nothing for native to learn — and
    /// one backed by <c>byte</c> costs a byte, which is the point of letting the
    /// author choose.
    /// </para>
    /// </summary>
    private static int? SizeOf(Type type)
    {
        Type t = NetEnums.Underlying(type);

        if (t == typeof(bool)) return 1;
        if (t == typeof(byte) || t == typeof(sbyte)) return 1;
        if (t == typeof(short) || t == typeof(ushort)) return 2;
        if (t == typeof(int) || t == typeof(uint)) return 4;
        if (t == typeof(float)) return 4;
        if (t == typeof(long) || t == typeof(ulong)) return 8;
        if (t == typeof(double)) return 8;
        if (t == typeof(Vector)) return 12;

        // THREE FLOATS LIKE A VECTOR, and deliberately NOT a Vector. QAngle is
        // the type a facing has, and the engine encodes one as SendPropQAngles
        // rather than three SendPropFloats -- so letting a declaration say
        // QAngle is what makes the kind inferable at all. Same twelve bytes.
        if (t == typeof(QAngle)) return 12;

        // FOUR BYTES, which is stock's color32 and NOT the sixteen a CLR
        // Color32 occupies -- it holds four ints because it is a general
        // purpose colour, used by the HUD and by map keyvalues, and widening
        // those to please the wire would be the tail wagging the dog.
        //
        // THE DIVERGENCE IS SAFE HERE AND WOULD NOT BE EVERYWHERE. A temp
        // entity blob is written and read FIELD BY FIELD through reflection
        // (TempEntDeclarationWriter/Reader), so what a layout says about a
        // member's width is what the blob does, full stop. TryRead<T> below is
        // the other kind of consumer: it memcpys a blob straight onto a struct,
        // and for that the generated layout has to agree with what .NET lays
        // out. Nothing puts a Color32 in one of those, and this comment is here
        // so that the day something wants to, the four-versus-sixteen is found
        // rather than discovered.
        //
        // The members this replaces were four separate ints -- sixteen bytes --
        // so this is twelve bytes back out of the engine's 192-byte event cap, not a cost.
        if (t == typeof(Color32)) return 4;

        // A FIXED-LENGTH RUN OF ONE ELEMENT, which is what CNetworkArray is on
        // the C++ side: N copies of the element, contiguous, in the object
        // itself. C#'s [InlineArray] is the same storage, and it is storage that
        // is wanted here rather than a T[] — an array member is a REFERENCE, so
        // laying it out would put a pointer where the engine expects the
        // elements. DT_TEBeamSpline's sixteen COORD vectors are the only one in
        // the whole temp entity inventory, and its DPT_Array prop addresses them
        // at offset + i * ElementStride out of exactly this run (plan 3.3).
        if (ElementOfInlineArray(t) is { } element)
        {
            int length = t.GetCustomAttribute<InlineArrayAttribute>()!.Length;

            return length > 0 && SizeOf(element) is { } one ? length * one : null;
        }

        // A NESTED BLOB is its own layout's size. See IsNestedBlob for why this
        // arm exists and why adding it cannot move any layout that already
        // worked: every type it accepts used to land on the `null` below.
        if (IsNestedBlob(t)) return Build(t).Size;

        return null;
    }
}

/// <summary>
/// Publishes the runtime-networked entity's layout to native.
///
/// <para>
/// WHAT IT PUBLISHES IS NO LONGER THE LAYOUT (plan_nettable_generic.md Step 7).
/// This used to hand native every field — name, type code, blob offset, wire
/// bits, storage bytes — and native built a SendTable of typed props from the
/// list. Step 6 replaced that table with a size TIER carrying N flat dword props
/// over the blob, and Step 7 deleted the description: native is told the type's
/// NAME, the fingerprint of its layout and its size, and what the bits inside
/// the payload mean is entirely this side's business.
/// </para>
///
/// <para>
/// The generator still has to be deterministic, and more strictly than before:
/// client and server compute the offsets independently and nothing native
/// arbitrates between them any more. What checks it is the schema hash, which
/// travels with every update and is refused rather than decoded on a
/// disagreement.
/// </para>
/// </summary>
public static class NetworkedLayout
{
    /// <summary>
    /// A field's kind as a small integer.
    ///
    /// <para>
    /// IT USED TO BE THE ABI — <c>SourceSharpFieldType</c> in native, whose
    /// values these are. Native no longer has an opinion about field types, so
    /// what these numbers decide now is the SCHEMA HASH: they are mixed into the
    /// fingerprint both sides compute over their own copy of the declarations,
    /// so renumbering them is a wire change on whichever side is rebuilt, in
    /// exactly the way it always was.
    /// </para>
    /// </summary>
    public static int TypeCodeOf(Type type)
        // ONE FOLD, NOT TWO. This used to have its own — float 1, Vector 2, bool
        // 3, everything integral 0 — beside NetSchema's, with a comment on each
        // saying they agreed. They did not, for unsigned: NetFieldKind.UInt is
        // code 5 and this answered 0, so an [EntityField] byte would have hashed
        // differently in EntityLayout.Fingerprint and NetSchema.SchemaHashOf.
        // Nothing had ever declared one, so nothing had ever noticed. Deferring
        // makes the agreement structural rather than a claim, and an enum
        // resolves to its underlying integer inside NetSchema.KindOf as it does
        // everywhere else.
        => NetSchema.WireTypeCodeOf(NetSchema.KindOf(type));

    // Publish went with the runtime-networked shell it published onto.
    // Everything publishes through StandaloneNetStates.PublishOne now, which
    // already carries the type-id parity check and the refusal record.
    //
    // TypeCodeOf above stays: it is what EntityLayout.Fingerprint mixes.
}

// THE OLDER LAYOUT MODEL'S ACCESSORS ARE GONE -- plan_entity.md Phase 8, step 8a.
//
// `static class EntityData` held four methods: two span reads, one read and one
// write that reached an entity BY HANDLE through Engine.GetEntityBlob and poked
// its blob at an EntityFieldLayout offset. That was the storage model of
// plan.md Part 4, before the generator emitted blob-backed properties with
// compile-time offsets (NetState.cs, NetStateGenerator.cs, NetBlobView).
//
// THE DIFFERENTIAL, not the count. All four bodies were made to throw
// `InvalidOperationException` and the whole managed suite was run:
//
//     Failed: 5, Passed: 4871, Skipped: 3, Total: 4879
//     CoreTests.EntityDataFailsClosed          names EntityData directly
//     CoreTests.BlittableLayoutTests           names EntityData directly
//     NetSchemaTests.Description / .Identity   NetSchema.FromLayout
//     NetEnumTests.RenumberingMovesTheSchemaHash        NetSchema.FromLayout
//
// Nothing else in 4,871 passing facts reached it, and there was never a
// production caller: `grep -rn 'EntityData\.'` over the managed tree outside
// SourceSharp.Tests found one doc-comment cross-reference in Engine.cs and no
// call. The write path also took Engine.MarkEntityStateChanged's ONLY
// production caller with it -- recorded in requests/8a.md rather than removed,
// because that is a general engine-area member and a separate decision.
//
// WHAT STAYS IN THIS FILE, and why it is not the older model: EntityLayout,
// EntityFieldLayout, EntityFieldAttribute and NetworkedLayout are pure layout
// arithmetic with no entity in their interface. TempEntity.cs builds every temp
// entity's prop layout with EntityLayout.Build (TePropAttribute derives from
// EntityFieldAttribute), and EntityLayoutRegistry hashes them into the
// fingerprint exchanged at every join. Deleting them would be deleting the temp
// entity wire format, which is not what this step is.
