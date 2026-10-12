using System.Globalization;
using System.Text;
using SourceSharp.Server.Interface;

namespace Debug.Api;

/// <summary>
/// The schema and the entity inspector, as JSON.
///
/// <para>
/// WHAT THIS REPLACES, and it has now actually replaced it. A networked class
/// used to be readable from the console: <c>cs_sv_dump_nettable</c> printed every
/// prop's name, blob offset, bit count and type, and anybody chasing a wrong
/// value could read the table off a log. Every prop is named <c>p000</c>..<c>p511</c>
/// now, the real names live only in C#, and Step 7 deleted the dump along with
/// native's copy of the field list it printed. These routes are where that
/// readability went, and they were built BEFORE the migration on purpose — an
/// oracle written after the thing it checks is an oracle written to agree with
/// it.
/// </para>
///
/// <para>
/// Hand-built JSON like the rest of this assembly. A serializer would be less
/// code and would also pull a dependency and a chunk of reflection into an
/// assembly whose whole design goal is to be absent unless somebody asked for
/// it.
/// </para>
/// </summary>
internal static class NetTableJson
{
    /// <summary>Bytes to a row of the hex ruler. Sixteen, as every hex dump.</summary>
    private const int RowBytes = 16;

    //-------------------------------------------------------------------------
    // /nettables — the schema.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every type either shell networks, and the tiers they are on.
    ///
    /// <para>
    /// THE TIER LIST IS A READING SINCE STEP 6. It used to be a projection of
    /// the boundary rule — this file's own copy of the sizes and its own
    /// arithmetic for a tier's prop count — flagged as one with
    /// <c>tiersLive:false</c> in every row. Native registers the tiers now and
    /// reports what it BUILT, which is the only version that cannot drift from
    /// what is on the wire.
    /// </para>
    ///
    /// <para>
    /// The prop count in particular had to stop being a formula: Part 3's
    /// "6 + payloadBytes/4" is the hierarchy's, and the runtime-networked entity
    /// puts three real props in front of its payload rather than six. A formula
    /// right about one shell would have been quietly wrong about the other.
    /// </para>
    ///
    /// <para>
    /// The panel flags anything within a tenth of its ceiling, because
    /// outgrowing a tier is the one schema change that still needs a restart and
    /// it should be seen coming rather than discovered at connect.
    /// </para>
    /// </summary>
    /// <param name="tiers">
    /// The tier table to report. Passed in rather than read from a static: the
    /// table is state, and a test driving this view supplies its own.
    /// </param>
    public static string Schema(IReadOnlyList<DebugNetType> types, DebugNetTiers tiers,
                                IReadOnlyList<DebugNetRefusal> refused)
    {
        var json = new StringBuilder("{\"types\":[");

        for (int i = 0; i < types.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            AppendType(json, types[i], withFields: false);
        }

        // The hierarchy's, because it is the shell with the classes on it. The
        // SIZES are the same for both — the boundary rule has one copy — so the
        // rows a reader compares a type against are the same either way; what
        // differs is the table name and the prop count, and the note under the
        // table says which shell those belong to.
        var rows = tiers.Standalone;

        json.Append("],\"count\":").Append(types.Count)
            .Append(",\"sharedProps\":").Append(tiers.SharedProps)
            .Append(",\"tiers\":[");

        for (int i = 0; i < rows.Count; i++)
        {
            var tier = rows[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"name\":").Append(Q(tier.Table))
                .Append(",\"payloadBytes\":").Append(tier.PayloadBytes)
                .Append(",\"words\":").Append(tier.Words)
                // What native BUILT, not what a formula says it should have.
                .Append(",\"props\":").Append(tier.Props)
                // Whether anything is actually on this tier. A tier registered
                // and never used is the honest middle state and the panel has to
                // be able to show it: it is what "room to grow into" looks like.
                .Append(",\"live\":").Append(tier.Live ? "true" : "false")
                .Append(",\"types\":[");

            int assigned = 0;

            foreach (var type in types)
            {
                // By the blob it is ACTUALLY carried on, not by the tier its own
                // layout would fit in. On the parallel hierarchy those are
                // different numbers and the difference is the whole of Step 6's
                // third correction: every entity there is allocated one blob
                // whatever its class, so a twenty-byte type sits on the tier the
                // greediest one needed. Assigning by layout size would put it in
                // a row whose table its entities never touch.
                if (type.BlobBytes != tier.PayloadBytes)
                    continue;

                if (assigned++ > 0)
                    json.Append(',');

                json.Append(Q(type.ClassName));
            }

            json.Append("],\"count\":").Append(assigned).Append('}');
        }

        // A type too big for the largest tier is not a rounding problem, it is a
        // type that cannot be networked by the design at all — so it is counted
        // separately rather than folded into the 2048 row.
        int oversized = 0;

        foreach (var type in types)
        {
            if (type.Oversized)
                oversized++;
        }

        // TRUE when the rows above came from native, and FALSE when there was
        // nothing to ask — which is a real state the panel has to render
        // differently, not an error. A UI that showed an empty tier list as
        // "this build has no tiers" would be the projection's mistake arriving
        // by the opposite door.
        json.Append("],\"tiersLive\":").Append(rows.Count > 0 ? "true" : "false")
            .Append(",\"oversized\":").Append(oversized);

        // WHAT THIS BUILD COULD NOT PUT ON THE WIRE (Part 13, "schema mismatch
        // must be loud"). Two causes, both build-time facts: a type id
        // collision, and a layout past the largest tier. Either one means a
        // class whose entities pair with the wrong managed half or with none,
        // and neither has any other symptom.
        json.Append(",\"refused\":[");



        for (int i = 0; i < refused.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append("{\"className\":").Append(Q(refused[i].ClassName))
                .Append(",\"typeIdHex\":").Append(Q($"0x{refused[i].TypeId:X8}"))
                .Append(",\"layoutBytes\":").Append(refused[i].LayoutBytes)
                .Append('}');
        }

        return json.Append(']').Append('}').ToString();
    }

