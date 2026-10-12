using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The three tables S-Phase 1 declares, against plan_tmp.md Part 6 — and, where
/// the file is still on disk, against the stock table it was transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS. A test comparing a C# declaration
/// with a fixture typed out beside it is a test comparing a transcription with
/// itself: both halves are the same person's reading of the same table, and a
/// misread stays green forever. docs/writing-tests.md records what that costs —
/// <c>SupplyTiers()</c> described three tiers for several steps after native had
/// grown to seven, and nothing failed. So these read
/// <c>game/server/te_sparks.cpp</c> and <c>te_particlesystem.cpp</c> and assert
/// the agreement, exactly as <c>AbiParityTests</c> reads the ABI header.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> The stock files these facts
/// read are no longer compiled by either DLL, so the farm no longer links them;
/// the facts that compare the port against them live here and read the pristine
/// upstream clone through <see cref="ValveSdk"/>, where a missing file is a
/// failure rather than a skip. The rest of TempEntSchemaTests stays where it was.
/// </para>
/// </summary>
public class TempEntSchemaOracleTests
{
    // ---------------------------------------------------------------------
    // Against stock's own tables, read off disk.
    // ---------------------------------------------------------------------

    [Fact]
    public void SparksMatchesTheTableItWasTranscribedFrom()
    {
        var stock = StockTable("te_sparks.cpp", "CTESparks", "DT_TESparks");

        Assert.True(stock.Count == 3, "stock's DT_TESparks declares three props");

        Assert.True(stock[0] == new StockProp("Int", "m_nMagnitude", "4", "SPROP_UNSIGNED"),
            "stock sends m_nMagnitude as 4 bits unsigned");
        Assert.True(stock[1] == new StockProp("Int", "m_nTrailLength", "4", "SPROP_UNSIGNED"),
            "stock sends m_nTrailLength as 4 bits unsigned");
        Assert.True(stock[2] == new StockProp("Vector", "m_vecDir", "-1", "SPROP_COORD"),
            "stock sends m_vecDir as a COORD vector with the flag deciding the width");

        // And now the agreement, which is the point: the same four facts, taken
        // out of the C# declaration rather than typed out again.
        //
        // MATCHED BY POSITION, WHICH IS WHAT MAKES THE CONVERSION SURVIVABLE.
        // Order IS wire order, so position was always the real key; the wire
        // name is now the C# member's and no longer stock's, so the one name
        // assertion compares against what this declaration says it calls the
        // prop, with stock's own spelling kept in the message so the mapping
        // stays readable. The kind, width and flag assertions below are
        // untouched and are what actually catch a transcription slip.
        string[] expected = ["Magnitude", "TrailLength", "Direction"];

        TeField[] mine = Sparks().Fields;

        for (int i = 0; i < 3; i++)
        {
            Assert.True(mine[i].Name == expected[i],
                $"prop {i} is '{expected[i]}' on the wire, where stock called it {stock[i].Member}");
            Assert.True(KindOf(stock[i].Function) == mine[i].Kind,
                $"prop {i} is a {stock[i].Function} on both sides");
            Assert.True(mine[i].Bits.ToString() == stock[i].Bits,
                $"prop {i} is {stock[i].Bits} bits on both sides");
            Assert.True(FlagsOf(stock[i].Flags) == mine[i].Flags,
                $"prop {i} carries {stock[i].Flags} on both sides");
        }
    }

    [Fact]
    public void TheParticleSystemMatchesTheNonTfBranchOfItsStockTable()
    {
        var stock = StockTable("te_particlesystem.cpp", "CTEParticleSystem", "DT_TEParticleSystem");

        // THE #else BRANCH, and saying so is half the assertion. TF_DLL appears
        // in neither target's defines (plan 2G), so the origin is SPROP_COORD
        // and not SPROP_COORD_MP_INTEGRAL. A reader taking the other branch
        // would produce a table that decodes to plausible integers.
        Assert.True(stock.Count == 3,
            "the non-TF branch declares three SendPropFloats (the TF branch was skipped)");
        Assert.True(stock.All(p => p.Flags == "SPROP_COORD"),
            "all three are SPROP_COORD, which is the non-TF spelling");

        TeField origin = TeClassBuilder.Build(typeof(TeParticleSystem)).Fields[0];

        Assert.True(stock.Select(p => p.Member).Distinct().Count() == 1
                 && stock[0].Member == "m_vecOrigin" && origin.Name == "Origin",
            "stock's three SENDINFO_VECTORELEM lines are one member, m_vecOrigin, and this "
          + "declaration's one member is Origin — the same field, under the name the C# "
          + "member gives it");
        Assert.True(KindOf(stock[0].Function) == origin.Kind, "sent as floats on both sides");
        Assert.True(stock[0].Bits == origin.Bits.ToString(), "with the same declared width");
        Assert.True(FlagsOf(stock[0].Flags) == (origin.Flags & ~TeFlags.VectorElem),
            "and the same encoding flag; VectorElem is how the offset is declared, not a wire flag");
        Assert.True(stock.Count == origin.Elems,
            "one C# field expands into exactly as many props as stock wrote out by hand");
    }

