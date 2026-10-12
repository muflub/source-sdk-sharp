using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The eight beam tables — <c>DT_BaseBeam</c> and its seven derivatives —
/// against plan_tmp.md Part 6, and against the stock tables they were
/// transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> writes out at length: a fixture typed beside
/// the declaration is one person's reading of one table twice, and a misread
/// stays green forever. So every one of these reads its
/// <c>game/server/te_beam*.cpp</c> off disk, parses the
/// <c>IMPLEMENT_SERVERCLASS_ST</c> block, and asserts the agreement prop for
/// prop — name, kind, width, flags and range.
/// </para>
///
/// <para>
/// The parser here is a superset of <see cref="TempEntSchemaTests"/>' and
/// deliberately a separate one: this lane needs <c>SendPropModelIndex</c> (no
/// width and no flags at all), a SYMBOLIC width (<c>MAX_EDICT_BITS</c>) and
/// float RANGES, none of which S-Phase 1's three tables have. Six lanes editing
/// one parser is the merge conflict this tree has already paid for; the two are
/// worth consolidating once the lanes have landed, and the note is here so that
/// is a decision rather than an oversight.
/// </para>
///
/// <para>
/// Two facts about this lane are load-bearing and each has an assertion of its
/// own below. <c>DT_BaseBeam</c> sends <c>m_nSpeed</c> BEFORE <c>r g b a</c>,
/// which is not the member order in <c>te_basebeam.h</c>. And
/// <c>DT_TEBeamRing</c> sends its two entity props in <c>MAX_EDICT_BITS</c>
/// where five siblings send the same value in a literal 24 — the two are not
/// unified, because a prop one bit narrower than the other side expects is a
/// bitstream that stops making sense partway through with no log line anywhere.
/// </para>
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> The stock files these facts
/// read are no longer compiled by either DLL, so the farm no longer links them;
/// the facts that compare the port against them live here and read the pristine
/// upstream clone through <see cref="ValveSdk"/>, where a missing file is a
/// failure rather than a skip. The rest of TempEntBeamTests stays where it was.
/// </para>
/// </summary>
public class TempEntBeamOracleTests
{
    /// <summary>
    /// Declaration order is wire order, and this table is where that stops being
    /// a slogan: <c>te_basebeam.h:44-49</c> declares <c>r, g, b, a</c> and THEN
    /// <c>m_nSpeed</c>, while the send table sends <c>m_nSpeed</c> first. A
    /// reader who transcribed the struct would produce a table that decodes
    /// speed as red and alpha as speed — four plausible numbers and no error.
    /// </summary>
    [Fact]
    public void BaseBeamSendsSpeedBeforeTheColourAndTheHeaderDoesNot()
    {
        string[] wire = BaseBeam().Fields.Select(f => f.Name).ToArray();

        Assert.True(wire[9]  == "Speed", "the tenth prop on the wire is Speed (stock's m_nSpeed)");
        Assert.True(wire[10] == "ColorR", "then ColorR (stock's r)");
        Assert.True(wire[11] == "ColorG", "then ColorG");
        Assert.True(wire[12] == "ColorB", "then ColorB");
        Assert.True(wire[13] == "ColorA", "then ColorA");
        Assert.True(wire[14] == "Flags", "and Flags last (stock's m_nFlags)");

        // The other half of the claim, read out of the header rather than
        // asserted from memory: the struct really does have them the other way
        // round, so this is a genuine divergence and not a misremembered one.
        string header = ValveSdk.Read("src/game/server/te_basebeam.h");

        int atColour = header.IndexOf("CNetworkVar( int, r )", StringComparison.Ordinal);
        int atSpeed  = header.IndexOf("CNetworkVar( int, m_nSpeed )", StringComparison.Ordinal);

        Assert.True(atColour > 0 && atSpeed > 0, "te_basebeam.h declares both r and m_nSpeed");
        Assert.True(atColour < atSpeed,
            "and declares the colour BEFORE m_nSpeed — the opposite of the table, "
          + "which is why this file copies the table");
    }

    // ---------------------------------------------------------------------
    // Against stock's own tables, read off disk.
    // ---------------------------------------------------------------------

