using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE FIELD ORDER, as a rule rather than as whatever the packer happens to do.
///
/// <para>
/// The rule has four buckets and they are laid down in this order inside each
/// group:
/// </para>
///
/// <list type="number">
/// <item>naturally four bytes, taken alone — a float, a full-width int or enum,
/// a Color32;</item>
/// <item>narrow fields whose widths add to EXACTLY 32, filling a word with
/// nothing over;</item>
/// <item>every other narrow field — still packed, and the only place in a class
/// where a word may have spare bits;</item>
/// <item>wider than a word — strings, arrays, Vectors, QAngles — starting on a
/// FRESH word.</item>
/// </list>
///
/// <para>
/// WHY IT IS WORTH A FILE OF ITS OWN. Every one of these is a claim a reader
/// makes when they look at the layout pane and reason about what an entity
/// costs: that the word-filling types are at the top, that a word with spare
/// bits is at the bottom of the packed run and nowhere else, that a 128-byte
/// string never begins part-way through somebody's bits. Each is easy to
/// preserve by accident on the twenty classes that exist and easy to lose on the
/// twenty-first, and losing one is not an error anywhere — it is a layout that
/// still works and no longer reads the way it is documented to.
/// </para>
///
/// <para>
/// AND THE ORDER IS IN THE SCHEMA HASH, so every fact here is also a fact the
/// two sides must agree on. A packer that answered differently on two builds
/// would not misread anything — it would refuse the join — but the refusal names
/// the hash and not the reason, so the reason is written down here.
/// </para>
/// </summary>
public partial class NetFieldOrderTests
{
    /// <summary>
    /// Where a class's own fields begin. The blob starts with the ENGINE BLOCK
    /// (plan_entity.md §3.2) — the fixed struct every shell getter reads at a
    /// constant offset — so "word 0" of a declaration is the first word AFTER
    /// it, and every blob offset below is written as that base plus the offset
    /// within the class. Named rather than added as a literal so that the block
    /// growing moves these numbers with it.
    /// </summary>
    private const int Block = EngineBlock.SizeBytes;

    //-------------------------------------------------------------------------
    // THE FIXTURES. Declared in an order the buckets have to undo, because a
    // fixture already in bucket order would pass under any rule at all.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One field from every bucket, declared inside out.
    ///
    /// The narrow fields are 8, 8, 8 and 1 bits — 25 between them, which is not
    /// a word — so all four are rule-3 leftovers and there is no rule-2 word in
    /// this class at all.
    /// </summary>
    [StandaloneNetState("ss_order_every_bucket")]
    private partial class EveryBucket
    {
        [NetString(16)] public partial string Name  { get; set; }
        [NetField(Bits = 8)] public partial byte A   { get; set; }
        public partial float Scale                   { get; set; }
        [NetField(Bits = 8)] public partial byte B   { get; set; }
        public partial Vector Where                  { get; set; }
        [NetField(Bits = 8)] public partial byte C   { get; set; }
        public partial bool Flag                     { get; set; }
        public partial int Count                     { get; set; }
    }

    /// <summary>
    /// A three-bit field in front of four bytes, which is the case that decides
    /// what "adds to exactly 32" means.
    ///
    /// <para>
    /// The window opens on Odd, reaches 35 at Alpha, drops Odd to get back to
    /// 32, and closes on Red..Alpha. So the four bytes are the rule-2 word and
    /// the three-bit field the window stepped over is a rule-3 leftover — which
    /// is only true because EXACTLY means exactly. A rule that took "close
    /// enough" would have grouped Odd with Red, Green and Blue at 27 of 32 and
    /// placed it FIRST.
    /// </para>
    /// </summary>
    [StandaloneNetState("ss_order_exact_fill")]
    private partial class ExactFill
    {
        [NetField(Bits = 3)] public partial byte Odd   { get; set; }
        [NetField(Bits = 8)] public partial byte Red   { get; set; }
        [NetField(Bits = 8)] public partial byte Green { get; set; }
        [NetField(Bits = 8)] public partial byte Blue  { get; set; }
        [NetField(Bits = 8)] public partial byte Alpha { get; set; }
        public partial float Scale                     { get; set; }
    }