    [Fact]
    public void TheBaseTableIsEmptyInStockToo()
    {
        var stock = StockTable("basetempentity.cpp", "CBaseTempEntity", "DT_BaseTempEntity");

        Assert.True(stock.Count == 0, "stock's DT_BaseTempEntity carries no props");
        Assert.True(TeClassBuilder.Build(typeof(TeBaseTempEntity)).Fields.Length == 0,
            "and neither does the managed declaration");
    }

    // ---------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------

    private static TeClass Sparks()
        => TeClassBuilder.Build(typeof(TeSparks),
                                TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes);

    private static TePropKind KindOf(string sendPropFunction) => sendPropFunction switch
    {
        "Int"      => TePropKind.Int,
        "Float"    => TePropKind.Float,
        "Vector"   => TePropKind.Vector,
        "Angle"    => TePropKind.Angle,
        "QAngles"  => TePropKind.QAngles,
        _          => throw new InvalidOperationException($"SendProp{sendPropFunction} is not a kind this knows"),
    };

    private static TeFlags FlagsOf(string spropExpression)
    {
        TeFlags flags = TeFlags.None;

        foreach (string token in spropExpression.Split('|', StringSplitOptions.TrimEntries))
        {
            flags |= token switch
            {
                "0" or ""                       => TeFlags.None,
                "SPROP_UNSIGNED"                => TeFlags.Unsigned,
                "SPROP_COORD"                   => TeFlags.Coord,
                "SPROP_NOSCALE"                 => TeFlags.NoScale,
                "SPROP_ROUNDDOWN"               => TeFlags.RoundDown,
                "SPROP_ROUNDUP"                 => TeFlags.RoundUp,
                "SPROP_NORMAL"                  => TeFlags.Normal,
                "SPROP_COORD_MP"                => TeFlags.CoordMp,
                "SPROP_COORD_MP_LOWPRECISION"   => TeFlags.CoordMpLowPrecision,
                "SPROP_COORD_MP_INTEGRAL"       => TeFlags.CoordMpIntegral,
                _ => throw new InvalidOperationException($"'{token}' is not an SPROP_ this knows"),
            };
        }

        return flags;
    }

    /// <summary>One prop line out of a stock IMPLEMENT_SERVERCLASS_ST block.</summary>
    private readonly record struct StockProp(string Function, string Member, string Bits, string Flags);

    /// <summary>
    /// Stock's own table, parsed out of its .cpp in the pristine Valve SDK. A
    /// missing file or table throws: an oracle never passes by reading nothing.
    ///
    /// <para>
    /// The <c>#if defined( TF_DLL )</c> arm is dropped and its <c>#else</c>
    /// kept: <c>TF_DLL</c> is in neither target's defines (plan 2G), so the
    /// branch this build compiles is the one the managed declaration must
    /// match.
    /// </para>
    /// </summary>
    private static List<StockProp> StockTable(string fileName, string className, string tableName)
    {
        string path = ValveSdk.Server(fileName);

        string text = File.ReadAllText(path);

        var block = Regex.Match(text,
            @"IMPLEMENT_SERVERCLASS_ST(?:_NOBASE)?\s*\(\s*" + Regex.Escape(className) + @"\s*,\s*"
            + Regex.Escape(tableName) + @"\s*\)(.*?)END_SEND_TABLE", RegexOptions.Singleline);

        if (!block.Success)
            throw new InvalidOperationException($"{fileName} has no {className} / {tableName} send table");

        string body = block.Groups[1].Value;

        // Drop the TF arm, keep the #else. Written as a replace over the whole
        // block rather than a line filter, because a filter that skipped
        // "#if defined( TF_DLL )" and nothing else would keep BOTH branches and
        // report six props where the build has three.
        body = Regex.Replace(body, @"#if\s+defined\(\s*TF_DLL\s*\).*?#else(.*?)#endif", "$1",
                             RegexOptions.Singleline);

        var props = new List<StockProp>();

        foreach (Match m in Regex.Matches(body,
            @"SendProp(\w+)\s*\(\s*SENDINFO(_VECTORELEM|_ARRAYELEM)?\s*\(\s*(\w+)\s*(?:,\s*\d+\s*)?\)\s*,?\s*([^,\)]*)\s*,?\s*([^\)]*)\)"))
        {
            string bits  = m.Groups[4].Value.Trim();
            string flags = m.Groups[5].Value.Trim();

            props.Add(new StockProp(m.Groups[1].Value, m.Groups[3].Value, bits, flags));
        }

        return props;
    }
}

