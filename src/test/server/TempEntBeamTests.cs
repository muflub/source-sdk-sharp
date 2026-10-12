using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

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
/// </summary>
public class TempEntBeamTests
{
    // ---------------------------------------------------------------------
    // The Part 6 rows.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheBaseBeamTableIsFifteenPropsAndClaimsNoClassYet()
    {
        TeClass te = BaseBeam();

        Assert.True(te.ClassName == "CTEBaseBeam", "DT_BaseBeam belongs to CTEBaseBeam");
        Assert.True(te.TableName == "DT_BaseBeam", "and is named DT_BaseBeam");
        Assert.True(te.BaseTable is null,
            "it is NOBASE — CTEBaseBeam derives from CBaseTempEntity in C++, but its "
          + "send table nests nothing, as IMPLEMENT_SERVERCLASS_ST_NOBASE says");

        // IT CLAIMS THE CLASS NAME, AND THE FLAG NOW MEANS SOMETHING DIFFERENT
        // ON EACH SIDE. It was false while BOTH native halves were compiled;
        // C-Phase 3 evicted c_te_basebeam.cpp, which took the CTEBaseBeam
        // ClientClass with it while the server's ServerClass stayed — and a
        // ServerClass with no matching ClientClass makes the client begin
        // instantiating and STOP with nothing in any log (plan 2E). So the
        // declaration claims the name and each shell decides for itself whether
        // to supply it: the client's does, the server's refuses out loud
        // because stock's is still there. See TeBaseBeam.cs.
        Assert.True(te.RegistersClass,
            "it claims the CTEBaseBeam class name, which the CLIENT shell now supplies "
          + "because c_te_basebeam.cpp is evicted and the server would otherwise have a "
          + "ServerClass with no counterpart");

        Assert.True(te.Fields.Length == 15, "fifteen props, and every beam carries all of them");
        Assert.True(te.BlobBytes == 48, "fifteen props in forty-eight bytes -- ten four-byte fields, a four-byte colour and the flags. It was sixty when the colour was four ints");
        Assert.True(te.BaseBytes == 0, "with nothing in front of them");
    }

    [Fact]
    public void TheBaseBeamPropsAreTheWidthsAndFlagsStockDeclares()
    {
        TeField[] f = BaseBeam().Fields;

        // The two model indices are SIGNED, because SendPropModelIndex is
        // SendPropInt( x, SP_MODEL_INDEX_BITS, 0 ) — flags zero, not
        // SPROP_UNSIGNED (dt_send.h:750, plan 2B).
        Assert.True(f[0].Name == "ModelIndex" && f[0].Kind == TePropKind.Int
                 && f[0].BitsFrom == TeBits.ModelIndex && f[0].Flags == TeFlags.None,
            "ModelIndex (stock's m_nModelIndex) is a signed SP_MODEL_INDEX_BITS int");
        Assert.True(f[1].Name == "HaloIndex" && f[1].BitsFrom == TeBits.ModelIndex
                 && f[1].Flags == TeFlags.None,
            "and so is HaloIndex (stock's m_nHaloIndex)");

        // The width is a SYMBOL and not a 13 typed here: native resolves it from
        // public/const.h, because a width with two copies drifts.
        Assert.True(f[0].Bits == -1 && f[1].Bits == -1,
            "neither carries a literal width; TeBits.ModelIndex is what native resolves");

        Assert.True(IsInt(f[2], "StartFrame", 8), "StartFrame is 8 bits unsigned");
        Assert.True(IsInt(f[3], "FrameRate", 8), "FrameRate is 8 bits unsigned");

        Assert.True(IsFloat(f[4], "Life", 8, TeFlags.None, 0f, 25.6f),
            "Life (stock's m_fLife) is 8 bits over [0, 25.6]");
        Assert.True(IsFloat(f[5], "Width", 10, TeFlags.None, 0f, 128f),
            "Width is 10 bits over [0, 128]");
        Assert.True(IsFloat(f[6], "EndWidth", 10, TeFlags.None, 0f, 128f),
            "EndWidth is 10 bits over [0, 128]");

        Assert.True(IsInt(f[7], "FadeLength", 8), "FadeLength is 8 bits unsigned");

        Assert.True(IsFloat(f[8], "Amplitude", 8, TeFlags.None, 0f, 64f),
            "Amplitude is 8 bits over [0, 64]");

        Assert.True(IsInt(f[9],  "Speed", 8), "Speed is 8 bits unsigned");
        Assert.True(IsInt(f[10], "ColorR", 8), "ColorR (stock's r) is 8 bits unsigned");
        Assert.True(IsInt(f[11], "ColorG", 8), "ColorG is 8 bits unsigned");
        Assert.True(IsInt(f[12], "ColorB", 8), "ColorB is 8 bits unsigned");
        Assert.True(IsInt(f[13], "ColorA", 8), "ColorA is 8 bits unsigned");
        Assert.True(IsInt(f[14], "Flags", 32), "Flags is a whole unsigned word");
    }

