using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

using static SourceSharp.Tests.TePropPlannerTests;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ServerTempEntEngine"/> against what shell_tempents.cpp's
/// SourceSharp_TempEntRegister and SourceSharp_TempEntPlayback did before
/// plan_server_native_v2 T4b moved their decisions here: the refusals, the
/// reload rule, the collapsible warning, the short-blob and no-class refusals,
/// the delay clamp, and the filter kinds. Native is a double that records the
/// blocks it is handed; the send is a double that records what it is asked.
/// </summary>
public sealed class ServerTempEntEngineTests
{
    /// <summary>The native store, recording each block and answering the next handle.</summary>
    private sealed class Tables : ITempEntTables
    {
        public readonly List<byte[]> Blocks = [];
        public Func<int, TempEntTable?>? Answer;

        // The class id's address is only ever compared with 0 here: the send
        // double never reads through it.
        public TempEntTable? Build(ReadOnlySpan<byte> block)
        {
            Blocks.Add(block.ToArray());
            int handle = Blocks.Count - 1;

            if (Answer is { } answer)
                return answer(handle);

            Read(block, out TeTableHeader header, out _);
            return new TempEntTable(handle, false, header.ShapeHash, 0x1000 + handle,
                                    header.RegisterClass != 0 ? 0x5000 + handle : 0);
        }
    }

    private sealed class Sender : ITempEntSender
    {
        public readonly List<(int[] Recipients, float Delay, byte[] Blob, int Handle)> Sent = [];

        public void Playback(IRecipientList recipients, float delay, ReadOnlySpan<byte> blob, in TempEntTable table)
            => Sent.Add((Enumerable.Range(0, recipients.Count).Select(recipients.IndexAt).ToArray(), delay,
                         blob.ToArray(), table.Handle));
    }

    private sealed class Recipients : ITempEntRecipients
    {
        public readonly List<TeFilter> Asked = [];

        public IRecipientList For(TeFilter filter)
        {
            Asked.Add(filter);
            var list = new RecipientFilter(8);
            list.Add(3);
            return list;
        }
    }

    private readonly RecordingLog _log = new();
    private readonly Tables _tables = new();

    private ServerTempEntEngine Engine() => new(new RecordingEngine(), _tables, _log);

    /// <summary>A block's header and rows, as native reads it.</summary>
    internal static void Read(ReadOnlySpan<byte> block, out TeTableHeader header, out SendPropRowDesc[] rows)
    {
        ReadOnlySpan<byte> payload = BitBlock.Payload(block)[..(BitBlock.Bits(block) / 8)];
        header = MemoryMarshal.Read<TeTableHeader>(payload);
        rows = MemoryMarshal.Cast<byte, SendPropRowDesc>(
            payload.Slice(Unsafe.SizeOf<TeTableHeader>(), header.Props * Unsafe.SizeOf<SendPropRowDesc>())).ToArray();
    }

    internal static string NameAt(ReadOnlySpan<byte> block, int offset)
    {
        ReadOnlySpan<byte> payload = BitBlock.Payload(block);
        int end = payload[offset..].IndexOf((byte)0);
        return System.Text.Encoding.UTF8.GetString(payload.Slice(offset, end));
    }

    private static TeClass ParticleSystem()
        => C("CTEParticleSystem", "DT_TEParticleSystem", null, 12,
             F("m_vecOrigin", TePropKind.Float, 0, 4, -1, TeFlags.Coord | TeFlags.VectorElem, elems: 3, stride: 4))
           with { RegistersClass = false };

    private static TeClass Sparks()
        => C("CTESparks", "DT_TESparks", "DT_TEParticleSystem", 32,
             F("m_nMagnitude", TePropKind.Int, 12, 4, 4, TeFlags.Unsigned),
             F("m_nTrailLength", TePropKind.Int, 16, 4, 4, TeFlags.Unsigned),
             F("m_vecDir", TePropKind.Vector, 20, 12, -1, TeFlags.Coord));