    /// <summary>
    /// Three eight-bit fields and nothing else narrow: 24 of 32 bits, the case
    /// the rule had to be made total for.
    /// </summary>
    [StandaloneNetState("ss_order_partial_word")]
    private partial class PartialWord
    {
        [NetField(Bits = 8)] public partial byte X { get; set; }
        [NetField(Bits = 8)] public partial byte Y { get; set; }
        [NetField(Bits = 8)] public partial byte Z { get; set; }
        public partial float Scale                 { get; set; }
    }

    /// <summary>
    /// A single bool, then the things that must not reach back into its word.
    /// </summary>
    [StandaloneNetState("ss_order_fresh_word")]
    private partial class FreshWord
    {
        public partial bool Flag                    { get; set; }
        [NetString(16)] public partial string Name  { get; set; }
        public partial Vector Where                 { get; set; }
    }

    /// <summary>
    /// Two sixteen-bit fields that would fill a word EXACTLY if the buckets were
    /// allowed to reach across the group boundary, and a float in the second
    /// group that would lead the class if bucket 1 outranked the hint.
    /// </summary>
    [StandaloneNetState("ss_order_grouped")]
    private partial class GroupedExact
    {
        [NetField(Bits = 16, Group = "a")] public partial ushort First  { get; set; }
        [NetField(Bits = 16, Group = "b")] public partial ushort Second { get; set; }
        [NetField(Group = "b")]            public partial float  Scale  { get; set; }
    }

    /// <summary>One of an identical pair. See <see cref="TheLayoutIsAFunctionOfTheDeclarationAlone"/>.</summary>
    [StandaloneNetState("ss_order_twin_one")]
    private partial class TwinOne
    {
        [NetString(16)] public partial string Name { get; set; }
        [NetField(Bits = 8)] public partial byte A { get; set; }
        public partial float Scale                 { get; set; }
        [NetField(Bits = 8)] public partial byte B { get; set; }
        public partial bool Flag                   { get; set; }
        public partial int Count                   { get; set; }
    }

    /// <summary>The other. Byte for byte the same declaration, under another name.</summary>
    [StandaloneNetState("ss_order_twin_two")]
    private partial class TwinTwo
    {
        [NetString(16)] public partial string Name { get; set; }
        [NetField(Bits = 8)] public partial byte A { get; set; }
        public partial float Scale                 { get; set; }
        [NetField(Bits = 8)] public partial byte B { get; set; }
        public partial bool Flag                   { get; set; }
        public partial int Count                   { get; set; }
    }

    //-------------------------------------------------------------------------
    // THE SHUFFLE PAIRS. Same fields, different source order.
    //
    // The twins above prove the class NAME does not leak into the layout, which
    // is worth having and is not the claim that matters: both twins present
    // their fields in the same order, so a packer that ignored the buckets
    // entirely and laid fields down as declared would pass them unchanged.
    //
    // These are the pairs that can fail. A cross-bucket shuffle must change
    // NOTHING -- the buckets decide which word -- and a within-bucket shuffle
    // must change the offsets -- declaration order decides where inside it.
    // Either one alone is passable by a different bug: the first by a packer
    // that sorted everything and forgot declaration order, the second by one
    // that never sorted at all. Together they are the whole rule.
    //-------------------------------------------------------------------------

    /// <summary>One field from three different buckets, in bucket order.</summary>
    [StandaloneNetState("ss_order_across_declared")]
    private partial class AcrossInBucketOrder
    {
        public partial float Scale                 { get; set; }
        public partial bool Flag                   { get; set; }
        [NetString(16)] public partial string Name { get; set; }
    }

    /// <summary>The same three, declared backwards. Must lay out identically.</summary>
    [StandaloneNetState("ss_order_across_reversed")]
    private partial class AcrossReversed
    {
        [NetString(16)] public partial string Name { get; set; }
        public partial bool Flag                   { get; set; }
        public partial float Scale                 { get; set; }
    }

    /// <summary>Two fields of ONE bucket. See <see cref="AShuffleWithinABucketDoesMoveThem"/>.</summary>
    [StandaloneNetState("ss_order_within_ab")]
    private partial class WithinAB
    {
        public partial float A { get; set; }
        public partial float B { get; set; }
    }

