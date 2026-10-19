using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// What the two sides have agreed to send, and how to read one entity's bytes
/// back through it.
///
/// <para>
/// THIS IS THE ORACLE, and it was written before the thing it checks. A
/// networked class used to be legible from the console: <c>cs_sv_dump_nettable</c>
/// printed each prop's name, blob offset, bit count and type. Every prop is
/// named <c>p000</c>..<c>p511</c> now and every real name lives in C#, so a
/// console dump can no longer answer "what does byte 12 mean" — and a payload
/// that decodes wrongly is the failure with no other symptom, which this tree
/// has already paid for once with a pane of glass that broke on the server and
/// stayed whole on every screen.
/// </para>
///
/// <para>
/// So this describes the schema in terms of the C# declarations, and decodes a
/// blob through it. Written in Step 2, against the per-class tables that still
/// existed then, deliberately: an oracle written after the thing it checks is an
/// oracle written to agree with it. It was cross-checked line for line against
/// <c>cs_sv_dump_nettable</c> while both existed, and that agreement is what
/// makes it worth trusting now that Step 7 has deleted the dump.
/// </para>
///
/// <para>
/// No engine calls in the description or the decode. Both are pure functions of
/// metadata and bytes, which is what lets them be tested without a game — see
/// <c>NetSchemaTests</c>.
/// </para>
/// </summary>
public static class NetSchema
{
    /// <summary>
    /// The real props every entity on the parallel hierarchy sends in front of
    /// its payload: origin, angles, model index, effects, interpolation frame,
    /// type id, schema hash, payload words, and the solidity pair — the rows of
    /// <c>StandaloneNetFields</c> in standalone_nettable.h.
    ///
    /// <para>
    /// SIX since Step 6, and it was four. <c>m_szClassname</c> — 64 bytes of
    /// string per entity — became <c>m_nTypeId</c>, four bytes of FNV-1a that
    /// the far side looks up in its own registry, and two more joined it:
    /// the layout fingerprint the receiver checks before decoding anything, and
    /// the meaningful word count. EIGHT since <c>m_fEffects</c> and
    /// <c>m_ubInterpolationFrame</c> joined the list, and TEN now that
    /// <c>m_nSolidType</c> and <c>m_nSolidFlags</c> carry a managed prop's
    /// shape across — without them the client's impact clip asks the prop's
    /// shell <c>GetSolid</c>, gets the SOLID_NONE the received block says, and
    /// the bullet passes through. This stayed at six through both earlier
    /// joins, and TierParityTests now holds it to the header.
    /// </para>
    ///
    /// <para>
    /// A FLOOR RATHER THAN THE ANSWER. What a tier's table really carries is
    /// read from native through <see cref="NetTiers"/>, because the other shell
    /// puts THREE real props in front of its payload and a single constant
    /// cannot be right about both. This is what the description falls back to
    /// with no engine to ask.
    /// </para>
    /// </summary>
    public const int StandaloneSharedProps = 10;

    /// <summary>
    /// The DT_ name a class's SendTable and RecvTable would be paired by.
    ///
    /// One rule. It used to mirror <c>SourceSharp_RuntimeTableName</c> in
    /// runtime_netfields.h, which Step 7 deleted along with the last table built
    /// from a classname; this is the only spelling left. A second one would be
    /// exactly the
    /// drift that file exists to prevent, so this is a mirror with a test rather
    /// than an independent idea of the name.
    ///
    /// <para>
    /// NO CLASS HAS ONE SINCE STEP 6. Entities are created through a tier, so
    /// the name a class's entities are really paired under is the TIER'S — see
    /// <see cref="TableForType"/>, which is what the schema reports. This is
    /// kept because the rule itself has not changed and the routes still accept
    /// a DT_ name as a key.
    /// </para>
    /// </summary>
    public static string TableNameOf(string className) => "DT_" + className;

    /// <summary>
    /// The table a type's entities are actually created through, or its DT_ name
    /// with no engine to ask.
    ///
    /// <para>
    /// Reported rather than derived, and it matters: after the collapse a class
    /// no longer has a table of its own, so a schema view that went on printing
    /// <c>DT_ss_beam</c> would be naming something that does not exist. Step 4
    /// caught this route going stale as an oracle once already, and an oracle
    /// that quietly stops describing what native builds is the same failure
    /// arriving later.
    /// </para>
    /// </summary>
    /// <param name="tiers">
    /// The tier table to read. Threaded in rather than reached for as a static,
    /// because it is state and a static one is state every caller shares — see
    /// <see cref="INetTiers"/>.
    /// </param>
    public static string TableForType(INetTiers tiers, NetShellKind shell, int blobBytes,
                                      string className)
        => TierCarrying(tiers, shell, blobBytes)?.Table ?? TableNameOf(className);

    /// <summary>
    /// The type id a payload would carry, per Part 6 of the plan:
    /// <c>FNV-1a(classname)</c>, computed independently on each side from the
    /// same string.
    ///
    /// NOT an index. There is no table and no ordering, which is the whole
    /// reason the classname string can be replaced by four bytes without
    /// introducing a second thing the two sides must agree the ordering of.
    ///
    /// Nothing on the wire uses this yet — <c>m_szClassname</c> is still a
    /// 64-byte string prop. It is computed here so the inspector can key a type
    /// by the identity it is about to be given, and so the C++ half that Step 6
    /// adds has a fixed corpus to be pinned against.
    /// </summary>
    public static uint TypeIdOf(string className)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime       = 16777619u;

        uint hash = offsetBasis;

        foreach (char c in className)
        {
            hash ^= (byte)c;
            hash *= prime;
        }

