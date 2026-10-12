using SourceSharp.Abi;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Enums in networked state (plan_nettable_generic.md Step 11).
///
/// <para>
/// THE CLAIM UNDER TEST is that an enum is nothing at all on the wire: it lays
/// out, packs and sends exactly as the integer it is backed by, so declaring one
/// costs nothing and native learns nothing. That is checked the only way it can
/// be — by declaring the same class twice, once with enums and once with the
/// integers behind them, and comparing every placement.
/// </para>
///
/// <para>
/// THE CLAIM THAT IS NOT FREE is the schema hash. Both sides compute from the
/// same assembly, so correctness never needed the member numbers in it; what
/// needs them is the two-build case, where a client that RENUMBERED a member —
/// not renamed it — has the same field names, offsets, widths and type codes and
/// would be handed the payload and read snow as rain. So a renumbering must move
/// the hash, and a field that is not an enum must not move at all.
/// </para>
/// </summary>
public partial class NetEnumTests
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
    // Declarations. Two of everything, so a placement can be compared against
    // the placement it claims to be identical to.
    //-------------------------------------------------------------------------

    /// <summary>An ordinary enum of alternatives, backed by int.</summary>
    private enum Weather
    {
        Rain     = 0,
        Snow     = 1,
        Ash      = 2,
        Snowfall = 3,
    }

    /// <summary>
    /// The same members with one of them RENUMBERED. Identical in every way the
    /// schema hash covered before Step 11 — same names, same count, same
    /// backing, same widths — and a different wire meaning.
    /// </summary>
    private enum WeatherRenumbered
    {
        Rain     = 0,
        Snow     = 1,
        Ash      = 2,
        Snowfall = 4,
    }

    /// <summary>
    /// The same numbers under different names. A rename moves not one bit on
    /// the wire, so this must hash IDENTICALLY — see the check that says so.
    /// </summary>
    private enum WeatherRenamed
    {
        Drizzle  = 0,
        Sleet    = 1,
        Cinder   = 2,
        Blizzard = 3,
    }

    /// <summary>
    /// Two members with their values SWAPPED, which the sorted-set rule does
    /// not catch. Declared so the gap is a test that states it rather than
    /// something a reader has to work out from the hash function.
    /// </summary>
    private enum WeatherSwapped
    {
        Rain     = 1,
        Snow     = 0,
        Ash      = 2,
        Snowfall = 3,
    }

    /// <summary>An alias, which names a value already covered and must change nothing.</summary>
    private enum WeatherAliased
    {
        Rain     = 0,
        None     = Rain,
        Snow     = 1,
        Ash      = 2,
        Snowfall = 3,
    }

    /// <summary>A flags enum backed by a byte, which is how a narrow one is asked for.</summary>
    [Flags]
    private enum Lit : byte
    {
        None  = 0b0000_0000,
        World = 0b0000_0001,
        Model = 0b0000_0010,
        Both  = 0b0000_0011,
    }

    [StandaloneNetState("ss_enum_declared")]
    private partial class WithEnums
    {
        public partial Lit     Lighting { get; set; }
        public partial Weather Kind { get; set; }
        public partial int     After { get; set; }
    }

    /// <summary>The same class with the integers the enums are backed by.</summary>
    [StandaloneNetState("ss_enum_plain")]
    private partial class WithoutEnums
    {
        public partial byte Lighting { get; set; }
        public partial int  Kind { get; set; }
        public partial int  After { get; set; }
    }

    /// <summary>An enum narrowed further than its backing type, which is the packed case.</summary>
    [StandaloneNetState("ss_enum_narrow")]
    private partial class NarrowEnum
    {
        [NetField(Bits = 2)] public partial Lit Lighting { get; set; }
        public partial bool Beside { get; set; }
    }

    /// <summary>The <c>[EntityField]</c> shell's version of the same question.</summary>
    private struct ShellWithEnum
    {
        [EntityField] public Weather Kind { get; set; }
        [EntityField] public Lit Lighting { get; set; }
        [EntityField] public int After { get; set; }
    }

    private struct ShellRenumbered
    {
        [EntityField] public WeatherRenumbered Kind { get; set; }
        [EntityField] public Lit Lighting { get; set; }
        [EntityField] public int After { get; set; }
    }

    private struct ShellPlain
    {
        [EntityField] public int Kind { get; set; }
        [EntityField] public byte Lighting { get; set; }
        [EntityField] public int After { get; set; }
    }


    //-------------------------------------------------------------------------

    /// <summary>
    /// The whole of the layout claim: enum in, integer out, at every placement.
    /// </summary>
    [Fact]
    public void TheUnderlyingIntegerIsTheLayout()
    {
        Assert.True(NetEnums.Underlying(typeof(Weather)) == typeof(int)
              && NetEnums.Underlying(typeof(Lit)) == typeof(byte)
              // And a type that is not an enum is itself, so every caller can
              // go through this unconditionally.
              && NetEnums.Underlying(typeof(float)) == typeof(float), "an enum resolves to the integer it is backed by");

        var declared = WithEnums.NetLayout;
        var plain    = WithoutEnums.NetLayout;

        Assert.True(declared.Fields.Count == plain.Fields.Count, "the two declarations have the same field count");

        Assert.True(declared.Size == plain.Size, "and the same total size");

        bool same = declared.Fields.Count == plain.Fields.Count;

        for (int i = 0; same && i < declared.Fields.Count; i++)
        {
            var a = declared.Fields[i];
            var b = plain.Fields[i];

            same = a.Name == b.Name && a.Kind == b.Kind && a.Offset == b.Offset
                   && a.Bytes == b.Bytes && a.Bits == b.Bits && a.BitShift == b.BitShift;
        }

        // The claim in one line: a class declaring enums is byte-for-byte the
        // class declaring their integers, so nothing on the wire moved and
        // native was told exactly what it was told before.
        Assert.True(same, "every enum field is placed exactly where its integer would be");

        // The byte-backed one is packed by the ordinary rule, which is the
        // answer to how this composes with Step 8: the packer reads the storage
        // width, never the member list.
        //
        // BY NAME AND NOT BY INDEX. It used to be Fields[0], which was true only
        // because the old packer hoisted every narrow field to the front. Eight
        // bits is not a word, so a byte-backed enum is a rule-3 leftover now and
        // sits behind Kind and After, which are naturally four bytes. The claim
        // being made here is about its WIDTH, not about its position, so the
        // lookup should not have been positional in the first place.
        var lighting = declared.Fields.Single(f => f.Name == "Lighting");

        Assert.True(lighting.Bits == 8 && lighting.Bytes == 4 && lighting.BitShift == 0 && lighting.IsPacked, "a byte-backed enum packs as eight bits of a shared word");

        Assert.True(lighting.Offset == Block + 8, $"placed after the two full-width fields, at 8 rather than {lighting.Offset}");

        // And an author who wants narrower says so, as they would for an int.
        var narrow = NarrowEnum.NetLayout.Fields.Single(f => f.Name == "Lighting");

        Assert.True(narrow.Bits == 2 && narrow.IsPacked, "and Bits narrows it further where the author asked");

        // The [EntityField] shell answers the same way, since SizeOf and
        // AlignmentOf go through the same resolution.
        var shell = EntityLayout.Build(typeof(ShellWithEnum));
        var flat  = EntityLayout.Build(typeof(ShellPlain));

        Assert.True(shell.Size == flat.Size && shell["Kind"].Offset == flat["Kind"].Offset && shell["Kind"].Size == flat["Kind"].Size && shell["Lighting"].Offset == flat["Lighting"].Offset && shell["Lighting"].Size == flat["Lighting"].Size && shell["After"].Offset == flat["After"].Offset, "the entity-field shell lays an enum out as its integer too");

        // An enum used to be a build failure here, deliberately -- EntityLayout
        // throws on a type it cannot lay out rather than skipping the field,
        // because a missing field desyncs both sides untraceably. That it no
        // longer throws for an enum is this step; that it still throws for
        // everything else is EntityLayoutRejectsUnsupportedTypes in Program.cs,
        // which is where the unsupported declaration lives -- it has to be
        // ABSTRACT to keep EntityLayoutRegistry.DiscoverAll, which walks every
        // loaded assembly, from building it and taking the suite down.
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// The member order, which is neither reflection's nor the source's and has
    /// to be the same on both routes into the hash.
    /// </summary>
    [Fact]
    public void TheMembersAreOneOrderForBothShells()
    {
        var members = NetEnums.MembersOf(typeof(WeatherSwapped));

        // By VALUE, not by declaration: Snow is 0 and comes first, though it is
        // declared second. Reflection's own order is by value too, but nothing
        // documents that it breaks ties the same way a generated list would --
        // so the order is imposed here rather than inherited.
        Assert.True(members.Count == 4 && members[0].Name == "Snow" && members[0].Value == 0 && members[1].Name == "Rain" && members[1].Value == 1 && members[2].Name == "Ash" && members[2].Value == 2 && members[3].Name == "Snowfall" && members[3].Value == 3, "members come back sorted by value");

        // An ALIAS is a second name for a value already there, so it is one
        // member in the list and one value in the hash.
        Assert.True(NetEnums.MembersOf(typeof(WeatherAliased)).Count == 5 && NetEnums.HashValuesOf(typeof(WeatherAliased)).Count == 4, "an alias is its own member but not its own hashed value");

        Assert.True(NetEnums.MembersOf(typeof(int)).Count == 0, "a type that is not an enum has no members, rather than throwing");

        Assert.True(NetEnums.IsFlags(typeof(Lit)) && !NetEnums.IsFlags(typeof(Weather)), "a flags enum says it is one, and an ordinary one says it is not");
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// What a value nothing names decodes to. An int on the wire can hold
    /// anything, so this is the ordinary case and not the exotic one — and a
    /// throw here would turn a cosmetic desync into a dropped client.
    /// </summary>
    [Fact]
    public void AnUnknownValueIsVisibleAndNeverFatal()
    {
        Assert.True(NetEnums.Format(typeof(Weather), 3) == "Snowfall", "a value a member names prints as the member");

        Assert.True(NetEnums.Format(typeof(Lit), 0b0000_0011) == "Both", "a flags value composes from the members that cover it");

        // Widest first, so a value covered exactly by a compound member reads as
        // that member. A value covered only by the parts still reads as parts.
        Assert.True(NetEnums.Format(typeof(Lit), 0) == "None", "and zero is the member declared for it rather than an empty string");

        // THE DECISION Step 11 leaves open. Not a throw, and not a bare number
        // that reads like a member nobody had heard of.
        Assert.True(NetEnums.Format(typeof(Weather), 9) == "9 (unnamed)", "a value nothing names prints as a number and says so");

        Assert.True(NetEnums.Format(typeof(Lit), 0b0100_0001) == "World|0x40 (unnamed)", "a flags value that is partly named keeps the part that is");

        // Every value of the backing type, because "an int on the wire can hold
        // anything" is the premise and a formatter that threw on one of them
        // would be found by a player rather than by this.
        bool threw = false;

        for (long value = -300; value <= 300; value++)
        {
            try
            {
                NetEnums.Format(typeof(Weather), value);
                NetEnums.Format(typeof(Lit), value);
            }
            catch
            {
                threw = true;
                break;
            }
        }

        Assert.True(!threw, "no value of the backing type makes the decode throw");

        Assert.True(!Throws(() => NetEnums.Format(typeof(Weather), long.MinValue)) && !Throws(() => NetEnums.Format(typeof(Weather), long.MaxValue)), "not even the extremes");
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// The hash decision, taken on purpose: the member NUMBERS are covered, so a
    /// build that renumbers one is refused rather than believed.
    /// </summary>
    [Fact]
    public void RenumberingMovesTheSchemaHash()
    {
        var original   = EntityLayout.Build(typeof(ShellWithEnum), "ss_enum_shell");
        var renumbered = EntityLayout.Build(typeof(ShellRenumbered), "ss_enum_shell");

        // Everything the hash covered BEFORE Step 11 is identical between these
        // two: same names, same order, same type codes, same offsets, same
        // widths. That is what makes the renumbering the only difference and
        // this check the only thing that can catch it.
        bool identicalOtherwise = original.Size == renumbered.Size
                                  && original.Fields.Count == renumbered.Fields.Count;

        for (int i = 0; identicalOtherwise && i < original.Fields.Count; i++)
        {
            identicalOtherwise =
                original.Fields[i].Name == renumbered.Fields[i].Name
                && original.Fields[i].Offset == renumbered.Fields[i].Offset
                && original.Fields[i].Size == renumbered.Fields[i].Size
                && original.Fields[i].Bits == renumbered.Fields[i].Bits;
        }

        Assert.True(identicalOtherwise, "the renumbered layout differs in nothing but the member numbers");

        Assert.True(original.Fingerprint != renumbered.Fingerprint, "and renumbering a member moves the layout fingerprint");

        // The two hash functions have to stay ONE function over the same values,
        // because the fingerprint already travels at join and a schema hash that
        // disagreed with it would be a second opinion about whether two builds
        // match. Checked over a layout with enums in it, which is the case that
        // could have made them diverge.
        //
        // THIS IS THE CHECK THAT FOUND A REAL DIVERGENCE, and not the one it was
        // written for. ShellWithEnum has a byte-backed enum in it, so it is the
        // first layout in the tree whose field is UNSIGNED -- and the two
        // functions disagreed on unsigned before Step 11, because
        // NetworkedLayout.TypeCodeOf folded every integral type to 0 while
        // NetFieldKind.UInt is 5. Each side carried a comment saying they
        // agreed. See NetSchema.KindOf.
        Assert.True(NetSchema.FromLayout(NoTiers, original, published: true).SchemaHash == original.Fingerprint && NetSchema.FromLayout(NoTiers, renumbered, published: true).SchemaHash == renumbered.Fingerprint, "the schema hash is still the layout fingerprint, enums and all");

        // And the agreement is now structural rather than claimed, over every
        // type either side can express.
        Type[] every =
        {
            typeof(int), typeof(sbyte), typeof(short), typeof(long),
            typeof(byte), typeof(ushort), typeof(uint), typeof(ulong),
            typeof(float), typeof(double), typeof(bool), typeof(Vector),
            typeof(Weather), typeof(Lit),
        };

        bool agree = true;

        foreach (var type in every)
        {
            agree &= NetworkedLayout.TypeCodeOf(type)
                     == NetSchema.WireTypeCodeOf(NetSchema.KindOf(type));
        }

        Assert.True(agree, "the two type-code folds are one fold, for every type either expresses");

        Assert.True(NetworkedLayout.TypeCodeOf(typeof(byte)) != NetworkedLayout.TypeCodeOf(typeof(sbyte)), "and an unsigned field really does code differently from a signed one");

        // AND NOTHING WITHOUT AN ENUM MOVED. The mix is skipped entirely for a
        // field that is not one, so every hash this tree already publishes is
        // exactly what it was -- pinned to a literal so that stops being a
        // claim and becomes a test.
        var plain = new[]
        {
            new NetSchemaField("A", "int", NetFieldKind.Int, 0, 4, 32),
            new NetSchemaField("B", "float", NetFieldKind.Float, 4, 4, 32),
        };

        Assert.True(NetSchema.SchemaHashOf(plain, 8) == PlainSchemaHash, "a layout with no enum in it hashes to what it always did");

        // WHAT IS DELIBERATELY NOT COVERED, which is the half of this decision
        // that is easy to get backwards. A rename moves not one bit on the wire,
        // so a hash that refused every client over a cosmetic refactor would be
        // a guard people learn to route around -- and a routed-around guard is
        // worse than none. The inspector still decodes by NAME; that is why the
        // hash list and the decode list are different lists.
        Assert.True(HashOfOne(typeof(Weather)) == HashOfOne(typeof(WeatherRenamed)), "renaming every member does not move the hash, because nothing moved");

        Assert.True(HashOfOne(typeof(Weather), "Weather") == HashOfOne(typeof(WeatherRenamed), "Weather"), "nor does renaming the enum type itself");

        Assert.True(HashOfOne(typeof(Weather)) == HashOfOne(typeof(WeatherAliased)), "nor does adding an alias for a value already there");

        Assert.True(HashOfOne(typeof(Weather)) != HashOfOne(typeof(WeatherRenumbered)), "but renumbering one member does");

        Assert.True(HashOfOne(typeof(Weather)) != HashOfOne(typeof(WeatherExtended)), "and so does adding a member");

        // THE GAP, ASSERTED RATHER THAN LEFT TO BE DISCOVERED. Hashing the
        // values as a sorted SET means two members that swap values leave the
        // set identical and slip through, though every decoded value now means
        // the opposite. The alternative -- declaration order -- catches the swap
        // and then refuses a build that merely reordered two declarations whose
        // values never moved, which is the same disease as names. A swap is
        // pathological; a reorder is routine. This check exists so the coverage
        // is stated in a place that fails if somebody changes the rule without
        // meaning to.
        Assert.True(HashOfOne(typeof(Weather)) == HashOfOne(typeof(WeatherSwapped)), "a swap of two members' values is NOT caught, and that is the known gap");
    }

    /// <summary>One enum field's schema hash, for comparing two enums.</summary>
    private static uint HashOfOne(Type enumType, string? csType = null)
        => NetSchema.SchemaHashOf(
            new[]
            {
                new NetSchemaField("A", csType ?? enumType.Name, NetFieldKind.Int,
                                   0, 4, 32, 0, enumType),
            },
            4);

    /// <summary>The same members with one more. A new value is a new decode.</summary>
    private enum WeatherExtended
    {
        Rain     = 0,
        Snow     = 1,
        Ash      = 2,
        Snowfall = 3,
        Hail     = 4,
    }

    /// <summary>
    /// FNV-1a over two plain fields — an int at 0 and a float at 4, in a layout
    /// of eight bytes — computed independently of this implementation and pinned
    /// so it cannot drift. It is the number that says Step 11 cost the wire
    /// nothing: an enum mixes extra values, and a field that is not one mixes
    /// exactly what it always did.
    /// </summary>
    private const uint PlainSchemaHash = 0xCA5438FFu;

    //-------------------------------------------------------------------------

    /// <summary>
    /// The part with the real value in it (Step 11's own words): the inspector
    /// decodes through the member names, because the generic payload took away
    /// every other place they could be read.
    /// </summary>
    [Fact]
    public void TheInspectorDecodesByName()
    {
        var state = new WithEnums();
        state.BindLocal();
        state.Kind     = Weather.Snowfall;
        state.Lighting = Lit.World;
        state.After    = -12;

        var described = NetSchema.FromStandalone(NoTiers, WithEnums.NetLayout, published: true);
        var decoded   = NetSchema.Decode(described, state.Snapshot())
                                 .ToDictionary(d => d.Field.Name);

        Assert.True(decoded["Kind"].Value == "Snowfall", "an enum field decodes to its member name, not its number");

        Assert.True(decoded["Lighting"].Value == "World", "and a flags one to the members it is made of");

        // The NUMBER is still reported beside the name, because the two answer
        // different readers: a name is what a person checks and a number is
        // what a diff can subtract.
        Assert.True(decoded["Kind"].Number == 3.0 && decoded["Lighting"].Number == 1.0, "the number is still there for anything that plots or diffs");

        // The C# type is the ENUM's, which is the thing CsNameOf could never
        // guess back from a kind and a width.
        Assert.True(decoded["Kind"].Field.CsType == "Weather" && decoded["Lighting"].Field.CsType == "Lit" && decoded["Kind"].Field.EnumType == typeof(Weather), "the field reports the type the author declared");

        // And a field that is not an enum is untouched by any of it.
        Assert.True(decoded["After"].Value == "-12" && decoded["After"].Field.EnumType is null, "a plain field decodes exactly as it did");

        // A value nothing names, arriving the only way it can: through the
        // bytes. Written past the accessor deliberately -- the setter would
        // refuse an out-of-range value on a narrowed field, and the wire has no
        // setter.
        //
        // AT THE FIELD'S OWN OFFSET, ASKED FOR RATHER THAN COUNTED. This wrote
        // blob[4..8] while Kind happened to be the second word; under the four
        // buckets Kind is a full-width field and leads the class, so the literal
        // 4 would now be overwriting After and asserting about a Kind nobody
        // touched. The test is about DECODING an unnamed value and has no
        // opinion about placement, so it asks the layout where the field is.
        int kindAt = WithEnums.NetLayout["Kind"].Offset;

        byte[] blob = state.Snapshot().ToArray();
        blob[kindAt] = 9; blob[kindAt + 1] = 0; blob[kindAt + 2] = 0; blob[kindAt + 3] = 0;

        var strange = NetSchema.Decode(described, blob).ToDictionary(d => d.Field.Name);

        Assert.True(strange["Kind"].Value == "9 (unnamed)" && strange["Kind"].Number == 9.0, "a value the wire invented decodes visibly rather than fatally");
    }

    //-------------------------------------------------------------------------

    [Fact]
    public void TheAccessorsRoundTrip()
    {
        var sender = new WithEnums();
        sender.BindLocal();
        sender.Kind     = Weather.Ash;
        sender.Lighting = Lit.Both;
        sender.After    = 77;

        Assert.True(sender.Kind == Weather.Ash && sender.Lighting == Lit.Both, "an enum written through the accessor reads back");

        var received = new WithEnums();
        received.BindLocal();
        received.Decode(sender.Snapshot());

        Assert.True(received.Kind == Weather.Ash && received.Lighting == Lit.Both && received.After == 77, "and survives the trip through a blob");

        // A neighbour test, because a packed enum shares its word: the bool
        // beside the narrowed one must not move when it is written.
        var narrow = new NarrowEnum();
        narrow.BindLocal();
        narrow.Beside   = true;
        narrow.Lighting = Lit.Model;

        Assert.True(narrow.Beside && narrow.Lighting == Lit.Model, "a narrowed enum does not disturb the field sharing its word");

        // THE RANGE CHECK STILL FIRES, and on the enum's integer rather than on
        // the enum -- which is the one thing the generator had to be told, since
        // C# will not compare an enum with a literal.
        Assert.True(Throws(() => { var s = new NarrowEnum(); s.BindLocal(); s.Lighting = (Lit)9; }), "a value too wide for the declared bits is refused, as an int's would be");

        Assert.True(!Throws(() => { var s = new NarrowEnum(); s.BindLocal(); s.Lighting = (Lit)3; }), "and a value that fits is not");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }
}
