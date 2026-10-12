using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// Lane C of the temp entity port — sprites and light: <c>DT_TESprite</c>,
/// <c>DT_TEGlowSprite</c>, <c>DT_TESpriteSpray</c>, <c>DT_TESmoke</c>,
/// <c>DT_TEDynamicLight</c> and <c>DT_TEShowLine</c>, against plan_tmp.md
/// Part 6 and against the stock tables they were transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> gives at length: a test comparing a
/// transcription with a fixture typed out beside it is a test comparing a
/// reading with itself, and a misread stays green forever. So every one of
/// these tables is also read out of <c>game/server/te_*.cpp</c> and compared
/// prop for prop — <b>including the [low, high] range and the rounding flag</b>,
/// which <see cref="TempEntSchemaTests"/>' own parser stops short of because
/// <c>DT_TESparks</c> has neither.
/// </para>
///
/// <para>
/// THAT IS WHY THIS FILE PARSES STOCK ITSELF instead of reusing that one's
/// helper. This lane's whole risk is in the two arguments after the flag:
/// <c>m_fNoise</c> is [0, 2.56] where five neighbours are [0, 25.6], and
/// <c>DT_TEDynamicLight</c> mixes <c>SPROP_ROUNDUP</c> with
/// <c>SPROP_ROUNDDOWN</c> inside one table. A parity check that dropped the
/// range and the rounding would pass on both mistakes.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> The stock files these facts
/// read are no longer compiled by either DLL, so the farm no longer links them;
/// the facts that compare the port against them live here and read the pristine
/// upstream clone through <see cref="ValveSdk"/>, where a missing file is a
/// failure rather than a skip. The rest of TempEntSpritesTests stays where it was.
/// </para>
/// </summary>
public class TempEntSpritesOracleTests
{

    [Fact]
    public void SpriteMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeSprite>("te_sprite.cpp", "CTESprite", "DT_TESprite",
                                  expected: ["Origin", "ModelIndex", "Scale", "Brightness"]);

    [Fact]
    public void GlowSpriteMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeGlowSprite>("te_glowsprite.cpp", "CTEGlowSprite", "DT_TEGlowSprite",
                                      expected: ["Origin", "ModelIndex", "Scale", "Life", "Brightness"]);

    [Fact]
    public void SpriteSprayMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeSpriteSpray>("te_spritespray.cpp", "CTESpriteSpray", "DT_TESpriteSpray",
                                       expected: ["Origin", "Direction", "ModelIndex", "Noise",
                                                  "Speed", "Count"]);

    [Fact]
    public void SmokeMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeSmoke>("te_smoke.cpp", "CTESmoke", "DT_TESmoke",
                                 expected: ["Origin", "ModelIndex", "Scale", "FrameRate"]);

    [Fact]
    public void DynamicLightMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeDynamicLight>("te_dynamiclight.cpp", "CTEDynamicLight", "DT_TEDynamicLight",
                                        expected: ["Origin", "R", "G", "B", "Exponent",
                                                   "Radius", "Time", "Decay"]);

    [Fact]
    public void ShowLineMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeShowLine>("te_showline.cpp", "CTEShowLine", "DT_TEShowLine",
                                    TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes,
                                    ["End"]);

