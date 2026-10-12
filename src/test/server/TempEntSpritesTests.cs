using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

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
/// The <c>te_*.cpp</c> files are excluded from the BUILD — a managed
/// ServerClass of a name cannot coexist with stock's (plan 2E) — but are still
/// in the tree. Part 7 deletes them; from then on these skip with the reason
/// printed and the Part 6 rows are what is left.
/// </para>
/// </summary>
public class TempEntSpritesTests
{
    // =====================================================================
    // DT_TESprite
    // =====================================================================

    [Fact]
    public void TheSpriteTableIsAnOriginAModelAScaleAndABrightness()
    {
        TeClass te = Build<TeSprite>();

        Assert.True(te.ClassName == "CTESprite", "the two sides pair on CTESprite");
        Assert.True(te.TableName == "DT_TESprite", "over DT_TESprite");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");
        Assert.True(te.RegistersClass,
            "and it claims a ServerClass: te_sprite.cpp is evicted from the build");

        Assert.True(te.Fields.Length == 4, "four props of its own");

        // Declaration order is wire order; a lookup by name would pass on a
        // reordered table and reordering is a wire-format change.
        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "ModelIndex", "then the model index");
        Assert.True(te.Fields[2].Name == "Scale", "then the scale");
        Assert.True(te.Fields[3].Name == "Brightness", "then the brightness");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertModelIndex(te.Fields[1]);
        AssertFloat(te.Fields[2], 8, TeFlags.RoundDown, 0f, 25.6f);
        AssertUnsignedInt(te.Fields[3], 8);