    /// <summary>The same two, swapped. Must NOT lay out identically.</summary>
    [StandaloneNetState("ss_order_within_ba")]
    private partial class WithinBA
    {
        public partial float B { get; set; }
        public partial float A { get; set; }
    }

    /// <summary>
    /// Sixteen, eight and eight — thirty-two exactly — declared adjacently, with
    /// an unrelated three-bit field after them.
    /// </summary>
    [StandaloneNetState("ss_order_run_joined")]
    private partial class RunJoined
    {
        [NetField(Bits = 16)] public partial ushort P          { get; set; }
        [NetField(Bits = 8)]  public partial byte   Q          { get; set; }
        [NetField(Bits = 8)]  public partial byte   R          { get; set; }
        [NetField(Bits = 3)]  public partial byte   Interloper { get; set; }
    }

    /// <summary>
    /// The same four fields with the three-bit one MOVED INTO the middle of
    /// them, which breaks the run's contiguity and therefore the run.
    /// </summary>
    [StandaloneNetState("ss_order_run_split")]
    private partial class RunSplit
    {
        [NetField(Bits = 16)] public partial ushort P          { get; set; }
        [NetField(Bits = 3)]  public partial byte   Interloper { get; set; }
        [NetField(Bits = 8)]  public partial byte   Q          { get; set; }
        [NetField(Bits = 8)]  public partial byte   R          { get; set; }
    }

    //-------------------------------------------------------------------------
    // THE ORDER ITSELF, enumerated. Totality by listing: every declared field
    // appears once, and the sequence is the four buckets in order.
    //-------------------------------------------------------------------------
    [Fact]
    public void TheFourBucketsAreLaidDownInOrder()
    {
        var layout = EveryBucket.NetLayout;

        // Derived, not read off: Scale and Count are naturally four bytes and
        // lead in declaration order; A, B, C and Flag are 25 bits between them
        // so they are leftovers in one word; Name and Where are wider than a
        // word and follow, in declaration order.
        Assert.True(layout.Fields.Select(f => f.Name).SequenceEqual(
                new[] { "Scale", "Count", "A", "B", "C", "Flag", "Name", "Where" }),
            "whole words, then the exact fills, then the leftovers, then the buffers");

        Assert.True(layout.Fields.Count == 8, "and every declared field is placed exactly once");

        Assert.True(layout["Scale"].Offset == Block + 0 && layout["Count"].Offset == Block + 4, "the two four-byte fields take words 0 and 1");

        Assert.True(layout["A"].Offset == Block + 8 && layout["A"].BitShift == 0
              && layout["B"].BitShift == 8 && layout["C"].BitShift == 16
              && layout["Flag"].BitShift == 24, "the four narrow ones share word 2, in declaration order");

        Assert.True(layout["Name"].Offset == Block + 12 && layout["Where"].Offset == Block + 28, "and the string and the vector follow, from word 3");

        Assert.True(layout.Size == Block + 40, $"which is 40 bytes, not {layout.Size}");
    }

    //-------------------------------------------------------------------------
    // EXACTLY MEANS EXACTLY, which is the decision the rule turns on.
    //-------------------------------------------------------------------------
    [Fact]
    public void AnExactFillIsFoundAndAPartialOneIsNot()
    {
        var layout = ExactFill.NetLayout;

        // Scale leads as the only rule-1 field. Then the rule-2 word: the four
        // channels, which add to 32. Then Odd, the leftover the window stepped
        // over -- declared FIRST and placed LAST of the narrow fields, which is
        // the whole observable difference between "exactly 32" and "close".
        Assert.True(layout.Fields.Select(f => f.Name).SequenceEqual(
                new[] { "Scale", "Red", "Green", "Blue", "Alpha", "Odd" }),
            "the word that fills exactly is placed before the field that cannot fill one");

        Assert.True(layout["Red"].Offset == Block + 4 && layout["Alpha"].Offset == Block + 4
              && layout["Alpha"].BitShift == 24, "the four channels fill word 1 between them");

        Assert.True(layout["Odd"].Offset == Block + 8 && layout["Odd"].BitShift == 0, "and the three-bit field starts a word of its own after it");

        Assert.True(layout.Size == Block + 12, $"12 bytes, not {layout.Size}");

        // THE SPARE BITS ARE ALL IN THE LAST PACKED WORD. Stated as a count
        // rather than as an offset, because it is the property a reader relies
        // on when they look at the pane and see "4 bytes holding 1 field".
        var words = layout.Fields.Where(f => f.IsPacked)
                          .GroupBy(f => f.Offset)
                          .OrderBy(g => g.Key)
                          .Select(g => g.Sum(f => f.Bits))
                          .ToArray();

        Assert.True(words.SequenceEqual(new[] { 32, 3 }), "one full word, then the leftover, and not the other way round");
    }

