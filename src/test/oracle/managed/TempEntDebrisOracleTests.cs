using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// Lane E of the temp entity port — physical debris: <c>DT_TEBreakModel</c>,
/// <c>DT_TEPhysicsProp</c>, <c>DT_TEExplosion</c>,
/// <c>DT_TEShatterSurface</c>, <c>DT_TELargeFunnel</c> and
/// <c>DT_TEKillPlayerAttachments</c>, against plan_tmp.md Part 6 and against
/// the stock tables they were transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> gives at length and lanes A and C both
/// proved: <c>cs_sv_te_dump</c> cannot catch a wrong-but-consistent width,
/// because both of its columns derive from the same C# declaration. Only a
/// check against stock's own text can. So every table here is also read out of
/// <c>game/server/te_*.cpp</c> and compared PROP for prop — not field for
/// field, which is the distinction this lane turns on.
/// </para>
///
/// <para>
/// THIS LANE'S PARSER READS THE <c>SENDINFO</c> MACRO ITSELF, which no earlier
/// lane needed. Three of its hazards are invisible to a parser that only sees
/// the member name:
/// </para>
/// <list type="bullet">
/// <item><c>SENDINFO_VECTORELEM</c> vs <c>SENDINFO_ARRAYELEM</c>: both expand
/// to <c>"name[i]"</c> and only the first NEGATES the offset so the engine sets
/// <c>SPROP_IS_A_VECTOR_ELEM</c>. <c>DT_TEBreakModel</c>'s angles are the one
/// and <c>DT_TEShatterSurface</c>'s colours are the other, in this one
/// lane.</item>
/// <item><c>SendPropAngle</c> vs <c>SendPropVector</c>/<c>SendPropQAngles</c>:
/// three props against one, and <c>DPT_Float</c> against
/// <c>DPT_Vector</c>.</item>
/// <item>A SYMBOLIC width against a literal one. The parser keeps
/// <c>ANIMATION_SKIN_BITS</c> as TEXT and the comparison maps it to
/// <see cref="TeBits.AnimationSkin"/> — because both symbols resolve to ten
/// today, and a check that compared evaluated numbers would agree with a
/// declaration that had restated the ten and drifts the day either header
/// moves.</item>
/// </list>
///
/// <para>
/// The <c>te_*.cpp</c> files are excluded from the BUILD — a managed
/// ServerClass of a name cannot coexist with stock's (plan 2E) — but are still
/// in the tree. Part 7 deletes them; from then on these skip with the reason
/// printed and the Part 6 rows are what is left.
/// </para>
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> The stock files these facts
/// read are no longer compiled by either DLL, so the farm no longer links them;
/// the facts that compare the port against them live here and read the pristine
/// upstream clone through <see cref="ValveSdk"/>, where a missing file is a
/// failure rather than a skip. The rest of TempEntDebrisTests stays where it was.
/// </para>
/// </summary>
public class TempEntDebrisOracleTests
{
    [Fact]
    public void BreakModelMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeBreakModel>("te_breakmodel.cpp", "CTEBreakModel", "DT_TEBreakModel",
                                      expected: ["Origin", "Rotation[0]", "Rotation[1]",
                                                 "Rotation[2]", "Size", "Velocity", "ModelIndex",
                                                 "Randomization", "Count", "Time", "Flags"]);

    [Fact]
    public void PhysicsPropMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TePhysicsProp>("te_physicsprop.cpp", "CTEPhysicsProp", "DT_TEPhysicsProp",
                                       expected: ["Origin", "Rotation[0]", "Rotation[1]",
                                                  "Rotation[2]", "Velocity", "ModelIndex", "Skin",
                                                  "Flags", "Effects"]);

    [Fact]
    public void ExplosionMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeExplosion>("te_explosion.cpp", "CTEExplosion", "DT_TEExplosion",
                                     ParticleSystemBytes,
                                     ["ModelIndex", "Scale", "FrameRate", "Flags",
                                      "Normal", "MaterialType", "Radius", "Magnitude"]);

    [Fact]
    public void ShatterSurfaceMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeShatterSurface>("te_glassshatter.cpp", "CTEShatterSurface",
                                          "DT_TEShatterSurface",
                                          expected: ["Origin", "Angles", "Force", "ForcePos",
                                                     "Width", "Height", "ShardSize", "SurfaceType",
                                                     "FrontColorR", "FrontColorG", "FrontColorB",
                                                     "BackColorR", "BackColorG", "BackColorB"]);

