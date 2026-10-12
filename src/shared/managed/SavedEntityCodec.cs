using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="SavedEntity"/> records to bytes and back — the one piece a
/// managed save/restore was measured to be missing.
///
/// <para>
/// <c>docs/plan-audit.md</c> D16 measured the whole question and stopped here,
/// in as many words: <i>"the managed shape already exists in memory
/// (<c>cs_sv_reload</c>, 472/473)… What is missing is a block handler that
/// writes those records into the <c>.sav</c>"</i>. This is the half of that
/// which has no engine in it, and it is deliberately the first half: it is the
/// only part that can be driven to a conclusion by a test rather than by a
/// game.
/// </para>
///
/// <para>
/// <b>NEITHER SIDE NEEDS A NEW STATE MODEL, WHICH IS WHY THIS IS TRACTABLE.</b>
/// The server has <c>ScriptedEntityRegistry.SaveForReload</c> and the client has
/// <c>ClientStandaloneRegistry.SaveForReload</c>; both produce
/// <see cref="SavedEntity"/>, and both are already exercised on every
/// <c>cs_sv_reload</c> / <c>cs_cl_reload</c>. So this file serves both sides
/// with one implementation, and it lives in <c>Abi</c> because
/// <see cref="SavedEntity"/> does and because <c>SourceSharp.Bootstrap</c> —
/// which is where an ABI slot would call it — references <c>Abi</c> and nothing
/// else.
/// </para>
///
/// <para>
/// And the property that makes a <see cref="SavedEntity"/> serialisable was not
/// designed for saving at all. Its own header states the rule it was built to:
/// <i>"nothing in this class may be an object from the side being unloaded"</i>,
/// because a strong reference would pin a collectible load context forever. A
/// record made only of <see cref="string"/>, <see cref="float"/> and
/// <see cref="int"/> is what that rule produces — and it is exactly what a byte
/// format needs. The hot-reload constraint and the save format's requirement are
/// the same constraint arrived at from two directions.
/// </para>
///
/// <para>
/// <b>WHAT A SAVE NEEDS THAT A RELOAD DOES NOT, and it is the whole reason this
/// is a separate lane rather than a rename of the reload path.</b> A reload's
/// producer and consumer are the same process, the same build and the same
/// second. A <c>.sav</c> is read by a DIFFERENT BUILD, possibly months later,
/// out of a file the player can copy, truncate, hand-edit or corrupt. Two
/// consequences, and both are structural rather than defensive:
/// </para>
///
/// <list type="number">
/// <item><description>
/// <b>A VERSION STAMP THAT REFUSES RATHER THAN MISPARSES.</b> A format change
/// with no stamp does not fail — it reads the old bytes as the new layout and
/// restores an entity with plausible wrong values, which is the failure mode
/// this whole port keeps finding. <see cref="TryRead"/> refuses an unknown
/// version by name and by number.
/// </description></item>
/// <item><description>
/// <b>A CHECKSUM, BECAUSE THE FRAMING CATCHES ONLY WHAT BREAKS THE FRAMING.</b>
/// The magic, the version, the bounded count, the per-field length prefixes and
/// the trailing-bytes refusal between them catch truncation, a foreign file and
/// a layout disagreement. They catch NOTHING that leaves the framing intact: a
/// flipped bit inside a string's payload, inside a float or inside a handle
/// produces a block that parses PERFECTLY and restores a wrong world, silently,
/// which is the same failure mode the version stamp exists to prevent arriving
/// by a different door. <see cref="Checksum"/> over everything the header does
/// not itself explain is what turns that into a refusal -- and it is verified
/// before the entity count or any length prefix is believed, so no number out
/// of a damaged file is ever used to size anything.
/// </description></item>
/// <item><description>
/// <b>EVERY READ IS BOUNDS-CHECKED AND NOTHING THROWS.</b> The input is a file.
/// A length prefix that says 2 GB, a count that says four billion entities, a
/// string that runs off the end — each is a REFUSAL with a reason, never an
/// exception and never a partial restore. Half a restored world is worse than
/// none: the player keeps playing in it.
/// </description></item>
/// </list>
///
/// <para>
/// THE ENGINE DOES NOT PARSE THESE BYTES, which is what makes the format ours to
/// choose. <c>CSaveRestoreData</c> is <c>CSaveRestoreSegment</c> — a byte buffer
/// and a string table (<c>public/saverestoretypes.h</c>) — and the game fills it.
/// Stock's <c>CSave</c>/<c>CRestore</c> is one encoder over that buffer, not a
/// contract. What the engine DOES read is the entity TABLE beside it, and
/// <c>saverestoretypes.h:103</c> says so against itself: <i>"Engine does some of
/// the game writing (alas, probably shouldn't)"</i>. That table is the native
/// half of this lane and is not this file's business.
/// </para>
/// </summary>
public static class SavedEntityCodec
{
    /// <summary>
    /// <c>"SSSV"</c>. First, so a buffer that is not this refuses before any
    /// length in it is believed.
    /// </summary>
    public const uint Magic = 0x5653_5353;

    /// <summary>
    /// The format's version, and the number <see cref="TryRead"/> refuses
    /// anything else by.
    ///
    /// <para>
    /// BUMP IT FOR ANY LAYOUT CHANGE, including one that only adds. A reader
    /// that skips an unknown trailing field is a reader that cannot tell "this
    /// save is older" from "this save is damaged", and the two want opposite
    /// answers.
    /// </para>
    ///
    /// <para>
    /// <b>3 IS THE FIRST NUMBER THAT MEANS ONE THING.</b> Two lanes bumped this
    /// to 2 independently and for different reasons -- one added the header
    /// CHECKSUM, the other added the per-record INDEX -- and neither shipped.
    /// The two changes are compatible and both are here, but "version 2" is a
    /// number that named two incompatible layouts, so it is burned rather than
    /// picked. A stamp whose meaning depends on which branch wrote it is worse
    /// than no stamp: it refuses nothing and misparses confidently.
    /// </para>
    ///
    /// <para>
    /// What 3 carries over 1: a checksum word in the header, which moves the
    /// entity count four bytes along; and an index in each record, which moves
    /// the classname four bytes along. Either shift alone makes a v1 block
    /// misparse into plausible-looking garbage rather than fail, which is why
    /// both are a bump and not an addition the reader tolerates.
    /// </para>
    ///
    /// <para>
    /// <b>4 ADDS THE NATIVE SECTION, AND IT IS THE LAST BUMP THIS KIND OF
    /// CHANGE WILL COST.</b> Up to 3 a record carried map keys,
    /// <c>[NetState]</c> fields and output tallies — the MANAGED half of an
    /// entity — and the native half was not in the file at all. Not merely the
    /// origin: <c>CShellGameDll::Save</c> makes one call, which writes this
    /// block, <c>WriteSaveHeaders</c> writes nothing, and there is no
    /// <c>ETABLE</c> in the file, so health, angles, velocity and everything
    /// else the shell holds went nowhere. Measured twice from opposite
    /// directions — <c>tools/save-live-gate</c>'s damaged
    /// <c>func_breakable_surf</c> came back undamaged, and stage 3 could not
    /// build a player-health differential because the health it could reach was
    /// the map key.
    /// </para>
    ///
    /// <para>
    /// 4 adds ONE count-prefixed store per record, before the map keys. It is a
    /// store and not a field list on purpose (see
    /// <see cref="SavedEntity.Native"/>): a native field added later is a new
    /// KEY, not a new layout, so it costs no version 5. What forces the bump
    /// NOW is the count word itself — a v3 record has the map-key count where a
    /// v4 record has the native count, so a v3 block read as v4 takes the map
    /// keys for native fields and then reads every later field from the wrong
    /// offset.
    /// </para>
    /// </summary>
    public const int Version = 4;