    //-------------------------------------------------------------------------
    // AND A PARTIAL GROUP IS STILL PACKED. Rule 3 is not "unpacked" -- three
    // bytes that cannot make a word still share one, they just do not get to
    // claim rule 2's promise that the word is full.
    //-------------------------------------------------------------------------
    [Fact]
    public void ThreeBytesAreAPartialWordAndStillShareIt()
    {
        var layout = PartialWord.NetLayout;

        Assert.True(layout["X"].Offset == layout["Y"].Offset
              && layout["Y"].Offset == layout["Z"].Offset, "three eight-bit fields share one word");

        Assert.True(layout["X"].BitShift == 0 && layout["Y"].BitShift == 8 && layout["Z"].BitShift == 16, "eight bits apiece, in declaration order");

        Assert.True(layout["Scale"].Offset == Block + 0 && layout["X"].Offset == Block + 4, "behind the float, because 24 of 32 bits is a leftover and not a fill");

        Assert.True(layout.Size == Block + 8, $"8 bytes with a byte of the second word spare, not {layout.Size}");
    }

    //-------------------------------------------------------------------------
    // BUCKET 4 STARTS ON A FRESH WORD.
    //
    // The rule that costs something -- a bool with 31 bits spare beside it, and
    // a string that will not use them. It is deliberate: a shared word is
    // charged to every field in it whenever any of them moves, so a buffer
    // reaching back into the leftovers' word would mean the name could not
    // change without the bool's word going too, and vice versa. Three spare
    // bytes is the price of uncoupling them, and it is the same argument that
    // word-aligned strings in the first place.
    //-------------------------------------------------------------------------
    [Fact]
    public void TheWideFieldsStartOnAWordNobodyElseIsUsing()
    {
        var layout = FreshWord.NetLayout;

        Assert.True(layout.Fields.Select(f => f.Name).SequenceEqual(
                new[] { "Flag", "Name", "Where" }),
            "the leftover leads and the two wide fields follow it");

        Assert.True(layout["Flag"].Offset == Block + 0 && layout["Flag"].Bits == 1, "the bool holds one bit of word 0");

        Assert.True(layout["Name"].Offset == Block + 4, "and the string starts at word 1 rather than at byte 1 of word 0");

        // Said as the general rule rather than as this class's number: no field
        // wider than a word begins inside a word a packed field occupies.
        foreach (var wide in layout.Fields.Where(f => f.Bytes > 4))
        {
            Assert.True(layout.Fields.Where(f => f.IsPacked).All(p => p.Offset != wide.Offset), $"'{wide.Name}' does not begin in a packed word");
        }

        Assert.True(layout.Size == Block + 32, $"32 bytes: a word for the bool, 16 for the name, 12 for the vector ({layout.Size})");
    }

    //-------------------------------------------------------------------------
    // AN EXPLICIT Group BEATS THE BUCKETS.
    //
    // Which way round this goes had to be decided rather than discovered. The
    // hint is the author saying "these do not change together", the buckets are
    // a default about how a layout READS, and a default that could silently
    // override a stated fact about gameplay would make the hint advisory --
    // which is the one thing NetFieldAttribute.Group promises it is not.
    //-------------------------------------------------------------------------
    [Fact]
    public void AGroupBoundaryBeatsAnExactFill()
    {
        var layout = GroupedExact.NetLayout;

        // First and Second are 16 bits each and would be a perfect rule-2 word
        // together. They are in different groups, so they are not.
        Assert.True(layout["First"].Offset != layout["Second"].Offset, "two 16-bit fields that would fill a word exactly do not share one across a group boundary");

        // AND THE GROUPS STAY IN FIRST-DECLARED ORDER, so a rule-1 field in the
        // second group does not overtake a rule-3 field in the first. The
        // buckets sort WITHIN a group and the groups are not sorted at all.
        Assert.True(layout["First"].Offset == Block + 0, "group 'a' is first because it was declared first");

        Assert.True(layout["Scale"].Offset > layout["First"].Offset, "and a float in the second group does not lead the class, though it is a rule-1 field");

        Assert.True(layout.Fields.Select(f => f.Name).SequenceEqual(
                new[] { "First", "Scale", "Second" }),
            "inside group 'b' the float still leads the leftover, which is the bucketing doing its job under the hint");

        Assert.True(layout.Size == Block + 12, $"three words, one per field, which is what the hint costs and buys ({layout.Size})");
    }

