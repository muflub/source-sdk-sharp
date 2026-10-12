using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Writes a whole temp entity DECLARATION into a blob — set the fields on the
/// struct the schema is already declared with, hand it over, and the offsets
/// come from the same <c>EntityLayout</c> the SendTable was built from.
///
/// <para>
/// WHY THIS EXISTS. The blob used to be filled a field at a time by WIRE name:
/// </para>
/// <code>
/// writer.Vector("m_vecOrigin", position);
/// writer.Int("m_nMagnitude", magnitude);
/// writer.Vector("m_vecDir", direction ?? Vector.Zero);
/// </code>
/// <para>
/// Three problems, and the third is the one that bites. A misspelling is a
/// runtime warning rather than a compile error. The stock spelling has to be
/// repeated at every call site, so <c>m_vecPos</c> appears in code that has a
/// perfectly good <c>Position</c> to say instead. And the set of fields written
/// is whatever the author remembered — nothing checks that a field was left at
/// zero because it was meant to be, rather than because it was forgotten. A
/// temp entity with one unwritten field is an effect at the world origin, or a
/// model index of zero, and it looks like a wire bug.
/// </para>
///
/// <para>
/// Handing over the declaration fixes all three: the field names are the C#
/// members, the compiler checks them, and the whole struct goes at once.
/// </para>
///
/// <para>
/// THE WIRE NAMES DO NOT GO AWAY and must not. <c>TeField</c> carries both —
/// the wire name is what the two sides pair on and may not be tidied
/// (plan_tmp.md 2E); the member name is what C# should be able to say. This
/// resolves by MEMBER name.
/// </para>
/// </summary>
public static class TempEntDeclarationWriter
{
    /// <summary>
    /// Writes every <c>[TeProp]</c> member of <paramref name="declaration"/>
    /// into <paramref name="writer"/>, by member name.
    ///
    /// <para>
    /// A base table's fields are its OWN declaration's, so a derived class is
    /// written by handing over both — base first, exactly as the blob is laid
    /// out and as <c>BEGIN_SEND_TABLE</c> nests them.
    /// </para>
    ///
    /// <para>
    /// Answers how many members it wrote, so a caller can assert it. A silent
    /// zero is the failure this is meant to remove, not one to introduce.
    /// </para>
    /// </summary>
    public static int Write<T>(ref TempEntWriter writer, in T declaration) where T : struct
        => WriteMembers(ref writer, typeof(T), declaration);

    /// <summary>
    /// Every <c>[TeProp]</c> on <paramref name="state"/> and on every class it
    /// inherits from, ROOT FIRST — the order the blob is laid out in.
    /// </summary>
    public static int WriteChain(ref TempEntWriter writer, TempEntState state)
        => WriteLevels(ref writer, state.GetType(), state, depth: 0);

    /// <summary>
    /// One object's <c>[TeProp]</c> members, ROOT FIRST — the order the blob is
    /// laid out in, and the order <c>EntityLayout.Build</c> walks the chain in.
    ///
    /// <para>
    /// The walk stops at <see cref="TempEntState"/> for a declaration and at
    /// <c>ValueType</c>/<c>object</c> for a nested table, which is a struct
    /// today and need not stay one.
    /// </para>
    /// </summary>
    private static int WriteLevels(ref TempEntWriter writer, Type leaf, object value, int depth)
    {
        var levels = new List<Type>();

        for (Type? t = leaf;
             t is not null && t != typeof(TempEntState)
                           && t != typeof(object) && t != typeof(ValueType);
             t = t.BaseType)
            levels.Add(t);

        levels.Reverse();

        int written = 0;

        foreach (Type level in levels)
            written += WriteMembers(ref writer, level, value, depth);

        return written;
    }

    private static int WriteMembers(ref TempEntWriter writer, Type level, object declaration,
                                    int depth = 0)
    {
        int written = 0;

        foreach (FieldInfo member in Members(level))
        {
            object? value = member.GetValue(declaration);

            if (value is null)
                continue;

            var attr = member.GetCustomAttribute<TePropAttribute>()!;

            // A TABLE NESTED AS A FIELD IS WRITTEN THROUGH ITS OWN DECLARATION,
            // by ITS OWN wire names. There is one in the whole inventory —
            // DT_TEEffectDispatch's m_EffectData — and every value that effect
            // carries lives one level down, so without this the outer
            // declaration writes NOTHING and the switch below reports the field
            // as a type it has no case for.
            //
            // No offset arithmetic here, and that is the point: the registry's
            // by-wire-name map already carries the nested table's fields at
            // their own offset PLUS the datatable prop's (TempEntRegistry.
            // AddFieldsOf), pad and all, so writing them by name lands them
            // where the SendTable reads them.
            if (attr is { Inferred: false, Kind: TePropKind.DataTable })
            {
                if (depth >= 8)
                {
                    writer.Log.Warning(
                        $"[SourceSharp] temp entity {level.Name}.{member.Name} nests tables more "
                        + "than eight deep; the rest was NOT written\n");
                    continue;
                }

                written += WriteLevels(ref writer, value.GetType(), value, depth + 1);
                continue;
            }

            // THE MEMBER'S OWN [TeProp] SAYS ITS WIRE NAME, which is why this
            // does not look one up in the registry. A base's fields are in the
            // derived class's blob but NOT in its TeClass.Fields -- they are
            // reachable only through the flattened by-wire-name dictionary --
            // so a member-name lookup against the class would resolve a derived
            // field and silently miss every inherited one. Asking the
            // declaration is exact for both.
            string wire = attr.WireName ?? member.Name;

            bool ok = value switch
            {
                Vector v        => writer.Vector(wire, v),

                // A FACING, which is a different wire kind from a vector and the
                // same twelve bytes -- see TempEntWriter.QAngle.
                QAngle q        => writer.QAngle(wire, q),

                // FOUR PROPS FROM ONE MEMBER, named as TeClassBuilder named
                // them. There is no SendPropColor; stock sends r, g, b and a as
                // four eight-bit unsigned ints, and this writes those four.
                Color32 c       => writer.Int(wire + "R", c.R)
                                 & writer.Int(wire + "G", c.G)
                                 & writer.Int(wire + "B", c.B)
                                 & writer.Int(wire + "A", c.A),

                float f         => writer.Float(wire, f),
                bool b          => writer.Int(wire, b ? 1 : 0),
                byte by         => writer.Int(wire, by),
                short sh        => writer.Int(wire, sh),
                int i           => writer.Int(wire, i),
                EntityHandle eh => writer.EHandle(wire, eh),
                Enum e          => writer.Int(wire, Convert.ToInt32(e)),
                _               => InlineVectors(ref writer, wire, member, value)
                                   ?? Unsupported(member, writer.Log),
            };

            if (ok)
                written++;
        }

        return written;
    }

