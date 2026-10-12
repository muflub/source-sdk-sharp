using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The join handshake carries TWO hashes, and the second one is compared.
///
/// <para>
/// The second is the ENGINE BLOCK's layout (plan_entity.md §3.2): a struct
/// declared once in C# and once in C++, at a fixed offset every shell getter
/// and the network decoder read. Two declarations of one layout is the port's
/// named drift risk (§7). Per-side offset facts cannot catch it — each build is
/// self-consistent — so the defence is a hash computed from the C# declaration
/// and exchanged at join.
/// </para>
///
/// <para>
/// THE SLOT WAS LANDED BEFORE THE BLOCK HAD ANY FIELDS. That is the point of
/// these tests: a wire slot that arrives together with its first real payload
/// is a slot whose refusal path has never run, and it would first run on the
/// change it exists to police. Here it refuses an empty block that disagrees.
/// </para>
///
/// <para>
/// WHAT "REFUSED" CAN MEAN HERE. The client is already fully signed on
/// (<c>SIGNONSTATE_FULL</c>) by the time the engine calls the game DLL's
/// <c>ClientActive</c>, which is where the server sends this message. So by
/// the time the check arrives the client is already decoding entities with
/// the tables it built. The check is a REPORT, not a gate — the only place a
/// game DLL can refuse a connection outright is <c>ClientConnect</c>
/// (returning false with a reject reason), and that has run long before. Loud and
/// not fatal is therefore a description of what is available at this point,
/// not a softening of it; anything stronger has to be a kick after the fact.
/// </para>
/// </summary>
public class LayoutHandshakeTests
{
    private const int LongBytes = 4;

    //-------------------------------------------------------------------------
    // The value on the wire.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheBlockCarriedNowIsTheRealEngineBlock()
    {
        // PHASE 1a REPLACED A FACT THAT WAS WRITTEN TO FAIL HERE. Phase 0f
        // asserted the carried block had no fields, so that "the hash did not
        // move during Phase 0" was checked rather than assumed AND so that the
        // step which filled the block could not do it quietly. This is its
        // replacement: the slot now carries the block it was built for.
        Assert.Equal(typeof(EngineBlock), EngineBlockFingerprint.BlockType);

        FieldInfo[] fields = EngineBlockFingerprint.BlockType.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotEmpty(fields);
    }

    [Fact]
    public void TheBlockHashesToSomethingSpecific()
    {
        // Not the bare FNV offset basis: the field count and the marshalled
        // size are mixed before any field is, so even an empty block was a value
        // and not a "nothing was hashed" sentinel that a broken walk would also
        // produce.
        Assert.NotEqual(2166136261u, EngineBlockFingerprint.Current);
        Assert.Equal(EngineBlockFingerprint.Of(EngineBlockFingerprint.BlockType),
                     EngineBlockFingerprint.Current);
    }