    [Fact]
    public void TheBeamEntPointTableIsTwoEntitiesThenTwoPoints()
    {
        TeClass te = Derived(typeof(TeBeamEntPoint));

        Assert.True(te.ClassName == "CTEBeamEntPoint", "the two sides pair on CTEBeamEntPoint");
        Assert.True(te.TableName == "DT_TEBeamEntPoint", "over DT_TEBeamEntPoint");
        Assert.True(te.BaseTable == "DT_BaseBeam", "which nests DT_BaseBeam");
        Assert.True(te.RegistersClass,
            "and it DOES claim a ServerClass: te_beamentpoint.cpp is evicted from the build");

        Assert.True(te.Fields.Length == 4, "four props of its own");

        // The order is the TABLE's, not the class's: te_beamentpoint.cpp:39-42
        // interleaves the ints and the vectors and the table does not.
        Assert.True(IsInt(te.Fields[0], "StartEntity", 24), "StartEntity first, 24 bits unsigned");
        Assert.True(IsInt(te.Fields[1], "EndEntity", 24), "then EndEntity, also 24");
        Assert.True(IsCoordVector(te.Fields[2], "StartPoint"), "then StartPoint");
        Assert.True(IsCoordVector(te.Fields[3], "EndPoint"), "then EndPoint");

        Assert.True(te.BaseBytes == 48, "its own fields start where DT_BaseBeam's fifteen end -- forty-eight now, because the colour is four bytes and was sixteen");
        Assert.True(te.Fields[0].Offset == 48 && te.Fields[1].Offset == 52
                 && te.Fields[2].Offset == 56 && te.Fields[3].Offset == 68,
            "at 48, 52, 56 and 68");
        Assert.True(te.BlobBytes == 80, "eighty bytes in all");
    }