    /// <summary>
    /// The versions this one replaced, kept as NUMBERS so the refusal can be
    /// specific rather than merely correct.
    ///
    /// <para>
    /// <b>1</b> carried a handle and no index, and no checksum. A handle out of
    /// a <c>.sav</c> names nothing after a load -- every serial advances when
    /// the world is rebuilt, measured at 0 of 470 surviving -- so a v1 block
    /// cannot be re-identified against a respawned map at all. It is REFUSED
    /// rather than read with a missing key, because a restore that guessed at
    /// the mapping is the silent-corruption failure this format exists to
    /// prevent.
    /// </para>
    ///
    /// <para>
    /// <b>2</b> is refused because it is AMBIGUOUS, which is a different reason
    /// and worth saying out loud. It named two different layouts in two
    /// unshipped branches. No file should carry it; one that does came from a
    /// development build and there is no way to tell which of the two it is.
    /// </para>
    /// </summary>
    public const int VersionWithoutIndex = 1;

    /// <inheritdoc cref="VersionWithoutIndex"/>
    public const int VersionThatMeantTwoThings = 2;

    /// <summary>
    /// <b>3</b> is refused for a third reason again: it carried no NATIVE
    /// section, so a v3 file holds an entity's managed state and nothing about
    /// where it was, what it weighed of health, or how fast it was moving.
    ///
    /// <para>
    /// It is refused rather than read as "a record with no native fields",
    /// which is the tolerant reading somebody will reach for. Two reasons, and
    /// the first is fatal on its own: a v3 record has its MAP-KEY count exactly
    /// where a v4 record has its native count, so reading one as the other
    /// takes the map keys for native fields and then reads every later field
    /// from the wrong offset — a block that parses into confident nonsense
    /// rather than failing. And even if the layouts had lined up, a restore
    /// that silently put every entity back with no position would be the
    /// original bug wearing a version number.
    /// </para>
    /// </summary>
    public const int VersionWithoutNativeState = 3;

    /// <summary>
    /// The longest string this format will write or accept, in UTF-8 bytes.
    ///
    /// <para>
    /// A cap rather than no cap because the length prefix comes off a FILE. It
    /// is generous against every real value — a classname is tens of bytes and
    /// the longest thing carried is a map key's value — and it turns a corrupt
    /// four-byte length into a named refusal instead of an allocation.
    /// </para>
    /// </summary>
    public const int MaxStringBytes = 8 * 1024;

    /// <summary>
    /// The most entities one block will write or accept.
    ///
    /// <para>
    /// dm_lockdown loads 473 and the reload path carries 472 of them. 65,536 is
    /// past anything this engine can hold — <c>MAX_EDICTS</c> is 2,048 — and is
    /// here for the same reason the string cap is: to bound a number read from
    /// a file before it is used to size anything.
    /// </para>
    /// </summary>
    public const int MaxEntities = 64 * 1024;

    /// <summary>
    /// Where the checksum word sits: after the magic and the version, and
    /// before everything it covers.
    ///
    /// <para>
    /// PUBLIC BECAUSE THE FORMAT IS, and because a test that has to build a
    /// hostile-but-well-framed block needs to stamp one. The alternative --
    /// a helper here that makes arbitrary bytes look valid -- is a worse thing
    /// to ship than two integers naming a layout this file documents anyway.
    /// </para>
    /// </summary>
    public const int ChecksumAt = 4 + 4;

    /// <summary>
    /// The first byte the checksum covers: everything from here to the end of
    /// the block.
    ///
    /// <para>
    /// THE ENTITY COUNT IS INSIDE IT, deliberately. The count is the first
    /// number out of the file that is used to size anything, so it is the last
    /// number that should be outside the check that says the file is intact.
    /// The magic and the version are outside it because each has a better
    /// refusal of its own: "this is not our file" and "this is not our layout"
    /// say more than "these bytes are damaged".
    /// </para>
    /// </summary>
    public const int ChecksumFrom = 4 + 4 + 4;

    private const int HeaderBytes = 4 + 4 + 4 + 4;   // magic, version, checksum, count

    /// <summary>
    /// How many bytes <paramref name="entities"/> occupies in this format.
    /// </summary>
    public static int ByteCount(IReadOnlyList<SavedEntity> entities)
    {
        int total = HeaderBytes;

        for (int i = 0; i < entities.Count; i++)
        {
            total += EntityBytes(entities[i]);
        }

        return total;
    }

    /// <summary>
    /// Write the records, and answer how many bytes the whole block needs.
    ///
    /// <para>
    /// NOTHING IS WRITTEN UNLESS EVERY BYTE FITS, so the return value answers
    /// both questions a caller has: pass an empty span to size, then call again
    /// to fill. It refuses where a diagnostic would truncate, and the reason is
    /// the one this format exists for — a half-written block is a save that
    /// restores SOME of the world, and the player keeps playing in what is
    /// left.
    /// </para>
    ///
    /// <para>
    /// A record too large to encode at all — a value past
    /// <see cref="MaxStringBytes"/> — makes the whole call refuse with -1
    /// rather than dropping that record. Dropping one is the silent failure:
    /// the save succeeds, the entity is simply not in it.
    /// </para>
    /// </summary>
    public static int Write(IReadOnlyList<SavedEntity> entities, Span<byte> destination, ILog log)
        => WriteCore(entities, destination, MaxStringBytes, log);