    //-------------------------------------------------------------------------
    // /nettable/<typeid> — one type's fields.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One type's fields, decoded from the C# layout: name, C# type, blob offset
    /// and size. This is the view that replaces reading prop names off a console
    /// dump, and every row of it can be checked against one today.
    /// </summary>
    /// <inheritdoc cref="Schema"/>
    public static string Type(IReadOnlyList<DebugNetType> types, DebugNetType? found, string key)
    {

        if (found is null)
        {
            // Naming what does exist, because the usual reason for a miss is a
            // classname that is declared but not published — and an empty
            // "no such type" would send somebody looking for a typo instead.
            var names = new StringBuilder();

            for (int i = 0; i < types.Count; i++)
            {
                if (i > 0)
                    names.Append(',');

                names.Append(Q(types[i].ClassName));
            }

            return $"{{\"error\":\"no networked type by that name or id\",\"asked\":{Q(key)}," +
                   $"\"known\":[{names}]}}";
        }

        var json = new StringBuilder();
        AppendType(json, found, withFields: true);
        return json.ToString();
    }

    //-------------------------------------------------------------------------
    // /entity/<index>/payload — one live entity, three ways.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One live entity's payload: offset-annotated hex, the fields decoded
    /// through its layout, and what changed since the last time this was asked.
    ///
    /// The third of those is the one that catches an offset bug. A field reading
    /// the wrong bytes does not look wrong on its own — it looks wrong when its
    /// NEIGHBOUR is written and it moves, and that is only visible against a
    /// previous sample.
    /// </summary>
    /// <inheritdoc cref="Schema"/>
    public static string Payload(DebugNetPayload read, NetPayloadSampler.Diff diff)
    {
        var type = read.Type;

        if (!read.Found)
        {
            return $"{{\"error\":\"no entity at that index\",\"index\":{read.Index}}}";
        }

        var json = new StringBuilder("{\"index\":").Append(read.Index)
            .Append(",\"handle\":").Append(read.Handle)
            .Append(",\"classname\":").Append(Q(read.ClassName))
            .Append(",\"type\":");

        if (type is null)
        {
            // A real answer and a common one: most classes on the hierarchy
            // network nothing of their own and share one table, so they have a
            // blob and no schema to read it through. Saying that beats decoding
            // the bytes against somebody else's layout.
            json.Append("null");
        }
        else
        {
            AppendType(json, type, withFields: false);
        }

        // Whether the two paths carrying this entity agree, while both are live
        // (plan_nettable_generic.md Part 14 Step 3).
        //
        // Ahead of the bytes because it is the answer somebody watching this
        // endpoint during the migration is watching FOR — and because it is the
        // one field here that native computed rather than this file deriving it
        // from the same bytes it is printing.
        json.Append(",\"payload\":{\"state\":").Append(Q(read.AgreementName))
            .Append(",\"agrees\":").Append(read.Agrees ? "true" : "false")
            .Append(",\"words\":").Append(read.AgreementWords)
            .Append(",\"firstBadWord\":").Append(read.FirstBadWord)
            .Append(",\"live\":")
            .Append(read.AgreementSupplied ? "true" : "false")
            .Append('}');

        // The bytes, as they are. First rather than last because when the
        // decode is what is under suspicion, this is the part that is not.
        json.Append(",\"blob\":{\"bytes\":").Append(read.Blob.Length)
            .Append(",\"hex\":").Append(Q(PayloadBytes.Hex(read.Blob)))
            .Append(",\"rowBytes\":").Append(RowBytes)
            .Append(",\"rows\":[");

        for (int offset = 0; offset < read.Blob.Length; offset += RowBytes)
        {
            int length = Math.Min(RowBytes, read.Blob.Length - offset);

            if (offset > 0)
                json.Append(',');

            json.Append("{\"offset\":").Append(offset)
                .Append(",\"hex\":").Append(Q(PayloadBytes.Hex(read.Blob.AsSpan(offset, length))))
                .Append(",\"ascii\":").Append(Q(Ascii(read.Blob.AsSpan(offset, length))))
                .Append('}');
        }

        json.Append("]}");

        // Field by field, through the layout.
        json.Append(",\"fields\":[");

        if (type is not null)
        {
            var decoded = read.Decoded;

            for (int i = 0; i < decoded.Count; i++)
            {
                if (i > 0)
                    json.Append(',');

                AppendDecoded(json, decoded[i], PayloadBytes.Touches(decoded[i].Field, diff.Runs));
            }
        }

        json.Append(']');

        // And what moved. A first sample has nothing to compare against, which
        // is not "nothing changed" — a view that showed those the same way would
        // report a still entity and a newly watched one identically.
        json.Append(",\"changed\":{\"first\":").Append(diff.First ? "true" : "false")
            .Append(",\"sinceSeconds\":").Append(Num(diff.SinceSeconds))
            .Append(",\"bytes\":").Append(diff.ChangedBytes)
            .Append(",\"resized\":").Append(diff.Resized ? "true" : "false")
            .Append(",\"runs\":[");

        for (int i = 0; i < diff.Runs.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append('[').Append(diff.Runs[i].Offset).Append(',').Append(diff.Runs[i].Length).Append(']');
        }

        json.Append("]}");

        // Native reserves the hierarchy's blob for the GREEDIEST class, so a
        // blob longer than this type's layout is normal and a blob shorter than
        // it is a real problem. Reported both ways rather than left as an
        // arithmetic exercise for whoever is reading a hex dump at the time.
        if (type is not null)
        {
            json.Append(",\"layoutBytes\":").Append(type.LayoutBytes)
                .Append(",\"blobShorterThanLayout\":")
                .Append(read.Blob.Length < type.LayoutBytes ? "true" : "false");
        }

        return json.Append('}').ToString();
    }

