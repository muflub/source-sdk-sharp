using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// S-Phase 4 — <c>DT_TEEffectDispatch</c>, <c>DT_EffectData</c> and the effect
/// name index — against plan_tmp.md Part 6 and against the two stock tables
/// they were transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason every lane before
/// this one wrote down and lanes A and C proved: a fixture typed beside the
/// declaration is one person's reading of one table twice, and
/// <c>cs_sv_te_dump</c> cannot catch a wrong-but-consistent width because both
/// its columns come from the same C# declaration. So the parity half reads
/// <c>game/shared/effect_dispatch_data.cpp</c> and
/// <c>game/server/te_effect_dispatch.cpp</c> off disk and asserts the agreement
/// prop for prop.
/// </para>
///
/// <para>
/// FIVE THINGS ARE NEW HERE AND EACH IS ONE OF THIS TABLE'S HAZARDS.
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Two preprocessor forks in one table.</b> <c>DT_EffectData</c> has an
/// <c>#ifdef HL2_DLL</c> around its six origin floats and an
/// <c>#if defined( TF_DLL )</c> around its normal. The parser drops the taken-if
/// arm of each and keeps the <c>#else</c> — and
/// <see cref="NeitherHl2NorTfIsDefinedInThisBuild"/> READS EVERY COMPILE
/// COMMAND of server.so and client.so to prove that is the branch this build
/// compiles, rather than trusting plan 2G.
/// </description></item>
/// <item><description>
/// <b>A width symbol that is NOT the one the plan names.</b> The table says
/// <c>MAX_MODEL_INDEX_BITS</c>, which is <b>12</b> in <c>common/qlimits.h</c>;
/// <c>SP_MODEL_INDEX_BITS</c> — what <see cref="TeBits.ModelIndex"/> resolves to
/// and what plan Part 6's row for <c>m_nMaterial</c> asks for — is <b>13</b> in
/// <c>public/const.h</c>. Both headers are read here.
/// </description></item>
/// <item><description>
/// <b>Two <c>SendProp</c> helpers whose expansion is the whole prop.</b>
/// <c>SendPropBool</c> and <c>SendPropIntWithMinusOneFlag</c> are one-line
/// wrappers in <c>game/server/sendproxy.cpp</c>, and this reads THAT file too
/// rather than assuming what they expand to.
/// </description></item>
/// <item><description>
/// <b>A nested DATATABLE.</b> Forty-three tables nest a base; this is the only
/// one that nests a table as a FIELD. Its offsets, its blob and the writer's
/// reach into it are all checked below.
/// </description></item>
/// <item><description>
/// <b>An index that plays the WRONG effect rather than none.</b>
/// <c>m_iEffectName</c> is the only prop in the port with that failure mode, and
/// its neighbour three lines up carries a <c>+1</c> that it must not.
/// </description></item>
/// </list>
/// </summary>
public class TempEntEffectDataTests
{
    // ---------------------------------------------------------------------
    // The Part 6 rows.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheDispatchTableNestsOneDatatableAndNothingElse()
    {
        TeClass te = Dispatch();

        Assert.True(te.ClassName == "CTEEffectDispatch", "the two sides pair on CTEEffectDispatch");
        Assert.True(te.TableName == "DT_TEEffectDispatch", "over DT_TEEffectDispatch");
        Assert.True(te.BaseTable == "DT_BaseTempEntity",
            "and it nests DT_BaseTempEntity, as IMPLEMENT_SERVERCLASS_ST without _NOBASE does");
        Assert.True(te.RegistersClass,
            "it DOES claim a ServerClass: te_effect_dispatch.cpp is evicted from the build");

        Assert.True(te.Fields.Length == 1, "one prop, and it is the payload");

        TeField nested = te.Fields[0];

        Assert.True(nested.Name == "EffectData",
            "named EffectData — the C# member, since the declaration dropped WireName "
          + "(docs/te-declaration-conversion.md); stock's SENDINFO_DT stringifies m_EffectData "
          + "and both halves of THIS build take the name from this one declaration");
        Assert.True(nested.Kind == TePropKind.DataTable,
            "a SendPropDataTable and not twenty-eight flattened props — the client has a "
          + "datatable prop here and pairs on it by name");
        Assert.True(nested.SubTable == "DT_EffectData", "pointing at DT_EffectData");

        // THE OFFSET MUST NOT BE ZERO, AND IT IS THE WHOLE OF WHY THIS TABLE
        // CRASHED THE CLIENT ONCE. See TheNestedTableMayNotSitAtOffsetZero.
        Assert.True(nested.Offset == 4,
            "at offset FOUR and not zero — a nested datatable prop declared at zero is made "
          + "SPROP_COLLAPSIBLE by the engine, and a collapsed one is applied at the receiving "
          + "object's BASE rather than through this prop's offset");
    }

    /// <summary>
    /// The one rule this whole table turns on: SendPropDataTable's
    /// collapsible flag, which the server applies in C# since
    /// plan_server_native_v2 T4b (<see cref="SendPropPlan.DataTable"/>, held to
    /// stock's dt_send.cpp by SendPropPlanTests).
    ///
    /// <para>
    /// A managed test cannot see a segfault inside <c>engine.so</c>, so what it
    /// can do is assert that the CONDITION which produced it is the one the fix
    /// assumes: collapsible at offset zero and only there.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNestedTableMayNotSitAtOffsetZero()
    {
        Assert.True(Dispatch().Fields[0].Offset != 0,
            "the nested datatable prop is not at offset zero");
        Assert.True(Dispatch().BlobBytes == Data().BlobBytes + 4,
            "and the blob is four bytes longer than the payload because of it — the pad is "
          + "never written and never sent");

        Assert.True((SendPropPlan.DataTable("m_EffectData", Dispatch().Fields[0].Offset, 0).Flags
                     & SendPropPlan.Collapsible) == 0,
            "the nested table at its padded offset is not collapsible");
        Assert.True(SendPropPlan.DataTable("baseclass", 0, 0).Flags == SendPropPlan.Collapsible,
            "SendPropDataTable sets SPROP_COLLAPSIBLE when the proxy is the identity AND the offset is "
          + "zero -- which is why moving the nested table off zero is enough, and why the 'baseclass' "
          + "prop at zero is still collapsible as stock intends");
    }

    [Fact]
    public void TheEffectDataTableIsNobaseAndTwentyTwoFieldsBecomeTwentyEightProps()
    {
        TeClass te = Data();

        Assert.True(te.ClassName == "CEffectData", "CEffectData names the table");
        Assert.True(te.TableName == "DT_EffectData", "which is DT_EffectData");
        Assert.True(te.BaseTable is null,
            "NOBASE — BEGIN_SEND_TABLE_NOBASE, and CEffectData is a plain struct with no "
          + "hierarchy at all");
        Assert.True(!te.RegistersClass,
            "and it is a table and NEVER a ServerClass: BEGIN_SEND_TABLE_NOBASE builds no "
          + "class, so unlike DT_BaseTempEntity this does not flip to true when a native file "
          + "is evicted");

        Assert.True(te.Fields.Length == 22, "twenty-two C# fields");
        Assert.True(ExpandedProps(te).Count == 28,
            "which expand into twenty-eight props — three fields carry three each, and 22 + 6 "
          + "is what stock's table declares by hand");
    }

