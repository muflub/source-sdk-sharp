using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceSharp.Generators;

/// <summary>
/// Writes the body of every <c>[StandaloneNetState]</c> class.
///
/// <para>
/// WHAT THE AUTHOR WRITES is the shape of the state — a partial class naming the
/// entity it belongs to, and a partial property per networked value. WHAT THIS
/// WRITES is everything that has to agree with everything else: the backing
/// field the runtime layout is computed from, the accessors that write through
/// to the entity's blob, and the decode that fills them from a received one.
/// </para>
///
/// <para>
/// The point is not brevity. Every one of those pieces is a place where the two
/// sides can silently disagree — a field described but not written, a value
/// written but never decoded, an offset read from the wrong place — and each of
/// them was hand-written and hand-paired until now. A generated pair cannot fall
/// out of step with a declaration it was generated from.
/// </para>
///
/// <para>
/// IT ALSO COMPUTES THE LAYOUT — the kind, the width, the storage and the offset
/// of every field — and emits it as constants. That answer is fixed the moment
/// the declaration is written, so working it out by reflection on both sides
/// every time a state type was first touched was work done twice per process to
/// reach a compile-time constant.
/// </para>
///
/// <para>
/// Doing it here DELETED the runtime implementation rather than adding a second
/// one, and that distinction is the whole of why it is safe: two copies of this
/// arithmetic is precisely the drift the design exists to prevent, and the copy
/// nobody calls is the one that drifts. The rules live below and nowhere else.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class NetStateGenerator : IIncrementalGenerator
{
    private const string StateAttribute = "SourceSharp.Abi.StandaloneNetStateAttribute";

    /// <summary>
    /// The engine block, and the constant on it that says how wide it is
    /// (plan_entity.md §3.2). Read out of the compilation rather than repeated
    /// here: a number this generator carried of its own would be a third
    /// declaration of the block's size, and the whole design has exactly two.
    /// </summary>
    private const string BlockTypeName  = "SourceSharp.Abi.EngineBlock";
    private const string BlockSizeField = "SizeBytes";
    private const string FieldAttribute = "SourceSharp.Abi.NetFieldAttribute";
    private const string StringAttribute = "SourceSharp.Abi.NetStringAttribute";
    private const string ArrayAttribute  = "SourceSharp.Abi.NetArrayAttribute";
    private const string ArrayType       = "SourceSharp.Abi.NetArray<";

    /// <summary>One networked property, placed.</summary>
    private sealed record Property(
        string Name,
        string WireName,
        string TypeName,
        string Kind,
        int Bits,
        int Bytes,
        int Offset,

        /// <summary>Elements, or zero for a single value.</summary>
        int Length = 0,

        /// <summary>The element's C# type, for an array.</summary>
        string ElementTypeName = "",

        /// <summary>
        /// Which fields this one changes with. Empty is the default group, which
        /// is what a class that has never been tuned declares.
        /// </summary>
        string Group = "",

        /// <summary>
        /// How far into <see cref="Offset"/>'s dword the field's low bit sits.
        /// Zero for everything that occupies its storage whole.
        /// </summary>
        int BitShift = 0,

        /// <summary>
        /// Whether the field shares its dword and therefore needs a shift and a
        /// mask rather than a typed load.
        /// </summary>
        bool Packed = false,

        /// <summary>
        /// The integer an ENUM-typed property is stored and sent as, or empty
        /// when the declared type is already the storage. Every layout rule
        /// works on this; only the property's own signature and the table's
        /// <c>typeof</c> use <see cref="TypeName"/>.
        /// </summary>
        string StorageTypeName = "")
    {
        public bool IsString => Kind == "String";
        public bool IsArray  => Length > 0;

        /// <summary>Whether the author declared this as an enum.</summary>
        public bool IsEnum => StorageTypeName.Length > 0;

        /// <summary>What the accessors load and store: the enum's integer, or the type.</summary>
        public string StorageType => IsEnum ? StorageTypeName : TypeName;

        /// <summary>Whether a narrow read has to sign extend. See Part 10.</summary>
        public bool Signed => Kind == "Int";
    }

    /// <summary>One state class and everything needed to write its other half.</summary>
    private sealed record State(
        string? Namespace,
        ImmutableArray<string> Containers,
        string TypeName,
        string ClassName,
        ImmutableArray<Property> Properties,
        int Size,
        ImmutableArray<Diagnostic> Diagnostics,
        int BlockBytes = 0);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var states = context.SyntaxProvider.ForAttributeWithMetadataName(
            StateAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => Describe(ctx));

        context.RegisterSourceOutput(states, static (spc, state) =>
        {
            if (state is null)
                return;

            foreach (var diagnostic in state.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            if (state.Properties.IsEmpty)
                return;

            // Nested types need the enclosing names in the hint, or two state
            // classes with the same name in different outers collide.
            string hint = state.Containers.IsEmpty
                ? state.TypeName
                : string.Join(".", state.Containers) + "." + state.TypeName;

            spc.AddSource($"{hint}.NetState.g.cs",
                          SourceText.From(Emit(state), Encoding.UTF8));
        });
    }

    //-------------------------------------------------------------------------
    // Reading the declaration.
    //-------------------------------------------------------------------------

    private static State? Describe(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
            return null;

        var attribute = ctx.Attributes[0];
        string className = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string ?? ""
            : "";

        var declaredProperties = new List<Property>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // READ FIRST, PLACE AFTERWARDS (Part 10, Step 8). This loop used to
        // carry a running offset and place each field where it fell, which made
        // declaration order the storage order and the wire order at once. It
        // cannot any more: packing needs to know every field before it can
        // decide which of them share a word, and the Group hint reorders them.
        // So reading the declaration and placing it are two passes now, and the
        // rules live entirely in the second one.
        if (className.Length == 0)
            diagnostics.Add(Diagnostic.Create(MissingClassName, type.Locations.FirstOrDefault(), type.Name));

        // Declaration order, which is wire order. GetMembers returns source
        // order within a part, and a state class is one part by construction —
        // the other part is the one being written here.
        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            // Only the ones asking to be implemented. An ordinary property on a
            // state class is left alone: not everything about an entity's state
            // is replicated, and a class may want a computed value over the
            // ones that are.
            if (!property.IsPartialDefinition)
                continue;

            string typeName = property.Type.ToDisplayString();

            // A fixed-length run. Native never learns these belong together:
            // they are laid out as that many consecutive fields, which is why
            // an array needs nothing of the ABI that a single value does not.
            if (typeName.StartsWith(ArrayType, StringComparison.Ordinal))
            {
                var run = DescribeArray(property, typeName, diagnostics);
                if (run is not null)
                    declaredProperties.Add(run);

                continue;
            }

            // AN ENUM IS ITS UNDERLYING INTEGER (plan_nettable_generic.md
            // Step 11), resolved once here so every rule below is the rule for
            // the integer the author chose. A PrecipitationType backed by int
            // lays out, packs and sends exactly as an int does; one backed by
            // byte costs a byte, which is the point of letting the author
            // choose. Nothing native learns anything.
            bool isEnum = property.Type.TypeKind == TypeKind.Enum;

            string storageTypeName = isEnum && property.Type is INamedTypeSymbol e
                                         && e.EnumUnderlyingType is not null
                ? e.EnumUnderlyingType.ToDisplayString()
                : typeName;

            string? kind = KindOf(storageTypeName);

            if (kind is null)
            {
                diagnostics.Add(Diagnostic.Create(UnsupportedType,
                    property.Locations.FirstOrDefault(), property.Name, typeName));
                continue;
            }

            // A bool-backed enum is not a thing C# can express, and a
            // char-backed one is not either -- but a string kind falling out of
            // the resolution above would mean the underlying type was read
            // wrong, and that would silently make a networked field a buffer.
            if (isEnum && kind is not ("Int" or "UInt"))
            {
                diagnostics.Add(Diagnostic.Create(UnsupportedType,
                    property.Locations.FirstOrDefault(), property.Name, typeName));
                continue;
            }

            // An array is get-only: its elements are written, never the array
            // itself, because replacing it would silently detach the elements
            // from the entity they write into.
            bool isArray = property.Type.ToDisplayString()
                                   .StartsWith(ArrayType, StringComparison.Ordinal);

            if (property.GetMethod is null || (!isArray && property.SetMethod is null))
            {
                diagnostics.Add(Diagnostic.Create(NeedsGetAndSet,
                    property.Locations.FirstOrDefault(), property.Name));
                continue;
            }

            // [NetString] IS a [NetField] — it derives from it — so the search
            // walks the base chain rather than matching one name.
            var declared = property.GetAttributes().FirstOrDefault(IsNetField);

            string wireName = property.Name;
            string group = "";
            int bits = 0;

            if (declared is not null)
            {
                // [NetString(64)] carries its length positionally, because for a
                // string the length is the only thing left to say.
                if (declared.AttributeClass?.ToDisplayString() == StringAttribute
                    && declared.ConstructorArguments.Length > 0
                    && declared.ConstructorArguments[0].Value is int length)
                {
                    bits = length;
                }
                // AN EXPLICIT KIND, which the attribute has always documented
                // and this has never read. [NetField(NetFieldKind.UInt)] on an
                // int was accepted by the compiler and dropped here, so a field
                // declaring it got the kind of its C# type anyway — silently,
                // which is the shape everything else in this file exists to
                // prevent.
                //
                // It reinterprets an INTEGER only, and only as signed or
                // unsigned. That is the whole of what the attribute's own
                // summary asks for ("a field whose kind cannot be read off its
                // type") and it is exactly the case that matters: an int
                // carrying a bitmask is unsigned however C# spells it, and once
                // the field is narrow the difference decides both the sign
                // extension on the way out and the range check on the way in.
                else if (declared.AttributeClass?.ToDisplayString() == FieldAttribute
                         && declared.ConstructorArguments.Length > 0
                         && declared.ConstructorArguments[0].Value is int declaredKind)
                {
                    string? asked = KindNameOf(declaredKind);

                    if (asked is null || kind is not ("Int" or "UInt")
                        || asked is not ("Int" or "UInt"))
                    {
                        diagnostics.Add(Diagnostic.Create(BadKindOverride,
                            property.Locations.FirstOrDefault(), property.Name,
                            asked ?? declaredKind.ToString(), typeName));
                    }
                    else
                    {
                        kind = asked;
                    }
                }

                foreach (var named in declared.NamedArguments)
                {
                    if (named.Key == "Name" && named.Value.Value is string n && n.Length > 0)
                        wireName = n;
                    else if (named.Key == "Bits" && named.Value.Value is int b)
                        bits = b;
                    else if (named.Key == "Group" && named.Value.Value is string g && g.Length > 0)
                        group = g;
                }
            }

            int bytes = BytesOf(kind, storageTypeName, bits);

            declaredProperties.Add(new Property(
                property.Name, wireName, typeName, kind,
                bits > 0 ? bits : DefaultBits(kind, bytes), bytes, Offset: 0,
                Group: group, StorageTypeName: isEnum ? storageTypeName : ""));
        }

        // THE BLOCK IS THE FIRST THING IN THE LAYOUT (plan_entity.md §3.2).
        //
        // The blob an entity carries begins with the engine block, at offset
        // zero, because the shell's ICollideable and IServerEntity getters read
        // it there and a getter that had to add a runtime base would not be the
        // single constant-offset load the partition's inner loop needs. So the
        // declared fields start AFTER it, and every offset below is a blob
        // offset with the block already accounted for.
        //
        // A missing block is an ERROR and not a zero. Placing from zero would
        // lay the first declared field straight over the block's handle and
        // origin, and the two would then write each other's memory with nothing
        // anywhere reporting it.
        int blockBytes = EngineBlockBytes(ctx.SemanticModel.Compilation);

        if (blockBytes < 0)
        {
            diagnostics.Add(Diagnostic.Create(MissingEngineBlock, type.Locations.FirstOrDefault(),
                                              type.Name, BlockTypeName, BlockSizeField));
            return new State(null, ImmutableArray<string>.Empty, type.Name, className,
                             ImmutableArray<Property>.Empty, 0, diagnostics.ToImmutable());
        }

        var (properties, size) = Place(declaredProperties, blockBytes);

        string? ns = type.ContainingNamespace.IsGlobalNamespace
            ? null
            : type.ContainingNamespace.ToDisplayString();

        // A state class may be nested — a test declares several inside the class
        // that checks them. The generated half has to be nested the same way or
        // it is a different type with the same name, which the compiler reports
        // as the partial property having no implementation and nothing explains.
        var containers = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
            containers.Insert(0, outer.Name);

        return new State(ns, containers.ToImmutable(), type.Name, className,
                         properties, size, diagnostics.ToImmutable(), blockBytes);
    }

    /// <summary>
    /// Describes a <c>NetArray&lt;T&gt;</c>: the element type decides everything
    /// about one element, and the attribute decides how many.
    /// </summary>
    private static Property? DescribeArray(IPropertySymbol property, string typeName,
                                           ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // NetArray<uint> -> uint
        string elementType = typeName.Substring(ArrayType.Length,
                                                typeName.Length - ArrayType.Length - 1);

        string? kind = KindOf(elementType);

        // Only the fixed-width kinds. A run of strings would be a run of
        // buffers whose length is declared somewhere else again, and a run of
        // vectors has no user yet — both are additive when one turns up. A
        // QAngle is refused for the vector's reason, and a Color32 for its own:
        // an element is read by a fixed stride through Read<T>, and a colour is
        // the one kind whose storage is not its C# type's size, so the stride
        // NetArray would compute is not the stride the layout used.
        if (kind is null || kind == "String" || kind == "Vector"
            || kind == "QAngle" || kind == "Color32")
        {
            diagnostics.Add(Diagnostic.Create(UnsupportedElement,
                property.Locations.FirstOrDefault(), property.Name, elementType));
            return null;
        }

        var declared = property.GetAttributes().FirstOrDefault(
            a => a.AttributeClass?.ToDisplayString() == ArrayAttribute);

        if (declared is null || declared.ConstructorArguments.Length == 0
            || declared.ConstructorArguments[0].Value is not int length || length <= 0)
        {
            diagnostics.Add(Diagnostic.Create(NeedsLength,
                property.Locations.FirstOrDefault(), property.Name));
            return null;
        }

        string wireName = property.Name;
        string group = "";
        int bits = 0;

        foreach (var named in declared.NamedArguments)
        {
            if (named.Key == "Name" && named.Value.Value is string n && n.Length > 0)
                wireName = n;
            else if (named.Key == "Bits" && named.Value.Value is int b)
                bits = b;
            else if (named.Key == "Group" && named.Value.Value is string g && g.Length > 0)
                group = g;
        }

        int elementBytes = BytesOf(kind, elementType, 0);

        // A RUN IS NEVER PACKED, and that is a rule rather than an omission.
        // Native sees an array as that many consecutive fields and the client's
        // renderer indexes it by a fixed stride, so an element that shared a word
        // with the one before it would break the arithmetic on both sides. The
        // elements each take their own storage whole, exactly as they did.
        return new Property(property.Name, wireName, typeName, kind,
                            bits > 0 ? bits : DefaultBits(kind, elementBytes),
                            elementBytes, Offset: 0, Length: length,
                            ElementTypeName: elementType, Group: group);
    }

    //-------------------------------------------------------------------------
    // THE PACKER (plan_nettable_generic.md Part 10, Step 8).
    //
    // What changed and why. Storage used to be declaration order with natural
    // alignment, which the old comment defended as "a packet dump and a memory
    // dump read alike -- worth more than the padding a tighter packing would
    // save". Two things retired that. The payload is no longer one typed prop
    // per field, so a packet dump is a run of dwords and reads nothing like a
    // declaration whatever the order is; and the delta unit is the dword, so the
    // padding is not padding -- it is words on the wire.
    //
    // COST IS WORDS TOUCHED, so packing weakly dominates: two fields sharing a
    // word cost 39 bits between them when both change and 39 when either does,
    // where two words cost 78 and 39. There is no change pattern where packing
    // loses, which is why it needs no measurement to justify and only a
    // measurement to TUNE -- and tuning is what Group is for.
    //-------------------------------------------------------------------------

    //-------------------------------------------------------------------------
    // THE FIELD ORDER, STATED.
    //
    // Inside one group, every field lands in exactly one of four buckets, and
    // the buckets are laid down in this order:
    //
    //   1. WHOLE WORD    -- a field that is naturally four bytes and takes them
    //                       alone: a float, a full-width int, uint or enum, a
    //                       Color32.
    //   2. EXACT FILL    -- narrow fields whose widths ADD UP TO EXACTLY 32,
    //                       grouped so that they fill a word with nothing left
    //                       over.
    //   3. LEFTOVERS     -- every other narrow field. Still packed, but the last
    //                       word of this bucket is the only one in the class
    //                       allowed to have spare bits in it.
    //   4. WIDER THAN A WORD -- strings, arrays, Vectors and QAngles: storage
    //                       measured in buffers rather than in words. THESE
    //                       START ON A FRESH WORD: rule 3's open word is closed
    //                       first, so nothing wide begins part-way through bits
    //                       somebody else is using.
    //
    // WHY THIS ORDER RATHER THAN THE OLD ONE. The old packer had two passes,
    // narrow-then-whole, so a class with a single bool led with that bool in a
    // word of its own and pushed everything real behind it -- color_correction
    // read "offset 0, one bool, 1 of 32 bits used", then a 128-byte string, then
    // the three floats that are what the entity actually does. The word-filling
    // types come first now and the ragged ends collect at the back, so reading
    // the layout top-down is reading the class in descending order of how
    // completely each field occupies its storage.
    //
    // IT IS AN ORDER AND NOT A SIZE RULE. Buckets 1 to 3 are word-aligned and
    // word-multiple by construction, and bucket 4 aligns to a word, so moving
    // fields between the buckets can only ever move bytes around -- the total is
    // decided by how many words the narrow fields pack into, which is the same
    // question in either order. Measured across all 32 declared classes, no
    // class changed size.
    //
    // "ADD UP TO EXACTLY 32" MEANS EXACTLY, AND THE RUN IS CONTIGUOUS.
    // Three 8-bit fields make 24 of 32 bits, which is not a word, so they are
    // LEFTOVERS under rule 3 and not a group under rule 2 -- otherwise "exactly"
    // would mean "approximately" and a reader would have to know how close is
    // close enough. The runs are found by scanning the group's narrow fields in
    // DECLARATION ORDER and taking the leftmost contiguous run that sums to 32,
    // then continuing after it: contiguous, because a rule that reached across
    // the declaration to pair a byte with a distant three-byte field would make
    // a field's place unpredictable from reading the source, and subset-sum is
    // not a thing a person does in their head. So a reader finds bucket 2 by
    // sliding a window down the declaration and adding.
    //
    // WHAT THIS COSTS AGAINST THE OLD PACKER: nothing, on bandwidth. Rule 2 and
    // rule 3 together place the same narrow fields into the same number of
    // words the old single pass did -- the greedy no-straddle walk and the
    // exact-fill-then-leftovers walk both fill words in declaration order and
    // both pad only when the next field will not fit. What changes is WHICH word
    // a given field lands in, which changes the schema hash and nothing else.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Which of the four buckets a field belongs to. Total by construction:
    /// a field is narrow or it is not, and each half splits in two.
    /// </summary>
    private enum Bucket
    {
        /// <summary>Rule 1: naturally four bytes, taken whole.</summary>
        WholeWord = 0,

        /// <summary>Rule 2: narrow, and part of a run that sums to exactly 32.</summary>
        ExactFill = 1,

        /// <summary>Rule 3: narrow, and not part of such a run.</summary>
        Leftover = 2,

        /// <summary>Rule 4: wider than a word — a string, an array, a Vector.</summary>
        Blob = 3,
    }

    /// <summary>
    /// How wide the engine block is, taken from
    /// <c>SourceSharp.Abi.EngineBlock.SizeBytes</c> in the compilation being
    /// generated for.
    ///
    /// <para>
    /// A CONSTANT READ, NOT A LAYOUT COMPUTED. Working the block's size out
    /// from its field symbols would mean implementing .NET's sequential layout
    /// rules here — alignment, fixed buffers, trailing padding — and a
    /// generator that got them subtly wrong would place every entity's fields
    /// over the end of the block and be believed. The declaration states its own
    /// size as a <c>const</c>, <c>EngineBlockTests</c> asserts that constant
    /// equals <c>Marshal.SizeOf&lt;EngineBlock&gt;()</c>, and this reads it.
    /// One number, checked once, against the runtime that will actually lay the
    /// struct out.
    /// </para>
    ///
    /// <para>Returns -1 when the type or the constant is not there.</para>
    /// </summary>
    private static int EngineBlockBytes(Compilation compilation)
    {
        var block = compilation.GetTypeByMetadataName(BlockTypeName);

        if (block is null)
            return -1;

        foreach (var member in block.GetMembers(BlockSizeField))
        {
            if (member is IFieldSymbol { IsConst: true, ConstantValue: int bytes } && bytes >= 0)
                return bytes;
        }

        return -1;
    }

    /// <summary>
    /// Places every field: groups in first-declared order, and inside each group
    /// the four buckets above in order.
    /// </summary>
    /// <returns>The fields with their placements, and the bytes they need.</returns>
    private static (ImmutableArray<Property> Placed, int Size) Place(List<Property> declared,
                                                                     int blockBytes)
    {
        // Groups in FIRST-DECLARED order, so the layout is a function of the
        // source and not of a hash's iteration order. Both sides compile the
        // same declaration and nothing native arbitrates between them any more,
        // so a placement that depended on anything else would be a desync with
        // no symptom until a field read wrong.
        var groups = new List<string>();

        foreach (var property in declared)
        {
            if (!groups.Contains(property.Group))
                groups.Add(property.Group);
        }

        var placed = ImmutableArray.CreateBuilder<Property>(declared.Count);

        // In BITS, because that is the unit a narrow field is placed in. Bytes
        // would need a second number beside it saying how far into the byte, and
        // one cursor is harder to get wrong than two.
        //
        // STARTS AFTER THE ENGINE BLOCK, not at zero. The block occupies
        // [0, blockBytes) of every blob and no declared field may touch it.
        int bit = blockBytes * 8;

        foreach (string group in groups)
        {
            // A GROUP NEVER SHARES A WORD WITH ANOTHER GROUP, AND AN EXPLICIT
            // GROUP BEATS THE BUCKETING. The buckets order the fields WITHIN one
            // group and never move a field across a group boundary, so a run
            // under rule 2 cannot span two groups however neatly the widths
            // would have added up. That is the whole of what the hint buys:
            // putting two fields in different groups is how an author says
            // "these do not change together", and letting an exact fill reach
            // across the boundary would make the hint advisory.
            bit = AlignBits(bit, 32);

            // The group's fields, in declaration order, and which bucket each
            // one is in. One pass, so the run-finding below sees exactly the
            // narrow fields this group declared and in the order it declared
            // them.
            var members = new List<Property>();

            foreach (var property in declared)
            {
                if (property.Group == group)
                    members.Add(property);
            }

            var exact = ExactFillFields(members);

            // ---- Rule 1: the whole-word types ---------------------------
            foreach (var property in members)
            {
                if (BucketOf(property, exact) != Bucket.WholeWord)
                    continue;

                bit = AlignBits(bit, 32);
                placed.Add(property with { Offset = bit / 8 });
                bit += 32;
            }

            // ---- Rules 2 and 3: the narrow ones -------------------------
            //
            // One walk over both buckets, because they place identically —
            // the only difference is that rule 2's fields are known to end
            // flush with a word and rule 3's are not. Rule 2 first, so the
            // spare bits collect in one place at the end.
            foreach (var bucket in new[] { Bucket.ExactFill, Bucket.Leftover })
            {
                foreach (var property in members)
                {
                    if (BucketOf(property, exact) != bucket)
                        continue;

                    int width = WidthOf(property);

                    // NO FIELD STRADDLES A DWORD. A 30-bit field starting at bit
                    // 20 would span two props, so changing it would dirty both
                    // -- 78 bits instead of 39 -- and every access would become
                    // a two-word read-modify-write. Pad to the next word
                    // instead. Rule 2's fields never trip this by construction;
                    // rule 3's are exactly the ones that can.
                    if ((bit % 32) + width > 32)
                        bit = AlignBits(bit, 32);

                    placed.Add(property with
                    {
                        // The STORAGE is the whole containing word, which is the
                        // honest answer to "what does touching this field cost"
                        // and what lets the traffic view charge the word to it
                        // by the same overlap test it used before packing.
                        Bytes    = 4,
                        Bits     = width,
                        Offset   = bit / 32 * 4,
                        BitShift = bit % 32,
                        Packed   = true,
                    });

                    bit += width;
                }
            }

            // ---- The bucket-4 boundary ----------------------------------
            //
            // A FIELD WIDER THAN A WORD STARTS ON A FRESH WORD, never part-way
            // through the one rule 3 left open. Rule 3 routinely leaves spare
            // bits -- partial groups live there by construction, and that is the
            // intended shape rather than waste -- and letting a string or a
            // Vector reach back to fill them would couple the two: a shared word
            // is charged to every field in it, so the buffer could not move
            // without the leftovers going too, and the leftovers could not move
            // without the buffer's first word going too.
            //
            // Rule 2's fields end flush by construction, so this closes rule
            // 3's word and nothing else. It is stated here rather than left to
            // AlignmentOf below because AlignmentOf is about ACCESS and this is
            // about the boundary: an array of bytes aligns to one and must still
            // start a word here.
            bit = AlignBits(bit, 32);

            // ---- Rule 4: everything wider than a word -------------------
            foreach (var property in members)
            {
                if (BucketOf(property, exact) != Bucket.Blob)
                    continue;

                // Takes its storage whole, at its own alignment, exactly as
                // before -- with one change made when packing landed: a STRING
                // is word aligned now where it used to align to a byte. Its
                // buffer is far longer than a word, so the only thing byte
                // alignment ever bought was letting the buffer's first and last
                // words be shared with a neighbour, and a shared word is charged
                // to both fields when either moves. Three bytes is a cheap price
                // for uncoupling them.
                int align = AlignmentOf(property.Kind, property.Bytes);
                bit = AlignBits(bit, align * 8);

                placed.Add(property with { Offset = bit / 8 });

                bit += property.Bytes * 8 * Math.Max(property.Length, 1);
            }
        }

        // Rounded to a whole word: the payload IS words, and a state whose last
        // field ends mid-word would have native reading a prop past the end of
        // the declared size.
        return (placed.ToImmutable(), AlignBits(bit, 32) / 8);
    }

    /// <summary>
    /// Which fields fill a word exactly: the leftmost contiguous runs of narrow
    /// fields, in declaration order, whose widths sum to 32.
    ///
    /// <para>
    /// A sliding window, which is what makes the answer both cheap and
    /// PREDICTABLE BY READING: widths are positive, so growing the window at the
    /// end and shrinking it at the front visits every contiguous run once, and
    /// the first run that hits 32 is taken and the scan continues after it. A
    /// field the window passed over on its way is a leftover, and the reader can
    /// see why — the run that would have contained it overshot.
    /// </para>
    ///
    /// <para>
    /// Returned as a set of field NAMES rather than as indices, because the
    /// caller iterates the declaration a second time to place them and matching
    /// on position would be matching on two loops staying in step. Names are
    /// unique within a state — the generator refuses a duplicate before this
    /// runs.
    /// </para>
    /// </summary>
    private static HashSet<string> ExactFillFields(List<Property> members)
    {
        var narrow = new List<Property>();

        foreach (var property in members)
        {
            if (IsNarrow(property))
                narrow.Add(property);
        }

        var exact = new HashSet<string>(StringComparer.Ordinal);

        int start = 0;
        int sum   = 0;

        for (int end = 0; end < narrow.Count; end++)
        {
            sum += WidthOf(narrow[end]);

            // Overshot: drop fields off the front until the window fits again.
            // What is dropped is not decided yet — a later run may still take
            // it, and only fields never inside a run that closes are leftovers.
            while (sum > 32)
            {
                sum -= WidthOf(narrow[start]);
                start++;
            }

            if (sum != 32)
                continue;

            for (int i = start; i <= end; i++)
                exact.Add(narrow[i].Name);

            start = end + 1;
            sum   = 0;
        }

        return exact;
    }

    /// <summary>
    /// The bucket a field is placed in. See THE FIELD ORDER above.
    /// </summary>
    private static Bucket BucketOf(Property property, HashSet<string> exact)
    {
        if (IsNarrow(property))
            return exact.Contains(property.Name) ? Bucket.ExactFill : Bucket.Leftover;

        // NOT NARROW, so it takes its storage whole and the only question left
        // is whether that storage is one word. An array is a run of them and a
        // string is a buffer, so both are rule 4 whatever their length — a
        // four-byte string buffer is still a buffer, and placing it among the
        // floats would say otherwise.
        return !property.IsArray && !property.IsString && property.Bytes == 4
            ? Bucket.WholeWord
            : Bucket.Blob;
    }

    /// <summary>
    /// Whether a field shares a word rather than taking storage whole.
    ///
    /// Only integers and bools narrower than a dword. A float is a whole word by
    /// definition, a vector is three, a string is a buffer, and an array is a
    /// run — none of them has spare bits to give and none can be masked out of
    /// one without the reader knowing the packing.
    /// </summary>
    private static bool IsNarrow(Property property)
        => !property.IsArray
           && property.Kind is "Int" or "UInt" or "Bool"
           && WidthOf(property) < 32;

    /// <summary>
    /// How many bits a field occupies in storage. A bool is one — one BIT, not
    /// one byte, which is the change packing makes and the reason a bool costs
    /// nothing to add to a class that already has a packed word with room.
    ///
    /// <para>
    /// AN ENUM IS ITS UNDERLYING INTEGER HERE TOO, AND THE PACKER DOES NOT READ
    /// ITS MEMBERS. Step 11 raises the opposite: an enum of five members needs
    /// three bits and declaring the type has already told the generator the
    /// domain, so why ask for a bit count as well. The answer is that the
    /// members are not the domain. C# lets an enum variable hold any value of
    /// its backing type — <c>(PrecipitationType)99</c> compiles — and a flags
    /// enum is routinely handed a bit stock defines and this tree has not named;
    /// <c>DamageType</c> names fifteen of the engine's DMG_ bits and no more. So
    /// narrowing to the declared members would turn a legal assignment into a
    /// <c>FieldRangeException</c> at the setter, which is a crash bought with
    /// 29 bits. It would also make ADDING a member a wire change with no source
    /// reordering, which is a surprise nothing else here springs.
    /// </para>
    ///
    /// <para>
    /// The author narrows an enum the same two ways they narrow an integer, and
    /// both are already here: back it with <c>byte</c> and it costs a byte and
    /// packs into eight bits, or write <c>Bits = n</c> and it costs n. Both say
    /// "I accept the range check", which the member list does not.
    /// </para>
    /// </summary>
    private static int WidthOf(Property property)
        => property.Kind == "Bool" ? 1
         : property.Bits > 0 ? property.Bits
         : property.Bytes * 8;

    private static int AlignBits(int bit, int alignment)
        => alignment <= 1 ? bit : (bit + alignment - 1) / alignment * alignment;

    /// <summary>
    /// A declared type as a READER would write it: <c>Vector</c>, not
    /// <c>SourceSharp.Abi.Vector</c>.
    ///
    /// The symbol's display string is fully qualified for anything that is not a
    /// C# keyword, and the inspector column this feeds is one narrow cell beside
    /// a field name. The namespace is never the part somebody is checking there
    /// — they are checking that the row says <c>byte</c> where they wrote
    /// <c>byte</c> — so it is dropped rather than allowed to push the columns
    /// that carry the numbers off the side.
    /// </summary>
    private static string ShortTypeName(string displayed)
    {
        int dot = displayed.LastIndexOf('.');
        return dot < 0 ? displayed : displayed.Substring(dot + 1);
    }

    //-------------------------------------------------------------------------
    // The layout rules. THE ONLY COPY OF THEM.
    //
    // They were computed at runtime, by reflection, every time a state type was
    // first touched — on both sides, from the same metadata, which was correct
    // but was work done twice per process to reach an answer fixed at compile
    // time. Doing it here makes the offsets constants in the generated
    // accessors and deletes the runtime implementation rather than adding a
    // second one: two implementations of this arithmetic is exactly the drift
    // this whole design exists to prevent.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The wire kind of a C# type, or null if it has none.
    ///
    /// The integer types are here in full because that is the point: an author
    /// who wants sixteen bits writes <c>short</c>, which is what C# already has
    /// for saying so, rather than an <c>int</c> and a bit count beside it that
    /// has to agree.
    /// </summary>
    private static string? KindOf(string typeName) => typeName switch
    {
        "sbyte" or "short" or "int"    => "Int",
        "byte" or "ushort" or "uint"   => "UInt",
        "float"                        => "Float",
        "bool"                         => "Bool",
        "string"                       => "String",
        "SourceSharp.Abi.Vector"       => "Vector",

        // A colour and a facing, both added in the step that converted
        // spotlight_end's four hand-packed channels into one field. Fully
        // qualified like Vector, because that is what ToDisplayString gives and
        // matching the short name would match anybody's own Color32.
        "SourceSharp.Abi.Color32"      => "Color32",
        "SourceSharp.Abi.QAngle"       => "QAngle",

        _ => null,
    };

    /// <summary>
    /// How much storage a field takes: its C# type's size — except a string,
    /// whose type says nothing and whose buffer is declared.
    /// </summary>
    private static int BytesOf(string kind, string typeName, int declaredBits) => kind switch
    {
        "Vector" => 12,

        // THREE FLOATS, EXACTLY A VECTOR'S. The kinds differ, the storage does
        // not, and stating the same 12 twice is better than folding the two
        // together -- a later change to one must not silently move the other.
        "QAngle" => 12,

        // FOUR, NOT SIXTEEN. sizeof(Color32) is sixteen because its channels are
        // ints; what it occupies here is the one word its four bytes pack into,
        // which is what the four byte channels it replaces already cost. This is
        // the only kind whose storage is not its C# type's size, and the reason
        // it has its own accessors rather than Read<T>.
        "Color32" => 4,

        // A bool is one BIT on the wire and one byte in storage, because a
        // prop's offset is taken against real storage and there is no
        // addressable bit.
        "Bool"   => 1,

        "String" => declaredBits > 0 ? declaredBits : DefaultStringLength,

        _ => typeName switch
        {
            "sbyte" or "byte"    => 1,
            "short" or "ushort"  => 2,
            _                    => 4,
        },
    };

    /// <summary>
    /// What a field aligns to. A Vector is three floats and aligns to four
    /// rather than to twelve, which is not an alignment at all; everything else
    /// aligns to its own width, up to a word.
    ///
    /// <para>
    /// A STRING ALIGNS TO A WORD NOW, where it used to align to a byte because
    /// its buffer is a byte array and needs nothing more. That was true of the
    /// access and false of the cost: a buffer starting mid-word shares its first
    /// word with whatever precedes it, and a shared word is charged to both
    /// fields whenever either moves. <c>func_smokevolume</c> had exactly that —
    /// a 128-byte material name starting at byte 6, so two of its six colour
    /// bytes could not change without the name's first word going too.
    /// </para>
    ///
    /// <para>
    /// A Bool never reaches here: it is always narrow and always packed.
    /// </para>
    /// </summary>
    private static int AlignmentOf(string kind, int bytes) => kind switch
    {
        "Vector" => 4,
        "QAngle" => 4,
        "String" => 4,
        "Bool"   => 1,
        _        => bytes,
    };

    /// <summary>
    /// What a field sends when it does not say: all of it.
    ///
    /// <para>
    /// SINCE PACKING, THIS IS THE STORAGE WIDTH TOO. It used to narrow the wire
    /// only — the comment here said so, and <c>NetTable.cs</c> justified it:
    /// "the storage stays the C# type's size, so a value that does not fit is
    /// truncated on the wire rather than misaligning everything after it".
    /// Nothing is misaligned by a narrow field any more, because the packer
    /// places what follows from the width rather than from the type. The
    /// truncation that sentence accepted is what became
    /// <see cref="SourceSharp.Abi.FieldRangeException"/>: a value that does not
    /// fit would now bleed into the neighbour sharing its word, so it is refused.
    /// </para>
    /// </summary>
    private static int DefaultBits(string kind, int bytes) => kind switch
    {
        "Int" or "UInt" => bytes * 8,
        "String"        => bytes,
        _               => 0,
    };

    /// <summary>
    /// A <c>NetFieldKind</c> value as the name this file uses for it.
    ///
    /// The numbers are the enum's declaration order in <c>NetTable.cs</c> and
    /// they are the ABI of the attribute as far as a generator is concerned: an
    /// attribute argument arrives as its underlying integer, and there is no
    /// enum to look it up in from here.
    /// </summary>
    private static string? KindNameOf(int value) => value switch
    {
        0 => "Vector",
        1 => "Int",
        2 => "UInt",
        3 => "String",
        4 => "Float",
        5 => "Bool",
        6 => "Color32",
        7 => "QAngle",
        _ => null,
    };

    private const int DefaultStringLength = 64;

    private static bool IsNetField(AttributeData attribute)
    {
        for (var t = attribute.AttributeClass; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == FieldAttribute)
                return true;
        }

        return false;
    }

    /// <summary>
    /// A packed field's accessors: a shift and a mask, and a range check on the
    /// way in.
    ///
    /// <para>
    /// THE SHIFT AND MASK ARE NOT WRITTEN OUT HERE. They go to
    /// <c>NetBlobView.ReadBits</c> / <c>ReadBitsSigned</c> / <c>WriteBits</c>
    /// with the placement as compile-time constants, so the arithmetic has one
    /// tested implementation instead of three hundred emitted copies — and the
    /// no-op-when-unchanged rule and the dirty mark come with it, which a
    /// hand-emitted read-modify-write would have had to remember.
    /// </para>
    ///
    /// <para>
    /// THE RANGE CHECK IS EMITTED ONLY WHERE IT CAN FIRE. A byte in eight bits
    /// and a short in sixteen cannot overflow their own width, so a check there
    /// would be a branch the JIT keeps and nothing can ever take. It is emitted
    /// exactly where the declaration narrows the type — which is the case the
    /// throw exists for.
    /// </para>
    /// </summary>
    private static void EmitPacked(StringBuilder sb, State state, Property p)
    {
        int width = WidthOf(p);

        if (p.Kind == "Bool")
        {
            sb.Append("        get => Blob(\"").Append(p.Name).Append("\").ReadBits(")
              .Append(p.Offset).Append(", ").Append(p.BitShift).AppendLine(", 1) != 0u;");
            sb.Append("        set => Blob(\"").Append(p.Name).Append("\").WriteBits(")
              .Append(p.Offset).Append(", ").Append(p.BitShift)
              .AppendLine(", 1, value ? 1u : 0u);");
            return;
        }

        if (p.Signed)
        {
            sb.Append("        get => (").Append(p.TypeName).Append(")Blob(\"").Append(p.Name)
              .Append("\").ReadBitsSigned(").Append(p.Offset).Append(", ").Append(p.BitShift)
              .Append(", ").Append(width).AppendLine(");");
        }
        else
        {
            sb.Append("        get => (").Append(p.TypeName).Append(")Blob(\"").Append(p.Name)
              .Append("\").ReadBits(").Append(p.Offset).Append(", ").Append(p.BitShift)
              .Append(", ").Append(width).AppendLine(");");
        }

        sb.AppendLine("        set");
        sb.AppendLine("        {");

        if (NarrowerThanItsType(p, width))
        {
            // AN ENUM IS COMPARED AS ITS INTEGER. C# will not order an enum
            // against a numeric literal, and the cast is free -- but leaving it
            // out would not fail to compile everywhere, it would fail only on
            // the classes that happen to declare one, which is the shape of bug
            // a generator exists to remove.
            string tested = p.IsEnum ? $"(({p.StorageType})value)" : "value";

            if (p.Signed)
            {
                long min = -(1L << (width - 1));
                long max = (1L << (width - 1)) - 1;

                sb.Append("            if (").Append(tested).Append(" < ").Append(min)
                  .Append(" || ").Append(tested).Append(" > ")
                  .Append(max).AppendLine(")");
                sb.Append("                NetFieldRange.Throw(\"").Append(state.TypeName)
                  .Append("\", \"").Append(p.Name).Append("\", ").Append(tested)
                  .Append(", ").Append(width)
                  .AppendLine(", true);");
            }
            else
            {
                ulong max = (1UL << width) - 1UL;

                // THE LOWER BOUND IS NOT ALWAYS FREE. An unsigned FIELD stored
                // in a signed C# type -- [NetField(NetFieldKind.UInt)] on an
                // int, which is how a bitmask is declared -- can be handed a
                // negative value, and masking it would store its low bits and
                // read back a large positive number with nothing saying so.
                // Where the C# type is itself unsigned the compiler has already
                // made this impossible and the test is not emitted.
                sb.Append("            if (")
                  .Append(IsSignedCsType(p.StorageType) ? tested + " < 0 || " : "")
                  .Append(tested).Append(" > ").Append(max).AppendLine(")");
                sb.Append("                NetFieldRange.Throw(\"").Append(state.TypeName)
                  .Append("\", \"").Append(p.Name).Append("\", (long)").Append(tested)
                  .Append(", ").Append(width)
                  .AppendLine(", false);");
            }

            sb.AppendLine();
        }

        sb.Append("            Blob(\"").Append(p.Name).Append("\").WriteBits(")
          .Append(p.Offset).Append(", ").Append(p.BitShift).Append(", ").Append(width)
          .AppendLine(", unchecked((uint)value));");
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Whether the declared width is narrower than the C# type it is stored in,
    /// i.e. whether a legal value of that type can fail to fit.
    /// </summary>
    /// <remarks>
    /// Against the STORAGE type, which for an enum is the integer it is backed
    /// by. Against the declared type it would ask whether four bits are narrower
    /// than a DynamicLightFlags, which has no answer, and TypeBits' default
    /// would quietly say 32 — emitting a check that cannot fire on a byte-backed
    /// enum and none at all where one was needed.
    /// </remarks>
    private static bool NarrowerThanItsType(Property p, int width)
        => width < TypeBits(p.StorageType)
           // An unsigned field in a signed C# type can be handed a negative
           // whatever the widths are, so the check is needed even at full width.
           || (!p.Signed && IsSignedCsType(p.StorageType));

    private static int TypeBits(string typeName) => typeName switch
    {
        "sbyte" or "byte"   => 8,
        "short" or "ushort" => 16,
        _                   => 32,
    };

    private static bool IsSignedCsType(string typeName)
        => typeName is "sbyte" or "short" or "int";

    //-------------------------------------------------------------------------
    // Writing the other half.
    //-------------------------------------------------------------------------

    private static string Emit(State state)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/> by SourceSharp.Generators.NetStateGenerator.");
        sb.AppendLine("// Edit the declaration, not this.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using SourceSharp.Abi;");
        sb.AppendLine();

        if (state.Namespace is not null)
        {
            sb.Append("namespace ").Append(state.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        // Reopen every enclosing type, outermost first. They must be partial
        // themselves, which the compiler says plainly if they are not.
        foreach (var container in state.Containers)
            sb.Append("partial class ").Append(container).AppendLine("\n{");

        sb.Append("partial class ").Append(state.TypeName).AppendLine(" : NetState")
          .AppendLine("{");

        sb.Append("    public override string NetClassName => \"")
          .Append(state.ClassName).AppendLine("\";");
        sb.AppendLine();

        // How long the state is, which the base reads once, at bind time. It is
        // what turns "the blob is shorter than this state" from an overrun on
        // the first write into a named exception at the moment the mistake is
        // made -- see BlobTooShortException.
        sb.Append("    protected override int StateBytes => ").Append(state.Size)
          .AppendLine(";");
        sb.AppendLine();

        // THE ENGINE BLOCK IS THE FIRST THING IN THIS LAYOUT (plan_entity.md
        // §3.2). Emitted before anything else about the class, because it is
        // before everything else in the blob: every offset below is a blob
        // offset and every one of them is at least this.
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Where this class's own fields begin: the bytes before them are the");
        sb.AppendLine("    /// ENGINE BLOCK (plan_entity.md §3.2), which sits at offset 0 of every");
        sb.AppendLine("    /// entity's blob so that a shell getter is one load at a constant");
        sb.AppendLine("    /// offset. No offset below is ever less than this.");
        sb.AppendLine("    /// </summary>");
        sb.Append("    public const int EngineBlockBytes = ").Append(state.BlockBytes)
          .AppendLine(";");
        sb.AppendLine();

        // NO BACKING FIELDS FOR VALUES (plan_nettable_generic.md Part 9). The
        // state's storage is the blob itself -- the entity's on the server, a
        // pinned buffer of its own on the client -- and every accessor below
        // reads and writes it directly. A managed copy beside it was a second
        // place the same value lived, with nothing comparing the two.
        //
        // An array still gets a field, because a NetArray is a CURSOR and not a
        // copy: it holds the state, the name and the offset so that indexing it
        // reaches the same storage every other property does. It is created on
        // first use rather than in an initialiser because it needs `this`, which
        // a field initialiser has no access to, and generating a constructor
        // would collide with whatever the author may have written.
        sb.AppendLine("    // WIRE ORDER IS NO LONGER DECLARATION ORDER (Part 10, Step 8). The");
        sb.AppendLine("    // packer groups the fields, packs the narrow ones into shared words and");
        sb.AppendLine("    // then emits the rest, so the order below is the PLACED order. Changing");
        sb.AppendLine("    // a declaration still moves everything after it and still has to be done");
        sb.AppendLine("    // on both sides at once -- what changed is that the schema hash now");
        sb.AppendLine("    // covers where inside its word each field sits, so a client that packed");
        sb.AppendLine("    // differently is refused rather than reading a neighbour's bits.");

        foreach (var p in state.Properties)
        {
            if (p.IsArray)
            {
                sb.Append("    private ").Append(p.TypeName).Append("? _")
                  .Append(p.Name).AppendLine(";");
            }
        }

        sb.AppendLine();
        sb.AppendLine("    // Offsets, computed at COMPILE time. Nothing here is worked out when");
        sb.AppendLine("    // the game starts, and nothing reflects: the answer was fixed the");
        sb.AppendLine("    // moment the declaration was written.");

        foreach (var p in state.Properties)
        {
            if (p.IsArray)
            {
                // One entry per element, named and placed consecutively. Native
                // sees a run of ordinary fields, which is all an array is once
                // the offsets are computed.
                for (int i = 0; i < p.Length; i++)
                {
                    sb.Append("    private static readonly NetFieldLayout s_").Append(p.Name)
                      .Append('_').Append(i)
                      .Append(" = new(\"").Append(p.WireName).Append('.').Append(i)
                      .Append("\", NetFieldKind.").Append(p.Kind)
                      .Append(", ").Append(p.Bits)
                      .Append(", ").Append(p.Bytes)
                      .Append(", ").Append(p.Offset + i * p.Bytes)
                      .Append(") { CsType = \"").Append(ShortTypeName(p.ElementTypeName))
                      .AppendLine("\" };");
                }

                continue;
            }

            sb.Append("    private static readonly NetFieldLayout s_").Append(p.Name)
              .Append(" = new(\"").Append(p.WireName)
              .Append("\", NetFieldKind.").Append(p.Kind)
              .Append(", ").Append(p.Bits)
              .Append(", ").Append(p.Bytes)
              .Append(", ").Append(p.Offset)
              .Append(", ").Append(p.BitShift);

            // The enum itself, where there is one. NOT on the wire -- native is
            // told a kind and a width and nothing more -- but the schema hash
            // covers its member NUMBERS, so a renumbering is refused rather than
            // believed, and the inspector decodes through its member NAMES.
            if (p.IsEnum)
                sb.Append(", typeof(").Append(p.TypeName).Append(')');

            // THE TYPE THE AUTHOR WROTE, carried because it cannot be worked out
            // downstream any more. The inspector used to guess it back from the
            // kind and the byte count; a packed field's byte count is its
            // WORD's, so every narrow field came out as the word's type -- four
            // `byte` channels sharing a dword all reported `uint`. Not hashed
            // and not on the wire: it changes no decoding, only what the one
            // view meant to report a declaration says the declaration was.
            sb.Append(") { CsType = \"").Append(ShortTypeName(p.TypeName)).AppendLine("\" };");
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>This class's table: its fields, in wire order, and its size.</summary>");
        sb.Append("    public static readonly StandaloneNetState NetLayout = new(\"")
          .Append(state.ClassName).Append("\", typeof(").Append(state.TypeName)
          .Append("), ").Append(state.Size).AppendLine(", new NetFieldLayout[]");
        sb.AppendLine("    {");

        foreach (var p in state.Properties)
        {
            if (p.IsArray)
            {
                for (int i = 0; i < p.Length; i++)
                    sb.Append("        s_").Append(p.Name).Append('_').Append(i).AppendLine(",");
            }
            else
            {
                sb.Append("        s_").Append(p.Name).AppendLine(",");
            }
        }

        sb.AppendLine("    });");

        foreach (var p in state.Properties)
        {
            sb.AppendLine();

            if (p.IsArray)
            {
                sb.Append("    public partial ").Append(p.TypeName).Append(' ').Append(p.Name)
                  .Append(" => _").Append(p.Name).Append(" ??= new ").Append(p.TypeName)
                  .Append("(this, \"").Append(p.Name).Append("\", ").Append(p.Offset)
                  .Append(", ").Append(p.Length)
                  .AppendLine(");");
                continue;
            }

            // Both halves go through Blob(), which is one load and one compare
            // and then a window onto the storage. There is no branch here on
            // which side this is: the server's storage is its entity's blob and
            // the client's is a buffer of its own, and an accessor cannot tell
            // -- which is what makes this one piece of generated code rather
            // than two.
            sb.Append("    public partial ").Append(p.TypeName).Append(' ').Append(p.Name)
              .AppendLine();
            sb.AppendLine("    {");

            if (p.IsString)
            {
                sb.Append("        get => Blob(\"").Append(p.Name).Append("\").ReadString(")
                  .Append(p.Offset).Append(", ").Append(p.Bytes).AppendLine(");");
                // The class and the field go with the value, and only the
                // refusal ever reads them: a string too long for its buffer is
                // thrown out rather than shortened, and the message has to name
                // what overran. Two literals in a setter cost nothing — they are
                // constants the JIT hands straight to a NoInlining cold path.
                sb.Append("        set => Blob(\"").Append(p.Name).Append("\").WriteString(")
                  .Append(p.Offset).Append(", ").Append(p.Bytes).Append(", value, \"")
                  .Append(state.TypeName).Append("\", \"").Append(p.Name).AppendLine("\");");
            }
            else if (p.Kind == "Color32")
            {
                // THE ONE KIND THAT CANNOT GO THROUGH Read<T>. Its C# type is
                // sixteen bytes of ints and its storage is one packed word, so
                // the pack and the unpack live in NetBlobView -- one tested
                // copy, with the byte order written down once -- and the class
                // and field travel with the write, because a channel outside
                // 0..255 is refused and the refusal has to name what overran.
                sb.Append("        get => Blob(\"").Append(p.Name).Append("\").ReadColor32(")
                  .Append(p.Offset).AppendLine(");");
                sb.Append("        set => Blob(\"").Append(p.Name).Append("\").WriteColor32(")
                  .Append(p.Offset).Append(", value, \"").Append(state.TypeName)
                  .Append("\", \"").Append(p.Name).AppendLine("\");");
            }
            else if (p.Packed)
            {
                EmitPacked(sb, state, p);
            }
            else
            {
                sb.Append("        get => Blob(\"").Append(p.Name).Append("\").Read<")
                  .Append(p.TypeName).Append(">(").Append(p.Offset).AppendLine(");");
                sb.Append("        set => Blob(\"").Append(p.Name).Append("\").Write(")
                  .Append(p.Offset).AppendLine(", value);");
            }

            sb.AppendLine("    }");
        }

        // NO DECODE (plan_nettable_generic.md Part 9). It used to be written
        // here, field by field, to fill the backing fields the accessors read.
        // With the state mapped onto its storage there is nothing to fill: the
        // base class copies the received image into that storage in one memcpy
        // and every accessor above is already reading it.
        //
        // That deletion is the larger of Part 9's two wins and the one it was
        // not undertaken to find. A per-field decode was a second description of
        // the layout -- one in the offsets above, one in the order of the copies
        // -- and two descriptions of a layout is the drift this whole design
        // exists to prevent.
        sb.AppendLine("}");

        for (int i = 0; i < state.Containers.Length; i++)
            sb.AppendLine("}");

        return sb.ToString();
    }

    //-------------------------------------------------------------------------
    // Diagnostics. Every one of these is something that would otherwise be a
    // field silently missing from the wire.
    //-------------------------------------------------------------------------

    private static readonly DiagnosticDescriptor MissingClassName = new(
        "SSNET001", "A net state must name its entity class",
        "[StandaloneNetState] on {0} names no entity class; the table would be keyed by nothing",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        "SSNET002", "Unsupported networked type",
        "'{0}' is a {1}, which cannot be networked; use sbyte, short, int, byte, ushort, uint, float, bool, string, Vector, QAngle or Color32",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedElement = new(
        "SSNET004", "Unsupported networked array element",
        "'{0}' is a run of {1}, which cannot be networked; use a fixed-width numeric or bool element",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NeedsLength = new(
        "SSNET005", "A networked array must state its length",
        "'{0}' needs [NetArray(n)]: the length is the wire format and cannot be decided at runtime",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor BadKindOverride = new(
        "SSNET006", "A declared kind can only reinterpret an integer's sign",
        "'{0}' asks for kind {1}, which a {2} cannot be; an explicit kind swaps Int for UInt on an integer and nothing else",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingEngineBlock = new(
        "SSNET007", "The engine block was not found",
        "'{0}' cannot be laid out: {1}.{2} was not found, so where the declared fields start is unknown; placing them from zero would lay them over the block",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NeedsGetAndSet = new(
        "SSNET003", "A networked property needs a get and a set",
        "'{0}' must declare both a get and a set: the server writes it and the client reads it",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