    /// <summary>
    /// <see cref="Write"/> with the string bound as a parameter: the <c>.sav</c>
    /// passes <see cref="MaxStringBytes"/>, the reload carrier
    /// <see cref="ReloadMaxStringBytes"/>.
    /// </summary>
    private static int WriteCore(IReadOnlyList<SavedEntity> entities, Span<byte> destination,
                                 int maxString, ILog log)
    {
        if (entities.Count > MaxEntities)
        {
            log.Warning($"[SourceSharp] save: {entities.Count} entities is past the " +
                        $"{MaxEntities} this format writes. Nothing written.\n");
            return -1;
        }

        for (int i = 0; i < entities.Count; i++)
        {
            if (!Encodable(entities[i], maxString, out string? offender))
            {
                log.Warning($"[SourceSharp] save: '{entities[i].ClassName}' has a value " +
                            $"longer than {maxString} bytes ({offender}). Nothing " +
                            "written -- a save that silently omits one entity is worse " +
                            "than one that refuses.\n");
                return -1;
            }
        }

        int required = ByteCount(entities);

        if (destination.Length < required)
        {
            return required;
        }

        var w = new Cursor(destination);
        w.UInt32(Magic);
        w.Int32(Version);
        w.UInt32(0);                 // the checksum, stamped once the payload exists
        w.Int32(entities.Count);

        for (int i = 0; i < entities.Count; i++)
        {
            SavedEntity e = entities[i];

            w.UInt32(e.Handle);
            w.Int32(e.Index);
            w.Str(e.ClassName);
            w.Str(e.Name);

            // FIRST OF THE FIVE STORES, which is where the version 3 -> 4 shift
            // comes from: a v3 record has the map-key count at this offset.
            w.Int32(e.Native.Count);
            foreach (var (field, value) in e.Native)
            {
                w.Str(field);
                w.Str(value);
            }

            w.Int32(e.Vars.Count);
            foreach (var (key, v) in e.Vars)
            {
                w.Str(key);
                w.Str(v.Value);
                w.Single(v.LastSetAt);
            }

            w.Int32(e.Net.Count);
            foreach (var (field, value) in e.Net)
            {
                w.Str(field);
                w.Str(value);
            }

            w.Int32(e.Outputs.Count);
            foreach (var (name, o) in e.Outputs)
            {
                w.Str(name);
                w.Int32(o.FireCount);
                // A null LastValue is not an empty one: an output that has
                // fired with no parameter is a different record from one that
                // fired with "". The flag carries the difference the string
                // cannot.
                w.Byte(o.LastValue is null ? (byte)0 : (byte)1);
                w.Str(o.LastValue ?? string.Empty);
            }

            w.Int32(e.Local.Count);
            foreach (var (key, value) in e.Local)
            {
                w.Str(key);
                w.Str(value);
            }
        }

        // STAMPED LAST, OVER WHAT WAS ACTUALLY WRITTEN rather than over the
        // records. Computing it from the objects would check the encoder
        // against itself; computing it from the bytes checks the bytes, which
        // is the only thing the reader will ever see.
        //
        // `required`, not `destination.Length`: a caller is allowed to hand
        // over a buffer larger than the block, and the slack past the block is
        // not part of it.
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[ChecksumAt..],
            Checksum(destination.Slice(ChecksumFrom, required - ChecksumFrom)));