    [Fact]
    public void LargeFunnelMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeLargeFunnel>("te_largefunnel.cpp", "CTELargeFunnel", "DT_TELargeFunnel",
                                       ParticleSystemBytes, ["ModelIndex", "Reversed"]);

    [Fact]
    public void KillPlayerAttachmentsMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeKillPlayerAttachments>("te_killplayerattachments.cpp",
                                                 "CTEKillPlayerAttachments",
                                                 "DT_TEKillPlayerAttachments",
                                                 expected: ["Player"]);

    /// <summary>
    /// The parser read something, and it can tell apart the three things this
    /// lane's parity checks turn on.
    ///
    /// <para>
    /// WITHOUT THIS THE PARITY HALF COULD DEGRADE TO SILENCE: a parser that
    /// quietly matched nothing would compare empty tables. A missing file or
    /// table throws (<see cref="ValveSdk"/>); this is the guard on the parse.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStockParserTellsVectorElemFromArrayElemAndASymbolFromANumber()
    {
        var gibs  = StockTable("te_breakmodel.cpp", "CTEBreakModel", "DT_TEBreakModel");
        var prop  = StockTable("te_physicsprop.cpp", "CTEPhysicsProp", "DT_TEPhysicsProp");
        var glass = StockTable("te_glassshatter.cpp", "CTEShatterSurface", "DT_TEShatterSurface");

        Assert.True(gibs.Count == 11 && prop.Count == 9 && glass.Count == 14,
            "all three tables parsed to the prop count stock declares");

        // THE MACRO, which decides whether the offset is negated and therefore
        // whether the engine sets SPROP_IS_A_VECTOR_ELEM.
        Assert.True(gibs[1].Macro == "SENDINFO_VECTORELEM" && gibs[1].Member == "m_angRotation"
                 && gibs[1].ElemIndex == 0,
            "m_angRotation[0] comes back as a SENDINFO_VECTORELEM at index 0");
        Assert.True(glass[8].Macro == "SENDINFO_ARRAYELEM" && glass[8].Member == "m_uchFrontColor"
                 && glass[8].ElemIndex == 0,
            "and m_uchFrontColor[0] as a SENDINFO_ARRAYELEM — the parser distinguishes the two "
            + "macros this lane mixes, which expand to the same NAME and different offsets");
        Assert.True(gibs[0].Macro == "SENDINFO" && gibs[0].ElemIndex < 0,
            "a plain SENDINFO comes back with no element index at all");

        // THE FUNCTION, which decides DPT_Float against DPT_Vector and three
        // props against one.
        Assert.True(gibs[0].Function == "Vector" && gibs[1].Function == "Angle",
            "m_vecOrigin is a SendPropVector and m_angRotation[0] a SendPropAngle — a parser that "
            + "answered the same for both would pass every parity assertion in this file");
        Assert.True(glass[1].Function == "Vector" && glass[1].Member == "m_vecAngles",
            "and m_vecAngles comes back as a SendPropVector, though its member is a CNetworkQAngle");

        // THE WIDTH AS TEXT. Both symbols resolve to ten today, so a check that
        // compared evaluated numbers would agree with a declaration that had
        // restated the ten.
        Assert.True(prop[6].Member == "m_nSkin" && prop[6].Bits == "ANIMATION_SKIN_BITS",
            "m_nSkin's width comes back as the SYMBOL, not as 10");
        Assert.True(prop[8].Member == "m_nEffects" && prop[8].Bits == "EF_MAX_BITS",
            "and m_nEffects' as its own symbol, which is a different symbol in a different header");
        Assert.True(prop[7].Member == "m_nFlags" && prop[7].Bits == "2",
            "while a literal comes back as a number");

        // THE SIGN, invisible in any width.
        Assert.True(prop[6].Flags is null,
            "m_nSkin's line passes NO flags argument at all, which is what makes it signed");
        Assert.True(prop[8].Flags == "SPROP_UNSIGNED",
            "and m_nEffects' passes SPROP_UNSIGNED on the very next line");

        // THE RANGE AND THE ROUNDING, on the float this lane could most easily
        // have given a rounding direction it does not have.
        Assert.True(gibs[9].Member == "m_fTime" && gibs[9].Flags == "0" && gibs[9].High == "102.4",
            "m_fTime comes back with a literal 0 for flags and 102.4 for its high bound");
    }

