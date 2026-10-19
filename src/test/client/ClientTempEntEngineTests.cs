extern alias client;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client.Interop;
using client::SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ClientTempEntEngine"/> and <see cref="RecvPropPlan"/> against what
/// shell_tempents_client.cpp and tempent_schema.h decided before the client's
/// temp entity planning moved to C#.
///
/// <para>
/// THE PLANS ARE HELD TO A RECORDING, not to themselves. The fixture
/// (<c>Fixtures/tempents-client-plan-native.tsv</c>) is the live client at
/// ab8f56ef2, where tempent_schema.h still planned every table: cs_cl_te_dump's
/// TE| line for each of the 255 RecvProps the engine held, and the registration
/// line for each of the 45 tables. Every client temp entity is registered here
/// through the managed engine and every field is compared, so a name, a type, an
/// offset, a flag, a width, a nested table, a handle or a worst case that moved
/// fails on the table it moved in.
/// </para>
///
/// <para>
/// AND THE THREE SILENT FAILURES THE HEADER EXISTED FOR have a fact each: an
/// offset outside the blob, a width that is not stock's, and a worst case over
/// the engine's 192-byte event cap are refused before native is asked to build
/// anything.
/// </para>
/// </summary>
public sealed class ClientTempEntEngineTests
{
    /// <summary>The native store: records each block and answers the next handle.</summary>
    private sealed class Tables : ITempEntRecvTables
    {
        public readonly List<byte[]> Blocks = [];
        public readonly List<int> BlobBytes = [];
        public Func<int, TempEntRecvTable?>? Answer;

        public TempEntRecvTable? Build(ReadOnlySpan<byte> block, int blobBytes)
        {
            Blocks.Add(block.ToArray());
            BlobBytes.Add(blobBytes);
            int handle = Blocks.Count - 1;

            if (Answer is { } answer)
                return answer(handle);

            Read(block, out TeTableHeader header, out _);
            return new TempEntRecvTable(handle, false, header.ShapeHash);
        }
    }

    /// <summary>Records every description on its way to the engine.</summary>
    private sealed class Recording(ITempEntRegistrar inner) : ITempEntRegistrar
    {
        public readonly List<TeClass> Seen = [];

        public int TempEntRegister(TeClass description)
        {
            Seen.Add(description);
            return inner.TempEntRegister(description);
        }
    }

    private readonly RecordingLog _log = new();
    private readonly Tables _tables = new();

    private ClientTempEntEngine Engine() => new(_tables, _log);

    internal static void Read(ReadOnlySpan<byte> block, out TeTableHeader header, out RecvPropRowDesc[] rows)
    {
        ReadOnlySpan<byte> payload = BitBlock.Payload(block)[..(BitBlock.Bits(block) / 8)];
        header = MemoryMarshal.Read<TeTableHeader>(payload);
        rows = MemoryMarshal.Cast<byte, RecvPropRowDesc>(
            payload.Slice(Unsafe.SizeOf<TeTableHeader>(), header.Props * Unsafe.SizeOf<RecvPropRowDesc>())).ToArray();
    }

    internal static string NameAt(ReadOnlySpan<byte> block, int offset)
    {
        ReadOnlySpan<byte> payload = BitBlock.Payload(block);
        int end = payload[offset..].IndexOf((byte)0);
        return System.Text.Encoding.UTF8.GetString(payload.Slice(offset, end));
    }

    private static TeField Field(string name, TePropKind kind, int offset, int size, int bits = -1,
                                 TeFlags flags = TeFlags.None, string? sub = null, int elems = 0, int stride = 0,
                                 TeBits from = TeBits.Literal)
        => new(name, kind, offset, size, bits, from, flags, 0f, 0f, sub, elems, stride);

    private static TeClass Class(string name, string table, int blob, TeField[] fields, string? baseTable = null,
                                 bool registers = true)
        => new(name, table, baseTable, blob, registers, typeof(object), fields);

