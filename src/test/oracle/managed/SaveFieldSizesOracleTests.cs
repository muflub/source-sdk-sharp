using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// <see cref="SaveFieldCodec"/>'s datamap numbers and FIELD_SIZE table held
/// against the pristine <c>public/datamap.h</c> (the client save codec).
///
/// <para>
/// Stock's ShouldSaveField and EmptyFields both walk fieldSize x FIELD_SIZE
/// bytes of a field, so <see cref="SaveFieldCodec.ElementBytes"/> decides which
/// fields a block writes and how much of the struct a read empties: a size
/// off by one type would write a zero field or leave a byte of the engine's
/// struct unemptied, with nothing failing to compile. The enum's order is read
/// from <c>fieldtype_t</c>, and every <c>DECLARE_FIELD_SIZE</c> is evaluated for
/// the 64-bit POSIX build (its <c>#ifdef POSIX</c> arm for FIELD_FUNCTION).
/// </para>
/// </summary>
public class SaveFieldSizesOracleTests
{
    private const string Datamap = "src/public/datamap.h";

    /// <summary>fieldtype_t's members in order: FIELD_VOID = 0 up to FIELD_TYPECOUNT.</summary>
    private static List<string> FieldTypes()
    {
        string text = ValveSdk.Read(Datamap);
        int start = text.IndexOf("typedef enum _fieldtypes", StringComparison.Ordinal);
        int end = text.IndexOf("} fieldtype_t;", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "datamap.h's fieldtype_t enum was not found");

        var names = new List<string>();
        foreach (Match m in Regex.Matches(text[start..end], @"^\s*(?<name>FIELD_\w+)\s*(=\s*0\s*)?,", RegexOptions.Multiline))
            names.Add(m.Groups["name"].Value);
        return names;
    }

    private static int SizeOf(string type) => type.Trim() switch
    {
        "float" or "int" => 4,
        "char" => 1,
        "short" => 2,
        "intp" or "uint64" or "int *" => 8,
        string other => throw new InvalidOperationException("datamap.h sizes an unknown type: sizeof(" + other + ")"),
    };

    /// <summary>Every DECLARE_FIELD_SIZE, evaluated; FIELD_FUNCTION from its POSIX arm.</summary>
    private static Dictionary<string, int> StockSizes()
    {
        string text = ValveSdk.Read(Datamap);
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        bool inElse = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("#ifdef POSIX", StringComparison.Ordinal))
                inElse = false;
            else if (line.StartsWith("#else", StringComparison.Ordinal))
                inElse = true;
            else if (line.StartsWith("#endif", StringComparison.Ordinal))
                inElse = false;

            Match m = Regex.Match(line, @"^DECLARE_FIELD_SIZE\(\s*(?<name>FIELD_\w+),\s*(?<expr>.+)\)\s*(//.*)?$");
            if (!m.Success || inElse)
                continue;

            Match e = Regex.Match(m.Groups["expr"].Value.Trim(), @"^(?:(?<n>\d+)\s*\*\s*)?sizeof\s*\(\s*(?<type>[^)]+)\)$");
            Assert.True(e.Success, "an unreadable DECLARE_FIELD_SIZE: " + line);
            int count = e.Groups["n"].Success ? int.Parse(e.Groups["n"].Value) : 1;
            sizes[m.Groups["name"].Value] = count * SizeOf(e.Groups["type"].Value);
        }
        return sizes;
    }

    [Fact]
    public void EveryFieldTypesElementBytesIsDatamapsFieldSize()
    {
        List<string> types = FieldTypes();
        Dictionary<string, int> sizes = StockSizes();

        // The control: the read found the whole enum and the sizes block.
        Assert.True(types.Count == 30 && types[^1] == "FIELD_TYPECOUNT", $"fieldtype_t has 30 entries to FIELD_TYPECOUNT (read {types.Count})");
        Assert.True(sizes.Count >= 25, $"datamap.h declares at least 25 field sizes (read {sizes.Count})");

        for (int i = 0; i < types.Count - 1; i++)
        {
            // An undeclared size is CDatamapFieldSizeDeducer's default, 0.
            int stock = sizes.GetValueOrDefault(types[i], 0);
            Assert.True(SaveFieldCodec.ElementBytes(i) == stock,
                        $"{types[i]} ({i}): the codec's ElementBytes is {SaveFieldCodec.ElementBytes(i)}, datamap.h's FIELD_SIZE {stock}");
        }

        Assert.Equal(0, SaveFieldCodec.ElementBytes(types.Count - 1));
    }

    [Fact]
    public void TheCodecsTypeNumbersAreFieldtypeTsOwn()
    {
        List<string> types = FieldTypes();
        Assert.Equal(SaveFieldCodec.FieldVoid, types.IndexOf("FIELD_VOID"));
        Assert.Equal(SaveFieldCodec.FieldVector, types.IndexOf("FIELD_VECTOR"));
        Assert.Equal(SaveFieldCodec.FieldInteger, types.IndexOf("FIELD_INTEGER"));
        Assert.Equal(SaveFieldCodec.FieldShort, types.IndexOf("FIELD_SHORT"));
        Assert.Equal(SaveFieldCodec.FieldCharacter, types.IndexOf("FIELD_CHARACTER"));
        Assert.Equal(SaveFieldCodec.FieldEmbedded, types.IndexOf("FIELD_EMBEDDED"));
        Assert.Equal(SaveFieldCodec.FieldCustom, types.IndexOf("FIELD_CUSTOM"));
        Assert.Equal(SaveFieldCodec.FieldEHandle, types.IndexOf("FIELD_EHANDLE"));
        Assert.Equal(SaveFieldCodec.FieldPositionVector, types.IndexOf("FIELD_POSITION_VECTOR"));
        Assert.Equal(SaveFieldCodec.FieldTime, types.IndexOf("FIELD_TIME"));
    }

    /// <summary>The multiplier stock applies to FLT_MAX in <paramref name="define"/>, read out of the C++.</summary>
    private static double FltMaxMultiplier(string relative, string define)
    {
        Match m = Regex.Match(ValveSdk.Read(relative), @"#define " + define + @"\s+\(+FLT_MAX\s*\*\s*(?<m>-?[0-9.]+)\)+");
        Assert.True(m.Success, $"{define} is no longer a multiple of FLT_MAX in {relative}");
        return double.Parse(m.Groups["m"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheTimeSentinelsAreStocksMultiplesOfFltMax()
    {
        // ZERO_TIME is game/shared/saverestore.cpp's, INVALID_TIME datamap.h's;
        // read out of the C++ so the comparison is not with a copy of itself.
        double zero = FltMaxMultiplier("src/game/shared/saverestore.cpp", "ZERO_TIME");
        double invalid = FltMaxMultiplier(Datamap, "INVALID_TIME");
        Assert.True(zero == -0.5 && invalid == -1.0, $"stock's multipliers moved: ZERO_TIME {zero}, INVALID_TIME {invalid}");
        Assert.True(SaveFieldCodec.ZeroTime == (float)(float.MaxValue * zero), "ZeroTime is stock's ZERO_TIME");
        Assert.True(SaveFieldCodec.InvalidTime == (float)(float.MaxValue * invalid), "InvalidTime is stock's INVALID_TIME");
    }
}
