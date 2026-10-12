using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// Lane B's six decal tables against plan_tmp.md Part 6 — and, where the file is
/// still on disk, against the stock table each was transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> writes out at length: a fixture typed out
/// beside the declaration is one person's reading of one table twice, and a
/// misread stays green forever. So these READ the six
/// <c>game/server/te_*decal.cpp</c> files, parse their
/// <c>IMPLEMENT_SERVERCLASS_ST</c> blocks, and assert the agreement.
/// </para>
///
/// <para>
/// The parser here is NOT the one in <see cref="TempEntSchemaTests"/> and could
/// not be. That one reads a bits field with <c>[^,\)]*</c>, which is enough for
/// <c>4</c> and <c>-1</c> and stops in the middle of
/// <c>Q_log2( MAX_PLAYERS )</c>; it takes everything after the bits as one flag
/// expression, which turns <c>SPROP_ROUNDUP, 0, 1024</c> into a token nothing
/// recognises. This lane has both of those and a two-argument
/// <c>SendPropQAngles</c> besides, so it splits the argument list on TOP-LEVEL
/// commas with a paren depth count and reads low and high as their own
/// arguments. A parser that could not see the range would have been unable to
/// check <c>m_flDistance</c> at all.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> The stock files these facts
/// read are no longer compiled by either DLL, so the farm no longer links them;
/// the facts that compare the port against them live here and read the pristine
/// upstream clone through <see cref="ValveSdk"/>, where a missing file is a
/// failure rather than a skip. The rest of TempEntDecalSchemaTests stays where it was.
/// </para>
/// </summary>
public class TempEntDecalSchemaOracleTests
{
    // =====================================================================
    // Against stock's own tables, read off disk.
    // =====================================================================

    [Fact]
    public void BspDecalMatchesTheTableItWasTranscribedFrom()
        => AgreesWithStock("te_bspdecal.cpp", "CTEBSPDecal", "DT_TEBSPDecal", typeof(TeBspDecal),
                           ["Origin", "Entity", "Index"]);

    [Fact]
    public void DecalMatchesTheTableItWasTranscribedFrom()
        => AgreesWithStock("te_decal.cpp", "CTEDecal", "DT_TEDecal", typeof(TeDecal),
                           ["Origin", "Start", "Entity", "Hitbox", "Index"]);

    [Fact]
    public void WorldDecalMatchesTheNonTfBranchOfItsStockTable()
        => AgreesWithStock("te_worlddecal.cpp", "CTEWorldDecal", "DT_TEWorldDecal", typeof(TeWorldDecal),
                           ["Origin", "Index"]);

    [Fact]
    public void PlayerDecalMatchesTheTableItWasTranscribedFrom()
        => AgreesWithStock("te_playerdecal.cpp", "CTEPlayerDecal", "DT_TEPlayerDecal", typeof(TePlayerDecal),
                           ["Origin", "Entity", "Player"]);

    [Fact]
    public void ProjectedDecalMatchesTheTableItWasTranscribedFrom()
        => AgreesWithStock("te_projecteddecal.cpp", "CTEProjectedDecal", "DT_TEProjectedDecal",
                           typeof(TeProjectedDecal),
                           ["Origin", "Rotation", "Distance", "Index"]);

    [Fact]
    public void FootprintDecalMatchesTheTableItWasTranscribedFrom()
        => AgreesWithStock("te_footprintdecal.cpp", "CTEFootprintDecal", "DT_TEFootprintDecal",
                           typeof(TeFootprintDecal),
                           ["Origin", "Direction", "Entity", "Index", "MaterialType"]);