    /// <summary>
    /// <c>DT_TEParticleSystem</c>'s blob, computed rather than written down —
    /// the two tables that nest it start their own fields after it.
    /// </summary>
    private static int ParticleSystemBytes
        => TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes;

    private static TeClass Build<T>() => TeClassBuilder.Build(typeof(T));

    /// <summary>
    /// One field expanded into the props it becomes — the same rule
    /// <c>SourceSharpTe_FieldPropCount</c> and <c>InternPropName</c> apply
    /// natively: N props named <c>name[0..N-1]</c> for a field with
    /// <c>Elems &gt; 1</c>, one named <c>name</c> otherwise.
    /// </summary>
    private readonly record struct ManagedProp(string Name, int ElemIndex, TeField Field);

    private static List<ManagedProp> PropsOf(IEnumerable<TeField> fields)
    {
        var props = new List<ManagedProp>();

        foreach (TeField f in fields)
        {
            int n = f.Elems > 1 ? f.Elems : 1;

            for (int i = 0; i < n; i++)
                props.Add(new ManagedProp(n > 1 ? $"{f.Name}[{i}]" : f.Name, n > 1 ? i : -1, f));
        }

        return props;
    }

    // ---------------------------------------------------------------------
    // Stock's own table, parsed out of its .cpp.
    // ---------------------------------------------------------------------

    /// <summary>
    /// One prop line out of a stock <c>IMPLEMENT_SERVERCLASS_ST</c> block.
    ///
    /// <para>
    /// <see cref="Macro"/> and <see cref="ElemIndex"/> are this lane's addition
    /// to the shape lanes B and C used. Without them
    /// <c>SENDINFO_VECTORELEM(m_angRotation, 0)</c> and
    /// <c>SENDINFO_ARRAYELEM(m_uchFrontColor, 0)</c> are indistinguishable —
    /// same member spelling, same expanded name, opposite sign on the offset.
    /// </para>
    /// </summary>
    private readonly record struct StockProp(
        string Function, string Macro, string Member, int ElemIndex,
        string? Bits, string? Flags, string? Low, string? High)
    {
        /// <summary>The name the prop is actually declared under — brackets included.</summary>
        public string PropName => ElemIndex >= 0 ? $"{Member}[{ElemIndex}]" : Member;

        /// <summary>Whether the macro NEGATES the offset, which is the whole difference.</summary>
        public bool IsVectorElem => Macro == "SENDINFO_VECTORELEM";
    }

    /// <summary>
    /// The whole parity check for one class: read stock's table off disk and
    /// assert the managed declaration agrees PROP for prop — name, kind, width
    /// (symbol or literal), flags, range, and whether the offset is negated.
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

        List<ManagedProp> mine = PropsOf(TeClassBuilder.Build(typeof(T), baseBytes).Fields);

        // PROP COUNT, not field count. A table declared with one QAngles prop
        // where stock declares three angles has the right fields and the wrong
        // wire, and this is the line that says so.
        Assert.True(stock.Count == mine.Count,
            $"stock's {tableName} declares {stock.Count} props and the managed one {mine.Count}");

        Assert.True(expected is null || expected.Length == stock.Count,
            $"{tableName}: {expected?.Length} expected name(s) for stock's {stock.Count} prop(s) — "
          + "the list is per-position, so a missing entry would silently stop checking a prop");