    [Fact]
    public void ADeclarationIsPlannedIntoOneBlockOfStoredRows()
    {
        ServerTempEntEngine engine = Engine();

        Assert.Equal(0, engine.TempEntRegister(ParticleSystem()));
        Assert.Equal(1, engine.TempEntRegister(Sparks()));
        Assert.Equal(2, _tables.Blocks.Count);

        Read(_tables.Blocks[1], out TeTableHeader header, out SendPropRowDesc[] rows);

        Assert.Equal(("CTESparks", "DT_TESparks", 1, 4),
                     (NameAt(_tables.Blocks[1], header.ClassName), NameAt(_tables.Blocks[1], header.TableName),
                      header.RegisterClass, header.Props));
        Assert.Equal(TeSchemaRegistry.ShapeHash(Sparks()), header.ShapeHash);

        // The base, nested by the handle native gave it, collapsible at zero.
        Assert.Equal(("baseclass", (int)SendPropType.DataTable, 0, SendPropPlan.Collapsible, (int)SendProxyId.DataTableToDataTable),
                     (NameAt(_tables.Blocks[1], rows[0].Name), rows[0].Type, rows[0].DataTable, rows[0].Flags, rows[0].Proxy));
        Assert.Equal(("m_nMagnitude", 12, 4, (int)SendProxyId.UInt32ToInt32),
                     (NameAt(_tables.Blocks[1], rows[1].Name), rows[1].Offset, rows[1].Bits, rows[1].Proxy));
        Assert.Equal(("m_vecDir", (int)SendPropType.Vector, SendPropPlan.Coord, 0, 0x4f000000u),
                     (NameAt(_tables.Blocks[1], rows[3].Name), rows[3].Type, rows[3].Flags, rows[3].Bits,
                      BitConverter.SingleToUInt32Bits(rows[3].HighLowMul)));

        Read(_tables.Blocks[0], out TeTableHeader baseHeader, out SendPropRowDesc[] baseRows);
        Assert.Equal((0, 3), (baseHeader.RegisterClass, baseHeader.Props));
        Assert.Equal(("m_vecOrigin[1]", -4), (NameAt(_tables.Blocks[0], baseRows[1].Name), baseRows[1].Offset));

        Assert.Contains("temp entity  1 'CTESparks' -> 'DT_TESparks': 4 prop(s), 32 blob byte(s)", _log.Text);
        Assert.Contains("worst case 214 bit(s), 27 byte(s) of 192", _log.Text);
    }

    [Fact]
    public void ASchemaRefusalBuildsNothingAndSaysWhy()
    {
        ServerTempEntEngine engine = Engine();

        Assert.Equal(0, engine.TempEntRegister(ParticleSystem()));
        Assert.Equal(-1, engine.TempEntRegister(ParticleSystem() with { TableName = "DT_Other" }));
        Assert.Single(_tables.Blocks);
        Assert.Contains("temp entity 'CTEParticleSystem' (DT_Other) NOT registered: a class of that name is already registered",
                        _log.Text);
    }

    [Fact]
    public void ANativeRefusalIsReportedAndNestsNothing()
    {
        ServerTempEntEngine engine = Engine();
        _tables.Answer = _ => null;

        Assert.Equal(-1, engine.TempEntRegister(ParticleSystem()));
        Assert.Contains("'CTEParticleSystem': native refused to store its table", _log.Text);

        // The schema took the base, native did not: the derived table has
        // nothing to nest and is refused before a block is built for it.
        _tables.Answer = null;
        Assert.Equal(-1, engine.TempEntRegister(Sparks()));
        Assert.Single(_tables.Blocks);
        Assert.Contains("prop 'baseclass' nests 'DT_TEParticleSystem', which no table built here carries", _log.Text);
    }

    [Fact]
    public void AReloadGetsItsHandleBackAndAReshapeIsSaid()
    {
        uint same = TeSchemaRegistry.ShapeHash(ParticleSystem());

        ServerTempEntEngine kept = Engine();
        _tables.Answer = h => new TempEntTable(7, true, same, 0x2000, 0);
        Assert.Equal(7, kept.TempEntRegister(ParticleSystem()));
        Assert.DoesNotContain("RESHAPED", _log.Text);
        Assert.Contains("(built by an earlier load)", _log.Text);

        ServerTempEntEngine reshaped = Engine();
        _tables.Answer = h => new TempEntTable(7, true, same ^ 1u, 0x2000, 0);
        Assert.Equal(7, reshaped.TempEntRegister(ParticleSystem()));
        Assert.Contains("'CTEParticleSystem' was RESHAPED across a reload", _log.Text);
    }

