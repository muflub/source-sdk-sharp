using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's temp entity table block (plan_server_native_v2 T4b) against
/// the headers it crosses into: sourcesharp.h's three structs and its proxy ids
/// against their managed mirrors, the row's DPT_* and SPROP_* numbers against
/// dt_common.h (<see cref="TePropPlanner"/>'s restated widths are held to the
/// stock headers that own them by TePropPlannerWidthsOracleTests). Each half is
/// read from the file, never retyped: a field inserted
/// on one side shifts every field after it, and the symptom is a SendProp
/// built with somebody else's width.
/// </summary>
public class TempEntTableAbiParityTests
{
    private static string Read(string path)
    {
        Assert.True(File.Exists(path), "missing: " + path);
        return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", string.Empty);
    }

    private static string AbiHeader => Read(RepoLayout.Repo("src", "host", "native", "sourcesharp.h"));

    private static List<(string Name, int Words)> HeaderFields(string structName)
    {
        string text = AbiHeader;
        int end = text.IndexOf($"}} {structName};", StringComparison.Ordinal);
        Assert.True(end > 0, $"sourcesharp.h declares {structName}");
        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);

        var fields = new List<(string, int)>();
        foreach (Match m in Regex.Matches(text[start..end], @"\b(float|int32_t|uint32_t|void\s*\*|int32_t\s*\*)\s*(\w+)\s*;"))
        {
            string name = Regex.Replace(m.Groups[2].Value, "^(p|fl|b|n|o|h)(?=[A-Z])", string.Empty);
            fields.Add((name, m.Groups[1].Value.Contains('*') ? 2 : 1));
        }

