using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Reads a whole temp entity DECLARATION out of a blob — the exact inverse of
/// <see cref="TempEntDeclarationWriter"/>, resolving by the same C# member names
/// against the same <c>EntityLayout</c> the RecvTable was built from.
///
/// <para>
/// WHY THIS EXISTS. The client used to take a blob apart a field at a time, by
/// wire name AND by hand-chosen accessor:
/// </para>
/// <code>
/// int    nameIndex = te.UInt("EffectName");
/// Vector origin    = te.Vector("Origin");
/// Vector start     = te.Vector("Start");
/// float  scale     = te.Float("Scale");
/// </code>
/// <para>
/// which is the shape the server side already stopped writing, mirrored. It
/// carries the writer's three problems — a misspelling is a warning rather than
/// a compile error, the spelling is repeated at every site, and a field nobody
/// remembered to read is silently a zero — and one the writer does not have.
/// </para>
///
/// <para>
/// <b>THE SIGNEDNESS IS PICKED AT THE CALL SITE, AND IT IS NOT THE CALL SITE'S
/// TO PICK.</b> <see cref="TempEntReader"/> offers <c>Int</c> and <c>UInt</c>,
/// and they differ exactly when the field is one or two bytes wide and its top
/// bit is set: <c>Int</c> reads a <c>sbyte</c>, so an unsigned 200 comes back as
/// -56. Ten members in the inventory are that shape — <c>TeShatterSurface</c>'s
/// six colour bytes among them — and there were 210 of these choices written out
/// by hand across 42 client files, each one reading into an <c>int</c> local
/// where nothing narrows the value back.
/// </para>
///
/// <para>
/// This reader cannot get that wrong, and the reason is worth being precise
/// about rather than claiming more than it does: the value lands back in the
/// MEMBER it came from, whose width is what <c>SizeBytes</c> was taken from in
/// the first place, so the cast restores the bits whichever accessor is used.
/// The declaration is immune by construction. It is the by-name form, reading a
/// one-byte field into a four-byte local, that is not — and that is the drift
/// being removed, not a bug in the reading itself.
/// </para>
///
/// <para>
/// Answers how many members it filled, for the same reason the writer does: a
/// silent zero is what this removes, not something to introduce.
/// </para>
/// </summary>
public static class TempEntDeclarationReader
{
    /// <summary>
    /// A new <typeparamref name="T"/> with every <c>[TeProp]</c> member — its
    /// own and every one it inherits — filled from <paramref name="te"/>.
    /// </summary>
    public static T Read<T>(in TempEntReader te) where T : TempEntState, new()
    {
        var state = new T();
        ReadChain(in te, state);
        return state;
    }

    /// <summary>
    /// Fills an existing state object, ROOT FIRST — the order the blob is laid
    /// out in and the order <c>EntityLayout.Build</c> walks the chain in.
    /// </summary>
    public static int ReadChain(in TempEntReader te, TempEntState state)
        => ReadLevels(in te, state.GetType(), state, depth: 0);

    /// <summary>
    /// The same for a nested table, which is a <c>struct</c> and so has to be
    /// boxed to be written through reflection and copied back out.
    /// </summary>
    public static int ReadInto<T>(in TempEntReader te, ref T declaration) where T : struct
    {
        object box = declaration;
        int filled = ReadLevels(in te, typeof(T), box, depth: 0);
        declaration = (T)box;
        return filled;
    }

    /// <summary>
    /// Root first, and the walk stops where the writer's does — at
    /// <see cref="TempEntState"/> for a declaration, at <c>ValueType</c> for a
    /// nested table.
    /// </summary>
    private static int ReadLevels(in TempEntReader te, Type leaf, object target, int depth)
    {
        var levels = new List<Type>();

        for (Type? t = leaf;
             t is not null && t != typeof(TempEntState)
                           && t != typeof(object) && t != typeof(ValueType);
             t = t.BaseType)
            levels.Add(t);

        levels.Reverse();

        int filled = 0;

        foreach (Type level in levels)
            filled += ReadMembers(in te, level, target, depth);

        return filled;
    }