    //-------------------------------------------------------------------------
    // DETERMINISM: THE LAYOUT IS A FUNCTION OF THE DECLARATION AND NOTHING ELSE.
    //
    // Two builds that disagree do not misread each other -- the schema hash
    // refuses the join -- but they refuse it for no reason a person can act on,
    // which is worse than a wrong number in one specific way: there is nothing
    // to look at. So the sort has to be total and stable, and nothing in it may
    // depend on reflection order, on a hash's iteration order, or on which class
    // was compiled first.
    //
    // ASKED AS A PAIR OF IDENTICAL DECLARATIONS. Comparing one class against
    // itself would compare a cached answer with itself and pass whatever the
    // packer did; two separately named classes with identical members are two
    // separate runs of the same arithmetic, and they must agree field for field.
    //-------------------------------------------------------------------------
    [Fact]
    public void TheLayoutIsAFunctionOfTheDeclarationAlone()
    {
        var one = TwinOne.NetLayout;
        var two = TwinTwo.NetLayout;

        Assert.True(one.Size == two.Size, $"two identical declarations are the same size ({one.Size} vs {two.Size})");

        Assert.True(one.Fields.Count == two.Fields.Count, "and have the same number of fields");

        bool same = true;

        for (int i = 0; i < one.Fields.Count; i++)
        {
            var a = one.Fields[i];
            var b = two.Fields[i];

            same &= a.Name == b.Name && a.Kind == b.Kind && a.Offset == b.Offset
                    && a.Bits == b.Bits && a.Bytes == b.Bytes && a.BitShift == b.BitShift;
        }

        Assert.True(same, "every field is placed identically, in the same order, in both");

        // AND THE HASH AGREES, which is the number the two sides actually
        // compare. Computed from the described fields rather than asserted as a
        // literal, because a literal here would pin the hash FUNCTION and this
        // is about the layout being stable under it.
        var describedOne = NetSchema.FromStandalone(TestServices.NoTiers, one, published: true);
        var describedTwo = NetSchema.FromStandalone(TestServices.NoTiers, two, published: true);

        Assert.True(describedOne.SchemaHash == describedTwo.SchemaHash, "and the schema hash the two sides compare is the same number");

        // Twice, from the same layout: the description is a computation and not
        // a reading of anything mutable.
        Assert.True(NetSchema.FromStandalone(TestServices.NoTiers, one, published: true).SchemaHash
              == describedOne.SchemaHash, "asked twice of one layout, it answers the same");
    }