        return hash;
    }

    /// <summary>
    /// A fingerprint of a type's exact layout: every field's name, wire type,
    /// offset and storage size, in order, then the total.
    ///
    /// DELIBERATELY THE SAME FUNCTION as <see cref="EntityLayout.Fingerprint"/>,
    /// mixed in the same order over the same values — which is checked, not
    /// asserted, in the tests. That matters because the fingerprint is already
    /// exchanged at join, so a schema hash that disagreed with it would be a
    /// second opinion about whether two builds match, and two opinions is one
    /// too many.
    /// </summary>
    public static uint SchemaHashOf(IReadOnlyList<NetSchemaField> fields, int size)
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

        foreach (var field in fields)
        {
            MixText(field.Name);
            Mix((uint)WireTypeCodeOf(field.Kind));
            Mix((uint)field.Offset);
            Mix((uint)field.Bytes);
            Mix(PlacementOf(field.BitShift, field.Bits));

            // AN ENUM'S VALUES, AND ONLY ITS VALUES. Same numbers, same order,
            // same position in the mix as EntityLayout.Fingerprint — the two
            // have to stay ONE function over the same values, and a test holds
            // them equal over a layout with enums in it. What is and is not
            // covered, and the one thing a sorted set cannot catch, is written
            // out in NetEnums.HashValuesOf. Nothing is mixed for a field that is
            // not an enum, so no hash this tree already publishes moves.
            if (field.EnumType is not null)
            {
                foreach (long value in NetEnums.HashValuesOf(field.EnumType))
                {
                    Mix(unchecked((uint)value));
                    Mix(unchecked((uint)(value >> 32)));
                }
            }
        }

        Mix((uint)size);
        return hash;
    }

    /// <summary>
    /// Where a field sits inside its storage and how wide it is, as one mixed
    /// value.
    ///
    /// <para>
    /// ADDED BY PACKING, and it closes a hole that predates it. Offset and Bytes
    /// alone stopped identifying a field the moment two of them could share a
    /// word: <c>Wide</c> and <c>High</c> both live at offset 0 across four bytes
    /// and differ only by their shift, so a build that swapped them would have
    /// hashed identically and read each other's bits.
    /// </para>
    ///
    /// <para>
    /// THE WIDTH GOES IN TOO, and that was already missing. <c>Bits</c> has never
    /// been mixed, so narrowing a field from eight bits to two changed what the
    /// two sides stored and left the fingerprint alone. It did not matter while
    /// Bits narrowed the wire only and storage stayed the type's; it matters now
    /// that Bits decides the storage, and it was worth covering either way — the
    /// last narrow field in a word can change width without moving anything else.
    /// </para>
    ///
    /// <para>
    /// One value rather than two so that
    /// <see cref="EntityLayout.Fingerprint"/> — which must stay the same function
    /// over the same values, and is checked to be — gains one mix and not two.
    /// </para>
    /// </summary>
    public static uint PlacementOf(int bitShift, int bits)
        => ((uint)bitShift << 16) | ((uint)bits & 0xFFFFu);

    /// <summary>
    /// The wire type code native receives for a kind, matching
    /// what <c>SourceSharpFieldType</c> was before Step 7 deleted it: the
    /// numbers the layout fingerprint is computed over.
    ///
    /// The same mapping <see cref="StandaloneNetStates.TypeCodeOf"/> uses, and
    /// it agrees with <see cref="NetworkedLayout.TypeCodeOf"/> on the four kinds
    /// both can express — which is why one hash function covers both shells.
    /// </summary>
    public static int WireTypeCodeOf(NetFieldKind kind) => StandaloneNetStates.TypeCodeOf(kind);

    /// <summary>
    /// How wide a field really is on the wire, once native has filled in what
    /// the declaration left at zero.
    ///
    /// The generated layout carries a bit count only where the author narrowed
    /// one; a vector, a float and a bool all arrive as zero and native decides.
    /// It decides the same way every time — a bool is
    /// <c>SendPropInt(..., 1, 1, SPROP_UNSIGNED)</c> whatever its storage, a
    /// vector's width is PER COMPONENT and defaults to 32, a string's prop
    /// length is its buffer — so reporting the raw zero would be reporting
    /// something no prop was ever built with. Mirrored here because the
    /// inspector's job is to say what native was told, and "0 bits" is the one
    /// answer that is certainly wrong.
    /// </summary>
    public static int EffectiveBits(NetFieldKind kind, int declaredBits, int bytes)
    {
        if (kind == NetFieldKind.Bool)
            return 1;

        if (declaredBits > 0)
            return declaredBits;

        return kind == NetFieldKind.String ? bytes : 32;
    }

    /// <summary>
    /// How native sizes a class's blob: the end of its last field, rounded up to
    /// sixteen.
    ///
    /// The arithmetic <c>SourceSharp_RuntimeBlobBytes</c> used to repeat on the
    /// far side of the ABI, and now has one copy. Reported rather than assumed
    /// because it is NOT the layout size — the layout is aligned to four — and a
    /// reader looking at a hex dump that is longer than the layout should be told
    /// why rather than left to wonder whether the extra bytes mean something.
    /// </summary>
    public static int BlobBytesFor(IReadOnlyList<NetSchemaField> fields)
    {
        int end = 0;

        foreach (var field in fields)
            end = Math.Max(end, field.Offset + field.Bytes);

        return (end + 15) / 16 * 16;
    }

    //-------------------------------------------------------------------------
    // Building the description from what each shell was actually told.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One <c>[StandaloneNetState]</c> class, as the inspector sees it.
    ///
    /// The field list is the generated one — the same list
    /// <see cref="StandaloneNetStates.PublishAll"/> hands to native, in the same
    /// order — so the offsets here are the offsets the props were built on and
    /// not a second computation of them.
    /// </summary>
    /// <param name="tiers">
    /// The tier table to read. Threaded in rather than reached for as a static,
    /// because it is state and a static one is state every caller shares — see
    /// <see cref="INetTiers"/>.
    /// </param>
    public static NetSchemaType FromStandalone(INetTiers tiers, StandaloneNetState state,
                                               bool published)
    {
        var fields = new List<NetSchemaField>(state.Fields.Count);

        foreach (var field in state.Fields)
        {
            fields.Add(new NetSchemaField(
                // WHAT THE AUTHOR WROTE, where the layout knows it — which since
                // the generator started carrying it is every generated class.
                // CsNameOf is the last resort and is only ever a guess: it reads
                // the kind and the BYTE COUNT, and a packed field's byte count
                // is its containing word's, so it answered `uint` for all four
                // of spotlight_end's `byte` channels and for every bool sharing
                // a word. The enum name stays ahead of it for a hand-built
                // layout, which has an EnumType and no CsType.
                field.Name,
                First(field.CsType, field.EnumType?.Name, CsNameOf(field.Kind, field.Bytes)),
                field.Kind,
                field.Offset, field.Bytes, EffectiveBits(field.Kind, field.Bits, field.Bytes),
                field.BitShift, field.EnumType));
        }

        // FROM THIS CLASS'S OWN SIZE (Part 18), where it was the hierarchy's.
        //
        // The blob is still rounded UP to a tier — that is what a tier is, and
        // it is why this reads the tier table rather than reporting state.Size —
        // but WHICH tier is now this class's business. Every entity on the
        // hierarchy used to be allocated the greediest declaration's blob,
        // because operator new is a static function with no idea which class the
        // factory is about to make; the factory sizes each entity to its own
        // class now, so a twenty-byte type is carried on 32 and not on 256.
        //
        // state.Size is the same number native is handed through PublishOne, so
        // both sides reach the tier by one rule from one number. Computing it
        // from `fields` here instead would be a second arithmetic that could
        // round differently — BlobBytesFor rounds to sixteen and a layout is
        // aligned to four — and the two answers would be a schema panel
        // disagreeing with the wire about which table an entity is on.
        //
        // With no engine to ask, the declaration is the floor and is reported as
        // such: it is what a build WOULD carry, and the alternative is reporting
        // nothing about a schema the caller can see declared.
        var tier = TierCarrying(tiers, NetShellKind.Standalone, state.Size);

        int blobBytes    = tier?.PayloadBytes ?? state.Size;
        int payloadWords = tier?.Words ?? ( state.Size / 4 );
        int props        = tier?.Props ?? ( StandaloneSharedProps + payloadWords );

        return new NetSchemaType(
            state.ClassName, state.Type.Name, NetSchemaShell.Standalone,
            fields, state.Size, props, published, blobBytes, payloadWords, tiers);
    }

    /// <summary>
    /// The runtime-networked entity's layout, whose whole table is generated
    /// from C# metadata with no shared props in front of it.
    /// </summary>
    /// <param name="tiers">
    /// The tier table to read. Threaded in rather than reached for as a static,
    /// because it is state and a static one is state every caller shares — see
    /// <see cref="INetTiers"/>.
    /// </param>
    public static NetSchemaType FromLayout(INetTiers tiers, EntityLayout layout, bool published)
    {
        var fields = new List<NetSchemaField>(layout.Fields.Count);

        foreach (var field in layout.Fields)
        {
            NetFieldKind kind = KindOf(field.Type);

            // The width rule lives on EntityFieldLayout now, so this reports
            // what native was told and the fingerprint mixes the same number,
            // from one copy. A bool costs one bit whatever its storage, and a
            // vector's width is PER COMPONENT.
            fields.Add(new NetSchemaField(
                field.Name, CsNameOfType(field.Type), kind,
                field.Offset, field.Size, field.Bits, BitShift: 0,
                EnumType: field.Type.IsEnum ? field.Type : null));
        }

        // The same correction as the hierarchy's: this shell's blob is rounded up
        // to its tier too, and its table is the tier's three real props plus the
        // tier's payload words — not one prop per declared field, which is what
        // this counted while the tables were per class.
        var tier = TierCarrying(tiers, NetShellKind.NetEntity, BlobBytesFor(fields));

        return new NetSchemaType(
            layout.ClassName, layout.ClassName, NetSchemaShell.NetEntity,
            fields, layout.Size,
            tier?.Props ?? fields.Count, published,
            tier?.PayloadBytes ?? BlobBytesFor(fields), tier?.Words ?? 0, tiers);
    }

    /// <summary>
    /// The tier native registered that a blob of this size is carried on, or
    /// null when there is no engine to ask.
    ///
    /// A READING and not a computation. The tier a size lands in is decided by
    /// one rule in generic_nettable.h; what is looked up here is which of the
    /// registered rows that answer names, so the prop count and the word count
    /// come back as native built them rather than as a formula that is right for
    /// one shell and wrong for the other.
    /// </summary>
    /// <param name="tiers">
    /// The tier table to read. Threaded in rather than reached for as a static,
    /// because it is state and a static one is state every caller shares — see
    /// <see cref="INetTiers"/>.
    /// </param>
    public static NetTierInfo? TierCarrying(INetTiers tiers, NetShellKind shell, int blobBytes)
    {
        foreach (var tier in tiers.For(shell))
        {
            if (blobBytes <= tier.PayloadBytes)
                return tier;
        }

        return null;
    }

    /// <summary>
    /// Every type either shell networks, in a stable order: the generated shell
    /// first, then the hierarchy classes by name.
    ///
    /// <paramref name="published"/> answers whether a class was actually put on
    /// the wire. A state may be declared before its entity class has been moved
    /// onto the hierarchy, and that one is described here and networks nothing —
    /// which is worth SAYING rather than hiding, because "the field is declared
    /// and the client never sees it" is otherwise indistinguishable from a
    /// decode bug.
    /// </summary>
    /// <param name="tiers">
    /// The tier table to read. Threaded in rather than reached for as a static,
    /// because it is state and a static one is state every caller shares — see
    /// <see cref="INetTiers"/>.
    /// </param>
    public static IReadOnlyList<NetSchemaType> Describe(
        INetTiers tiers,
        IEnumerable<EntityLayout> layouts,
        IEnumerable<StandaloneNetState> states,
        Func<string, bool>? published = null)
    {
        var types = new List<NetSchemaType>();

        foreach (var layout in layouts)
            types.Add(FromLayout(tiers, layout, published?.Invoke(layout.ClassName) ?? true));

        // NO SHARED LENGTH SINCE PART 18. This used to compute the hierarchy's
        // greediest declaration and describe every standalone type against it,
        // because that was the blob native reserved for all of them. Each class
        // is sized to its own tier now, so a type is described against its own
        // declaration and there is no number to carry across the loop.
        //
        // HierarchyBlobBytes still exists and is still what NetHierarchyDeclare
        // sends: native uses it as a ceiling check — does the greediest class
        // still fit any tier — which is a different question from what any one
        // type is carried on.
        foreach (var state in states.OrderBy(s => s.ClassName, StringComparer.Ordinal))
        {
            types.Add(FromStandalone(tiers, state, published?.Invoke(state.ClassName) ?? true));
        }

        return types;
    }

    /// <summary>
    /// The type a name or an id refers to, or null.
    ///
    /// Both spellings, because the route is <c>/nettable/&lt;typeid&gt;</c> and
    /// the CLI verb in the plan is <c>./debug-api nettable prop_physics</c>. A
    /// classname is what a person types and a type id is what the wire will
    /// carry, and refusing either would make the tool answer a different
    /// question from the one it was asked.
    /// </summary>
    public static NetSchemaType? Find(IReadOnlyList<NetSchemaType> types, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        key = key.Trim();

        foreach (var type in types)
        {
            if (string.Equals(type.ClassName, key, StringComparison.OrdinalIgnoreCase))
                return type;
        }

        if (TryParseTypeId(key, out uint id))
        {
            foreach (var type in types)
            {
                if (type.TypeId == id)
                    return type;
            }
        }

        return null;
    }

    /// <summary>Decimal, or hex with or without the 0x.</summary>
    public static bool TryParseTypeId(string text, out uint id)
    {
        text = text.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);

        if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            return true;

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    //-------------------------------------------------------------------------
    // Reading the bytes back.
    //
    // The half that earns the file. Everything above describes; this is what
    // says the description is TRUE of a particular entity, and it is the piece
    // that has to be right for the inspector to be worth having at all.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Decodes a blob field by field through a type's layout.
    ///
    /// Fails per field rather than as a whole: a blob shorter than the layout is
    /// a real state — native sizes the hierarchy's blob for the greediest class,
    /// so a short one means something is wrong — and reporting which fields fell
    /// off the end says far more than refusing the whole read. A field that does
    /// not fit is returned <see cref="NetDecodedField.Present"/> false with no
    /// value invented for it.
    /// </summary>
    public static IReadOnlyList<NetDecodedField> Decode(NetSchemaType type, ReadOnlySpan<byte> blob)
    {
        var decoded = new List<NetDecodedField>(type.Fields.Count);

        // Room to lift a packed field out of the word it shares. On the heap
        // rather than the stack because it is handed to the formatters as a
        // span alongside spans over the caller's blob, and a stack one cannot
        // outlive this frame in a way the compiler can prove. One allocation per
        // inspector call, on a route that already builds a list and a string per
        // field.
        byte[] lifted = new byte[4];

        foreach (var field in type.Fields)
        {
            if (field.Offset < 0 || field.Bytes <= 0 || field.Offset + field.Bytes > blob.Length)
            {
                decoded.Add(new NetDecodedField(field, Present: false, "", "", null));
                continue;
            }

            ReadOnlySpan<byte> bytes = blob.Slice(field.Offset, field.Bytes);

            // A PACKED FIELD IS NOT ITS STORAGE (Part 10, Step 8). Its offset
            // names the word it shares, so decoding that word whole would report
            // its neighbours' bits as part of its value — which is exactly the
            // failure this route exists to catch in others. Lift the field's own
            // bits out first, and the formatters need to know nothing about
            // packing.
            //
            // THE HEX STAYS THE STORAGE, deliberately, and it is the one column
            // that should. It is there to be checked by eye against a ruler, so
            // for a packed field the useful thing to show is the word — the same
            // four bytes beside each of the fields sharing it, which is what
            // says they share it. The value column is where the field's own bits
            // belong, and it now has them.
            ReadOnlySpan<byte> value = bytes;

            if (field.IsPacked)
            {
                Unpack(field, bytes, lifted);
                value = lifted;
            }

            decoded.Add(new NetDecodedField(field, Present: true, Format(field, value), Hex(bytes),
                                            AsNumber(field, value)));
        }

        return decoded;
    }

    /// <summary>
    /// One packed field's own bits, as four bytes holding its value.
    ///
    /// Sign extended for a signed field, which is the half that is silent when
    /// it goes wrong: a wrongly-extended negative is a plausible number and a
    /// wrongly-masked one is a huge positive, and neither reports anything. Same
    /// arithmetic as <c>NetBlobView.ReadBitsSigned</c>, and pinned against it in
    /// the tests rather than trusted to two readings of the same rule.
    /// </summary>
    private static void Unpack(in NetSchemaField field, ReadOnlySpan<byte> word, Span<byte> into)
    {
        uint bits = MemoryMarshal.Read<uint>(word);

        int value = field.Kind == NetFieldKind.Int
            ? (int)(bits << (32 - field.BitShift - field.Bits)) >> (32 - field.Bits)
            : (int)((bits >> field.BitShift)
                    & (field.Bits >= 32 ? uint.MaxValue : (1u << field.Bits) - 1u));

        BitConverter.TryWriteBytes(into, value);
    }

    /// <summary>
    /// One field's value as text, in the C# spelling of its type.
    ///
    /// Invariant culture everywhere. This is read by a tool and compared against
    /// a console dump, and a decimal comma would be a difference that means
    /// nothing and reads like one that means something.
    /// </summary>
    private static string Format(in NetSchemaField field, ReadOnlySpan<byte> bytes)
    {
        // THROUGH THE MEMBER NAMES (plan_nettable_generic.md Step 11). Part 13's
        // whole argument is that these routes replace the readability the
        // generic payload removed, and a field decoding to "3" rather than
        // "Snowfall" is that loss arriving by another door. NetEnums.Format
        // never throws and never invents a member: a value nothing names prints
        // as its number and says so.
        //
        // NAMES ARE READ HERE AND ARE NOT IN THE SCHEMA HASH, which is worth
        // saying plainly because it looks like an oversight and is not. The hash
        // covers what changes DECODING; a rename changes not one bit on the
        // wire, so refusing a client over one would be a guard people route
        // around. The numbers behind these names ARE hashed, which is what makes
        // the name printed here the name the sending build meant. See
        // NetEnums.HashValuesOf.
        if (field.EnumType is not null)
        {
            return NetEnums.Format(field.EnumType,
                                   field.Kind == NetFieldKind.UInt
                                       ? unchecked((long)ReadUnsigned(bytes))
                                       : ReadSigned(bytes));
        }

        return FormatPlain(field, bytes);
    }

    private static string FormatPlain(in NetSchemaField field, ReadOnlySpan<byte> bytes) => field.Kind switch
    {
        NetFieldKind.Vector => FormatVector(bytes),

        // SPELLED DIFFERENTLY THOUGH THE BYTES ARE A VECTOR'S, because that is
        // the entire value of the kind in this view: a reader looking at three
        // numbers cannot tell a position from a facing, and this column is where
        // they find out which one they are looking at.
        NetFieldKind.QAngle  => FormatAngle(bytes),
        NetFieldKind.Color32 => FormatColor(bytes),

        NetFieldKind.Float  => FormatFloat(bytes),
        NetFieldKind.Bool   => AnyNonZero(bytes) ? "true" : "false",
        NetFieldKind.String => ReadString(bytes),
        NetFieldKind.UInt   => ReadUnsigned(bytes).ToString(CultureInfo.InvariantCulture),
        _                   => ReadSigned(bytes).ToString(CultureInfo.InvariantCulture),
    };

    private static string FormatVector(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
            return "";

        float x = MemoryMarshal.Read<float>(bytes);
        float y = MemoryMarshal.Read<float>(bytes[4..]);
        float z = MemoryMarshal.Read<float>(bytes[8..]);

        return $"({N(x)}, {N(y)}, {N(z)})";
    }

    /// <summary>
    /// The same three floats, labelled as the angles they are.
    ///
    /// Pitch/yaw/roll rather than x/y/z. Same arithmetic as
    /// <see cref="FormatVector"/> and deliberately not a call to it: what is
    /// different is the only thing this kind exists for, and folding the two
    /// would put the difference back in a comment.
    /// </summary>
    private static string FormatAngle(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
            return "";

        float pitch = MemoryMarshal.Read<float>(bytes);
        float yaw   = MemoryMarshal.Read<float>(bytes[4..]);
        float roll  = MemoryMarshal.Read<float>(bytes[8..]);

        return $"({N(pitch)}p, {N(yaw)}y, {N(roll)}r)";
    }

    /// <summary>
    /// A colour's four channels out of the one word they pack into, R first.
    ///
    /// The same order <c>NetBlobView.ReadColor32</c> uses, because it is the
    /// same bytes — and this is the view somebody checks that order against when
    /// a colour comes out wrong.
    /// </summary>
    private static string FormatColor(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
            return "";

        uint word = MemoryMarshal.Read<uint>(bytes);

        return $"rgba({word & 0xFF}, {(word >> 8) & 0xFF}, " +
               $"{(word >> 16) & 0xFF}, {(word >> 24) & 0xFF})";
    }

    private static string FormatFloat(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 8 ? N(MemoryMarshal.Read<double>(bytes))
         : bytes.Length >= 4 ? N(MemoryMarshal.Read<float>(bytes))
         : "";

    /// <summary>
    /// A field as a number, where being one means anything.
    ///
    /// Null for a vector and a string: a view that plots or diffs numbers should
    /// skip those rather than be handed a zero that looks like a measurement.
    ///
    /// An enum reports its INTEGER here and its member name in the value column.
    /// Both are wanted and they are wanted by different readers: a name is what
    /// a person checks, and a number is what a diff can subtract.
    /// </summary>
    private static double? AsNumber(in NetSchemaField field, ReadOnlySpan<byte> bytes) => field.Kind switch
    {
        NetFieldKind.Vector => null,
        NetFieldKind.String => null,

        // Null for the reason a vector is: these are composites, and a view that
        // plots or subtracts numbers should skip them rather than be handed one
        // component, or a packed word read as an integer, as though it measured
        // the whole.
        NetFieldKind.QAngle  => null,
        NetFieldKind.Color32 => null,

        NetFieldKind.Float  => bytes.Length >= 8 ? MemoryMarshal.Read<double>(bytes)
                             : bytes.Length >= 4 ? MemoryMarshal.Read<float>(bytes)
                             : null,
        NetFieldKind.Bool   => AnyNonZero(bytes) ? 1 : 0,
        NetFieldKind.UInt   => ReadUnsigned(bytes),
        _                   => ReadSigned(bytes),
    };

    /// <summary>
    /// A bool is one byte of storage and one bit on the wire, and native sends
    /// <c>SendPropInt(..., 1, 1, SPROP_UNSIGNED)</c> over the whole byte — so
    /// any non-zero byte is true, which is what the engine will encode.
    /// </summary>
    private static bool AnyNonZero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (b != 0)
                return true;
        }

        return false;
    }

    private static long ReadSigned(ReadOnlySpan<byte> bytes) => bytes.Length switch
    {
        1 => (sbyte)bytes[0],
        2 => MemoryMarshal.Read<short>(bytes),
        4 => MemoryMarshal.Read<int>(bytes),
        8 => MemoryMarshal.Read<long>(bytes),
        _ => 0,
    };

    private static ulong ReadUnsigned(ReadOnlySpan<byte> bytes) => bytes.Length switch
    {
        1 => bytes[0],
        2 => MemoryMarshal.Read<ushort>(bytes),
        4 => MemoryMarshal.Read<uint>(bytes),
        8 => MemoryMarshal.Read<ulong>(bytes),
        _ => 0,
    };

    /// <summary>
    /// To the terminator, exactly as <see cref="NetState.ReadString"/> does and
    /// as the engine's recv proxy does — a buffer with no NUL is read whole.
    /// </summary>
    private static string ReadString(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>Lowercase, no separators. What a hex ruler is read against.</summary>
    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length * 2);

        foreach (byte b in bytes)
            text.Append(b.ToString("x2", CultureInfo.InvariantCulture));

        return text.ToString();
    }

    /// <summary>
    /// The byte runs that differ between two samples of the same blob.
    ///
    /// Runs rather than a flag per byte, because this is what the delta actually
    /// is: the payload design of Part 3 makes each dword its own prop, so the
    /// bytes that differ are precisely the bytes that go on the wire. Today it
    /// is only a diff over the blob, and that is enough for the inspector to say
    /// "this field moved" — which is the thing an offset bug shows up as, since
    /// a field reading the wrong bytes changes when its NEIGHBOUR is written.
    ///
    /// A length change ends the comparison at the shorter length rather than
    /// pretending the tail changed: the two samples are then not of the same
    /// shape and saying so is the caller's job.
    /// </summary>
    public static IReadOnlyList<(int Offset, int Length)> ChangedRuns(
        ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current)
    {
        var runs = new List<(int, int)>();
        int shared = Math.Min(previous.Length, current.Length);
        int start = -1;

        for (int i = 0; i < shared; i++)
        {
            bool differs = previous[i] != current[i];

            if (differs && start < 0)
                start = i;
            else if (!differs && start >= 0)
            {
                runs.Add((start, i - start));
                start = -1;
            }
        }

        if (start >= 0)
            runs.Add((start, shared - start));

        return runs;
    }

    /// <summary>Whether any byte of a field's storage falls inside a changed run.</summary>
    public static bool Touches(in NetSchemaField field, IReadOnlyList<(int Offset, int Length)> runs)
    {
        foreach (var (offset, length) in runs)
        {
            if (offset < field.Offset + field.Bytes && field.Offset < offset + length)
                return true;
        }

        return false;
    }

    //-------------------------------------------------------------------------
    // Naming things the way they were written.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The C# spelling of a kind at a storage width.
    ///
    /// A guess in one direction only: a <c>NetFieldKind.Int</c> of four bytes is
    /// an <c>int</c>, and there is no way back from the kind to know it was
    /// declared as something else. Good enough for a label and deliberately not
    /// used for anything that decides how bytes are read.
    /// </summary>
    /// <summary>
    /// The first of these that says something. Named rather than written as a
    /// chain of <c>??</c> because the empty string is the "nobody said" here and
    /// null-coalescing would have taken it as an answer.
    /// </summary>
    private static string First(string? carried, string? enumName, string guessed)
        => !string.IsNullOrEmpty(carried) ? carried
         : !string.IsNullOrEmpty(enumName) ? enumName
         : guessed;

    private static string CsNameOf(NetFieldKind kind, int bytes) => kind switch
    {
        NetFieldKind.Vector  => "Vector",
        NetFieldKind.QAngle  => "QAngle",
        NetFieldKind.Color32 => "Color32",
        NetFieldKind.Float  => bytes >= 8 ? "double" : "float",
        NetFieldKind.Bool   => "bool",
        NetFieldKind.String => "string",
        NetFieldKind.UInt   => bytes switch { 1 => "byte", 2 => "ushort", 8 => "ulong", _ => "uint" },
        _                   => bytes switch { 1 => "sbyte", 2 => "short", 8 => "long", _ => "int" },
    };

    private static string CsNameOfType(Type type)
        => type == typeof(bool)   ? "bool"
         : type == typeof(byte)   ? "byte"
         : type == typeof(sbyte)  ? "sbyte"
         : type == typeof(short)  ? "short"
         : type == typeof(ushort) ? "ushort"
         : type == typeof(int)    ? "int"
         : type == typeof(uint)   ? "uint"
         : type == typeof(long)   ? "long"
         : type == typeof(ulong)  ? "ulong"
         : type == typeof(float)  ? "float"
         : type == typeof(double) ? "double"
         // An enum falls through to its own name, which is the answer: a reader
         // wants "PrecipitationType", not the int it is stored as.
         : type.Name;

    /// <summary>
    /// Which wire kind an <c>[EntityField]</c>'s C# type maps to.
    ///
    /// <para>
    /// THE ONE ANSWER, AND IT DID NOT USED TO BE. This was private and
    /// <see cref="NetworkedLayout.TypeCodeOf"/> had its own fold — every
    /// integral type to "int" — with a comment here claiming "the two still
    /// agree on the type CODE, which is what the schema hash mixes". They did
    /// not: <c>NetFieldKind.UInt</c> is code 5 and that fold answered 0, so an
    /// <c>[EntityField] byte</c> would have hashed one way in
    /// <see cref="EntityLayout.Fingerprint"/> and another in
    /// <see cref="SchemaHashOf"/> — two opinions about whether two builds match,
    /// which is the exact thing both functions exist to prevent. It never fired
    /// because the shell happens to declare only int, float, Vector and bool
    /// today. Found by Step 11, which added the first test to compare the two
    /// hashes over a layout with an unsigned field in it.
    /// </para>
    /// </summary>
    public static NetFieldKind KindOf(Type declared)
    {
        // An enum is its underlying integer here as everywhere else, so its
        // SIGNEDNESS follows the backing type the author chose and a
        // byte-backed enum reads back unsigned.
        Type type = NetEnums.Underlying(declared);

        // THE TWO NEW KINDS ANSWER FIRST, and answering at all is the point.
        // Neither can reach a layout on this shell — EntityLayout.SizeOf has no
        // size for either and refuses the declaration by name — but this is a
        // public type-to-kind function whose fold ENDS in NetFieldKind.Int, so
        // without these a caller asking about a QAngle is told it is a signed
        // integer, which is the shape of wrong answer this file exists to stop.
        return type == typeof(QAngle) ? NetFieldKind.QAngle
             : type == typeof(Color32) ? NetFieldKind.Color32
             : type == typeof(Vector) ? NetFieldKind.Vector
             : type == typeof(float) || type == typeof(double) ? NetFieldKind.Float
             : type == typeof(bool) ? NetFieldKind.Bool
             : type == typeof(byte) || type == typeof(ushort)
               || type == typeof(uint) || type == typeof(ulong) ? NetFieldKind.UInt
             : NetFieldKind.Int;
    }

    private static string N(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>Which native shell carries a type's table.</summary>
public enum NetSchemaShell
{
    /// <summary>The runtime-networked entity, whose whole table is generated.</summary>
    NetEntity = 0,

    /// <summary>The parallel hierarchy: four shared props, then the class's own.</summary>
    Standalone = 1,
}

/// <summary>
/// One networked field as the inspector reports it: where it is, how wide it is
/// in storage, and how much of it goes on the wire.
///
/// <see cref="Bytes"/> and <see cref="Bits"/> are genuinely different numbers
/// and both have to travel — a short narrowed to ten bits still occupies two
/// bytes, and every offset after it depends on the two.
/// </summary>
/// <param name="EnumType">
/// The enum this field was declared as, or null. Carried so that the schema
/// hash can cover the member NUMBERS and the inspector can decode through the
/// member NAMES; the wire knows nothing about it, since an enum is sent as its
/// underlying integer (plan_nettable_generic.md Step 11).
/// </param>
public readonly record struct NetSchemaField(
    string Name, string CsType, NetFieldKind Kind, int Offset, int Bytes, int Bits,
    int BitShift = 0, Type? EnumType = null)
{
    /// <summary>
    /// Whether this field shares its storage with another. See
    /// <see cref="NetFieldLayout.IsPacked"/>, which is the same rule — this
    /// struct is the inspector's view of that one.
    /// </summary>
    public bool IsPacked
        => Kind is NetFieldKind.Int or NetFieldKind.UInt or NetFieldKind.Bool
           && (BitShift != 0 || Bits < Bytes * 8);
}

/// <summary>
/// One C# type's networked schema: its identity, its size, and its fields.
/// </summary>
public sealed class NetSchemaType
{
    /// <param name="tiers">
    /// The tier table, which decides the DT_ name a blob of this size is carried
    /// on. Optional, and null means "no engine registered any": the type then
    /// reports its own DT_&lt;classname&gt;, which is what a build with no tiers
    /// genuinely has. A static tier table used to answer this, which made the
    /// name depend on whatever a previous test had left behind.
    /// </param>
    public NetSchemaType(string className, string typeName, NetSchemaShell shell,
                         IReadOnlyList<NetSchemaField> fields, int layoutBytes,
                         int propCount, bool published, int carriedBlobBytes = 0,
                         int payloadWords = 0,
                         INetTiers? tiers = null)
    {
        ClassName  = className;
        TypeName   = typeName;
        Shell      = shell;
        Fields     = fields;
        LayoutBytes = layoutBytes;
        PropCount  = propCount;
        Published  = published;
        PayloadWords = payloadWords;

        TypeId     = NetSchema.TypeIdOf(className);
        SchemaHash = NetSchema.SchemaHashOf(fields, layoutBytes);

        // The class's own fields rounded to sixteen is a FLOOR, not the answer:
        // native reserves a whole TIER for an entity, so a twenty-byte layout
        // has thirty-two bytes of blob. Reporting the fields' own length would
        // make the inspector disagree with native about how many bytes an entity
        // has -- and the inspector is the oracle the later steps are checked
        // against, so it has to report what native reserves.
        //
        // carriedBlobBytes is THIS TYPE'S tier since Part 18, where it was the
        // whole hierarchy's. The floor stays, because with no tier table to read
        // there is nothing else to report.
        BlobBytes  = Math.Max(NetSchema.BlobBytesFor(fields), carriedBlobBytes);

        // AFTER BlobBytes, because the table is the one carrying a blob that
        // size. A class has no table of its own since Step 6 — its entities are
        // created through a tier — so reporting DT_<classname> would be naming
        // something that does not exist.
        TableName  = tiers is null
            ? NetSchema.TableNameOf(className)
            : NetSchema.TableForType(
                tiers,
                shell == NetSchemaShell.Standalone ? NetShellKind.Standalone : NetShellKind.NetEntity,
                BlobBytes, className);
    }

    /// <summary>The classname the two tables are keyed by.</summary>
    public string ClassName { get; }

    /// <summary>The C# type that declared the fields, for finding it in source.</summary>
    public string TypeName { get; }

    public NetSchemaShell Shell { get; }

    /// <summary>The DT_ name the connect handshake pairs on.</summary>
    public string TableName { get; }

    /// <summary>FNV-1a of the classname. See <see cref="NetSchema.TypeIdOf"/>.</summary>
    public uint TypeId { get; }

    /// <summary>Fingerprint of the exact layout. See <see cref="NetSchema.SchemaHashOf"/>.</summary>
    public uint SchemaHash { get; }

    /// <summary>Bytes the generated layout occupies, aligned to four.</summary>
    public int LayoutBytes { get; }

    /// <summary>Bytes native reserves for the class, aligned to sixteen.</summary>
    public int BlobBytes { get; }

    /// <summary>
    /// Props in the built table: the shared ones, the class's own, and the
    /// generic payload words.
    /// </summary>
    public int PropCount { get; }

    /// <summary>
    /// Generic payload words on the built table (Part 14 Steps 3 and 4).
    ///
    /// On the parallel hierarchy this is ONE number for every class, because
    /// the blob it covers is one length for every entity there. Zero for a
    /// class whose table carries no payload.
    /// </summary>
    public int PayloadWords { get; }

    /// <summary>Whether this side actually put the class on the wire.</summary>
    public bool Published { get; }

    public IReadOnlyList<NetSchemaField> Fields { get; }
}

/// <summary>One field read out of a live blob.</summary>
/// <param name="Present">
/// False when the blob was too short to hold the field. No value is invented for
/// one that fell off the end: a fabricated zero is a lie the reader would act on.
/// </param>
public readonly record struct NetDecodedField(
    NetSchemaField Field, bool Present, string Value, string Hex, double? Number);

/// <summary>
/// One live entity's bytes, lifted off the game thread.
///
/// A COPY, and it has to be: <see cref="EngineSide.GetEntityBlob"/> hands back
/// borrowed native memory whose lifetime is the entity's, and this is read by a
/// request that outlives the frame it was taken on.
/// </summary>
/// <param name="Agreement">
/// What the server says about this entity's two paths while the payload
/// migration runs. Defaulted, so a caller that has no game to ask reports
/// "unsupported" rather than inventing an answer.
/// </param>
public readonly record struct NetEntityBlob(
    bool Found, int Index, uint Handle, string ClassName, byte[] Blob,
    PayloadAgreement Agreement = default)
{
    public static NetEntityBlob Missing(int index)
        => new(false, index, 0, "", Array.Empty<byte>());
}

/// <summary>
/// The <see cref="PayloadAgreement"/> a side with no check answers, and what an
/// agreement says beyond its fields.
///
/// <para>
/// Extension methods rather than members since plan_shim_areas_split phase 4:
/// the agreement is in a seam's signature
/// (<c>IEngineNetworking.CheckPayloadAgreement</c>), so it lives with the seam
/// in an interface assembly, which holds only data.
/// </para>
/// </summary>
public static class PayloadAgreements
{
    /// <summary>The answer for an entity whose side supplied no payload check.</summary>
    public static readonly PayloadAgreement NotSupplied = new(PayloadAgreementState.NotSupplied, 0, -1);

    /// <summary>True only for <see cref="PayloadAgreementState.Agrees"/>.</summary>
    public static bool Agrees(this PayloadAgreement agreement) => agreement.State == PayloadAgreementState.Agrees;

    /// <summary>The state as the JSON and the CLI spell it.</summary>
    public static string Name(this PayloadAgreement agreement) => agreement.State switch
    {
        PayloadAgreementState.Agrees      => "agrees",
        PayloadAgreementState.Disagrees   => "disagrees",
        PayloadAgreementState.NoPayload   => "none",
        _                                 => "unsupported",
    };
}

/// <summary>
/// A type this build could not put on the wire, and why.
///
/// <para>
/// Two things produce one: a TYPE ID COLLISION — two classnames with the same
/// FNV-1a, where recording the second would leave it reachable by no lookup and
/// the first answering for both — and a layout too large for the biggest tier,
/// which is not a rounding problem but a type the design cannot network at all.
/// </para>
///
/// <para>
/// Both are build-time facts about a fixed set of declarations, so they are
/// known at startup and stay true. That is what makes them worth a banner: the
/// alternative is discovering at connect that one class's entities pair with
/// another class's managed half.
/// </para>
/// </summary>
public readonly record struct NetTypeRefusal(string ClassName, uint TypeId, int LayoutBytes);

/// <summary>
/// What this side could not publish. Filled at startup by the publish path and
/// read by the schema panel.
/// </summary>
public interface INetTypeRefusals
{
    /// <summary>Every refusal recorded, in the order they happened.</summary>
    IReadOnlyList<NetTypeRefusal> All { get; }

    /// <summary>Records one refusal; a classname already recorded is not recorded twice.</summary>
    void Record(string className, uint typeId, int layoutBytes);

    /// <summary>Forgets every refusal.</summary>
    void Forget();
}

/// <summary>
/// What this side could not publish. Filled at startup by the publish path and
/// read by the schema panel.
///
/// <para>
/// A LEDGER PER MODULE rather than a return value threaded through five call
/// sites, because the fact is about the BUILD rather than about any one call: a
/// collision is a property of the whole set of classnames, and the reader who
/// needs it is a panel that was not present when the publishing happened. Each
/// module registers one in its container and hands it to the publish path; the
/// server's reaches the dev API's schema panel through
/// <c>IDebugApi.ReadNetRefusals</c>. It was a static list, which in this assembly
/// (loaded once, shared by both sides) meant one list for the whole process.
/// </para>
/// </summary>
public sealed class NetTypeRefusals : INetTypeRefusals
{
    /// <inheritdoc/>
    public IReadOnlyList<NetTypeRefusal> All
    {
        get { lock (_lock) return _refused.ToArray(); }
    }

    public void Record(string className, uint typeId, int layoutBytes)
    {
        lock (_lock)
        {
            foreach (var already in _refused)
            {
                if (already.ClassName == className)
                    return;
            }

            _refused.Add(new NetTypeRefusal(className, typeId, layoutBytes));
        }
    }

    /// <summary>Drops what was recorded. A managed reload publishes again.</summary>
    public void Forget()
    {
        lock (_lock)
            _refused.Clear();
    }

    private readonly List<NetTypeRefusal> _refused = new();
    private readonly object _lock = new();
}

/// <summary>
/// Which shell hosts a type. Mirrors SourceSharpNetShell in generic_nettable.h,
/// and travels because the client cannot work it out: entity classes are
/// registered on the server, so the client has no list to check a name against.
/// </summary>
public enum NetShellKind
{
    /// <summary>
    /// RETIRED. The runtime-networked entity, whose whole table was generated.
    /// The shell is deleted; the value is RESERVED, exactly as
    /// <c>SS_SHELL_NETENTITY</c> is in generic_nettable.h — these numbers
    /// travel, so reusing 0 or renumbering <see cref="Standalone"/> onto it
    /// changes what one side means by a number the other reads. Native refuses
    /// a publish arriving on it, by name, on both sides.
    /// </summary>
    NetEntity = 0,

    /// <summary>The parallel hierarchy. The only shell.</summary>
    Standalone = 1,
}

/// <summary>
/// The size tiers of Part 7, READ FROM NATIVE.
///
/// <para>
/// This used to be a projection: its own copy of <c>{32, 256, 2048}</c> and its
/// own idea of a tier's prop count, flagged as a projection in the JSON
/// (<c>tiersLive:false</c>) and in the UI because nothing was registered against
/// it. Step 6 registered the tiers, and the note left on that step was explicit
/// that when it landed this had to become a reading rather than a second
/// implementation — two implementations of a boundary rule drift, and this one
/// drifts silently: a type assigned to a tier one side thinks is 256 bytes and
/// the other thinks is 32 has its tail simply absent from the wire.
/// </para>
///
/// <para>
/// SO THERE IS NO LIST HERE. <see cref="Sizes"/> asks native and caches what it
/// is told. With no engine — the test suite — the answer is EMPTY rather than a
/// guess, and every caller has to have an answer for that; a fallback list would
/// be the projection back again, wearing the word "fallback". What the tests pin
/// instead is the C++ boundary rule directly, in the gtest, which is where the
/// one implementation is.
/// </para>
/// </summary>
public sealed class NetTiers : INetTiers
{
    private readonly IEngineNetworking _net;

    /// <summary>
    /// The tier table for one load, read from the engine it was handed.
    ///
    /// <para>
    /// AN INSTANCE, not a static, and the tests are why. It cached what native
    /// said in a process-wide dictionary with <c>Forget</c> and
    /// <c>SupplyForTests</c> beside it, which made the table something any test
    /// could change for every other. Two turned out to be passing on a table a
    /// different suite had supplied while testing something else, and one on a
    /// table it needed EMPTY and never emptied. A table somebody owns cannot do
    /// that.
    /// </para>
    /// </summary>
    public NetTiers(IEngineNetworking net)
    {
        _net = net;
    }

    /// <summary>
    /// Every tier native registered for a shell, smallest first. Empty when
    /// there is no engine to ask.
    ///
    /// <para>
    /// Cached, because a tier list cannot change after DLL init — a ServerClass
    /// may only be constructed before the engine collects the class list — and
    /// walking the ABI per poll would be work done to reach an answer that was
    /// fixed at startup.
    /// </para>
    /// </summary>
    public IReadOnlyList<NetTierInfo> For(NetShellKind shell)
    {
        int key = (int)shell;

        lock (_lock)
        {
            if (_byShell.TryGetValue(key, out var cached))
                return cached;

            var found = new List<NetTierInfo>();

            for (int i = 0; ; i++)
            {
                var tier = _net.NetTierAt(key, i);

                if (tier is null)
                    break;

                found.Add(tier.Value);
            }

            // NOT cached when there was nothing to read. An engine that has not
            // finished starting would otherwise pin "no tiers" for the session,
            // and the UI would report a build with no tiers as though that were
            // a fact about the build.
            if (found.Count > 0)
                _byShell[key] = found;

            return found;
        }
    }

    private readonly Dictionary<int, IReadOnlyList<NetTierInfo>> _byShell = new();
    private readonly object _lock = new();

    /// <summary>Drops what was read, for a test that wants to read it again.</summary>
    public void Forget()
    {
        lock (_lock)
            _byShell.Clear();
    }

    /// <summary>
    /// Supplies what native WOULD have said. For tests, and named so that a
    /// grep for it in game code finds a mistake.
    ///
    /// <para>
    /// The tests exercise what this class DOES with a tier list — which tier a
    /// size lands in, how full it is, whether it is near its ceiling — and none
    /// of that needs a game. What they must not do is carry a second copy of
    /// the tier SIZES as though they were a fact about the design, because that
    /// is the projection Step 6 removed. So they hand in a list, exercise the
    /// rules over it, and the real sizes are pinned exactly once, in the gtest,
    /// against the one implementation in generic_nettable.h.
    /// </para>
    /// </summary>
    public void SupplyForTests(NetShellKind shell, IReadOnlyList<NetTierInfo> tiers)
    {
        lock (_lock)
            _byShell[(int)shell] = tiers;
    }

    /// <summary>
    /// The payload sizes native registered, or an empty list with no engine.
    ///
    /// The two shells register the same sizes — the boundary rule has one copy
    /// in generic_nettable.h — so this asks the hierarchy's and would be the
    /// same answer either way.
    /// </summary>
    public IReadOnlyList<int> Sizes
    {
        get
        {
            var tiers = For(NetShellKind.Standalone);
            var sizes = new int[tiers.Count];

            for (int i = 0; i < tiers.Count; i++)
                sizes[i] = tiers[i].PayloadBytes;

            return sizes;
        }
    }

    /// <summary>
    /// The smallest tier a layout fits in, or -1 — including when there is no
    /// engine to ask, which is a different fact with the same answer and the
    /// caller must treat both as "cannot say".
    /// </summary>
    public int Ceiling(int layoutBytes)
    {
        foreach (int size in Sizes)
        {
            if (layoutBytes <= size)
                return size;
        }

        return -1;
    }

    /// <summary>How much of its tier a layout uses, 0..1, or 1 when it fits none.</summary>
    public double Fill(int layoutBytes)
    {
        int ceiling = Ceiling(layoutBytes);
        return ceiling <= 0 ? 1.0 : (double)layoutBytes / ceiling;
    }

    /// <summary>
    /// Within a tenth of outgrowing its tier, which is what the schema panel
    /// flags. Part 13's number, kept here so the UI and the CLI agree on it.
    /// </summary>
    public bool NearCeiling(int layoutBytes) => Fill(layoutBytes) >= 0.9;
}
