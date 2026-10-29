using System.Text;
using System.Text.Json;
using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The schema view and the payload decoder — the oracle the generic-payload plan
/// leans on from Step 3 onward.
///
/// <para>
/// WHY THIS NEEDS ITS OWN COVERAGE, and more of it than a debugging tool usually
/// earns. Everything else in the dev API answers a question somebody can check
/// by looking: an entity list is right or it is obviously not. A decoder is
/// different — a field read from the wrong offset produces a plausible number,
/// and once the payload plan lands there is nothing else in the tree that can
/// say it is the wrong one. The whole reason these routes exist is to catch a
/// wrong decode, so a wrong decode HERE would be worse than not having them.
/// </para>
///
/// <para>
/// So the decode is checked against bytes this file laid out itself, field by
/// field, for every kind native can carry — and the neighbour tests are the
/// point of the exercise: an off-by-one offset shows up as a field that moves
/// when the field beside it is written, and that is the only symptom it has.
/// </para>
/// </summary>
public partial class NetSchemaTests
{
    /// <summary>
    /// Every field kind, laid out with the alignment case that matters: a float
    /// after a single byte must not land on an odd address.
    ///
    /// Its own declaration rather than a borrowed one, because these tests are
    /// about reading bytes back and that needs a shape whose every offset is
    /// stated here where the expectations are.
    /// </summary>
    [StandaloneNetState("ss_schema_every_kind")]
    private partial class EveryKind
    {
        public partial Vector Where { get; set; }
        public partial int    Count { get; set; }
        public partial uint   Bits { get; set; }
        public partial bool   Broken { get; set; }
        public partial float  Ratio { get; set; }
        public partial short  Small { get; set; }
        public partial byte   Tiny { get; set; }
        [NetString(16)] public partial string Label { get; set; }
    }

    /// <summary>
    /// A generated-shell layout, for the <see cref="NetSchema.FromLayout"/>
    /// path. These checks used to run against <c>NetEntityLayout.Layout</c>,
    /// which went with the deleted shell; what they are about is the
    /// DESCRIPTION rule — a vector is one prop, the schema hash is the layout
    /// fingerprint — not the type that was its only user. Four field kinds, so
    /// the vector-is-one-prop check has a vector to find.
    /// </summary>
    // CS0649 is a false positive here and suppressing it is the honest answer:
    // nothing assigns these because nothing reads them as VALUES. EntityLayout
    // reflects over the declarations, which is the whole subject of the test.
#pragma warning disable CS0649
    private struct GeneratedShellState
    {
        [EntityField] public int    SpawnCount;
        [EntityField] public float  LastDamage;
        [EntityField] public Vector SpawnOrigin;
        [EntityField] public bool   IsBroken;
    }
#pragma warning restore CS0649

    private static EntityLayout GeneratedShell
        => EntityLayout.Build(typeof(GeneratedShellState), "ss_schema_generated");

    /// <summary>Two ints side by side, which is all a neighbour test needs.</summary>
    [StandaloneNetState("ss_schema_neighbours")]
    private partial class Neighbours
    {
        public partial int First { get; set; }
        public partial int Second { get; set; }
        public partial int Third { get; set; }
    }

    /// <summary>
    /// Narrow fields of four different declared types sharing one word, which is
    /// the case that broke the C# type column.
    ///
    /// Four <c>byte</c>s and a <c>bool</c> and a genuinely-narrowed <c>uint</c>,
    /// because the point is that they are DIFFERENT declarations that all land
    /// in a dword — a column that reports the storage cannot tell them apart and
    /// a column that reports the declaration must.
    /// </summary>
    [StandaloneNetState("ss_schema_declared_types")]
    private partial class DeclaredTypes
    {
        [NetField(Bits = 8)] public partial byte   Red    { get; set; }
        [NetField(Bits = 8)] public partial byte   Green  { get; set; }
        public partial bool   Lit    { get; set; }
        [NetField(Bits = 6)] public partial uint   Style  { get; set; }
        [NetField(Bits = 5)] public partial short  Nudge  { get; set; }
        public partial float  Scale  { get; set; }
    }

    //-------------------------------------------------------------------------
    // THE C# TYPE COLUMN SAYS WHAT WAS DECLARED.
    //
    // It used to say what the STORAGE was, because it was guessed back from the
    // kind and the byte count, and a packed field's byte count is its containing
    // word's. Every narrow field therefore reported the word's type: four `byte`
    // channels sharing a dword all came out `uint`, and so did a `bool`. The one
    // view whose job is to report a declaration was reporting the packer.
    //
    // Not cosmetic. A reader checking a suspect field against its declaration
    // compares this column to the source; a column that answers `uint` for every
    // narrow field agrees with any declaration at all and so can never disagree
    // with a wrong one.
    //-------------------------------------------------------------------------
    [Fact]
    public void DeclaredTypesSurviveThePacker()
    {
        var type = NetSchema.FromStandalone(
            NoTiers, StandaloneNetStates.Of(typeof(DeclaredTypes)), published: true);

        var byName = type.Fields.ToDictionary(f => f.Name);

        // The setup, asserted rather than assumed: these really are packed, so
        // this really is the case that used to be wrong.
        Assert.True(byName["Red"].IsPacked && byName["Green"].IsPacked
              && byName["Lit"].IsPacked && byName["Style"].IsPacked
              && byName["Nudge"].IsPacked, "five narrow fields of four declared types are packed");

        Assert.True(byName.Values.Where(f => f.IsPacked).All(f => f.Bytes == 4), "and every one of them reports its containing WORD as its storage");

        // Which is exactly why the storage cannot be the type. Four distinct
        // declarations, four distinct answers.
        Assert.True(byName["Red"].CsType == "byte" && byName["Green"].CsType == "byte", "a packed byte reports byte, not the uint word it lives in");

        Assert.True(byName["Lit"].CsType == "bool", "a packed bool reports bool");

        Assert.True(byName["Style"].CsType == "uint" && byName["Nudge"].CsType == "short", "and a genuinely narrowed uint and short report themselves");

        Assert.True(byName["Scale"].CsType == "float", "an unpacked field is unaffected");

        // THE KIND IS STILL THE WIRE'S. These two columns say different things
        // on purpose — `byte` is what was written, `uint` is what goes out — and
        // the defect was the first column having quietly become a copy of the
        // second.
        Assert.True(byName["Red"].Kind == NetFieldKind.UInt
              && byName["Nudge"].Kind == NetFieldKind.Int
              && byName["Lit"].Kind == NetFieldKind.Bool, "the kind column still reports the wire encoding, which is a different fact");

        Assert.True(type.Fields.Count(f => f.CsType != f.Kind.ToString().ToLowerInvariant()) >= 3, "and the two columns are not the same string read twice");

        // The real call site, which is what the reading that started this was
        // looking at.
        var spotlight = NetSchema.FromStandalone(
            NoTiers, SpotlightEndNetState.NetLayout, published: true);

        Assert.True(spotlight.Fields.Where(f => f.IsPacked).All(f => f.CsType == "byte"), "and spotlight_end's channels report byte apiece");
    }