    //-------------------------------------------------------------------------
    // A SHUFFLE ACROSS BUCKETS CHANGES NOTHING.
    //
    // This is the test the twins above could not be. They declare their fields
    // in the same order as each other, so a packer that had no buckets at all
    // and simply laid fields down as declared would pass them and report a
    // clean determinism check. Here the two classes declare the SAME three
    // fields in OPPOSITE orders, one field from each of three buckets, and the
    // claim is that the layout does not notice: the float is a rule-1 field
    // whichever line it is on, the bool is a rule-3 leftover, the string is a
    // rule-4 buffer, and the buckets are laid down in their order and not in
    // the source's.
    //
    // A packer that respected source order over the buckets fails on the first
    // assertion, which is what makes this worth having.
    //-------------------------------------------------------------------------
    [Fact]
    public void AShuffleAcrossBucketsDoesNotMoveAnything()
    {
        var declared = AcrossInBucketOrder.NetLayout;
        var reversed = AcrossReversed.NetLayout;

        // Derived: rule 1 takes Scale at word 0, rule 3 takes Flag at word 1
        // with 31 bits spare, rule 4 takes Name on the fresh word after it.
        foreach (var layout in new[] { declared, reversed })
        {
            Assert.True(layout.Fields.Select(f => f.Name).SequenceEqual(
                    new[] { "Scale", "Flag", "Name" }),
                "the placed order is the buckets' whichever order the source used");

            Assert.True(layout["Scale"].Offset == Block + 0 && layout["Flag"].Offset == Block + 4
                  && layout["Name"].Offset == Block + 8, "and the offsets are the buckets' too: 0, 4, 8");

            Assert.True(layout.Size == Block + 24, $"24 bytes either way, not {layout.Size}");
        }

        // Field for field, which is the form that would catch a shift or a
        // width moving while the offsets stayed put.
        bool same = declared.Fields.Count == reversed.Fields.Count;

        for (int i = 0; same && i < declared.Fields.Count; i++)
        {
            var a = declared.Fields[i];
            var b = reversed.Fields[i];

            same = a.Name == b.Name && a.Kind == b.Kind && a.Offset == b.Offset
                   && a.Bits == b.Bits && a.Bytes == b.Bytes && a.BitShift == b.BitShift;
        }

        Assert.True(same, "reversing the declaration moves no field by any measure");

        // AND THE HASH IS THE SAME NUMBER, which is the consequence that
        // matters on the wire: two builds that disagreed only about the order
        // three fields were typed in would refuse each other's join.
        Assert.True(NetSchema.FromStandalone(TestServices.NoTiers, declared, published: true).SchemaHash
              == NetSchema.FromStandalone(TestServices.NoTiers, reversed, published: true).SchemaHash, "so the two schema hashes are equal and the two builds would pair");
    }

    //-------------------------------------------------------------------------
    // AND A SHUFFLE WITHIN ONE BUCKET DOES MOVE THEM.
    //
    // The other half, and it has to be asserted or the half above could pass
    // for the wrong reason: a packer that sorted fields by width or by name and
    // ignored declaration order entirely would also be unmoved by a shuffle,
    // and would be a different bug wearing the same green tick.
    //
    // The stated rule is declaration order WITHIN each bucket, so two floats
    // swap when they are swapped in the source. That is a schema-hash change
    // and it is meant to be one: it is the same class of edit as renaming a
    // field, and the refusal is how a half-rebuilt pair finds out.
    //-------------------------------------------------------------------------
    [Fact]
    public void AShuffleWithinABucketDoesMoveThem()
    {
        var ab = WithinAB.NetLayout;
        var ba = WithinBA.NetLayout;

        Assert.True(ab["A"].Offset == Block + 0 && ab["B"].Offset == Block + 4, "declared A then B, A leads");

        Assert.True(ba["B"].Offset == Block + 0 && ba["A"].Offset == Block + 4, "declared B then A, B leads");

        Assert.True(ab["A"].Offset != ba["A"].Offset, "so swapping two fields of one bucket moves them");

        Assert.True(ab.Size == ba.Size, "though the class is the same size, which is why nothing but the hash would notice");

        Assert.True(NetSchema.FromStandalone(TestServices.NoTiers, ab, published: true).SchemaHash
              != NetSchema.FromStandalone(TestServices.NoTiers, ba, published: true).SchemaHash, "and the schema hash differs, so two builds that disagree about it refuse each other");
    }