    /// <summary>
    /// The parity check itself: stock's props, prop for prop, against the C#
    /// declaration — name, kind, width (symbol or literal), flags and, where the
    /// prop has one, the range.
    ///
    /// <para>
    /// IT MATCHES BY POSITION, WHICH IS WIRE ORDER, and that has not changed.
    /// What changed is the one NAME assertion: the declarations dropped
    /// <c>WireName</c> (<c>docs/te-declaration-conversion.md</c>), so stock's
    /// <c>m_vecOrigin</c> goes on this build's wire as <c>Origin</c> — and the
    /// caller states the expected spellings IN WIRE ORDER so that a reordered
    /// declaration still fails here. Stock's own spelling stays in the failure
    /// message, which is where the mapping has to stay readable. The kind, width,
    /// flag and range assertions below are untouched and are what actually catch
    /// a transcription slip.
    /// </para>
    /// </summary>
    /// <param name="expected">
    /// This table's wire names, in wire order — the C# member names. Its length
    /// is asserted against stock's prop count, so a class that grew a field
    /// without this list following it fails rather than skipping the check.
    /// </param>
    private static void AgreesWithStock(string fileName, string className, string tableName,
                                        Type declared, string[] expected)
    {
        var stock = StockTable(fileName, className, tableName);

        TeField[] mine = Declared(declared).Fields;

        Assert.True(stock.Count == mine.Length,
            $"{tableName} declares {stock.Count} prop(s) in stock and {mine.Length} in C#");

        Assert.True(expected.Length == stock.Count,
            $"{tableName}: {expected.Length} expected name(s) for stock's {stock.Count} prop(s) — "
          + "the list is per-position, so a missing entry would silently stop checking a prop");

        for (int i = 0; i < stock.Count; i++)
        {
            Assert.True(mine[i].Name == expected[i],
                $"{tableName} prop {i} is '{expected[i]}' on the wire, where stock called it "
              + $"{stock[i].Member}");
            Assert.True(KindOf(stock[i].Function) == mine[i].Kind,
                $"{tableName} prop {i} is a SendProp{stock[i].Function} on both sides");
            Assert.True(FlagsOf(stock[i].Flags) == mine[i].Flags,
                $"{tableName} prop {i} carries '{stock[i].Flags}' on both sides");

            (TeBits from, int bits) = WidthOf(stock[i].Bits);

            Assert.True(from == mine[i].BitsFrom,
                $"{tableName} prop {i} takes its width from '{stock[i].Bits}' on both sides");
            Assert.True(from != TeBits.Literal || bits == mine[i].Bits,
                $"{tableName} prop {i} is {stock[i].Bits} bits on both sides");

            Assert.True(RangeOf(stock[i].Low) == mine[i].Low,
                $"{tableName} prop {i} has stock's low bound");
            Assert.True(RangeOf(stock[i].High) == mine[i].High,
                $"{tableName} prop {i} has stock's high bound");
        }
    }

    // =====================================================================
    // Fixtures.
    // =====================================================================

    /// <summary>
    /// A declaration built on its own. All six nest <c>DT_BaseTempEntity</c>,
    /// which declares no fields, so the base contributes no bytes and the
    /// registry would pass the same zero in.
    /// </summary>
    private static TeClass Declared(Type type) => TeClassBuilder.Build(type);

    private static TePropKind KindOf(string sendPropFunction) => sendPropFunction switch
    {
        "Int"     => TePropKind.Int,
        "Float"   => TePropKind.Float,
        "Vector"  => TePropKind.Vector,
        "Angle"   => TePropKind.Angle,
        "QAngles" => TePropKind.QAngles,
        _ => throw new InvalidOperationException($"SendProp{sendPropFunction} is not a kind this knows"),
    };

    private static TeFlags FlagsOf(string spropExpression)
    {
        TeFlags flags = TeFlags.None;

        foreach (string token in spropExpression.Split('|', StringSplitOptions.TrimEntries))
        {
            flags |= token switch
            {
                "0" or ""                     => TeFlags.None,
                "SPROP_UNSIGNED"              => TeFlags.Unsigned,
                "SPROP_COORD"                 => TeFlags.Coord,
                "SPROP_NOSCALE"               => TeFlags.NoScale,
                "SPROP_ROUNDDOWN"             => TeFlags.RoundDown,
                "SPROP_ROUNDUP"               => TeFlags.RoundUp,
                "SPROP_NORMAL"                => TeFlags.Normal,
                "SPROP_COORD_MP"              => TeFlags.CoordMp,
                "SPROP_COORD_MP_LOWPRECISION" => TeFlags.CoordMpLowPrecision,
                "SPROP_COORD_MP_INTEGRAL"     => TeFlags.CoordMpIntegral,
                _ => throw new InvalidOperationException($"'{token}' is not an SPROP_ this knows"),
            };
        }

        return flags;
    }