    [Fact]
    public void TheBeamEntsTableIsTwoEntityIndices()
    {
        TeClass te = Derived(typeof(TeBeamEnts));

        Assert.True(te.ClassName == "CTEBeamEnts" && te.TableName == "DT_TEBeamEnts",
            "the two sides pair on CTEBeamEnts over DT_TEBeamEnts");
        Assert.True(te.BaseTable == "DT_BaseBeam", "nesting DT_BaseBeam");
        Assert.True(te.RegistersClass, "te_beaments.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 2, "two props of its own");
        Assert.True(IsInt(te.Fields[0], "StartEntity", 24), "StartEntity, 24 bits unsigned");
        Assert.True(IsInt(te.Fields[1], "EndEntity", 24), "EndEntity, 24 bits unsigned");

        Assert.True(te.Fields[0].Offset == 48 && te.Fields[1].Offset == 52, "at 48 and 52");
        Assert.True(te.BlobBytes == 56, "fifty-six bytes in all -- twelve fewer than when the colour was four ints");
    }

    [Fact]
    public void TheBeamFollowTableIsOneEntityIndexSpeltWithAnI()
    {
        TeClass te = Derived(typeof(TeBeamFollow));

        Assert.True(te.ClassName == "CTEBeamFollow" && te.TableName == "DT_TEBeamFollow",
            "the two sides pair on CTEBeamFollow over DT_TEBeamFollow");
        Assert.True(te.BaseTable == "DT_BaseBeam", "nesting DT_BaseBeam");
        Assert.True(te.RegistersClass, "te_beamfollow.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 1, "one prop of its own");

        // STOCK CALLED IT m_iEntIndex, with an i, where its six siblings say
        // m_nStartEntity -- the lane's one odd name. c_te_beamfollow.cpp's
        // hand-written RecvTable paired by that spelling and plan 2E forbade
        // tidying it; that table is evicted, so the wire name is the member's
        // and the oddity survives only in the parity message. The WIDTH is
        // still the thing worth pinning, and it is the sibling 24.
        Assert.True(IsInt(te.Fields[0], "EntIndex", 24),
            "EntIndex — stock's m_iEntIndex — 24 bits unsigned");

        Assert.True(te.Fields[0].Offset == 48, "at 48");
        Assert.True(te.BlobBytes == 52, "fifty-two bytes in all");
    }

    [Fact]
    public void TheBeamLaserTableIsTwoEntityIndicesAndStillItsOwnTable()
    {
        TeClass te = Derived(typeof(TeBeamLaser));

        Assert.True(te.ClassName == "CTEBeamLaser" && te.TableName == "DT_TEBeamLaser",
            "the two sides pair on CTEBeamLaser over DT_TEBeamLaser");
        Assert.True(te.BaseTable == "DT_BaseBeam", "nesting DT_BaseBeam");
        Assert.True(te.RegistersClass, "te_beamlaser.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 2, "two props of its own");
        Assert.True(IsInt(te.Fields[0], "StartEntity", 24), "StartEntity, 24 bits unsigned");
        Assert.True(IsInt(te.Fields[1], "EndEntity", 24), "EndEntity, 24 bits unsigned");
        Assert.True(te.BlobBytes == 56, "fifty-six bytes in all -- twelve fewer than when the colour was four ints");

        // Prop for prop identical to DT_TEBeamEnts, and deliberately NOT merged
        // with it: the difference is what the client does with it, and the set
        // of registered names must be exactly stock's.
        TeClass ents = Derived(typeof(TeBeamEnts));

        Assert.True(te.TableName != ents.TableName,
            "it has the same props as DT_TEBeamEnts and is still its own table");
        Assert.True(te.Fields.Select(f => f.Name).SequenceEqual(ents.Fields.Select(f => f.Name)),
            "the props really are the same two, which is why saying so is worth an assertion");
    }

    [Fact]
    public void TheBeamPointsTableIsTwoWorldCoordinates()
    {
        TeClass te = Derived(typeof(TeBeamPoints));

        Assert.True(te.ClassName == "CTEBeamPoints" && te.TableName == "DT_TEBeamPoints",
            "the two sides pair on CTEBeamPoints over DT_TEBeamPoints");
        Assert.True(te.BaseTable == "DT_BaseBeam", "nesting DT_BaseBeam");
        Assert.True(te.RegistersClass, "te_beampoints.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 2, "two props of its own");
        Assert.True(IsCoordVector(te.Fields[0], "StartPoint"), "StartPoint, one COORD vector prop");
        Assert.True(IsCoordVector(te.Fields[1], "EndPoint"), "EndPoint, likewise");

        Assert.True(te.Fields[0].Offset == 48 && te.Fields[1].Offset == 60, "at 48 and 60");
        Assert.True(te.Fields[0].SizeBytes == 12,
            "twelve bytes each — a SendPropVector is ONE prop over a whole vector, unlike "
          + "DT_TEParticleSystem's three SENDINFO_VECTORELEM floats");
        Assert.True(te.BlobBytes == 72, "seventy-two bytes in all");
    }

    /// <summary>
    /// The lane's one symbolic width, and the assertion that keeps it from being
    /// tidied into the 24 its five siblings use.
    /// </summary>
    [Fact]
    public void TheBeamRingTableUsesMaxEdictBitsWhereItsSiblingsUseALiteral24()
    {
        TeClass te = Derived(typeof(TeBeamRing));

        Assert.True(te.ClassName == "CTEBeamRing" && te.TableName == "DT_TEBeamRing",
            "the two sides pair on CTEBeamRing over DT_TEBeamRing");
        Assert.True(te.BaseTable == "DT_BaseBeam", "nesting DT_BaseBeam");
        Assert.True(te.RegistersClass, "te_beamring.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 2, "two props of its own");

        Assert.True(te.Fields[0].Name == "StartEntity"
                 && te.Fields[0].BitsFrom == TeBits.MaxEdict
                 && te.Fields[0].Bits == -1
                 && te.Fields[0].Flags == TeFlags.Unsigned,
            "StartEntity is MAX_EDICT_BITS wide, resolved natively and never typed here");
        Assert.True(te.Fields[1].Name == "EndEntity"
                 && te.Fields[1].BitsFrom == TeBits.MaxEdict
                 && te.Fields[1].Bits == -1,
            "and so is EndEntity");

        // The half that matters: the SAME two prop names on four sibling tables
        // carry a literal 24, and the two forms must not converge.
        foreach (Type sibling in new[] { typeof(TeBeamEnts), typeof(TeBeamLaser),
                                         typeof(TeBeamEntPoint) })
        {
            TeField[] f = Derived(sibling).Fields;

            Assert.True(f[0].BitsFrom == TeBits.Literal && f[0].Bits == 24,
                $"{sibling.Name}.m_nStartEntity keeps the literal 24 stock's own table gives it");
            Assert.True(f[1].BitsFrom == TeBits.Literal && f[1].Bits == 24,
                $"{sibling.Name}.m_nEndEntity keeps it too");
        }

        Assert.True(Derived(typeof(TeBeamFollow)).Fields[0].Bits == 24,
            "and CTEBeamFollow's single entity prop is a literal 24 as well");

        Assert.True(te.BlobBytes == 56, "fifty-six bytes in all -- twelve fewer than when the colour was four ints");
    }

    [Fact]
    public void TheBeamRingPointTableIsACentreAndTwoRoundedUpRadii()
    {
        TeClass te = Derived(typeof(TeBeamRingPoint));

        Assert.True(te.ClassName == "CTEBeamRingPoint" && te.TableName == "DT_TEBeamRingPoint",
            "the two sides pair on CTEBeamRingPoint over DT_TEBeamRingPoint");
        Assert.True(te.BaseTable == "DT_BaseBeam", "nesting DT_BaseBeam");
        Assert.True(te.RegistersClass, "te_beamringpoint.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 3, "three props of its own");
        Assert.True(IsCoordVector(te.Fields[0], "Center"), "Center, one COORD vector prop");

        Assert.True(IsFloat(te.Fields[1], "StartRadius", 16, TeFlags.RoundUp, 0f, 4096f),
            "StartRadius (stock's m_flStartRadius) is 16 bits ROUNDED UP over [0, 4096]");
        Assert.True(IsFloat(te.Fields[2], "EndRadius", 16, TeFlags.RoundUp, 0f, 4096f),
            "and EndRadius the same");

        Assert.True(te.Fields[0].Offset == 48 && te.Fields[1].Offset == 60
                 && te.Fields[2].Offset == 64,
            "at 48, 60 and 64");
        Assert.True(te.BlobBytes == 68, "sixty-eight bytes in all");
    }

    // ---------------------------------------------------------------------
    // Discovery, ordering, and the blob a writer reaches.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheRegistryFindsAllEightAndPublishesTheBaseFirst()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        foreach (string name in BeamClassNames)
            Assert.True(registry.Find(name) is not null, $"{name} was discovered by attribute");

        Assert.True(registry.Find("CTEBaseBeam") is not null, "and so was the base, CTEBaseBeam");

        var order = registry.All.Select(t => t.Class.TableName).ToList();
        int baseAt = order.IndexOf("DT_BaseBeam");

        Assert.True(baseAt >= 0, "DT_BaseBeam is in the published list");

        foreach (string table in BeamTableNames)
        {
            Assert.True(order.IndexOf(table) > baseAt,
                $"DT_BaseBeam is published before {table}, which nests it");
        }
    }

    /// <summary>
    /// A writer for any beam can set the base chain's fifteen fields as well as
    /// its own, because they are in the base's TABLE and in the derived's BLOB.
    /// </summary>
    [Fact]
    public void AWriterReachesTheBaseBeamsFieldsFromEveryDerivative()
    {
        foreach (string name in BeamClassNames)
        {
            TempEntType te = Registered(name);

            Assert.True(te.Fields.ContainsKey("ModelIndex"),
                $"{name} can set ModelIndex, though its own table does not declare it");
            Assert.True(te.Fields.ContainsKey("Flags"), $"{name} can set Flags");
            Assert.True(te.Fields.ContainsKey("ColorA"), $"{name} can set the brightness");
        }

        Assert.True(Registered("CTEBeamFollow").Fields.Count == 16,
            "sixteen fields in all for the smallest of them: fifteen inherited and one of its own");
        Assert.True(Registered("CTEBeamEntPoint").Fields.Count == 19,
            "and nineteen for the largest");
    }

    /// <summary>
    /// The writer puts every value where the offsets say — checked on the widest
    /// table in the lane, which is the one with the most ways to be wrong.
    /// </summary>
    [Fact]
    public void TheWriterPutsABeamsValuesWhereTheOffsetsSay()
    {
        TempEntType te = Registered("CTEBeamEntPoint");

        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        Assert.True(writer.Int("ModelIndex", 71), "the model index is written");
        Assert.True(writer.Float("Life", 2.0f), "the life is written");
        Assert.True(writer.Int("Speed", 3), "the speed is written");
        Assert.True(writer.Int("ColorA", 150), "the brightness is written");
        Assert.True(writer.Int("Flags", 0), "the flags are written");
        Assert.True(writer.Int("StartEntity", 4097), "the start entity is written");
        Assert.True(writer.Vector("EndPoint", new Vector(1f, 2f, 3f)), "the end point is written");

        Assert.True(BitConverter.ToInt32(blob, 0) == 71, "the model index at byte 0");
        Assert.True(BitConverter.ToSingle(blob, 16) == 2.0f, "the life at 16");
        Assert.True(BitConverter.ToInt32(blob, 36) == 3,
            "the speed at 36 — where the TABLE puts it, ten fields in, not where the header does");
        Assert.True(blob[43] == 150, "the brightness at 43 -- the alpha BYTE of a four-byte colour, where it used to be a four-byte int at 52");
        Assert.True(BitConverter.ToInt32(blob, 44) == 0, "the flags at 44");
        Assert.True(BitConverter.ToInt32(blob, 48) == 4097, "the start entity at 48, after the base");
        Assert.True(BitConverter.ToSingle(blob, 68) == 1f, "the end point's x at 68");
        Assert.True(BitConverter.ToSingle(blob, 76) == 3f, "and its z at 76");
    }

    /// <summary>
    /// Every beam blob is in the layout fingerprint the two sides exchange at
    /// join, by the C# type name — see <see cref="TempEntSchemaTests"/> for why
    /// that is the right key.
    /// </summary>
    [Fact]
    public void EveryBeamDeclarationIsAnEntityLayoutToo()
    {
        var layouts = new EntityLayoutRegistry(new RecordingLog());
        layouts.DiscoverAll(TestServices.ServerGame);

        var names = layouts.Layouts.Select(l => l.ClassName).ToList();

        foreach (string name in new[] { "TeBaseBeam", "TeBeamEntPoint", "TeBeamEnts",
                                        "TeBeamFollow", "TeBeamLaser", "TeBeamPoints",
                                        "TeBeamRing", "TeBeamRingPoint" })
        {
            Assert.True(names.Contains(name), $"{name}'s blob is in the fingerprint both sides compare");
        }

        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeBaseBeam").Size == 48,
            "DT_BaseBeam's own layout is forty-eight bytes");

        // A DERIVATIVE'S LAYOUT IS THE WHOLE BLOB NOW, AND THAT IS THE
        // CONVERSION'S DOING. It used to be its OWN twenty bytes, because the
        // declaration was a struct that named its base in a string and
        // EntityLayout had nothing to walk. TeBeamRingPoint derives from
        // TeBaseBeam, so EntityLayout.Build walks the chain root-first and the
        // layout IS the eighty bytes on the wire — the same number
        // TheBeamRingPointTableIsACentreAndTwoRoundedUpRadii asserts as
        // BlobBytes, which is the point: the fingerprint the two sides exchange
        // at join now describes the blob rather than a fragment of it.
        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeBeamRingPoint").Size == 68,
            "and a derivative's is its base chain's forty-eight plus its own twenty — the blob");

        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeBeamFollow").Size == 52,
            "the smallest of the seven likewise: sixty inherited and one field of its own");
    }

    // ---------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------

    private static readonly string[] BeamClassNames =
    {
        "CTEBeamEntPoint", "CTEBeamEnts", "CTEBeamFollow", "CTEBeamLaser",
        "CTEBeamPoints", "CTEBeamRing", "CTEBeamRingPoint",
    };

    private static readonly string[] BeamTableNames =
    {
        "DT_TEBeamEntPoint", "DT_TEBeamEnts", "DT_TEBeamFollow", "DT_TEBeamLaser",
        "DT_TEBeamPoints", "DT_TEBeamRing", "DT_TEBeamRingPoint",
    };

    private static TeClass BaseBeam() => TeClassBuilder.Build(typeof(TeBaseBeam));

    private static TeClass Derived(Type type)
        => TeClassBuilder.Build(type, BaseBeam().BlobBytes);

    private static TempEntType Registered(string className)
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry.Find(className)
            ?? throw new InvalidOperationException($"{className} was not discovered");
    }

    private static bool IsInt(TeField f, string name, int bits)
        => f.Name == name && f.Kind == TePropKind.Int && f.Bits == bits
        && f.BitsFrom == TeBits.Literal && f.Flags == TeFlags.Unsigned;

    private static bool IsFloat(TeField f, string name, int bits, TeFlags flags, float low, float high)
        => f.Name == name && f.Kind == TePropKind.Float && f.Bits == bits
        && f.BitsFrom == TeBits.Literal && f.Flags == flags
        && f.Low == low && f.High == high;

    private static bool IsCoordVector(TeField f, string name)
        => f.Name == name && f.Kind == TePropKind.Vector && f.Bits == -1
        && f.BitsFrom == TeBits.Literal && f.Flags == TeFlags.Coord;
}
