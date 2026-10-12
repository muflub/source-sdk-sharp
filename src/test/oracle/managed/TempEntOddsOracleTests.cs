using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// Lane F of the temp entity port — the odd ones, the restored pair, and Dust:
/// <c>DT_TEBeamSpline</c>, <c>DT_TEClientProjectile</c>,
/// <c>DT_TEMuzzleFlash</c>, <c>DT_TEImpact</c>, <c>DT_TEGaussExplosion</c> and
/// <c>DT_TEDust</c>, against plan_tmp.md Part 6 and against the stock tables
/// they were transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> gives at length and lanes A and C both
/// proved: <c>cs_sv_te_dump</c> cannot catch a wrong-but-consistent width, flag,
/// sign or range, because both its columns come from the same C# declaration.
/// So every one of these six tables is ALSO read off disk and compared prop for
/// prop — name, kind, width, flags and the [low, high] range.
/// </para>
///
/// <para>
/// THE PARITY SOURCES ARE NOT ALL <c>te_*.cpp</c>, AND THAT IS THIS LANE:
/// </para>
/// <list type="bullet">
///   <item><c>te_beamspline.cpp</c>, <c>te_clientprojectile.cpp</c> and
///     <c>te_muzzleflash.cpp</c> — evicted by this lane, still in the tree.</item>
///   <item><c>te_impact.cpp</c> and <c>hl2/te_gaussexplosion.cpp</c> — excluded
///     from this build long BEFORE the port (ledger rows S2 and S3), never
///     evicted by anyone, and still in the tree for the same reason.</item>
///   <item><c>func_dust.cpp</c> for <c>DT_TEDust</c>. Its table had been moved
///     VERBATIM into <c>shell_entity_orphans.cpp</c>, which is where this lane
///     evicted it from — so the orphans copy no longer exists and
///     <c>func_dust.cpp</c> is the surviving original. The two were identical
///     text; the commit that moved it says so, and this file is the one still
///     on disk to read.</item>
/// </list>
///
/// <para>
/// This lane's risk is concentrated in three encodings that no byte total and
/// no prop count would catch, and each has assertions of its own: the ARRAY's
/// element count and stride (16 and 12, read off stock's own
/// <c>CNetworkArray</c> and <c>#define</c>), the EHANDLE's symbolic width and
/// the composition <see cref="TempEntWriter"/> writes into the blob, and DUST's
/// four-bit direction over an explicit [-1, 1] with NO flags — which is one
/// <c>SPROP_NORMAL</c> away from a completely different bitstream.
/// </para>
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> The stock files these facts
/// read are no longer compiled by either DLL, so the farm no longer links them;
/// the facts that compare the port against them live here and read the pristine
/// upstream clone through <see cref="ValveSdk"/>, where a missing file is a
/// failure rather than a skip. The rest of TempEntOddsTests stays where it was.
/// </para>
/// </summary>
public class TempEntOddsOracleTests
{

    [Fact]
    public void BeamSplineMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeBeamSpline>("te_beamspline.cpp", "CTEBeamSpline", "DT_TEBeamSpline",
            expected: ["Points", "Spline"]);

    /// <summary>
    /// The array's LENGTH, read out of stock's own declaration rather than
    /// typed beside the one under test.
    ///
    /// <para>
    /// THE SEND TABLE DOES NOT CARRY IT. <c>SendPropArray</c> takes the element
    /// template and the member name; the count comes from
    /// <c>CNetworkArray( Vector, m_vecPoints, MAX_SPLINE_POINTS )</c> and the
    /// <c>#define</c> above it, which is why the parity check above cannot see
    /// it and this one exists. A managed <c>Elems</c> of 8 or 32 would pair with
    /// a client expecting 16 and produce a bitstream that stops making sense
    /// partway through — the failure with no log line.
    /// </para>
    /// </summary>
    [Fact]
    public void TheArrayIsAsLongAsStocksOwnDeclarationSaysAndAsWideAsItsElement()
    {
        (string? element, int count) = StockArray("te_beamspline.cpp", "m_vecPoints");

        TeField points = Build<TeBeamSpline>().Fields[1];

        Assert.True(count == 16,
            $"stock declares MAX_SPLINE_POINTS elements and that resolves to 16 — read {count}");
        Assert.True(points.Elems == count,
            $"the managed array is {points.Elems} elements where stock's is {count}");

        Assert.True(element == "Vector",
            $"stock's element type is a Vector — read '{element}'");
        Assert.True(points.ElemStride == 12,
            $"so the stride is twelve bytes, not {points.ElemStride}");

        // The blob has to be big enough to HOLD them, which is the half native
        // refuses out loud: SOURCESHARP_TE_REFUSED_OFFSET for a field running
        // past the declared blob.
        Assert.True(Build<TeBeamSpline>().BlobBytes >= points.Offset + count * points.ElemStride,
            "and the declared blob holds all sixteen — a field past the end is refused at "
          + "registration, and one that fits but is not backed networks whatever follows it");
    }

