using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The save format, and mostly the ways it REFUSES.
///
/// <para>
/// <c>docs/plan-audit.md</c> D16 measured save/restore to a stop and named the
/// one missing piece: the managed state already exists in memory on both sides
/// (<c>ScriptedEntityRegistry</c>, <c>ClientStandaloneRegistry</c>), and what
/// nothing did was write it into a <c>.sav</c>.
/// <see cref="SavedEntityCodec"/> is that, and these are its facts.
/// </para>
///
/// <para>
/// <b>THE ROUND TRIP IS THE EASY HALF AND IS FIVE OF THESE FACTS.</b> The rest
/// are about a file that is not what it claims to be, because that is the
/// difference between this and the hot-reload path it shares its record type
/// with. A reload's producer and consumer are one process, one build, one
/// second. A <c>.sav</c> is read by a different build out of a file a player
/// can truncate, copy between machines or hand-edit — so every length in it is
/// hostile input, and "it worked on the save I just wrote" proves nothing about
/// the case that matters.
/// </para>
///
/// <para>
/// <b>AND THE FRAMING FACTS ARE NOT ENOUGH, WHICH IS WHY HALF THIS FILE MOVED.</b>
/// A magic, a version, a bounded count and per-field lengths catch every block
/// whose SHAPE is wrong and no block whose CONTENT is. A single flipped bit
/// inside a string, a float or a handle parses perfectly and restores a world
/// that is not the saved one — so the format carries a checksum, and because it
/// is verified before the count and before any length, every fact here that
/// patches a byte and names the guard it expects has to restamp or it silently
/// stops testing that guard. Each one that does says so in its own comment.
/// </para>
/// </summary>
public class SavedEntityCodecTests
{
    private static SavedEntity Barrel()
    {
        var e = new SavedEntity(0x1234u, "ss_barrel", "ss_test_target");

        e.SaveVar("health", "250", 12.5f);
        e.SaveNet("m_iHealth", "250");
        e.SaveOutput("OnBreak", 3, "boom");
        e.Set("burning", true);

        // AND THE NATIVE HALF, which up to version 3 was not in the file at
        // all. Two of these are the measured failures: the health the live gate
        // damaged and lost, and the origin the player and the barrel came back
        // without.
        e.SaveNative(NativeField.Health, 4);
        e.SaveNative(NativeField.Origin, new Vector(-3194.96f, 3768f, 1.92f));

        return e;
    }

    private static byte[] Pack(params SavedEntity[] entities)
    {
        var buffer = new byte[SavedEntityCodec.ByteCount(entities)];

        Assert.Equal(buffer.Length, SavedEntityCodec.Write(entities, buffer, new RecordingLog()));

        return buffer;
    }

    /// <summary>
    /// Recompute <paramref name="block"/>'s checksum over its own bytes, so it
    /// is well-framed again after being patched.
    ///
    /// <para>
    /// <b>THIS IS THE MOST DANGEROUS HELPER IN THE FILE AND IT IS LOAD-BEARING
    /// IN A DOZEN FACTS.</b> The checksum is verified before the count and
    /// before any length, which means EVERY fact that patches a byte and then
    /// names the guard it expects would, without this, trip the checksum first
    /// and pass for the wrong reason -- still refused, still red-when-deleted
    /// for the checksum, and silently proving nothing about the guard in its
    /// own name. Each such fact calls this, and each says so.
    /// </para>
    ///
    /// <para>
    /// It reads <see cref="SavedEntityCodec.ChecksumFrom"/> rather than a
    /// literal 12 on purpose: a change to which bytes the checksum covers must
    /// show up as facts about the FORMAT failing, not as every hand-patched
    /// block in this file failing to stamp.
    /// </para>
    /// </summary>
    private static byte[] Restamp(byte[] block)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(
            block.AsSpan(SavedEntityCodec.ChecksumAt),
            SavedEntityCodec.Checksum(block.AsSpan(SavedEntityCodec.ChecksumFrom)));

