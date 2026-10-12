using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

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
/// </summary>
public class TempEntDebrisTests
{
    // =====================================================================
    // DT_TEBreakModel
    // =====================================================================

    [Fact]
    public void TheBreakModelTableSendsThreeAngleElementsBetweenItsVectors()
    {
        TeClass te = Build<TeBreakModel>();

        Assert.True(te.ClassName == "CTEBreakModel", "the two sides pair on CTEBreakModel");
        Assert.True(te.TableName == "DT_TEBreakModel", "over DT_TEBreakModel");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");
        Assert.True(te.RegistersClass,
            "and it claims a ServerClass: te_breakmodel.cpp is evicted from the build");

        // NINE FIELDS, ELEVEN PROPS. The rotation is one field and three props,
        // which is the whole of what makes this table different from every one
        // ported before it.
        Assert.True(te.Fields.Length == 9, "nine fields of its own");
        Assert.True(PropsOf(te.Fields).Count == 11, "which are eleven props on the wire");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Rotation", "then the rotation");
        Assert.True(te.Fields[2].Name == "Size", "then the size");
        Assert.True(te.Fields[3].Name == "Velocity", "then the velocity");
        Assert.True(te.Fields[4].Name == "ModelIndex", "then the model index");
        Assert.True(te.Fields[5].Name == "Randomization", "then the randomization");
        Assert.True(te.Fields[6].Name == "Count", "then the count");
        Assert.True(te.Fields[7].Name == "Time", "then the time");
        Assert.True(te.Fields[8].Name == "Flags", "then the flags");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertAngleElements(te.Fields[1], "Rotation", 13);
        AssertCoordVector(te.Fields[2], "Size");
        AssertCoordVector(te.Fields[3], "Velocity");
        AssertModelIndex(te.Fields[4], "ModelIndex");
        AssertUnsignedInt(te.Fields[5], 9);
        AssertUnsignedInt(te.Fields[6], 8);

        // FLAGS OF ZERO, not SPROP_ROUNDDOWN. Five other floats in this port
        // round down over a range that looks just like this one's.
        AssertFloat(te.Fields[7], 10, TeFlags.None, 0f, 102.4f);

        AssertUnsignedInt(te.Fields[8], 8);

        Assert.True(te.BaseBytes == 0, "DT_BaseTempEntity is empty, so its blob is empty");
        Assert.True(te.BlobBytes == 68,
            "sixty-eight bytes: four twelve-byte vectors and five of four, computed not written down");
    }

    // =====================================================================
    // DT_TEPhysicsProp
    // =====================================================================

    [Fact]
    public void ThePhysicsPropTableTakesTwoWidthsFromHeadersAndOneSignFromSilence()
    {
        TeClass te = Build<TePhysicsProp>();

        Assert.True(te.ClassName == "CTEPhysicsProp", "the two sides pair on CTEPhysicsProp");
        Assert.True(te.TableName == "DT_TEPhysicsProp", "over DT_TEPhysicsProp");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 7, "seven fields of its own");
        Assert.True(PropsOf(te.Fields).Count == 9, "which are nine props on the wire");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Rotation", "then the rotation");
        Assert.True(te.Fields[2].Name == "Velocity", "then the velocity");
        Assert.True(te.Fields[3].Name == "ModelIndex", "then the model index");
        Assert.True(te.Fields[4].Name == "Skin", "then the SKIN, which the RecvTable lists last but one");
        Assert.True(te.Fields[5].Name == "Flags", "then the flags");
        Assert.True(te.Fields[6].Name == "Effects", "then the effects");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertAngleElements(te.Fields[1], "Rotation", 13);
        AssertCoordVector(te.Fields[2], "Velocity");
        AssertModelIndex(te.Fields[3], "ModelIndex");