    /// <summary>
    /// Members carrying a <c>[TeProp]</c> that THIS type declares, in
    /// declaration order. Declared-only, because the caller walks the chain and
    /// a level that re-reported its base's fields would write them twice.
    /// </summary>
    private static IEnumerable<FieldInfo> Members(Type type)
        => type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
               .Where(f => f.GetCustomAttribute<TePropAttribute>() is not null)
               .OrderBy(f => f.MetadataToken);

    /// <summary>
    /// An <c>[InlineArray]</c> run of <see cref="Vector"/> — the inventory's one
    /// array, and the one member shape the value switch above cannot express.
    ///
    /// <para>
    /// EVERY OTHER MEMBER IS ONE VALUE AT ONE OFFSET, so the writer addresses it
    /// by name and is done. <c>DT_TEBeamSpline</c>'s sixteen points are ONE
    /// field at <c>offset + i * 12</c> — a name AND an index — and
    /// <c>TempEntWriter.VectorAt</c> is the setter that takes both. Without this
    /// arm a <c>TeBeamSpline</c> handed to <c>ToBlob</c> wrote its count and
    /// warned about its points, which is a spline with sixteen coordinates at
    /// the world origin: exactly the silently-unwritten field this writer exists
    /// to remove, on the one declaration where it would have been hardest to
    /// see.
    /// </para>
    ///
    /// <para>
    /// ALL <c>Length</c> ELEMENTS GO, whatever the count field says, because the
    /// array prop's length proxy is NULL and the engine encodes every element
    /// regardless (plan 3.3) — the same reason stock's own <c>TE_BeamSpline</c>
    /// zeroes its tail rather than leaving it.
    /// </para>
    ///
    /// <para>
    /// READ THROUGH A PINNED POINTER, because reflection cannot see past element
    /// zero: an <c>[InlineArray]</c> declares ONE field and the other fifteen
    /// elements are storage the compiler reserved after it, with no
    /// <c>FieldInfo</c> of their own. The run is contiguous by construction —
    /// that is what makes it the right storage for a <c>CNetworkArray</c> — so
    /// the elements are at the boxed value's address, a <c>Vector</c> apart.
    /// </para>
    ///
    /// <para>
    /// Null for a member that is not one of these, so the caller falls through
    /// to its warning rather than this having to know what else exists.
    /// </para>
    /// </summary>
    private static unsafe bool? InlineVectors(ref TempEntWriter writer, string wire,
                                              FieldInfo member, object value)
    {
        if (member.FieldType.GetCustomAttribute<InlineArrayAttribute>() is not { } run)
            return null;

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public |
                                 BindingFlags.NonPublic;

        FieldInfo? element = member.FieldType.GetFields(Any).FirstOrDefault();

        if (element?.FieldType != typeof(Vector) || run.Length <= 0)
            return null;

        GCHandle pin = GCHandle.Alloc(value, GCHandleType.Pinned);

        try
        {
            var at = (float*)pin.AddrOfPinnedObject();
            int  n  = Unsafe.SizeOf<Vector>() / sizeof(float);
            bool ok = true;

            for (int i = 0; i < run.Length; i++)
                ok &= writer.VectorAt(wire, i, new Vector(at[i * n], at[i * n + 1], at[i * n + 2]));

            return ok;
        }
        finally
        {
            pin.Free();
        }
    }

    private static bool Unsupported(FieldInfo member, ILog log)
    {
        log.Warning(
            $"[SourceSharp] temp entity member {member.DeclaringType?.Name}.{member.Name} is a " +
            $"{member.FieldType.Name}, which this writer has no case for; NOTHING was written " +
            "for it. Add a case rather than writing it by wire name at the call site — a field " +
            "left at zero looks exactly like one that was meant to be zero.\n");

        return false;
    }
}