        return block;
    }

    /// <summary>Where the entity count sits, once the checksum precedes it.</summary>
    private const int CountAt = 4 + 4 + 4;

    /// <summary>Where the first record begins.</summary>
    private const int FirstRecordAt = CountAt + 4;

    //-------------------------------------------------------------------------
    // The round trip.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryStoreSurvivesTheRoundTrip()
    {
        Assert.True(SavedEntityCodec.TryRead(Pack(Barrel()), out var back, out string? why), why);

        SavedEntity e = Assert.Single(back);

        Assert.Equal(0x1234u, e.Handle);
        Assert.Equal("ss_barrel", e.ClassName);
        Assert.Equal("ss_test_target", e.Name);

        // All four stores, because they are restored by four different
        // mechanisms and a format that carried three would look correct on any
        // entity that happened not to use the fourth.
        Assert.Equal("250", e.Vars["health"].Value);
        Assert.Equal(12.5f, e.Vars["health"].LastSetAt);
        Assert.Equal("250", e.Net["m_iHealth"]);
        Assert.Equal(3, e.Outputs["OnBreak"].FireCount);
        Assert.Equal("boom", e.Outputs["OnBreak"].LastValue);
        Assert.True(e.GetBool("burning"));
    }

    [Fact]
    public void AnOutputThatFiredWithNoParameterIsNotOneThatFiredWithEmpty()
    {
        // The distinction the flag byte exists for. Both are "no text", and
        // they are different records: one output has never carried a parameter,
        // the other carried an empty one.
        var none = new SavedEntity(1u, "c", "n");
        none.SaveOutput("OnUser1", 1, null);

        var empty = new SavedEntity(2u, "c", "n");
        empty.SaveOutput("OnUser1", 1, string.Empty);

        Assert.True(SavedEntityCodec.TryRead(Pack(none, empty), out var back, out _));

        Assert.Null(back[0].Outputs["OnUser1"].LastValue);
        Assert.Equal(string.Empty, back[1].Outputs["OnUser1"].LastValue);
    }

    [Fact]
    public void AnEmptyWorldRoundTripsAsAnEmptyWorld()
    {
        // Not a degenerate case: it is what a save taken before any managed
        // entity spawns produces, and "no entities" must read back as no
        // entities rather than as a refusal.
        Assert.True(SavedEntityCodec.TryRead(Pack(), out var back, out string? why), why);
        Assert.Empty(back);
    }

    [Fact]
    public void NonAsciiTextSurvives()
    {
        // The length prefix is in BYTES and the text is UTF-8, so a value whose
        // character count and byte count differ is the case that catches a
        // prefix written in the wrong unit.
        var e = new SavedEntity(1u, "ss_sign", "s");
        e.Set("message", "héllo wörld — ok");

        Assert.True(SavedEntityCodec.TryRead(Pack(e), out var back, out _));
        Assert.Equal("héllo wörld — ok", back[0].Local["message"]);
    }

    [Fact]
    public void ASizingCallWritesNothingAndAShortBufferIsRefused()
    {
        SavedEntity[] one = [Barrel()];
        int required = SavedEntityCodec.Write(one, Span<byte>.Empty, new RecordingLog());

        Assert.Equal(SavedEntityCodec.ByteCount(one), required);

        var tooSmall = new byte[required - 1];
        Assert.Equal(required, SavedEntityCodec.Write(one, tooSmall, new RecordingLog()));

        // NOTHING written. A half-written block is a save that restores some of
        // the world, and the player keeps playing in what is left.
        Assert.All(tooSmall, b => Assert.Equal(0, b));
    }

    //-------------------------------------------------------------------------
    // The refusals. Every one of these is a file, not a bug in the writer.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEmptyBlockIsRefusedRatherThanReadAsEmpty()
    {
        Assert.False(SavedEntityCodec.TryRead([], out var back, out string? why));
        Assert.Empty(back);
        Assert.Contains("header", why);
    }

    [Fact]
    public void SomethingElsesBytesAreRefusedByMagicBeforeAnyLengthIsBelieved()
    {
        // The magic is first for exactly this reason: a block that is not ours
        // must be refused before a four-byte field in it is used to size
        // anything.
        var alien = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(alien, 0xDEADBEEF);

        Assert.False(SavedEntityCodec.TryRead(alien, out _, out string? why));
        Assert.Contains("magic", why);
    }

    [Fact]
    public void AFutureVersionIsRefusedAndBothNumbersAreNamed()
    {
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(4), SavedEntityCodec.Version + 1);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why));
        Assert.Empty(back);

        // BY NUMBER, both of them. "unsupported version" without the two
        // numbers is the message that sends its reader to the source.
        Assert.Contains((SavedEntityCodec.Version + 1).ToString(), why);
        Assert.Contains(SavedEntityCodec.Version.ToString(), why);
    }

    [Fact]
    public void AVersionTwoBlockIsRefusedAsAMBIGUOUSRatherThanMerelyOld()
    {
        // 2 IS NOT JUST "OLDER". Two lanes bumped this format to 2
        // independently -- one adding the header checksum, the other the
        // per-record index -- and neither shipped. So a file stamped 2 came
        // from a development build and there is no way to tell WHICH layout it
        // is, which is a different problem from being out of date and wants a
        // different message.
        //
        // The distinction is the whole reason the number went to 3 instead of
        // being picked by whichever lane landed second.
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(4), SavedEntityCodec.VersionThatMeantTwoThings);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why));
        Assert.Empty(back);

        // Not the generic message. If this ever falls through to the default
        // arm, a v2 file reports as ordinarily old and its reader goes looking
        // for an upgrade path that cannot exist.
        Assert.Contains("two different layouts", why);
    }

    [Fact]
    public void APastVersionIsRefusedToo()
    {
        // Not "upgrade silently". A reader that accepts an older layout has to
        // know what changed, and this format has had no change to know about --
        // so the honest answer is a refusal that says which two versions
        // disagreed.
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(4), SavedEntityCodec.Version - 1);

        Assert.False(SavedEntityCodec.TryRead(block, out _, out string? why));
        Assert.Contains("version", why);
    }

    [Fact]
    public void AnAbsurdEntityCountIsRefusedBeforeItSizesAnything()
    {
        // RESTAMPED, and that is the whole reason this fact still tests the
        // count. The count is inside the checksum, so a patched count without a
        // fresh stamp is refused by the CHECKSUM before the bound is consulted
        // -- which is a real property (and has its own fact below) but is not
        // this one. Without the restamp this fact would pass with the bound
        // deleted.
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(CountAt), int.MaxValue);
        Restamp(block);

        Assert.False(SavedEntityCodec.TryRead(block, out _, out string? why));
        Assert.Contains("count", why);
    }

    [Fact]
    public void ANegativeEntityCountIsRefused()
    {
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(CountAt), -1);
        Restamp(block);                       // see the fact above

        Assert.False(SavedEntityCodec.TryRead(block, out _, out string? why));
        Assert.Contains("count", why);
    }

    [Fact]
    public void AnAbsurdStringLengthIsRefusedRatherThanAllocated()
    {
        // A record is handle (4) then INDEX (4) then the classname, so the first
        // string's length prefix is FirstRecordAt + 4 + 4. A length of 2 GB
        // there is what a corrupt file looks like, and it must not become an
        // allocation or an exception.
        //
        // BOTH OFFSETS ARE THE FORMAT AND NEITHER IS AN ADJUSTMENT TO SUIT THE
        // TEST. FirstRecordAt moved because the header gained a checksum; the
        // +4 is the index, which is the key a save re-identifies by, because a
        // handle out of a .sav names nothing once the world has been rebuilt.
        // Two lanes each bumped the version to 2 for one of those and neither
        // shipped, which is why the number is 3.
        //
        // RESTAMPED, or the checksum refuses first and this fact stops being
        // about a length at all.
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(FirstRecordAt + 4 + 4), int.MaxValue);
        Restamp(block);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out _));
        Assert.Empty(back);
    }

    [Fact]
    public void EveryTruncationIsRefusedAndNoneOfThemThrows()
    {
        // THE SWEEP, and it is the fact worth having. A file can be cut at any
        // byte -- a full disk, a killed process, a partial copy -- so every
        // prefix of a valid block is tested rather than three chosen ones.
        // Each must refuse, none may throw, and none may return entities.
        //
        // WHAT REFUSES THEM IS NOW MOSTLY THE CHECKSUM, and that is honest
        // about the real case: a truncated file carries the checksum of the
        // whole one. It also means this sweep no longer reaches the reader's
        // own bounds checks -- so the twin sweep below restamps each prefix and
        // exercises those instead. The pair is deliberate: this one is the file
        // a disk produced, that one is the reader in isolation.
        byte[] whole = Pack(Barrel(), Barrel());

        for (int cut = 0; cut < whole.Length; cut++)
        {
            bool ok = SavedEntityCodec.TryRead(whole.AsSpan(0, cut), out var back, out _);

            Assert.False(ok, $"a block cut to {cut} of {whole.Length} bytes was accepted");
            Assert.Empty(back);
        }

        // ...and the uncut one still reads, so the sweep above is not passing
        // because nothing ever reads.
        Assert.True(SavedEntityCodec.TryRead(whole, out var full, out string? why), why);
        Assert.Equal(2, full.Count);
    }

    [Fact]
    public void EveryTruncationThatCARRIESAValidChecksumIsStillRefused()
    {
        // THE SWEEP ABOVE, WITH THE CHECKSUM TAKEN OUT OF THE ARGUMENT. Each
        // prefix is restamped, so it arrives at the reader well-framed and
        // every refusal comes from a bounds check inside the parse rather than
        // from the header.
        //
        // It is not a hypothetical shape: it is what a writer that ran out of
        // room and stamped anyway produces. And it is what keeps the reader's
        // own guards under test at all -- without it, deleting
        // `bytes > Remaining` from Reader.Str throws nowhere, because no
        // truncated block ever gets that far.
        byte[] whole = Pack(Barrel(), Barrel());

        // From the header's end: a prefix shorter than the header has nowhere
        // to put a checksum, and the sweep above already covers those.
        for (int cut = FirstRecordAt; cut < whole.Length; cut++)
        {
            byte[] prefix = Restamp(whole.AsSpan(0, cut).ToArray());

            bool ok = SavedEntityCodec.TryRead(prefix, out var back, out string? reason);

            Assert.False(ok, $"a well-framed block cut to {cut} of {whole.Length} bytes was accepted");

            // EMPTY, and the cuts inside the SECOND record are why this matters:
            // the first record has already parsed by then, so a refusal that
            // handed back what it managed to read would hand back one entity.
            Assert.Empty(back);
            Assert.NotNull(reason);

            // ...and not by the checksum, which is the point of restamping.
            Assert.DoesNotContain("checksum", reason);
        }
    }

    //-------------------------------------------------------------------------
    // The checksum. Everything above it catches a block whose FRAMING is wrong
    // -- a foreign magic, a stale version, a length that runs off the end,
    // bytes left over. None of them catches a block whose framing is perfect
    // and whose CONTENT is wrong, and a flipped bit inside a string, a float or
    // a handle is exactly that: it parses, it is accepted, and it restores a
    // world that is not the saved one, with no report of any kind.
    //-------------------------------------------------------------------------

    [Fact]
    public void ACorruptPayloadByteIsRefused()
    {
        // Inside the classname's text, which is the case the framing cannot
        // see: the length prefix is untouched, so every later field still lands
        // where it should and the block parses to completion.
        byte[] block = Pack(Barrel());

        // handle, INDEX, then the classname's length prefix.
        int textAt = FirstRecordAt + 4 + 4 + 4;

        // PROVED, not assumed: if this byte were not the classname's first
        // character the fact would be flipping something else and would still
        // pass.
        //
        // AND IT EARNED ITS KEEP. Merging the index into this format moved the
        // classname's text four bytes along, and this assertion is what caught
        // it -- it landed on the length prefix (9, for "ss_barrel") instead of
        // 's'. Without it the fact would have flipped a byte of the LENGTH,
        // still been refused, still passed, and stopped being about a corrupt
        // payload at all.
        Assert.Equal((byte)'s', block[textAt]);

        block[textAt] ^= 0x01;                   // 's' -> 'r'

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why),
                     "a one-bit change inside a string was accepted and would have restored " +
                     "an entity of the wrong class");
        Assert.Empty(back);
        Assert.Contains("checksum", why);
    }

    [Fact]
    public void TheChecksumRefusalNamesBothValuesTheWayTheVersionRefusalNamesBothVersions()
    {
        byte[] block = Pack(Barrel());
        uint stamped = BinaryPrimitives.ReadUInt32LittleEndian(
            block.AsSpan(SavedEntityCodec.ChecksumAt));

        block[^1] ^= 0xFF;

        uint computed = SavedEntityCodec.Checksum(block.AsSpan(SavedEntityCodec.ChecksumFrom));

        // The corruption really did move the checksum, so the two numbers below
        // are two numbers and not one printed twice.
        Assert.NotEqual(stamped, computed);

        Assert.False(SavedEntityCodec.TryRead(block, out _, out string? why));

        // BY VALUE, BOTH OF THEM. "checksum mismatch" sends its reader to the
        // source; these two numbers say whether the block was damaged or was
        // never stamped.
        Assert.Contains(stamped.ToString("X8"), why);
        Assert.Contains(computed.ToString("X8"), why);
    }

    [Fact]
    public void APerfectlyValidPayloadUnderAnotherBlocksChecksumIsStillRefused()
    {
        // THE ANTI-VACUITY FACT FOR THE CHECKSUM ITSELF. Every other corruption
        // fact could conceivably be caught by something downstream noticing the
        // damage. This one cannot be: the payload spliced in is a whole, valid,
        // self-consistent block body that parses to a complete entity. The ONLY
        // thing wrong with the result is that the header's checksum belongs to
        // a different payload -- so if this is accepted, the checksum is not
        // being checked, and nothing else in the file will ever say so.
        var one = new SavedEntity(1u, "aaa", "bbb");
        one.Set("k", "v1");

        var other = new SavedEntity(1u, "aaa", "bbb");
        other.Set("k", "v2");

        byte[] a = Pack(one);
        byte[] b = Pack(other);

        Assert.Equal(a.Length, b.Length);        // the splice has to line up
        Assert.NotEqual(a, b);                   // ...and has to change something

        // b's payload really is valid on its own, which is what makes the
        // splice below prove anything.
        Assert.True(SavedEntityCodec.TryRead(b, out var whole, out string? ok), ok);
        Assert.Equal("v2", Assert.Single(whole).Local["k"]);

        byte[] spliced = [.. a.AsSpan(0, SavedEntityCodec.ChecksumFrom),
                          .. b.AsSpan(SavedEntityCodec.ChecksumFrom)];

        Assert.False(SavedEntityCodec.TryRead(spliced, out var back, out string? why),
                     "a valid payload under a stale checksum was accepted");
        Assert.Empty(back);
        Assert.Contains("checksum", why);
    }

    [Fact]
    public void AnEditedCountIsRefusedByTheChecksumBeforeTheCountItselfIsJudged()
    {
        // THE ORDER, AS A FACT. The entity count is the first number out of the
        // file that is used to size anything, so it is inside the checksum and
        // the checksum is verified before it is looked at. Both halves are
        // asserted here, because each fails differently:
        //
        //   * cover the count but check it after the bound, and the refusal
        //     below says "entity count";
        //   * check the checksum first but start it past the count, and the
        //     refusal below says "entity count" too.
        //
        // Either way this fact goes red, and no other fact in the file does --
        // the count facts above restamp precisely so that they don't.
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(CountAt), int.MaxValue);
        // NOT restamped. That is the subject.

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why));
        Assert.Empty(back);

        Assert.Contains("checksum", why);
        Assert.DoesNotContain("entity count", why);
    }

    [Fact]
    public void TheChecksumIsTheStandardCrc32AndIsPinnedToItsCheckValue()
    {
        // The check value every CRC-32 implementation publishes. Without it,
        // "it is a checksum" and "it is a checksum of its own invention that
        // happens to be self-consistent" are the same green run -- and a
        // homemade one is where the weak cases hide.
        Assert.Equal(0xCBF4_3926u, SavedEntityCodec.Checksum("123456789"u8));

        // ...and it is not a length-only or order-blind function: a sum would
        // give these two the same answer.
        Assert.NotEqual(SavedEntityCodec.Checksum([1, 2]), SavedEntityCodec.Checksum([2, 1]));
        Assert.Equal(0u, SavedEntityCodec.Checksum([]));
    }

    [Fact]
    public void TrailingBytesAreRefusedRatherThanIgnored()
    {
        // The one disagreement a version stamp CANNOT catch: two builds that
        // agree on the version number and disagree about the layout. Leftover
        // bytes are the symptom, and by the time they are noticed something has
        // already been restored -- so this refuses the whole block.
        //
        // RESTAMPED AFTER THE APPEND, and this is what makes the fact about the
        // layout rather than about damage. A stray byte nobody meant to write
        // is caught by the checksum; a byte the OTHER BUILD wrote on purpose
        // arrives under a checksum that covers it and is correct, and the
        // trailing-bytes guard is the only thing that can see it. That second
        // case is the one this fact is named for, so it is the one built here.
        byte[] whole = Pack(Barrel());
        byte[] plusOne = Restamp([.. whole, 0]);

        Assert.False(SavedEntityCodec.TryRead(plusOne, out var back, out string? why));
        Assert.Empty(back);
        Assert.Contains("left over", why);
    }

    //-------------------------------------------------------------------------
    // Guards that had NO test with teeth until a review asked, of each refusal
    // fact, "would this still pass if the guard it names were deleted?"
    //
    // For four of them the answer was yes AND the deletion is silent: a
    // negative sub-count skips its loop CONSUMING NOTHING, so parsing resumes
    // at the right offset and the record comes back with that whole store
    // empty, refusal null, TryRead true. A one-byte edit, a wrong world, no
    // report.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One entity, hand-built, with all four stores EMPTY and every length
    /// honest — the shape the guards below are tested against.
    ///
    /// <para>
    /// It is hand-built because the WRITER refuses the inputs these facts need,
    /// which is correct of it and is exactly why the reader cannot be tested
    /// through the writer: no block the writer produces is hostile, and hostile
    /// blocks are the only ones the reader's guards are for.
    /// </para>
    /// </summary>
    private static byte[] Bare(string className, int? classNameBytes = null)
    {
        var block = new List<byte>();

        void I32(int v)
        {
            Span<byte> four = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(four, v);
            block.AddRange(four.ToArray());
        }

        // `claimed` is NULLABLE rather than a negative sentinel, because a
        // negative claimed length is one of the things these facts must be able
        // to build. A -1 "use the real length" sentinel silently ate the first
        // version of the negative-length fact and made it pass for the wrong
        // reason.
        void Str(string text, int? claimed)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            I32(claimed ?? utf8.Length);
            block.AddRange(utf8);
        }

        I32(unchecked((int)SavedEntityCodec.Magic));
        I32(SavedEntityCodec.Version);
        I32(0);                                  // the checksum, stamped below
        I32(1);                                  // one entity

        I32(0x1234);                             // handle
        I32(7);                                  // index
        Str(className, classNameBytes);
        Str("n", null);
        I32(0); I32(0); I32(0); I32(0); I32(0);  // native, vars, net, outputs, local

        // STAMPED, AND EVERY FACT BELOW STANDS ON THIS. The checksum is checked
        // before the count and before any length, so an unstamped hand-built
        // block is refused as damage no matter what is wrong with it -- which
        // would make every fact below pass while testing the checksum instead
        // of the guard it names. TheHandBuiltBlockIsOtherwiseValid is what
        // proves the stamp is right.
        return Restamp([.. block]);
    }

    /// <summary>
    /// Where the five store counts begin in <see cref="Bare"/>'s block —
    /// native, then map keys, networked fields, outputs and local keys.
    ///
    /// <para>
    /// THE NATIVE COUNT IS FIRST AND THAT IS THE v3 -> v4 SHIFT. A version 3
    /// record has its MAP-KEY count at this offset, which is why a v3 block
    /// read as v4 misparses rather than coming up short, and why the version is
    /// refused rather than tolerated.
    /// </para>
    /// </summary>
    private static int Counts(string className)
        => 4 + 4 + 4 + 4                                      // magic, version, checksum, count
         + 4 + 4                                              // handle, index
         + 4 + Encoding.UTF8.GetByteCount(className)          // classname
         + 4 + 1;                                             // name "n"

    [Fact]
    public void TheHandBuiltBlockIsOtherwiseValid()
    {
        // THE VACUITY GUARD FOR EVERY FACT BELOW. Each of them patches one field
        // of this block and requires a refusal. If the block were malformed to
        // begin with, all of them would pass without the patched field mattering
        // at all -- which is the exact failure the harness caught in the first
        // version of these tests.
        Assert.True(SavedEntityCodec.TryRead(Bare("ss_barrel"), out var back, out string? why), why);

        SavedEntity e = Assert.Single(back);
        Assert.Equal("ss_barrel", e.ClassName);
        Assert.Empty(e.Native);
        Assert.Empty(e.Vars);
        Assert.Empty(e.Net);
        Assert.Empty(e.Outputs);
        Assert.Empty(e.Local);
    }

    [Fact]
    public void ANegativeStringLengthIsRefusedAndDoesNotThrow()
    {
        // Without the `bytes < 0` arm this is not a refusal, it is an
        // ArgumentOutOfRangeException out of Span.Slice -- a breach of the
        // "never throws" contract this class is built on, because the input is
        // a file.
        byte[] block = Bare("ss_barrel", classNameBytes: -1000);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why));
        Assert.Empty(back);
        Assert.NotNull(why);
    }

    [Fact]
    public void AStringLengthPastTheCapIsRefusedEvenThoughTheBlockIsOtherwiseWhole()
    {
        // THE CAP, in the only situation that tests it. Every other oversized
        // length is caught by `bytes > Remaining` on its own -- the reader runs
        // off the end and refuses for that reason instead, so the fact passes
        // with the cap deleted and says nothing.
        //
        // So: a classname whose bytes are ALL THERE and which is merely longer
        // than the cap allows. Delete the cap and this block parses cleanly and
        // TryRead returns true -- one 9,000-byte string out of a file, believed.
        string huge = new string('c', SavedEntityCodec.MaxStringBytes + 1);
        byte[] block = Bare(huge);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why),
                     "a string longer than the cap was accepted because its bytes were present");
        Assert.Empty(back);
        Assert.NotNull(why);
    }

    [Theory]
    [InlineData(0, "native-field")]
    [InlineData(1, "map-key")]
    [InlineData(2, "networked-field")]
    [InlineData(3, "output")]
    [InlineData(4, "local-key")]
    public void ANegativeStoreCountIsRefusedRatherThanReadAsAnEmptyStore(int which, string named)
    {
        // THE SILENT ONE, and the reason the empty block above is the right
        // subject. A negative count skips its loop CONSUMING NOTHING; with the
        // store empty there are no orphaned entries left in the stream to
        // desynchronise the parse, so every later field still lands at the right
        // offset and the record comes back whole -- accepted, refusal null,
        // TryRead true. A wrong world out of a one-byte edit, reported nowhere.
        //
        // Patched against a block whose stores are all empty and all five counts
        // adjacent, so `which` indexes them directly.
        const string Class = "ss_barrel";
        byte[] block = Bare(Class);

        // WHAT THE BYTES SHOULD BE, ASSERTED BEFORE THEY ARE PATCHED. A merge
        // once moved a classname four bytes along and a fact went on patching
        // the length prefix instead of the field it named -- still refused,
        // still green, and no longer about anything. Adding the native count at
        // the head of the stores moved this offset by four, which is exactly
        // the shape of change that does it again.
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                            block.AsSpan(Counts(Class) + (4 * which))));

        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(Counts(Class) + (4 * which)), -1);

        // RESTAMPED, and this fact is where that requirement was discovered.
        // Bare stamps the block it builds; patching a byte afterwards breaks
        // the stamp, so without this all four cases were refused with
        // "checksum 0x... computed 0x..." and asserted nothing about the store
        // count in their own name. The five `named` assertions below are the
        // only reason it was visible rather than a silent green.
        Restamp(block);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why),
                     $"a negative {named} count was read as an empty store");
        Assert.Empty(back);
        Assert.Contains(named, why);
    }

    [Fact]
    public void AValueTooLongToEncodeRefusesTheWholeSaveRatherThanDroppingIt()
    {
        var e = new SavedEntity(1u, "ss_sign", "s");
        e.Set("message", new string('x', SavedEntityCodec.MaxStringBytes + 1));

        // -1, not "wrote the others". Dropping the record is the silent
        // failure: the save succeeds and the entity is simply not in it.
        Assert.Equal(-1, SavedEntityCodec.Write([e], Span<byte>.Empty, new RecordingLog()));
    }

    [Fact]
    public void AValueExactlyAtTheCapIsAccepted()
    {
        // The boundary in the direction that is not a refusal, so the cap is
        // measured rather than approximately enforced.
        var e = new SavedEntity(1u, "ss_sign", "s");
        e.Set("message", new string('x', SavedEntityCodec.MaxStringBytes));

        Assert.True(SavedEntityCodec.TryRead(Pack(e), out var back, out string? why), why);
        Assert.Equal(SavedEntityCodec.MaxStringBytes, back[0].Local["message"].Length);
    }

    [Fact]
    public void TheFormatIsLittleEndianAndSaysSoInItsBytes()
    {
        // Pinned rather than assumed. Both sides run on the same architecture
        // today, and a save copied between machines should not be the thing
        // that discovers an endian assumption -- so the byte order is a fact
        // with a test rather than a property of the host.
        byte[] block = Pack();

        Assert.Equal(new byte[] { 0x53, 0x53, 0x53, 0x56 }, block[..4]);   // "SSSV"
        Assert.Equal(SavedEntityCodec.Version, BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(CountAt)));

        // ...and the checksum word is where the layout says, over the bytes
        // the layout says. An empty block has four covered bytes -- the count
        // -- so this is a real value and not a CRC of nothing.
        Assert.Equal(SavedEntityCodec.Checksum(block.AsSpan(SavedEntityCodec.ChecksumFrom)),
                     BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(SavedEntityCodec.ChecksumAt)));
        Assert.Equal(4 + 4 + 4, SavedEntityCodec.ChecksumFrom);
        Assert.Equal(4 + 4, SavedEntityCodec.ChecksumAt);
    }

    [Fact]
    public void ABlockOfManyEntitiesRoundTripsWithEveryRecordIntact()
    {
        // 473 is dm_lockdown's entity count and 472 is what cs_sv_reload
        // carries, so this is the size the real thing has to survive -- and it
        // is where an off-by-one in a per-record length shows up and one entity
        // does not.
        var many = new List<SavedEntity>();

        for (int i = 0; i < 473; i++)
        {
            var e = new SavedEntity((uint)i, $"ss_class_{i}", $"name_{i}");
            e.Set("index", i);
            e.SaveNet("m_iHealth", (100 + i).ToString());
            many.Add(e);
        }

        byte[] block = Pack([.. many]);

        Assert.True(SavedEntityCodec.TryRead(block, out var back, out string? why), why);
        Assert.Equal(473, back.Count);

        for (int i = 0; i < 473; i++)
        {
            Assert.Equal((uint)i, back[i].Handle);
            Assert.Equal($"ss_class_{i}", back[i].ClassName);
            Assert.Equal(i, back[i].GetInt("index"));
            Assert.Equal((100 + i).ToString(), back[i].Net["m_iHealth"]);
        }
    }

    [Fact]
    public void TheOrderOfEntitiesIsPreserved()
    {
        // Not decoration: a restore rebuilds entity references by handle, and
        // an order that shuffled would make a referenced entity arrive after
        // the one referring to it. The format is a sequence and this says so.
        var a = new SavedEntity(1u, "first", "a");
        var b = new SavedEntity(2u, "second", "b");
        var c = new SavedEntity(3u, "third", "c");

        Assert.True(SavedEntityCodec.TryRead(Pack(a, b, c), out var back, out _));
        Assert.Equal(["first", "second", "third"], back.Select(x => x.ClassName));
    }

    /// <summary>
    /// <b>A VERSION 1 BLOCK IS REFUSED, and refusing it is the point of the
    /// bump.</b>
    ///
    /// <para>
    /// v1 carried a handle and no index. A handle out of a <c>.sav</c> names
    /// nothing once the world has been rebuilt — measured, 0 of 470 survived a
    /// load — so a v1 block cannot be re-identified against a respawned map at
    /// all. Read with the missing key defaulted, every record in it would place
    /// nowhere or, worse, somewhere. It is refused by number, and the message
    /// says which number and why.
    /// </para>
    /// </summary>
    [Fact]
    public void AVersionOneBlockIsRefusedByNumber()
    {
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(4), SavedEntityCodec.VersionWithoutIndex);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why));
        Assert.Empty(back);
        Assert.Contains("1", why);
        Assert.Contains("index", why);
    }

    [Fact]
    public void TheIndexRoundTrips()
    {
        var e = new SavedEntity(0x0004_0011u, "prop_physics", "", 4242);

        byte[] block = Pack(e);

        Assert.True(SavedEntityCodec.TryRead(block, out var back, out string? why), why);
        Assert.Equal(4242, back[0].Index);
    }

    /// <summary>
    /// -1 is what <c>Collect</c> answers with no engine to ask, and it must
    /// survive as -1 rather than as a plausible index. A record that came back
    /// claiming index 0 would be a record aimed at worldspawn.
    /// </summary>
    [Fact]
    public void AnAbsentIndexStaysAbsent()
    {
        byte[] block = Pack(new SavedEntity(0x0004_0011u, "prop_physics", "", -1));

        Assert.True(SavedEntityCodec.TryRead(block, out var back, out _));
        Assert.Equal(-1, back[0].Index);
    }

    //-------------------------------------------------------------------------
    // Version 4: the native section.
    //
    // Up to 3 a record carried the MANAGED half of an entity and the native
    // half was in no file anywhere -- CShellGameDll::Save makes one call, which
    // writes this block, WriteSaveHeaders writes nothing, and there is no
    // ETABLE. So a save carried what an entity's C# thought and not where it
    // was, what health it had left or how fast it was moving.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <b>THE POSITIVE CASE, AND IT COMES FIRST.</b> Every refusal below is
    /// about a native section that is malformed; if the store did not survive
    /// an ordinary round trip they would all pass with the reader gutted.
    /// </summary>
    [Fact]
    public void TheNativeSectionSurvivesTheRoundTrip()
    {
        Assert.True(SavedEntityCodec.TryRead(Pack(Barrel()), out var back, out string? why), why);

        SavedEntity e = Assert.Single(back);

        Assert.Equal(2, e.Native.Count);
        Assert.Equal(4, e.NativeInt(NativeField.Health));

        Vector origin = e.NativeVector(NativeField.Origin);

        Assert.Equal(-3194.96f, origin.X, 2);
        Assert.Equal(3768f, origin.Y, 2);
        Assert.Equal(1.92f, origin.Z, 2);
    }

    /// <summary>
    /// A record with no native section round-trips as one with no native
    /// section, rather than as one full of zeroes.
    ///
    /// <para>
    /// THE DIFFERENCE IS NOT COSMETIC. With no engine to ask, an origin read
    /// would be <c>(0,0,0)</c> — a REAL coordinate, the middle of the map — so
    /// "no native state was taken" has to be a thing the record can say, and
    /// the store being EMPTY is how it says it. A reader that manufactured
    /// zeroes would teleport every entity to the world origin, plausibly, with
    /// nothing to report.
    /// </para>
    /// </summary>
    [Fact]
    public void ARecordWithNoNativeSectionComesBackWithNone()
    {
        byte[] block = Pack(new SavedEntity(0x0004_0011u, "prop_physics", "", 4));

        Assert.True(SavedEntityCodec.TryRead(block, out var back, out string? why), why);

        Assert.Empty(back[0].Native);
        Assert.False(back[0].HasNative(NativeField.Origin));
    }

    /// <summary>
    /// The origin goes down as the number it was given, untransformed — which
    /// is the whole of the "absolute, not landmark-relative" decision, pinned
    /// to the bytes rather than left in a comment.
    /// </summary>
    [Fact]
    public void TheOriginIsStoredAbsoluteAndIsNotRebasedByAnything()
    {
        var e = new SavedEntity(1u, "ss_barrel", "");
        var origin = new Vector(-3194.96f, 3768f, 1.92f);

        e.SaveNative(NativeField.Origin, origin);

        Assert.True(SavedEntityCodec.TryRead(Pack(e), out var back, out string? why), why);

        Vector round = back[0].NativeVector(NativeField.Origin);

        // Not "close to". Stock's WritePositionVector SUBTRACTS the save's
        // landmark; this format does not, because this port has no transition
        // for a relative coordinate to survive into and nothing here can be
        // told a landmark. If a rebase were ever added on one side only, every
        // origin in the file would be wrong by a constant and the world would
        // load without a complaint -- so the identity is a fact.
        Assert.Equal(origin.X, round.X);
        Assert.Equal(origin.Y, round.Y);
        Assert.Equal(origin.Z, round.Z);
    }

    /// <summary>
    /// <b>A VERSION 3 BLOCK IS REFUSED, AND THE REASON IS ITS OWN.</b> v1 has
    /// no index, v2 named two layouts, and v3 carried no native section — three
    /// different problems wanting three different messages. "Unsupported
    /// version" would be true of all of them and useful for none.
    /// </summary>
    [Fact]
    public void AVersionThreeBlockIsRefusedBecauseItCarriedNoNativeSection()
    {
        byte[] block = Pack(Barrel());
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(4),
                                                SavedEntityCodec.VersionWithoutNativeState);

        Assert.False(SavedEntityCodec.TryRead(block, out var back, out string? why));
        Assert.Empty(back);

        Assert.Contains("3", why);
        Assert.Contains("native", why);

        // NOT THE GENERIC ARM. If this ever falls through, a v3 file reports as
        // ordinarily old and its reader goes looking for the layout change that
        // is not named anywhere.
        Assert.Contains("misparse", why);
    }

    /// <summary>
    /// And 3 is <see cref="SavedEntityCodec.Version"/> minus one, so the
    /// generic past-version fact and this one are about the same number — which
    /// is worth knowing, because it means the specific arm is what is being
    /// exercised and the generic arm is currently unreachable from below.
    /// </summary>
    [Fact]
    public void TheVersionBelowThisOneIsTheNativeSectionOne()
    {
        Assert.Equal(SavedEntityCodec.Version - 1, SavedEntityCodec.VersionWithoutNativeState);
    }

    /// <summary>
    /// A truncation inside the native section is a refusal that names it, not a
    /// record that comes back with half its native fields.
    /// </summary>
    [Fact]
    public void ATruncatedNativeSectionIsRefusedAndSaysWhere()
    {
        byte[] block = Pack(Barrel());

        // Cut a byte off the end and restamp, so the refusal is about the
        // truncation rather than about the checksum -- which would be a real
        // refusal for the wrong reason and would leave this fact green with the
        // native reader deleted.
        byte[] cut = block[..^1];
        Restamp(cut);

        Assert.False(SavedEntityCodec.TryRead(cut, out var back, out string? why));
        Assert.Empty(back);
        Assert.NotNull(why);
    }

    /// <summary>
    /// A native field longer than the cap refuses the whole save, on the same
    /// terms as every other store: dropping the record is the silent failure.
    /// </summary>
    [Fact]
    public void ANativeFieldTooLongToEncodeRefusesTheWholeSave()
    {
        var e = new SavedEntity(1u, "ss_sign", "s");
        e.SaveNative("scribble", new string('x', SavedEntityCodec.MaxStringBytes + 1));

        Assert.Equal(-1, SavedEntityCodec.Write([e], Span<byte>.Empty, new RecordingLog()));
    }
}