    //-------------------------------------------------------------------------
    // A DECLARED NAME CHANGES NO DECODING, SO IT MUST NOT MOVE THE HASH.
    //
    // Same rule the enum step established: member VALUES are hashed because they
    // change what a number means, member NAMES are not. `byte` and `uint`
    // narrowed to eight bits are one encoding under two names, so a build that
    // spells it one way decodes a build that spells it the other exactly right —
    // and refusing that connection would be a refusal with nothing behind it.
    //-------------------------------------------------------------------------
    [Fact]
    public void TheDeclaredNameIsNotHashed()
    {
        var type = NetSchema.FromStandalone(
            NoTiers, StandaloneNetStates.Of(typeof(DeclaredTypes)), published: true);

        var renamed = NetSchemaType.Create(
            type.ClassName, type.TypeName, type.Shell,
            type.Fields.Select(f => f with { CsType = f.CsType + "_other" }).ToArray(),
            type.LayoutBytes, type.PropCount, published: true);

        Assert.True(renamed.SchemaHash == type.SchemaHash, "renaming every declared type leaves the schema hash alone");

        // And the guard that says the hash is not simply ignoring this struct:
        // move something that DOES change decoding and it must move.
        var widened = NetSchemaType.Create(
            type.ClassName, type.TypeName, type.Shell,
            type.Fields.Select(f => f.Name == "Style" ? f with { Bits = f.Bits + 1 } : f).ToArray(),
            type.LayoutBytes, type.PropCount, published: true);

        Assert.True(widened.SchemaHash != type.SchemaHash, "while widening one field by a single bit does move it");
    }

    //-------------------------------------------------------------------------
    // Identity: the type id, the schema hash and the table name.
    //-------------------------------------------------------------------------
    [Fact]
    public void Identity()
    {
        var tiers = NoTiers;

        // A FIXED CORPUS, pinned by hand, and MIRRORED IN THE GTEST since Step
        // 6: SourceSharp_HashClassName in generic_nettable.h is the same
        // function in the other language, and ClassNameHash.MatchesTheManagedCorpus
        // asserts the same literals over the same strings. Two implementations
        // of one function is the shape this tree keeps paying for; a shared
        // corpus is what turns a drift into two failing suites rather than into
        // entities pairing with the wrong managed class.
        //
        // There is a live check as well, and it is the stronger of the two:
        // NetPublishType hands back the id NATIVE hashed for every name the
        // build actually publishes, and PublishOne compares it against this
        // function before anything is on the wire. The corpus covers the names
        // somebody thought of; that covers all of them.
        Assert.True(NetSchema.TypeIdOf("sourcesharp_netentity") == 0x362417E7u, "the type id is FNV-1a of the classname");

        // The names the gtest pins, so a change to either implementation fails
        // in both suites rather than in one.
        Assert.True(NetSchema.TypeIdOf("ss_barrel") == 0xE456BA08u
            && NetSchema.TypeIdOf("beam") == 0xA75279FAu
            && NetSchema.TypeIdOf("func_breakable_surf") == 0x043470C0u, "and the hierarchy classes the gtest pins too");

        // A COLLISION is a build-time fact about a fixed set of strings, and
        // native refuses the second name rather than recording it — the row
        // would be reachable by no lookup and the first would answer for both,
        // which is one entity decoding as another. Asserted here as the fact the
        // refusal rests on: FNV-1a over 32 bits really can collide, and this
        // pair is what the gtest forces the branch with.
        Assert.True(NetSchema.TypeIdOf("costarring") == NetSchema.TypeIdOf("liquid")
            && "costarring" != "liquid", "two different classnames really can hash alike");

        Assert.True(NetSchema.TypeIdOf("prop_physics") == 0xA1C3A842u, "and of another, so the corpus is not one lucky string");

        // The empty string is the offset basis and nothing else, which is the
        // case a hand-written implementation gets wrong by starting at zero.
        Assert.True(NetSchema.TypeIdOf("") == 0x811C9DC5u, "the empty classname hashes to the offset basis");

        Assert.True(NetSchema.TypeIdOf("a") == 0xE40C292Cu, "a one-character classname is one round");

        Assert.True(NetSchema.TypeIdOf("prop_physics") != NetSchema.TypeIdOf("prop_dynamic"), "different classnames get different ids");

        // The name the connect handshake pairs on for anything still built from
        // a classname. Nothing on either shell is, since Step 6 — every managed
        // entity arrives through a tier whose table name is a literal — so this
        // pins the rule the inspector reports rather than one the wire uses.
        Assert.True(NetSchema.TableNameOf("prop_physics") == "DT_prop_physics", "a table is DT_ and the classname");

        // THE ONE THAT MATTERS MOST HERE. The layout fingerprint already goes
        // over the wire at every join, so a schema hash that disagreed with it
        // would be a second opinion about whether two builds match. Checked
        // against the real layout rather than a contrived one.
        var netEntity = NetSchema.FromLayout(tiers, GeneratedShell, published: true);

        Assert.True(netEntity.SchemaHash == GeneratedShell.Fingerprint, "the schema hash IS the layout fingerprint, not a second opinion");

        // Both halves of the hash have to matter, or it is not a fingerprint.
        var moved = new[]
        {
            new NetSchemaField("A", "int", NetFieldKind.Int, 0, 4, 32),
            new NetSchemaField("B", "int", NetFieldKind.Int, 4, 4, 32),
        };

        var renamed = new[]
        {
            new NetSchemaField("A", "int", NetFieldKind.Int, 0, 4, 32),
            new NetSchemaField("C", "int", NetFieldKind.Int, 4, 4, 32),
        };

        var reordered = new[]
        {
            new NetSchemaField("B", "int", NetFieldKind.Int, 0, 4, 32),
            new NetSchemaField("A", "int", NetFieldKind.Int, 4, 4, 32),
        };

        Assert.True(NetSchema.SchemaHashOf(moved, 8) != NetSchema.SchemaHashOf(renamed, 8), "renaming a field changes the schema hash");

        Assert.True(NetSchema.SchemaHashOf(moved, 8) != NetSchema.SchemaHashOf(reordered, 8), "reordering two fields changes the schema hash");

        Assert.True(NetSchema.SchemaHashOf(moved, 8) != NetSchema.SchemaHashOf(moved, 12), "growing the layout changes the schema hash");

        // A retype that keeps every width is the change that would otherwise
        // slip through: same names, same offsets, same sizes, different meaning.
        var retyped = new[]
        {
            new NetSchemaField("A", "int", NetFieldKind.Int, 0, 4, 32),
            new NetSchemaField("B", "float", NetFieldKind.Float, 4, 4, 32),
        };

        Assert.True(NetSchema.SchemaHashOf(moved, 8) != NetSchema.SchemaHashOf(retyped, 8), "changing a field's kind changes the schema hash");
    }