    [Fact]
    public void AddingAFieldToTheBlockMovesTheHash()
    {
        // The property Phase 1a's gate depends on, asserted now on stand-in
        // declarations so that the mechanism is known to work before the real
        // block exists.
        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(NoFieldBlock)),
                        EngineBlockFingerprint.Of(typeof(OneFieldBlock)));
    }

    [Fact]
    public void ReorderingTheBlockMovesTheHash()
    {
        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(TwoFieldBlock)),
                        EngineBlockFingerprint.Of(typeof(TwoFieldBlockReordered)));
    }

    [Fact]
    public void RetypingAFieldMovesTheHash()
    {
        // Same name, same offset, same order — only the width changes. Caught
        // because the field's marshalled size is mixed.
        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(OneFieldBlock)),
                        EngineBlockFingerprint.Of(typeof(OneFieldBlockRetyped)));
    }

    //-------------------------------------------------------------------------
    // The reader, and the comparison.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheReaderTakesTheBlockFingerprintFromTheThirdLong()
    {
        var msg = Receive(0x11111111u, 7, 0x22222222u, registry: null);

        Assert.Equal(0x11111111u, msg.Fingerprint);
        Assert.Equal(7,           msg.LayoutCount);
        Assert.Equal(0x22222222u, msg.BlockFingerprint);
    }

    [Fact]
    public void AMatchingPairIsAccepted()
    {
        var reg = Discovered();

        var msg = Receive(reg.Fingerprint, reg.Count, reg.BlockFingerprint, reg);

        Assert.True(msg.Matched,
            "a client and server agreeing on both hashes must pass the join check");
    }

    [Fact]
    public void ADivergentBlockFingerprintIsRefused()
    {
        // THE FACT THIS STEP EXISTS FOR. The entity layouts agree exactly — the
        // count and the layout hash are the registry's own — and only the
        // engine block disagrees. Before the block hash was compared this
        // payload was accepted.
        var reg = Discovered();

        var msg = Receive(reg.Fingerprint, reg.Count, reg.BlockFingerprint ^ 1u, reg);

        Assert.False(msg.Matched,
            "the engine-block hash must be compared: the layouts agreed and only the block "
            + "differed, and that is exactly the divergence the second hash exists to refuse");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_protocol_list_the_server_declares_differently_is_refused(int list)
    {
        // plan_side_isolation §2.1: layouts agree exactly, and one of the three
        // key lists differs -- a declaration one side compiled and the other
        // did not.
        var reg  = Discovered();
        var mine = SharedManifest();
        var sent = new ProtocolManifest(mine.StringTables ^ (list == 0 ? 1u : 0u),
                                        mine.TempEnts     ^ (list == 1 ? 1u : 0u),
                                        mine.NetStates    ^ (list == 2 ? 1u : 0u));

        Assert.True(Receive(reg.Fingerprint, reg.Count, reg.BlockFingerprint, reg, mine, mine).Matched);
        Assert.False(Receive(reg.Fingerprint, reg.Count, reg.BlockFingerprint, reg, sent, mine).Matched);
        Assert.Single(mine.Differences(sent.StringTables, sent.TempEnts, sent.NetStates));
    }

    [Fact]
    public void ATruncatedMessageIsNotAccepted()
    {
        // Three longs, which is what the previous wire format was. A server
        // that still sends the old message must not be read as though the
        // protocol hashes happened to be whatever followed in the buffer.
        var reg = Discovered();

        Span<byte> buf = stackalloc byte[3 * LongBytes];
        var writer = new UserMessageWriter(buf);
        writer.WriteInt((int)reg.Fingerprint);
        writer.WriteInt(reg.Count);
        writer.WriteInt((int)reg.BlockFingerprint);

        var msg = new LayoutCheckMessage(reg, SharedManifest());
        ((IUserMessage)msg).OnReceived(new UserMessageReader(buf));

        Assert.False(msg.Matched);
    }

    [Fact]
    public void TheDeclaredSizeIsSixLongs()
    {
        // Size is a wire contract the engine validates a fixed-size message
        // against, not documentation.
        var attr = typeof(LayoutCheckMessage).GetCustomAttribute<UserMessageAttribute>()!;

        Assert.Equal(6 * LongBytes, attr.Size);
    }

    //-------------------------------------------------------------------------
    // The writer, AND IT IS MANAGED SINCE plan_shim.md §5 3e.
    //
    // It was `shell_usermessages.cpp`'s SourceSharp_SendLayoutCheck: three
    // WRITE_LONGs, and the comment over it said what was wrong with that --
    // "the one place client and server hand-agree on a byte layout". The three
    // ints go through UserMessageWriter now and come back through
    // UserMessageReader, which is one encoding agreed in one place.
    //
    // THE FACTS DID NOT GO, THEY MOVED. What each of them asserted is still a
    // claim worth making; only the file it is made about changed. The two
    // orders are still held against each other, which is the point: reversing
    // EITHER file has to fail, or a handshake parses and means something else.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheSenderFillsEachMemberFromItsSourceAndThePayloadFixesTheOrder()
    {
        // The writer and the reader are one generated pair now, so the order
        // they agree on is the payload's declaration, pinned here; what is left
        // to hold is that the sender puts each value in the member named for it.
        Assert.Equal(new Dictionary<string, string>
        {
            ["Fingerprint"]      = "_layouts.Fingerprint",
            ["LayoutCount"]      = "_layouts.Count",
            ["BlockFingerprint"] = "_layouts.BlockFingerprint",
            ["StringTablesHash"] = "_protocol.StringTables",
            ["TempEntsHash"]     = "_protocol.TempEnts",
            ["NetStatesHash"]    = "_protocol.NetStates",
        }, SenderAssignments(ManagedSenderBody()));

        Assert.Equal(new[] { "Fingerprint", "LayoutCount", "BlockFingerprint",
                             "StringTablesHash", "TempEntsHash", "NetStatesHash" }, PayloadMemberOrder());
    }

    [Fact]
    public void TheWriterTakesTheBlockFingerprintFromTheRegistry()
    {
        // It must READ the value the registry computed, not work one out. A
        // sender that derived the block's shape for itself would agree with
        // itself forever, which is the failure the hash exists to prevent.
        //
        // The registry's own BlockFingerprint comes from the C# declaration and
        // only from there, so this is the whole of the chain: declaration ->
        // registry -> writer -> wire.
        Assert.Contains("_layouts.BlockFingerprint", ManagedSenderBody());
    }

    [Fact]
    public void NoNativeSenderIsLeftToDisagreeWithIt()
    {
        // The half a repoint would otherwise lose. If SourceSharp_SendLayoutCheck
        // came back, the two senders would BOTH compile, both write twelve
        // bytes, and differ only in which one the server actually calls -- and
        // every fact above would go on passing about the one that does not.
        Assert.DoesNotContain("void SourceSharp_SendLayoutCheck", NativeSenderSource());

        // AND THE SLOT ITSELF IS GONE, which is a stronger claim than the
        // `engineApi.SendLayoutCheck = NULL;` this used to assert.
        //
        // `= NULL` with the field left in place was the retirement protocol
        // while other lanes were still editing the two slot tables: removing a
        // field from the middle renumbers every slot after it, and
        // AbiParityTests pins names IN ORDER. ABI 249 is the pass that removed
        // every marked field at once, so there is no field to null any more and
        // the assertion moves from "it points at nothing" to "there is nothing
        // to point".
        string fill = ReadSource(Path.Combine("src", "sourcesharp", "game", "shared",
                                              "sourcesharp_gamesystem.cpp"));
        Assert.DoesNotContain("engineApi.SendLayoutCheck", fill);
    }

    [Fact]
    public void TheDeclaredSizeMatchesWhatThePayloadWrites()
    {
        // THIS ONE IS LOAD-BEARING IN A WAY THAT LOOKS COSMETIC. The engine
        // compares a fixed-size user message's registered size against the
        // bytes written and DOES NOT SEND when they differ: no handshake,
        // silently, while every other fact here goes on passing.
        var attr = typeof(LayoutCheckMessage).GetCustomAttribute<UserMessageAttribute>()!;

        Assert.Equal(attr.Size, new LayoutCheck().Write(new byte[UserMessageSender.MaxPayloadBytes]));
    }

    [Fact]
    public void TheServerSendsItUnderTheNameTheClientHooks()
    {
        // The name is the OTHER half of the contract the size is half of. The
        // sender names the message by its TYPE (plan_side_isolation §2.2), so
        // its key is the declaration's own and a typo cannot compile; this
        // holds the sender to that form rather than a string literal.
        string body = ManagedSenderBody();

        Assert.Contains("_send.Send(new LayoutCheck", body);
        Assert.Equal(typeof(LayoutCheckMessage), HandlerOf<LayoutCheck>());
        Assert.Equal("SourceSharp:Layout", UserMessageKey.Of<LayoutCheckMessage>());

        // Reliable and aimed at the joining player. A handshake the network may
        // drop is a client that silently never checks its layouts, and a
        // broadcast one tells every player about one player's join.
        Assert.Contains("reliable: true", body);
        Assert.Contains("entIndex", body);
    }

    //-------------------------------------------------------------------------
    // Fixtures and helpers.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The fieldless stand-in Phase 0f's block actually was. Kept as a local
    /// fixture now that the real block has fields, because the property being
    /// asserted — that ADDING a field moves the hash — needs something with none
    /// to add to.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NoFieldBlock
    {
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct OneFieldBlock
    {
        public int Origin;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct OneFieldBlockRetyped
    {
        public long Origin;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TwoFieldBlock
    {
        public int Origin;
        public int Angles;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TwoFieldBlockReordered
    {
        public int Angles;
        public int Origin;
    }

    private static ProtocolManifest SharedManifest() => ProtocolManifest.Of(TestServices.ServerGame, new RecordingLog());

    private static EntityLayoutRegistry Discovered()
    {
        var reg = new EntityLayoutRegistry(new RecordingLog());
        reg.DiscoverAll(TestServices.ServerGame);
        return reg;
    }

    /// <summary>
    /// Builds the exact three longs the native sender writes and feeds them
    /// through the message's own reader.
    /// </summary>
    private static LayoutCheckMessage Receive(uint fingerprint, int count, uint blockFingerprint,
                                              EntityLayoutRegistry? registry,
                                              ProtocolManifest? sent = null, ProtocolManifest? mine = null)
    {
        sent ??= SharedManifest();

        var buf = new byte[6 * LongBytes];
        int written = new LayoutCheck
        {
            Fingerprint = fingerprint, LayoutCount = count, BlockFingerprint = blockFingerprint,
            StringTablesHash = sent.StringTables, TempEntsHash = sent.TempEnts, NetStatesHash = sent.NetStates,
        }.Write(buf);
        Assert.Equal(buf.Length, written);

        // The registry is the handler's own, handed to its constructor as the
        // client's container does. It was a static every fact had to restore.
        var msg = registry is null
            ? new LayoutCheckMessage()
            : new LayoutCheckMessage(registry, mine ?? SharedManifest());
        ((IUserMessage)msg).OnReceived(new UserMessageReader(buf));

        return msg;
    }

    // The native send half since plan_side_isolation P2: the file a native
    // sender would come back to.
    private static string NativeSenderSource()
        => ReadSource(Path.Combine("src", "sourcesharp", "game", "server",
                                   "shell_usermessage_send.cpp"));

    /// <summary>
    /// The body of <c>ServerModule.SendLayoutCheck</c> — the managed writer
    /// that replaced <c>SourceSharp_SendLayoutCheck</c> at
    /// <c>plan_shim.md</c> §5 3e.
    /// </summary>
    private static string ManagedSenderBody()
    {
        string source = ReadSource(Path.Combine("src", "sourcesharp", "managed",
                                                "SourceSharp.Server", "ServerModule.cs"));

        int start = source.IndexOf("private void SendLayoutCheck(int entIndex)",
                                   StringComparison.Ordinal);
        Assert.True(start >= 0,
            "ServerModule.SendLayoutCheck was not found. The join handshake's "
            + "payload is written there; if it moved, every fact in this file "
            + "about the writer is looking at nothing.");

        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "ServerModule.SendLayoutCheck's body did not close");

        return source.Substring(start, end - start);
    }

    private static Type HandlerOf<T>() where T : IUserMessagePayload<T> => T.Handler;

    /// <summary>Each member the sender's <c>new LayoutCheck { ... }</c> sets, and what it sets it from.</summary>
    private static Dictionary<string, string> SenderAssignments(string body)
    {
        int start = body.IndexOf("new LayoutCheck", StringComparison.Ordinal);
        Assert.True(start >= 0, "the sender does not build a LayoutCheck payload");
        int end = body.IndexOf('}', start);

        return Regex.Matches(body[start..end], @"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z_][A-Za-z0-9_.]*)\s*,")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    /// <summary>The LayoutCheck payload's members, in declaration order: the wire order.</summary>
    private static List<string> PayloadMemberOrder()
    {
        string source = ReadSource(Path.Combine("src", "sourcesharp", "managed",
                                                "SourceSharp.Shared", "UserMessages.cs"));

        int start = source.IndexOf("public partial struct LayoutCheck", StringComparison.Ordinal);
        Assert.True(start >= 0, "the LayoutCheck payload was not found");

        int end = source.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, "the LayoutCheck payload's declaration did not close");

        return Regex.Matches(source[start..end], @"public partial [A-Za-z0-9_]+ ([A-Za-z_][A-Za-z0-9_]*) \{")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private static string ReadSource(string relative)
    {
        string? path = RepoLayout.FindSdkFile(relative);
        Assert.True(path is not null,
            $"{relative} was not found in {RepoLayout.SdkTree}; a scan that reads "
            + "nothing would pass every check below for the wrong reason");
        return File.ReadAllText(path!);
    }
}