    //-------------------------------------------------------------------------
    // A RULE-2 RUN IS CONTIGUOUS, AND MOVING ONE FIELD INTO IT BREAKS IT.
    //
    // A consequence of the sliding window that a reader would not guess, and
    // therefore one to state rather than leave as an implementation detail.
    // 16 + 8 + 8 is exactly 32, so P, Q and R fill a word when they are
    // declared together. Put an unrelated three-bit field BETWEEN them and
    // there is no longer a contiguous run summing to 32 -- the window opens on
    // P, reaches 35 at R, drops P to get back under, and closes on nothing --
    // so all four are leftovers instead.
    //
    // Both classes are 8 bytes and hold the same four fields; what differs is
    // which word each is in and how much of it is spare. That is the price of
    // contiguity, and it is the right price: the alternative is a subset-sum
    // over the declaration, which packs no tighter here and which nobody can
    // do in their head while reading the source.
    //-------------------------------------------------------------------------
    [Fact]
    public void AnExactFillRunHasToBeContiguous()
    {
        var joined = RunJoined.NetLayout;
        var split  = RunSplit.NetLayout;

        // Joined: P, Q, R are a rule-2 word; the three-bit field is the only
        // leftover and starts the next word.
        Assert.True(joined["P"].Offset == Block + 0 && joined["Q"].Offset == Block + 0 && joined["R"].Offset == Block + 0, "declared together, the three that add to 32 share one word");

        Assert.True(joined["P"].BitShift == 0 && joined["Q"].BitShift == 16 && joined["R"].BitShift == 24, "filling it exactly");

        Assert.True(joined["Interloper"].Offset == Block + 4, "and the field that is not part of the fill follows in the next word");

        // Split: no contiguous run sums to 32, so everything is a leftover and
        // packs in declaration order -- 16 + 3 + 8 is 27, and R does not fit
        // behind it.
        Assert.True(split["P"].Offset == Block + 0 && split["Interloper"].Offset == Block + 0 && split["Q"].Offset == Block + 0, "with the interloper between them there is no run, so the first three pack as leftovers");

        Assert.True(split["P"].BitShift == 0 && split["Interloper"].BitShift == 16 && split["Q"].BitShift == 19, "in declaration order, 27 bits of the word");

        Assert.True(split["R"].Offset == Block + 4, "and R starts the next word rather than straddling");

        // The point, stated as the difference: the same four fields, the same
        // size, different words.
        Assert.True(joined.Size == split.Size && joined.Size == Block + 8, $"both are 8 bytes ({joined.Size} and {split.Size})");

        Assert.True(joined["R"].Offset != split["R"].Offset, "so moving one declaration into the middle of a run moves the run's fields");
    }

    //-------------------------------------------------------------------------
    // AND THE RULE HOLDS FOR EVERY CLASS THAT REALLY EXISTS.
    //
    // The fixtures above are chosen to be awkward; these are the thirty-odd
    // classes the mod actually ships, and the claim is that reading any of their
    // layouts top to bottom gives the four buckets in order. A class added later
    // that breaks the shape fails here rather than at whatever moment somebody
    // next reads the pane.
    //
    // None of them declares a Group, which is checked rather than assumed: the
    // buckets are per group, so a grouped class would legitimately interleave
    // them and this sweep would be wrong to fail it.
    //-------------------------------------------------------------------------
    [Fact]
    public void EveryDeclaredClassReadsInBucketOrder()
    {
        // DISCOVER WALKS EVERY LOADED ASSEMBLY, so the fixtures at the top of
        // this file come back with the shipped classes -- and one of them
        // declares a Group deliberately and interleaves the buckets, which is
        // legal and is exactly what AGroupBoundaryBeatsAnExactFill asserts. The
        // sweep is about the classes the mod ships, so it takes those.
        var shared = typeof(BeamNetState).Assembly;

        var states = StandaloneNetStates.Discover(shared, new RecordingLog())
                                        .Where(s => s.Type.Assembly == shared)
                                        .ToArray();

        Assert.True(states.Length >= 30, $"the shared assembly declares the classes to sweep ({states.Length})");

        // THE GROUP DETECTOR, PROVED BEFORE IT IS TRUSTED. The claim below is
        // that no shipped class declares a Group -- an assertion that something
        // is absent, which passes just as happily when the search is broken. So
        // it is run first against the fixture that DOES declare one: three
        // members, three hints. A reflection change that stopped finding them
        // fails here rather than turning the sweep into a formality.
        Assert.True(GroupHintsOn(typeof(GroupedExact)) == 3, "the Group hints on the grouped fixture are visible to reflection");

        foreach (var state in states)
        {
            // The buckets are PER GROUP, so a class with a Group hint would
            // interleave them legitimately and this sweep would be wrong to fail
            // it. None of the shipped classes has one; checked, not believed.
            Assert.True(GroupHintsOn(state.Type) == 0, $"{state.ClassName} declares no Group, so its buckets do not interleave");

            // Every placed name is distinct, which is the totality claim the
            // sweep can make on its own: a field that landed in two buckets
            // would be here twice, and one that landed in none would leave a
            // hole the offsets below would not notice.
            Assert.True(state.Fields.Select(f => f.Name).Distinct().Count() == state.Fields.Count, $"{state.ClassName} places each field exactly once");

            int previous = -1;
            int behind   = -1;

            foreach (var field in state.Fields)
            {
                int bucket = BucketFromTheLayout(field);

                Assert.True(bucket >= previous, $"{state.ClassName}.{field.Name} is in bucket {bucket} after a bucket {previous} field");

                previous = bucket;

                // Offsets never go backwards, which is what makes "read it top
                // to bottom" mean anything at all. Equal is allowed and is the
                // normal case inside a packed word.
                Assert.True(field.Offset >= behind, $"{state.ClassName}.{field.Name} at {field.Offset} is behind the field before it");

                behind = field.Offset;
            }

            // AND NOTHING WIDE SHARES A PACKED WORD. The fresh-word rule,
            // swept: a buffer starting inside the leftovers' word would still
            // work and would silently couple the two on the wire.
            foreach (var wide in state.Fields.Where(f => BucketFromTheLayout(f) == 2))
            {
                Assert.True(state.Fields.Where(f => f.IsPacked).All(p => p.Offset != wide.Offset), $"{state.ClassName}.{wide.Name} begins on a word of its own");
            }
        }
    }