        return fields;
    }

    private static List<(string Name, int Words)> ManagedFields(Type type)
        => type.GetFields(BindingFlags.Public | BindingFlags.Instance)
               .OrderBy(f => f.MetadataToken)
               .Select(f => (f.Name, f.FieldType.IsPointer ? 2 : 1))
               .ToList();

    [Theory]
    [InlineData("SourceSharpTeTableHeader_t", typeof(TeTableHeader))]
    [InlineData("SourceSharpSendPropRow_t", typeof(SendPropRowDesc))]
    [InlineData("SourceSharpTeBuiltTable_t", typeof(TeBuiltTable))]
    public void EachStructIsItsMirrorFieldForField(string native, Type managed)
    {
        var header = HeaderFields(native);
        var mirror = ManagedFields(managed);

        Assert.True(header.Count >= 4, $"{native}: the header read found {header.Count} field(s)");
        Assert.Equal(header, mirror);
    }

    [Fact]
    public void TheMirrorsAreTheSizesTheirFieldsSay()
    {
        Assert.Equal(5 * 4, Unsafe.SizeOf<TeTableHeader>());
        Assert.Equal(12 * 4, Unsafe.SizeOf<SendPropRowDesc>());
        Assert.Equal(4 + 4 + 8 + 8, Unsafe.SizeOf<TeBuiltTable>());
    }

    [Fact]
    public void EveryProxyIdIsTheHeadersNumber()
    {
        string text = AbiHeader;
        var ids = Regex.Matches(text, @"SOURCESHARP_SENDPROXY_(\w+)\s*=\s*(\d+)")
                       .ToDictionary(m => m.Groups[1].Value.Replace("_", string.Empty), m => int.Parse(m.Groups[2].Value),
                                     StringComparer.OrdinalIgnoreCase);

        Assert.Equal(15, ids.Count);   // fourteen ids and the count

        foreach (SendProxyId id in Enum.GetValues<SendProxyId>())
            Assert.True(ids.TryGetValue(id.ToString(), out int value) && value == (int)id,
                        $"SendProxyId.{id} = {(int)id}; the header says {(ids.TryGetValue(id.ToString(), out int v) ? v : -1)}");

        Assert.Equal(Enum.GetValues<SendProxyId>().Length, ids["COUNT"]);
    }

    private static string DtCommon => Read(RepoLayout.Sdk("src", "public", "dt_common.h"));

    [Fact]
    public void TheRowsTypesAreDtCommonsSendPropTypes()
    {
        string text = DtCommon;
        int start = text.IndexOf("DPT_Int=0", StringComparison.Ordinal);
        Assert.True(start > 0, "dt_common.h declares DPT_Int=0");
        string body = text[start..text.IndexOf("DPT_NUMSendPropTypes", start, StringComparison.Ordinal)];

        // Shipped as compiled: the #if 0 Quaternion and the SUPPORTS_INT64 arm
        // are not in the enum this build numbers.
        body = Regex.Replace(body, @"#if 0.*?#endif", string.Empty, RegexOptions.Singleline);
        body = Regex.Replace(body, @"#ifdef SUPPORTS_INT64.*?#endif", string.Empty, RegexOptions.Singleline);
        string[] names = Regex.Matches(body, @"DPT_(\w+)").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(["Int", "Float", "Vector", "VectorXY", "String", "Array", "DataTable"], names);
        for (int i = 0; i < names.Length; i++)
            Assert.Equal(i, (int)Enum.Parse<SendPropType>(names[i]));
    }

    [Theory]
    [InlineData("SPROP_UNSIGNED", SendPropPlan.Unsigned)]
    [InlineData("SPROP_COORD", SendPropPlan.Coord)]
    [InlineData("SPROP_NOSCALE", SendPropPlan.NoScale)]
    [InlineData("SPROP_ROUNDDOWN", SendPropPlan.RoundDown)]
    [InlineData("SPROP_ROUNDUP", SendPropPlan.RoundUp)]
    [InlineData("SPROP_NORMAL", SendPropPlan.Normal)]
    [InlineData("SPROP_PROXY_ALWAYS_YES", SendPropPlan.ProxyAlwaysYes)]
    [InlineData("SPROP_COLLAPSIBLE", SendPropPlan.Collapsible)]
    [InlineData("SPROP_COORD_MP", SendPropPlan.CoordMp)]
    [InlineData("SPROP_COORD_MP_LOWPRECISION", SendPropPlan.CoordMpLowPrecision)]
    [InlineData("SPROP_COORD_MP_INTEGRAL", SendPropPlan.CoordMpIntegral)]
    public void EveryFlagIsDtCommonsBit(string name, int value)
    {
        Match m = Regex.Match(DtCommon, $@"#define\s+{name}\s+\(1\s*<<\s*(\d+)\)");
        Assert.True(m.Success, $"dt_common.h defines {name}");
        Assert.Equal(1 << int.Parse(m.Groups[1].Value), value);
    }

    [Fact]
    public void HighDefaultIsDtCommonsSentinel()
    {
        Match m = Regex.Match(DtCommon, @"#define\s+HIGH_DEFAULT\s+(-?[\d.]+)f");
        Assert.True(m.Success, "dt_common.h defines HIGH_DEFAULT");
        float header = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(header == SendPropPlan.HighDefault, $"dt_common.h says {header}, SendPropPlan {SendPropPlan.HighDefault}");
    }

    [Fact]
    public void ABlockIsTheBitCountTheHeaderTheRowsAndTheNames()
    {
        SendPropRow[] rows =
        [
            SendPropPlan.DataTable("baseclass", 0, 3),
            SendPropPlan.Int("m_n", 12, 4, 4, SendPropPlan.Unsigned),
        ];

        byte[] block = TempEntBlock.Write("CTEX", "DT_TEX", true, 0xABCDu, rows);
        int payload = BitBlock.Bits(block) / 8;

        Assert.Equal(block.Length - BitBlock.HeaderBytes, payload);
        ServerTempEntEngineTests.Read(block, out TeTableHeader header, out SendPropRowDesc[] read);

        Assert.Equal((2, 1, 0xABCDu), (header.Props, header.RegisterClass, header.ShapeHash));
        Assert.Equal("CTEX", ServerTempEntEngineTests.NameAt(block, header.ClassName));
        Assert.Equal("DT_TEX", ServerTempEntEngineTests.NameAt(block, header.TableName));
        Assert.Equal(Unsafe.SizeOf<TeTableHeader>() + 2 * Unsafe.SizeOf<SendPropRowDesc>(), header.ClassName);
        Assert.Equal(("baseclass", 3, (int)SendProxyId.DataTableToDataTable),
                     (ServerTempEntEngineTests.NameAt(block, read[0].Name), read[0].DataTable, read[0].Proxy));
        Assert.Equal(("m_n", 12, 4, (int)SendProxyId.UInt32ToInt32, 1, -1),
                     (ServerTempEntEngineTests.NameAt(block, read[1].Name), read[1].Offset, read[1].Bits, read[1].Proxy,
                      read[1].Elements, read[1].ElementStride));
        Assert.Equal(0, block[^1]);
    }
}