    /// <summary>
    /// The parser read something, and it can tell the two things apart that the
    /// six parity checks above turn on.
    ///
    /// <para>
    /// WITHOUT THIS THE PARITY HALF COULD DEGRADE TO SILENCE: a parser that
    /// quietly matched nothing would compare empty tables (a missing file or
    /// table throws). So this asserts the parse actually happened and that
    /// the two fields it would be easiest to conflate come back DIFFERENT —
    /// a parser returning the same thing for both would pass every parity
    /// assertion in this file.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStockParserReadsTheRangeAndTheFlagItIsThereFor()
    {
        var spray = StockTable("te_spritespray.cpp", "CTESpriteSpray", "DT_TESpriteSpray");
        var light = StockTable("te_dynamiclight.cpp", "CTEDynamicLight", "DT_TEDynamicLight");

        Assert.True(spray.Count == 6 && light.Count == 8,
            "both tables parsed to the prop count stock declares");

        // The range, which the S-Phase 1 parser cannot see at all.
        Assert.True(spray[3].Member == "m_fNoise" && spray[3].High == "2.56",
            "m_fNoise's high bound comes back as 2.56");
        Assert.True(light[6].Member == "m_fTime" && light[6].High == "25.6",
            "and m_fTime's as 25.6 — the parser distinguishes the two ranges this lane mixes");

        // The rounding flag, which no bit count and no byte total would catch.
        Assert.True(light[5].Member == "m_fRadius" && light[5].Flags == "SPROP_ROUNDUP",
            "m_fRadius comes back as SPROP_ROUNDUP");
        Assert.True(light[7].Member == "m_fDecay" && light[7].Flags == "SPROP_ROUNDDOWN",
            "and m_fDecay as SPROP_ROUNDDOWN, over the same [0,2560] range");

        // The sign, likewise invisible on the wire's width.
        Assert.True(light[1].Member == "r" && light[1].Flags == "SPROP_UNSIGNED",
            "r comes back unsigned");
        Assert.True(light[4].Member == "exponent" && light[4].Flags == "0",
            "and exponent comes back as a literal 0, which is what makes it signed");

        // And the model index, whose arguments are not in the table text at all.
        Assert.True(spray[2].Member == "m_nModelIndex" && spray[2].Function == "ModelIndex"
                 && string.IsNullOrEmpty(spray[2].Bits),
            "SendPropModelIndex carries no width and no flags of its own; dt_send.h:750 supplies both");
    }

    private static TeClass Build<T>() => TeClassBuilder.Build(typeof(T));

    // ---------------------------------------------------------------------
    // Stock's own table, parsed out of its .cpp.
    // ---------------------------------------------------------------------

    /// <summary>One prop line out of a stock IMPLEMENT_SERVERCLASS_ST block.</summary>
    private readonly record struct StockProp(
        string Function, string Member, string? Bits, string? Flags, string? Low, string? High);

    /// <summary>
    /// The whole parity check for one class: read stock's table off disk and
    /// assert the managed declaration agrees prop for prop — name, kind, width,
    /// flags AND the [low, high] range.
    /// </summary>
    /// <param name="expected">
    /// What THIS declaration calls each prop, in wire order, for a class whose
    /// wire names are its C# members'. Null keeps stock's own spelling as the
    /// expectation, which is right for every declaration not yet converted —
    /// so this file's converted and unconverted classes are checked by the same
    /// helper without either forcing the other's state.
    /// </param>
    private static void AssertParity<T>(string fileName, string className, string tableName,
                                        int baseBytes = 0, string[]? expected = null)
    {
        var stock = StockTable(fileName, className, tableName);

        TeField[] mine = TeClassBuilder.Build(typeof(T), baseBytes).Fields;

        Assert.True(stock.Count == mine.Length,
            $"stock's {tableName} declares {stock.Count} props and the managed one {mine.Length}");

        Assert.True(expected is null || expected.Length == stock.Count,
            $"{tableName}: {expected?.Length} expected name(s) for stock's {stock.Count} prop(s) — "
          + "the list is per-position, so a missing entry would silently stop checking a prop");

        for (int i = 0; i < stock.Count; i++)
        {
            StockProp s = stock[i];
            TeField m = mine[i];

            // MATCHED BY POSITION, which is what order-is-wire-order always
            // meant; only the NAME expectation moves when a class converts, and
            // stock's own spelling stays in the message so the mapping is still
            // readable from the failure.
            string want = expected is not null ? expected[i] : s.Member;

            Assert.True(m.Name == want,
                expected is not null
                    ? $"prop {i} is '{want}' on the wire, where stock called it {s.Member}"
                    : $"prop {i} is called {s.Member} on both sides");
            Assert.True(KindOf(s.Function) == m.Kind, $"prop {i} is a SendProp{s.Function} on both sides");

            if (s.Function == "ModelIndex")
            {
                // SendPropModelIndex takes no width and no flags of its own:
                // dt_send.h:750 supplies SP_MODEL_INDEX_BITS and 0.
                Assert.True(m.BitsFrom == TeBits.ModelIndex,
                    $"prop {i} takes its width from SP_MODEL_INDEX_BITS, as stock's macro does");
                Assert.True(m.Flags == TeFlags.None,
                    $"prop {i} carries no flags, as SendPropModelIndex's own 0 says");
                continue;
            }

            Assert.True(m.BitsFrom == TeBits.Literal,
                $"prop {i}'s width is a literal in stock's table, so it is a literal here");
            Assert.True(m.Bits.ToString(CultureInfo.InvariantCulture) == s.Bits,
                $"prop {i} is {s.Bits} bits on both sides");
            Assert.True(FlagsOf(s.Flags) == m.Flags,
                $"prop {i} carries {s.Flags ?? "(none)"} on both sides");

            // THE RANGE, which is half this lane's risk and which the S-Phase 1
            // parser does not read at all.
            Assert.True(NumberOr(s.Low, 0f) == m.Low,
                $"prop {i}'s range starts at {s.Low ?? "0"} on both sides");
            Assert.True(NumberOr(s.High, 0f) == m.High,
                $"prop {i}'s range ends at {s.High ?? "0"} on both sides");
        }
    }