        return required;
    }

    /// <summary>
    /// CRC-32 (the IEEE one: reflected, polynomial <c>0xEDB88320</c>) over
    /// <paramref name="bytes"/>.
    ///
    /// <para>
    /// A CRC RATHER THAN A HASH, and rather than a sum. The threat here is a
    /// damaged file, not a forged one -- nothing in a <c>.sav</c> is a secret
    /// and an attacker who can rewrite the file can rewrite the checksum too --
    /// so what is wanted is the cheap function that catches every burst error
    /// shorter than 33 bits and every single-bit flip, which a sum does not: a
    /// sum cannot see two bytes swapped, and a swapped pair is exactly what a
    /// layout disagreement produces.
    /// </para>
    ///
    /// <para>
    /// WRITTEN OUT HERE rather than taken from <c>System.IO.Hashing</c>,
    /// because a package reference is a thing the native host has to deploy and
    /// this is sixteen lines. It is the standard CRC-32 and is pinned to the
    /// standard check value by a test, so "it is some checksum of its own
    /// invention" is a question with an answer.
    /// </para>
    /// </summary>
    public static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint c = 0xFFFF_FFFFu;

        for (int i = 0; i < bytes.Length; i++)
        {
            c = CrcTable[(int)((c ^ bytes[i]) & 0xFF)] ^ (c >> 8);
        }

        return c ^ 0xFFFF_FFFFu;
    }

    private static readonly ImmutableArray<uint> CrcTable = [.. BuildCrcTable()];

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint c = i;

            for (int bit = 0; bit < 8; bit++)
            {
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }

    /// <summary>
    /// Read a block back, or refuse with a reason.
    ///
    /// <para>
    /// NEVER THROWS AND NEVER PARTIALLY SUCCEEDS. On any refusal
    /// <paramref name="entities"/> is empty and <paramref name="refusal"/> says
    /// what was wrong and where. The caller's correct response to a refusal is
    /// to load nothing, which is a failed load the player can see, rather than
    /// a world missing the entities the reader gave up on.
    /// </para>
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source,
                              out List<SavedEntity> entities,
                              out string? refusal)
        => TryReadCore(source, MaxStringBytes, out entities, out refusal);

    /// <summary><see cref="TryRead"/> with the string bound as a parameter. See <see cref="WriteCore"/>.</summary>
    private static bool TryReadCore(ReadOnlySpan<byte> source, int maxString,
                                    out List<SavedEntity> entities,
                                    out string? refusal)
    {
        entities = new List<SavedEntity>();

        var r = new Reader(source, maxString);

        // THE WHOLE HEADER IS READ FIRST AND BELIEVED AFTERWARDS, in order.
        // Reading four bounds-checked words into four locals uses none of them
        // for anything; the order that matters is the order of the checks
        // below, and it is magic, then version, then CHECKSUM, and only then
        // the count. Nothing out of this file sizes anything until the checksum
        // has said the file is the file that was written.
        if (!r.UInt32(out uint magic) || !r.Int32(out int version)
            || !r.UInt32(out uint stamped) || !r.Int32(out int count))
        {
            refusal = "the block is shorter than its own header";
            return false;
        }

        if (magic != Magic)
        {
            refusal = $"magic 0x{magic:X8}, expected 0x{Magic:X8} -- this block was not " +
                       "written by SavedEntityCodec";
            return false;
        }

        if (version != Version)
        {
            // BY NUMBER, both of them. "unsupported version" without the two
            // numbers is the message that sends the reader to the source.
            // ...and the two OLD numbers are refused for different reasons, so
            // they say different things. "Unsupported version" would be true of
            // both and useful for neither.
            refusal = version switch
            {
                VersionWithoutIndex =>
                    $"version {version}, this build reads {Version} -- v{VersionWithoutIndex} " +
                    "carried no entity index, and a handle out of a .sav names nothing " +
                    "once the world has been rebuilt",

                VersionThatMeantTwoThings =>
                    $"version {version}, this build reads {Version} -- v{VersionThatMeantTwoThings} " +
                    "named two different layouts in two unshipped branches, so there is no " +
                    "way to tell which one this is",

                VersionWithoutNativeState =>
                    $"version {version}, this build reads {Version} -- v{VersionWithoutNativeState} " +
                    "carried no native section, so it holds an entity's managed state and not " +
                    "its origin, angles, velocity or health; and its map-key count sits where " +
                    "this layout's native count does, so reading it here would misparse rather " +
                    "than come up short",

                _ => $"version {version}, this build reads {Version}",
            };
            return false;
        }

        uint computed = Checksum(source[ChecksumFrom..]);

        if (computed != stamped)
        {
            // BY VALUE, both of them, for the same reason the version refusal
            // names two numbers: "checksum mismatch" tells its reader nothing
            // they can act on, and these two numbers say whether the block was
            // damaged in transit or was never stamped at all.
            refusal = $"checksum 0x{stamped:X8}, computed 0x{computed:X8} -- these are not " +
                       "the bytes that were written";
            return false;
        }

        if (count < 0 || count > MaxEntities)
        {
            refusal = $"entity count {count} is not in 0..{MaxEntities}";
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!ReadOne(ref r, out SavedEntity? entity, out string? why))
            {
                refusal = $"entity {i} of {count}: {why}";
                entities.Clear();
                return false;
            }

            entities.Add(entity!);
        }

        // TRAILING BYTES ARE A REFUSAL, not a shrug. They mean the writer and
        // this reader disagree about the layout while agreeing about the
        // version -- which is the one disagreement the version stamp cannot
        // catch, and the one most likely to have restored something wrong
        // already.
        //
        // THE CHECKSUM DOES NOT SUBSUME THIS. Bytes appended to a finished
        // block by accident are caught above, as damage; bytes a DIFFERENT
        // BUILD wrote deliberately arrive with a checksum that covers them and
        // is correct, and this is the only thing that sees them.
        if (!r.AtEnd)
        {
            refusal = $"{r.Remaining} bytes left over after {count} entities";
            entities.Clear();
            return false;
        }

        refusal = null;
        return true;
    }

    private static bool ReadOne(ref Reader r, out SavedEntity? entity, out string? why)
    {
        entity = null;

        if (!r.UInt32(out uint handle)) { why = "truncated at the handle"; return false; }
        if (!r.Int32(out int index)) { why = "truncated at the index"; return false; }
        if (!r.Str(out string? className)) { why = "truncated at the classname"; return false; }
        if (!r.Str(out string? name)) { why = "truncated at the name"; return false; }

        var e = new SavedEntity(handle, className!, name!, index);

        if (!r.Int32(out int nNative) || nNative < 0) { why = "bad native-field count"; return false; }
        for (int i = 0; i < nNative; i++)
        {
            if (!r.Str(out string? field) || !r.Str(out string? value))
            {
                why = $"truncated in native field {i} of {nNative}";
                return false;
            }

            e.SaveNative(field!, value!);
        }

        if (!r.Int32(out int nVars) || nVars < 0) { why = "bad map-key count"; return false; }
        for (int i = 0; i < nVars; i++)
        {
            if (!r.Str(out string? key) || !r.Str(out string? value) || !r.Single(out float at))
            {
                why = $"truncated in map key {i} of {nVars}";
                return false;
            }

            e.SaveVar(key!, value!, at);
        }

        if (!r.Int32(out int nNet) || nNet < 0) { why = "bad networked-field count"; return false; }
        for (int i = 0; i < nNet; i++)
        {
            if (!r.Str(out string? field) || !r.Str(out string? value))
            {
                why = $"truncated in networked field {i} of {nNet}";
                return false;
            }

            e.SaveNet(field!, value!);
        }

        if (!r.Int32(out int nOut) || nOut < 0) { why = "bad output count"; return false; }
        for (int i = 0; i < nOut; i++)
        {
            if (!r.Str(out string? outName) || !r.Int32(out int fired)
                || !r.Byte(out byte hasValue) || !r.Str(out string? last))
            {
                why = $"truncated in output {i} of {nOut}";
                return false;
            }

            e.SaveOutput(outName!, fired, hasValue == 0 ? null : last);
        }

        if (!r.Int32(out int nLocal) || nLocal < 0) { why = "bad local-key count"; return false; }
        for (int i = 0; i < nLocal; i++)
        {
            if (!r.Str(out string? key) || !r.Str(out string? value))
            {
                why = $"truncated in local key {i} of {nLocal}";
                return false;
            }

            e.Set(key!, value);
        }

        entity = e;
        why = null;
        return true;
    }

    private static int EntityBytes(SavedEntity e)
    {
        int n = 4 + 4                                // handle, index
              + StrBytes(e.ClassName) + StrBytes(e.Name)
              + 4 + 4 + 4 + 4 + 4;                   // the five counts

        foreach (var (field, value) in e.Native)
            n += StrBytes(field) + StrBytes(value);

        foreach (var (key, v) in e.Vars)
            n += StrBytes(key) + StrBytes(v.Value) + 4;

        foreach (var (field, value) in e.Net)
            n += StrBytes(field) + StrBytes(value);

        foreach (var (name, o) in e.Outputs)
            n += StrBytes(name) + 4 + 1 + StrBytes(o.LastValue ?? string.Empty);

        foreach (var (key, value) in e.Local)
            n += StrBytes(key) + StrBytes(value);

        return n;
    }

    private static bool Encodable(SavedEntity e, int max, out string? offender)
    {
        bool TooLong(string s) => Encoding.UTF8.GetByteCount(s) > max;

        if (TooLong(e.ClassName)) { offender = "classname"; return false; }
        if (TooLong(e.Name)) { offender = "targetname"; return false; }

        foreach (var (field, value) in e.Native)
            if (TooLong(field) || TooLong(value)) { offender = $"native field '{field}'"; return false; }

        foreach (var (key, v) in e.Vars)
            if (TooLong(key) || TooLong(v.Value)) { offender = $"map key '{key}'"; return false; }

        foreach (var (field, value) in e.Net)
            if (TooLong(field) || TooLong(value)) { offender = $"field '{field}'"; return false; }

        foreach (var (name, o) in e.Outputs)
            if (TooLong(name) || TooLong(o.LastValue ?? string.Empty))
            { offender = $"output '{name}'"; return false; }

        foreach (var (key, value) in e.Local)
            if (TooLong(key) || TooLong(value)) { offender = $"local key '{key}'"; return false; }

        offender = null;
        return true;
    }

    private static int StrBytes(string s) => 4 + Encoding.UTF8.GetByteCount(s);

    //-------------------------------------------------------------------------
    // The two cursors. Little-endian throughout and stated rather than assumed:
    // this format is written and read by the same architecture today, and a
    // save moved between machines should not be the thing that discovers an
    // endian assumption.
    //-------------------------------------------------------------------------

    //-------------------------------------------------------------------------
    // THE RELOAD CARRIER'S SECTIONS -- plan_abi_hotreload D6.
    //
    // A hot reload used to hand ReloadStore the records themselves: SavedEntity,
    // SavedIo, ShellLessEntity and ReloadRecord objects, waiting in the default
    // load context while the side that made them unloaded. They held only
    // strings, floats and bare handles, so they pinned no side -- but they
    // pinned their TYPES in SourceSharp.Abi, and through them ScriptedVars,
    // EntityDirectory and ShellLess. Now the store holds bytes, and these
    // write and read them.
    //
    // ONE PROCESS, ONE BUILD, ONE SECOND, so these sections carry no version
    // history and no string bound worth the name: a reload that refused an
    // entity for a long map key would lose it, where a .sav refusing is the
    // safer answer. The framing and the checksum stay anyway -- they are what
    // turns a writer/reader disagreement into a refusal rather than a world
    // restored from misparsed bytes.
    //-------------------------------------------------------------------------

    /// <summary>The I/O section's magic, <c>"SSIO"</c> little-endian.</summary>
    public const uint IoMagic = 0x4F49_5353;

    /// <summary>The shell-less section's magic, <c>"SSSL"</c> little-endian.</summary>
    public const uint ShellLessMagic = 0x4C53_5353;

    /// <summary>The participant-record section's magic, <c>"SSRP"</c> little-endian.</summary>
    public const uint PartsMagic = 0x5052_5353;

    /// <summary>The layout of the three reload-only sections.</summary>
    public const int ReloadVersion = 1;

    /// <summary>
    /// The longest string a reload section carries. A reload has no hostile
    /// input to bound -- the writer is this build, a second ago -- so this is
    /// a sanity ceiling, not a format limit.
    /// </summary>
    public const int ReloadMaxStringBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The entity rows of one reload, as bytes: the <c>.sav</c> layout exactly,
    /// with the string bound lifted. Answers null when the rows cannot be
    /// written at all, which the caller reports.
    /// </summary>
    public static byte[]? EncodeForReload(IReadOnlyList<SavedEntity> entities, ILog log)
    {
        int required = WriteCore(entities, Span<byte>.Empty, ReloadMaxStringBytes, log);
        if (required < 0)
            return null;

        var bytes = new byte[required];
        WriteCore(entities, bytes, ReloadMaxStringBytes, log);
        return bytes;
    }

    /// <summary>Reads what <see cref="EncodeForReload"/> wrote.</summary>
    public static bool TryReadForReload(ReadOnlySpan<byte> source,
                                        out List<SavedEntity> entities,
                                        out string? refusal)
        => TryReadCore(source, ReloadMaxStringBytes, out entities, out refusal);

    /// <summary>One side's I/O half -- the queued events and the connections -- as bytes.</summary>
    public static byte[] EncodeIo(SavedIo io)
    {
        var w = new Sink(IoMagic);

        w.Int32(io.Events.Count);
        foreach (var e in io.Events)
        {
            w.Single(e.Delay);
            w.Str(e.Target);
            w.UInt32(e.DirectTarget);
            w.Str(e.TargetInput);
            w.OptStr(e.Value);
            w.UInt32(e.Activator);
            w.UInt32(e.Caller);
            w.Int32(e.OutputId);
        }

        w.Int32(io.Connections.Count);
        foreach (var c in io.Connections)
        {
            w.UInt32(c.Owner);
            w.Str(c.OutputName);
            w.Str(c.Target);
            w.Str(c.TargetInput);
            w.OptStr(c.Parameter);
            w.Single(c.Delay);
            w.Int32(c.TimesToFire);
            w.Int32(c.IdStamp);
        }

        return w.Finish();
    }

    /// <summary>Reads what <see cref="EncodeIo"/> wrote.</summary>
    public static bool TryReadIo(ReadOnlySpan<byte> source, out SavedIo io, out string? refusal)
    {
        io = SavedIo.Empty;

        if (!OpenSection(source, IoMagic, "I/O", out Reader r, out refusal))
            return false;

        if (!r.Int32(out int nEvents) || nEvents < 0 || nEvents > r.Remaining)
        {
            refusal = "bad I/O event count";
            return false;
        }

        var events = new List<SavedIoEvent>(nEvents);
        for (int i = 0; i < nEvents; i++)
        {
            if (!r.Single(out float delay) || !r.Str(out string? target)
                || !r.UInt32(out uint direct) || !r.Str(out string? input)
                || !r.OptStr(out string? value) || !r.UInt32(out uint activator)
                || !r.UInt32(out uint caller) || !r.Int32(out int outputId))
            {
                refusal = $"truncated in I/O event {i} of {nEvents}";
                return false;
            }

            events.Add(new SavedIoEvent(delay, target!, direct, input!, value,
                                        activator, caller, outputId));
        }

        if (!r.Int32(out int nConnections) || nConnections < 0 || nConnections > r.Remaining)
        {
            refusal = "bad connection count";
            return false;
        }

        var connections = new List<SavedConnection>(nConnections);
        for (int i = 0; i < nConnections; i++)
        {
            if (!r.UInt32(out uint owner) || !r.Str(out string? output)
                || !r.Str(out string? target) || !r.Str(out string? input)
                || !r.OptStr(out string? parameter) || !r.Single(out float delay)
                || !r.Int32(out int times) || !r.Int32(out int stamp))
            {
                refusal = $"truncated in connection {i} of {nConnections}";
                return false;
            }

            connections.Add(new SavedConnection(owner, output!, target!, input!, parameter,
                                                delay, times, stamp));
        }

        if (!CloseSection(ref r, out refusal))
            return false;

        io = new SavedIo(events, connections);
        return true;
    }

    /// <summary>
    /// One side's shell-less entities, as bytes. Every settable member of
    /// <see cref="ShellLessEntity"/>: a member added there and not here is
    /// caught by <c>ReloadCarrierCodecTests</c>, which sets every property
    /// by reflection and compares.
    /// </summary>
    public static byte[] EncodeShellLess(IReadOnlyList<ShellLessEntity> entities)
    {
        var w = new Sink(ShellLessMagic);

        w.Int32(entities.Count);
        foreach (var e in entities)
        {
            w.UInt32(e.Handle.Value);
            w.Str(e.ClassName);
            w.Str(e.Name);
            w.Int32(e.SpawnFlags);
            w.Int32(e.Health);
            w.Vec(e.Origin);
            w.Vec(e.Angles);
            w.Str(e.Model);
            w.Vec(e.SpawnOrigin);
            w.Vec(e.SpawnAngles);
            w.Byte(e.HasSpawned ? (byte)1 : (byte)0);
            w.OptStr(e.ParentName);
            w.OptStr(e.ParentAttachmentName);
        }

        return w.Finish();
    }

    /// <summary>Reads what <see cref="EncodeShellLess"/> wrote.</summary>
    public static bool TryReadShellLess(ReadOnlySpan<byte> source,
                                        out List<ShellLessEntity> entities,
                                        out string? refusal)
    {
        entities = new List<ShellLessEntity>();

        if (!OpenSection(source, ShellLessMagic, "shell-less", out Reader r, out refusal))
            return false;

        if (!r.Int32(out int count) || count < 0 || count > r.Remaining)
        {
            refusal = "bad shell-less count";
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!r.UInt32(out uint handle) || !r.Str(out string? className)
                || !r.Str(out string? name) || !r.Int32(out int spawnFlags)
                || !r.Int32(out int health) || !r.Vec(out Vector origin)
                || !r.Vec(out Vector angles) || !r.Str(out string? model)
                || !r.Vec(out Vector spawnOrigin) || !r.Vec(out Vector spawnAngles)
                || !r.Byte(out byte spawned) || !r.OptStr(out string? parentName)
                || !r.OptStr(out string? parentAttachment))
            {
                refusal = $"truncated in shell-less entity {i} of {count}";
                entities.Clear();
                return false;
            }

            var e = new ShellLessEntity(new EntityHandle(handle), className!)
            {
                Name = name!,
                SpawnFlags = spawnFlags,
                Health = health,
                Origin = origin,
                Angles = angles,
                Model = model!,
                SpawnOrigin = spawnOrigin,
                SpawnAngles = spawnAngles,
                HasSpawned = spawned != 0,
            };

            // THROUGH THE KEY THE MAP SET THEM BY, because they have no other
            // setter: a parent link is recorded from the map and resolved later,
            // and a recorded-but-unresolved link is exactly what crosses here.
            if (parentName is not null)
                e.ApplyShellKey("parentname", parentName);
            if (parentAttachment is not null)
                e.ApplyShellKey("parentattachment", parentAttachment);

            entities.Add(e);
        }

        if (!CloseSection(ref r, out refusal))
        {
            entities.Clear();
            return false;
        }

        return true;
    }

    /// <summary>
    /// One side's participant records -- the state that is not an entity's --
    /// as bytes, in the dictionary's order.
    /// </summary>
    public static byte[] EncodeParts(IReadOnlyDictionary<string, ReloadRecord> parts)
    {
        var w = new Sink(PartsMagic);

        w.Int32(parts.Count);
        foreach (var (key, record) in parts)
        {
            w.Str(key);
            w.Str(record.Owner);

            w.Int32(record.Scalars.Count);
            foreach (var (k, v) in record.Scalars)
            {
                w.Str(k);
                w.Str(v);
            }

            w.Int32(record.Rows.Count);
            foreach (var row in record.Rows)
            {
                w.Int32(row.Count);
                foreach (var (k, v) in row.Fields)
                {
                    w.Str(k);
                    w.Str(v);
                }
            }
        }

        return w.Finish();
    }

    /// <summary>Reads what <see cref="EncodeParts"/> wrote.</summary>
    public static bool TryReadParts(ReadOnlySpan<byte> source,
                                    out Dictionary<string, ReloadRecord> parts,
                                    out string? refusal)
    {
        parts = new Dictionary<string, ReloadRecord>(StringComparer.Ordinal);

        if (!OpenSection(source, PartsMagic, "participant", out Reader r, out refusal))
            return false;

        if (!r.Int32(out int count) || count < 0 || count > r.Remaining)
        {
            refusal = "bad participant-record count";
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!r.Str(out string? key) || !r.Str(out string? owner)
                || !r.Int32(out int nScalars) || nScalars < 0)
            {
                refusal = $"truncated in participant record {i} of {count}";
                parts.Clear();
                return false;
            }

            var record = new ReloadRecord(owner!);

            for (int s = 0; s < nScalars; s++)
            {
                if (!r.Str(out string? k) || !r.Str(out string? v))
                {
                    refusal = $"truncated in scalar {s} of record '{owner}'";
                    parts.Clear();
                    return false;
                }

                record.Set(k!, v);
            }

            if (!r.Int32(out int nRows) || nRows < 0)
            {
                refusal = $"bad row count in record '{owner}'";
                parts.Clear();
                return false;
            }

            for (int j = 0; j < nRows; j++)
            {
                if (!r.Int32(out int nFields) || nFields < 0)
                {
                    refusal = $"bad field count in row {j} of record '{owner}'";
                    parts.Clear();
                    return false;
                }

                var row = record.AddRow();
                for (int f = 0; f < nFields; f++)
                {
                    if (!r.Str(out string? k) || !r.Str(out string? v))
                    {
                        refusal = $"truncated in row {j} of record '{owner}'";
                        parts.Clear();
                        return false;
                    }

                    row.Set(k!, v);
                }
            }

            parts[key!] = record;
        }

        if (!CloseSection(ref r, out refusal))
        {
            parts.Clear();
            return false;
        }

        return true;
    }

    /// <summary>
    /// The tally a reload's records add up to. Shared by the save and the
    /// restore so the two halves cannot count differently -- a report whose
    /// two numbers were computed by two loops would disagree the first time one
    /// of them was edited, and "carried 472, restored 471" would then mean
    /// nothing. Here rather than on <see cref="ReloadStore"/>, which holds bytes
    /// and so no longer knows what a record is.
    /// </summary>
    public static ReloadStore.ReloadTally Measure(IReadOnlyList<SavedEntity> entities,
                                                  IReadOnlyDictionary<string, ReloadRecord>? parts,
                                                  SavedIo? io,
                                                  int shellLess)
    {
        int fields = 0, bytes = 0;
        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < entities.Count; i++)
        {
            SavedEntity e = entities[i];
            classes.Add(e.ClassName);
            fields += e.Count;
            bytes += e.ByteCount;
        }

        int partFields = 0, partBytes = 0;
        if (parts is not null)
        {
            foreach (var (_, record) in parts)
            {
                partFields += record.FieldCount;
                partBytes += record.ByteCount;
            }
        }

        return new ReloadStore.ReloadTally(entities.Count, classes.Count, fields, bytes,
                                           parts?.Count ?? 0, partFields, partBytes,
                                           io?.Events.Count ?? 0, io?.Connections.Count ?? 0,
                                           shellLess);
    }

    /// <summary>
    /// Checks a reload section's magic, version and checksum, and leaves the
    /// reader at its first payload byte.
    /// </summary>
    private static bool OpenSection(ReadOnlySpan<byte> source, uint magic, string what,
                                    out Reader r, out string? refusal)
    {
        r = new Reader(source, ReloadMaxStringBytes);

        if (!r.UInt32(out uint got) || !r.Int32(out int version) || !r.UInt32(out uint stamped))
        {
            refusal = $"the {what} section is shorter than its own header";
            return false;
        }

        if (got != magic)
        {
            refusal = $"magic 0x{got:X8}, expected 0x{magic:X8} -- not a {what} section";
            return false;
        }

        if (version != ReloadVersion)
        {
            refusal = $"{what} section version {version}, this build reads {ReloadVersion}";
            return false;
        }

        uint computed = Checksum(source[ChecksumFrom..]);
        if (computed != stamped)
        {
            refusal = $"{what} section checksum 0x{stamped:X8}, computed 0x{computed:X8}";
            return false;
        }

        refusal = null;
        return true;
    }

    private static bool CloseSection(ref Reader r, out string? refusal)
    {
        if (!r.AtEnd)
        {
            refusal = $"{r.Remaining} bytes left over after the section";
            return false;
        }

        refusal = null;
        return true;
    }

    /// <summary>
    /// A growing writer for the reload sections: magic, version and a checksum
    /// that <see cref="Finish"/> stamps, then whatever the caller writes. Not
    /// size-then-fill like <see cref="Write"/>, because nothing here writes into
    /// an engine's buffer.
    /// </summary>
    private sealed class Sink
    {
        private readonly System.Buffers.ArrayBufferWriter<byte> _out = new();

        public Sink(uint magic)
        {
            UInt32(magic);
            Int32(ReloadVersion);
            UInt32(0);               // the checksum, stamped by Finish
        }

        public void Byte(byte v)
        {
            _out.GetSpan(1)[0] = v;
            _out.Advance(1);
        }

        public void Int32(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_out.GetSpan(4), v);
            _out.Advance(4);
        }

        public void UInt32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_out.GetSpan(4), v);
            _out.Advance(4);
        }

        public void Single(float v)
        {
            BinaryPrimitives.WriteSingleLittleEndian(_out.GetSpan(4), v);
            _out.Advance(4);
        }

        public void Vec(Vector v)
        {
            Single(v.X);
            Single(v.Y);
            Single(v.Z);
        }

        public void Str(string s)
        {
            int bytes = Encoding.UTF8.GetByteCount(s);
            Int32(bytes);
            Encoding.UTF8.GetBytes(s, _out.GetSpan(bytes));
            _out.Advance(bytes);
        }

        /// <summary>A string that may be null: a presence byte, then the string (empty when absent).</summary>
        public void OptStr(string? s)
        {
            Byte(s is null ? (byte)0 : (byte)1);
            Str(s ?? string.Empty);
        }

        /// <summary>The section's bytes, with the checksum over everything after it stamped in.</summary>
        public byte[] Finish()
        {
            byte[] bytes = _out.WrittenSpan.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ChecksumAt),
                                                     Checksum(bytes.AsSpan(ChecksumFrom)));
            return bytes;
        }
    }

    private ref struct Cursor
    {
        private readonly Span<byte> _at;
        private int _n;

        public Cursor(Span<byte> at) { _at = at; _n = 0; }

        public void Byte(byte v) => _at[_n++] = v;

        public void Int32(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_at[_n..], v);
            _n += 4;
        }

        public void UInt32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_at[_n..], v);
            _n += 4;
        }

        public void Single(float v)
        {
            BinaryPrimitives.WriteSingleLittleEndian(_at[_n..], v);
            _n += 4;
        }

        public void Str(string s)
        {
            int bytes = Encoding.UTF8.GetByteCount(s);
            Int32(bytes);
            Encoding.UTF8.GetBytes(s, _at[_n..]);
            _n += bytes;
        }
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _at;
        private readonly int _maxString;
        private int _n;

        public Reader(ReadOnlySpan<byte> at, int maxString) { _at = at; _maxString = maxString; _n = 0; }

        public bool AtEnd => _n == _at.Length;
        public int Remaining => _at.Length - _n;

        public bool Byte(out byte v)
        {
            if (Remaining < 1) { v = 0; return false; }
            v = _at[_n++];
            return true;
        }

        public bool Int32(out int v)
        {
            if (Remaining < 4) { v = 0; return false; }
            v = BinaryPrimitives.ReadInt32LittleEndian(_at[_n..]);
            _n += 4;
            return true;
        }

        public bool UInt32(out uint v)
        {
            if (Remaining < 4) { v = 0; return false; }
            v = BinaryPrimitives.ReadUInt32LittleEndian(_at[_n..]);
            _n += 4;
            return true;
        }

        public bool Single(out float v)
        {
            if (Remaining < 4) { v = 0f; return false; }
            v = BinaryPrimitives.ReadSingleLittleEndian(_at[_n..]);
            _n += 4;
            return true;
        }

        public bool Str(out string? s)
        {
            s = null;

            if (!Int32(out int bytes))
                return false;

            // THE LENGTH CAME OFF A FILE. Negative, absurd, or simply longer
            // than what is left: each is a refusal here rather than an
            // exception three frames up.
            if (bytes < 0 || bytes > _maxString || bytes > Remaining)
                return false;

            s = Encoding.UTF8.GetString(_at.Slice(_n, bytes));
            _n += bytes;
            return true;
        }

        /// <summary>A string that may be null: a presence byte, then the string.</summary>
        public bool OptStr(out string? s)
        {
            s = null;

            if (!Byte(out byte present) || present > 1)
                return false;

            if (present == 0)
                return Str(out _);

            return Str(out s);
        }

        public bool Vec(out Vector v)
        {
            v = default;

            if (!Single(out float x) || !Single(out float y) || !Single(out float z))
                return false;

            v = new Vector(x, y, z);
            return true;
        }
    }
}