        for (int i = 0; i < stock.Count; i++)
        {
            StockProp s = stock[i];
            ManagedProp m = mine[i];
            TeField f = m.Field;

            // MATCHED BY POSITION, which is what order-is-wire-order always
            // meant; only the NAME expectation moves when a class converts, and
            // stock's own spelling stays in the message so the mapping is still
            // readable from the failure.
            string want = expected is not null ? expected[i] : s.PropName;

            Assert.True(m.Name == want,
                expected is not null
                    ? $"prop {i} is '{want}' on the wire, where stock called it {s.PropName}"
                    : $"prop {i} is declared as {s.PropName} on both sides");
            Assert.True(KindOf(s.Function) == f.Kind,
                $"prop {i} is a SendProp{s.Function} on both sides");

            // THE MACRO. SENDINFO_ARRAYELEM's offset is positive and
            // SENDINFO_VECTORELEM's is negated, which is the only difference
            // between this lane's two "one field, N props" shapes.
            Assert.True(f.Flags.HasFlag(TeFlags.VectorElem) == s.IsVectorElem,
                $"prop {i} is a {s.Macro} on both sides — the offset is "
                + (s.IsVectorElem ? "negated" : "not negated"));

            if (s.Function == "ModelIndex")
            {
                // SendPropModelIndex takes no width and no flags of its own:
                // dt_send.h:750 supplies SP_MODEL_INDEX_BITS and 0.
                Assert.True(f.BitsFrom == TeBits.ModelIndex,
                    $"prop {i} takes its width from SP_MODEL_INDEX_BITS, as stock's macro does");
                Assert.True(f.Flags == TeFlags.None,
                    $"prop {i} carries no flags, as SendPropModelIndex's own 0 says");
                continue;
            }

            // THE WIDTH, AS TEXT. A symbol must be the matching TeBits and a
            // number must be a literal of that value. Comparing resolved
            // numbers would agree with a declaration that had restated
            // ANIMATION_SKIN_BITS as 10, which is the failure this whole
            // mechanism exists to prevent.
            TeBits from = SymbolOf(s.Bits);

            Assert.True(f.BitsFrom == from,
                $"prop {i}'s width comes from {(from == TeBits.Literal ? "a literal" : from.ToString())} "
                + $"on both sides — stock's table says '{s.Bits}'");

            if (from == TeBits.Literal)
            {
                Assert.True(f.Bits.ToString(CultureInfo.InvariantCulture) == s.Bits,
                    $"prop {i} is {s.Bits} bits on both sides");
            }

            Assert.True(FlagsOf(s.Flags) == (f.Flags & ~TeFlags.VectorElem),
                $"prop {i} carries {s.Flags ?? "(none)"} on both sides");

            Assert.True(NumberOr(s.Low, 0f) == f.Low,
                $"prop {i}'s range starts at {s.Low ?? "0"} on both sides");
            Assert.True(NumberOr(s.High, 0f) == f.High,
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

    /// <summary>
    /// A width argument as stock WROTE it: which symbol, or
    /// <see cref="TeBits.Literal"/> for a number. Anything else throws rather
    /// than being taken for a literal — an unrecognised symbol quietly read as
    /// zero bits is precisely the silent disagreement the symbols exist to
    /// prevent.
    /// </summary>
    private static TeBits SymbolOf(string? bits)
    {
        string text = (bits ?? "").Trim();

        if (text.Length == 0 || int.TryParse(text, NumberStyles.Integer,
                                             CultureInfo.InvariantCulture, out _))
            return TeBits.Literal;

        return text switch
        {
            "MAX_EDICT_BITS"                 => TeBits.MaxEdict,
            "SP_MODEL_INDEX_BITS"            => TeBits.ModelIndex,
            "EF_MAX_BITS"                    => TeBits.EfMax,
            "ANIMATION_SKIN_BITS"            => TeBits.AnimationSkin,
            "MAX_EFFECT_FLAG_BITS"           => TeBits.EffectFlag,
            "MAX_EFFECT_DISPATCH_STRING_BITS"=> TeBits.EffectDispatchString,
            "NUM_NETWORKED_EHANDLE_BITS"     => TeBits.NetworkedEHandle,
            _ => throw new InvalidOperationException(
                $"'{text}' is not a width symbol this knows; plan 3.1 lists the nine"),
        };
    }

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
    /// one regex, which is lane B's fix and not this lane's:
    /// <c>SENDINFO(m_fScale )</c> is itself a call, and a regex that took
    /// "everything up to the next comma" for the width would read
    /// <c>0, 0, 102.4</c> as one flag expression.
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
        // defines (plan 2G).
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

            // THE MACRO AND THE ELEMENT INDEX, not just the member. See
            // StockProp for why.
            var member = Regex.Match(parts[0],
                @"^(SENDINFO\w*)\s*\(\s*(\w+)\s*(?:,\s*(\d+)\s*)?\)$");

            if (!member.Success)
                continue;

            props.Add(new StockProp(
                Function:  function,
                Macro:     member.Groups[1].Value,
                Member:    member.Groups[2].Value,
                ElemIndex: member.Groups[3].Success
                    ? int.Parse(member.Groups[3].Value, CultureInfo.InvariantCulture) : -1,
                Bits:      parts.Count > 1 ? parts[1] : null,
                Flags:     parts.Count > 2 ? parts[2] : null,
                Low:       parts.Count > 3 ? parts[3] : null,
                High:      parts.Count > 4 ? parts[4] : null));
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
        // is not a further argument.
        if (parts.Count > 0 && parts[^1].Length == 0)
            parts.RemoveAt(parts.Count - 1);

        return parts;
    }
}