    /// <summary>
    /// Stock's bits argument as the pair the schema carries: which symbol it
    /// came from, and the literal when it is one.
    ///
    /// <para>
    /// THE SYMBOLS ARE NOT EVALUATED HERE, and that is the point. Turning
    /// <c>MAX_EDICT_BITS</c> into 11 in this parser and comparing it with an 11
    /// in C# would agree with a declaration that had restated the number —
    /// which is exactly the drift <see cref="TeBits"/> exists to prevent. What
    /// is compared is WHERE THE WIDTH COMES FROM.
    /// </para>
    /// </summary>
    private static (TeBits From, int Bits) WidthOf(string expression)
    {
        string flat = Regex.Replace(expression, @"\s+", "");

        return flat switch
        {
            "MAX_EDICT_BITS"        => (TeBits.MaxEdict, -1),
            "SP_MODEL_INDEX_BITS"   => (TeBits.ModelIndex, -1),
            "Q_log2(MAX_PLAYERS)"   => (TeBits.MaxPlayersLog2, -1),
            _ => int.TryParse(flat, out int literal)
                ? (TeBits.Literal, literal)
                : throw new InvalidOperationException($"'{expression}' is not a width this knows"),
        };
    }

    /// <summary>A low or high bound, or 0 where stock wrote none — which is the attribute's default.</summary>
    private static float RangeOf(string expression)
        => expression.Length == 0 ? 0f : float.Parse(expression);

    /// <summary>One prop line out of a stock IMPLEMENT_SERVERCLASS_ST block.</summary>
    private readonly record struct StockProp(
        string Function, string Member, string Bits, string Flags, string Low, string High);

    /// <summary>
    /// Stock's own table, parsed out of its .cpp in the pristine Valve SDK. A
    /// missing file or table throws: an oracle never passes by reading nothing.
    /// </summary>
    private static List<StockProp> StockTable(string fileName, string className, string tableName)
    {
        string path = ValveSdk.Server(fileName);

        var block = Regex.Match(File.ReadAllText(path),
            @"IMPLEMENT_SERVERCLASS_ST(?:_NOBASE)?\s*\(\s*" + Regex.Escape(className) + @"\s*,\s*"
            + Regex.Escape(tableName) + @"\s*\)(.*?)END_SEND_TABLE", RegexOptions.Singleline);

        if (!block.Success)
            throw new InvalidOperationException($"{fileName} has no {className} / {tableName} send table");

        // Drop the TF arm, keep the #else. Written as a replace over the whole
        // block rather than a line filter: a filter that skipped the #if line
        // and nothing else would keep BOTH branches and report two m_vecOrigins
        // where the build has one.
        string body = Regex.Replace(block.Groups[1].Value,
            @"#if\s+defined\(\s*TF_DLL\s*\).*?#else(.*?)#endif", "$1", RegexOptions.Singleline);

        var props = new List<StockProp>();

        for (int at = body.IndexOf("SendProp", StringComparison.Ordinal); at >= 0;
                 at = body.IndexOf("SendProp", at + 1, StringComparison.Ordinal))
        {
            int open = body.IndexOf('(', at);

            if (open < 0)
                break;

            string function = body[(at + "SendProp".Length)..open].Trim();
            List<string> args = Arguments(body, open, out int end);

            if (args.Count < 2)
                break;

            var member = Regex.Match(args[0], @"SENDINFO(?:_VECTORELEM|_ARRAYELEM)?\s*\(\s*(\w+)");

            if (!member.Success)
                break;

            props.Add(new StockProp(
                Function: function,
                Member:   member.Groups[1].Value,
                Bits:     args[1],
                Flags:    args.Count > 2 ? args[2] : "",
                Low:      args.Count > 3 ? args[3] : "",
                High:     args.Count > 4 ? args[4] : ""));

            at = end;
        }

        return props;
    }

    /// <summary>
    /// One call's arguments, split at TOP-LEVEL commas.
    ///
    /// <para>
    /// A depth count rather than a regex, because two of this lane's arguments
    /// contain commas and parens of their own —
    /// <c>SENDINFO_VECTORELEM( m_vecOrigin, 0 )</c> and
    /// <c>Q_log2( MAX_PLAYERS )</c> — and a character class that stopped at the
    /// first comma would read half of each.
    /// </para>
    /// </summary>
    private static List<string> Arguments(string text, int openParen, out int afterClose)
    {
        var args = new List<string>();
        int depth = 0;
        int start = openParen + 1;

        for (int i = openParen; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;

                if (depth == 0)
                {
                    args.Add(text[start..i].Trim());
                    afterClose = i + 1;
                    return args;
                }
            }
            else if (c == ',' && depth == 1)
            {
                args.Add(text[start..i].Trim());
                start = i + 1;
            }
        }

        afterClose = text.Length;
        return args;
    }
}