        // ANIMATION_SKIN_BITS, AND SIGNED. Stock's line passes no third
        // argument at all where the two under it pass SPROP_UNSIGNED.
        Assert.True(te.Fields[4].BitsFrom == TeBits.AnimationSkin,
            "m_nSkin's width is ANIMATION_SKIN_BITS, resolved natively from baseanimating.h");
        Assert.True(te.Fields[4].Bits == -1,
            "and no literal is left beside it to be read by mistake");
        Assert.True(te.Fields[4].Flags == TeFlags.None,
            "m_nSkin is SIGNED — its callers pass small non-negative skins, so unsigned would "
            + "look correct in every screenshot");

        AssertUnsignedInt(te.Fields[5], 2);

        Assert.True(te.Fields[6].BitsFrom == TeBits.EfMax,
            "m_nEffects' width is EF_MAX_BITS, resolved natively from const.h");
        Assert.True(te.Fields[6].Flags == TeFlags.Unsigned, "and it IS unsigned, unlike m_nSkin");

        // The two symbols are ten each today and they are not the same ten.
        Assert.True(te.Fields[4].BitsFrom != te.Fields[6].BitsFrom,
            "ANIMATION_SKIN_BITS and EF_MAX_BITS are different symbols in different headers, "
            + "however equal their values happen to be");

