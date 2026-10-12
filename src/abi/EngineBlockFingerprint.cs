using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A fingerprint of the ENGINE BLOCK's layout — the struct every shell getter
/// and the network decoder read at a fixed offset (plan_entity.md §3.2). It is
/// declared once, in C#; the C++ <c>EngineBlock_t</c> is generated from that
/// declaration.
///
/// <para>
/// Two declarations of one layout was the port's named drift risk (§7, "Engine
/// block drift"), and generating the native struct removed the second
/// declaration rather than defending it. This answers the half that generating
/// cannot: offset facts on each side catch a field that moved WITHIN a side, but
/// they cannot catch a client and a server BUILT FROM DIFFERENT SOURCES, because
/// each build is self-consistent and each generated its own header. That is what
/// this is for — the value is computed FROM THE C# DECLARATION, exchanged at
/// join beside the entity layout fingerprint, and a disagreement refuses the
/// check.
/// </para>
///
/// <para>
/// PHASE 0f CARRIED AN EMPTY BLOCK, and that was the point of it: the hash was
/// on the wire, compared, and refusing a mismatch before there was anything in
/// the block to get wrong. A slot that arrives together with its first real
/// payload is a slot whose refusal path has never run. Phase 1a repointed
/// <see cref="BlockType"/> at <see cref="EngineBlock"/>, and every field that
/// block declares — at any depth — moves this value.
/// </para>
/// </summary>
public static class EngineBlockFingerprint
{
    /// <summary>
    /// The declaration the fingerprint covers.
    ///
    /// <para>
    /// PHASE 1a REPOINTED THIS. Phase 0f carried a fieldless <c>EmptyBlock</c>
    /// so that the wire slot, the comparison and the refusal all existed and had
    /// been exercised before there was anything in the block to get wrong. The
    /// block now exists (<see cref="EngineBlock"/>) and this names it; nothing
    /// else in the handshake changed when it did, which was the point of landing
    /// the slot first.
    /// </para>
    /// </summary>
    public static Type BlockType { get; } = typeof(EngineBlock);

    /// <summary>
    /// The value this build puts on the wire. Computed once — it cannot change
    /// after load, and it is read at every join.
    /// </summary>
    public static uint Current { get; } = Of(BlockType);

    /// <summary>
    /// FNV-1a over the block's shape: its field count, its marshalled size, and
    /// then each field's name, type, offset and size in declaration order —
    /// recursively, for a field that is itself a struct.
    ///
    /// <para>
    /// The same function and the same mixing order as
    /// <see cref="EntityLayout.Fingerprint"/>, for the reason that one gives:
    /// two opinions about whether two builds match is worse than none. Adding,
    /// reordering, resizing or renaming a field each move the result, which is
    /// exactly the set of edits Phase 1a's mutation gate names.
    /// </para>
    /// </summary>
    public static uint Of(Type block)
    {
        ArgumentNullException.ThrowIfNull(block);

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

        MixStruct(block);

        return hash;

        // A struct's whole shape, and RECURSIVELY the shape of any struct it
        // holds.
        //
        // WHY THE RECURSION IS NOT OPTIONAL. The real block is a shared head
        // and two tails, declared as separate structs (§3.2), so its own field
        // list is three names. Reordering Origin and Angles INSIDE the head —
        // the drift this hash exists to refuse, and the one a person would
        // actually commit — leaves those three names, their three offsets and
        // their three sizes all unchanged. A walk that stopped at the top level
        // would return the same number for a block that had been reordered
        // under it, which is a check that cannot fail on the case it was
        // written for.
        //
        // It costs nothing on a flat declaration: a struct of primitives mixes
        // exactly what it mixed before, so the values Phase 0f's tests pinned
        // are unmoved.
        void MixStruct(Type type)
        {
            // Declared fields in metadata order. Sequential layout means
            // metadata order IS memory order, so this walk and the C++ struct's
            // field list describe the same thing in the same sequence.
            var fields = type.GetFields(System.Reflection.BindingFlags.Public |
                                        System.Reflection.BindingFlags.NonPublic |
                                        System.Reflection.BindingFlags.Instance);

            // Count and total size FIRST, so an empty block is still a specific
            // number rather than the bare offset basis — and so a field added at
            // the very end, whose own mixing would otherwise be all that moved,
            // moves the hash twice.
            Mix((uint)fields.Length);
            Mix((uint)Marshal.SizeOf(type));

            foreach (var f in fields)
            {
                MixText(f.Name);
                MixText(TypeTokenOf(f));
                Mix((uint)Marshal.OffsetOf(type, f.Name));
                Mix((uint)Marshal.SizeOf(f.FieldType));

                if (Recurses(f))
                    MixStruct(f.FieldType);
            }
        }
    }

    /// <summary>
    /// What a field is, as a string, so that a RETYPE AT THE SAME WIDTH moves
    /// the hash.
    ///
    /// <para>
    /// THE MUTATION A NAME-OFFSET-WIDTH WALK CANNOT SEE, and the one plan.md §4
    /// singles out: <c>int</c> to <c>float</c> keeps the name, the offset, the
    /// width and the struct's size, so everything else mixed here is identical.
    /// The two sides would then agree about the shape of the block and disagree
    /// about what its bytes MEAN, which is the worse of the two failures — a
    /// desync with no size to point at.
    /// </para>
    ///
    /// <para>
    /// A fixed buffer is named by its ELEMENT type rather than by the struct the
    /// compiler generated for it: that generated type is named after its field
    /// and its declaring type, so renaming an unrelated neighbour would move the
    /// hash for a change that moves no byte.
    /// </para>
    /// </summary>
    private static string TypeTokenOf(System.Reflection.FieldInfo field)
    {
        var buffer = field.GetCustomAttribute<FixedBufferAttribute>();

        return buffer is not null
            ? buffer.ElementType.FullName + "[]"
            : field.FieldType.FullName ?? field.FieldType.Name;
    }

    /// <summary>
    /// Whether a field is a nested struct whose own shape belongs in the hash.
    ///
    /// <para>
    /// Not a fixed buffer — <see cref="Marshal.SizeOf(System.Type)"/> already
    /// reports the whole buffer's width for the type the compiler generates, and
    /// that type's one field is named for the compiler rather than for anything
    /// a person wrote. Not a primitive and not an enum: those are leaves, and a
    /// name, a type and a width say everything there is to say about them.
    /// </para>
    /// </summary>
    private static bool Recurses(System.Reflection.FieldInfo field)
    {
        Type type = field.FieldType;

        return type.IsValueType
            && !type.IsPrimitive
            && !type.IsEnum
            && field.GetCustomAttribute<FixedBufferAttribute>() is null;
    }
}