    /// <summary>
    /// Declaration order is wire order, so the whole list is asserted in order
    /// rather than looked up by name. Part 6's prose has <c>m_vNormal</c> before
    /// <c>m_vAngles</c> and the table has them the other way round; a test that
    /// looked each field up by name would pass on either.
    /// </summary>
    [Fact]
    public void TheWireOrderIsStocksTableOrderAndNotPartSixsProseOrder()
    {
        string[] order = Data().Fields.Select(f => f.Name).ToArray();

        Assert.True(order.SequenceEqual(new[]
        {
            "Origin", "Start", "Angles", "Normal",
            "Flags", "Magnitude", "Scale", "AttachmentIndex",
            "SurfaceProp", "EffectName", "Material", "DamageType",
            "HitBox", "EntIndex", "Color", "Radius",
            "CustomColors", "Color1", "Color2",
            "ControlPoint1", "ParticleAttachment",
            "ControlPointOffset",
        }), "the twenty-two fields are in the order effect_dispatch_data.cpp declares them");

        Assert.True(Array.IndexOf(order, "Angles") < Array.IndexOf(order, "Normal"),
            "Angles (stock's m_vAngles) comes BEFORE Normal (m_vNormal), which is the table's "
          + "order and not the order "
          + "plan Part 6's prose lists them in");
    }

    /// <summary>
    /// The 2G branch, on the two props it decides. Taking the HL2 arm would give
    /// coordinates a few inches out — a plausible value, which is the failure
    /// this whole port is scored against.
    /// </summary>
    [Fact]
    public void TheOriginAndStartTakeTheNonHl2IntegralForm()
    {
        TeClass te = Data();

        foreach (string name in new[] { "Origin", "Start" })
        {
            TeField f = Field(te, name);

            Assert.True(f.Kind == TePropKind.Float,
                $"{name} is three SendPropFloats and not one SendPropVector");
            Assert.True(f.Elems == 3 && f.ElemStride == 4,
                $"{name} expands to three props four bytes apart");
            Assert.True(f.Bits == -1, $"{name}'s width is -1, so the flag decides it");
            Assert.True(f.Flags == TeFlags.CoordMpIntegral,
                $"{name} is SPROP_COORD_MP_INTEGRAL — the #else arm. The HL2 arm asks for "
              + "COORD_INTEGER_BITS+SUBINCH_PRECISION over an explicit range, and a build that "
              + "took it would decode a few inches out with nothing saying so");
            Assert.True(!f.Flags.HasFlag(TeFlags.VectorElem),
                $"{name} carries NO VectorElem: stock spells the three elements out with "
              + "SENDINFO_NOCHECK and a positive offset, not with SENDINFO_VECTORELEM");
            Assert.True(f.SizeBytes == 4, $"{name}'s props read one float each, not twelve bytes");
        }

        TeField normal = Field(te, "Normal");

        Assert.True(normal.Kind == TePropKind.Vector && normal.Bits == 0
                 && normal.Flags == TeFlags.Normal,
            "Normal, stock's m_vNormal, is the non-TF arm: one SPROP_NORMAL vector at width 0, "
          + "not six bits over [-1,1]");
    }

    /// <summary>
    /// plan 2G is a claim about this build's defines, and this is the check.
    /// Read the -D definitions of EVERY compile command of both game DLLs
    /// (compile_commands.json, via <see cref="CMakeGraph.TargetEntries"/>)
    /// rather than the target's list or one command: a define added anywhere in
    /// the build, per target or per source, decides these two props.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void NeitherHl2NorTfIsDefinedInThisBuild()
    {
        // THE BUILD'S OWN COMMANDS AND NOT EVERY BUILD SCRIPT IN THE TREE. The
        // VPC form of this fact followed server_csharp.vpc / client_csharp.vpc
        // and their $Include chain, because the tree also carried
        // server_hl2.vpc, server_tf.vpc and three more that DO define HL2_DLL or
        // TF_DLL for other mods -- a scan that read them all reported the wrong
        // answer with total confidence the first time it was written.
        IReadOnlySet<string> server = DefinesOfEveryCommand(CMakeGraph.Server);
        IReadOnlySet<string> client = DefinesOfEveryCommand(CMakeGraph.Client);

        var offenders = new List<string>();

        foreach ((string target, IReadOnlySet<string> defs) in new[] { (CMakeGraph.Server, server), (CMakeGraph.Client, client) })
        {
            foreach (string def in defs)
            {
                string name = def.Split('=', 2)[0];
                if (name is "HL2_DLL" or "TF_DLL")
                    offenders.Add($"{target}: -D{def}");
            }
        }

        Assert.True(offenders.Count == 0,
            "nothing in this build's compile commands defines HL2_DLL or TF_DLL, which is "
          + "what decides DT_EffectData's six origin floats and its normal (plan 2G). Found: "
          + string.Join(", ", offenders));

        // And the scan can SEE a define, so the empty answer above means
        // something: each DLL's own side macro.
        Assert.True(server.Contains("GAME_DLL"),
            "the scan does find a define that IS there -- GAME_DLL on server_sourcesharp -- so "
          + "the two it does not find are absent rather than unlooked-for");
        Assert.True(client.Contains("CLIENT_DLL"),
            "the scan does find a define that IS there -- CLIENT_DLL on client_sourcesharp");
    }

    /// <summary>
    /// The -D definitions (NAME or NAME=VALUE) of every compile command of a
    /// target. <see cref="CMakeGraph.CompileDefinitions"/> reads only the first
    /// command, and a per-source define would be on some commands only.
    /// </summary>
    private static IReadOnlySet<string> DefinesOfEveryCommand(string target)
    {
        var defs = new SortedSet<string>(StringComparer.Ordinal);

        foreach (CMakeGraph.Entry e in CMakeGraph.TargetEntries(target))
        {
            foreach (string t in e.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (t.StartsWith("-D", StringComparison.Ordinal) && t.Length > 2)
                    defs.Add(t[2..]);
            }
        }

        return defs;
    }

    /// <summary>
    /// The proxy is gone and the arithmetic is not (plan 2B) — and the helper's
    /// expansion is READ rather than assumed, because everything the managed
    /// declaration claims about this prop comes out of one line of
    /// <c>sendproxy.cpp</c>.
    /// </summary>
    [Fact]
    public void TheSurfacePropIsAPlainEightBitUnsignedIntOverAShort()
    {
        TeField f = Field(Data(), "SurfaceProp");

        Assert.True(f.Kind == TePropKind.Int && f.Bits == 8 && f.BitsFrom == TeBits.Literal,
            "SurfaceProp, stock's m_nSurfaceProp, is eight literal bits");
        Assert.True(f.Flags == TeFlags.Unsigned,
            "UNSIGNED, which is what SendPropIntWithMinusOneFlag supplies");
        Assert.True(f.SizeBytes == 2,
            "over two bytes of blob, because CEffectData::m_nSurfaceProp is a short and "
          + "SendPropInt picks its read proxy from the declared storage width");

        // The width, the flag and the plus one are stock's own lines, read in
        // TempEntSurfacePropOracleTests (the server's copies of those two
        // helpers went in the server gc cull, 2026-10-01: nothing called them).
    }