    //-------------------------------------------------------------------------
    // Describing what each shell was actually told.
    //-------------------------------------------------------------------------
    [Fact]
    public void Description()
    {
        // NO TIERS, deliberately: the assertions below are the floor a build
        // with nothing registered describes — six props and no payload — which
        // the comment beside them already said and the test never established.
        // It inherited whichever state ran before it, and passed because that
        // state happened to be this one. An empty table states it instead.
        var tiers = NoTiers;

        var state = StandaloneNetStates.Of(typeof(EveryKind));
        var described = NetSchema.FromStandalone(tiers, state, published: true);

        Assert.True(described.Fields.Count == state.Fields.Count
            && described.Fields[0].Name == state.Fields[0].Name
            && described.Fields[^1].Name == state.Fields[^1].Name, "a described type keeps the generated field order");

        Assert.True(described.Fields.Select(f => f.Offset).SequenceEqual(state.Fields.Select(f => f.Offset)), "and the generated offsets, rather than a second computation of them");

        // The TEN real props come first — origin, angles, model index, effects,
        // interpolation frame, type id, schema hash, payload words, and the
        // solidity pair — and the class's declared fields are not
        // props at all any more: they are inside the payload. So the count is
        // the tier's, and it is the class's OWN tier since Part 18.
        //
        // WITH NO TIER TABLE THE FLOOR IS THE CLASS'S OWN DECLARATION, and that
        // changed here. It used to be six props and no payload, because the
        // caller passed the hierarchy's shared blob and zero meant "does not
        // know it". There is no unknown any longer — a class always knows its
        // own size — so reporting no payload for a class that plainly declares
        // fields would be the description understating what any build carries.
        Assert.True(described.PropCount == NetSchema.StandaloneSharedProps + state.Size / 4, "a hierarchy class's props are its own tier's, not one per declared field");

        var netEntity = NetSchema.FromLayout(tiers, GeneratedShell, published: true);

        Assert.True(netEntity.PropCount == GeneratedShell.Fields.Count, "the generated shell falls back to one prop per field with no tier to read");

        Assert.True(netEntity.Fields.Count(f => f.Kind == NetFieldKind.Vector) == 1
            && netEntity.PropCount == netEntity.Fields.Count, "a vector is one prop, not three");

        // Wire width and storage width are different numbers, and conflating
        // them is how every offset after a narrowed field goes wrong.
        // A BOOL IS ONE BIT IN THE BLOB TOO, since packing. The old shape of
        // this check — one bit on the wire, a whole byte in storage — was the
        // rule while a bool was its own SendProp taken against real storage.
        // The payload is dwords now and what a bit means inside one is entirely
        // this side's business, so a bool costs a bit of a word it shares.
        Assert.True(described.Fields.Single(f => f.Kind == NetFieldKind.Bool)
                is { Bits: 1, Bytes: 4, IsPacked: true },
            "a bool is one bit, in a word it shares");

        Assert.True(described.Fields.Single(f => f.Kind == NetFieldKind.Vector) is { Bits: 32, Bytes: 12 }, "a vector's wire width is per component, not for the whole field");

        // Native rounds a class's blob to sixteen; the layout is rounded to
        // four. A reader looking at a hex dump longer than the layout should be
        // told why rather than left wondering what the extra bytes mean.
        Assert.True(described.BlobBytes % 16 == 0 && described.BlobBytes >= described.LayoutBytes, "the blob native reserves is the layout rounded up to sixteen");

        Assert.True(NetSchema.BlobBytesFor(new[] { new NetSchemaField("A", "int", NetFieldKind.Int, 0, 4, 32) }) == 16, "a four-byte layout still takes a sixteen-byte blob");

        // A state declared before its entity class moved onto the hierarchy is
        // described and networks nothing. "Declared but not on the wire" must
        // not read the same as "on the wire and decoding to zero".
        var unpublished = NetSchema.FromStandalone(tiers, state, published: false);
        Assert.True(!unpublished.Published, "a type that was not published says so");

        var all = NetSchema.Describe(tiers, 
            new[] { GeneratedShell },
            new[] { state, StandaloneNetStates.Of(typeof(Neighbours)) },
            name => name != "ss_schema_neighbours");

        Assert.True(all.Count == 3
            && all.Count(t => t.Shell == NetSchemaShell.NetEntity) == 1
            && all.Count(t => t.Shell == NetSchemaShell.Standalone) == 2, "the description covers both shells");

        Assert.True(all.Single(t => t.ClassName == "ss_schema_neighbours").Published == false
            && all.Single(t => t.ClassName == "ss_schema_every_kind").Published, "the publish predicate is asked per class");

        // Both spellings, because the route is /nettable/<typeid> and the verb
        // in the plan is "./debug-api nettable prop_physics".
        Assert.True(NetSchema.Find(all, "ss_schema_every_kind")?.ClassName == "ss_schema_every_kind", "a type is findable by classname");

        Assert.True(NetSchema.Find(all, NetSchema.TypeIdOf("ss_schema_every_kind").ToString())
                ?.ClassName == "ss_schema_every_kind", "and by decimal type id");

        Assert.True(NetSchema.Find(all, $"0x{NetSchema.TypeIdOf("ss_schema_every_kind"):X8}")
                ?.ClassName == "ss_schema_every_kind"
            && NetSchema.Find(all, $"{NetSchema.TypeIdOf("ss_schema_every_kind"):X8}")
                ?.ClassName == "ss_schema_every_kind", "and by hex type id, with the 0x or without it");

        Assert.True(NetSchema.Find(all, "no_such_class") is null, "and not by something that is neither");
    }