    /// <summary>
    /// Which of the three visible buckets a placed field is in: 0 for a whole
    /// word, 1 for a packed one, 2 for anything wider than a word.
    ///
    /// <para>
    /// REBUILT FROM THE LAYOUT AND NOT ASKED OF THE GENERATOR, which is the
    /// point of the sweep: a reader with only the pane in front of them can tell
    /// these apart, and so can this. Rules 2 and 3 collapse into one here
    /// because they are indistinguishable from a placement alone — an exactly
    /// full word and a leftover word that happens to be full look the same — and
    /// the fixtures above are where that distinction is pinned.
    /// </para>
    ///
    /// <para>
    /// AN ARRAY ELEMENT IS DOTTED. The layout expands <c>PaneBits</c> into eight
    /// <c>PaneBits.N</c> fields of four bytes each, so element-wise they look
    /// like whole words and are not: the array is one declaration, it is wider
    /// than a word, and it is placed as one thing in rule 4. The dot in the name
    /// is what says so, and it is the only thing that does.
    /// </para>
    /// </summary>
    private static int BucketFromTheLayout(NetFieldLayout field)
        => field.IsPacked                                     ? 1
         : field.Bytes > 4                                    ? 2
         : field.Kind == NetFieldKind.String                  ? 2
         : field.Name.Contains('.', StringComparison.Ordinal) ? 2
         : 0;

    /// <summary>
    /// How many of a state type's members name a
    /// <see cref="NetFieldAttribute.Group"/>.
    ///
    /// <para>
    /// BOTH PROPERTIES AND FIELDS, because the generator moves the attribute
    /// from the partial property it read onto the backing field it wrote, and
    /// which of the two carries it at runtime is an implementation detail of the
    /// generator rather than something this should depend on. Counted by
    /// declaration NAME so a member found as both is one member — a backing
    /// field is named <c>&lt;Tint&gt;k__BackingField</c> for the property it
    /// belongs to.
    /// </para>
    ///
    /// <para>
    /// AN UNADORNED PARTIAL PROPERTY IS A NETWORKED FIELD TOO, and carries no
    /// attribute at all — most shipped classes declare every field that way. So
    /// this counts HINTS and not fields; the field count is pinned by the
    /// fixtures above, which list theirs.
    /// </para>
    /// </summary>
    private static int GroupHintsOn(Type type)
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in type.GetProperties(Any).Cast<MemberInfo>().Concat(type.GetFields(Any)))
        {
            var attribute = member.GetCustomAttribute<NetFieldAttribute>();

            if (attribute?.Group is not { Length: > 0 })
                continue;

            string name = member.Name.StartsWith('<')
                ? member.Name.Substring(1, member.Name.IndexOf('>') - 1)
                : member.Name;

            seen.Add(name);
        }

        return seen.Count;
    }
}