    [Fact]
    public void ClientProjectileMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeClientProjectile>("te_clientprojectile.cpp",
                                            "CTEClientProjectile", "DT_TEClientProjectile",
                                            expected: ["Origin", "Velocity", "ModelIndex",
                                                       "LifeTime", "Owner"]);

    [Fact]
    public void MuzzleFlashMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeMuzzleFlash>("te_muzzleflash.cpp", "CTEMuzzleFlash", "DT_TEMuzzleFlash",
                                       expected: ["Origin", "Angles", "Scale", "Type"]);

    [Fact]
    public void ImpactMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeImpact>("te_impact.cpp", "CTEImpact", "DT_TEImpact",
                                  expected: ["Origin", "Normal", "Type"]);

    [Fact]
    public void GaussExplosionMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeGaussExplosion>("hl2/te_gaussexplosion.cpp",
                                          "CTEGaussExplosion", "DT_TEGaussExplosion",
                                          ParticleSystemBytes, ["Type", "Direction"]);

    /// <summary>
    /// Parity for Dust against <c>func_dust.cpp</c>, and the note about which
    /// copy that is.
    ///
    /// <para>
    /// <c>CTEDust</c>'s table lived in TWO places: <c>func_dust.cpp</c>, which
    /// is excluded from this build, and <c>shell_entity_orphans.cpp</c>, where
    /// it had been moved verbatim so that <c>IEffects::Dust</c>'s live callers
    /// kept working. This lane deleted the orphans copy, so the file read here
    /// is <c>func_dust.cpp</c> — the surviving original, and the one the orphans
    /// copy was character-for-character taken from.
    /// </para>
    /// </summary>
    [Fact]
    public void DustMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeDust>("func_dust.cpp", "CTEDust", "DT_TEDust", ParticleSystemBytes,
                                ["Size", "Speed", "Direction"]);

