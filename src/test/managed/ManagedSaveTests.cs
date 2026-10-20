extern alias client;
using System.Buffers.Binary;

using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;
using SourceSharp.Server.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// The ABI BOUNDARY of managed save/restore — <c>SaveCollectEntities</c> and
/// <c>SaveRestoreEntities</c>, docs/managed-saverestore.md.
///
/// <para>
/// <see cref="SaveRoundTripTests"/> owns the path itself: a live entity through
/// the real registry, through <see cref="SavedEntityCodec"/>'s bytes, into a
/// registry that has never seen it. What is left, and what is here, is the
/// SLOT CONTRACT — the part a caller in C++ sees and the part no round trip
/// exercises, because a round trip always passes a buffer that is exactly the
/// right size.
/// </para>
///
/// <para>
/// THREE THINGS, AND ALL THREE ARE ABOUT A NUMBER MEANING WHAT IT SAYS. Size
/// then fill: a short buffer is answered with the size and written into not at
/// all. 0 is "nothing to save", -1 is "this must not be loaded", and they are
/// not interchangeable — a shell that took -1 for 0 would write a valid save of
/// an empty world over a world that was refused. And both sides are wired,
/// because a slot declared on both sides of the ABI and assigned on neither is
/// a null pointer, a guarded call that skips, and a feature that silently does
/// nothing.
/// </para>
/// </summary>
public class ManagedSaveTests
{
    private static ScriptedEntityRegistry Registry()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ManagedSaveTests).Assembly);
        registry.Services = Services();
        return registry;
    }

    //-------------------------------------------------------------------------
    // Size then fill. sourcesharp.h states this of both slots by name, in the
    // words AnimListActivityNames states it in: the bytes the WHOLE block
    // needs, and nothing written unless every one of them fits.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The sizing call — what native's <c>(NULL, 0)</c> arrives as — answers the
    /// size of the whole block rather than 0 or a failure.
    /// </summary>
    [Fact]
    public void SizingWithAnEmptyBufferAnswersTheWholeBlocksBytes()
    {
        var registry = Registry();
        registry.KeyValue(new EntityHandle(0x0004_0401u), "reload_fixture", "speed", "4");

        var records = registry.Collect();

        Assert.Equal(SavedEntityCodec.ByteCount(records),
                     SavedEntityCodec.Write(records, Span<byte>.Empty, new RecordingLog()));
    }

    /// <summary>
    /// AND A SHORT BUFFER IS NOT PARTLY FILLED. One byte short of the whole
    /// block is the case that matters: a writer that filled what it could would
    /// leave a save holding every entity but the last, and nothing anywhere
    /// would say which one was lost.
    /// </summary>
    [Fact]
    public void AShortBufferIsSizedRatherThanPartlyFilled()
    {
        var registry = Registry();
        registry.KeyValue(new EntityHandle(0x0004_0402u), "reload_fixture", "speed", "4");

        var records = registry.Collect();
        int required = SavedEntityCodec.ByteCount(records);

        Assert.True(required > 1, "the block needs more than one byte");

        byte[] tooSmall = new byte[required - 1];

        Assert.Equal(required, SavedEntityCodec.Write(records, tooSmall, new RecordingLog()));
        Assert.True(Array.TrueForAll(tooSmall, b => b == 0),
                    "and NOTHING was written into the buffer that could not hold it");
    }

    //-------------------------------------------------------------------------
    // What the two slots answer, through the modules the thunks call.
    //-------------------------------------------------------------------------

    /// <summary>
    /// NOTHING TO SAVE IS 0, not the twelve bytes of an empty block's header.
    /// sourcesharp.h says 0 is what "there is nothing to save" looks like, and
    /// a shell that sized twelve bytes for every empty world would be writing a
    /// block whose only content is that there is none.
    /// </summary>
    [Fact]
    public void ServerSaveCollectAnswersZeroWithNoEntities()
    {
        Assert.Equal(0, new ServerModule(new ModuleEngine(TestSides.Create(Side.Server))).SaveCollect(Span<byte>.Empty));
    }

    /// <summary>The client's half of the same, over its own registry.</summary>
    [Fact]
    public void ClientSaveCollectAnswersZeroWithNoEntities()
    {
        Assert.Equal(0, new ClientModule(new client::SourceSharp.Abi.ModuleEngine(client::SourceSharp.Abi.TestSides.Create(Side.Client))).SaveCollect(Span<byte>.Empty));
    }

    /// <summary>
    /// A REFUSED BLOCK IS -1, AND -1 IS NOT "NOTHING WAS IN IT". The two
    /// answers send the caller in opposite directions: 0 is a save taken in a
    /// world with no managed entities, -1 is a save that must not be loaded.
    /// </summary>
    [Fact]
    public void ServerSaveRestoreRefusesGarbageWithMinusOne()
    {
        // Right length, wrong magic: a buffer that is not this format.
        byte[] garbage = { 0xDE, 0xAD, 0xBE, 0xEF, 1, 0, 0, 0, 1, 0, 0, 0 };

        Assert.Equal(-1, new ServerModule(new ModuleEngine(TestSides.Create(Side.Server))).SaveRestore(garbage));
    }

    /// <summary>The client refuses the same bytes the same way.</summary>
    [Fact]
    public void ClientSaveRestoreRefusesGarbageWithMinusOne()
    {
        byte[] garbage = { 0xDE, 0xAD, 0xBE, 0xEF, 1, 0, 0, 0, 1, 0, 0, 0 };

        Assert.Equal(-1, new ClientModule(new client::SourceSharp.Abi.ModuleEngine(client::SourceSharp.Abi.TestSides.Create(Side.Client))).SaveRestore(garbage));
    }

    /// <summary>
    /// A REFUSAL RESTORES NOTHING, and this is the fact that says so of the
    /// SLOT rather than of the codec. The block is a valid TWO-entity block
    /// with its last byte cut off — the case a reader that restored as it read
    /// would answer with one entity and no complaint.
    /// </summary>
    [Fact]
    public void ATruncatedBlockRefusedAtTheSlotRestoresNoEntityAtAll()
    {
        var source = Registry();
        source.KeyValue(new EntityHandle(0x0004_0403u), "reload_fixture", "speed", "1");
        source.KeyValue(new EntityHandle(0x0004_0404u), "reload_fixture", "speed", "2");

        var records = source.Collect();
        byte[] whole = new byte[SavedEntityCodec.ByteCount(records)];
        SavedEntityCodec.Write(records, whole, new RecordingLog());

        // RESTAMPED, or the checksum refuses this at the header and the slot
        // never reaches the per-record refusal this fact is named for.
        byte[] cut = CutAndRestamp(whole, 1);

        Assert.Equal(-1, new ServerModule(new ModuleEngine(TestSides.Create(Side.Server))).SaveRestore(cut));

        // AND THE REGISTRY IS NEVER REACHED, which is what makes -1 mean
        // "nothing was restored" rather than "the caller should tidy up".
        // ASSERTED RATHER THAN ASSUMED: the module passes the reader's out-list
        // straight through, so "the reader hands back an empty list on a
        // refusal" is load-bearing here and is checked rather than trusted.
        Assert.False(SavedEntityCodec.TryRead(cut, out var decoded, out string? refusal));
        Assert.NotNull(refusal);
        Assert.DoesNotContain("checksum", refusal);
        Assert.Empty(decoded);

        var loaded = Registry();
        Assert.Equal(0, loaded.RestoreFrom(decoded));
        Assert.Null(loaded.Get(new EntityHandle(0x0004_0403u)));
    }

    /// <summary>
    /// AN EMPTY BLOCK IS NOT A REFUSAL. It is what <c>SaveCollect</c> answered
    /// 0 for, and a load that refused it would fail every save taken before the
    /// first scripted entity spawned — an ordinary map, refused.
    /// </summary>
    [Fact]
    public void AnEmptyBlockRestoresNothingWithoutRefusing()
    {
        Assert.Equal(0, new ServerModule(new ModuleEngine(TestSides.Create(Side.Server))).SaveRestore(ReadOnlySpan<byte>.Empty));
        Assert.Equal(0, new ClientModule(new client::SourceSharp.Abi.ModuleEngine(client::SourceSharp.Abi.TestSides.Create(Side.Client))).SaveRestore(ReadOnlySpan<byte>.Empty));
    }

    //-------------------------------------------------------------------------
    // Both sides are wired.
    //
    // A slot declared in sourcesharp.h and Abi.cs and assigned in neither fill
    // site passes every parity check there is -- the names agree, the order
    // agrees, the types agree -- and is a null pointer at run time. The shell
    // guards it, so the save simply does nothing and nothing is logged. This is
    // the same failure AbiParityTests' wiring scan exists for, asked of the two
    // slots this lane added and of BOTH sides, because a pair wired only on the
    // server is a listen server whose client entities are saved by nobody.
    //-------------------------------------------------------------------------

    [Fact]
    public void BothSlotsAreAssignedOnBothSides()
    {
        // REQUIRED, NOT OPTIONAL, and that is a deliberate difference from
        // AbiParityTests' own scan. `Assert.True(true)` on a missing file
        // reports PASSED rather than SKIPPED, so a wiring check that could not
        // find the load file would be a wiring check that always passes -- which is
        // exactly the failure this fact exists to catch, one level up.
        // The side's load, where a thunk would be wired (plan_side_entries SE2
        // moved it out of the Bootstrap's Entry.cs).
        string entry = RepoLayout.FindSdkFile(Path.Combine("sourcesharp", "managed",
                                                 "SourceSharp.Shared.Interop", "SideLoad.cs"))
            ?? throw new FileNotFoundException(
                "SideLoad.cs was not found in " + RepoLayout.SdkTree);

        string text = File.ReadAllText(entry);

        // THE SERVER'S MANAGED-BUILT AREA since plan_engineapi_vtables E4
        // (SaveRestoreBinding): its slots are methods of the binding, which the
        // generated table cannot leave unfilled. The client's pair had no
        // native caller and was dropped at plan_client_vtables CE7's merge, so
        // its binding declares neither; nothing fills the retired struct slot.
        string server = File.ReadAllText(RepoLayout.Repo("src", "server", "interop", "SaveRestoreBinding.cs"));
        string client = File.ReadAllText(RepoLayout.Repo("src", "client", "interop", "SaveRestoreClientBinding.cs"));
        // plan_server_native_v2 T4c: the body block's two slots (the entity
        // block's SaveCollectEntities/SaveRestoreEntities retired with it).
        foreach (string slot in new[] { "SaveWriteBody", "SaveReadBody" })
        {
            Assert.True(Regex.IsMatch(server, @"public int " + slot + @"\("), slot + " is a method of SaveRestoreBinding.cs");
            Assert.False(Regex.IsMatch(client, @"public int " + slot + @"\("), slot + " is not a method of SaveRestoreClientBinding.cs");
        }
        Assert.Contains("public int ReloadSave()", client);
        foreach (string thunk in new[] { "ServerSaveCollect", "ClientSaveCollect", "ClientSaveRestore" })
            Assert.False(text.Contains("= &" + thunk + ";", StringComparison.Ordinal),
                         thunk + " no longer fills the retired struct slot");
    }

    /// <summary>
    /// A block cut short, with its checksum RESTAMPED over what is left.
    ///
    /// <para>
    /// The restamp is the whole point. Version 2 put a checksum in the header,
    /// and a plain truncation now fails THAT — at the header, before the reader
    /// ever reaches the per-record path this fact is about. The fact went on
    /// passing, for a reason that had nothing to do with its subject. Measured
    /// by the codec's own mutation harness, which found this fact staying green
    /// with the guard it exists to protect deleted.
    /// </para>
    ///
    /// <para>
    /// So: cut, then make the checksum honest about the cut. What is left is a
    /// block whose integrity check AGREES and whose contents are still short —
    /// which is the only input that exercises a refusal past the header.
    /// </para>
    /// </summary>
    private static byte[] CutAndRestamp(byte[] block, int drop)
    {
        byte[] cut = block.AsSpan(0, block.Length - drop).ToArray();

        BinaryPrimitives.WriteUInt32LittleEndian(
            cut.AsSpan(SavedEntityCodec.ChecksumAt),
            SavedEntityCodec.Checksum(cut.AsSpan(SavedEntityCodec.ChecksumFrom)));

        return cut;
    }
}