/// <summary>
/// The typed door in front of <see cref="ReloadStore"/>: a side's reload records
/// in, through <see cref="SavedEntityCodec"/>, as bytes; and back out again.
///
/// <para>
/// <b>WHY THE STORE ITSELF NO LONGER TAKES RECORDS</b> (<c>plan_abi_hotreload</c>
/// D6). The store has to live in the default load context, which is
/// SourceSharp.Abi, and a store typed on <see cref="SavedEntity"/>,
/// <see cref="SavedIo"/>, <see cref="ShellLessEntity"/> and
/// <see cref="ReloadRecord"/> kept all four — and what they name — in Abi with
/// it. This class names them instead, and can leave Abi with them.
/// </para>
///
/// <para>
/// The store's rules hold through here unchanged: per side, taking empties,
/// an empty participant record is dropped. A section that will not decode is
/// said once and comes back empty: a reload that restored misparsed bytes would
/// put a world back that was never saved.
/// </para>
/// </summary>
public static class ReloadCarrier
{
    /// <summary>The section names in <see cref="ReloadStore"/>.</summary>
    public const string EntitiesSection = "entities";

    /// <inheritdoc cref="EntitiesSection"/>
    public const string IoSection = "io";

    /// <inheritdoc cref="EntitiesSection"/>
    public const string ShellLessSection = "shell-less";