    private static TeClass Splash() => Class("CTEEnergySplash", "DT_TEEnergySplash", 28,
    [
        Field("m_vecPos", TePropKind.Vector, 0, 12, flags: TeFlags.Coord),
        Field("m_vecDir", TePropKind.Vector, 12, 12, flags: TeFlags.Coord),
        Field("m_bExplosive", TePropKind.Int, 24, 4, 1, TeFlags.Unsigned),
    ]);

    //=========================================================================//
    // THE RECORDING: every client temp entity, field for field.
    //=========================================================================//

    private sealed record TeLine(string Table, int Index, string Name, string Type, int EngineOffset, int PlanOffset,
                                 int Bits, int Flags, string Nested);

    private sealed record RegLine(int Handle, string Class, string Table, int Props, int Blob, string Base, int Worst);

    private static (List<RegLine> Reg, List<TeLine> Props) Fixture()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tempents-client-plan-native.tsv");
        Assert.True(File.Exists(path), "missing: " + path);

        var reg = new List<RegLine>();
        var props = new List<TeLine>();

        foreach (string line in File.ReadLines(path))
        {
            string[] f = line.Split('|');

            if (f[0] == "REG")
                reg.Add(new RegLine(int.Parse(f[1]), f[2], f[3], int.Parse(f[4]), int.Parse(f[5]), f[6], int.Parse(f[7])));
            else if (f[0] == "TE")
                props.Add(new TeLine(f[1], int.Parse(f[2]), f[3], f[4], int.Parse(f[5]), int.Parse(f[6]),
                                     int.Parse(f[7]), Convert.ToInt32(f[8], 16), f[9]));
        }