    /// <summary>
    /// The field this phase exists for. Its width is a SYMBOL, and the value is
    /// the index and nothing else.
    /// </summary>
    [Fact]
    public void TheEffectNameIsTheStringTableIndexAtItsSymbolicWidth()
    {
        TeField f = Field(Data(), "EffectName");

        Assert.True(f.Kind == TePropKind.Int, "EffectName, stock's m_iEffectName, is an int");
        Assert.True(f.BitsFrom == TeBits.EffectDispatchString,
            "at MAX_EFFECT_DISPATCH_STRING_BITS, resolved natively from the header that owns it "
          + "— a restated 10 that drifted would play a DIFFERENT effect, not a misplaced one");
        Assert.True(f.Bits == -1,
            "and no literal width beside the symbol, so there is only one number");
        Assert.True(f.Flags == TeFlags.Unsigned, "unsigned, because an index is never negative");

        string? header = ReadSource(Path.Combine("src", "game", "shared", "effect_dispatch_data.h"));

        if (header is null)
        {
            Assert.True(true, "effect_dispatch_data.h is not beside the binary (SKIPPED)");
            return;
        }

        var m = Regex.Match(header, @"#define\s+MAX_EFFECT_DISPATCH_STRING_BITS\s+(\d+)");

        Assert.True(m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) == 10,
            "the symbol is ten bits in effect_dispatch_data.h, which is 1024 effect names — "
          + "the number TePropPlanner restates and TePropPlannerWidthsOracleTests "
          + "checks against this same header");
    }

    /// <summary>
    /// The bit that plan Part 6 gets wrong, and that would have been one bit
    /// wide of the client with no log line anywhere.
    /// </summary>
    [Fact]
    public void TheMaterialWidthIsQlimitsTwelveAndNotConstsThirteen()
    {
        TeField f = Field(Data(), "Material");

        Assert.True(f.BitsFrom == TeBits.Literal,
            "Material's width — stock's m_nMaterial — is a literal here, because TeBits has no member for "
          + "MAX_MODEL_INDEX_BITS and the ABI header is the integrator's");
        Assert.True(f.Flags == TeFlags.Unsigned, "and it is UNSIGNED, unlike SendPropModelIndex");

        string? qlimits = ReadSource(Path.Combine("src", "common", "qlimits.h"));
        string? consts  = ReadSource(Path.Combine("src", "public", "const.h"));

        if (qlimits is null || consts is null)
        {
            Assert.True(true, "qlimits.h/const.h are not beside the binary (SKIPPED)");
            return;
        }

        int maxModelIndexBits = DefineValue(qlimits, "MAX_MODEL_INDEX_BITS");
        int spModelIndexBits  = DefineValue(consts, "SP_MODEL_INDEX_BITS");

        Assert.True(maxModelIndexBits == 12, "qlimits.h's MAX_MODEL_INDEX_BITS is twelve");
        Assert.True(spModelIndexBits == 13, "const.h's SP_MODEL_INDEX_BITS is thirteen");
        Assert.True(maxModelIndexBits != spModelIndexBits,
            "the two model-index widths in this engine are DIFFERENT, which is the whole "
          + "reason this test exists");

        // The agreement, rather than the literal maintained by hand. This is
        // TierParityTests' answer to the same problem: a fixture standing in for
        // something native owns asserts the agreement.
        Assert.True(f.Bits == maxModelIndexBits,
            "and the declaration is MAX_MODEL_INDEX_BITS, read out of qlimits.h — plan Part 6's "
          + "row says 'ModelIndex u', which is SP_MODEL_INDEX_BITS and one bit too wide");
    }

    /// <summary>
    /// THE ONE PROP IN THIS TABLE WHOSE NAME WAS NEVER ITS MEMBER'S. Stock
    /// wrote <c>SENDINFO_NAME( m_nEntIndex, entindex )</c> because the CLIENT's
    /// <c>CEffectData</c> has no <c>m_nEntIndex</c> at all — it keeps a handle
    /// and fills it through <c>RecvProxy_EntIndex</c>, declared under the name
    /// <c>entindex</c> — and two hand-written tables had to agree on the
    /// string. Neither is hand-written now, so the prop is called after the
    /// member like every other one; what this asserts is that the rename landed
    /// on the RIGHT name, and that the C++ member's spelling did not leak in
    /// with it.
    /// </summary>
    [Fact]
    public void TheEntityIndexTakesItsMembersNameNowThatBothTablesAreOurs()
    {
        TeField f = Field(Data(), "EntIndex");

        Assert.True(f.Name == "EntIndex",
            "the wire name is the C# member's, EntIndex — where stock's table called this prop "
          + "'entindex' through SENDINFO_NAME and its member m_nEntIndex, because two "
          + "hand-written tables had to agree on a string and the client had no such member");
        Assert.True(f.BitsFrom == TeBits.MaxEdict, "at MAX_EDICT_BITS, as a symbol");
        Assert.True(f.Flags == TeFlags.Unsigned, "unsigned");

        Assert.True(!Data().Fields.Any(x => x.Name == "m_nEntIndex"),
            "and nothing is called m_nEntIndex — neither the old wire name nor the C++ member's "
          + "spelling survives, which is what a half-done rename would have left");
    }

    [Fact]
    public void TheTwoCustomColoursAreEightBitsOverZeroToOneWithNoEncodingFlag()
    {
        foreach (string name in new[] { "Color1", "Color2" })
        {
            TeField f = Field(Data(), name);

            Assert.True(f.Kind == TePropKind.Vector && f.Bits == 8,
                $"{name} is one eight-bit vector prop");
            Assert.True(f.Flags == TeFlags.None,
                $"{name} carries NO encoding flag — the range is the encoding, and a COORD here "
              + "would spend sixty-six bits on a colour");
            Assert.True(f.Low == 0f && f.High == 1f, $"{name} spans [0,1]");
            Assert.True(!f.Name.Contains('.'),
                $"{name} carries NO struct qualifier any more. Stock spelt these two "
              + "m_CustomColors.m_vecColor1 and .m_vecColor2, dot and all, because RECVINFO "
              + "stringified the same expression on the client and the two tables paired on "
              + "the string; the client builds from this declaration now, so the name is the "
              + "member's");
        }

        // AND THEY ARE STILL TWO SEPARATE VECTORS TWELVE BYTES APART, which is
        // what the two dotted names used to guarantee could not be collapsed
        // into one. Losing the qualifier is a spelling change; losing the second
        // colour would be a wire change.
        Assert.True(Field(Data(), "Color2").Offset - Field(Data(), "Color1").Offset == 12,
            "the second custom colour sits one whole Vector after the first");
    }

    /// <summary>
    /// Two coordinate encodings in one table, which is stock's and not a slip.
    /// </summary>
    [Fact]
    public void TheControlPointOffsetIsPlainCoordWhereTheOriginsAreIntegral()
    {
        TeField offset = Field(Data(), "ControlPointOffset");

        Assert.True(offset.Kind == TePropKind.Float && offset.Elems == 3 && offset.ElemStride == 4,
            "ControlPointOffset, stock's m_ControlPoint1.m_vecOffset, is three floats");
        Assert.True(offset.Bits == -1 && offset.Flags == TeFlags.Coord,
            "at SPROP_COORD — the ORDINARY coordinate encoding");
        Assert.True(Field(Data(), "Origin").Flags == TeFlags.CoordMpIntegral,
            "where m_vOrigin at the top of the same table is SPROP_COORD_MP_INTEGRAL. Two "
          + "coordinate encodings in one table is what stock wrote, and unifying them would "
          + "cost the offset its fraction");
    }

    [Fact]
    public void TheBlobIsAHundredAndFortyFourBytesAndTheDispatchBlobIsTheSameBytes()
    {
        TeClass data = Data();
        TeClass disp = Dispatch();

        Assert.True(data.BlobBytes == 144,
            "DT_EffectData's blob is 144 bytes, computed by EntityLayout and never written down");
        Assert.True(disp.BlobBytes == data.BlobBytes + 4,
            "and DT_TEEffectDispatch's blob is the payload plus a four-byte pad: DT_BaseTempEntity "
          + "is empty, so without it the nested table would sit at offset zero and the engine "
          + "would collapse it (see TheNestedTableMayNotSitAtOffsetZero)");
        Assert.True(disp.BaseBytes == 4,
            "and BaseBytes is four, which is where this class's own fields begin: DT_BaseTempEntity "
          + "contributes nothing to it and the pad contributes all of it");

        // The offsets that a nested table's props read at. Spot-checked at the
        // two ends and at the two fields whose storage is not four bytes, which
        // is where a layout slip shows up first.
        Assert.True(Field(data, "Origin").Offset == 0, "Origin (stock's m_vOrigin) at 0");
        Assert.True(Field(data, "SurfaceProp").Offset == 64, "the short at 64");
        Assert.True(Field(data, "EffectName").Offset == 68,
            "and the index right after it at 68, aligned past the short's two bytes");
        Assert.True(Field(data, "Color").Offset == 88, "the colour byte at 88");
        Assert.True(Field(data, "ControlPointOffset").Offset == 132,
            "and the last vector at 132, which is 144 less its twelve bytes");
    }

    // ---------------------------------------------------------------------
    // Against stock's own tables, read off disk.
    // ---------------------------------------------------------------------

    [Fact]
    public void EffectDataMatchesTheTableItWasTranscribedFrom()
    {
        List<StockProp>? stock = StockEffectData();

        if (stock is null)
        {
            Assert.True(true, "effect_dispatch_data.cpp read for the parity check (SKIPPED, "
                            + "not found — Part 7 leaves this file, but the check degrades "
                            + "gracefully like every other lane's)");
            return;
        }

        Assert.True(stock.Count == 28,
            $"stock's DT_EffectData declares twenty-eight props once the two preprocessor "
          + $"forks are resolved, and the parser found {stock.Count}");

        List<ExpandedProp> mine = ExpandedProps(Data());

        Assert.True(stock.Count == mine.Count,
            $"stock declares {stock.Count} props and the managed table expands into {mine.Count}");

        for (int i = 0; i < stock.Count; i++)
        {
            StockProp   s = stock[i];
            ExpandedProp m = mine[i];

            string want = ExpectedName(s.Member);

            Assert.True(m.Name == want,
                $"prop {i} is '{want}' on the wire, where stock called it {s.Member}");
            Assert.True(s.Kind == m.Field.Kind,
                $"{s.Member} is a SendProp{s.Function} on both sides");

            // The width, in whichever of three forms stock used: a symbol this
            // port carries as a TeBits; a symbol from a header that has no
            // TeBits, resolved by reading that header; or a literal.
            if (BitsSymbols.TryGetValue(s.Bits ?? "", out TeBits symbol))
            {
                Assert.True(m.Field.BitsFrom == symbol,
                    $"{s.Member} is {s.Bits} wide on both sides, as a symbol and not a number");
            }
            else
            {
                int? resolved = HeaderSymbol(s.Bits ?? "");

                Assert.True(m.Field.BitsFrom == TeBits.Literal,
                    $"{s.Member}'s width is not a TeBits symbol, so it is a literal here");
                Assert.True(m.Field.Bits == (resolved ?? int.Parse(s.Bits!, CultureInfo.InvariantCulture)),
                    $"{s.Member} is {s.Bits} bits on both sides");
            }

            Assert.True(s.Flags == m.Field.Flags,
                $"{s.Member} carries {s.Flags} on both sides");
            Assert.True(s.Low == m.Field.Low && s.High == m.Field.High,
                $"{s.Member} spans [{s.Low}, {s.High}] on both sides");
        }
    }

    // ---------------------------------------------------------------------
    // The nested table, the registry and the writer.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheRegistryPublishesTheNestedTableBeforeTheOneThatNestsIt()
    {
        TempEntRegistry registry = Registry();

        Assert.True(registry.Find("CEffectData") is not null,
            "CEffectData was discovered by attribute, like everything else");
        Assert.True(registry.Find("CTEEffectDispatch") is not null,
            "and so was CTEEffectDispatch");

        var order = registry.All.Select(t => t.Class.TableName).ToList();

        Assert.True(order.IndexOf("DT_EffectData") < order.IndexOf("DT_TEEffectDispatch"),
            "DT_EffectData is published FIRST, because native has to point a SendPropDataTable "
          + "at a table that already exists — and base ordering alone would not give that, "
          + "since DT_EffectData is nobody's base");
    }

    /// <summary>
    /// A writer for <c>CTEEffectDispatch</c> can set every value the effect
    /// carries, though <c>DT_TEEffectDispatch</c> declares exactly one prop and
    /// it is the nested table.
    /// </summary>
    [Fact]
    public void TheNestedTablesFieldsAreAddressableFromTheOuterClass()
    {
        TempEntType dispatch = Registered("CTEEffectDispatch");

        Assert.True(dispatch.Fields.Count == 23,
            "twenty-three addressable fields: the datatable prop itself and the twenty-two it "
          + "nests");

        foreach (TeField f in Data().Fields)
        {
            Assert.True(dispatch.Fields.ContainsKey(f.Name),
                $"{f.Name} is addressable from CTEEffectDispatch, though DT_TEEffectDispatch "
              + "does not declare it");
            Assert.True(dispatch.Fields[f.Name].Offset == f.Offset + 4,
                $"{f.Name} at the nested table's own offset PLUS the datatable prop's four, "
              + "because SendProxy_DataTableToDataTable shifts the object pointer by exactly "
              + "that much before the nested props read");
        }
    }

    [Fact]
    public void TheWriterPutsEveryEffectDataValueWhereTheOffsetsSay()
    {
        TempEntType dispatch = Registered("CTEEffectDispatch");

        var blob = new byte[dispatch.BlobBytes];
        var writer = new TempEntWriter(dispatch, blob);

        Assert.True(writer.Vector("Origin", new Vector(1f, 2f, 3f)), "the origin is written");
        Assert.True(writer.Vector("Start", new Vector(4f, 5f, 6f)), "the start is written");
        Assert.True(writer.Int("SurfaceProp", 42), "the surface property is written");
        Assert.True(writer.Int("EffectName", 17), "the effect name index is written");
        Assert.True(writer.Int("Color", 200), "the colour byte is written");
        Assert.True(writer.Bool("ControlPoint1", true), "the control point flag is written");
        Assert.True(writer.Vector("ControlPointOffset", new Vector(7f, 8f, 9f)),
            "and the control point offset is written");

        // ALL THREE COMPONENTS, NOT JUST THE FIRST. m_vOrigin is ONE C# field
        // and THREE props at offset + i*4, and the writer addresses it by name —
        // one name, one offset. That works here only because the stride is a
        // float and the three props are contiguous, so twelve bytes at the
        // field's offset IS element 0, 1 and 2. A field whose stride were
        // anything else would need a per-element setter, and the symptom of
        // getting it wrong is element 0 correct and the rest silently zero:
        // a PLAUSIBLE origin a few feet away rather than an obviously wrong one.
        // EVERY OFFSET BELOW IS THE PAYLOAD'S PLUS FOUR, and the four is the pad
        // that keeps the nested datatable off offset zero. The first four bytes
        // are never written.
        Assert.True(blob[0] == 0 && blob[1] == 0 && blob[2] == 0 && blob[3] == 0,
            "the pad is untouched");

        Assert.True(BitConverter.ToSingle(blob, 4) == 1f, "origin.x at byte 4, which is prop [0]");
        Assert.True(BitConverter.ToSingle(blob, 8) == 2f, "origin.y at 8, which is prop [1]");
        Assert.True(BitConverter.ToSingle(blob, 12) == 3f, "origin.z at 12, which is prop [2]");

        Assert.True(BitConverter.ToSingle(blob, 16) == 4f, "start.x at 16");
        Assert.True(BitConverter.ToSingle(blob, 20) == 5f, "start.y at 20");
        Assert.True(BitConverter.ToSingle(blob, 24) == 6f, "start.z at 24");

        Assert.True(BitConverter.ToInt16(blob, 68) == 42,
            "the surface property as a SHORT at 68 — a four-byte store here would be two bytes "
          + "of the effect name index");
        Assert.True(BitConverter.ToInt32(blob, 72) == 17, "the effect name index at 72");

        Assert.True(blob[92] == 200, "the colour as one byte at 92");
        Assert.True(blob[93] == 0 && blob[94] == 0 && blob[95] == 0,
            "and the three bytes after it untouched, which a four-byte store would not have left");

        Assert.True(blob[128] == 1, "the control point bool as one byte at 128");

        // The third three-prop field, and the last twelve bytes of the blob.
        Assert.True(BitConverter.ToSingle(blob, 136) == 7f, "the offset's x at 136");
        Assert.True(BitConverter.ToSingle(blob, 140) == 8f, "its y at 140");
        Assert.True(BitConverter.ToSingle(blob, 144) == 9f,
            "and its z at 144, which is the last float in the blob — an element setter that "
          + "wrote only [0] would leave these two zero and the control point at the attachment");
    }

    [Fact]
    public void AMisspelledNestedFieldWritesNothing()
    {
        TempEntType dispatch = Registered("CTEEffectDispatch");

        var blob = new byte[dispatch.BlobBytes];
        var writer = new TempEntWriter(dispatch, blob);

        Assert.True(!writer.Int("Effectname", 17),
            "a misspelled nested field is refused rather than written somewhere — and the "
          + "misspelling is of the name the field has NOW (EffectName), because a stale "
          + "m_iEffectName would be refused for the wrong reason and this test would pass "
          + "whatever the rename did");
        Assert.True(blob.All(b => b == 0), "and nothing was written anywhere");
    }

    // ---------------------------------------------------------------------
    // DispatchEffect, end to end on this side.
    // ---------------------------------------------------------------------

    [Fact]
    public void DispatchEffectAsksTheEngineToPlayTheBytesItPacked()
    {
        var (temps, engine) = Server();

        temps.DispatchEffect(TeFilters.Pas(new Vector(64f, -32f, 8f)), 0f, 9, Sample());

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 1, "one temp entity was sent");

        var call = engine.Last("Effects.TempEntPlayback")!.Value;

        Assert.True(call.ArgAs<int>(0) == 11, "under the handle native answered with");
        Assert.True(call.ArgAs<int>(1) == 148,
            "carrying a whole DT_TEEffectDispatch blob: 144 of payload behind a four-byte pad");

        byte[] blob = Blob(call);

        Assert.True(BitConverter.ToSingle(blob, 4) == 10f
                 && BitConverter.ToSingle(blob, 8) == 20f
                 && BitConverter.ToSingle(blob, 12) == 30f, "the origin the caller gave");
        Assert.True(BitConverter.ToSingle(blob, 28) == 45f, "the angles the caller gave");
        Assert.True(BitConverter.ToInt32(blob, 88) == 33, "the entity index, under the name entindex");
        Assert.True(blob[92] == 7, "the colour byte");
        Assert.True(BitConverter.ToSingle(blob, 96) == 12.5f, "the radius");
    }

    /// <summary>
    /// THE TWO NUMBERS THAT SIT THREE LINES APART AND MUST NOT BE TREATED THE
    /// SAME. <c>m_nSurfaceProp</c> carries the <c>+1</c> the send proxy used to
    /// add; <c>m_iEffectName</c> carries nothing, because a wrong index there
    /// plays a different effect rather than a wrong-sounding one.
    /// </summary>
    [Fact]
    public void TheSurfacePropGoesOnTheWirePlusOneAndTheEffectNameGoesOnItVerbatim()
    {
        var (temps, engine) = Server();

        TeEffectData data = Sample();
        data.SurfaceProp = 42;

        temps.DispatchEffect(TeFilters.Broadcast, 0f, 300, data);

        byte[] blob = Blob(engine.Last("Effects.TempEntPlayback")!.Value);

        Assert.True(BitConverter.ToInt16(blob, 68) == 43,
            "a surface property of 42 goes on the wire as 43 — SendProxy_ShortAddOne's plus "
          + "one, which the declaration cannot carry because a send proxy never appears in the "
          + "table the client pairs against (plan 2B)");
        Assert.True(BitConverter.ToInt32(blob, 72) == 300,
            "and an effect name index of 300 goes on the wire as 300, with nothing added. The "
          + "client reads the NAME back by this number, so an off-by-one here fires a different "
          + "effect and reads as a bug in that effect");
    }

    /// <summary>
    /// -1 is the surface property that means "none", and the whole point of the
    /// minus-one flag is that it survives an eight-bit UNSIGNED prop.
    /// </summary>
    [Fact]
    public void ASurfacePropertyOfMinusOneArrivesAsZeroRatherThanAsTwoFiftyFive()
    {
        var (temps, engine) = Server();

        TeEffectData data = Sample();
        data.SurfaceProp = -1;

        temps.DispatchEffect(TeFilters.Broadcast, 0f, 1, data);

        byte[] blob = Blob(engine.Last("Effects.TempEntPlayback")!.Value);

        Assert.True(BitConverter.ToInt16(blob, 68) == 0,
            "-1 plus one is zero, which fits an unsigned eight-bit prop; without the plus one "
          + "it would encode as 65535 truncated to 255, a real surface property");
    }

    [Fact]
    public void AnEffectDataDeclarationIsAnEntityLayoutToo()
    {
        var layouts = new EntityLayoutRegistry(new RecordingLog());
        layouts.DiscoverAll(TestServices.ServerGame);

        var names = layouts.Layouts.Select(l => l.ClassName).ToList();

        Assert.True(names.Contains("TeEffectData"),
            "DT_EffectData's blob is in the layout fingerprint the two sides compare");
        Assert.True(names.Contains("TeEffectDispatch"),
            "and so is DT_TEEffectDispatch's, whose single field is the nested blob");

        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeEffectData").Size == 144,
            "144 bytes, the same number TeClassBuilder computes");
        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeEffectDispatch").Size == 144,
            "and the outer LAYOUT is 144 too, because a nested blob field is sized by its own "
          + "layout rather than by a constant. The four-byte pad is not part of the layout: it "
          + "is TeClassBuilder's, applied on the way to the blob, so the fingerprint the two "
          + "sides compare is a statement about the fields and nothing else");
    }

    // ---------------------------------------------------------------------
    // The string table's live traffic.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Gate 3 is "did the RIGHT one play", and it can only be that if the
    /// command that drives it fires the names this build actually sends. This
    /// reads the live callers out of the shells and asserts the probe command
    /// knows every one of them — so a caller added with a new name fails here
    /// rather than going unexercised.
    /// </summary>
    [Fact]
    public void EveryLiveDispatchNameInTheShellsIsOneTheProbeCommandFires()
    {
        string[] shells =
        {
            // Phase 1b merged shell_ai_orphans.cpp and shell_entity_orphans.cpp
            // into shell_absent.cpp, keeping only what an object on the link line
            // still imported. UTIL_BloodSpray -- the one live DispatchEffect
            // caller either file had -- moved verbatim, so the set of names this
            // reads is the same set.
            "shell_worldquery.cpp", "shell_absent.cpp",
        };

        var live = new SortedSet<string>(StringComparer.Ordinal);
        int filesRead = 0;

        foreach (string shell in shells)
        {
            string? text = ReadSource(Path.Combine("src", "sourcesharp", "game", "server", shell));

            if (text is null)
                continue;

            filesRead++;

            foreach (Match m in Regex.Matches(text, @"\bDispatchEffect\(\s*""([^""]+)"""))
                live.Add(m.Groups[1].Value);
        }

        if (filesRead == 0)
        {
            Assert.True(true, "the shells are not beside the binary (SKIPPED)");
            return;
        }

        Assert.True(filesRead == shells.Length, "both shells were read");

        // ONE SINCE plan_server_native_v2 T4d. `Impact` and `watersplash` were
        // SourceSharp_ImpactEffect's and SourceSharp_SplashEffect's, which
        // EffectsOverTempEnts had already replaced (Part 8) and which went with
        // their slots; managed code dispatches both names now (asserted below
        // against EffectsOverTempEnts' own text, so the move is not a loss).
        //
        // `GlassImpact`'s only server-side caller was
        // `CBaseEntity::HandleShotImpactingGlass` in shell_ai_orphans.cpp, and
        // that body sat inside `#ifndef SS_CBASEENTITY_ALIAS` -- an arm this
        // build has never compiled -- until lane the-close 1/N deleted it with
        // the other 240.
        Assert.True(live.Count >= 1,
            $"the shells dispatch at least one effect by name ({string.Join(", ", live)})");

        Assert.True(live.Contains("bloodspray"),
            "'bloodspray' is still dispatched from the shells -- plan Part 6 names it as one "
          + "of the live callers this table carries");

        foreach (string moved in new[] { "Impact", "watersplash" })
        {
            Assert.DoesNotContain(moved, live);
        }

        string managedPath = RepoLayout.Repo("src", "server", "managed", "EffectsOverTempEnts.cs");
        Assert.True(File.Exists(managedPath), "EffectsOverTempEnts.cs is readable at " + managedPath);
        string managed = File.ReadAllText(managedPath);
        foreach (string moved in new[] { "Impact", "watersplash" })
        {
            Assert.True(managed.Contains($"Dispatch(\"{moved}\", in data);", StringComparison.Ordinal),
                $"'{moved}' left the shells for EffectsOverTempEnts, which dispatches it by name");
        }

        Assert.DoesNotContain("GlassImpact", live);

        string? forward = ReadSource(
            Path.Combine("src", "sourcesharp", "game", "server", "shell_te_forwards_debug.cpp"));

        // cs_sv_te_dispatch is a diagnostic: shell_te_forwards_debug.cpp, compiled
        // into a debug server.so only.
        if (forward is null)
        {
            Assert.True(true, "shell_te_forwards_debug.cpp is not beside the binary (SKIPPED)");
            return;
        }

        foreach (string name in live)
        {
            Assert.True(forward.Contains($"\"{name}\"", StringComparison.Ordinal),
                $"cs_sv_te_dispatch fires '{name}', so gate 3 exercises the traffic this build "
              + "actually produces rather than a list somebody chose");
        }

        // AND GlassImpact IS STILL A LIVE EFFECT NAME, which is the half that
        // keeps the absence above from reading as a capability quietly dropped.
        // The shells do not dispatch it; the forward table still carries it and
        // the CLIENT still registers a receiver for it, so the name crosses the
        // wire when MANAGED code sends it -- which is the arrangement the
        // temp-entity port put it on.
        // THE TABLE ROW, NOT THE WORD. The first draft of this read
        // `forward.Contains("\"GlassImpact\"")` and was satisfied by
        // shell_te_fwd_s4.cpp:19, a COMMENT that quotes
        // `DispatchEffect( "GlassImpact" )` while explaining where the two
        // deleted server callers were. Deleting the actual table entry left it
        // GREEN. Found by the mutation, which is what the mutation is for.
        Assert.True(Regex.IsMatch(forward, @"^\s*""GlassImpact"",\s*$", RegexOptions.Multiline),
            "cs_sv_te_dispatch can still fire 'GlassImpact' -- without this, deleting the "
          + "name from the forward table would satisfy the DoesNotContain above and the "
          + "effect would be gone with nothing saying so");

        // The receiver is managed since lane c5-particles (ShardEffects' GlassImpact handler).
        string? client = ReadSource(
            Path.Combine("src", "sourcesharp", "managed", "SourceSharp.Client", "Effects", "ShardEffects.cs"));
        if (client is not null)
        {
            Assert.True(client.Contains("public string EffectName => \"GlassImpact\";",
                                        StringComparison.Ordinal),
                "and the client still registers a receiver for it, so a dispatch from managed "
              + "code reaches FX_GlassImpact's port");
        }
    }

    // ---------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------

    private static TeClass Data() => TeClassBuilder.Build(typeof(TeEffectData));

    /// <summary>
    /// DT_TEEffectDispatch built over its base's size, looked up rather than
    /// typed — DT_BaseTempEntity is empty today and the zero is a fact about it,
    /// not about this table.
    /// </summary>
    private static TeClass Dispatch()
        => TeClassBuilder.Build(typeof(TeEffectDispatch),
                                TeClassBuilder.Build(typeof(TeBaseTempEntity)).BlobBytes);

    private static TeField Field(TeClass te, string name)
        => te.Fields.Single(f => f.Name == name);

    private static TempEntRegistry Registry()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 11);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry;
    }

    private static TempEntType Registered(string className)
        => Registry().Find(className)
           ?? throw new InvalidOperationException($"{className} was not discovered");

    private static (SourceSharp.Server.TempEnts Temps, RecordingEngine Engine) Server()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 11);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return (new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine), engine);
    }

    /// <summary>
    /// The bytes a playback actually carried.
    ///
    /// <para>
    /// Argument FOUR, appended to the recording by this lane. The other three
    /// were already there and are indexed 0..3 by every lane's tests, so the
    /// copy went on the end. It is here because the plus one on
    /// <c>m_nSurfaceProp</c> happens in <c>TempEnts.DispatchEffect</c> and not
    /// in the writer, so driving the writer directly would assert this test's
    /// own arithmetic rather than what left the server.
    /// </para>
    /// </summary>
    private static byte[] Blob(EngineCall call)
        => call.ArgAs<byte[]>(4)
           ?? throw new InvalidOperationException("the playback recorded no blob");

    /// <summary>A payload with a distinguishable value in every field that has one.</summary>
    private static TeEffectData Sample() => new()
    {
        Origin             = new Vector(10f, 20f, 30f),
        Start              = new Vector(-1f, -2f, -3f),
        Angles             = new Vector(45f, 90f, 0f),
        Normal             = new Vector(0f, 0f, 1f),
        Flags              = 3,
        Magnitude          = 2.5f,
        Scale              = 1.5f,
        AttachmentIndex    = -1,
        SurfaceProp        = 0,
        Material           = 71,
        DamageType         = 64,
        HitBox             = 5,
        EntIndex           = 33,
        Color              = 7,
        Radius             = 12.5f,
        CustomColors       = true,
        Color1             = new Vector(1f, 0f, 0f),
        Color2             = new Vector(0f, 1f, 0f),
        ControlPoint1      = false,
        ParticleAttachment = 2,
        ControlPointOffset = new Vector(4f, 5f, 6f),
    };

    /// <summary>
    /// The managed table as PROPS rather than as fields — one entry per prop,
    /// expanded the way <c>shell_tempents.cpp</c> expands a multi-element field,
    /// so the parity comparison is against what actually goes on the wire.
    /// </summary>
    private readonly record struct ExpandedProp(string Name, TeField Field);

    /// <summary>
    /// What stock's prop is called on the wire now that <see cref="TeEffectData"/>
    /// has dropped its <c>WireName</c>s: the C# member's name, with the element
    /// index stock's <c>SENDINFO_NOCHECK( m_vOrigin[0] )</c> carries put back on.
    ///
    /// <para>
    /// THE MAPPING IS WRITTEN DOWN HERE AND NOWHERE ELSE, so the parity test is
    /// still a parity test: it reads twenty-eight props out of
    /// <c>effect_dispatch_data.cpp</c>, in order, and checks each one against
    /// the field in the same position — kind, width, flags and range unchanged,
    /// and the name through this table. A prop that moved position still fails,
    /// which is the whole reason the loop is by position. An entry that is not
    /// here throws rather than passing, because a silent fallback to stock's own
    /// spelling would make a HALF-converted declaration look converted.
    /// </para>
    /// </summary>
    private static string ExpectedName(string stockName)
    {
        int bracket = stockName.IndexOf('[');
        string bare  = bracket < 0 ? stockName : stockName[..bracket];
        string index = bracket < 0 ? ""        : stockName[bracket..];

        return StockToMember[bare] + index;
    }

    private static readonly Dictionary<string, string> StockToMember = new(StringComparer.Ordinal)
    {
        ["m_vOrigin"]           = "Origin",
        ["m_vStart"]            = "Start",
        ["m_vAngles"]           = "Angles",
        ["m_vNormal"]           = "Normal",
        ["m_fFlags"]            = "Flags",
        ["m_flMagnitude"]       = "Magnitude",
        ["m_flScale"]           = "Scale",
        ["m_nAttachmentIndex"]  = "AttachmentIndex",
        ["m_nSurfaceProp"]      = "SurfaceProp",
        ["m_iEffectName"]       = "EffectName",
        ["m_nMaterial"]         = "Material",
        ["m_nDamageType"]       = "DamageType",
        ["m_nHitBox"]           = "HitBox",

        // Stock's one SENDINFO_NAME: the member was m_nEntIndex and the prop was
        // called entindex, because the client's CEffectData has neither.
        ["entindex"]            = "EntIndex",

        ["m_nColor"]            = "Color",
        ["m_flRadius"]          = "Radius",
        ["m_bCustomColors"]     = "CustomColors",

        // The two that lose a struct qualifier rather than a Hungarian prefix.
        ["m_CustomColors.m_vecColor1"] = "Color1",
        ["m_CustomColors.m_vecColor2"] = "Color2",

        ["m_bControlPoint1"]                     = "ControlPoint1",
        ["m_ControlPoint1.m_eParticleAttachment"] = "ParticleAttachment",
        ["m_ControlPoint1.m_vecOffset"]           = "ControlPointOffset",
    };

    private static List<ExpandedProp> ExpandedProps(TeClass te)
    {
        var props = new List<ExpandedProp>();

        foreach (TeField f in te.Fields)
        {
            if (f.Elems > 1)
            {
                for (int i = 0; i < f.Elems; i++)
                    props.Add(new ExpandedProp($"{f.Name}[{i}]", f));
            }
            else
            {
                props.Add(new ExpandedProp(f.Name, f));
            }
        }

        return props;
    }

    // ---------------------------------------------------------------------
    // Stock's table, parsed out of the .cpp it lives in.
    // ---------------------------------------------------------------------

    /// <summary>One prop line, already reduced to the four things that decide the wire.</summary>
    private readonly record struct StockProp(
        string Function, string Member, TePropKind Kind,
        string? Bits, TeFlags Flags, float Low, float High);

    private static readonly Dictionary<string, TeBits> BitsSymbols = new(StringComparer.Ordinal)
    {
        ["MAX_EDICT_BITS"]                  = TeBits.MaxEdict,
        ["SP_MODEL_INDEX_BITS"]             = TeBits.ModelIndex,
        ["EF_MAX_BITS"]                     = TeBits.EfMax,
        ["ANIMATION_SKIN_BITS"]             = TeBits.AnimationSkin,
        ["NUM_NETWORKED_EHANDLE_BITS"]      = TeBits.NetworkedEHandle,
        ["MAX_EFFECT_FLAG_BITS"]            = TeBits.EffectFlag,
        ["MAX_EFFECT_DISPATCH_STRING_BITS"] = TeBits.EffectDispatchString,
    };

    /// <summary>
    /// A width that is a <c>#define</c> in a header this port carries no
    /// <see cref="TeBits"/> for — <c>MAX_MODEL_INDEX_BITS</c>, and only that one
    /// today. Resolved by reading the header, never by knowing the number.
    /// </summary>
    private static int? HeaderSymbol(string symbol)
    {
        if (symbol != "MAX_MODEL_INDEX_BITS")
            return null;

        string? qlimits = ReadSource(Path.Combine("src", "common", "qlimits.h"));

        return qlimits is null ? null : DefineValue(qlimits, symbol);
    }

    private static int DefineValue(string text, string symbol)
    {
        var m = Regex.Match(text, @"#define\s+" + Regex.Escape(symbol) + @"\s+(\d+)");

        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
    }

    /// <summary>
    /// <c>DT_EffectData</c> as stock declares it, with both preprocessor forks
    /// resolved the way this build resolves them.
    /// </summary>
    private static List<StockProp>? StockEffectData()
    {
        string? text = ReadSource(Path.Combine("src", "game", "shared", "effect_dispatch_data.cpp"));

        if (text is null)
            return null;

        var block = Regex.Match(text,
            @"BEGIN_SEND_TABLE_NOBASE\(\s*CEffectData\s*,\s*DT_EffectData\s*\)(.*?)END_SEND_TABLE",
            RegexOptions.Singleline);

        if (!block.Success)
            return null;

        string body = Undefined(block.Groups[1].Value, "HL2_DLL", "TF_DLL");

        var props = new List<StockProp>();

        foreach (Match call in Regex.Matches(body, @"SendProp(\w+)\s*\("))
        {
            string function = call.Groups[1].Value;
            var    args     = SplitTopLevel(ArgumentsOf(body, call.Index + call.Length));

            props.Add(Reduce(function, args));
        }

        return props;
    }

    /// <summary>
    /// One stock prop reduced to what the wire cares about, INCLUDING what the
    /// one-line helpers in <c>sendproxy.cpp</c> expand to.
    ///
    /// <para>
    /// Reducing here rather than at the comparison is deliberate: the two
    /// helpers supply a width and a flag the table does not spell out, and
    /// burying that in the comparison would make the parity check quietly
    /// lenient about the two props it matters most for.
    /// </para>
    /// </summary>
    private static StockProp Reduce(string function, List<string> args)
    {
        string member = MemberOf(args[0]);

        // SendPropBool( SENDINFO_NOCHECK( x ) ) -> SendPropInt( x, 1, SPROP_UNSIGNED ),
        // sendproxy.cpp:53. No width and no flags in the table.
        if (function == "Bool")
            return new StockProp(function, member, TePropKind.Int, "1", TeFlags.Unsigned, 0f, 0f);

        // SendPropIntWithMinusOneFlag( SENDINFO_NOCHECK( x ), 8, proxy )
        //   -> SendPropInt( x, 8, SPROP_UNSIGNED, proxy ), sendproxy.cpp:73.
        // The third argument is a PROXY and not a flag, which is exactly the
        // shape a parser that treated it positionally would get wrong.
        if (function == "IntWithMinusOneFlag")
        {
            return new StockProp(function, member, TePropKind.Int,
                                 args.Count > 1 ? args[1] : null, TeFlags.Unsigned, 0f, 0f);
        }

        return new StockProp(
            Function: function,
            Member:   member,
            Kind:     KindOf(function),
            Bits:     args.Count > 1 ? args[1] : null,
            Flags:    FlagsOf(args.Count > 2 ? args[2] : null),
            Low:      NumberOf(args.Count > 3 ? args[3] : null),
            High:     NumberOf(args.Count > 4 ? args[4] : null));
    }

    /// <summary>
    /// The prop's WIRE name out of a SENDINFO of any spelling.
    ///
    /// <para>
    /// <c>SENDINFO_NAME( m_nEntIndex, entindex )</c> is the one that matters:
    /// the prop is called by its SECOND argument, and reading the first would
    /// have this test agreeing with a declaration the client cannot pair with.
    /// </para>
    /// </summary>
    private static string MemberOf(string sendinfo)
    {
        var named = Regex.Match(sendinfo, @"SENDINFO_(?:DT_)?NAME\s*\(\s*[\w.\[\]]+\s*,\s*(\w+)\s*\)");

        if (named.Success)
            return named.Groups[1].Value;

        var plain = Regex.Match(sendinfo,
            @"SENDINFO(?:_NOCHECK|_DT|_VECTORELEM|_ARRAYELEM|_STRUCTELEM)?\s*\(\s*([\w.\[\]]+)");

        if (!plain.Success)
            throw new InvalidOperationException($"'{sendinfo}' is not a SENDINFO this knows");

        return plain.Groups[1].Value;
    }

    /// <summary>
    /// Resolves <c>#ifdef</c> / <c>#if defined(...)</c> forks for symbols this
    /// build does NOT define: the taken-if arm is dropped and the
    /// <c>#else</c> kept.
    ///
    /// <para>
    /// Written as a replace over the whole block rather than as a line filter,
    /// because a filter that skipped the <c>#ifdef</c> line and nothing else
    /// would keep BOTH arms and report thirty-four props where the build has
    /// twenty-eight. It refuses a symbol it was not told about instead of
    /// guessing, so a third fork appearing in this table fails here rather than
    /// being silently resolved the wrong way.
    /// </para>
    /// </summary>
    private static string Undefined(string body, params string[] symbols)
    {
        foreach (Match m in Regex.Matches(body, @"#\s*if(?:def)?\s+([^\r\n]+)"))
        {
            string condition = m.Groups[1].Value;

            Assert.True(symbols.Any(s => condition.Contains(s, StringComparison.Ordinal)),
                $"the parser was told what to do with every preprocessor fork in this table, "
              + $"and '{condition.Trim()}' is not one of them");
        }

        foreach (string symbol in symbols)
        {
            body = Regex.Replace(body,
                @"#\s*if(?:def)?\s+[^\r\n]*" + Regex.Escape(symbol) + @"[^\r\n]*\r?\n"
              + @"(?:.*?)#\s*else(.*?)#\s*endif",
                "$1", RegexOptions.Singleline);
        }

        return body;
    }

    private static TePropKind KindOf(string sendPropFunction) => sendPropFunction switch
    {
        "Int"        => TePropKind.Int,
        "ModelIndex" => TePropKind.Int,
        "Bool"       => TePropKind.Int,
        "Float"      => TePropKind.Float,
        "Vector"     => TePropKind.Vector,
        "Angle"      => TePropKind.Angle,
        "QAngles"    => TePropKind.QAngles,
        "DataTable"  => TePropKind.DataTable,
        _            => throw new InvalidOperationException(
                            $"SendProp{sendPropFunction} is not a kind this knows"),
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

    private static float NumberOf(string? literal)
    {
        string text = (literal ?? "").Trim();

        return text.Length == 0
            ? 0f
            : float.Parse(text.TrimEnd('f', 'F'), CultureInfo.InvariantCulture);
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

    private static string? ReadSource(string relative)
    {
        string? path = RepoLayout.FindSdkFile(relative);

        return path is null ? null : File.ReadAllText(path);
    }
}