    /// <inheritdoc cref="EntitiesSection"/>
    public const string PartsSection = "parts";

    //-------------------------------------------------------------------------
    // The entity rows.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Puts entities' state aside, replacing anything already waiting for the
    /// same handle and keeping the rest. Answers how many are waiting.
    /// </summary>
    public static int Put(ReloadStore store, IReadOnlyList<SavedEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var merged = new Dictionary<uint, SavedEntity>();

        foreach (var waiting in Read(store, store.Peek(EntitiesSection)))
            merged[waiting.Handle] = waiting;

        foreach (var e in entities)
            merged[e.Handle] = e;

        var all = merged.Values.ToArray();
        byte[]? bytes = SavedEntityCodec.EncodeForReload(all, store.Log);

        if (bytes is null)
        {
            store.Log.Warning($"[SourceSharp] reload: {all.Length} entity record(s) could not be " +
                        "encoded; nothing is carried for this side\n");
            store.Put(EntitiesSection, null, 0);
            return 0;
        }

        store.Put(EntitiesSection, bytes, all.Length);
        return all.Length;
    }

    /// <summary>One entity's state; see <see cref="Put(ReloadStore, IReadOnlyList{SavedEntity})"/>.</summary>
    public static int Put(ReloadStore store, SavedEntity entity) => Put(store, new[] { entity });