    /// <summary>
    /// The parser read something, and it can tell apart the things this lane's
    /// six parity checks turn on.
    ///
    /// <para>
    /// WITHOUT THIS THE PARITY HALF COULD DEGRADE TO SILENCE: a parser that
    /// quietly matched nothing would compare empty tables (a missing file or
    /// table throws). Lane C added the same check for the same reason; this
    /// one asserts the two shapes lane C's parser does not have — an ARRAY whose
    /// element encoding is inside a nested call, and an EHANDLE with no
    /// arguments but a member at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStockParserReadsTheArrayAndTheEHandleItIsThereFor()
    {
        var spline     = StockTable("te_beamspline.cpp", "CTEBeamSpline", "DT_TEBeamSpline");
        var projectile = StockTable("te_clientprojectile.cpp",
                                    "CTEClientProjectile", "DT_TEClientProjectile");
        var dust       = StockTable("func_dust.cpp", "CTEDust", "DT_TEDust");

        Assert.True(spline.Count == 2 && projectile.Count == 5 && dust.Count == 3,
            $"the three tables parsed to the prop counts stock declares — read "
          + $"{spline.Count}, {projectile.Count}, {dust.Count}");

        // THE ARRAY. One prop out of a nested pair of calls, with the ELEMENT's
        // encoding and the member the DPT_Array prop names.
        Assert.True(spline[1].Function == "Array" && spline[1].Member == "m_vecPoints",
            "SendPropArray comes back as one prop called m_vecPoints, from the SENDINFO_ARRAY "
          + "inside its element template");
        Assert.True(spline[1].Element == "Vector" && spline[1].Bits == "-1"
                 && spline[1].Flags == "SPROP_COORD",
            "and the element's own encoding is read out of the nested SendPropVector, which is "
          + "the only place it appears");
        Assert.True(spline[1].ArrayMember == "m_vecPoints",
            "the array prop's second argument is the member itself, which is what the engine "
          + "links the two props by");

        // The nested call is NOT also counted as a prop of its own, which is
        // the mistake that would have made every parity check pass on a table
        // with one prop too many.
        Assert.True(spline[0].Function == "Int" && spline[0].Member == "m_nPoints",
            "and the count is still the first prop, so the nested SendPropVector was consumed "
          + "by the array rather than counted twice");

        // THE EHANDLE, whose arguments are not in the table text at all.
        Assert.True(projectile[4].Function == "EHandle" && projectile[4].Member == "m_hOwner"
                 && string.IsNullOrEmpty(projectile[4].Bits),
            "SendPropEHandle carries no width and no flags of its own; sendproxy.cpp:70 "
          + "supplies NUM_NETWORKED_EHANDLE_BITS and SPROP_UNSIGNED");
        Assert.True(projectile[2].Function == "ModelIndex"
                 && string.IsNullOrEmpty(projectile[2].Bits),
            "and neither does SendPropModelIndex — dt_send.h:750 supplies both");

        // DUST's range and its literal zero for flags, which is the difference
        // between the cheap normal and every other vector in the inventory.
        Assert.True(dust[2].Member == "m_vecDirection" && dust[2].Bits == "4",
            "m_vecDirection comes back as four bits");
        Assert.True(dust[2].Flags == "0",
            "with a literal 0 for flags — not SPROP_COORD and not SPROP_NORMAL");
        Assert.True(dust[2].Low == "-1.0f" && dust[2].High == "1.0f",
            "over [-1.0f, 1.0f], read with the f suffix stock writes and the parser strips");
    }

    private static int ParticleSystemBytes
        => TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes;

    private static TeClass Build<T>() => TeClassBuilder.Build(typeof(T));

    // ---------------------------------------------------------------------
    // Stock's own table, parsed out of its .cpp.
    // ---------------------------------------------------------------------

    /// <summary>
    /// One prop line out of a stock <c>IMPLEMENT_SERVERCLASS_ST</c> block.
    ///
    /// <para>
    /// <see cref="Element"/> and <see cref="ArrayMember"/> are non-empty only
    /// for a <c>SendPropArray</c>, whose encoding lives in a NESTED call — the
    /// one shape lane A's and lane C's parsers do not have, and the reason this
    /// is a third parser rather than a reuse. The three are worth consolidating
    /// once the lanes have landed; the note is here so that is a decision rather
    /// than an oversight.
    /// </para>
    /// </summary>
    private readonly record struct StockProp(
        string Function, string Member, string Bits, string Flags, string Low, string High,
        string Element = "", string ArrayMember = "");