    private static float NumberOr(string? text, float fallback)
        => string.IsNullOrWhiteSpace(text)
            ? fallback
            : float.Parse(text, CultureInfo.InvariantCulture);

    private static TePropKind KindOf(string sendPropFunction) => sendPropFunction switch
    {
        "Int"        => TePropKind.Int,
        "ModelIndex" => TePropKind.Int,   // dt_send.h:750 — a SendPropInt with a fixed width
        "Float"      => TePropKind.Float,
        "Vector"     => TePropKind.Vector,
        "Angle"      => TePropKind.Angle,
        "QAngles"    => TePropKind.QAngles,
        _ => throw new InvalidOperationException($"SendProp{sendPropFunction} is not a kind this knows"),
    };

    private static TeFlags FlagsOf(string? spropExpression)
    {
        TeFlags flags = TeFlags.None;

        foreach (string token in (spropExpression ?? "").Split('|', StringSplitOptions.TrimEntries))
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
    /// Stock's table, read from the pristine Valve SDK. A missing file or a
    /// missing table throws: an oracle never passes by reading nothing.
    ///
    /// <para>
    /// THE ARGUMENTS ARE SPLIT ON BALANCED PARENTHESES rather than matched by
    /// one regex. <c>SENDINFO(m_fScale )</c> is itself a call, and a regex that
    /// took "everything up to the next comma" for the width would read
    /// <c>SPROP_ROUNDDOWN, 0.0, 25.6</c> as one flag expression — which is
    /// precisely the range this lane needs checked.
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

        // Drop the TF arm and keep the #else: TF_DLL is in neither target's
        // defines (plan 2G). Written as a replace over the whole block rather
        // than a line filter, because a filter would keep BOTH branches.
        body = Regex.Replace(body, @"#if\s+defined\(\s*TF_DLL\s*\).*?#else(.*?)#endif", "$1",
                             RegexOptions.Singleline);

        var props = new List<StockProp>();

        foreach (Match call in Regex.Matches(body, @"SendProp(\w+)\s*\("))
        {
            string function = call.Groups[1].Value;
            string? args = BalancedArgs(body, call.Index + call.Length - 1);

            if (args is null)
                continue;

            List<string> parts = SplitTopLevel(args);

            if (parts.Count == 0)
                continue;

            var member = Regex.Match(parts[0], @"SENDINFO\w*\s*\(\s*(\w+)");

            if (!member.Success)
                continue;

            props.Add(new StockProp(
                Function: function,
                Member:   member.Groups[1].Value,
                Bits:     parts.Count > 1 ? parts[1] : null,
                Flags:    parts.Count > 2 ? parts[2] : null,
                Low:      parts.Count > 3 ? parts[3] : null,
                High:     parts.Count > 4 ? parts[4] : null));
        }

        return props;
    }

    /// <summary>The text inside the parenthesis at <paramref name="open"/>, or null if unbalanced.</summary>
    private static string? BalancedArgs(string text, int open)
    {
        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0)
                return text[(open + 1)..i];
        }

        return null;
    }

    /// <summary>Splits on commas that are not inside a nested call.</summary>
    private static List<string> SplitTopLevel(string args)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '(') depth++;
            else if (args[i] == ')') depth--;
            else if (args[i] == ',' && depth == 0)
            {
                parts.Add(args[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(args[start..].Trim());

        // A trailing comma inside the argument list leaves an empty tail, which
        // is not a fifth argument.
        if (parts.Count > 0 && parts[^1].Length == 0)
            parts.RemoveAt(parts.Count - 1);

        return parts;
    }
}