    /// <summary>Every entity record waiting for a side, and the store holds none afterwards.</summary>
    public static IReadOnlyList<SavedEntity> Take(ReloadStore store)
        => Read(store, store.Take(EntitiesSection));

    /// <summary>How many entity records are waiting, without disturbing them.</summary>
    public static int Count(ReloadStore store) => store.Count(EntitiesSection);

    private static List<SavedEntity> Read(ReloadStore store, byte[]? bytes)
    {
        if (bytes is null)
            return new List<SavedEntity>();

        if (SavedEntityCodec.TryReadForReload(bytes, out var entities, out string? refusal))
            return entities;

        Refused(store, EntitiesSection, refusal);
        return new List<SavedEntity>();
    }

    //-------------------------------------------------------------------------
    // The I/O half, which is not per entity: the queue and the connections
    // belong to the directory, not to any one row.
    //-------------------------------------------------------------------------

    /// <summary>Puts a side's I/O half aside, replacing anything already there.</summary>
    public static void PutIo(ReloadStore store, SavedIo? io)
    {
        io ??= SavedIo.Empty;
        store.Put(IoSection, SavedEntityCodec.EncodeIo(io), io.Count);
    }

    /// <summary>
    /// A side's I/O half, and the store holds none afterwards: an event restored
    /// twice is an output delivered twice.
    /// </summary>
    public static SavedIo TakeIo(ReloadStore store) => ReadIo(store, store.Take(IoSection));