    /// <summary>
    /// The whole parity check for one class: read stock's table off disk and
    /// assert the managed declaration agrees prop for prop — name, kind, width,
    /// flags and the [low, high] range.
    /// <para>
    /// <paramref name="expected"/> IS WHAT A CONVERTED DECLARATION NEEDS AND
    /// WHY THIS TEST DID NOT HAVE TO BE DELETED. A declaration that has dropped
    /// its <c>WireName</c> sends each prop under its C# MEMBER's name, so the
    /// one assertion that compared the two spellings can no longer hold — and
    /// the other three, which are what actually catch a transcription slip,
    /// are untouched. Passing the expected names keeps the mapping written down
    /// and checked: stock's spelling stays in the failure message, so prop 6 of
    /// nine is still readable as the one stock called <c>m_nSprayModel</c>.
    /// Null means "not converted yet", and those classes still assert the two
    /// spellings are identical.
    /// </para>
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
            $"stock's {tableName} declares {stock.Count} prop(s) and the managed table has "
          + $"{mine.Length}");

        Assert.True(expected is null || expected.Length == stock.Count,
            $"{tableName}: {expected?.Length} expected name(s) for stock's {stock.Count} prop(s) — "
          + "the list is per-position, so a missing entry would silently stop checking a prop");

        for (int i = 0; i < stock.Count; i++)
        {
            StockProp s = stock[i];
            TeField   m = mine[i];

            // MATCHED BY POSITION, which is what order-is-wire-order always
            // meant; only the NAME expectation moves when a class converts, and
            // stock's own spelling stays in the message so the mapping is still
            // readable from the failure.
            string want = expected is not null ? expected[i] : s.Member;

            Assert.True(m.Name == want,
                expected is not null
                    ? $"{tableName} prop {i} is '{want}' on the wire, where stock called it {s.Member}"
                    : $"{tableName} prop {i} is called {s.Member} on both sides");
            Assert.True(KindOf(s) == m.Kind,
                $"{tableName}.{s.Member} is a SendProp{s.Function} on both sides");

            // The width, in whichever of the three forms stock used: a macro
            // that supplies its own, a symbol, or a literal.
            if (s.Function == "ModelIndex")
            {
                Assert.True(m.BitsFrom == TeBits.ModelIndex,
                    $"{tableName}.{s.Member} takes its width from SP_MODEL_INDEX_BITS, as the macro does");
                Assert.True(m.Flags == TeFlags.None,
                    $"{tableName}.{s.Member} carries no flags, as SendPropModelIndex's own 0 says");
                continue;
            }

            if (s.Function == "EHandle")
            {
                Assert.True(m.BitsFrom == TeBits.NetworkedEHandle,
                    $"{tableName}.{s.Member} takes its width from NUM_NETWORKED_EHANDLE_BITS, "
                  + "as sendproxy.cpp:70 does");
                Assert.True(m.Flags == TeFlags.Unsigned,
                    $"{tableName}.{s.Member} is SPROP_UNSIGNED, which that line ORs in unconditionally");
                continue;
            }

            Assert.True(m.BitsFrom == TeBits.Literal,
                $"{tableName}.{s.Member}'s width is a literal in stock's table, so it is a literal here");
            Assert.True(m.Bits.ToString(CultureInfo.InvariantCulture) == s.Bits,
                $"{tableName}.{s.Member} is {s.Bits} bits on both sides");
            Assert.True(FlagsOf(s.Flags) == m.Flags,
                $"{tableName}.{s.Member} carries {(s.Flags.Length == 0 ? "0" : s.Flags)} on both sides");

            Assert.True(NumberOf(s.Low) == m.Low && NumberOf(s.High) == m.High,
                $"{tableName}.{s.Member} spans [{NumberOf(s.Low)}, {NumberOf(s.High)}] on both sides");
        }
    }

    private static TePropKind KindOf(StockProp s) => s.Function switch
    {
        "Int"        => TePropKind.Int,
        "ModelIndex" => TePropKind.Int,      // SendPropInt( x, SP_MODEL_INDEX_BITS, 0 )
        "EHandle"    => TePropKind.Int,      // SendPropInt( x, NUM_NETWORKED_EHANDLE_BITS, UNSIGNED )
        "Float"      => TePropKind.Float,
        "Vector"     => TePropKind.Vector,
        "Angle"      => TePropKind.Angle,
        "QAngles"    => TePropKind.QAngles,
        "Array"      => TePropKind.Array,
        _            => throw new InvalidOperationException(
                            $"SendProp{s.Function} is not a kind this knows"),
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

    /// <summary>A stock numeric literal — <c>25.6</c>, <c>-1.0f</c>, or absent.</summary>
    private static float NumberOf(string literal)
    {
        string text = literal.Trim().TrimEnd('f', 'F');

        return text.Length == 0 ? 0f : float.Parse(text, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stock's own table, parsed out of its .cpp in the pristine Valve SDK. A
    /// missing file or table throws: an oracle never passes by reading nothing.
    ///
    /// <para>
    /// THE ARGUMENTS ARE SPLIT ON BALANCED PARENTHESES rather than matched by
    /// one regex: <c>SENDINFO(m_flSize )</c> is itself a call, and a regex
    /// taking "everything up to the next comma" would read
    /// <c>0, -1.0f, 1.0f</c> as one flag expression — which is precisely the
    /// range this lane needs checked.
    /// </para>
    ///
    /// <para>
    /// A <c>SendPropArray</c> is ONE prop here and its nested element template
    /// is CONSUMED rather than counted again. Stock's one macro emits two props
    /// and declares one member; the managed declaration is one field with
    /// <c>Elems</c> and <c>ElemStride</c>, and native expands it back into the
    /// pair. Counting the nested call separately would make the prop counts
    /// agree with a table that has one prop too many.
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

        string body = block.Groups[1].Value;
        var props = new List<StockProp>();
        int consumedTo = 0;

        foreach (Match call in Regex.Matches(body, @"SendProp(\w+)\s*\("))
        {
            // Inside a SendPropArray's arguments: already read as its element.
            if (call.Index < consumedTo)
                continue;

            string function = call.Groups[1].Value;
            int    open     = call.Index + call.Length;
            string args     = ArgumentsOf(body, open);
            var    parts    = SplitTopLevel(args);

            if (function == "Array")
            {
                consumedTo = open + args.Length;

                var element = Regex.Match(parts[0], @"SendProp(\w+)\s*\(");

                if (!element.Success)
                    throw new InvalidOperationException($"{tableName}: SendPropArray without a template");

                var inner = SplitTopLevel(ArgumentsOf(parts[0], element.Index + element.Length));

                props.Add(new StockProp(
                    Function:    "Array",
                    Member:      MemberOf(tableName, inner[0]),
                    Bits:        inner.Count > 1 ? inner[1] : "",
                    Flags:       inner.Count > 2 ? inner[2] : "",
                    Low:         inner.Count > 3 ? inner[3] : "",
                    High:        inner.Count > 4 ? inner[4] : "",
                    Element:     element.Groups[1].Value,
                    ArrayMember: parts.Count > 1 ? parts[1] : ""));

                continue;
            }

            props.Add(new StockProp(
                Function: function,
                Member:   MemberOf(tableName, parts[0]),
                Bits:     parts.Count > 1 ? parts[1] : "",
                Flags:    parts.Count > 2 ? parts[2] : "",
                Low:      parts.Count > 3 ? parts[3] : "",
                High:     parts.Count > 4 ? parts[4] : ""));
        }

        return props;
    }

    /// <summary>
    /// The array's element type and length, out of stock's own
    /// <c>CNetworkArray</c> and the <c>#define</c> it names — because the SEND
    /// TABLE does not carry either.
    /// </summary>
    private static (string? Element, int Count) StockArray(string fileName, string member)
    {
        string text = File.ReadAllText(ValveSdk.Server(fileName));

        var declared = Regex.Match(text,
            @"CNetworkArray\s*\(\s*(\w+)\s*,\s*" + Regex.Escape(member) + @"\s*,\s*([A-Za-z0-9_]+)\s*\)");

        if (!declared.Success)
            return (null, 0);

        string length = declared.Groups[2].Value;

        if (int.TryParse(length, out int literal))
            return (declared.Groups[1].Value, literal);

        // A #define in the same file, which is where MAX_SPLINE_POINTS lives —
        // stock defines its own copy in te_beamspline.cpp AND in
        // c_te_beamspline.cpp rather than sharing a header.
        var defined = Regex.Match(text, @"#define\s+" + Regex.Escape(length) + @"\s+(\d+)");

        return defined.Success
            ? (declared.Groups[1].Value, int.Parse(defined.Groups[1].Value, CultureInfo.InvariantCulture))
            : (declared.Groups[1].Value, 0);
    }

    private static string MemberOf(string tableName, string sendinfo)
    {
        var member = Regex.Match(sendinfo, @"SENDINFO(?:_VECTORELEM|_ARRAYELEM|_ARRAY)?\s*\(\s*(\w+)");

        return member.Success
            ? member.Groups[1].Value
            : throw new InvalidOperationException($"{tableName}: '{sendinfo}' is not a SENDINFO");
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