        Assert.True(te.BaseBytes == 0, "DT_BaseTempEntity is empty, so its blob is empty");
        Assert.True(te.BlobBytes == 24, "twelve bytes of vector and three of four, computed not written down");
    }

    // =====================================================================
    // DT_TEGlowSprite
    // =====================================================================

    [Fact]
    public void TheGlowSpriteTableIsTheSpriteTableWithALifeInTheMiddle()
    {
        TeClass te = Build<TeGlowSprite>();

        Assert.True(te.ClassName == "CTEGlowSprite", "the two sides pair on CTEGlowSprite");
        Assert.True(te.TableName == "DT_TEGlowSprite", "over DT_TEGlowSprite");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 5, "five props of its own");

        // m_fLife is BETWEEN the scale and the brightness, though TE_GlowSprite's
        // parameter list puts life before size. The table is the wire.
        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "ModelIndex", "then the model index");
        Assert.True(te.Fields[2].Name == "Scale", "then the scale");
        Assert.True(te.Fields[3].Name == "Life", "then the life, AFTER the scale");
        Assert.True(te.Fields[4].Name == "Brightness", "then the brightness");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertModelIndex(te.Fields[1]);
        AssertFloat(te.Fields[2], 8, TeFlags.RoundDown, 0f, 25.6f);
        AssertFloat(te.Fields[3], 8, TeFlags.RoundDown, 0f, 25.6f);
        AssertUnsignedInt(te.Fields[4], 8);

        // The two floats encode identically, which is exactly why transposing
        // them would produce two plausible numbers and no error anywhere.
        Assert.True(te.Fields[2].Bits == te.Fields[3].Bits
                 && te.Fields[2].Flags == te.Fields[3].Flags
                 && te.Fields[2].High == te.Fields[3].High,
            "scale and life are indistinguishable on the wire, so their ORDER is the only thing "
            + "that keeps them apart");

        Assert.True(te.BlobBytes == 28, "twenty-eight bytes");
    }

    // =====================================================================
    // DT_TESpriteSpray
    // =====================================================================

    [Fact]
    public void TheSpriteSprayTableSendsNoiseBeforeSpeedAndOverItsOwnRange()
    {
        TeClass te = Build<TeSpriteSpray>();

        Assert.True(te.ClassName == "CTESpriteSpray", "the two sides pair on CTESpriteSpray");
        Assert.True(te.TableName == "DT_TESpriteSpray", "over DT_TESpriteSpray");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 6, "six props of its own");

        // CTESpriteSpray DECLARES m_nSpeed before m_fNoise and SENDS m_fNoise
        // first. The table is the wire; the member order is not.
        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Direction", "then the direction");
        Assert.True(te.Fields[2].Name == "ModelIndex", "then the model index");
        Assert.True(te.Fields[3].Name == "Noise", "then the NOISE, which the class declares last but one");
        Assert.True(te.Fields[4].Name == "Speed", "then the speed, which the class declares first of the two");
        Assert.True(te.Fields[5].Name == "Count", "then the count");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertCoordVector(te.Fields[1], "Direction");
        AssertModelIndex(te.Fields[2]);

        // [0, 2.56], one decimal point away from every other eight-bit
        // ROUNDDOWN float in this lane.
        AssertFloat(te.Fields[3], 8, TeFlags.RoundDown, 0f, 2.56f);
        Assert.True(te.Fields[3].High < 3f,
            "m_fNoise's range is 2.56, not 25.6 — a factor of ten in what arrives");

        AssertUnsignedInt(te.Fields[4], 8);
        AssertUnsignedInt(te.Fields[5], 8);

        Assert.True(te.BlobBytes == 40, "forty bytes: two vectors and four four-byte fields");
    }

    // =====================================================================
    // DT_TESmoke
    // =====================================================================

    [Fact]
    public void TheSmokeTableIsTheSpriteTableWithAFrameRate()
    {
        TeClass te = Build<TeSmoke>();

        Assert.True(te.ClassName == "CTESmoke", "the two sides pair on CTESmoke");
        Assert.True(te.TableName == "DT_TESmoke", "over DT_TESmoke");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 4, "four props of its own");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "ModelIndex", "then the model index");
        Assert.True(te.Fields[2].Name == "Scale", "then the scale");
        Assert.True(te.Fields[3].Name == "FrameRate", "then the frame rate");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertModelIndex(te.Fields[1]);
        AssertFloat(te.Fields[2], 8, TeFlags.RoundDown, 0f, 25.6f);
        AssertUnsignedInt(te.Fields[3], 8);

        Assert.True(te.BlobBytes == 24, "twenty-four bytes, the same shape as DT_TESprite");
    }

    // =====================================================================
    // DT_TEDynamicLight — the one with a signed int and both rounding flags
    // =====================================================================

    [Fact]
    public void TheDynamicLightTableMixesBothRoundingFlagsAndThreeRanges()
    {
        TeClass te = Build<TeDynamicLight>();

        Assert.True(te.ClassName == "CTEDynamicLight", "the two sides pair on CTEDynamicLight");
        Assert.True(te.TableName == "DT_TEDynamicLight", "over DT_TEDynamicLight");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 8, "eight props of its own");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "R", "then r — stock's own spelling, not m_r");
        Assert.True(te.Fields[2].Name == "G", "then g");
        Assert.True(te.Fields[3].Name == "B", "then b");
        Assert.True(te.Fields[4].Name == "Exponent", "then exponent");
        Assert.True(te.Fields[5].Name == "Radius", "then the radius");
        Assert.True(te.Fields[6].Name == "Time", "then the time");
        Assert.True(te.Fields[7].Name == "Decay", "then the decay");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertUnsignedInt(te.Fields[1], 8);
        AssertUnsignedInt(te.Fields[2], 8);
        AssertUnsignedInt(te.Fields[3], 8);

        // THE ONE SIGNED EIGHT-BIT INT IN THE WHOLE INVENTORY. Its flags are
        // literally 0 where the three props before it are SPROP_UNSIGNED.
        Assert.True(te.Fields[4].Kind == TePropKind.Int, "exponent is an int");
        Assert.True(te.Fields[4].Bits == 8, "of eight bits");
        Assert.True(te.Fields[4].Flags == TeFlags.None,
            "and NO flags — signed, unlike r, g and b immediately before it");
        Assert.True(!te.Fields[4].Flags.HasFlag(TeFlags.Unsigned),
            "SPROP_UNSIGNED here would spend the sign bit on magnitude and disagree with the "
            + "client on every value from 128 up");

        // ROUNDUP, ROUNDDOWN, ROUNDDOWN — each from its own line, and the
        // ranges do not move in step with them.
        AssertFloat(te.Fields[5], 8, TeFlags.RoundUp,   0f, 2560.0f);
        AssertFloat(te.Fields[6], 8, TeFlags.RoundDown, 0f, 25.6f);
        AssertFloat(te.Fields[7], 8, TeFlags.RoundDown, 0f, 2560.0f);

        Assert.True(te.Fields[5].Flags != te.Fields[6].Flags,
            "the radius rounds the other way from the time it sits beside");
        Assert.True(te.Fields[6].High != te.Fields[7].High,
            "and m_fTime's range is not m_fDecay's, though both are ROUNDDOWN");

        Assert.True(te.BlobBytes == 40, "forty bytes");
    }

    // =====================================================================
    // DT_TEShowLine — the lane's one non-DT_BaseTempEntity base
    // =====================================================================

    [Fact]
    public void TheShowLineTableIsOneVectorOnTopOfTheParticleSystemsOrigin()
    {
        TeClass te = ShowLine();

        Assert.True(te.ClassName == "CTEShowLine", "the two sides pair on CTEShowLine");
        Assert.True(te.TableName == "DT_TEShowLine", "over DT_TEShowLine");
        Assert.True(te.BaseTable == "DT_TEParticleSystem",
            "and it nests DT_TEParticleSystem, not DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 1, "one prop of its own");
        Assert.True(te.Fields[0].Name == "End", "the far end of the line (stock: m_vecEnd)");
        AssertCoordVector(te.Fields[0], "End");

        // The blob a derived table addresses starts where its base's ends: the
        // base is nested at datatable offset zero, so its props read at THEIR
        // own offsets out of the same object.
        Assert.True(te.BaseBytes == 12, "DT_TEParticleSystem's origin occupies the front of the blob");
        Assert.True(te.Fields[0].Offset == 12, "so End begins after it");
        Assert.True(te.BlobBytes == 24, "twenty-four bytes in all");
    }

    /// <summary>
    /// The line's NEAR end is the base's <c>m_vecOrigin</c>, and a writer for
    /// <c>CTEShowLine</c> has to be able to set it — <c>DT_TEShowLine</c> does
    /// not declare it, but this class's blob holds it.
    ///
    /// <para>
    /// Get this wrong and <c>TE_ShowLine</c> draws from the world origin to the
    /// end point, which is a plausible-looking line rather than a missing one.
    /// </para>
    /// </summary>
    [Fact]
    public void AShowLineWriterReachesTheStartPointItsOwnTableDoesNotDeclare()
    {
        TempEntType showLine = Registered("CTEShowLine");

        Assert.True(showLine.Fields.ContainsKey("Origin"),
            "Origin is addressable from CTEShowLine, though DT_TEShowLine does not declare it");
        Assert.True(showLine.Fields.ContainsKey("End"), "and so is its own End");
        Assert.True(showLine.Fields.Count == 2, "two fields in all: one inherited and one of its own");

        var blob = new byte[showLine.BlobBytes];
        var writer = new TempEntWriter(showLine, blob);

        Assert.True(writer.Vector("Origin", new Vector(1f, 2f, 3f)), "the start is written");
        Assert.True(writer.Vector("End", new Vector(4f, 5f, 6f)), "and the end is written");

        Assert.True(BitConverter.ToSingle(blob, 0) == 1f, "start.x at byte 0, where the base's prop reads");
        Assert.True(BitConverter.ToSingle(blob, 8) == 3f, "start.z at 8");
        Assert.True(BitConverter.ToSingle(blob, 12) == 4f, "end.x at 12, after the base");
        Assert.True(BitConverter.ToSingle(blob, 20) == 6f, "end.z at 20");
    }

    // =====================================================================
    // The lane as a whole.
    // =====================================================================

    [Fact]
    public void EveryClassInTheLaneIsDiscoveredByAttributeAndRegistered()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        foreach (string name in LaneClassNames)
        {
            Assert.True(registry.Find(name) is { Registered: true },
                $"{name} was discovered by [TempEntity] and accepted — there is no list file");
        }

        // Base before derived, and it is load-bearing rather than tidy:
        // CTEShowLine's blob starts where DT_TEParticleSystem's ends.
        var order = registry.All.Select(t => t.Class.TableName).ToList();

        Assert.True(order.IndexOf("DT_TEParticleSystem") < order.IndexOf("DT_TEShowLine"),
            "DT_TEParticleSystem is published before the table that nests it");
    }

    /// <summary>
    /// The census that stops a neighbour's flag being copied onto a field.
    ///
    /// <para>
    /// ROUNDUP AND ROUNDDOWN ARE NOT DECORATION. They pick which endpoint of a
    /// range the quantiser gives away, so the wrong one is an error only at the
    /// ends of the range and nothing at all in the middle — which is to say it
    /// survives every screenshot. This asserts the whole lane's rounding at
    /// once, so a copy-paste shows up as a census that changed rather than as
    /// one assertion somebody adjusted.
    /// </para>
    /// </summary>
    [Fact]
    public void ExactlyOneFieldInTheLaneRoundsUp()
    {
        var up   = new List<string>();
        var down = new List<string>();

        foreach (TeField f in LaneFields())
        {
            if (f.Flags.HasFlag(TeFlags.RoundUp))   up.Add(f.Name);
            if (f.Flags.HasFlag(TeFlags.RoundDown)) down.Add(f.Name);
        }

        Assert.True(up.Count == 1 && up[0] == "Radius",
            "DT_TEDynamicLight's m_fRadius — Radius on this build's wire — is the lane's ONLY "
            + "SPROP_ROUNDUP, and it sits between two ROUNDDOWNs in its own table");

        Assert.True(down.Count == 7,
            "seven fields round down: Scale three times (Sprite, GlowSprite, Smoke), then "
            + "Life, Noise, Time and Decay — stock's m_fScale, m_fLife, m_fNoise, m_fTime and "
            + $"m_fDecay — found [{string.Join(", ", down)}]");

        Assert.True(!up.Intersect(down).Any(), "and nothing carries both");
    }

    /// <summary>
    /// <c>exponent</c> is the one signed integer here, and everything else that
    /// is not a model index is <c>SPROP_UNSIGNED</c>.
    /// </summary>
    [Fact]
    public void ExponentIsTheOnlySignedNarrowIntInTheLane()
    {
        var signed = new List<string>();

        foreach (TeField f in LaneFields())
        {
            if (f.Kind != TePropKind.Int || f.BitsFrom != TeBits.Literal)
                continue;

            if (!f.Flags.HasFlag(TeFlags.Unsigned))
                signed.Add(f.Name);
        }

        Assert.True(signed.Count == 1 && signed[0] == "Exponent",
            "Exponent alone is signed — stock spells it `exponent`, lower case and no m_ "
          + $"— found [{string.Join(", ", signed)}]");
    }

    /// <summary>
    /// Every model index in the lane takes its width from the symbol and
    /// carries no flags.
    ///
    /// <para>
    /// <c>dt_send.h:750</c> is <c>SendPropInt( pVarName, offset, sizeofVar,
    /// SP_MODEL_INDEX_BITS, 0 )</c> — thirteen bits and <b>zero</b> flags, not
    /// <c>SPROP_UNSIGNED</c>. Four of this lane's six tables carry one, so
    /// getting it wrong once is getting it wrong four times.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryModelIndexIsSymbolicAndUnflagged()
    {
        var found = new List<string>();

        foreach (TeField f in LaneFields())
        {
            if (f.Name != "ModelIndex")
                continue;

            found.Add(f.Name);

            Assert.True(f.BitsFrom == TeBits.ModelIndex,
                "the width is SP_MODEL_INDEX_BITS, resolved natively — never a literal 13 restated here");
            Assert.True(f.Flags == TeFlags.None,
                "and SendPropModelIndex passes 0 for flags, not SPROP_UNSIGNED");
        }

        Assert.True(found.Count == 4,
            $"four of the six tables carry a model index — found {found.Count}");
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
            "TeSprite", "TeGlowSprite", "TeSpriteSpray", "TeSmoke", "TeDynamicLight", "TeShowLine",
        })
        {
            Assert.True(names.Contains(t), $"{t}'s blob is in the layout fingerprint the two sides compare");
        }

        // TWENTY-FOUR, NOT TWELVE, and the change is the conversion rather than
        // a wire move. TeShowLine is now a CLASS deriving TeParticleSystem, so
        // the layout the fingerprint is taken over covers the whole object — the
        // base's twelve bytes and its own twelve — where before it covered the
        // declared field alone and left the base to be fingerprinted separately.
        // Both sides load this same assembly, so both compute the same number;
        // the BLOB is twenty-four either way, which is what
        // TheShowLineTableIsOneVectorOnTopOfTheParticleSystemsOrigin asserts.
        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeShowLine").Size == 24,
            "CTEShowLine's layout is the base's twelve bytes and its own field");
    }

    /// <summary>
    /// <c>sprites/gunsmoke.vmt</c> is still precached, which is the one thing
    /// this lane could have quietly dropped.
    ///
    /// <para>
    /// <c>CTESprite::Precache</c> (<c>te_sprite.cpp:70</c>) took it, and
    /// <c>CBaseTempEntity::PrecacheTempEnts</c> reached it by walking the list
    /// of temp entity singletons. <c>CTESprite</c> is no longer on that list —
    /// <c>te_sprite.cpp</c> left the build with this lane — so without
    /// <see cref="SourceSharp.Server.ServerPrecache.PrecacheAll(IEnginePrecache, ILog, System.Reflection.Assembly[])"/> the model would stop
    /// being precached and NOTHING would say so: the symptom is a missing model
    /// at runtime, not a build error.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSpritesPrecacheSurvivedTheEvictionOfItsNativeClass()
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

        Assert.True(asked.Contains("sprites/gunsmoke.vmt"),
            "CTESprite's model is precached from managed code now that its native class is gone "
            + $"— asked for [{string.Join(", ", asked)}]");
    }

    // =====================================================================
    // Fixtures and the stock parser.
    // =====================================================================

    private static readonly string[] LaneClassNames =
    {
        "CTESprite", "CTEGlowSprite", "CTESpriteSpray", "CTESmoke", "CTEDynamicLight", "CTEShowLine",
    };

    private static TeClass Build<T>() => TeClassBuilder.Build(typeof(T));

    private static TeClass ShowLine()
        => TeClassBuilder.Build(typeof(TeShowLine),
                                TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes);

    /// <summary>Every field this lane declares, its own only — not the bases'.</summary>
    private static IEnumerable<TeField> LaneFields()
        => Build<TeSprite>().Fields
            .Concat(Build<TeGlowSprite>().Fields)
            .Concat(Build<TeSpriteSpray>().Fields)
            .Concat(Build<TeSmoke>().Fields)
            .Concat(Build<TeDynamicLight>().Fields)
            .Concat(ShowLine().Fields);

    private static TempEntType Registered(string className)
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry.Find(className)
            ?? throw new InvalidOperationException($"{className} was not discovered");
    }

    private static void AssertCoordVector(TeField f, string name)
    {
        Assert.True(f.Name == name, $"{name} is spelled stock's way");
        Assert.True(f.Kind == TePropKind.Vector, $"{name} is one SendPropVector, not three elements");
        Assert.True(f.Bits == -1, $"{name}'s width is -1, so SPROP_COORD decides it");
        Assert.True(f.Flags == TeFlags.Coord, $"{name} is a world coordinate and nothing else");
        Assert.True(f.SizeBytes == 12, $"{name} reads a whole Vector out of the blob");
    }

    private static void AssertModelIndex(TeField f)
    {
        Assert.True(f.Name == "ModelIndex",
            "the model index goes on this build's wire as ModelIndex, where stock spelled it "
          + "m_nModelIndex");
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