    [Fact]
    public void TheShapeHashIsTheTableTheBlobAndTheFieldCount()
    {
        uint hash = TeSchemaRegistry.ShapeHash(Sparks());

        Assert.Equal(hash, TeSchemaRegistry.ShapeHash(Sparks() with { ClassName = "CTEOther" }));
        Assert.NotEqual(hash, TeSchemaRegistry.ShapeHash(Sparks() with { TableName = "DT_TEOther" }));
        Assert.NotEqual(hash, TeSchemaRegistry.ShapeHash(Sparks() with { BlobBytes = 36 }));
        Assert.NotEqual(hash, TeSchemaRegistry.ShapeHash(Sparks() with { Fields = Sparks().Fields[..2] }));
    }

    [Fact]
    public void ANestedTableAtOffsetZeroIsSaidOutLoud()
    {
        ServerTempEntEngine engine = Engine();
        TeClass data = C("CEffectData", "DT_EffectData", null, 8, F("m_nA", TePropKind.Int, 0, 4, 8, TeFlags.None))
                       with { RegistersClass = false };

        Assert.Equal(0, engine.TempEntRegister(data));
        Assert.Equal(1, engine.TempEntRegister(C("CTEPadded", "DT_TEPadded", null, 12,
            F("m_Data", TePropKind.DataTable, 4, 0, 0, TeFlags.None, subTable: "DT_EffectData"))));
        Assert.DoesNotContain("SPROP_COLLAPSIBLE", _log.Text);

        Assert.Equal(2, engine.TempEntRegister(C("CTEUnpadded", "DT_TEUnpadded", null, 8,
            F("m_Data", TePropKind.DataTable, 0, 0, 0, TeFlags.None, subTable: "DT_EffectData"))));
        Assert.Contains("'CTEUnpadded': nested table prop 'm_Data' came out SPROP_COLLAPSIBLE (offset 0)", _log.Text);
    }

    private (ServerTempEntEngine Engine, Sender Sender, Recipients Recipients) Attached()
    {
        ServerTempEntEngine engine = Engine();
        Assert.Equal(0, engine.TempEntRegister(ParticleSystem()));
        Assert.Equal(1, engine.TempEntRegister(Sparks()));

        var sender = new Sender();
        var recipients = new Recipients();
        engine.Attach(sender, recipients);
        return (engine, sender, recipients);
    }

    [Fact]
    public void APlaybackGoesToTheFiltersRecipientsThroughItsTable()
    {
        var (engine, sender, recipients) = Attached();
        byte[] blob = new byte[32];
        blob[12] = 7;

        engine.TempEntPlayback(1, blob, TeFilters.Pas(new Vector(1, 2, 3)), 0.25f);

        var sent = Assert.Single(sender.Sent);
        Assert.Equal([3], sent.Recipients);
        Assert.Equal((0.25f, 1), (sent.Delay, sent.Handle));
        Assert.Equal(blob, sent.Blob);
        Assert.Equal(TeFilterKind.Pas, Assert.Single(recipients.Asked).Kind);
    }

    [Fact]
    public void AShortBlobIsRefusedNotTruncated()
    {
        var (engine, sender, _) = Attached();

        engine.TempEntPlayback(1, new byte[31], TeFilters.Broadcast, 0f);

        Assert.Empty(sender.Sent);
        Assert.Contains("'CTESparks': 31 byte(s) offered for a 32 byte blob; NOT sent", _log.Text);
    }

    [Fact]
    public void ATableWithNoServerClassCannotBePlayed()
    {
        var (engine, sender, _) = Attached();

        engine.TempEntPlayback(0, new byte[12], TeFilters.Broadcast, 0f);

        Assert.Empty(sender.Sent);
        Assert.Contains("'CTEParticleSystem' has no ServerClass of its own and cannot be played back", _log.Text);
    }

    [Fact]
    public void AnUnknownHandleOrAnEmptyBlobSendsNothingSilently()
    {
        var (engine, sender, _) = Attached();
        int lines = _log.Lines.Count;

        engine.TempEntPlayback(9, new byte[32], TeFilters.Broadcast, 0f);
        engine.TempEntPlayback(-1, new byte[32], TeFilters.Broadcast, 0f);
        engine.TempEntPlayback(1, ReadOnlySpan<byte>.Empty, TeFilters.Broadcast, 0f);

        Assert.Empty(sender.Sent);
        Assert.Equal(lines, _log.Lines.Count);
    }

