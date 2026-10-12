using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The two composite kinds: <see cref="Color32"/> and <see cref="QAngle"/>.
///
/// <para>
/// WHAT EACH ONE IS FOR, because they are for different things. A Color32 is a
/// STORAGE change: four channels in one word, which is what <c>spotlight_end</c>
/// was already hand-packing out of four byte fields, made into one field with
/// one name and one range check. A QAngle is a TYPE change and nothing else — it
/// is bit-for-bit a Vector on the wire, and it exists because three numbers that
/// are a facing and three that are a position are indistinguishable everywhere
/// except in the type.
/// </para>
///
/// <para>
/// So the tests differ too. Colour is checked against the BYTES, because its
/// whole claim is that it produces the same word four byte fields did. Angle is
/// checked against a Vector's bytes for the same reason in reverse — the claim
/// is that they are identical, and a claim of identity is worth a test precisely
/// because nothing else would notice if it stopped being true.
/// </para>
/// </summary>
public partial class NetCompositeKindTests
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

    /// <summary>
    /// A colour with an int either side of it, to catch a stride bug.
    ///
    /// DECLARED COLOUR FIRST, WHICH IS NOW THE PAIR THAT DIVERGES. A Color32 is
    /// naturally four bytes, so it is a rule-1 field and leads; four narrow
    /// channels add to exactly 32, so they are a rule-2 word and sit BEHIND
    /// every rule-1 field in their class. Declaring the colour first therefore
    /// puts it at offset 0 while the channels it replaces land last — which is
    /// exactly the ordering fact <see cref="ConvertingChannelsMovesTheOrderAndNotTheSize"/>
    /// asserts against this pair.
    ///
    /// <para>
    /// IT USED TO BE THE OTHER WAY ROUND. The old packer hoisted narrow fields
    /// into a first pass, so channels declared FIRST stayed first and this pair
    /// was the byte-identical one; the byte comparison has moved to
    /// <see cref="WithColourLast"/> accordingly, where the layouts coincide
    /// under the new order.
    /// </para>
    /// </summary>
    [StandaloneNetState("ss_kind_colour")]
    private partial class WithColour
    {
        public partial Color32 Tint  { get; set; }
        public partial int    Before { get; set; }
        public partial int    After  { get; set; }
    }

    /// <summary>The four channels the colour replaces, declared the old way.</summary>
    [StandaloneNetState("ss_kind_channels")]
    private partial class WithChannels
    {
        [NetField(Bits = 8)] public partial byte Red   { get; set; }
        [NetField(Bits = 8)] public partial byte Green { get; set; }
        [NetField(Bits = 8)] public partial byte Blue  { get; set; }
        [NetField(Bits = 8)] public partial byte Alpha { get; set; }
        public partial int Before { get; set; }
        public partial int After  { get; set; }
    }

    /// <summary>
    /// The same six values with the colour declared LAST, which is where
    /// spotlight_end's are — AND THE PAIR THAT NOW COINCIDES.
    ///
    /// Rule 1 places Before and After first in both classes; the colour is a
    /// rule-1 field declared after them, and the four channels are the class's
    /// only rule-2 word, so both land at byte 8. That is what makes the byte
    /// comparison in <see cref="AColourPacksExactlyAsTheFourChannelsDid"/>
    /// possible: the two blobs are comparable only where the layouts agree.
    /// </summary>
    [StandaloneNetState("ss_kind_colour_last")]
    private partial class WithColourLast
    {
        public partial int    Before { get; set; }
        public partial int    After  { get; set; }
        public partial Color32 Tint  { get; set; }
    }

    /// <summary>The same, hand-packed, so the two orders can be compared.</summary>
    [StandaloneNetState("ss_kind_channels_last")]
    private partial class WithChannelsLast
    {
        public partial int Before { get; set; }
        public partial int After  { get; set; }
        [NetField(Bits = 8)] public partial byte Red   { get; set; }
        [NetField(Bits = 8)] public partial byte Green { get; set; }
        [NetField(Bits = 8)] public partial byte Blue  { get; set; }
        [NetField(Bits = 8)] public partial byte Alpha { get; set; }
    }

    /// <summary>A facing and a position, side by side, which is the whole point.</summary>
    [StandaloneNetState("ss_kind_angle")]
    private partial class WithAngle
    {
        public partial QAngle Facing { get; set; }
        public partial Vector Where  { get; set; }
    }

    //-------------------------------------------------------------------------
    // COLOUR IS ONE WORD, and it is the SAME word the channels were.
    //
    // The conversion of spotlight_end rests on this: if a Color32 did not lay
    // out exactly where four packed byte channels did, converting the class
    // would have been a wire change dressed as a tidy-up.
    //-------------------------------------------------------------------------
    [Fact]
    public void AColourIsOneWordAndNotSixteenBytes()
    {
        var colour   = WithColourLast.NetLayout;
        var channels = WithChannelsLast.NetLayout;

        var tint = colour.Fields.Single(f => f.Name == "Tint");

        Assert.True(tint.Kind == NetFieldKind.Color32, "a Color32 property resolves to the Color32 kind");

        // FOUR, not sizeof(Color32). The C# type is four ints and sixteen bytes
        // wide; what it costs here is the one word its channels pack into.
        Assert.True(tint.Bytes == 4, $"a colour occupies one word, not {tint.Bytes} bytes");

        Assert.True(tint.BitShift == 0 && !tint.IsPacked, "and it occupies that word WHOLE rather than sharing it");

        // The same size as the four channels it replaces, and the same size for
        // the class -- which is what makes converting one a field-list change
        // rather than a layout change.
        Assert.True(colour.Size == channels.Size, $"the class is the same size either way ({colour.Size} vs {channels.Size})");

        // THE MATCHING ORDER IS COLOUR-LAST NOW. Both classes declare Before and
        // After first, both are rule-1 fields, so both classes start Before at 0
        // and After at 4 — and the colour and the channel word both land at 8,
        // the colour as the last rule-1 field and the channels as the only
        // rule-2 word. Everything BEFORE the colour lands where it did, which is
        // the form of the claim that survives a rule where a whole word sorts
        // ahead of a packed one.
        Assert.True(colour.Fields.Single(f => f.Name == "Before").Offset
              == channels.Fields.Single(f => f.Name == "Before").Offset
              && colour.Fields.Single(f => f.Name == "After").Offset
              == channels.Fields.Single(f => f.Name == "After").Offset, "and, declared in the matching order, everything around it lands where it did");

        Assert.True(tint.Offset == channels.Fields.Single(f => f.Name == "Red").Offset, "with the colour on exactly the word the four channels were on");
    }

    //-------------------------------------------------------------------------
    // WHAT CONVERTING A CLASS ACTUALLY COSTS, found by the byte comparison
    // above failing before this fixture was reordered.
    //
    // Four narrow channels SINK: they add to exactly 32, which is rule 2, and
    // every rule-1 field in the class is placed ahead of them. A Color32 IS a
    // rule-1 field -- naturally four bytes, taken whole -- so it stays among
    // them, in declaration order. Converting therefore moves the fields within
    // the class even though the class is the same size, which is a schema-hash
    // change and nothing more, and is worth knowing before somebody reads a hex
    // dump from before the change against a layout from after it.
    //
    // THE PAIR THIS IS ASKED OF SWAPPED WHEN THE BUCKETS LANDED. It used to be
    // the colour-LAST pair, because the old packer hoisted narrow fields to the
    // front and a colour declared last stayed last. Now it is the colour-FIRST
    // pair: a colour declared first stays first and channels declared first
    // sink behind the two ints. Same fact, opposite fixtures, and the byte
    // comparison moved the other way for the same reason.
    //-------------------------------------------------------------------------
    [Fact]
    public void ConvertingChannelsMovesTheOrderAndNotTheSize()
    {
        var colour   = WithColour.NetLayout;
        var channels = WithChannels.NetLayout;

        Assert.True(colour.Size == channels.Size, "declared first, the colour class is still the same size as the channel class");

        // The channels sank behind the two ints; the colour did not.
        Assert.True(channels.Fields.Single(f => f.Name == "Red").Offset == Block + 8, "four channels declared FIRST are placed LAST, behind the whole-word fields");

        Assert.True(colour.Fields.Single(f => f.Name == "Tint").Offset == Block + 0, "while a colour declared first stays first, because it is naturally four bytes");

        Assert.True(channels.Fields.Single(f => f.Name == "Before").Offset
              != colour.Fields.Single(f => f.Name == "Before").Offset, "so converting moves the fields around the colour, though not the size of the class");
    }

    //-------------------------------------------------------------------------
    // AND THE SAME BYTES. Written both ways, the blobs are identical.
    //
    // ASKED OF THE COLOUR-LAST PAIR SINCE THE BUCKETS LANDED. A whole blob can
    // only be compared between two classes the packer lays out identically, and
    // that is now the pair whose colour is declared after the ints: rule 1 puts
    // Before and After at 0 and 4 in both, then the colour takes word 2 as the
    // last rule-1 field and the four channels take word 2 as the only rule-2
    // one. The colour-FIRST pair no longer lays out alike -- that is the
    // ordering fact above -- so comparing its blobs would have been comparing
    // two different layouts and calling the difference a packing bug.
    //-------------------------------------------------------------------------
    [Fact]
    public void AColourPacksExactlyAsTheFourChannelsDid()
    {
        var colour = new WithColourLast();
        colour.BindLocal();

        var channels = new WithChannelsLast();
        channels.BindLocal();

        colour.Before = 7;
        colour.Tint   = new Color32(0x12, 0x34, 0x56, 0x78);
        colour.After  = 9;

        channels.Before = 7;
        channels.Red    = 0x12;
        channels.Green  = 0x34;
        channels.Blue   = 0x56;
        channels.Alpha  = 0x78;
        channels.After  = 9;

        Assert.True(colour.Snapshot().SequenceEqual(channels.Snapshot()), "a Color32 and four hand-packed byte channels produce byte-identical blobs");

        // And it reads back. A pack that round-trips through its own unpack is
        // not proof of the byte order; the blob comparison above is, and this is
        // proof the accessors agree with it.
        Assert.True(colour.Tint == new Color32(0x12, 0x34, 0x56, 0x78), "and the colour reads back as it was written");

        // R IS THE LOW BYTE, pinned as a literal rather than left to the
        // round trip. This is the one fact the far side has to share.
        int at = WithColourLast.NetLayout.Fields.Single(f => f.Name == "Tint").Offset;

        Assert.True(BitConverter.ToUInt32(colour.Snapshot(), at) == 0x78563412u, "R is the low byte of the word and A the high one");
    }

    //-------------------------------------------------------------------------
    // A CHANNEL THAT IS NOT A CHANNEL IS REFUSED.
    //
    // Color32's channels are int, so 300 is a value a call site can hold and the
    // compiler is content. Masking it would store 44 and send a plausible wrong
    // colour, which is what every other narrow field in this tree throws for.
    //-------------------------------------------------------------------------
    [Fact]
    public void AChannelOutsideItsByteIsRefused()
    {
        var state = new WithColour();
        state.BindLocal();

        state.Before = 0x5A5A5A;
        state.Tint   = new Color32(1, 2, 3, 4);
        state.After  = 0xA5A5A5;

        string message = "";

        try   { state.Tint = new Color32(300, 2, 3, 4); }
        catch (FieldRangeException e) { message = e.Message; }

        Assert.True(message.Length > 0, "a channel above 255 is refused rather than masked");

        Assert.True(message.Contains("WithColour") && message.Contains("Tint")
              && message.Contains("R") && message.Contains("300"), "and the refusal names the class, the field, the CHANNEL and the value");

        Assert.True(Throws(() => state.Tint = new Color32(1, 2, 3, -1)), "a negative channel is refused too, which masking would have turned into 255");

        // Every channel, counted rather than trusted: a check written for R and
        // pasted three times is a check that can be pasted three times wrongly.
        int refused = 0;

        foreach (var bad in new[] { new Color32(256, 0, 0, 0), new Color32(0, 256, 0, 0),
                                    new Color32(0, 0, 256, 0), new Color32(0, 0, 0, 256) })
        {
            if (Throws(() => state.Tint = bad))
                refused++;
        }

        Assert.True(refused == 4, $"all four channels are checked, not {refused} of them");

        // THE REFUSAL LEAVES THE BLOB ALONE, including the neighbours. A
        // half-written colour beside two ints is worse than either outcome.
        Assert.True(state.Tint == new Color32(1, 2, 3, 4) && state.Before == 0x5A5A5A
              && state.After == 0xA5A5A5, "and a refused colour changes neither its own word nor its neighbours");
    }

    //-------------------------------------------------------------------------
    // A WRITE THAT CHANGES NOTHING MUST NOT MARK.
    //
    // The colour accessors are the only ones that do not go through Write<T>
    // directly, so the no-op rule had to be checked for them rather than
    // inherited. A redundant store costs a full encode of the whole payload.
    //-------------------------------------------------------------------------
    [Fact]
    public unsafe void RewritingTheSameColourDoesNotDirty()
    {
        var layout = WithColour.NetLayout;

        // Native's half by hand, as the mapped-memory tests do it: only a real
        // slot has a dirty word, and BindLocal's is a shared stub.
        byte[] blob = GC.AllocateArray<byte>(layout.Size + 8, pinned: true);
        EntitySlot[] slot = GC.AllocateArray<EntitySlot>(1, pinned: true);

        const uint serial = 909u;
        slot[0].Serial = serial;

        var state = new WithColour();
        state.BindTo(new EntityHandle(0x00010009u),
                     (byte*)Unsafe.AsPointer(ref blob[0]), blob.Length,
                     (EntitySlot*)Unsafe.AsPointer(ref slot[0]), serial);

        state.Tint = new Color32(10, 20, 30, 40);
        Assert.True(slot[0].Dirty == 1, "writing a colour marks the entity");

        slot[0].Dirty = 0;
        _ = state.Tint;
        Assert.True(slot[0].Dirty == 0, "reading one marks nothing");

        state.Tint = new Color32(10, 20, 30, 40);
        Assert.True(slot[0].Dirty == 0, "and storing the colour that is already there marks nothing either");

        // One channel by one, which is the smallest change there is. A no-op
        // rule that swallowed this would be worse than no rule.
        state.Tint = new Color32(10, 20, 30, 41);
        Assert.True(slot[0].Dirty == 1, "while changing one channel by one does mark");
    }

    //-------------------------------------------------------------------------
    // AN ANGLE IS A VECTOR ON THE WIRE. Same bytes, deliberately.
    //-------------------------------------------------------------------------
    [Fact]
    public void AnAngleIsBitForBitAVector()
    {
        var layout = WithAngle.NetLayout;

        var facing = layout.Fields.Single(f => f.Name == "Facing");
        var where  = layout.Fields.Single(f => f.Name == "Where");

        Assert.True(facing.Kind == NetFieldKind.QAngle && where.Kind == NetFieldKind.Vector, "a QAngle property resolves to the QAngle kind and a Vector to Vector");

        Assert.True(facing.Bytes == where.Bytes && facing.Bytes == 12, "and both are twelve bytes, because both are three floats");

        var state = new WithAngle();
        state.BindLocal();

        state.Facing = new QAngle(15f, -90f, 0.5f);
        state.Where  = new Vector(15f, -90f, 0.5f);

        byte[] blob = state.Snapshot();

        Assert.True(blob.AsSpan(facing.Offset, 12).SequenceEqual(blob.AsSpan(where.Offset, 12)), "the same three numbers written as a facing and as a position are the same twelve bytes");

        Assert.True(state.Facing == new QAngle(15f, -90f, 0.5f), "and an angle reads back in declaration order: pitch, yaw, roll");
    }

    //-------------------------------------------------------------------------
    // AND IT IS STILL A DIFFERENT KIND, WHICH THE HASH COVERS.
    //
    // This is the deliberate part. The bits are identical, so on the narrow
    // reading of "the hash covers what changes decoding" a Vector/QAngle swap
    // should not move it. It does, because the two sides then disagree about the
    // VALUE rather than about the bytes -- a client that thinks a position is a
    // facing hands it to Angles.Vectors and turns a trigger the wrong way, with
    // nothing in any log.
    //-------------------------------------------------------------------------
    [Fact]
    public void SwappingAVectorForAnAngleMovesTheSchemaHash()
    {
        var type = NetSchema.FromStandalone(NoTiers, WithAngle.NetLayout, published: true);

        var asVectors = new NetSchemaType(
            type.ClassName, type.TypeName, type.Shell,
            type.Fields.Select(f => f.Kind == NetFieldKind.QAngle
                                        ? f with { Kind = NetFieldKind.Vector } : f).ToArray(),
            type.LayoutBytes, type.PropCount, published: true);

        Assert.True(asVectors.SchemaHash != type.SchemaHash, "a build calling the same twelve bytes a Vector hashes differently");

        // The wire codes are what did it, and they are APPENDED. Renumbering an
        // existing one would rehash every class in the tree, so these two took
        // the next free numbers rather than the tidy ones.
        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.Color32) == 6
              && StandaloneNetStates.TypeCodeOf(NetFieldKind.QAngle) == 7, "the two new kinds took wire codes 6 and 7, after the five that existed");

        Assert.True(StandaloneNetStates.TypeCodeOf(NetFieldKind.Vector) == 2
              && StandaloneNetStates.TypeCodeOf(NetFieldKind.UInt) == 5, "and the codes that were already published did not move");
    }

    //-------------------------------------------------------------------------
    // The inspector tells the two apart, which is the other half of why QAngle
    // is worth having. Three floats in a hex dump are three floats.
    //-------------------------------------------------------------------------
    [Fact]
    public void TheInspectorSpellsEachCompositeAsWhatItIs()
    {
        var angles = new WithAngle();
        angles.BindLocal();
        angles.Facing = new QAngle(15f, -90f, 0.5f);
        angles.Where  = new Vector(15f, -90f, 0.5f);

        var angleType = NetSchema.FromStandalone(NoTiers, WithAngle.NetLayout, published: true);
        var read = NetSchema.Decode(angleType, angles.Snapshot()).ToDictionary(d => d.Field.Name);

        Assert.True(read["Where"].Value == "(15, -90, 0.5)", "a vector still decodes as x, y, z");

        Assert.True(read["Facing"].Value == "(15p, -90y, 0.5r)", "and the same bytes as a facing decode as pitch, yaw, roll");

        Assert.True(read["Facing"].Value != read["Where"].Value, "so the one thing the bytes cannot say is the thing the view does");

        Assert.True(read["Facing"].Field.CsType == "QAngle" && read["Where"].Field.CsType == "Vector", "and the declared type column separates them too");

        var colour = new WithColour();
        colour.BindLocal();
        colour.Tint = new Color32(0x12, 0x34, 0x56, 0x78);

        var colourType = NetSchema.FromStandalone(NoTiers, WithColour.NetLayout, published: true);
        var tint = NetSchema.Decode(colourType, colour.Snapshot()).Single(d => d.Field.Name == "Tint");

        Assert.True(tint.Value == "rgba(18, 52, 86, 120)", $"a colour decodes as its four channels, not as a dword ({tint.Value})");

        Assert.True(tint.Field.CsType == "Color32" && tint.Number is null, "and it is not offered as a number, because a packed word is not a measurement");
    }


    //-------------------------------------------------------------------------
    // light_dynamic, whose three channels became one colour.
    //
    // A DIFFERENT SHAPE FROM spotlight_end'S, and worth its own test for that
    // reason. There the four channels were the whole of the packed word and the
    // class had nothing else narrow in it. Here they shared a word with Style
    // and took it to 30 of 32 bits, so Flags — four bits — could not fit and was
    // padded into a word of its own with 28 spare. Merging the channels frees
    // that word: Style and Flags now share one.
    //
    // So the packed half SHRANK by a word and the whole half GREW by one, and
    // the class is the same 52 bytes it was. That is a coincidence of this
    // declaration rather than a rule, which is exactly why it is asserted
    // rather than assumed — 52 is the number that decides the tier.
    //-------------------------------------------------------------------------
    [Fact]
    public void ADynamicLightsColourCostsItNothing()
    {
        var layout = DynamicLightNetState.NetLayout;

        Assert.True(layout.Size == Block + 52, $"light_dynamic is still 52 bytes, not {layout.Size}");

        var colour = layout.Fields.Single(f => f.Name == "Colour");

        Assert.True(colour.Kind == NetFieldKind.Color32 && colour.Bytes == 4 && !colour.IsPacked, "its colour is one Color32 taking one whole word");

        // ONE PACKED WORD NOW, WHERE THERE WERE TWO. Counted from the layout
        // rather than named, so a packer that stopped combining them fails here.
        int packedWords = layout.Fields.Where(f => f.IsPacked)
                                .Select(f => f.Offset).Distinct().Count();

        Assert.True(packedWords == 1, $"Style and Flags share a single packed word, not {packedWords}");

        // AND FLAGS IS UNDISTURBED, which is the part a reshuffle could break
        // quietly. Four bits, still the enum, now at shift 6 because it sits
        // behind Style's six rather than alone at the start of its own word.
        var style = layout.Fields.Single(f => f.Name == "Style");
        var flags = layout.Fields.Single(f => f.Name == "Flags");

        Assert.True(flags.Bits == 4 && flags.EnumType == typeof(DynamicLightFlags), "Flags is still four bits of DynamicLightFlags");

        Assert.True(style.Offset == flags.Offset && style.BitShift == 0 && flags.BitShift == 6, "and it now follows Style in one word rather than owning the next");

        Assert.True(style.Bits + flags.Bits == 10, "ten bits between them, which is why they fit where three channels and a style did not");
    }

    //-------------------------------------------------------------------------
    // THE BITS THEMSELVES, because two fields newly sharing a word is exactly
    // where a shift bug reads a neighbour and reports a plausible number.
    //
    // Flags never shared a word with anything before this change. It does now,
    // so "writing one leaves the other alone" is a NEW claim about this class
    // and not an inherited one.
    //-------------------------------------------------------------------------
    [Fact]
    public void ADynamicLightsStyleAndFlagsDoNotReadEachOther()
    {
        var state = new DynamicLightNetState();
        state.BindLocal();

        // All ones in both, so a mask one bit too wide picks up a SET bit rather
        // than a zero and the test can see it.
        state.Style = 63;
        state.Flags = (DynamicLightFlags)0xF;

        Assert.True(state.Style == 63 && state.Flags == (DynamicLightFlags)0xF, "both fields hold their widest value at once");

        state.Style = 0;
        Assert.True(state.Flags == (DynamicLightFlags)0xF, "clearing the style leaves the flags alone");

        state.Style = 63;
        state.Flags = 0;
        Assert.True(state.Style == 63, "and clearing the flags leaves the style alone");

        // The colour is a whole word away and must not be touched by either.
        state.Colour = new Color32(1, 2, 3);
        state.Style  = 21;
        state.Flags  = (DynamicLightFlags)0x5;

        Assert.True(state.Colour == new Color32(1, 2, 3, 255), "and writing the packed word does not reach the colour's");
    }

    //-------------------------------------------------------------------------
    // AND THE COLOUR IS THE SAME THREE BYTES IT WAS.
    //
    // The byte comparison, which is the shape that caught the field reordering
    // last time rather than a reading of the code doing it. light_dynamic's only
    // writer parses a packed uint out of the `_light` key — R in the low byte —
    // so the claim is that those three bytes land in the blob exactly as three
    // hand-assigned channels would have.
    //-------------------------------------------------------------------------
    [Fact]
    public void ADynamicLightsColourIsTheSameBytesTheChannelsWere()
    {
        var state = new DynamicLightNetState();
        state.BindLocal();

        state.Colour = new Color32(10, 20, 30);

        int at = DynamicLightNetState.NetLayout.Fields.Single(f => f.Name == "Colour").Offset;

        uint word = BitConverter.ToUInt32(state.Snapshot(), at);

        Assert.True((word & 0xFF) == 10 && ((word >> 8) & 0xFF) == 20 && ((word >> 16) & 0xFF) == 30, "R is the low byte, then G, then B — the order ParseColour packs in");

        Assert.True((word >> 24) == 255, "and the top byte is opaque rather than vrad's brightness, which this entity does not send");
    }

    private static bool Throws(Action write)
    {
        try   { write(); return false; }
        catch (FieldRangeException) { return true; }
    }
}