        Assert.True(te.BlobBytes == 52, "fifty-two bytes");
    }

    // =====================================================================
    // DT_TEExplosion
    // =====================================================================

    [Fact]
    public void TheExplosionTableNestsTheParticleSystemAndSpendsTwoWholeInts()
    {
        TeClass te = Explosion();

        Assert.True(te.ClassName == "CTEExplosion", "the two sides pair on CTEExplosion");
        Assert.True(te.TableName == "DT_TEExplosion", "over DT_TEExplosion");
        Assert.True(te.BaseTable == "DT_TEParticleSystem",
            "which nests DT_TEParticleSystem, not DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 8, "eight props of its own");

        // THE WIRE NAMES ARE THE MEMBERS' NOW, and stock's spellings are kept
        // beside them because they are what te_explosion.cpp says and what the
        // parity test below reads off disk.
        Assert.True(te.Fields[0].Name == "ModelIndex", "the model index goes first (m_nModelIndex)");
        Assert.True(te.Fields[1].Name == "Scale", "then the scale (m_fScale)");
        Assert.True(te.Fields[2].Name == "FrameRate", "then the frame rate (m_nFrameRate)");
        Assert.True(te.Fields[3].Name == "Flags", "then the flags (m_nFlags)");
        Assert.True(te.Fields[4].Name == "Normal", "then the normal (m_vecNormal)");
        Assert.True(te.Fields[5].Name == "MaterialType", "then the material type (m_chMaterialType)");
        Assert.True(te.Fields[6].Name == "Radius", "then the radius (m_nRadius)");
        Assert.True(te.Fields[7].Name == "Magnitude", "then the magnitude (m_nMagnitude)");

        AssertModelIndex(te.Fields[0], "ModelIndex");

        // FLAGS OF ZERO again — nine bits over [0, 51.2] with no rounding
        // direction, which is stock's own literal 0.
        AssertFloat(te.Fields[1], 9, TeFlags.None, 0f, 51.2f);

        AssertUnsignedInt(te.Fields[2], 8);
        AssertUnsignedInt(te.Fields[3], 8);
        AssertCoordVector(te.Fields[4], "Normal");

        // ONE BYTE OF STORAGE, because stock's member is an unsigned char and
        // SendPropInt picks its read proxy from exactly that (dt_send.cpp:492).
        AssertUnsignedInt(te.Fields[5], 8);
        Assert.True(te.Fields[5].SizeBytes == 1,
            "MaterialType reads ONE byte — the C# member is a byte, and the kind is INFERRED "
            + "from that; described as four it would read three bytes of "
            + "whatever follows it");

        AssertUnsignedInt(te.Fields[6], 32);
        AssertUnsignedInt(te.Fields[7], 32);

        // THE BASE'S ORIGIN IS AT THE FRONT OF THIS BLOB and this class's own
        // fields start after it. An explosion is a thing that happens
        // somewhere, and the somewhere is not in this table.
        Assert.True(te.BaseBytes == 12, "DT_TEParticleSystem's origin occupies the first twelve bytes");
        Assert.True(te.Fields[0].Offset == 12, "so ModelIndex is the thirteenth byte in");
        Assert.True(te.BlobBytes == 52, "fifty-two bytes in all");

        Assert.True(!te.Fields.Any(f => f.Name == "Origin"),
            "and Origin is NOT one of this table's props — it is the base's, reached through "
            + "the inheritance and nested at datatable offset zero");
    }

    // =====================================================================
    // DT_TEShatterSurface
    // =====================================================================

    [Fact]
    public void TheShatterSurfaceTableSendsAQAngleAsAPlainCoordVector()
    {
        TeClass te = Build<TeShatterSurface>();

        Assert.True(te.ClassName == "CTEShatterSurface", "the two sides pair on CTEShatterSurface");
        Assert.True(te.TableName == "DT_TEShatterSurface", "over DT_TEShatterSurface");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 14, "fourteen props, six of them colour bytes");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Angles", "then the angles");
        Assert.True(te.Fields[2].Name == "Force", "then the force");
        Assert.True(te.Fields[3].Name == "ForcePos", "then where it was applied");

        AssertCoordVector(te.Fields[0], "Origin");

        // THE MEMBER IS A CNetworkQAngle AND THE PROP IS A SendPropVector.
        // c_te_glassshatter.cpp:76 receives it with RecvPropVector to match, so
        // "the member is a QAngle so the prop must be QAngles" is exactly the
        // reasoning that breaks this table.
        AssertCoordVector(te.Fields[1], "Angles");
        Assert.True(te.Fields[1].Kind != TePropKind.QAngles,
            "m_vecAngles is NOT the qangles kind, whatever its member's type says");

        AssertCoordVector(te.Fields[2], "Force");
        AssertCoordVector(te.Fields[3], "ForcePos");

        // Width ZERO and SPROP_NOSCALE, which is stock's own spelling and not
        // the -1 the COORD vectors use.
        AssertFloat(te.Fields[4], 0, TeFlags.NoScale, 0f, 0f);
        AssertFloat(te.Fields[5], 0, TeFlags.NoScale, 0f, 0f);
        AssertFloat(te.Fields[6], 0, TeFlags.NoScale, 0f, 0f);

        AssertUnsignedInt(te.Fields[7], 2);
        Assert.True(te.Fields[7].SizeBytes == 4,
            "m_nSurfaceType is an int of storage — ShatterSurfaceType is backed by int, as "
            + "stock's CNetworkVar(int) is");

        Assert.True(te.BlobBytes == 72, "seventy-two bytes");
    }

    /// <summary>
    /// The six colour bytes are six props at six consecutive offsets, one byte
    /// each, and NONE of them carries <see cref="TeFlags.VectorElem"/>.
    ///
    /// <para>
    /// THIS IS THE ASSERTION THAT PINS THE SHAPE CHOICE. <c>SENDINFO_ARRAYELEM</c>
    /// (<c>dt_send.h:624</c>) takes a POSITIVE offset into <c>varName[i]</c>;
    /// <c>SENDINFO_VECTORELEM</c> (<c>:631</c>) negates it so the engine can set
    /// <c>SPROP_IS_A_VECTOR_ELEM</c> itself. A colour byte is the first kind, so
    /// the flag the angles in this same lane carry must be absent here — and
    /// declaring the six as six fields rather than as one field with
    /// <c>Elems = 3</c> is what makes the offsets consecutive with no padding,
    /// which is the other half of agreeing with stock's <c>byte[3]</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void ColourBytesAreSixConsecutiveProps()
    {
        TeField[] fields = Build<TeShatterSurface>().Fields;

        string[] expected =
        {
            "FrontColorR", "FrontColorG", "FrontColorB",
            "BackColorR",  "BackColorG",  "BackColorB",
        };

        for (int i = 0; i < expected.Length; i++)
        {
            TeField f = fields[8 + i];

            Assert.True(f.Name == expected[i],
                $"prop {8 + i} is spelled {expected[i]}, brackets included — the client matches "
                + "send props to recv props by name and RECVINFO produces the same string");
            Assert.True(f.Kind == TePropKind.Int && f.Bits == 8 && f.Flags == TeFlags.Unsigned,
                $"{expected[i]} is eight bits unsigned");
            Assert.True(f.SizeBytes == 1, $"{expected[i]} reads ONE byte of blob");
            Assert.True(!f.Flags.HasFlag(TeFlags.VectorElem),
                $"{expected[i]} is a SENDINFO_ARRAYELEM and carries no vector-elem flag — "
                + "which the angles in this same lane do");
        }

        for (int i = 1; i < expected.Length; i++)
        {
            Assert.True(fields[8 + i].Offset == fields[8 + i - 1].Offset + 1,
                $"{expected[i]} is the byte after {expected[i - 1]}, as stock's byte[3] pair is");
        }
    }

    // =====================================================================
    // DT_TELargeFunnel
    // =====================================================================

    [Fact]
    public void TheLargeFunnelTableIsAModelAndTwoBitsOfDirection()
    {
        TeClass te = LargeFunnel();

        Assert.True(te.ClassName == "CTELargeFunnel", "the two sides pair on CTELargeFunnel");
        Assert.True(te.TableName == "DT_TELargeFunnel", "over DT_TELargeFunnel");
        Assert.True(te.BaseTable == "DT_TEParticleSystem", "which nests DT_TEParticleSystem");

        Assert.True(te.Fields.Length == 2, "two props of its own");

        AssertModelIndex(te.Fields[0], "ModelIndex");

        // TWO BITS FOR A BOOLEAN, which is stock's choice. Nothing after it in
        // this table today, which is exactly what makes narrowing it look
        // harmless until the table grows.
        AssertUnsignedInt(te.Fields[1], 2);
        Assert.True(te.Fields[1].Name == "Reversed",
            "and it is called Reversed on the wire, where stock called it m_nReversed");

        Assert.True(te.BaseBytes == 12, "the base's origin is the first twelve bytes");
        Assert.True(te.BlobBytes == 20, "twenty bytes in all");
    }

    // =====================================================================
    // DT_TEKillPlayerAttachments
    // =====================================================================

    [Fact]
    public void TheKillPlayerAttachmentsTableIsOnePropOfFiveLiteralBits()
    {
        TeClass te = Build<TeKillPlayerAttachments>();

        Assert.True(te.ClassName == "CTEKillPlayerAttachments",
            "the two sides pair on CTEKillPlayerAttachments");
        Assert.True(te.TableName == "DT_TEKillPlayerAttachments", "over DT_TEKillPlayerAttachments");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 1, "one prop, which is the smallest table in the inventory");

        AssertUnsignedInt(te.Fields[0], 5);
        Assert.True(te.Fields[0].Name == "Player",
            "Player — stock's m_nPlayer — and a player SLOT, not an edict");

        // FIVE IS A LITERAL HERE AND Q_log2( MAX_PLAYERS ) IN DT_TEPlayerDecal.
        // The two resolve alike today and the port keeps each as stock wrote
        // it: a literal that became a symbol would invent a dependency stock
        // does not have, in the same way a symbol that became a literal drops
        // one it does.
        Assert.True(te.Fields[0].BitsFrom == TeBits.Literal,
            "five is a literal in stock's own table here, however DT_TEPlayerDecal spells the "
            + "same quantity");

        Assert.True(te.BlobBytes == 4, "four bytes of blob");
    }

    // =====================================================================
    // Lane-wide rules.
    // =====================================================================

    /// <summary>
    /// Every one of the six is discovered by attribute and registered, base
    /// chain resolved, with no list file anywhere.
    /// </summary>
    [Fact]
    public void AllSixAreDiscoveredAndRegistered()
    {
        foreach (string className in LaneClassNames)
        {
            TempEntType type = Registered(className);

            Assert.True(type.Registered, $"{className} was accepted by native");
            Assert.True(type.Class.RegistersClass,
                $"{className} claims a ServerClass of its own — its te_*.cpp is evicted");
        }

        // The two that nest DT_TEParticleSystem can address the base's origin
        // through the writer, which is what stops an explosion happening at the
        // world origin.
        Assert.True(Registered("CTEExplosion").Fields.ContainsKey("Origin"),
            "CTEExplosion's writer can set Origin, which is the base's field");
        Assert.True(Registered("CTELargeFunnel").Fields.ContainsKey("Origin"),
            "and so can CTELargeFunnel's");
    }

    /// <summary>
    /// The lane's two symbolic widths are never restated as numbers, and the
    /// literal widths are never turned into symbols.
    ///
    /// <para>
    /// SELECTED BY ROLE, not by the value being checked. The subjects are named
    /// here — <c>m_nSkin</c>, <c>m_nEffects</c> and every model index — so a
    /// rename fails loudly rather than narrowing the test to nothing, which is
    /// <c>docs/writing-tests.md</c>' own recorded trap.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSymbolicWidthsAreSymbolsAndTheLiteralOnesAreLiterals()
    {
        // BOTH SPELLINGS, deliberately. DT_TEBreakModel, DT_TEPhysicsProp and
        // DT_TEShatterSurface have converted and put their MEMBER names on the
        // wire; DT_TEExplosion and DT_TELargeFunnel are another lane's and still
        // send stock's. The subject of this test is the WIDTH, and listing both
        // keeps every one of the six checked while the conversion lands class by
        // class — dropping either spelling would silently stop counting.
        var symbolic = new Dictionary<string, TeBits>(StringComparer.Ordinal)
        {
            ["Skin"]          = TeBits.AnimationSkin,
            ["Effects"]       = TeBits.EfMax,
            ["ModelIndex"]    = TeBits.ModelIndex,
            ["m_nSkin"]       = TeBits.AnimationSkin,
            ["m_nEffects"]    = TeBits.EfMax,

            // BOTH SPELLINGS, because the lane is half converted: BreakModel's
            // and PhysicsProp's model index is still stock's m_nModelIndex and
            // Explosion's and LargeFunnel's is now ModelIndex. Dropping either
            // key would not fail — it would silently move those props into the
            // "must be a literal" arm below and count six as four.
            ["m_nModelIndex"] = TeBits.ModelIndex,
            ["ModelIndex"]    = TeBits.ModelIndex,
        };

        int found = 0;

        foreach (TeField f in LaneFields())
        {
            if (symbolic.TryGetValue(f.Name, out TeBits expected))
            {
                found++;
                Assert.True(f.BitsFrom == expected,
                    $"{f.Name}'s width is {expected}, resolved natively from the header that owns "
                    + "it — never a number restated here");
                Assert.True(f.Bits == -1,
                    $"{f.Name} leaves no literal width beside the symbol to be read by mistake");
            }
            else
            {
                Assert.True(f.BitsFrom == TeBits.Literal,
                    $"{f.Name} is a literal in stock's own table, so it is a literal here");
            }
        }

        // Three model indices (BreakModel, PhysicsProp, Explosion, LargeFunnel
        // — four) plus the skin and the effects.
        Assert.True(found == 6,
            $"six symbolic widths across the lane: four model indices, one skin, one effects "
            + $"— found {found}");
    }

    /// <summary>
    /// <c>m_nSkin</c> is the only signed int in the lane, and every other
    /// integer prop is <c>SPROP_UNSIGNED</c>.
    ///
    /// <para>
    /// The model indices are excluded by NAME rather than by their flags:
    /// <c>SendPropModelIndex</c> passes a literal 0 for flags
    /// (<c>dt_send.h:750</c>) and so is unsigned in neither sense, and a filter
    /// written over the flag value would have skipped exactly the case worth
    /// catching.
    /// </para>
    /// </summary>
    [Fact]
    public void OnlyTheSkinIsSigned()
    {
        var signed = new List<string>();

        foreach (TeField f in LaneFields())
        {
            if (f.Kind != TePropKind.Int || f.Name is "m_nModelIndex" or "ModelIndex")
                continue;

            if (!f.Flags.HasFlag(TeFlags.Unsigned))
                signed.Add(f.Name);
        }

        Assert.True(signed.Count == 1 && signed[0] == "Skin",
            "Skin alone is signed — stock calls it m_nSkin — "
          + $"found [{string.Join(", ", signed)}]");
    }

    /// <summary>
    /// The angle props are angles, are three of them, carry the vector-elem
    /// instruction, and carry no range of their own.
    ///
    /// <para>
    /// <c>SendPropAngle</c> supplies low 0 and high 360 itself
    /// (<c>dt_send.cpp:416</c>) and its declaration takes no bounds at all, so
    /// a <c>Low</c>/<c>High</c> written here would be a number nothing reads —
    /// and the first reader to trust it would be the one who "fixed" the kind
    /// to <see cref="TePropKind.Float"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void TheAnglesAreAnglesAndCarryNoRangeOfTheirOwn()
    {
        var found = new List<string>();

        foreach (TeField f in LaneFields())
        {
            if (f.Kind != TePropKind.Angle)
                continue;

            found.Add(f.Name);

            Assert.True(f.Name == "Rotation",
                "the lane's only angle field is Rotation, which stock spells m_angRotation");
            Assert.True(f.Elems == 3 && f.ElemStride == 4,
                "three props, four bytes apart — SENDINFO_VECTORELEM's shape");
            Assert.True(f.Flags == TeFlags.VectorElem,
                "carrying the vector-elem instruction and nothing else: the offset is NEGATED and "
                + "the engine sets SPROP_IS_A_VECTOR_ELEM itself");
            Assert.True(f.Bits == 13, "thirteen bits per component");
            Assert.True(f.SizeBytes == 4, "each prop reads one float");
            Assert.True(f.Low == 0f && f.High == 0f,
                "and no range is declared: SendPropAngle supplies [0,360] itself");
        }

        Assert.True(found.Count == 2,
            $"two tables in this lane send angles — found {found.Count}");

        Assert.True(!LaneFields().Any(f => f.Kind == TePropKind.QAngles),
            "and NOTHING in this lane is a SendPropQAngles: the inventory's only one is "
            + "DT_TEProjectedDecal's, which is lane B's");
    }

    /// <summary>
    /// The lane's declarations are entity layouts too, so the fingerprint the
    /// two sides already exchange at join covers their blobs.
    /// </summary>
    [Fact]
    public void TheLanesBlobsAreInTheLayoutFingerprint()
    {
        var layouts = new EntityLayoutRegistry(new RecordingLog());
        layouts.DiscoverAll(TestServices.ServerGame);

        var names = layouts.Layouts.Select(l => l.ClassName).ToHashSet(StringComparer.Ordinal);

        // By the C# TYPE name: DiscoverAll builds each layout without the
        // attribute, so it is a fingerprint over the SHAPE.
        foreach (string t in new[]
        {
            "TeBreakModel", "TePhysicsProp", "TeExplosion",
            "TeShatterSurface", "TeLargeFunnel", "TeKillPlayerAttachments",
        })
        {
            Assert.True(names.Contains(t), $"{t}'s blob is in the layout fingerprint the two sides compare");
        }

        // TWENTY, NOT EIGHT, and the change is the conversion rather than a
        // wire move. TeLargeFunnel is now a CLASS deriving TeParticleSystem, so
        // the layout the fingerprint is taken over covers the whole object —
        // the base's twelve bytes and its own eight — where before it covered
        // the declared fields alone and left the base to be fingerprinted
        // separately. Both sides load this same assembly, so both compute the
        // same number; the BLOB is fifty-two-and-twenty either way, which is
        // what TheLargeFunnelTableIsAModelAndTwoBitsOfDirection asserts.
        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeLargeFunnel").Size == 20,
            "CTELargeFunnel's layout is the base's twelve bytes and its own two fields");
    }

    /// <summary>
    /// <c>models/gibs/hgibs.mdl</c> is still precached, which is the one thing
    /// this lane could have quietly dropped.
    ///
    /// <para>
    /// <c>CTEBreakModel::Precache</c> (<c>te_breakmodel.cpp:78</c>) and
    /// <c>CTEPhysicsProp::Precache</c> (<c>te_physicsprop.cpp:73</c>) BOTH took
    /// it, and <c>CBaseTempEntity::PrecacheTempEnts</c> reached them by walking
    /// the temp entity singleton list. Neither class is on that list any more,
    /// so without <see cref="SourceSharp.Server.ServerPrecache.PrecacheAll(IEnginePrecache, ILog, System.Reflection.Assembly[])"/> the model
    /// would stop being precached and NOTHING would say so: the symptom is a
    /// missing model at runtime, not a build error.
    /// </para>
    ///
    /// <para>
    /// It also asserts the model is asked for ONCE. Two temp entities took the
    /// same model, and a lane that added a call site per class rather than a
    /// line to the list would have precached it twice — harmless today, and the
    /// reason a later removal takes the other's precache with it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheGibsPrecacheSurvivedTheEvictionOfBothItsNativeClasses()
    {
        var engine = Fake();
        engine.Returns("Precache.PrecacheModel", 64);

        // THE SWEEP ITSELF, which is what LevelInit runs. TempEnts.Precache was
        // a one-line delegate to it, kept only so no caller was stranded while
        // the [Precache] attribute landed; calling the real thing here means
        // this test exercises the path the game does.
        SourceSharp.Server.ServerPrecache.PrecacheAll(engine, new RecordingLog(), typeof(SourceSharp.Shared.TeSparks).Assembly);

        var asked = engine.Calls
            .Where(c => c.Member == "Precache.PrecacheModel")
            .Select(c => c.ArgAs<string>(0))
            .ToList();

        Assert.True(asked.Contains("models/gibs/hgibs.mdl"),
            "CTEBreakModel's and CTEPhysicsProp's model is precached from managed code now that "
            + $"both native classes are gone — asked for [{string.Join(", ", asked)}]");

        Assert.True(asked.Count(m => m == "models/gibs/hgibs.mdl") == 1,
            "once, not once per temp entity that draws itself with it");

        // Lane C's line is still there. A precache list is exactly the kind of
        // thing a later edit shortens by accident.
        Assert.True(asked.Contains("sprites/gunsmoke.vmt"),
            "and CTESprite's model is still on the same list");
    }

    // =====================================================================
    // Fixtures and the stock parser.
    // =====================================================================

    private static readonly string[] LaneClassNames =
    {
        "CTEBreakModel", "CTEPhysicsProp", "CTEExplosion",
        "CTEShatterSurface", "CTELargeFunnel", "CTEKillPlayerAttachments",
    };

    /// <summary>
    /// <c>DT_TEParticleSystem</c>'s blob, computed rather than written down —
    /// the two tables that nest it start their own fields after it.
    /// </summary>
    private static int ParticleSystemBytes
        => TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes;

    private static TeClass Build<T>() => TeClassBuilder.Build(typeof(T));

    private static TeClass Explosion()
        => TeClassBuilder.Build(typeof(TeExplosion), ParticleSystemBytes);

    private static TeClass LargeFunnel()
        => TeClassBuilder.Build(typeof(TeLargeFunnel), ParticleSystemBytes);

    /// <summary>Every field this lane declares, its own only — not the bases'.</summary>
    private static IEnumerable<TeField> LaneFields()
        => Build<TeBreakModel>().Fields
            .Concat(Build<TePhysicsProp>().Fields)
            .Concat(Explosion().Fields)
            .Concat(Build<TeShatterSurface>().Fields)
            .Concat(LargeFunnel().Fields)
            .Concat(Build<TeKillPlayerAttachments>().Fields);

    private static TempEntType Registered(string className)
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry.Find(className)
            ?? throw new InvalidOperationException($"{className} was not discovered");
    }

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

    private static void AssertCoordVector(TeField f, string name)
    {
        Assert.True(f.Name == name, $"{name} is spelled stock's way");
        Assert.True(f.Kind == TePropKind.Vector, $"{name} is one SendPropVector, not three elements");
        Assert.True(f.Bits == -1, $"{name}'s width is -1, so SPROP_COORD decides it");
        Assert.True(f.Flags == TeFlags.Coord, $"{name} is a world coordinate and nothing else");
        Assert.True(f.SizeBytes == 12, $"{name} reads a whole Vector out of the blob");
    }

    private static void AssertAngleElements(TeField f, string name, int bits)
    {
        Assert.True(f.Name == name, $"{name} is spelled stock's way");
        Assert.True(f.Kind == TePropKind.Angle,
            $"{name} is a SendPropAngle — DPT_Float with [0,360], one component at a time — "
            + "and not a SendPropQAngles, which is DPT_Vector with all three");
        Assert.True(f.Elems == 3 && f.ElemStride == 4,
            $"{name} becomes THREE props four bytes apart, not one");
        Assert.True(f.Bits == bits, $"{name} is {bits} bits per component");
        Assert.True(f.Flags == TeFlags.VectorElem,
            $"{name} carries the vector-elem instruction, which negates the declared offset");
        Assert.True(f.SizeBytes == 4, $"{name}'s props each read one float");
    }

    /// <summary>
    /// The model-index prop, whatever it is CALLED. The name is a parameter
    /// because this lane holds both converted and unconverted declarations: a
    /// converted one's prop is <c>ModelIndex</c>, an unconverted one's is still
    /// stock's <c>m_nModelIndex</c>, and hard-coding either spelling here would
    /// have made a shared helper enforce one lane's state on another's.
    /// </summary>
    private static void AssertModelIndex(TeField f, string name = "m_nModelIndex")
    {
        Assert.True(f.Name == name, $"the model index is spelled {name}");
        Assert.True(f.Kind == TePropKind.Int, "SendPropModelIndex IS a SendPropInt");
        Assert.True(f.BitsFrom == TeBits.ModelIndex,
            "of SP_MODEL_INDEX_BITS, resolved natively rather than restated as 13");
        Assert.True(f.Flags == TeFlags.None,
            "with flags of ZERO — dt_send.h:750 does not pass SPROP_UNSIGNED");
        Assert.True(f.SizeBytes == 4, "reading a four-byte int, which is what CNetworkVar(int) is");
    }

    private static void AssertFloat(TeField f, int bits, TeFlags flags, float low, float high)
    {
        Assert.True(f.Kind == TePropKind.Float, $"{f.Name} is a SendPropFloat");
        Assert.True(f.Bits == bits, $"{f.Name} is {bits} bits");
        Assert.True(f.Flags == flags, $"{f.Name} carries {flags} and nothing else");
        Assert.True(f.Low == low, $"{f.Name}'s range starts at {low}");
        Assert.True(f.High == high, $"{f.Name}'s range ends at {high}");
        Assert.True(f.SizeBytes == 4, $"{f.Name} reads a float out of the blob");
    }

    private static void AssertUnsignedInt(TeField f, int bits)
    {
        Assert.True(f.Kind == TePropKind.Int, $"{f.Name} is a SendPropInt");
        Assert.True(f.Bits == bits, $"{f.Name} is {bits} bits");
        Assert.True(f.BitsFrom == TeBits.Literal, $"{f.Name}'s width is a literal in stock's own table");
        Assert.True(f.Flags == TeFlags.Unsigned, $"{f.Name} is SPROP_UNSIGNED");
    }
}