    //-------------------------------------------------------------------------
    // Shared shapes.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A type's header, with or without its field list.
    ///
    /// One writer for both routes so the schema view and the detail view cannot
    /// come to describe the same type differently — which is exactly the drift
    /// an oracle must not have.
    /// </summary>
    private static void AppendType(StringBuilder json, DebugNetType type, bool withFields)
    {
        json.Append("{\"className\":").Append(Q(type.ClassName))
            .Append(",\"typeName\":").Append(Q(type.TypeName))
            .Append(",\"shell\":").Append(Q(type.Standalone ? "standalone" : "netentity"))
            .Append(",\"table\":").Append(Q(type.TableName))
            .Append(",\"typeId\":").Append(type.TypeId)
            .Append(",\"typeIdHex\":").Append(Q($"0x{type.TypeId:X8}"))
            .Append(",\"schemaHash\":").Append(type.SchemaHash)
            .Append(",\"schemaHashHex\":").Append(Q($"0x{type.SchemaHash:X8}"))
            .Append(",\"layoutBytes\":").Append(type.LayoutBytes)
            .Append(",\"blobBytes\":").Append(type.BlobBytes)
            .Append(",\"props\":").Append(type.PropCount)
            // Of those props, how many are generic payload words. ONE number
            // for every class on the parallel hierarchy, because the blob it
            // covers is one length for every entity there -- see the ABI's
            // NetHierarchyDeclare for why that is a hierarchy number and not
            // a per-class one.
            .Append(",\"payloadWords\":").Append(type.PayloadWords)
            .Append(",\"fieldCount\":").Append(type.Fields.Count)
            // A class may declare state before its entity has been moved onto
            // the hierarchy. That one is described and networks nothing, and
            // "declared but not on the wire" must not read the same as "on the
            // wire and decoding to zero".
            .Append(",\"published\":").Append(type.Published ? "true" : "false")
            // The tier the type is IN, not one it would be in — so the ceiling
            // is the blob it is carried on, which on the hierarchy is the
            // family's rather than this class's. "live" says whether any of it
            // was read: false is what a build with no tier list to ask answers,
            // and it has to stay distinguishable from a real reading.
            //
            // Fill is against that ceiling, so what it measures is the thing
            // worth a warning: how close THIS type is to needing a tier its
            // family does not have. A type on a 256-byte blob whose own layout
            // is 20 bytes is nowhere near its ceiling, and reporting it as
            // 20-of-32 would have flagged it for a restart it does not need.
            .Append(",\"tier\":{\"ceiling\":").Append(type.BlobBytes)
            .Append(",\"fill\":")
            .Append(Num(type.BlobBytes > 0 ? (double)type.LayoutBytes / type.BlobBytes : 1.0))
            .Append(",\"nearCeiling\":")
            .Append(type.BlobBytes > 0 && (double)type.LayoutBytes / type.BlobBytes >= 0.9
                        ? "true" : "false")
            .Append(",\"live\":")
            .Append(type.TierLive ? "true" : "false")
            .Append('}');

        if (withFields)
        {
            json.Append(",\"fields\":[");

            for (int i = 0; i < type.Fields.Count; i++)
            {
                if (i > 0)
                    json.Append(',');

                AppendField(json, type.Fields[i]);
            }

            json.Append(']');
        }

        json.Append('}');
    }