    [Fact]
    public void BaseBeamMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_basebeam.cpp", "CTEBaseBeam", "DT_BaseBeam", BaseBeam(),
                        BaseBeamWireNames);

    [Fact]
    public void BeamEntPointMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_beamentpoint.cpp", "CTEBeamEntPoint", "DT_TEBeamEntPoint",
                        Derived(typeof(TeBeamEntPoint)),
                        ["StartEntity", "EndEntity", "StartPoint", "EndPoint"]);

    [Fact]
    public void BeamEntsMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_beaments.cpp", "CTEBeamEnts", "DT_TEBeamEnts",
                        Derived(typeof(TeBeamEnts)),
                        ["StartEntity", "EndEntity"]);

    [Fact]
    public void BeamFollowMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_beamfollow.cpp", "CTEBeamFollow", "DT_TEBeamFollow",
                        Derived(typeof(TeBeamFollow)),
                        ["EntIndex"]);

    [Fact]
    public void BeamLaserMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_beamlaser.cpp", "CTEBeamLaser", "DT_TEBeamLaser",
                        Derived(typeof(TeBeamLaser)),
                        ["StartEntity", "EndEntity"]);

    [Fact]
    public void BeamPointsMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_beampoints.cpp", "CTEBeamPoints", "DT_TEBeamPoints",
                        Derived(typeof(TeBeamPoints)),
                        ["StartPoint", "EndPoint"]);

    [Fact]
    public void BeamRingMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_beamring.cpp", "CTEBeamRing", "DT_TEBeamRing",
                        Derived(typeof(TeBeamRing)),
                        ["StartEntity", "EndEntity"]);

    [Fact]
    public void BeamRingPointMatchesTheTableItWasTranscribedFrom()
        => AssertAgrees("te_beamringpoint.cpp", "CTEBeamRingPoint", "DT_TEBeamRingPoint",
                        Derived(typeof(TeBeamRingPoint)),
                        ["Center", "StartRadius", "EndRadius"]);

    /// <summary>
    /// The parser can see the difference it exists to see.
    ///
    /// <para>
    /// <c>AssertAgrees</c> is only worth something if a stock table that
    /// disagreed with the declaration would fail it — so this reads the two
    /// tables that differ ONLY in that width and asserts the parser reports them
    /// differently. Without it, a parser that silently produced the same answer
    /// for <c>24</c> and <c>MAX_EDICT_BITS</c> would make all eight parity tests
    /// pass over a mismatch.
    /// </para>
    /// </summary>
    [Fact]
    public void TheParserTellsASymbolicWidthFromALiteralOne()
    {
        var ring = StockTable("te_beamring.cpp", "CTEBeamRing", "DT_TEBeamRing");
        var ents = StockTable("te_beaments.cpp", "CTEBeamEnts", "DT_TEBeamEnts");

        Assert.True(ring[0].Member == ents[0].Member,
            "both tables send a prop called m_nStartEntity");
        Assert.True(ring[0].Bits == "MAX_EDICT_BITS", "CTEBeamRing's is declared as a symbol");
        Assert.True(ents[0].Bits == "24", "CTEBeamEnts' is declared as a literal");
        Assert.True(ring[0].Bits != ents[0].Bits,
            "and the parser reports them differently, which is what makes the eight "
          + "parity assertions above capable of failing");
    }

    /// <summary>
    /// <c>DT_BaseBeam</c>'s fifteen props as they go on the WIRE, which since
    /// the declaration conversion is <see cref="TeBaseBeam"/>'s member names.
    ///
    /// <para>
    /// IN THE TABLE'S ORDER AND NOT THE HEADER'S — <c>Speed</c> sits fifth from
    /// last, where stock's <c>m_nSpeed</c> does, and not beside the widths where
    /// <c>te_basebeam.h</c> declares it. Typed out once here rather than
    /// derived from the declaration: a list computed FROM the thing under test
    /// agrees with it however wrong it is, which is
    /// <c>shell_nettable.cpp</c>'s "do not compare a computation with itself".
    /// </para>
    /// </summary>
    private static readonly string[] BaseBeamWireNames =
    {
        "ModelIndex", "HaloIndex", "StartFrame", "FrameRate", "Life", "Width",
        "EndWidth", "FadeLength", "Amplitude", "Speed",
        "ColorR", "ColorG", "ColorB", "ColorA", "Flags",
    };

    private static TeClass BaseBeam() => TeClassBuilder.Build(typeof(TeBaseBeam));

    private static TeClass Derived(Type type)
        => TeClassBuilder.Build(type, BaseBeam().BlobBytes);

    // ---------------------------------------------------------------------
    // Stock's tables, parsed out of the .cpp they live in.
    // ---------------------------------------------------------------------

    /// <summary>One prop line out of a stock IMPLEMENT_SERVERCLASS_ST block.</summary>
    private readonly record struct StockProp(
        string Function, string Member, string Bits, string Flags, string Low, string High);

    /// <summary>
    /// Asserts a managed table against the stock one it was transcribed from,
    /// prop for prop and in order.
    ///
    /// <para>
    /// The stock table is read from the pristine Valve SDK; a missing file or a
    /// missing table throws rather than skipping, so this can never pass by
    /// reading nothing.
    /// </para>
    /// </summary>
    private static void AssertAgrees(string fileName, string className, string tableName,
                                     TeClass mine, string[] expected)
    {
        Assert.True(expected.Length == mine.Fields.Length,
            $"the expected wire names for {tableName} are one per prop — {expected.Length} "
          + $"named against {mine.Fields.Length} declared. This list is the SECOND reading "
          + "of the declaration and is worth nothing if it is allowed to be short.");

        var stock = StockTable(fileName, className, tableName);

        Assert.True(stock.Count == mine.Fields.Length,
            $"stock's {tableName} declares {stock.Count} prop(s) and the managed table has "
          + $"{mine.Fields.Length}");

        for (int i = 0; i < stock.Count; i++)
        {
            StockProp s = stock[i];
            TeField   m = mine.Fields[i];

            // THE ONE ASSERTION THE CONVERSION CHANGED. Stock's spelling is no
            // longer the wire name -- the declaration dropped its WireNames and
            // the member name goes out instead -- so this pins the wire name to
            // a list typed here and keeps stock's spelling in the message, where
            // a reader comparing the two tables needs it. Position is still what
            // pairs the two, because declared order is wire order.
            Assert.True(m.Name == expected[i],
                $"{tableName} prop {i} is '{expected[i]}' on the wire, where stock called it "
              + $"{s.Member}");
            Assert.True(KindOf(s.Function) == m.Kind,
                $"{tableName}.{s.Member} is a SendProp{s.Function} on both sides");

            // The width, whichever of the two forms stock used. A symbol travels
            // as a TeBits and native resolves it; a literal stays a literal.
            if (s.Function == "ModelIndex")
            {
                Assert.True(m.BitsFrom == TeBits.ModelIndex,
                    $"{tableName}.{s.Member} takes its width from SP_MODEL_INDEX_BITS on both sides");
                Assert.True(m.Flags == TeFlags.None,
                    $"{tableName}.{s.Member} is SIGNED on both sides — SendPropModelIndex passes flags 0");
            }
            else if (BitsSymbols.TryGetValue(s.Bits, out TeBits symbol))
            {
                Assert.True(m.BitsFrom == symbol,
                    $"{tableName}.{s.Member} is {s.Bits} wide on both sides, as a symbol and not a number");
            }
            else
            {
                Assert.True(m.BitsFrom == TeBits.Literal && m.Bits.ToString() == s.Bits,
                    $"{tableName}.{s.Member} is {s.Bits} bits on both sides");
            }

            if (s.Function != "ModelIndex")
            {
                Assert.True(FlagsOf(s.Flags) == m.Flags,
                    $"{tableName}.{s.Member} carries {(s.Flags.Length == 0 ? "0" : s.Flags)} on both sides");
            }

            Assert.True(NumberOf(s.Low) == m.Low && NumberOf(s.High) == m.High,
                $"{tableName}.{s.Member} spans [{NumberOf(s.Low)}, {NumberOf(s.High)}] on both sides");
        }
    }

    private static readonly Dictionary<string, TeBits> BitsSymbols = new(StringComparer.Ordinal)
    {
        ["MAX_EDICT_BITS"]                    = TeBits.MaxEdict,
        ["SP_MODEL_INDEX_BITS"]               = TeBits.ModelIndex,
        ["EF_MAX_BITS"]                       = TeBits.EfMax,
        ["ANIMATION_SKIN_BITS"]               = TeBits.AnimationSkin,
        ["NUM_NETWORKED_EHANDLE_BITS"]        = TeBits.NetworkedEHandle,
        ["MAX_EFFECT_FLAG_BITS"]              = TeBits.EffectFlag,
        ["MAX_EFFECT_DISPATCH_STRING_BITS"]   = TeBits.EffectDispatchString,
    };

    private static TePropKind KindOf(string sendPropFunction) => sendPropFunction switch
    {
        "Int"        => TePropKind.Int,
        "ModelIndex" => TePropKind.Int,      // SendPropInt( x, SP_MODEL_INDEX_BITS, 0 )
        "Float"      => TePropKind.Float,
        "Vector"     => TePropKind.Vector,
        "Angle"      => TePropKind.Angle,
        "QAngles"    => TePropKind.QAngles,
        _            => throw new InvalidOperationException(
                            $"SendProp{sendPropFunction} is not a kind this knows"),
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

    /// <summary>A stock numeric literal — <c>25.6</c>, <c>4096.0f</c>, or absent.</summary>
    private static float NumberOf(string literal)
    {
        string text = literal.Trim().TrimEnd('f', 'F');

        return text.Length == 0 ? 0f
             : float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stock's own table, parsed out of its .cpp.
    ///
    /// <para>
    /// The arguments are split on TOP-LEVEL commas rather than matched by one
    /// regex, because this lane's tables have four argument shapes —
    /// <c>SendPropModelIndex( SENDINFO(x) )</c> with no width at all,
    /// <c>SendPropInt( SENDINFO(x), 24, SPROP_UNSIGNED )</c>,
    /// <c>SendPropInt( SENDINFO(x), MAX_EDICT_BITS, SPROP_UNSIGNED )</c> and
    /// <c>SendPropFloat( SENDINFO(x), 8, 0, 0.0, 25.6 )</c> — and a regex
    /// covering all four is a regex nobody can check by reading.
    /// </para>
    /// </summary>
    private static List<StockProp> StockTable(string fileName, string className, string tableName)
    {
        string path = ValveSdk.Server(fileName);

        var block = Regex.Match(File.ReadAllText(path),
            @"IMPLEMENT_SERVERCLASS_ST(?:_NOBASE)?\s*\(\s*" + Regex.Escape(className) + @"\s*,\s*"
            + Regex.Escape(tableName) + @"\s*\)(.*?)END_SEND_TABLE", RegexOptions.Singleline);

        if (!block.Success)
            throw new InvalidOperationException($"{fileName} has no {className} / {tableName} send table");

        var props = new List<StockProp>();

        foreach (Match call in Regex.Matches(block.Groups[1].Value, @"SendProp(\w+)\s*\("))
        {
            string function = call.Groups[1].Value;
            string body     = ArgumentsOf(block.Groups[1].Value, call.Index + call.Length);
            var    args     = SplitTopLevel(body);

            var sendinfo = Regex.Match(args[0], @"SENDINFO(?:_VECTORELEM|_ARRAYELEM)?\s*\(\s*(\w+)");

            if (!sendinfo.Success)
                throw new InvalidOperationException($"{tableName}: '{args[0]}' is not a SENDINFO");

            props.Add(new StockProp(
                Function: function,
                Member:   sendinfo.Groups[1].Value,
                Bits:     args.Count > 1 ? args[1] : "",
                Flags:    args.Count > 2 ? args[2] : "",
                Low:      args.Count > 3 ? args[3] : "",
                High:     args.Count > 4 ? args[4] : ""));
        }

        return props;
    }

    /// <summary>The text between an already-opened paren and its match.</summary>
    private static string ArgumentsOf(string text, int afterOpenParen)
    {
        int depth = 1;

        for (int i = afterOpenParen; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0)
                return text[afterOpenParen..i];
        }

        throw new InvalidOperationException("unbalanced parentheses in a send table");
    }

    private static List<string> SplitTopLevel(string arguments)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;

        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == '(') depth++;
            else if (arguments[i] == ')') depth--;
            else if (arguments[i] == ',' && depth == 0)
            {
                parts.Add(arguments[start..i].Trim());
                start = i + 1;
            }
        }

        string last = arguments[start..].Trim();

        if (last.Length > 0)
            parts.Add(last);

        return parts;
    }
}