        return (reg, props);
    }

    private static string TypeName(int type) => (SendPropType)type switch
    {
        SendPropType.Int       => "int",
        SendPropType.Float     => "float",
        SendPropType.Vector    => "vector",
        SendPropType.Array     => "array",
        SendPropType.DataTable => "datatable",
        _                      => "?",
    };

    private (Recording Seen, int Registered) RegisterEveryClientTempEntity()
    {
        var recording = new Recording(Engine());
        var log = new RecordingLog();
        var registry = new TempEntRegistry(log);

        // What the game loads: every mod assembly's declarations, and not the
        // probes this test assembly declares for its own facts.
        Type[] types = LoadedAssemblies.Types(TestServices.ClientGame, log)
            .Where(t => t.Assembly != typeof(ClientTempEntEngineTests).Assembly
                        && t.GetCustomAttributes(typeof(TempEntityAttribute), false).Length > 0)
            .ToArray();

        int registered = registry.Register("Client", types, recording);
        return (recording, registered);
    }

    [Fact]
    public void TheRecordingHasEveryTableAndEveryProp()
    {
        var (reg, props) = Fixture();

        // The stimulus has to reach the subject: a fixture cut short would
        // compare nothing and pass.
        Assert.Equal(45, reg.Count);
        Assert.Equal(255, props.Count);
        Assert.Equal(reg.Sum(r => r.Props), props.Count);
    }

    [Fact]
    public void EveryClientTempEntityIsBuiltInTheRecordedOrder()
    {
        var (reg, _) = Fixture();
        var (seen, registered) = RegisterEveryClientTempEntity();

        Assert.Equal(reg.Count, registered);
        Assert.Equal(reg.Count, _tables.Blocks.Count);

        for (int i = 0; i < reg.Count; i++)
        {
            Read(_tables.Blocks[i], out TeTableHeader header, out _);

            Assert.Equal((reg[i].Class, reg[i].Table, reg[i].Props),
                         (NameAt(_tables.Blocks[i], header.ClassName), NameAt(_tables.Blocks[i], header.TableName),
                          header.Props));
            Assert.Equal(reg[i].Blob, seen.Seen[i].BlobBytes);
            Assert.Equal(reg[i].Blob, _tables.BlobBytes[i]);
            Assert.Equal(reg[i].Base, seen.Seen[i].BaseTable ?? "(none)");
        }
    }

    [Fact]
    public void EveryRecvPropIsTheOneTheNativeHeaderBuilt()
    {
        var (reg, props) = Fixture();
        RegisterEveryClientTempEntity();

        int compared = 0;

        foreach (RegLine table in reg)
        {
            byte[] block = _tables.Blocks[table.Handle];
            Read(block, out _, out RecvPropRowDesc[] rows);
            TeLine[] want = props.Where(p => p.Table == table.Table).OrderBy(p => p.Index).ToArray();

            Assert.Equal(want.Length, rows.Length);

            for (int i = 0; i < rows.Length; i++)
            {
                RecvPropRowDesc row = rows[i];
                string nested = row.DataTable >= 0 ? reg[row.DataTable].Table : "";

                Assert.True((want[i].Name, want[i].Type, want[i].EngineOffset, want[i].Flags, want[i].Nested)
                            == (NameAt(block, row.Name), TypeName(row.Type), row.Offset, row.Flags, nested),
                            $"{table.Table} prop {i}: recorded {want[i]}, planned {NameAt(block, row.Name)} "
                            + $"{TypeName(row.Type)} off {row.Offset} flags 0x{row.Flags:x} nested '{nested}'");
                compared++;
            }
        }

        Assert.Equal(255, compared);
    }

    [Fact]
    public void EveryPlannedWidthAndOffsetIsTheRecordedOne()
    {
        var (reg, props) = Fixture();
        var (seen, _) = RegisterEveryClientTempEntity();

        int compared = 0;

        for (int t = 0; t < reg.Count; t++)
        {
            IReadOnlyList<TePropPlan> plan = TePropPlanner.Plan(seen.Seen[t])!;
            TeLine[] want = props.Where(p => p.Table == reg[t].Table).OrderBy(p => p.Index).ToArray();

            Assert.Equal(want.Length, plan.Count);

            for (int i = 0; i < plan.Count; i++)
            {
                Assert.True((want[i].PlanOffset, want[i].Bits) == (plan[i].Offset, plan[i].Bits),
                            $"{reg[t].Table} prop {i} ({want[i].Name}): recorded offset {want[i].PlanOffset} bits "
                            + $"{want[i].Bits}, planned {plan[i].Offset} / {plan[i].Bits}");
                compared++;
            }
        }

        Assert.Equal(255, compared);
    }

    [Fact]
    public void EveryRegistrationLineIsTheRecordedOne()
    {
        var (reg, _) = Fixture();
        RegisterEveryClientTempEntity();

        var said = new List<string>();
        foreach (var (text, warning) in _log.Lines)
        {
            Assert.False(warning, "a client temp entity was refused: " + text);
            said.Add(text);
        }

        foreach (RegLine r in reg)
        {
            string head = $"[SourceSharp] temp entity {r.Handle,2} '{r.Class}' -> '{r.Table}': {r.Props} prop(s), "
                          + $"{r.Blob} blob byte(s), base '{r.Base}', worst case \n";
            string price = $"[SourceSharp]     {r.Worst} bit(s), {(r.Worst + 7) / 8} byte(s) of 192\n";
            int at = said.IndexOf(head);

            Assert.True(at >= 0, "the client said: " + head.TrimEnd());
            Assert.Equal(price, said[at + 1]);
        }
    }

    //=========================================================================//
    // THE THREE SILENT FAILURES.
    //=========================================================================//

    [Fact]
    public void AFieldPastTheEndOfTheBlobIsRefusedBeforeNativeIsAsked()
    {
        ClientTempEntEngine engine = Engine();

        // The positive arm first: the same class with its blob intact is built.
        Assert.Equal(0, engine.TempEntRegister(Splash()));

        TeClass past = Splash() with { ClassName = "CTEPast", TableName = "DT_TEPast", BlobBytes = 27 };

        Assert.Equal(-1, engine.TempEntRegister(past));
        Assert.Single(_tables.Blocks);
        Assert.Contains("a field outside the declared blob", _log.Text);
    }

    [Fact]
    public void AWidthIsResolvedFromItsSymbolAndAnUnknownOneIsRefused()
    {
        ClientTempEntEngine engine = Engine();
        TeClass decal = Class("CTEPlayerDecal", "DT_TEPlayerDecal", 4,
            [Field("m_nPlayer", TePropKind.Int, 0, 4, from: TeBits.MaxPlayersLog2, flags: TeFlags.Unsigned)]);

        Assert.Equal(0, engine.TempEntRegister(decal));
        Read(_tables.Blocks[0], out _, out RecvPropRowDesc[] rows);
        Assert.Equal(((int)SendPropType.Int, (int)RecvProxyId.Int32ToInt32), (rows[0].Type, rows[0].Proxy));

        // Q_log2(MAX_PLAYERS = 33) is five, which the live engine dump reads back.
        Assert.Equal(5, TePropPlanner.Plan(decal)![0].Bits);

        TeClass unknown = Class("CTEUnknown", "DT_TEUnknown", 4,
            [Field("m_n", TePropKind.Int, 0, 4, from: (TeBits)99)]);

        Assert.Equal(-1, engine.TempEntRegister(unknown));
        Assert.Single(_tables.Blocks);
    }

    [Fact]
    public void AWorstCaseOverTheEventCapIsRefusedBeforeNativeIsAsked()
    {
        ClientTempEntEngine engine = Engine();

        // Sixteen COORD vectors fit (stock's DT_TEBeamSpline); ninety-six do not.
        TeClass fits = Class("CTEFits", "DT_TEFits", 196,
            [Field("m_vecPoints", TePropKind.Array, 4, 12, flags: TeFlags.Coord, elems: 16, stride: 12)]);
        TeClass over = Class("CTEOver", "DT_TEOver", 1156,
            [Field("m_vecPoints", TePropKind.Array, 4, 12, flags: TeFlags.Coord, elems: 96, stride: 12)]);

        Assert.Equal(0, engine.TempEntRegister(fits));
        Assert.Equal(-1, engine.TempEntRegister(over));
        Assert.Single(_tables.Blocks);
        Assert.Contains("worst case over the engine's 192-byte event cap", _log.Text);
    }

    //=========================================================================//
    // THE CLIENT'S OWN REFUSALS, AND THE RELOAD.
    //=========================================================================//

    [Fact]
    public void ANestedTableAtOffsetZeroIsRefusedAndTheBaseclassPropIsNot()
    {
        ClientTempEntEngine engine = Engine();
        TeClass inner = Class("CEffectData", "DT_EffectData", 12,
            [Field("m_vOrigin", TePropKind.Vector, 0, 12, flags: TeFlags.Coord)], registers: false);
        TeClass atZero = Class("CTEDispatch", "DT_TEDispatch", 16,
            [Field("m_EffectData", TePropKind.DataTable, 0, 0, sub: "DT_EffectData")]);
        TeClass padded = Class("CTEDispatch2", "DT_TEDispatch2", 16,
            [Field("m_EffectData", TePropKind.DataTable, 4, 0, sub: "DT_EffectData")]);
        TeClass derived = Class("CTEDerived", "DT_TEDerived", 16,
            [Field("m_n", TePropKind.Int, 12, 4, 8)], baseTable: "DT_EffectData");

        Assert.Equal(0, engine.TempEntRegister(inner));
        Assert.Equal(-1, engine.TempEntRegister(atZero));
        Assert.Contains("nested table prop 'm_EffectData' is at offset 0", _log.Text);
        Assert.Equal(1, engine.TempEntRegister(padded));
        Assert.Equal(2, engine.TempEntRegister(derived));
        Assert.Equal(3, _tables.Blocks.Count);

        Read(_tables.Blocks[2], out _, out RecvPropRowDesc[] rows);
        Assert.Equal(("baseclass", (int)SendPropType.DataTable, 0, 0, (int)RecvProxyId.StaticDataTable),
                     (NameAt(_tables.Blocks[2], rows[0].Name), rows[0].Type, rows[0].Offset, rows[0].DataTable,
                      rows[0].Proxy));
    }

    [Fact]
    public void ANestedTableNoTableBuiltHereCarriesIsRefused()
    {
        ClientTempEntEngine engine = Engine();
        TeClass inner = Class("CEffectData", "DT_EffectData", 12,
            [Field("m_vOrigin", TePropKind.Vector, 0, 12, flags: TeFlags.Coord)], registers: false);
        TeClass outer = Class("CTEDispatch", "DT_TEDispatch", 16,
            [Field("m_EffectData", TePropKind.DataTable, 4, 0, sub: "DT_EffectData")]);

        // Native refused the nested table, so the schema holds it and no build does.
        _tables.Answer = _ => null;
        Assert.Equal(-1, engine.TempEntRegister(inner));
        Assert.Contains("native refused to store its table", _log.Text);

        _tables.Answer = null;
        Assert.Equal(-1, engine.TempEntRegister(outer));
        Assert.Contains("nests 'DT_EffectData', which no table built here carries", _log.Text);
        Assert.Single(_tables.Blocks);
    }

    [Fact]
    public void ADescriptionTheSchemaRefusesNeverReachesNative()
    {
        ClientTempEntEngine engine = Engine();

        Assert.Equal(0, engine.TempEntRegister(Splash()));
        Assert.Equal(-1, engine.TempEntRegister(Splash()));
        Assert.Single(_tables.Blocks);
        Assert.Contains("NOT registered on the client: a class of that name is already registered", _log.Text);
    }

    [Fact]
    public void AReloadGetsItsHandleBackAndAReshapeIsSaid()
    {
        uint shape = TeSchemaRegistry.ShapeHash(Splash());

        _tables.Answer = _ => new TempEntRecvTable(7, true, shape);
        Assert.Equal(7, Engine().TempEntRegister(Splash()));
        Assert.DoesNotContain("RESHAPED", _log.Text);
        Assert.Contains("(built by an earlier load)", _log.Text);

        _tables.Answer = _ => new TempEntRecvTable(7, true, shape ^ 1u);
        Assert.Equal(7, Engine().TempEntRegister(Splash()));
        Assert.Contains("'CTEEnergySplash' was RESHAPED across a reload", _log.Text);
    }

    [Fact]
    public void TheBlockCarriesTheClassTheShapeAndTheRegisterBit()
    {
        ClientTempEntEngine engine = Engine();
        TeClass table = Splash() with { ClassName = "CTEBase", TableName = "DT_TEBase", RegistersClass = false };

        Assert.Equal(0, engine.TempEntRegister(Splash()));
        Assert.Equal(1, engine.TempEntRegister(table));

        Read(_tables.Blocks[0], out TeTableHeader classHeader, out _);
        Read(_tables.Blocks[1], out TeTableHeader tableHeader, out _);

        Assert.Equal((3, 1, TeSchemaRegistry.ShapeHash(Splash())),
                     (classHeader.Props, classHeader.RegisterClass, classHeader.ShapeHash));
        Assert.Equal(0, tableHeader.RegisterClass);
        Assert.Equal(2, engine.Built);
    }

    //=========================================================================//
    // ONE PROP, AS dt_recv.h's BUILDERS LEFT IT.
    //=========================================================================//

    private static TePropPlan Planned(TePropKind kind, int size = 4, int offset = 8, TeFlags flags = TeFlags.None,
                                      int elems = 0, int stride = 0, bool arrayElement = false, string name = "m_x")
        => new(name, kind, offset, -offset, size, 7, flags, 0f, 0f, elems, stride, null, arrayElement, 0, 0);

    [Theory]
    [InlineData(1, RecvProxyId.Int32ToInt8)]
    [InlineData(2, RecvProxyId.Int32ToInt16)]
    [InlineData(4, RecvProxyId.Int32ToInt32)]
    public void AnIntsStorageWidthPicksItsNarrowingWrite(int size, RecvProxyId proxy)
    {
        RecvPropRow row = RecvPropPlan.FromPlan(Planned(TePropKind.Int, size, flags: TeFlags.Unsigned))!.Value;

        Assert.Equal(("m_x", SendPropType.Int, 8, (int)TeFlags.Unsigned, proxy, 1, -1, -1),
                     (row.Name, row.Type, row.Offset, row.Flags, row.Proxy, row.Elements, row.ElementStride,
                      row.DataTable));
    }

    [Fact]
    public void AnIntNoWriteNarrowsToIsRefused()
        => Assert.Null(RecvPropPlan.FromPlan(Planned(TePropKind.Int, 3)));

    [Theory]
    [InlineData(TePropKind.Float, SendPropType.Float, RecvProxyId.FloatToFloat)]
    [InlineData(TePropKind.Angle, SendPropType.Float, RecvProxyId.FloatToFloat)]
    [InlineData(TePropKind.Vector, SendPropType.Vector, RecvProxyId.VectorToVector)]
    [InlineData(TePropKind.QAngles, SendPropType.Vector, RecvProxyId.VectorToVector)]
    public void TheAngleKindsArriveAsTheirFloatAndVectorForms(TePropKind kind, SendPropType type, RecvProxyId proxy)
    {
        // At the POSITIVE offset: the plan's declare offset is its negation.
        RecvPropRow row = RecvPropPlan.FromPlan(Planned(kind, 12, 16, TeFlags.Coord))!.Value;

        Assert.Equal((type, 16, (int)TeFlags.Coord, proxy), (row.Type, row.Offset, row.Flags, row.Proxy));
    }

    [Fact]
    public void ADataTableNestsTheHandleItIsGivenAndNoneWithout()
    {
        TePropPlan plan = Planned(TePropKind.DataTable, 0, 0, name: "baseclass");

        Assert.Null(RecvPropPlan.FromPlan(plan));
        RecvPropRow row = RecvPropPlan.FromPlan(plan, 3)!.Value;
        Assert.Equal((SendPropType.DataTable, 0, RecvProxyId.StaticDataTable, 3),
                     (row.Type, row.Offset, row.Proxy, row.DataTable));
    }

    [Fact]
    public void AnArrayIsItsTemplateUnderTheIndexedNameThenTheArrayPropWithNoOffset()
    {
        RecvPropRow template = RecvPropPlan.FromPlan(
            Planned(TePropKind.Vector, 12, 4, TeFlags.Coord, arrayElement: true, name: "m_vecPoints"))!.Value;
        RecvPropRow array = RecvPropPlan.FromPlan(
            Planned(TePropKind.Array, 0, 4, elems: 16, stride: 12, name: "m_vecPoints"))!.Value;

        Assert.Equal(("m_vecPoints[0]", SendPropType.Vector, 4), (template.Name, template.Type, template.Offset));
        Assert.Equal(("m_vecPoints", SendPropType.Array, 0, 0, 16, 12, RecvProxyId.None),
                     (array.Name, array.Type, array.Offset, array.Flags, array.Elements, array.ElementStride,
                      array.Proxy));
        Assert.Null(RecvPropPlan.FromPlan(Planned(TePropKind.Array, 0, 4, elems: 0, stride: 12)));
    }

    //=========================================================================//
    // THE BLOCK AGAINST sourcesharp.h.
    //=========================================================================//

    private static string AbiHeader
    {
        get
        {
            string path = RepoLayout.Repo("src", "host", "native", "sourcesharp.h");
            Assert.True(File.Exists(path), "missing: " + path);
            return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", string.Empty);
        }
    }

    [Theory]
    [InlineData("SourceSharpRecvPropRow_t", typeof(RecvPropRowDesc), 8)]
    [InlineData("SourceSharpTeRecvBuilt_t", typeof(TeRecvBuilt), 2)]
    public void EachStructIsItsMirrorFieldForField(string native, Type managed, int count)
    {
        string text = AbiHeader;
        int end = text.IndexOf($"}} {native};", StringComparison.Ordinal);
        Assert.True(end > 0, $"sourcesharp.h declares {native}");
        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);

        string[] header = Regex.Matches(text[start..end], @"\b(?:int32_t|uint32_t)\s*(\w+)\s*;")
                               .Select(m => Regex.Replace(m.Groups[1].Value, "^(b|n|o|h)(?=[A-Z])", string.Empty))
                               .ToArray();
        string[] mirror = managed.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                                 .OrderBy(f => f.MetadataToken).Select(f => f.Name).ToArray();

        Assert.Equal(count, header.Length);
        Assert.Equal(header, mirror);
        Assert.Equal(count * 4, Marshal.SizeOf(managed));
    }

    [Fact]
    public void EveryRecvProxyIdIsTheHeadersNumber()
    {
        var ids = Regex.Matches(AbiHeader, @"SOURCESHARP_RECVPROXY_(\w+)\s*=\s*(\d+)")
                       .ToDictionary(m => m.Groups[1].Value.Replace("_", string.Empty), m => int.Parse(m.Groups[2].Value),
                                     StringComparer.OrdinalIgnoreCase);

        Assert.Equal(Enum.GetValues<RecvProxyId>().Length + 1, ids.Count);

        foreach (RecvProxyId id in Enum.GetValues<RecvProxyId>())
            Assert.True(ids.TryGetValue(id.ToString(), out int value) && value == (int)id,
                        $"RecvProxyId.{id} = {(int)id}; the header says {(ids.TryGetValue(id.ToString(), out int v) ? v : -1)}");

        Assert.Equal(Enum.GetValues<RecvProxyId>().Length, ids["COUNT"]);
    }

    [Fact]
    public void ABlockIsTheBitCountTheHeaderTheRowsAndTheNames()
    {
        RecvPropRow[] rows =
        [
            new("baseclass", SendPropType.DataTable, 0, 0, 1, -1, RecvProxyId.StaticDataTable, 3),
            new("m_n", SendPropType.Int, 12, 1, 1, -1, RecvProxyId.Int32ToInt8, -1),
        ];

        byte[] block = TempEntRecvBlock.Write("CTEX", "DT_TEX", true, 0xABCDu, rows);
        int payload = BitBlock.Bits(block) / 8;

        Assert.Equal(block.Length - BitBlock.HeaderBytes, payload);
        Read(block, out TeTableHeader header, out RecvPropRowDesc[] read);

        Assert.Equal((2, 1, 0xABCDu), (header.Props, header.RegisterClass, header.ShapeHash));
        Assert.Equal(("CTEX", "DT_TEX"), (NameAt(block, header.ClassName), NameAt(block, header.TableName)));
        Assert.Equal(Unsafe.SizeOf<TeTableHeader>() + 2 * Unsafe.SizeOf<RecvPropRowDesc>(), header.ClassName);
        Assert.Equal(("baseclass", 3, (int)RecvProxyId.StaticDataTable),
                     (NameAt(block, read[0].Name), read[0].DataTable, read[0].Proxy));
        Assert.Equal(("m_n", 12, 1, (int)RecvProxyId.Int32ToInt8, 1, -1),
                     (NameAt(block, read[1].Name), read[1].Offset, read[1].Flags, read[1].Proxy, read[1].Elements,
                      read[1].ElementStride));
        Assert.Equal(0, block[^1]);
    }
}
