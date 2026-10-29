using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The generated net tables.
///
/// <para>
/// Worth testing on its own because every way it can be wrong is silent. The
/// server and the client build their tables from one declaration, and the engine
/// pairs them by NAME and ORDER during the connect handshake — so a field that
/// resolves differently on the two sides is not a wrong value, it is a client
/// that begins joining and stops with nothing in any log.
/// </para>
///
/// <para>
/// And worth testing because it is GENERATED: nobody reads generated code, so a
/// property wired to the wrong offset or a decode that skips one would look
/// exactly like working code. These declarations exist for no other purpose than
/// to be checked.
/// </para>
/// </summary>
public partial class NetTableTests
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

    /// <summary>Every kind, and the alignment case that matters.</summary>
    [StandaloneNetState("ss_test_every_kind")]
    private partial class EveryKind
    {
        public partial Vector Origin { get; set; }
        [NetField(Bits = 11)] public partial int ModelIndex { get; set; }
        [NetString(64)] public partial string Classname { get; set; }
        public partial bool Broken { get; set; }

        // The one that matters: a float after a single byte must not land on an
        // odd address, because the native side reads the member directly.
        [NetField(Name = "m_flPanelWidth")] public partial float PanelWidth { get; set; }
    }

    /// <summary>The narrow integers, which are the point of reading the type.</summary>
    [StandaloneNetState("ss_test_narrow")]
    private partial class Narrow
    {
        public partial sbyte  Tiny          { get; set; }
        public partial short  Small         { get; set; }
        public partial int    Whole         { get; set; }
        public partial byte   UnsignedTiny  { get; set; }
        public partial ushort UnsignedSmall { get; set; }
        [NetField(Bits = 10)] public partial short Narrowed { get; set; }
    }

    /// <summary>A fixed-length run, and something either side of it.</summary>
    [StandaloneNetState("ss_test_array")]
    private partial class WithArray
    {
        public partial bool Before { get; set; }
        [NetArray(4)] public partial NetArray<uint> Words { get; }
        public partial short After { get; set; }
    }

    /// <summary>Defaults, stated nowhere.</summary>
    [StandaloneNetState("ss_test_defaults")]
    private partial class Defaults
    {
        public partial int Count { get; set; }
        public partial string Name { get; set; }
    }

    /// <summary>
    /// Every narrow shape, for the round trip at the extremes.
    ///
    /// One of each thing that can be got wrong: a signed field that must sign
    /// extend, an unsigned one that must not, an unsigned FIELD in a signed C#
    /// type (a bitmask, which is how the mistake this found was spelled), a
    /// field at a non-zero shift, and one that fills a word exactly.
    /// </summary>
    [StandaloneNetState("ss_test_packed")]
    private partial class Packed
    {
        [NetField(Bits = 3)]  public partial int    Signed3   { get; set; }
        [NetField(Bits = 5)]  public partial byte   Unsigned5 { get; set; }
        [NetField(Bits = 10)] public partial short  Signed10  { get; set; }
        [NetField(NetFieldKind.UInt, Bits = 8)] public partial int Flags8 { get; set; }
        public partial bool Flag { get; set; }
        public partial uint Whole { get; set; }
    }

    /// <summary>
    /// The Group hint, and the whole of what it buys: a field in one group never
    /// shares a word with a field in another, however much room is left.
    /// </summary>
    [StandaloneNetState("ss_test_grouped")]
    private partial class Grouped
    {
        [NetField(Group = "hot")]  public partial byte HotA { get; set; }
        [NetField(Group = "cold")] public partial byte ColdA { get; set; }
        [NetField(Group = "hot")]  public partial byte HotB { get; set; }
        [NetField(Group = "cold")] public partial byte ColdB { get; set; }
        [NetField]                 public partial byte Ungrouped { get; set; }
    }

    //-------------------------------------------------------------------------
    // THE LAYOUT OF EveryKind.
    //
    // These were one [Fact] with sixteen assertions that ended by CALLING eight
    // other facts in this class -- so each of those ran twice per suite and the
    // second run reported under this one's name. Nothing calls them now.
    //-------------------------------------------------------------------------

    private static StandaloneNetState EveryKindLayout => StandaloneNetStates.Of(typeof(EveryKind));

    [Fact]
    public void TheWholeWordsLeadThenTheLeftoversThenTheBuffers()
    {
        // PLACED ORDER, NOT DECLARATION ORDER (Part 10, Step 8), AND THE FOUR
        // BUCKETS DECIDE IT. Derived rather than read off the output:
        //
        //   rule 1, naturally four bytes   PanelWidth (a float)
        //   rule 2, narrow and adding to 32  -- nothing: the two narrow fields
        //                                    are 11 and 1 bits, which is 12
        //   rule 3, the remaining narrow ones  ModelIndex, then Broken
        //   rule 4, wider than a word        Origin (12 bytes), Classname (64)
        //
        // Declaration order still decides the order WITHIN a bucket, and within
        // a group. This used to read ModelIndex, Broken, Origin, Classname,
        // m_flPanelWidth, because the old packer had one narrow pass and one
        // whole pass and a lone float sorted behind a 64-byte string.
        Assert.True(EveryKindLayout.Fields.Select(f => f.Name).SequenceEqual(
                new[] { "m_flPanelWidth", "ModelIndex", "Broken", "Origin", "Classname" }),
            "the whole words lead, then the leftovers, then the buffers");
    }

    [Fact]
    public void AnExplicitWireNameOverridesThePropertys()
        => Assert.True(EveryKindLayout["m_flPanelWidth"].Kind == NetFieldKind.Float,
            "an explicit wire name overrides the property's");

    [Fact]
    public void TheKindComesOffTheCSharpType()
    {
        var layout = EveryKindLayout;

        Assert.True(layout["Origin"].Kind == NetFieldKind.Vector
              && layout["ModelIndex"].Kind == NetFieldKind.Int
              && layout["Classname"].Kind == NetFieldKind.String
              && layout["Broken"].Kind == NetFieldKind.Bool
              && layout["m_flPanelWidth"].Kind == NetFieldKind.Float,
            "the kind comes off the C# type");
    }

    [Fact]
    public void StatedBitsAreKept()
        => Assert.True(EveryKindLayout["ModelIndex"].Bits == 11, "stated bits are kept");

    [Fact]
    public void AnIntWithNoBitsGetsItsTypesWidth()
        => Assert.True(StandaloneNetStates.Of(typeof(Defaults)).Fields[0].Bits == 32, "an int with no bits gets its type's width");

    [Fact]
    public void AStringWithNoLengthGetsTheDefault()
        => Assert.True(StandaloneNetStates.Of(typeof(Defaults)).Fields[1].Bits == NetTable.DefaultStringLength, "a string with no length gets the default");

    // Storage: the native shell keeps a class's networked state in one blob and
    // the props are offsets into it, so these numbers decide whether a class
    // fits.

    [Fact]
    public void AVectorIsTwelveBytes()
        => Assert.True(EveryKindLayout["Origin"].Bytes == 12, "a vector is twelve bytes");

    [Fact]
    public void AStringCostsItsBuffer()
        => Assert.True(EveryKindLayout["Classname"].Bytes == 64, "a string costs its buffer");

    [Fact]
    public void ABoolIsOneBitSharingTheWordItSitsIn()
    {
        // A BOOL IS ONE BIT NOW, where it used to be one byte. The old comment
        // said "a prop's offset is taken against real storage and there is no
        // addressable bit" -- true while a bool was its own SendProp, and gone
        // with the typed props: the payload is dwords and what a bit means
        // inside one is this side's business entirely.
        var layout = EveryKindLayout;

        Assert.True(layout["Broken"].Bits == 1
              && layout["Broken"].Bytes == 4
              && layout["Broken"].IsPacked, "a bool is one bit, sharing the word it sits in");
    }

    // The float takes word 0; ModelIndex 11 bits at word 1 bit 0 and Broken 1
    // bit at word 1 bit 11 are the leftovers; then the two buffers from byte 8:
    // vector 8..20, string 20..84.

    [Fact]
    public void TheOneNaturallyFourByteFieldLeads()
        => Assert.True(EveryKindLayout["m_flPanelWidth"].Offset == Block + 0, "the one naturally-four-byte field leads");

    [Fact]
    public void TheFirstLeftoverStartsAtBitZeroOfTheNextWord()
    {
        var layout = EveryKindLayout;

        Assert.True(layout["ModelIndex"].Offset == Block + 4
              && layout["ModelIndex"].BitShift == 0, "the first leftover starts at bit zero of the next word");
    }

    [Fact]
    public void TheNextLeftoverFollowsItInsideTheSameWord()
    {
        var layout = EveryKindLayout;

        Assert.True(layout["Broken"].Offset == Block + 4
              && layout["Broken"].BitShift == 11, "the next leftover follows it inside the same word");
    }

    [Fact]
    public void TheFieldsWiderThanAWordStartAtTheWordAfterTheLeftovers()
    {
        // AND THE BUFFERS START ON A FRESH WORD. The leftovers' word has 20 bits
        // spare and the vector does not reach back for them: a shared word is
        // charged to everything in it, so a Vector filling those bits could not
        // move without ModelIndex and Broken going too.
        var layout = EveryKindLayout;

        Assert.True(layout["Origin"].Offset == Block + 8
              && layout["Classname"].Offset == Block + 20, "the fields wider than a word start at the word after the leftovers");
    }

    [Fact]
    public void TheBlobIsBigEnoughForTheLastField()
        => Assert.True(EveryKindLayout.Size == Block + 84, "the blob is big enough for the last field");

    [Fact]
    public void PackingCostThisClassNoWordsAndSavedItOne()
    {
        // The saving, stated as the thing it is: two fields that used to cost a
        // word each cost one between them, and the class is a word shorter.
        var layout = EveryKindLayout;

        // The unpacked class would have been 88 bytes of its own; both numbers
        // are past the engine block, so the WORD saving is the difference
        // between them and not between the two blob sizes.
        Assert.True(layout.Size == Block + 84 && (Block + 88) / 4 - layout.Size / 4 == 1, "packing cost this class no words and saved it one");
    }

    [Fact]
    public void AStateTypeIsDescribedOnce()
    {
        // Asked twice, the same table: it is a static the generator wrote, not
        // something worked out per call.
        Assert.True(ReferenceEquals(StandaloneNetStates.Of(typeof(EveryKind)),
                              StandaloneNetStates.Of(typeof(EveryKind))), "a state type is described once");
    }

    /// <summary>
    /// The width of an integer comes from its C# type.
    ///
    /// <para>
    /// Which is the whole reason to read the type: an author who wants sixteen
    /// bits writes <c>short</c> rather than an <c>int</c> and a bit count beside
    /// it that has to agree. The two numbers a field carries stay separate —
    /// BITS is what goes on the wire and BYTES is what it occupies — and they
    /// differ exactly when a value is narrowed.
    /// </para>
    /// </summary>
    [Fact]
    public void NarrowIntegers()
    {
        var layout = StandaloneNetStates.Of(typeof(Narrow));

        Assert.True(layout["Tiny"].Bits == 8 && layout["Small"].Bits == 16
              && layout["Whole"].Bits == 32, "an integer's width is its type's");

        Assert.True(layout["UnsignedTiny"].Kind == NetFieldKind.UInt
              && layout["UnsignedSmall"].Kind == NetFieldKind.UInt
              && layout["Tiny"].Kind == NetFieldKind.Int, "an unsigned type is an unsigned field");

        // THE MEANING OF Bits CHANGED HERE, and this check is the one that has
        // to change with it. It used to say "narrowing the wire does not narrow
        // the storage", because the storage was the C# type's and every offset
        // after the field depended on it. Packing places what follows from the
        // WIDTH, so ten bits occupies ten bits — and the truncation that rule
        // accepted became FieldRangeException, which is checked in Packing().
        Assert.True(layout["Narrowed"].Bits == 10 && layout["Narrowed"].IsPacked, "a narrowed field occupies its declared width, not its type's");

        // THE BUCKETS, DERIVED. Whole is 32 bits, so it is naturally four bytes
        // and leads at word 0. Then the narrow fields in declaration order —
        // Tiny 8, Small 16, Whole (skipped, not narrow), UnsignedTiny 8,
        // UnsignedSmall 16, Narrowed 10 — and the sliding window finds
        // 8 + 16 + 8 = 32 across Tiny, Small and UnsignedTiny: an EXACT fill,
        // so those three take word 1 between them under rule 2. UnsignedSmall
        // and Narrowed are 16 and 10, which is 26 and not a word, so they are
        // leftovers under rule 3 and take word 2 with six bits spare.
        //
        // This is the same THREE WORDS the old packer produced from one narrow
        // pass; what moved is which word each field is in.
        Assert.True(!layout["Whole"].IsPacked && layout["Whole"].Offset == Block + 0
              && layout["Whole"].Bytes == 4, "a full-width integer is not packed and leads, taking its own word");

        Assert.True(layout["Tiny"].Offset == Block + 4 && layout["Tiny"].BitShift == 0
              && layout["Small"].Offset == Block + 4 && layout["Small"].BitShift == 8
              && layout["UnsignedTiny"].Offset == Block + 4 && layout["UnsignedTiny"].BitShift == 24, "the three that add to exactly 32 fill one word between them");

        Assert.True(layout["UnsignedSmall"].Offset == Block + 8 && layout["UnsignedSmall"].BitShift == 0
              && layout["Narrowed"].Offset == Block + 8 && layout["Narrowed"].BitShift == 16, "and the two that add to 26 are leftovers in the word after it");

        // THE SPARE BITS ARE ALL IN ONE PLACE, which is what rule 2 buys: every
        // packed word but the last is full.
        var words = layout.Fields.Where(f => f.IsPacked)
                          .GroupBy(f => f.Offset)
                          .OrderBy(g => g.Key)
                          .Select(g => g.Sum(f => f.Bits))
                          .ToArray();

        Assert.True(words.SequenceEqual(new[] { 32, 26 }), "one word is filled exactly and only the last has room left");

        // NO FIELD STRADDLES A DWORD. Stated over the whole layout rather than
        // field by field, because it is the rule the accessors depend on: a
        // straddling field would cost two props on the wire and need a two-word
        // read-modify-write, and nothing else here would notice.
        foreach (var field in layout.Fields)
        {
            Assert.True(field.BitShift + field.Bits <= 32 || !field.IsPacked, $"'{field.Name}' does not straddle a word");
        }

        Assert.True(layout.Size == Block + 12, "the blob ends past the last field");

        Assert.True(14 / 4 + 1 - (layout.Size - Block) / 4 == 1, "packing saved this class a word");
    }

    /// <summary>
    /// The packed accessors, at the extremes of every declared width.
    ///
    /// <para>
    /// THIS IS THE SHAPE THE PINNING TESTS HAD TO TAKE (Part 10). They used to
    /// assert that the generated layout equalled what .NET would lay out — which
    /// stopped being either true or interesting once the generator owns the
    /// placement. What matters instead is that every field round-trips at its
    /// declared width including at its limits, and that a field's neighbours in
    /// its word are untouched by writing it. Weaker in form and much stronger in
    /// what it covers: it tests the accessors rather than an arithmetic
    /// coincidence.
    /// </para>
    /// </summary>
    private static void Packing()
    {
        var layout = StandaloneNetStates.Of(typeof(Packed));
        var state = new Packed(); state.BindLocal();

        // THE EXTREMES, which is where sign extension and masking both live. A
        // three-bit signed field holds -4..3; getting the extension wrong gives
        // a plausible number and no error, which is the whole reason this is
        // checked at the ends rather than in the middle.
        state.Signed3 = -4;
        Assert.True(state.Signed3 == -4, "a narrow signed field round-trips at its minimum");
        state.Signed3 = 3;
        Assert.True(state.Signed3 == 3, "a narrow signed field round-trips at its maximum");
        state.Signed3 = -1;
        Assert.True(state.Signed3 == -1, "minus one is minus one and not seven");

        state.Signed10 = -512;
        Assert.True(state.Signed10 == -512, "a ten-bit signed field round-trips at its minimum");
        state.Signed10 = 511;
        Assert.True(state.Signed10 == 511, "a ten-bit signed field round-trips at its maximum");

        state.Unsigned5 = 31;
        Assert.True(state.Unsigned5 == 31, "a narrow unsigned field round-trips at its maximum");
        state.Unsigned5 = 0;
        Assert.True(state.Unsigned5 == 0, "and at zero");

        state.Flags8 = 255;
        Assert.True(state.Flags8 == 255, "an unsigned field in a signed type stays positive above 127");

        state.Flag = true;
        Assert.True(state.Flag, "a bool round-trips true");
        state.Flag = false;
        Assert.True(!state.Flag, "a bool round-trips false");

        state.Whole = uint.MaxValue;
        Assert.True(state.Whole == uint.MaxValue, "a full-width field is untouched by packing");

        // NEIGHBOURS. The failure packing can introduce that nothing else would
        // report: a set that writes its own bits and somebody else's. Every
        // field is given a distinctive value, then each is rewritten in turn and
        // the rest are checked to have survived.
        state.Signed3   = -3;
        state.Unsigned5 = 21;
        state.Signed10  = -321;
        state.Flags8    = 200;
        state.Flag      = true;
        state.Whole     = 0xFEEDFACEu;

        bool intact = true;

        void Survives()
        {
            if (state.Signed3 != -3 || state.Unsigned5 != 21 || state.Signed10 != -321
                || state.Flags8 != 200 || !state.Flag || state.Whole != 0xFEEDFACEu)
            {
                intact = false;
            }
        }

        state.Signed3 = -3;    Survives();
        state.Unsigned5 = 21;  Survives();
        state.Signed10 = -321; Survives();
        state.Flags8 = 200;    Survives();
        state.Flag = true;     Survives();

        Assert.True(intact, "writing one packed field leaves the ones sharing its word alone");

        // RANGE. Since packing, Bits narrows the storage, so a value that does
        // not fit would bleed into the neighbour above rather than being
        // truncated on the wire. It is refused instead, and the exception names
        // the field, the value and the width.
        Assert.True(Throws(() => state.Signed3 = 4), "a signed field refuses a value above its width");

        Assert.True(Throws(() => state.Signed3 = -5), "and below it");

        Assert.True(Throws(() => state.Unsigned5 = 32), "an unsigned field refuses a value above its width");

        // The case the tree was actually carrying: a bitmask declared as an int
        // and narrowed. A negative would have been masked into a large positive
        // with nothing saying so.
        Assert.True(Throws(() => state.Flags8 = -1), "an unsigned field in a signed type refuses a negative");

        Assert.True(Throws(() => state.Flags8 = 256), "and a value above its width");

        // A refused write must leave the blob alone, which is the same rule
        // every other throw on this path follows — see NetBlobView.Write.
        state.Unsigned5 = 7;
        try { state.Unsigned5 = 99; } catch (FieldRangeException) { }
        Assert.True(state.Unsigned5 == 7, "a refused write does not change the value");

        // A field wide enough for its type gets no check at all, because none
        // could ever fire — a byte in eight bits cannot overflow eight bits.
        Assert.True(!Throws(() => { var s = new Packed(); s.BindLocal(); s.Whole = uint.MaxValue; }), "a field as wide as its type accepts its whole range");

        // And the arithmetic the generator used to bound them, so the numbers
        // above are pinned against the rule rather than against themselves.
        Assert.True(NetFieldRange.SignedRange(3) == (-4L, 3L)
              && NetFieldRange.SignedRange(10) == (-512L, 511L)
              && NetFieldRange.UnsignedMax(5) == 31UL
              && NetFieldRange.UnsignedMax(8) == 255UL, "the declared ranges are the widths' own");

        // NO FIELD STRADDLES A WORD, over a layout built to have one field of
        // every awkward width. This is the rule the wire cost depends on: a
        // straddling field would dirty two payload props instead of one.
        foreach (var field in layout.Fields)
        {
            Assert.True(!field.IsPacked || field.BitShift + field.Bits <= 32, $"packed '{field.Name}' fits inside one word");
        }

        // AND THE INSPECTOR AGREES WITH THE ACCESSORS. Two readers of one blob,
        // which is the check that replaced the payload shadow at Step 6 and is
        // the right one here too: the generated getter and NetSchema.Decode
        // compute the placement independently of each other.
        var described = NetSchema.FromStandalone(NoTiers, layout, published: true);
        var decoded = NetSchema.Decode(described, state.Snapshot())
                               .ToDictionary(d => d.Field.Name);

        Assert.True(decoded["Signed3"].Value == "-3" && decoded["Signed10"].Value == "-321", "the inspector decodes a packed signed field the same way");

        Assert.True(decoded["Unsigned5"].Value == "7" && decoded["Flags8"].Value == "200", "and a packed unsigned one");

        Assert.True(decoded["Flag"].Value == "true", "and a packed bool");
    }

    /// <summary>
    /// The <c>Group</c> hint. What it buys is one rule, and this is it: a field
    /// in one group never shares a word with a field in another.
    /// </summary>
    private static void Grouping()
    {
        var layout = StandaloneNetStates.Of(typeof(Grouped));

        int Word(string name) => layout[name].Offset;

        Assert.True(Word("HotA") == Word("HotB") && Word("ColdA") == Word("ColdB"), "fields in one group share a word");

        Assert.True(Word("HotA") != Word("ColdA")
              && Word("HotA") != Word("Ungrouped")
              && Word("ColdA") != Word("Ungrouped"), "fields in different groups do not, however much room is left");

        // Five bytes that would fit in two words unpacked take three grouped,
        // and that is the trade the hint exists to make: a word is charged to
        // every field in it that changed, so two fields that never change
        // together should not be paying for each other.
        Assert.True(Word("HotA") == 0 && Word("ColdA") == 4 && Word("Ungrouped") == 8
              && layout.Size == Block + 12, "the groups are in first-declared order, and one word each");

        // Ungrouped fields are ONE group, not one group each. Otherwise the
        // default — which is what almost every class declares — would be the
        // worst possible packing rather than the best.
        var ungrouped = StandaloneNetStates.Of(typeof(Narrow));
        Assert.True(ungrouped["Tiny"].Offset == ungrouped["Small"].Offset, "ungrouped fields form a single group");
    }

    /// <summary>
    /// What the schema hash has to notice, now that a placement is an offset AND
    /// a shift AND a width.
    ///
    /// <para>
    /// This is the guard the whole step rests on for the two-build case: packing
    /// moves every offset in every layout, so a client and a server built from
    /// different commits must refuse each other rather than read each other's
    /// bits. Two fields sharing a word have the same offset and the same storage
    /// size, so before this the hash could not tell them apart at all.
    /// </para>
    /// </summary>
    private static void PlacementIsInTheHash()
    {
        // Two fields in one word, differing only by which bits they own -- which
        // is precisely what offset and size cannot express.
        var asDeclared = new[]
        {
            new NetSchemaField("A", "byte", NetFieldKind.UInt, 0, 4, 8, 0),
            new NetSchemaField("B", "byte", NetFieldKind.UInt, 0, 4, 8, 8),
        };

        var swapped = new[]
        {
            new NetSchemaField("A", "byte", NetFieldKind.UInt, 0, 4, 8, 8),
            new NetSchemaField("B", "byte", NetFieldKind.UInt, 0, 4, 8, 0),
        };

        Assert.True(NetSchema.SchemaHashOf(asDeclared, 4) != NetSchema.SchemaHashOf(swapped, 4), "swapping two fields inside one word changes the schema hash");

        // AND THE WIDTH, which was uncovered before packing and would have stayed
        // uncovered: narrowing a field changed what both sides stored and left
        // the fingerprint alone. It shows up on the LAST field in a word, which
        // can change width without moving anything else.
        var narrower = new[]
        {
            new NetSchemaField("A", "byte", NetFieldKind.UInt, 0, 4, 8, 0),
            new NetSchemaField("B", "byte", NetFieldKind.UInt, 0, 4, 2, 8),
        };

        Assert.True(NetSchema.SchemaHashOf(asDeclared, 4) != NetSchema.SchemaHashOf(narrower, 4), "narrowing a field changes the schema hash");

        // The placement is one mixed value so that EntityLayout.Fingerprint --
        // which must stay the SAME function over the same values -- gains one
        // mix and not two. Pinned rather than trusted, because that identity is
        // checked elsewhere and this is what it is checked against.
        Assert.True(NetSchema.PlacementOf(16, 2) == ((16u << 16) | 2u)
              && NetSchema.PlacementOf(0, 32) == 32u, "a placement is a shift and a width in one value");
    }

    private static bool Throws(Action write)
    {
        try   { write(); return false; }
        catch (FieldRangeException) { return true; }
    }

    /// <summary>
    /// A fixed-length run.
    ///
    /// <para>
    /// Native never learns these belong together: they are laid out as that many
    /// consecutive fields and go over the ABI one at a time, which is why an
    /// array needed nothing of native that a single value did not.
    /// </para>
    /// </summary>
    [Fact]
    public void Arrays()
    {
        var layout = StandaloneNetStates.Of(typeof(WithArray));

        Assert.True(layout.Fields.Count == 1 + 4 + 1, "a run is one field per element");

        Assert.True(layout["Words.0"].Offset >= Block + 0 && layout["Words.3"].Offset >= Block + 0, "elements are named by index");

        // A RUN IS NEVER PACKED. Native sees it as that many consecutive fields
        // and the client's renderer indexes it by a fixed stride, so an element
        // sharing a word with the one before it would break the arithmetic on
        // both sides. The two narrow scalars go in word 0 and the run starts at
        // the next word: bool 1 bit at 0, short 16 bits at 1, then 4..20.
        Assert.True(layout["Words.0"].Offset == Block + 4 && layout["Words.1"].Offset == Block + 8
              && layout["Words.3"].Offset == Block + 16, "the run is word aligned and its elements are consecutive");

        Assert.True(!layout["Words.0"].IsPacked && !layout["Words.3"].IsPacked, "no element of a run is packed");

        Assert.True(layout["Before"].Offset == Block + 0 && layout["Before"].BitShift == 0
              && layout["After"].Offset == Block + 0 && layout["After"].BitShift == 1
              && layout.Size == Block + 20, "the scalars around the run share the word before it");

        // The elements write through and read back, and the array object is
        // never replaced — which is why the property is get-only.
        var state = new WithArray(); state.BindLocal();
        state.Words[2] = 0xDEADBEEF;
        Assert.True(state.Words[2] == 0xDEADBEEF, "an element is readable back");
        Assert.True(state.Words.Length == 4, "a run knows its length");

        state.Words.Fill(7u);
        Assert.True(state.Words[0] == 7u && state.Words[3] == 7u, "fill sets every element");

        // The round trip through a blob, which is what the client does. Written
        // through a sender rather than laid out by hand: Before and After share
        // word zero now, so a short copied to After's offset would take Before's
        // bit with it.
        var sender = new WithArray(); sender.BindLocal();
        sender.Before   = true;
        sender.Words[1] = 0x11223344u;
        sender.After    = -9;

        var received = new WithArray(); received.BindLocal();
        received.Decode(sender.Snapshot());

        Assert.True(received.Before && received.Words[1] == 0x11223344u && received.After == -9, "decode fills the run and its neighbours");

        // The surface's own packing, which is the reason the run exists.
        var glass = new BreakableSurfaceNetState(); glass.BindLocal();
        glass.FillPanes();
        Assert.True(glass.IsPaneSolid(0, 0) && glass.IsPaneSolid(15, 15), "a filled grid has every pane");

        glass.SetPaneSolid(3, 5, false);
        Assert.True(!glass.IsPaneSolid(3, 5) && glass.IsPaneSolid(4, 5) && glass.IsPaneSolid(3, 6), "one pane can be taken out");

        // The stride is the FULL grid, not the surface's own width, so a pane's
        // bit does not move when the grid is derived.
        Assert.True(!glass.IsPaneSolid(-1, 0) && !glass.IsPaneSolid(16, 0) && !glass.IsPaneSolid(0, 16), "panes outside the grid read as gone");
    }

    /// <summary>
    /// The numbers the schema hash is computed over.
    ///
    /// Pinned rather than trusted, because NetFieldKind is ordered for reading
    /// and these are not — they were SourceSharpFieldType over the ABI and grew
    /// in the order the ABI needed them, so a cast between the two would
    /// compile and run. Step 7 deleted the ABI half; what these decide now is
    /// the layout fingerprint both sides compute and every update carries, so
    /// renumbering one is a wire change on whichever side is rebuilt.
    /// </summary>
    [Fact]
    public void WireTypeCodes()
    {
        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.Int)    == 0, "int is wire type 0");
        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.Float)  == 1, "float is wire type 1");
        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.Vector) == 2, "vector is wire type 2");
        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.Bool)   == 3, "bool is wire type 3");
        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.String) == 4, "string is wire type 4");
        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.UInt) == 5, "unsigned is wire type 5");

        // The one that would be missed: every kind must HAVE a code. A new kind
        // added to the enum and not to the mapping throws here rather than
        // reaching native as whatever the default happened to be.
        bool everyKindMapped = true;
        foreach (NetFieldKind kind in Enum.GetValues<NetFieldKind>())
        {
            try   { StandaloneNetStates.TypeCodeOf(kind); }
            catch { everyKindMapped = false; }
        }

        Assert.True(everyKindMapped, "every kind has a wire type");
    }

    /// <summary>
    /// The declarations the mod actually ships, discovered from shared code in a
    /// stable order — both sides walk this sequence and must produce the same
    /// one, and reflection promises no order.
    /// </summary>
    [Fact]
    public void StateDeclarations()
    {
        var all = StandaloneNetStates.Discover(typeof(BarrelNetState).Assembly, new RecordingLog());

        Assert.True(all.Count > 0, "discovery finds the declarations");

        bool named = true, sized = true;
        foreach (var state in all)
        {
            if (string.IsNullOrEmpty(state.ClassName))
                named = false;

            // A declared state with no fields is a table with no props, which is
            // a class paying for a ServerClass and sending nothing.
            if (state.Fields.Count == 0 || state.Size <= 0)
                sized = false;
        }

        Assert.True(named, "every declared state names a class");
        Assert.True(sized, "every declared state has fields and a size");

        // One class, one table. Two declarations for the same name would race to
        // register a ServerClass under it, and the loser would silently not be
        // on the wire.
        Assert.True(all.Select(s => s.ClassName)
                 .Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count, "no class is declared twice");
    }

    /// <summary>
    /// The accessors and the decode, checked against a blob laid out by hand at
    /// the offsets the generator produced.
    /// </summary>
    [Fact]
    public void GeneratedState()
    {
        var layout = StandaloneNetStates.Of(typeof(BarrelNetState));

        Assert.True(layout["Thinks"].Bits == 16 && layout["Thinks"].Bytes == 4
              && layout["Thinks"].BitShift == 0 && layout["Broken"].BitShift == 16, "a short is sixteen bits, in the word it now shares");
        Assert.True(layout["NetModel"].Bits == 64, "a [NetString] length survives generation");

        // A state with STORAGE OF ITS OWN, which is what a client-side one is
        // (plan_nettable_generic.md Part 9). It has no entity to write through
        // to, so BindLocal gives it a pinned buffer and the accessors read and
        // write that instead. It is not "unbound": an unbound state has nowhere
        // to put a value and throws, which is checked in MappedMemoryTests.
        var state = new BarrelNetState();
        state.BindLocal();
        Assert.True(!state.IsBound, "a local state is not bound to an entity");
        Assert.True(state.IsLive, "but it has storage, so it is live");

        state.Thinks      = 7;
        state.LastDamage  = 2.5f;
        state.SpawnOrigin = new Vector(1f, 2f, 3f);
        state.Broken      = true;
        state.NetModel    = "models/props_c17/oildrum001.mdl";

        Assert.True(state.Thinks == 7 && state.LastDamage == 2.5f
              && state.SpawnOrigin.Y == 2f && state.Broken
              && state.NetModel == "models/props_c17/oildrum001.mdl", "a set is readable back");

        // The round trip the client actually performs, built the only way it
        // can be built now: through the writer. Laying the blob out by hand here
        // used to be the honest fixture and stopped being one when Thinks and
        // Broken started sharing a word — copying two bytes to Thinks' offset
        // would take Broken's bit with it. What this checks is unchanged and is
        // in fact stronger: the bytes one state wrote are the values another
        // reads back, field for field, through the same layout.
        var sender = new BarrelNetState(); sender.BindLocal();
        sender.Thinks      = 1234;
        sender.LastDamage  = 6.25f;
        sender.SpawnOrigin = new Vector(10f, 20f, 30f);
        sender.Broken      = true;
        sender.NetModel    = "models/x.mdl";

        byte[] blob = sender.Snapshot();

        var received = new BarrelNetState(); received.BindLocal();
        received.Decode(blob);

        Assert.True(received.Thinks == 1234
              && received.LastDamage == 6.25f
              && received.SpawnOrigin.X == 10f
              && received.SpawnOrigin.Y == 20f
              && received.SpawnOrigin.Z == 30f
              && received.Broken
              && received.NetModel == "models/x.mdl", "decode reads every field back");

        // A string stops at its terminator, not at the end of its buffer: the
        // bytes past it are whatever a longer previous value left there.
        var stale = new byte[layout.Size];
        Write(stale, layout["NetModel"], System.Text.Encoding.UTF8.GetBytes("short\0LEFTOVER"));

        var second = new BarrelNetState(); second.BindLocal();
        second.Decode(stale);
        Assert.True(second.NetModel == "short", "a string ends at its terminator");

        // Short blobs fail closed rather than reading past the end of what is,
        // on the real path, borrowed engine memory.
        var truncated = new BarrelNetState(); truncated.BindLocal();
        truncated.Decode(new byte[4]);
        Assert.True(truncated.NetModel.Length == 0 && !truncated.Broken, "a short blob decodes to defaults, not to a read past the end");

        // The literal on purpose: this is the string the connect handshake pairs
        // the two tables by, so the test pins what actually goes on the wire
        // rather than agreeing with the constant by construction.
        Assert.True(state.NetClassName == EntityNames.SsBarrel, "the state names its entity class");
    }

    /// <summary>
    /// The hierarchy's shared blob size — plan_nettable_generic.md Part 14
    /// Step 4, and the number the whole step turns on.
    ///
    /// <para>
    /// <c>HierarchyBlobBytes</c> is the greediest declaration on the hierarchy,
    /// and it is what C# sends native through <c>NetHierarchyDeclare</c>.
    /// </para>
    ///
    /// <para>
    /// WHAT IT MEANS CHANGED IN PART 18 and the arithmetic did not. It used to
    /// be the blob length EVERY entity on the hierarchy was allocated, because
    /// <c>operator new</c> is a static function with no idea which class the
    /// factory is about to make. Each class is sized to its own tier now, so
    /// this is a ceiling check — does the greediest declaration still fit any
    /// tier at all — and no longer the cost anybody pays.
    /// </para>
    ///
    /// <para>
    /// The two ways this can be wrong are both silent, and both are here: a
    /// number that depends on the ORDER the declarations arrive in, and a
    /// number derived from the classes a SIDE hosts, which differs between the
    /// client and the server.
    /// </para>
    /// </summary>
    [Fact]
    public void Hierarchy()
    {
        // Discovery, done HERE rather than inherited from whichever suite ran
        // first. The old runner's fixed order hid the dependency completely.
        StandaloneNetStates.Discover(typeof(BarrelNetState).Assembly, new RecordingLog());

        var barrel  = StandaloneNetStates.Of(typeof(BarrelNetState));
        var beam    = StandaloneNetStates.Of(typeof(BeamNetState));
        var surface = StandaloneNetStates.Of(typeof(BreakableSurfaceNetState));

        int Blob(params StandaloneNetState[] states)
            => StandaloneNetStates.HierarchyBlobBytes(states);

        Assert.True(Blob() == 0, "nothing declared is no blob at all");

        Assert.True(Blob(barrel) % 16 == 0 && Blob(beam) % 16 == 0 && Blob(surface) % 16 == 0, "the blob is sixteen-aligned, as SourceSharp_RuntimeBlobBytes rounds it");

        // The greediest, whichever it is. Written as a max rather than as the
        // name of a state, so that a state growing past the surface's still
        // makes this true rather than making it a stale literal.
        int greediest = Math.Max(Blob(barrel), Math.Max(Blob(beam), Blob(surface)));

        Assert.True(Blob(barrel, beam, surface) == greediest, "the hierarchy is sized to the greediest declaration");

        // THE ORDERING TRAP. Native learns the blob size from the class
        // descriptions as they arrive, so a word count derived when the first
        // class is built is smaller than one derived when the last is. Order
        // must not matter here, and this is what says so.
        Assert.True(Blob(barrel, beam, surface) == Blob(surface, beam, barrel)
              && Blob(barrel, beam, surface) == Blob(beam, surface, barrel), "the order the declarations arrive in does not change the number");

        // THE HOSTING TRAP. A state may be declared before one half of its
        // entity has been ported, so the two sides publish different sets. A
        // number derived from what a side publishes would differ between them
        // — and their payload prop counts with it, which is a table pair that
        // does not pair.
        Assert.True(Blob(barrel) != greediest || Blob(beam) != greediest
              || Blob(surface) != greediest, "a subset of the declarations is not the hierarchy's number");

        // The word count that falls out of it, which is what both sides put on
        // the wire. Whole, because the blob is 16-aligned — a shadow shorter
        // than the blob would silently shorten the client's byte-for-byte
        // comparison, which is the same class of failure as no comparison.
        Assert.True(Blob(barrel, beam, surface) % 4 == 0, "the blob is a whole number of four-byte payload words");

        Assert.True(Blob(barrel, beam, surface) / 4 <= 512, "and within the 512 words the prop-name table can name");

        // Each state's own fields must fit inside the shared blob, or its typed
        // props read past the end of every entity. True by construction of the
        // max above, and pinned because the construction is what a refactor
        // would change.
        foreach (var state in new[] { barrel, beam, surface })
        {
            Assert.True(state.Fields.All(f => f.Offset + f.Bytes <= Blob(barrel, beam, surface)), $"'{state.ClassName}' fits inside the shared blob");
        }

        //---------------------------------------------------------------------
        // AND EACH CLASS IS DESCRIBED AGAINST ITS OWN DECLARATION (Part 18).
        //
        // This block asserted the opposite until per-class allocation landed:
        // every type reported the hierarchy's blob and the hierarchy's payload
        // word count, whatever it had declared, because that was what native
        // reserved. The inspector is the oracle the wire is checked against, so
        // it has to report what native reserves — which is now the class's own
        // tier.
        //---------------------------------------------------------------------
        // A FOURTH AND A FIFTH STATE, AND THE ENGINE BLOCK IS WHY. barrel, beam
        // and surface declare 52 to 68 bytes between them, which used to
        // straddle the 64-byte tier boundary and so proved the tier was the
        // class's. Every blob opens with the engine block (plan_entity.md §3.2),
        // so all three land in one tier together and the difference they were
        // chosen for no longer shows.
        //
        // THE BLOCK HAS GROWN TWICE NOW AND THIS FIXTURE HAS FOLLOWED IT TWICE,
        // which is worth writing down as the cost it is rather than as a fixture
        // detail. At 352 bytes, player_manager's 340 declared bytes landed it in
        // 1024 while the three small classes shared 512, and two states were
        // enough. Step 6b's render policy took the block to 480, and at 480 the
        // three small classes land in 1024 as well -- 480 + 68 = 548, past the
        // 512 boundary -- so player_manager no longer differs from them. occluder
        // declares eight bytes and lands in 512, which restores the thing being
        // asserted: classes of different sizes, tiers of different sizes.
        //
        // In one sentence: sixteen bytes apart used to buy two tiers, and after
        // 5c it took three hundred, and after 6b it takes four hundred and
        // seventy.
        var resource = StandaloneNetStates.Of(typeof(PlayerResourceNetState));
        var occluder = StandaloneNetStates.Of(typeof(OccluderNetState));

        var described = NetSchema.Describe(SupplyTiers(),
            Array.Empty<EntityLayout>(), new[] { barrel, beam, surface, resource, occluder });

        foreach (var type in described)
        {
            // Rounded UP to a tier and never down: a tier is the point, so the
            // blob is at least the declaration and never less than it.
            Assert.True(type.BlobBytes >= type.LayoutBytes,
                $"'{type.ClassName}' is carried on a blob at least as long as it declared");

            Assert.True(type.PayloadWords == type.BlobBytes / 4,
                $"'{type.ClassName}' carries its own tier's payload word count");

            // The prop count is its TIER'S, and it does not include the class's
            // own fields at all — those are inside the payload now.
            Assert.True(type.PropCount == NetSchema.StandaloneSharedProps + type.BlobBytes / 4,
                $"'{type.ClassName}' prop count is its own tier's, not its own fields'");
        }

        // THE CLAIM OF PART 18, STATED AS ONE ASSERTION, and the exact
        // assertion that was inverted here before it. ss_occluder and
        // player_manager are either side of the 512 boundary once the block is
        // counted, so a build that had gone back to one blob for the family
        // would collapse these to one number and fail.
        Assert.True(described.Select(t => t.PayloadWords).Distinct().Count() > 1,
            "classes of different sizes carry DIFFERENT payload word counts");

        Assert.True(described.Select(t => t.PropCount).Distinct().Count() > 1,
            "and different prop counts, because the tier is the class's now");

        // A description with no tier table to read falls back to the class's
        // own declaration — a floor rather than a guess. The fallback has to
        // stay honest, because a guessed word count is exactly the failure the
        // generic payload replaced.
        var unaware = NetSchema.FromStandalone(NoTiers, beam, published: true);
        Assert.True(
            unaware.BlobBytes >= beam.Size
            && unaware.BlobBytes < StandaloneNetStates.HierarchyBlobBytes(
                   new[] { barrel, beam, surface, resource }),
            "a description with no tier table reports the class's own floor, not a family's");
    }

    /// <summary>A string field, for the width the buffer will and will not take.</summary>
    [StandaloneNetState("ss_test_string_width")]
    private partial class StringWidth
    {
        [NetString(16)] public partial string Path { get; set; }
        public partial int After { get; set; }
    }

    /// <summary>
    /// What the hierarchy's greediest declaration is, and which class sets it.
    ///
    /// <para>
    /// THIS TEST HAD NOT RUN SINCE THE xUNIT CONVERSION. It was
    /// <c>public static void RunTierPressure(Action&lt;string, bool&gt;)</c> —
    /// the old runner's shape, which still compiles, which xUnit does not
    /// discover, and which nothing referenced. It is the shape
    /// <c>docs/writing-tests.md</c> warns about, and it was carrying its own
    /// inline <c>{32, 256, 2048}</c> tier table: the same staleness that was
    /// just fixed in <c>SupplyTiers</c>, in a method that could not have
    /// reported it because it never ran. Split into facts and rewritten here.
    /// </para>
    ///
    /// <para>
    /// THE PRESSURE THIS WAS WRITTEN FOR IS GONE, and that is the point of the
    /// rewrite rather than a reason to delete it. Every entity on the hierarchy
    /// used to be allocated one blob whatever its class, so a single class
    /// widening a field moved every entity onto a bigger tier with nothing in
    /// that class's file saying so — <c>color_correction</c>'s
    /// <c>[NetString(260)]</c> did exactly that, and put the whole hierarchy on
    /// 2048 bytes and 518 props. Part 18 sizes each class to its own tier, so
    /// one class widening now costs that class.
    /// </para>
    ///
    /// <para>
    /// The number is still pinned, because it is still what
    /// <c>NetHierarchyDeclare</c> sends and still the ceiling check: a
    /// declaration past the largest tier is a class that cannot be networked at
    /// all. It is MANAGED arithmetic and not a second copy of the tier sizes,
    /// which are pinned once in the gtest against <c>generic_nettable.h</c> and
    /// read here through <c>SupplyTiers</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void TierPressureIsPerClassNow()
    {
        var all = StandaloneNetStates.Discover(typeof(ColorCorrectionNetState).Assembly, new RecordingLog());

        int Size(string className)
            => all.First(s => s.ClassName == className).Size;

        // What reaches native through NetHierarchyDeclare, and the only number
        // on this hierarchy that decides a tier.
        int blob = StandaloneNetStates.HierarchyBlobBytes(all);

        // 344 bytes past the engine block every blob opens with
        // (plan_entity.md §3.2). Written as the sum rather than as 848 so that
        // either half moving is a one-line edit that still says which half
        // moved -- and it was 352 until lane defect-teleport, which is worth a
        // sentence because THE BLOB DID NOT MOVE.
        //
        // The greediest declaration ends 340 bytes past the block and
        // HierarchyBlobBytes rounds the total up to a multiple of sixteen. The
        // block grew by eight bytes for the teleport pair, and the rounding
        // absorbed all eight: 496 + 340 -> 836 -> 848 became 504 + 340 -> 844
        // -> 848. So this number is the same 848 it was, every entity on the
        // hierarchy carries exactly the bytes it did before, and what changed
        // is only how much of the slack is named.
        //
        // 408 since the scoreboard's ping (plan_client_ui U3): player_manager's
        // seventh run, 34 shorts, took the greediest declaration from 340 to 408
        // past the block, 844 -> 912, and this number with it. It is the ceiling
        // check only (Part 18): every other class is sized to its own tier and
        // did not move, and player_manager stays on 1024 (below).
        //
        // 472 since the team totals (tf2 stage 0.7 part C): TeamScore is
        // EngineLimits.MaxTeams = 32 shorts, 64 bytes, the eighth run, and it
        // took the declaration 912 -> 976 — still under the 1024 tier, so
        // player_manager's wire block is unchanged and no other class moved.
        //
        // 960 since the client tail's four unwritten Studio*Count words went
        // (2026-10-18): the block shrank 504 -> 488, so 488 + 472 = 960.
        Assert.True(blob == EngineBlock.SizeBytes + 472, $"the hierarchy's declared blob is 472 bytes past the engine block ({blob})");
        Assert.Equal(960, blob);

        // The greediest classes, BY NAME. A max with no name would still pass
        // with color_correction back at 276 — it is WHICH class sets the number
        // that this step changed.
        //
        // player_manager took the top when the scoreboard was ported (lane U
        // cluster 14): six runs of 34 rows is 340 bytes against the 164 that two
        // classes used to tie on (color_correction_volume and func_smokevolume,
        // a 128-byte string plus two vectors and three scalars against a
        // 128-byte string plus nine scalars — both still there, both still 164).
        //
        // AND THAT IS THE POINT OF THIS TEST RATHER THAN A PROBLEM WITH IT.
        // Before Part 18 the greediest class set the blob for EVERY entity on
        // the hierarchy, so one class arriving at twice the previous top would
        // have doubled the payload props of all of them; the landings assertion
        // at the bottom is what says it no longer does. What the number above
        // still decides is the CEILING CHECK — a declaration past the largest
        // tier is a class that cannot be networked at all — and 472 is
        // comfortably inside 2,048.
        var greediest = all.OrderByDescending(s => s.Size).First();

        var atTheTop = all.Where(s => s.Size == greediest.Size)
                          .Select(s => s.ClassName)
                          .OrderBy(n => n)
                          .ToArray();

        Assert.True(greediest.Size == Block + 472
              && atTheTop.SequenceEqual(new[] { "player_manager" }),
            $"player_manager is the greediest declaration ({string.Join(", ", atTheTop)} at {greediest.Size})");

        // The pair it displaced, still where they were. Without this the
        // assertion above would pass just as well if the scoreboard had somehow
        // widened everything beneath it.
        Assert.True(Size("func_smokevolume") == Block + 164 && Size("color_correction_volume") == Block + 164,
            "func_smokevolume and color_correction_volume are untouched at 164 of their own");

        Assert.True(Size("color_correction") == Block + 144 && Size("color_correction") < greediest.Size, "color_correction no longer sets the hierarchy's size");

        // THE FLOOR, and the reason narrowing the filename further would buy
        // nothing. func_breakable_surf is eight pane bitmask words and two
        // vectors with no string in it, so no string change reaches a tier
        // below the one 68 bytes needs.
        Assert.True(Size("func_breakable_surf") == Block + 68, "a class with no string in it is already past the smallest tier's 32 bytes");

        // And the rule that turns a size into a tier, over THE FIXTURE rather
        // than an inline list. This test used to carry its own three rows —
        // {32, 256, 2048} — which stopped being the game's list when native
        // went to seven and could not have said so, because the method was
        // never discovered. SupplyTiers is the one fixture, and TierParityTests
        // is what holds it to generic_nettable.h.
        var tiers = SupplyTiers();

        // 1024 AND NOT 512, and the engine block is the whole of the difference:
        // 340 bytes of declaration used to land on 512 with room to spare, and
        // 340 plus a 352-byte block does not. Recorded as the number it is
        // rather than as an inequality, because a tier is a doubling and the
        // step that was taken here is worth being able to point at.
        Assert.True(tiers.Ceiling(blob) == 1024, $"the greediest declaration lands on the 1024 tier ({tiers.Ceiling(blob)})");

        Assert.True(tiers.Ceiling(288) == 512, "and 288 -- what color_correction alone once forced -- lands on 512, not 2048");

        // The boundary itself, because "the smallest that fits" is an inclusive
        // test and an off-by-one here is a tail simply absent from the wire.
        Assert.True(tiers.Ceiling(256) == 256 && tiers.Ceiling(257) == 512, "the ceiling is inclusive at the boundary and exclusive above it");

        //---------------------------------------------------------------------
        // AND THE CLASSES DO NOT SHARE A TIER (Part 18), which is the whole of
        // what the pressure this test was written for became.
        //
        // Over the real declarations rather than a fixture's, because what is
        // being checked is that the mod's own classes spread across the list —
        // a build that regressed to one blob for the family would put every one
        // of them on the same row and this is what would say so.
        //---------------------------------------------------------------------
        var landings = all.Select(state => tiers.Ceiling(state.Size)).ToList();

        Assert.True(landings.All(c => c > 0), "every declared class fits some tier");

        Assert.True(landings.Distinct().Count() > 1, "the declared classes do NOT all land on one tier");

        Assert.True(tiers.Ceiling(Size("func_lod")) < tiers.Ceiling(greediest.Size), "a four-byte class is carried on a smaller tier than the greediest one");

        // THE PING PAYS ONLY ITS OWN ENTITY (plan_client_ui U3), and so does
        // the team-total run (tf2 stage 0.7 part C): player_manager's eighth
        // run is 64 more bytes on player_manager alone — 844 -> 912 -> 976,
        // all three on the 1024 tier, so not even its own entity changes tier;
        // no other class's size depends on either (each is its own declaration
        // since Part 18).
        Assert.True(Size("player_manager") == Block + 472, $"player_manager is {Size("player_manager")}");
        Assert.True(tiers.Ceiling(Block + 340) == 1024 && tiers.Ceiling(Size("player_manager")) == 1024,
            "player_manager is on the 1024 tier before the ping, after it, and after the team totals");
    }

    /// <summary>
    /// A string that does not fit is REFUSED, and the refusal names itself.
    ///
    /// <para>
    /// Split out of the dead <c>RunTierPressure</c> above, where it had been
    /// bundled with the tier arithmetic and had not run since the xUnit
    /// conversion either. It is a different subject and it is its own fact now.
    /// </para>
    ///
    /// <para>
    /// It had to be checked rather than assumed: <c>WriteString</c> documented
    /// itself as truncating and never did — <c>Encoding.UTF8.GetBytes</c> throws
    /// on a short destination — so the behaviour was already right and only the
    /// message was useless.
    /// </para>
    /// </summary>
    [Fact]
    public void AStringPastItsDeclaredWidthIsRefused()
    {
        var state = new StringWidth();
        state.BindLocal();

        state.Path = "0123456789abcde";
        Assert.True(state.Path == "0123456789abcde", "a string exactly filling the buffer bar its terminator is written");

        state.After = 0x5A5A5A;

        string message = "";

        try   { state.Path = "0123456789abcdef"; }
        catch (FieldRangeException e) { message = e.Message; }

        Assert.True(message.Length > 0, "one byte more than the buffer holds is refused");

        Assert.True(message.Contains("StringWidth") && message.Contains("Path")
              && message.Contains("16"), "and the refusal names the class, the field and the declared width");

        // The refusal must leave the blob alone. A partial write would be worse
        // than either outcome: the field beside it shares the storage, and a
        // half-encoded path is a lookup nobody declared.
        Assert.True(state.Path == "0123456789abcde" && state.After == 0x5A5A5A, "a refused string changes neither its own field nor its neighbour");

        // Multi-byte, because the buffer is BYTES and the declaration reads like
        // characters. Five three-byte characters are fifteen bytes and fit; six
        // are eighteen and do not, though six characters plainly "fit" a 16.
        state.Path = "あいうえお";
        Assert.True(state.Path == "あいうえお", "a multi-byte string is measured in bytes, not characters");

        Assert.True(Throws(() => state.Path = "あいうえおか"), "and refused on its byte length rather than its character count");
    }

    //-------------------------------------------------------------------------
    // THE PACKER IS NOT THE BUG.
    //
    // A reading of the Net tables pane made spotlight_end look like it spends a
    // whole word on each of its four byte channels: four rows at offset 0, each
    // reporting BYTES 4, inside a class the same pane calls 8 bytes long. Both
    // of those cannot be true, and this is the test that says which is. It was
    // the view.
    //
    // ASKED OF beam NOW. spotlight_end's four channels have since become one
    // Color32, and this guard is about the PACKER rather than about that class —
    // a beam still declares four byte channels and a bool, so it is the same
    // question with a live answer. Pinned on the LAYOUT, so a packer that
    // regressed into a word per narrow field fails here rather than being
    // argued about through whatever the inspector is rendering that week.
    //-------------------------------------------------------------------------
    [Fact]
    public void FourChannelsShareOneWord()
    {
        var layout = BeamNetState.NetLayout;

        var placed = new[] { "Red", "Green", "Blue", "Brightness" }
                     .Select(name => layout.Fields.Single(f => f.Name == name)).ToArray();

        // ONE WORD BETWEEN THEM, wherever the buckets put it. The four are 8
        // bits each and 32 between them, so they are an EXACT FILL under rule 2
        // and lead the narrow half of the class — behind the eleven fields that
        // are naturally four bytes (Width, EndWidth, Noise, ScrollRate,
        // SpriteIndex, Flags, FadeLength, HaloIndex, HaloScale,
        // HdrColorScale, Frame), which is why the word is at 44 rather than at 0.
        // That number is derived and pinned; what the test is ABOUT is that
        // there is one word and not four.
        Assert.True(placed.Select(f => f.Offset).Distinct().Count() == 1, "all four of a beam's colour channels live in ONE word");

        Assert.True(placed[0].Offset == Block + 44, $"the word after the class's eleven whole-word fields, i.e. 44 and not {placed[0].Offset - Block}");

        Assert.True(placed.Select(f => f.BitShift).SequenceEqual(new[] { 0, 8, 16, 24 }), "each takes the next eight bits of it rather than a word of its own");

        Assert.True(placed.All(f => f.Bits == 8 && f.IsPacked), "eight bits wide apiece, and each one knows it is sharing");

        // Visible starts the word AFTER them — which is the proof that the four
        // channels consumed one word between them. Were the packer spending a
        // word each, it would be four words further on. It is a leftover rather
        // than part of their word because 32 + 1 does not fit in 32; the exact
        // fill is closed and the odd bool starts the next.
        Assert.True(layout.Fields.Single(f => f.Name == "Visible").Offset == placed[0].Offset + 4, "and the field after them starts one word later, so the four cost one word between them");

        // Counted rather than recalled: what the class would occupy if every
        // narrow field took a word of its own, against what it actually does.
        int unpacked = layout.Fields.Sum(f => f.IsPacked ? 4 : f.Bytes);

        // Against the class's OWN bytes. layout.Size counts the engine block in
        // front of it too (plan_entity.md §3.2), and a sum over the class's
        // fields never can, so comparing the two would compare a declaration
        // with a declaration plus 352 bytes and pass for the wrong reason.
        Assert.True(unpacked > layout.Size - Block,
            $"unpacked these fields would be {unpacked} bytes; packed the class declares {layout.Size - Block}");
    }

    //-------------------------------------------------------------------------
    // AND WHAT THE CONVERSION COST spotlight_end, which is the class the
    // reading was actually about.
    //
    // Four byte channels became one Color32. The claim is that this is free on
    // the wire — the packer was already spending exactly one word on them — so
    // the class must still be 8 bytes and its word must still be one word.
    //-------------------------------------------------------------------------
    [Fact]
    public void ASpotlightEndCarriesItsColourInOneWord()
    {
        var layout = SpotlightEndNetState.NetLayout;

        Assert.True(layout.Size == Block + 8, $"spotlight_end's whole layout is still 8 bytes, not {layout.Size}");

        Assert.True(layout.Fields.Count == 2, $"and it is two fields now rather than five ({layout.Fields.Count})");

        var tint = layout.Fields.Single(f => f.Name == "Tint");

        Assert.True(tint.Kind == NetFieldKind.Color32 && tint.Bytes == 4, "the colour is one Color32 field occupying one word");

        // THE FIELDS SWAPPED, which is the one thing the conversion did move.
        // Four narrow channels were hoisted into the packed pass ahead of the
        // float; a colour takes its word whole and stays where it was declared.
        Assert.True(layout.Fields.Single(f => f.Name == "LightScale").Offset == Block + 0
              && tint.Offset == Block + 4, "the float is first now and the colour second, which is the reordering a whole-storage field causes");
    }

    private static void Write(byte[] blob, NetFieldLayout field, byte[] bytes)
        => Array.Copy(bytes, 0, blob, field.Offset,
                      Math.Min(bytes.Length, blob.Length - field.Offset));
}