    //-------------------------------------------------------------------------
    // Reading bytes back. The half that earns the file.
    //-------------------------------------------------------------------------
    [Fact]
    public void Decoding()
    {
        var tiers = NoTiers;

        var type = NetSchema.FromStandalone(tiers, StandaloneNetStates.Of(typeof(EveryKind)), published: true);

        // WRITTEN THROUGH THE ACCESSORS, which is what this fixture had to
        // become when packing landed. It used to copy bytes to each field's
        // offset, on the reasoning that a hand-laid blob would be a second
        // opinion about where the fields are. That reasoning survives and its
        // implementation does not: an offset is no longer the whole placement,
        // so copying two bytes to Small's offset now overwrites Broken and Tiny
        // as well.
        //
        // Going through the generated setters is the same argument taken one
        // step further. The fixture is built by the only code that knows the
        // packing, and what the decode is then checked against is what a real
        // entity would actually be holding — so this checks the WRITER and the
        // READER agree, which the old shape could not.
        var written = new EveryKind();
        written.BindLocal();

        written.Where  = new Vector(1.5f, -2.25f, 3f);
        written.Count  = -4242;
        written.Bits   = 0xDEADBEEFu;
        written.Broken = true;
        written.Ratio  = 0.125f;
        written.Small  = -300;
        written.Tiny   = 200;
        written.Label  = "hello";

        byte[] blob = written.Snapshot();

        var decoded = NetSchema.Decode(type, blob).ToDictionary(d => d.Field.Name);

        Assert.True(decoded.Values.All(d => d.Present), "every field decodes");

        Assert.True(decoded["Where"].Value == "(1.5, -2.25, 3)", "a vector decodes component by component");

        // A signed int carrying a negative value is the case a decoder written
        // for unsigned quietly gets wrong, and the wrong answer looks like a
        // huge positive number rather than an error.
        Assert.True(decoded["Count"].Value == "-4242", "a signed int keeps its sign");

        // And the reverse: an unsigned field above the signed range must not
        // come back negative. Part 6's own words about why UInt is a separate
        // kind rather than a flag.
        Assert.True(decoded["Bits"].Value == "3735928559", "an unsigned int above the signed range stays positive");

        Assert.True(decoded["Small"].Value == "-300", "a narrow signed field keeps its sign at its own width");

        Assert.True(decoded["Tiny"].Value == "200", "a byte above 127 is not a negative sbyte");

        Assert.True(decoded["Ratio"].Value == "0.125"
            && decoded["Ratio"].Number == 0.125, "a float decodes");

        Assert.True(decoded["Broken"].Value == "true", "a bool decodes");

        // To the terminator, as the engine's recv proxy does. A string read to
        // its buffer length would carry fifteen NULs into every comparison.
        Assert.True(decoded["Label"].Value == "hello", "a string stops at the terminator");

        // A vector and a string have no number, and null is the only honest way
        // to say so — a zero would be a measurement rather than the absence of
        // one.
        Assert.True(decoded["Where"].Number is null && decoded["Label"].Number is null, "a vector and a string report no number");

        // The hex beside each value is what makes the decode checkable by eye
        // against the ruler, so it has to be the field's OWN bytes.
        Assert.True(decoded.Values.All(d =>
                d.Hex == NetSchema.Hex(blob.AsSpan(d.Field.Offset, d.Field.Bytes))), "each field's hex is exactly the bytes at its own offset");

        Assert.True(NetSchema.Decode(type, new byte[type.BlobBytes]).All(d => d.Present)
            && NetSchema.Decode(type, new byte[type.BlobBytes])
                        .Single(d => d.Field.Name == "Count").Value == "0", "a zero blob decodes to zeroes rather than refusing");
    }

    //-------------------------------------------------------------------------
    // The neighbour tests.
    //
    // The whole point of the inspector, in Part 13's own words: a field reading
    // the wrong bytes shows up as a value that moves when its NEIGHBOUR is
    // written. That is only a symptom if writing a neighbour genuinely leaves a
    // field alone, so that is what is checked here.
    //-------------------------------------------------------------------------
    [Fact]
    public void Neighbouring()
    {
        var tiers = NoTiers;

        var type = NetSchema.FromStandalone(tiers, StandaloneNetStates.Of(typeof(Neighbours)), published: true);
        byte[] blob = new byte[type.BlobBytes];

        Put(blob, type, "First", BitConverter.GetBytes(11));
        Put(blob, type, "Second", BitConverter.GetBytes(22));
        Put(blob, type, "Third", BitConverter.GetBytes(33));

        var before = NetSchema.Decode(type, blob).ToDictionary(d => d.Field.Name, d => d.Value);

        Assert.True(before["First"] == "11" && before["Second"] == "22" && before["Third"] == "33", "three neighbouring ints decode to three different values");

        Put(blob, type, "Second", BitConverter.GetBytes(999));
        var after = NetSchema.Decode(type, blob).ToDictionary(d => d.Field.Name, d => d.Value);

        Assert.True(after["Second"] == "999", "writing one field moves that field");

        Assert.True(after["First"] == before["First"] && after["Third"] == before["Third"], "and leaves its neighbours exactly where they were");

        // And the failure the inspector is for, staged: a layout whose middle
        // field is one byte off reads a value nobody wrote. If the decoder could
        // not tell these apart it would have nothing to report.
        var shifted = NetSchemaType.Create(
            type.ClassName, type.TypeName, type.Shell,
            new[]
            {
                type.Fields[0],
                type.Fields[1] with { Offset = type.Fields[1].Offset + 1 },
                type.Fields[2],
            },
            type.LayoutBytes, type.PropCount, published: true);

        var wrong = NetSchema.Decode(shifted, blob).ToDictionary(d => d.Field.Name, d => d.Value);

        Assert.True(wrong["Second"] != after["Second"], "a layout one byte off reads a value nobody wrote");

        // And the hash catches it before anybody has to notice the value. This
        // is the check that becomes the schema-mismatch banner at Step 6.
        Assert.True(shifted.SchemaHash != type.SchemaHash, "and its schema hash differs, which is what a mismatch banner reads");
    }