    [Theory]
    [InlineData(1.5f, 1f)]
    [InlineData(-3f, -1f)]
    public void ADelayOutsideStocksBoundIsClampedAndSaid(float asked, float sent)
    {
        var (engine, sender, _) = Attached();

        engine.TempEntPlayback(1, new byte[32], TeFilters.Broadcast, asked);

        Assert.Equal(sent, Assert.Single(sender.Sent).Delay);
        Assert.Contains("is outside stock's [-1,1]; clamped", _log.Text);
    }

    [Fact]
    public void BeforeTheSenderIsAttachedNothingIsSentAndThatIsSaidOnce()
    {
        ServerTempEntEngine engine = Engine();
        Assert.Equal(0, engine.TempEntRegister(ParticleSystem()));
        Assert.Equal(1, engine.TempEntRegister(Sparks()));

        engine.TempEntPlayback(1, new byte[32], TeFilters.Broadcast, 0f);
        engine.TempEntPlayback(1, new byte[32], TeFilters.Broadcast, 0f);

        Assert.Single(_log.Lines, l => l.Text.Contains("no sender yet"));
    }

    private sealed class Names(int answer) : IEffectDispatchNames
    {
        public readonly List<string> Asked = [];

        public int IndexOf(string name)
        {
            Asked.Add(name);
            return answer;
        }
    }

    [Fact]
    public void AnEffectNameIsTheStringTablesIndexAndARefusalIsZero()
    {
        var names = new Names(42);
        var engine = new ServerTempEntEngine(new RecordingEngine(), _tables, _log, names);

        Assert.Equal(42, engine.EffectDispatchIndex("Impact"));
        Assert.Equal(0, engine.EffectDispatchIndex(""));
        Assert.Equal(["Impact"], names.Asked);

        Assert.Equal(0, new ServerTempEntEngine(new RecordingEngine(), _tables, _log, new Names(-1)).EffectDispatchIndex("Impact"));
        Assert.Equal(0, Engine().EffectDispatchIndex("Impact"));
    }
}

/// <summary>
/// <see cref="TempEntRecipients"/>: the filter class shell_tempents.cpp's
/// PlaybackThrough built for each kind, over the server's recipient rules.
/// </summary>
public sealed class TempEntRecipientsTests
{
    private readonly SoundEmitterTests.FakeEngine _engine = new();
    private readonly SoundEmitterTests.Listeners _listeners = new();

    private TempEntRecipients Recipients() => new(new SoundRecipients(_engine, _listeners));

    private static int[] Slots(IRecipientList list) => Enumerable.Range(0, list.Count).Select(list.IndexAt).ToArray();

    public TempEntRecipientsTests()
    {
        _listeners.Ears[1] = new Vector(0, 0, 0);
        _listeners.Ears[2] = new Vector(5000, 0, 0);
        _engine.PasBits[0] = 0b11;   // slots 1 and 2
    }

    [Fact]
    public void PvsAndPasAskTheEngineWhichSetTheyMean()
    {
        Assert.Equal([1, 2], Slots(Recipients().For(TeFilters.Pvs(Vector.Zero))));
        Assert.Equal([1, 2], Slots(Recipients().For(TeFilters.Pas(Vector.Zero))));
        Assert.Equal(["Multicast False", "Multicast True"], _engine.Asked);
    }

    [Fact]
    public void AttenuationCropsByDistanceAndDefaultsToAttnNorm()
    {
        // 2 * 1000 / 0.8 = 2500 units: slot 2's ears are 5000 away.
        Assert.Equal([1], Slots(Recipients().For(TeFilters.PasAttenuation(Vector.Zero))));
        // 2 * 1000 / 0.3 = 6666: both hear it.
        Assert.Equal([1, 2], Slots(Recipients().For(TeFilters.PasAttenuation(Vector.Zero, 0.3f))));
    }

    [Fact]
    public void OnePlayerOrEveryone()
    {
        Assert.Equal([2], Slots(Recipients().For(TeFilters.SinglePlayer(2))));
        Assert.Empty(Slots(Recipients().For(TeFilters.SinglePlayer(99))));
        Assert.Equal([1, 2], Slots(Recipients().For(TeFilters.Broadcast)));
        Assert.Empty(_engine.Asked);
    }
}