    /// <summary>
    /// A field's shape. Left OPEN when a value is about to be appended to it:
    /// the inspector reads a field and its value together, so they are one
    /// object rather than two, and one writer produces the shape for both routes.
    /// </summary>
    private static void AppendField(StringBuilder json, DebugNetField field, bool close = true)
    {
        json.Append("{\"name\":").Append(Q(field.Name))
            .Append(",\"csType\":").Append(Q(field.CsType))
            .Append(",\"kind\":").Append(Q(field.Kind))
            .Append(",\"offset\":").Append(field.Offset)
            .Append(",\"bytes\":").Append(field.Bytes)
            // Storage and wire width are different numbers and both matter. Since
            // packing they differ for every narrow field: a bool is one bit in
            // four bytes of storage it SHARES, which is what bitShift and packed
            // below say. Without them a reader sees four fields at offset zero
            // across four bytes each and has no way to tell that from a bug.
            .Append(",\"bits\":").Append(field.Bits)
            .Append(",\"bitShift\":").Append(field.BitShift)
            .Append(",\"packed\":").Append(field.Packed ? "true" : "false");

        // THE MEMBER NAMES (plan_nettable_generic.md Step 11). Part 13's whole
        // argument is that these routes replace the readability the generic
        // payload removed, and a field that reads "3" where the author wrote
        // Snowfall is that loss arriving by another door. Carried on the FIELD
        // rather than in a table beside it, so /nettable and the payload decode
        // get it from one writer and cannot come to disagree.
        //
        // Absent, not empty, for a field that is not an enum: a panel testing
        // for the key gets the same answer as a panel testing the type.
        if (field.Enum is { } enumType)
        {
            var members = enumType.Members;

            json.Append(",\"enum\":{\"type\":").Append(Q(enumType.TypeName))
                .Append(",\"flags\":").Append(enumType.Flags ? "true" : "false")
                .Append(",\"members\":[");

            for (int i = 0; i < members.Count; i++)
            {
                if (i > 0)
                    json.Append(',');

                json.Append("{\"name\":").Append(Q(members[i].Name))
                    .Append(",\"value\":").Append(members[i].Value).Append('}');
            }

            json.Append("]}");
        }

        if (close)
            json.Append('}');
    }

    private static void AppendDecoded(StringBuilder json, DebugNetValue decoded, bool changed)
    {
        AppendField(json, decoded.Field, close: false);

        json.Append(",\"present\":").Append(decoded.Present ? "true" : "false")
            .Append(",\"value\":").Append(Q(decoded.Value))
            .Append(",\"hex\":").Append(Q(decoded.Hex))
            .Append(",\"number\":").Append(decoded.Number is { } n ? Num(n) : "null")
            .Append(",\"changed\":").Append(changed ? "true" : "false")
            .Append('}');
    }

    /// <summary>
    /// The printable half of a hex row.
    ///
    /// Worth having next to the bytes because a string field is the one thing a
    /// hex dump makes unreadable, and <c>m_szClassname</c> is 64 bytes of one on
    /// every entity on the hierarchy.
    /// </summary>
    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);

        foreach (byte b in bytes)
            text.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');

        return text.ToString();
    }

    /// <summary>
    /// A number JSON will accept. "R" so a float that came out of a blob is not
    /// rounded on its way to the one view meant to say what the blob contains.
    /// </summary>
    private static string Num(double value)
        => double.IsFinite(value)
            ? value.ToString("R", CultureInfo.InvariantCulture)
            // JSON has no NaN. Null rather than a zero, which would be a
            // measurement rather than the absence of one.
            : "null";

    private static string Q(string text) => DebugApiServer.Quote(text);
}