    //-------------------------------------------------------------------------
    // Fail closed. Borrowed native memory is what is being read here.
    //-------------------------------------------------------------------------
    [Fact]
    public void FailingClosed()
    {
        var tiers = NoTiers;

        var type = NetSchema.FromStandalone(tiers, StandaloneNetStates.Of(typeof(Neighbours)), published: true);

        // Per field, not as a whole: a short blob is a real state worth
        // reporting, and saying WHICH fields fell off the end says far more than
        // refusing the read.
        //
        // The five bytes are five bytes OF THE CLASS'S OWN STATE. Every blob
        // opens with the engine block (plan_entity.md §3.2), so a buffer that
        // stopped before the end of it would not truncate the first field -- it
        // would truncate every field, and the fact below about which one
        // survives would be about nothing.
        var truncated = NetSchema.Decode(type, new byte[EngineBlock.SizeBytes + 5]);

        Assert.True(truncated[0].Present, "a field that fits in a short blob still decodes");

        Assert.True(truncated.Skip(1).All(d => !d.Present && d.Value == "" && d.Number is null), "a field past the end is absent rather than zero");

        Assert.True(NetSchema.Decode(type, Array.Empty<byte>()).All(d => !d.Present), "an empty blob decodes nothing and throws nothing");

        // A negative offset cannot arise from the generator, and a decoder that
        // trusted one would index before the start of engine memory.
        var negative = NetSchemaType.Create("x", "x", NetSchemaShell.Standalone,
            new[] { new NetSchemaField("Bad", "int", NetFieldKind.Int, -4, 4, 32) },
            4, 1, published: true);

        Assert.True(!NetSchema.Decode(negative, new byte[16])[0].Present, "a negative offset is refused rather than followed");
    }

    //-------------------------------------------------------------------------
    // What changed since last time.
    //-------------------------------------------------------------------------
    [Fact]
    public void Diffing()
    {
        var tiers = NoTiers;

        byte[] before = { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.True(NetSchema.ChangedRuns(before, before).Count == 0, "an identical blob has no runs");

        Assert.True(NetSchema.ChangedRuns(before, new byte[] { 1, 2, 9, 4, 5, 6, 7, 8 })
                .SequenceEqual(new[] { (2, 1) }), "one changed byte is one run of one");

        Assert.True(NetSchema.ChangedRuns(before, new byte[] { 1, 9, 9, 9, 5, 6, 7, 8 })
                .SequenceEqual(new[] { (1, 3) }), "adjacent changes merge into one run");

        Assert.True(NetSchema.ChangedRuns(before, new byte[] { 9, 2, 3, 4, 5, 6, 7, 9 })
                .SequenceEqual(new[] { (0, 1), (7, 1) }), "separated changes stay separate runs");

        Assert.True(NetSchema.ChangedRuns(before, new byte[] { 1, 2, 3, 4, 9, 9, 9, 9 })
                .SequenceEqual(new[] { (4, 4) }), "a run reaching the end is closed");

        // A length change means the two samples are not of the same shape. The
        // comparison stops at the shorter length rather than reporting the tail
        // as changed, which would be a claim about bytes that were never
        // compared.
        Assert.True(NetSchema.ChangedRuns(before, new byte[] { 1, 2, 3 }).Count == 0, "a shorter blob is compared only as far as it goes");

        var field = new NetSchemaField("A", "int", NetFieldKind.Int, 4, 4, 32);

        Assert.True(NetSchema.Touches(field, new[] { (5, 1) }), "a run inside a field touches it");

        Assert.True(NetSchema.Touches(field, new[] { (2, 3) }), "a run overlapping a field's first byte touches it");

        Assert.True(!NetSchema.Touches(field, new[] { (0, 4) }), "a run ending exactly where a field starts does not");

        Assert.True(!NetSchema.Touches(field, new[] { (8, 4) }), "a run starting exactly where a field ends does not");
    }

    //-------------------------------------------------------------------------
    // The tiers of Part 7, which NetTiers now READS rather than projects.
    //
    // The boundary rule has exactly one implementation — SourceSharp_TierForSize
    // in generic_nettable.h — and exactly one test of its values, the gtest that
    // Part 15 asks for on 0/32/33/256/257/2048/2049. What is checked here is
    // what NetTiers DOES with a tier list, over a list it is handed, because a
    // second copy of {32, 256, 2048} in C# is precisely the projection Step 6
    // removed.
    //
    // The distinction matters and is easy to lose: these tests would pass just
    // as happily against tiers of {1, 2, 3}. That is the point. Whether the
    // sizes are right is a question about the C++, and it is asked there.
    //-------------------------------------------------------------------------
    [Fact]
    public void Tiers()
    {
        // A table of this test's own. It was a process-wide static, and this
        // test opened by calling Forget on it — clearing a table every other
        // suite could also be reading.
        // Ignoring the read: with no engine the table is empty, which is the
        // state the first assertions are about.
        var tiers = new NetTiers(Fake().Ignores("Networking.NetTierAt"));

        // With nothing to ask — no engine, which is this test suite — the answer
        // is EMPTY rather than a guess. A fallback list would be the projection
        // back again wearing the word "fallback", so every caller has to have an
        // answer for "cannot say", and this is what it looks like.

        Assert.True(tiers.Sizes.Count == 0, "with no engine there are no tiers, rather than a guessed list");

        Assert.True(tiers.Ceiling(16) == -1, "and a size cannot be assigned to one");

        // Supplied the way native would report it. The prop counts are the
        // hierarchy's — ten real props in front of the payload — and they are
        // data here rather than arithmetic, which is the whole change.
        tiers.SupplyForTests(NetShellKind.Standalone, new[]
        {
            new NetTierInfo(0, "DT_SourceSharpStdEnt32",   32,     8,   18, true),
            new NetTierInfo(1, "DT_SourceSharpStdEnt256",  256,   64,   74, false),
            new NetTierInfo(2, "DT_SourceSharpStdEnt2048", 2048, 512,  522, false),
        });

        Assert.True(tiers.Ceiling(0) == 32, "an empty layout still needs the smallest tier");
        Assert.True(tiers.Ceiling(32) == 32, "a layout exactly on a boundary stays in that tier");
        Assert.True(tiers.Ceiling(33) == 256, "one byte over moves up");
        Assert.True(tiers.Ceiling(256) == 256, "the middle tier holds to its own boundary");
        Assert.True(tiers.Ceiling(257) == 2048, "and one over that moves up again");
        Assert.True(tiers.Ceiling(2048) == 2048, "the largest tier is the array ceiling");
        Assert.True(tiers.Ceiling(2049) == -1, "and there is nothing above it");

        Assert.True(tiers.Sizes.Count == 3 && tiers.Sizes[0] == 32
            && tiers.Sizes[1] == 256 && tiers.Sizes[2] == 2048, "the sizes are the ones native reported, in order");

        // A tier registered and never used is the honest middle state: the class
        // list carries it so a type can grow into it without a rebuilt client,
        // and nothing is on it yet.
        var rows = tiers.For(NetShellKind.Standalone);

        Assert.True(rows[0].Live && !rows[1].Live && !rows[2].Live, "a tier says whether anything is actually on it");

        Assert.True(rows[0].Props == 18 && rows[1].Props == 74 && rows[2].Props == 522, "and reports the props native built rather than a formula");

        // The flag Part 13 asks the schema panel for: within a tenth of
        // outgrowing a tier, because that is the one schema change that still
        // needs a restart and it should be seen coming.
        Assert.True(!tiers.NearCeiling(16), "a layout well inside its tier is not flagged");
        Assert.True(tiers.NearCeiling(29), "a layout at nine tenths of its tier is flagged");
        Assert.True(tiers.NearCeiling(32), "a layout exactly on its ceiling is flagged");

        // A type too big for any tier is not nearly full, it is unnetworkable —
        // and the flag has to fire on it rather than divide by a ceiling of -1.
        Assert.True(tiers.NearCeiling(4096) && tiers.Fill(4096) == 1.0, "a layout too big for any tier is flagged rather than divided by");

        // THE TABLE IS KEYED BY SHELL, which is what this actually tests.
        //
        // It used to read "the two shells put different numbers of real props in
        // front of the payload". That shell is deleted and native answers six
        // for every nShell, so left alone this would be a fabricated fixture
        // asserting a fact that had stopped being true — coverage that reads as
        // a check and is not one. What remains: a table supplied for one shell
        // must not answer for the other.
        tiers.SupplyForTests(NetShellKind.NetEntity, new[]
        {
            new NetTierInfo(0, "DT_Retired32", 32, 8, 11, false),
        });

        Assert.True(tiers.For(NetShellKind.NetEntity)[0].Table == "DT_Retired32"
            && tiers.For(NetShellKind.Standalone)[0].Table == "DT_SourceSharpStdEnt32", "the tier table is keyed by shell, and one shell's rows do not answer for the other");

        Assert.True(tiers.For(NetShellKind.NetEntity).Count == 1
            && tiers.For(NetShellKind.Standalone).Count == 3, "and the two keys hold their own row counts");
    }

    //-------------------------------------------------------------------------
    // The routes, driven through the router the way every other one is.
    //-------------------------------------------------------------------------
    [Fact]
    public void Routing()
    {
        var tiers = SupplyTiers();

        var fixture = new SchemaFixture();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server(netSchema: fixture.Hook()));

        var everyKind = NetSchema.FromStandalone(tiers, 
            StandaloneNetStates.Of(typeof(EveryKind)), published: true);
        var neighbours = NetSchema.FromStandalone(tiers, 
            StandaloneNetStates.Of(typeof(Neighbours)), published: false);

        // A host that wired none of this up says so rather than answering with
        // an empty schema, which would read as "this mod networks nothing".
        // G5: the schema comes through the server's IDebugNetSchema, which is
        // always there; with nothing in it the route lists no types.
        Assert.Contains("\"types\":[],\"count\":0", Body(server, "/nettables"), StringComparison.Ordinal);

        fixture.DescribeNetTables = () => new[] { everyKind, neighbours };

        // The tier table the view reports. Supplied by this test rather than
        // read from a static one another suite might have filled.
        fixture.ReadNetTiers = () => tiers;

        string schema = Body(server, "/nettables");

        Assert.True(IsJson(schema), "the schema answers with valid JSON");

        // The table is the TIER's, not DT_<classname>. A class has no table of
        // its own since Step 6 — its entities are created through a tier — so
        // reporting the old name would be naming something that does not exist.
        // With no engine to read a tier from, the DT_ name is what is left, and
        // that is what this fixture exercises.
        Assert.True(schema.Contains($"\"table\":\"{everyKind.TableName}\"")
            && schema.Contains($"\"typeIdHex\":\"0x{everyKind.TypeId:X8}\"")
            && schema.Contains($"\"schemaHashHex\":\"0x{everyKind.SchemaHash:X8}\"")
            && schema.Contains($"\"layoutBytes\":{everyKind.LayoutBytes}"), "it carries each type's table, ids and sizes");

        Assert.True(schema.Contains("\"className\":\"ss_schema_neighbours\"")
            && schema.Contains("\"published\":false"), "a type that is not on the wire is listed and marked");

        // The tier block is a READING since Step 6, and it says which: a panel
        // that showed a projection as though it were a reading would be the
        // first thing to mislead somebody, and so would one that showed an
        // absent reading as "this build has no tiers".
        Assert.True(schema.Contains("\"tiersLive\":true")
            && schema.Contains("\"name\":\"DT_SourceSharpStdEnt32\""), "the tier rollup reports what native registered");

        Assert.True(schema.Contains("\"payloadBytes\":32,\"words\":8,\"props\":20,\"live\":true")
            && schema.Contains("\"payloadBytes\":256,\"words\":64,\"props\":76,\"live\":false"), "a tier reports the props native built, and whether anything is on it");

        string one = Body(server, "/nettable/ss_schema_every_kind");

        Assert.True(IsJson(one), "one type answers with valid JSON");

        Assert.True(one.Contains("\"name\":\"Where\"") && one.Contains("\"csType\":\"Vector\"")
            && one.Contains("\"kind\":\"vector\"") && one.Contains("\"bytes\":12"), "and lists its fields with their offsets and widths");

        Assert.True(IsJson(Body(server, $"/nettable/0x{everyKind.TypeId:X8}"))
            && Body(server, $"/nettable/0x{everyKind.TypeId:X8}").Contains("ss_schema_every_kind"), "a type is reachable by its type id too");

        // The usual reason for a miss is a name that is declared but spelled
        // differently, so the error names what does exist.
        string missing = Body(server, "/nettable/no_such_thing");

        Assert.True(IsJson(missing) && missing.Contains("no networked type")
            && missing.Contains("ss_schema_every_kind"), "an unknown type names what is known rather than answering nothing");

        PayloadRouting(server, fixture, everyKind);
    }