    /// <summary>A side's I/O half as it is waiting, without disturbing it.</summary>
    public static SavedIo PeekIo(ReloadStore store) => ReadIo(store, store.Peek(IoSection));

    /// <summary>How much of a side's I/O half is waiting: events plus connections.</summary>
    public static int IoCount(ReloadStore store) => store.Count(IoSection);

    private static SavedIo ReadIo(ReloadStore store, byte[]? bytes)
    {
        if (bytes is null)
            return SavedIo.Empty;

        if (SavedEntityCodec.TryReadIo(bytes, out var io, out string? refusal))
            return io;

        Refused(store, IoSection, refusal);
        return SavedIo.Empty;
    }

    //-------------------------------------------------------------------------
    // The shell-less entities themselves: not state put back onto an entity the
    // native world kept, but the entity, which the unload deleted.
    //-------------------------------------------------------------------------

    /// <summary>Puts a side's shell-less entities aside, replacing anything already there.</summary>
    public static void PutShellLess(ReloadStore store, IReadOnlyList<ShellLessEntity>? entities)
    {
        entities ??= Array.Empty<ShellLessEntity>();
        store.Put(ShellLessSection, SavedEntityCodec.EncodeShellLess(entities),
                        entities.Count);
    }

    /// <summary>A side's shell-less entities, and the store holds none afterwards.</summary>
    public static IReadOnlyList<ShellLessEntity> TakeShellLess(ReloadStore store)
    {
        byte[]? bytes = store.Take(ShellLessSection);

        if (bytes is null)
            return Array.Empty<ShellLessEntity>();

        if (SavedEntityCodec.TryReadShellLess(bytes, out var entities, out string? refusal))
            return entities;

        Refused(store, ShellLessSection, refusal);
        return Array.Empty<ShellLessEntity>();
    }

    /// <summary>How many shell-less entities are waiting, without disturbing them.</summary>
    public static int ShellLessCount(ReloadStore store) => store.Count(ShellLessSection);

    //-------------------------------------------------------------------------
    // The participant records: the state that is not an entity's, one record
    // per participant, keyed by its owner.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Puts one participant's record aside, replacing anything already there
    /// for it. An empty record is DROPPED rather than stored, and drops what was
    /// there: a participant that had nothing to say is not the same as one that
    /// said nothing, and the report counts what was carried.
    /// </summary>
    public static void PutPart(ReloadStore store, ReloadRecord? record)
    {
        if (record is null)
            return;

        var parts = ReadParts(store, store.Peek(PartsSection));

        if (record.IsEmpty)
            parts.Remove(record.Owner);
        else
            parts[record.Owner] = record;

        store.Put(PartsSection, SavedEntityCodec.EncodeParts(parts), parts.Count);
    }

    /// <summary>
    /// A side's participant records, and the store holds none afterwards. A
    /// record restored twice would re-place every pickup the level init made
    /// and re-book every think.
    /// </summary>
    public static IReadOnlyDictionary<string, ReloadRecord> TakeParts(ReloadStore store)
        => ReadParts(store, store.Take(PartsSection));

    /// <summary>
    /// The participant records actually WAITING, without disturbing them —
    /// read back from the store's bytes, so the save tally counts the deposit
    /// and not the offer.
    /// </summary>
    public static IReadOnlyDictionary<string, ReloadRecord> PeekParts(ReloadStore store)
        => ReadParts(store, store.Peek(PartsSection));

    /// <summary>How many participant records are waiting, without disturbing them.</summary>
    public static int PartCount(ReloadStore store) => store.Count(PartsSection);

    private static Dictionary<string, ReloadRecord> ReadParts(ReloadStore store, byte[]? bytes)
    {
        if (bytes is null)
            return new Dictionary<string, ReloadRecord>(StringComparer.Ordinal);

        if (SavedEntityCodec.TryReadParts(bytes, out var parts, out string? refusal))
            return parts;

        Refused(store, PartsSection, refusal);
        return new Dictionary<string, ReloadRecord>(StringComparer.Ordinal);
    }

    /// <summary>Drops every section a side has waiting. See <see cref="ReloadStore.Clear"/>.</summary>
    public static void Clear(ReloadStore store) => store.Clear();

    private static void Refused(ReloadStore store, string section, string? refusal)
        => store.Log.Warning($"[SourceSharp] reload: the {store.Side} side's '{section}' section did not " +
                       $"decode ({refusal}); nothing from it is restored\n");
}