    private static int ReadMembers(in TempEntReader te, Type level, object target, int depth)
    {
        int filled = 0;

        foreach (FieldInfo member in Members(level))
        {
            var attr = member.GetCustomAttribute<TePropAttribute>()!;

            // A NESTED TABLE IS READ THROUGH ITS OWN DECLARATION, by its own
            // wire names, exactly as the writer writes it. There is one in the
            // inventory -- DT_TEEffectDispatch's m_EffectData -- and everything
            // that effect carries lives one level down, so without this arm the
            // outer declaration reads NOTHING and the type switch below falls to
            // its warning.
            if (attr is { Inferred: false, Kind: TePropKind.DataTable })
            {
                if (depth >= 8)
                {
                    te.Log.Warning(
                        $"[SourceSharp] temp entity {level.Name}.{member.Name} nests tables more "
                        + "than eight deep; the rest was NOT read\n");
                    continue;
                }

                object nested = member.GetValue(target)
                                ?? Activator.CreateInstance(member.FieldType)!;

                filled += ReadLevels(in te, nested.GetType(), nested, depth + 1);
                member.SetValue(target, nested);
                continue;
            }

            // THE MEMBER'S OWN [TeProp] SAYS ITS WIRE NAME, for the same reason
            // the writer asks it rather than the registry: a base's fields are
            // in the derived blob but not in its TeClass.Fields, so a lookup by
            // member name against the class resolves the derived fields and
            // silently misses every inherited one.
            string wire = attr.WireName ?? member.Name;
            Type   type = member.FieldType;

            // SIGNEDNESS COMES FROM THE DECLARATION. See the class note: this is
            // the one choice the by-name form made at the call site that it had
            // no business making.
            bool unsigned = (attr.Flags & TeFlags.Unsigned) != 0;

            object? value;

            if (type == typeof(Vector))
                value = te.Vector(wire);
            // A facing: a different wire kind from a vector, the same twelve
            // bytes, and the member's type is what says which.
            else if (type == typeof(QAngle))
                value = te.QAngle(wire);
            // The four props TeClassBuilder expanded this one member into.
            else if (type == typeof(Color32))
                value = new Color32(te.UInt(wire + "R"), te.UInt(wire + "G"),
                                    te.UInt(wire + "B"), te.UInt(wire + "A"));
            else if (type == typeof(float))
                value = te.Float(wire);
            else if (type == typeof(bool))
                value = te.Bool(wire);
            else if (type == typeof(byte))
                value = (byte)(unsigned ? te.UInt(wire) : te.Int(wire));
            else if (type == typeof(short))
                value = (short)(unsigned ? te.UInt(wire) : te.Int(wire));
            else if (type == typeof(int))
                value = unsigned ? te.UInt(wire) : te.Int(wire);
            else if (type == typeof(EntityHandle))
                value = Handle(in te, wire);
            else if (type.IsEnum)
                value = Enum.ToObject(type, unsigned ? te.UInt(wire) : te.Int(wire));
            else if (InlineVectors(in te, wire, member, target))
            {
                filled++;
                continue;
            }
            else
            {
                Unsupported(member, te.Log);
                continue;
            }

            member.SetValue(target, value);
            filled++;
        }

        return filled;
    }

    /// <summary>
    /// The packed networked handle put back together the way
    /// <c>TempEntWriter.EHandle</c> took it apart: the entry index in the low
    /// bits and the serial at <c>NUM_SERIAL_NUM_SHIFT_BITS</c>. An invalid
    /// handle on the wire reads as <see cref="EntityHandle.None"/> rather than
    /// as entity zero, which is the world.
    /// </summary>
    private static EntityHandle Handle(in TempEntReader te, string wire)
    {
        const int SerialShift = 16;   // NUM_SERIAL_NUM_SHIFT_BITS

        if (!te.EHandle(wire, out int entry, out int serial))
            return EntityHandle.None;

        return new EntityHandle((uint)(entry | (serial << SerialShift)));
    }

    /// <summary>
    /// Members carrying a <c>[TeProp]</c> that THIS type declares, in
    /// declaration order — declared-only, because the caller walks the chain and
    /// a level that re-reported its base's fields would read them twice.
    /// </summary>
    private static IEnumerable<FieldInfo> Members(Type type)
        => type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
               .Where(f => f.GetCustomAttribute<TePropAttribute>() is not null)
               .OrderBy(f => f.MetadataToken);

    /// <summary>
    /// An <c>[InlineArray]</c> run of <see cref="Vector"/> — the inventory's one
    /// array, and the one member shape the type switch above cannot express.
    ///
    /// <para>
    /// WRITTEN THROUGH A PINNED POINTER for the reason the writer reads through
    /// one: an <c>[InlineArray]</c> declares ONE field and the rest of the run
    /// is storage the compiler reserved after it, with no <c>FieldInfo</c> to
    /// address. The elements are contiguous by construction, which is what makes
    /// it the right storage for a <c>CNetworkArray</c>.
    /// </para>
    ///
    /// <para>
    /// EVERY element is read, whatever the count field says, because the array
    /// prop's length proxy is null and the engine encodes them all (plan 3.3) —
    /// symmetric with the writer, which sends them all.
    /// </para>
    /// </summary>
    private static unsafe bool InlineVectors(in TempEntReader te, string wire,
                                             FieldInfo member, object target)
    {
        if (member.FieldType.GetCustomAttribute<InlineArrayAttribute>() is not { } run)
            return false;

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public |
                                 BindingFlags.NonPublic;

        FieldInfo? element = member.FieldType.GetFields(Any).FirstOrDefault();

        if (element?.FieldType != typeof(Vector) || run.Length <= 0)
            return false;

        object box = member.GetValue(target) ?? Activator.CreateInstance(member.FieldType)!;

        GCHandle pin = GCHandle.Alloc(box, GCHandleType.Pinned);

        try
        {
            var at = (float*)pin.AddrOfPinnedObject();
            int n  = Unsafe.SizeOf<Vector>() / sizeof(float);

            for (int i = 0; i < run.Length; i++)
            {
                Vector v = te.VectorAt(wire, i);

                at[i * n]     = v.X;
                at[i * n + 1] = v.Y;
                at[i * n + 2] = v.Z;
            }
        }
        finally
        {
            pin.Free();
        }

        member.SetValue(target, box);
        return true;
    }

    private static void Unsupported(FieldInfo member, ILog log)
        => log.Warning(
            $"[SourceSharp] temp entity member {member.DeclaringType?.Name}.{member.Name} is a " +
            $"{member.FieldType.Name}, which this reader has no case for; it was LEFT AT ITS " +
            "DEFAULT. Add a case rather than reading it by wire name at the call site — a field " +
            "left at zero looks exactly like one that arrived as zero.\n");
}