    //-------------------------------------------------------------------------
    // The refusal ledger: one per module, read by the panel it is handed to.
    //-------------------------------------------------------------------------
    [Fact]
    public void ARefusalIsRecordedOncePerClassName()
    {
        var ledger = new NetTypeRefusals();

        ledger.Record("ss_too_big", 0x1234u, 4096);
        ledger.Record("ss_too_big", 0x1234u, 4096);

        Assert.Single(ledger.All);
        Assert.Equal(new NetTypeRefusal("ss_too_big", 0x1234u, 4096), ledger.All[0]);
    }

    [Fact]
    public void TwoLedgersDoNotShareRefusals()
    {
        // The property the static list lacked: in this assembly, loaded once
        // for both sides, it was one list for the whole process.
        var mine = new NetTypeRefusals();
        var theirs = new NetTypeRefusals();

        theirs.Record("ss_too_big", 0x1234u, 4096);

        Assert.Empty(mine.All);
        Assert.Single(theirs.All);
    }

    [Fact]
    public void TheSchemaPanelListsTheRefusalsItIsGiven()
    {
        var ledger = new NetTypeRefusals();
        ledger.Record("ss_too_big", 0xCAFEF00Du, 4096);

        var fixture = new SchemaFixture();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server(netSchema: fixture.Hook()));
        fixture.DescribeNetTables = () => Array.Empty<NetSchemaType>();
        fixture.ReadNetTiers = () => SupplyTiers();

        string before = Body(server, "/nettables");
        Assert.True(IsJson(before) && before.Contains("\"refused\":[]"),
                    "with no ledger supplied the panel lists nothing refused");

        fixture.ReadNetRefusals = () => ledger;
        string schema = Body(server, "/nettables");

        Assert.True(IsJson(schema), "the schema answers with valid JSON");
        Assert.True(schema.Contains("\"refused\":[{\"className\":\"ss_too_big\",\"typeIdHex\":\"0xCAFEF00D\",\"layoutBytes\":4096}]"),
                    "the panel lists the supplied ledger's refusals");
    }

    //-------------------------------------------------------------------------
    // Golden JSON (plan_devapi_interfaces P2, group G5), written before the
    // schema, tiers, refusals and payloads moved behind IDebugNetSchema.
    //-------------------------------------------------------------------------

    /// <summary>A server with two types, the tiers, one refusal and one entity's payload.</summary>
    private static (DebugApiServer Server, byte[] Blob, NetSchemaType Type) GoldenSchemaServer()
    {
        var tiers = SupplyTiers();
        var everyKind = NetSchema.FromStandalone(tiers, StandaloneNetStates.Of(typeof(EveryKind)), published: true);
        var neighbours = NetSchema.FromStandalone(tiers, StandaloneNetStates.Of(typeof(Neighbours)), published: false);
        var ledger = new NetTypeRefusals();
        ledger.Record("ss_too_big", 0xCAFEF00Du, 4096);

        byte[] blob = new byte[everyKind.BlobBytes];
        Put(blob, everyKind, "Count", BitConverter.GetBytes(7));
        Put(blob, everyKind, "Label", Encoding.UTF8.GetBytes("crate\0"));

        var fixture = new SchemaFixture();
        var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server(netSchema: fixture.Hook()));
        fixture.DescribeNetTables = () => new[] { everyKind, neighbours };
        fixture.ReadNetTiers = () => tiers;
        fixture.ReadNetRefusals = () => ledger;
        fixture.ReadEntityPayload = index => index == 42
            ? new NetEntityBlob(true, 42, 0xABCDu, "ss_schema_every_kind", blob.ToArray())
            : NetEntityBlob.Missing(index);
        return (server, blob, everyKind);
    }

    internal static string MaskSince(string json)
        => System.Text.RegularExpressions.Regex.Replace(json, "\"sinceSeconds\":[-0-9.E]+", "\"sinceSeconds\":0");

    /// <summary>The recorded body, from src/test/managed/Golden/DebugApi.</summary>
    private static string Golden(string name)
    {
        string path = RepoLayout.Repo("src", "test", "managed", "Golden", "DebugApi", $"schema-{name}.json");
        Assert.True(File.Exists(path), $"{path} is missing; a golden that read nothing would pass");
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_schema_routes_render_exactly()
    {
        var (server, _, type) = GoldenSchemaServer();
        using (server)
        {
            Assert.Equal(Golden("nettables"), Body(server, "/nettables"));
            Assert.Equal(Golden("nettable"), Body(server, "/nettable/ss_schema_every_kind"));
            Assert.Equal(Golden("nettable-id"), Body(server, $"/nettable/0x{type.TypeId:X8}"));
            Assert.Equal(Golden("nettable-missing"), Body(server, "/nettable/no_such_thing"));
        }
    }

    [Fact]
    public void A_payload_renders_exactly_first_and_after_a_change()
    {
        var (server, blob, type) = GoldenSchemaServer();
        using (server)
        {
            Assert.Equal(Golden("payload-first"), MaskSince(OnAPumpedServer(server, s => Body(s, "/entity/42/payload"))));
            Put(blob, type, "Count", BitConverter.GetBytes(9));
            Assert.Equal(Golden("payload-moved"), MaskSince(OnAPumpedServer(server, s => Body(s, "/entity/42/payload"))));
            Assert.Equal(Golden("payload-absent"), OnAPumpedServer(server, s => Body(s, "/entity/999/payload")));
        }
    }

    /// <summary>
    /// What the server's IDebugNetSchema reads (G5), settable as a test goes:
    /// the same four sources the dev API used to take as delegates.
    /// </summary>
    private sealed class SchemaFixture
    {
        public Func<IReadOnlyList<NetSchemaType>> DescribeNetTables { get; set; } = () => [];
        public Func<INetTiers> ReadNetTiers { get; set; } = () => EmptyTiers.Instance;
        public Func<INetTypeRefusals?> ReadNetRefusals { get; set; } = () => null;
        public Func<int, NetEntityBlob> ReadEntityPayload { get; set; } = NetEntityBlob.Missing;

        public SourceSharp.Server.DebugNetSchema Hook()
            => new(() => DescribeNetTables(), () => ReadNetTiers(), () => ReadNetRefusals(), i => ReadEntityPayload(i));
    }

    private static void PayloadRouting(DebugApiServer server, SchemaFixture fixture,
                                       NetSchemaType type)
    {
        byte[] blob = new byte[type.BlobBytes];
        Put(blob, type, "Count", BitConverter.GetBytes(7));
        Put(blob, type, "Label", Encoding.UTF8.GetBytes("crate\0"));

        fixture.ReadEntityPayload = index => index == 42
            ? new NetEntityBlob(true, 42, 0xABCDu, "ss_schema_every_kind", blob.ToArray())
            : NetEntityBlob.Missing(index);

        string first = OnAPumpedServer(server, s => Body(s, "/entity/42/payload"));

        Assert.True(IsJson(first), "a payload answers with valid JSON");

        Assert.True(first.Contains("\"index\":42") && first.Contains("\"handle\":43981")
            && first.Contains("\"classname\":\"ss_schema_every_kind\""), "it carries the entity, not just the type");

        // Offset-annotated hex, one of Part 13's three views. The ascii column
        // is there because a string field is the one thing a hex dump makes
        // unreadable, and m_szClassname is 64 bytes of one on every entity.
        Assert.True(first.Contains("\"rows\":[{\"offset\":0,") && first.Contains("crate"), "the blob comes back as offset-annotated hex with an ascii column");

        Assert.True(first.Contains("\"name\":\"Count\"") && first.Contains("\"value\":\"7\"")
            && first.Contains("\"value\":\"crate\""), "and decoded field by field through the layout");

        // A first sample has nothing to compare against, and that is not the
        // same as nothing having changed.
        Assert.True(first.Contains("\"first\":true"), "the first sample says so rather than reporting no change");

        // Nothing written between the two reads, so nothing moved.
        string still = OnAPumpedServer(server, s => Body(s, "/entity/42/payload"));

        Assert.True(still.Contains("\"first\":false") && still.Contains("\"bytes\":0")
            && still.Contains("\"runs\":[]"), "a second sample of an unchanged blob reports no change");

        Put(blob, type, "Count", BitConverter.GetBytes(9));
        string moved = OnAPumpedServer(server, s => Body(s, "/entity/42/payload"));

        Assert.True(moved.Contains("\"value\":\"9\",\"hex\":\"09000000\",\"number\":9,\"changed\":true"), "a changed field is reported as changed");

        Assert.True(System.Text.RegularExpressions.Regex.Matches(moved, "\"changed\":true").Count == 1, "and only that field, which is what makes a neighbour bug visible");

        // The run is the BYTES that differ, not the field they belong to: 7 and
        // 9 differ in one byte of four. That is not a rounding-down of the
        // truth, it is the truth — Part 3 makes each dword its own prop, so the
        // bytes that differ are exactly the bytes the engine will send.
        Assert.True(moved.Contains($"\"runs\":[[{type.Fields.Single(f => f.Name == "Count").Offset},1]]"), "the changed run names the bytes that differ, inside the field");

        // An index nothing answers to is a real answer. An index is reused the
        // moment an entity is freed, so "nothing there" is worth telling apart
        // from a blob of zeroes.
        string absent = OnAPumpedServer(server, s => Body(s, "/entity/999/payload"));
        Assert.True(IsJson(absent) && absent.Contains("no entity"), "an index with no entity says so");

        // The path has a variable in the middle of it, which the route table
        // cannot express — so the matcher has to be tight enough not to swallow
        // whatever endpoint is added next.
        Assert.True(Reply(server, "/entity//payload").Status == 404, "a payload path with no index is not this route");

        Assert.True(Reply(server, "/entity/abc/payload").Status == 404, "a payload path with a non-numeric index is not this route");

        Assert.True(Reply(server, "/entity/42/payload/extra").Status == 404, "and neither is a longer path that merely starts the same way");

        PayloadAgreementRouting(server, fixture, blob);
    }

    //-------------------------------------------------------------------------
    // Payload agreement (plan_nettable_generic.md Part 14 Step 3).
    //
    // While both paths carry the same state, the route has to say whether they
    // do — and has to say the three "not a failure" answers apart from the one
    // failure, because a watch loop that cried wolf on every entity without a
    // payload would be turned off within the hour.
    //-------------------------------------------------------------------------
    private static void PayloadAgreementRouting(DebugApiServer server, SchemaFixture fixture,
                                                byte[] blob)
    {
        fixture.ReadEntityPayload = index => new NetEntityBlob(
            true, index, 1u, "ss_schema_every_kind", blob.ToArray(),
            new PayloadAgreement(PayloadAgreementState.Agrees, 8, -1));

        string agrees = OnAPumpedServer(server, s => Body(s, "/entity/42/payload"));

        Assert.True(agrees.Contains("\"payload\":{\"state\":\"agrees\",\"agrees\":true,\"words\":8")
            && agrees.Contains("\"firstBadWord\":-1") && agrees.Contains("\"live\":true"), "a payload route reports the two paths agreeing");

        fixture.ReadEntityPayload = index => new NetEntityBlob(
            true, index, 1u, "ss_schema_every_kind", blob.ToArray(),
            new PayloadAgreement(PayloadAgreementState.Disagrees, 8, 3));

        string differs = OnAPumpedServer(server, s => Body(s, "/entity/42/payload"));

        // The WORD, not a byte offset. The blob bases differ between client and
        // server by construction, so an offset means nothing across the pair and
        // an index means everything.
        Assert.True(differs.Contains("\"state\":\"disagrees\"") && differs.Contains("\"agrees\":false")
            && differs.Contains("\"firstBadWord\":3"), "a disagreement names the first word that differs");

        fixture.ReadEntityPayload = index => new NetEntityBlob(
            true, index, 1u, "ss_schema_every_kind", blob.ToArray(),
            new PayloadAgreement(PayloadAgreementState.NoPayload, 0, -1));

        string none = OnAPumpedServer(server, s => Body(s, "/entity/42/payload"));

        Assert.True(none.Contains("\"state\":\"none\"") && none.Contains("\"agrees\":false")
            && none.Contains("\"live\":true"), "an entity with no payload is not reported as a disagreement");

        // The default, which is what a caller with no game to ask gets. It must
        // not read as a disagreement either.
        fixture.ReadEntityPayload = index => new NetEntityBlob(
            true, index, 1u, "ss_schema_every_kind", blob.ToArray());

        string unsupported = OnAPumpedServer(server, s => Body(s, "/entity/42/payload"));

        Assert.True(unsupported.Contains("\"state\":\"unsupported\"")
            && unsupported.Contains("\"live\":false"), "a build that cannot answer says so rather than guessing");
    }

    //-------------------------------------------------------------------------
    // Helpers.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Writes bytes at a field's own offset.
    ///
    /// Through the layout rather than at a literal offset, deliberately: a
    /// fixture laid out by hand would be a second opinion about where the fields
    /// are, and the test would then be comparing two opinions rather than
    /// checking the decode.
    /// </summary>
    private static void Put(byte[] blob, NetSchemaType type, string name, byte[] bytes)
    {
        var field = type.Fields.Single(f => f.Name == name);
        bytes.AsSpan(0, Math.Min(bytes.Length, field.Bytes)).CopyTo(blob.AsSpan(field.Offset));
    }

    private static HttpReply Reply(DebugApiServer server, string path)
    {
        HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request);
        return server.Route(request);
    }

    private static string Body(DebugApiServer server, string path) => Reply(server, path).Body;

    /// <summary>
    /// Anything that reaches the game thread needs something pumping it. The
    /// payload route does, because the blob has to be lifted on the frame.
    /// </summary>
    private static string OnAPumpedServer(DebugApiServer server, Func<DebugApiServer, string> request)
    {
        using var stop = new CancellationTokenSource();

        var pump = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                server.Pump();
                Thread.Sleep(1);
            }
        }) { IsBackground = true };

        pump.Start();

        try { return request(server); }
        finally { stop.Cancel(); }
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
