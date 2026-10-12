using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

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
/// The files are excluded from the BUILD (a managed ServerClass of that name
/// cannot coexist with stock's) but still present in the tree. Part 7 deletes
/// them; from then on these skip with the reason printed, and the Part 6 rows
/// below are what is left. Said out loud here so a later reader knows the
/// strongest half of this suite has an expiry date on it.
/// </para>
/// </summary>
public class TempEntSchemaTests
{
    // ---------------------------------------------------------------------
    // The Part 6 rows.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheBaseTableIsEmptyAndClaimsNoClassYet()
    {
        TeClass te = TeClassBuilder.Build(typeof(TeBaseTempEntity));

        Assert.True(te.ClassName == "CBaseTempEntity", "DT_BaseTempEntity belongs to CBaseTempEntity");
        Assert.True(te.TableName == "DT_BaseTempEntity", "and is named DT_BaseTempEntity");
        Assert.True(te.BaseTable is null, "it is NOBASE, as IMPLEMENT_SERVERCLASS_ST_NOBASE says");
        Assert.True(te.Fields.Length == 0, "it declares no props at all");
        Assert.True(te.BlobBytes == 0, "and therefore needs no blob");

        // The transitional state, asserted rather than assumed: stock's
        // CBaseTempEntity is still registered because thirty-eight native
        // te_*.cpp derive from it, and a second ServerClass of that name would
        // hang the join.
        Assert.True(!te.RegistersClass,
            "it is a table and not a ServerClass while stock still registers CBaseTempEntity");
    }

    [Fact]
    public void TheParticleSystemTableIsThreeCoordFloatsOfOneVector()
    {
        TeClass te = TeClassBuilder.Build(typeof(TeParticleSystem));

        Assert.True(te.ClassName == "CTEParticleSystem", "DT_TEParticleSystem belongs to CTEParticleSystem");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "and nests DT_BaseTempEntity");
        // It CLAIMS the class now, on both sides, and it is the first base in
        // the port to do so: nothing native derives from CTEParticleSystem or
        // registers it any more (lane F took Dust, lane CP took the other six
        // and every client half), so te_particlesystem.cpp is evicted and the
        // C_TEParticleSystem block is guarded out. Flipping this back to false
        // would leave a table with no class on EITHER side while the seven
        // derived classes still pair -- and the client would then decode a base
        // whose props nothing addressed.
        Assert.True(te.RegistersClass,
            "DT_TEParticleSystem's class is managed on both sides now");

        Assert.True(te.Fields.Length == 1, "one C# field carries the origin");

        TeField origin = te.Fields[0];

        Assert.True(origin.Name == "Origin",
            "the wire name is the MEMBER's now — stock called it m_vecOrigin, and nothing "
          + "pairs against that spelling any more: both halves build this prop from this one "
          + "declaration, so they agree with each other whatever it is called");
        Assert.True(origin.Kind == TePropKind.Float, "sent as floats, not as one vector prop");
        Assert.True(origin.Elems == 3, "three of them, as three SENDINFO_VECTORELEM lines");
        Assert.True(origin.ElemStride == 4, "four bytes apart, which is a float");
        Assert.True(origin.Bits == -1, "the width is -1, so the flag decides it");
        Assert.True(origin.Flags.HasFlag(TeFlags.Coord), "encoded as a world coordinate");
        Assert.True(origin.Flags.HasFlag(TeFlags.VectorElem),
            "declared the way SENDINFO_VECTORELEM does, with a negated offset");
        Assert.True(origin.Offset == 0, "it is the first thing in the blob");
        Assert.True(origin.SizeBytes == 4, "each prop reads one float, not the whole vector");

        Assert.True(te.BlobBytes == 12, "a vector's worth of blob, from EntityLayout and not by hand");
    }

    [Fact]
    public void TheSparksTableIsTwoNarrowIntsAndACoordVector()
    {
        TeClass te = Sparks();

        Assert.True(te.ClassName == "CTESparks", "the two sides pair on CTESparks");
        Assert.True(te.TableName == "DT_TESparks", "over DT_TESparks");
        Assert.True(te.BaseTable == "DT_TEParticleSystem", "which nests DT_TEParticleSystem");
        Assert.True(te.RegistersClass,
            "and it DOES claim a ServerClass: te_sparks.cpp is evicted from the build");

        Assert.True(te.Fields.Length == 3, "three props of its own");

        // Declaration order is wire order. A test that looked each field up by
        // name would pass on a reordered table, and reordering is a wire-format
        // change.
        Assert.True(te.Fields[0].Name == "Magnitude", "magnitude goes first (stock: m_nMagnitude)");
        Assert.True(te.Fields[1].Name == "TrailLength", "then the trail length (m_nTrailLength)");
        Assert.True(te.Fields[2].Name == "Direction", "then the direction (m_vecDir)");

        // AND THE MEMBER NAMES ARE STILL CARRIED SEPARATELY. They are equal to
        // the wire names now, which is exactly why this is asserted rather than
        // assumed: a TeField that had stopped recording which member it came
        // from would look identical from the wire side.
        Assert.True(te.Fields[0].MemberName == "Magnitude"
                 && te.Fields[1].MemberName == "TrailLength"
                 && te.Fields[2].MemberName == "Direction",
            "each field still knows the C# member it was built from");

        Assert.True(te.Fields[0].Kind == TePropKind.Int && te.Fields[0].Bits == 4
                 && te.Fields[0].Flags == TeFlags.Unsigned,
            "Magnitude is 4 bits unsigned — the kind INFERRED from int, the width still declared");
        Assert.True(te.Fields[1].Kind == TePropKind.Int && te.Fields[1].Bits == 4
                 && te.Fields[1].Flags == TeFlags.Unsigned, "TrailLength is 4 bits unsigned");
        Assert.True(te.Fields[2].Kind == TePropKind.Vector && te.Fields[2].Bits == -1
                 && te.Fields[2].Flags == TeFlags.Coord,
            "Direction is one COORD vector prop — inferred from the Vector member");
    }

    /// <summary>
    /// The blob a derived table addresses starts where its base's ends.
    ///
    /// <para>
    /// <c>SendProxy_DataTableToDataTable</c> shifts the object pointer by the
    /// datatable prop's offset, and stock declares that offset as zero — so the
    /// nested base's props read at THEIR own offsets out of the SAME blob. Get
    /// this wrong and the origin arrives as the magnitude, which is a plausible
    /// number.
    /// </para>
    /// </summary>
    [Fact]
    public void ADerivedBlobStartsWhereItsBaseEnds()
    {
        TeClass te = Sparks();

        Assert.True(te.BaseBytes == 12, "DT_TEParticleSystem's vector occupies the front of it");
        Assert.True(te.Fields[0].Offset == 12, "so Magnitude begins after it");
        Assert.True(te.Fields[1].Offset == 16, "and TrailLength after that");
        Assert.True(te.Fields[2].Offset == 20, "and the direction vector after that");
        Assert.True(te.BlobBytes == 32, "thirty-two bytes in all, computed and never written down");
    }

    // ---------------------------------------------------------------------
    // Discovery and the blob.
    // ---------------------------------------------------------------------

    /// <summary>
    /// A temp entity declaration IS an entity layout, and the fingerprint the
    /// two sides already exchange at join now covers it.
    ///
    /// <para>
    /// This is what unsealing <see cref="EntityFieldAttribute"/> bought, and it
    /// is worth more than the saved keystrokes: <c>EntityLayoutRegistry</c>
    /// finds a <c>[TeProp]</c> without knowing temp entities exist, so two
    /// builds whose TE blob layouts differ are caught by the check that already
    /// runs — rather than by a temp entity decoding into the wrong fields with
    /// nothing anywhere saying so.
    /// </para>
    ///
    /// <para>
    /// <c>TeBaseTempEntity</c> is deliberately absent: it declares no fields, so
    /// it has no layout to fingerprint, which is <c>HasEntityField</c>'s answer
    /// and the right one.
    /// </para>
    /// </summary>
    [Fact]
    public void ATempEntityDeclarationIsAnEntityLayoutToo()
    {
        var layouts = new EntityLayoutRegistry(new RecordingLog());
        layouts.DiscoverAll(TestServices.ServerGame);

        var names = layouts.Layouts.Select(l => l.ClassName).ToList();

        // BY THE C# TYPE NAME, not the wire class name. DiscoverAll builds each
        // layout without the attribute, so EntityLayout falls back to the type's
        // own name — which is a fingerprint over the SHAPE, and the shape is
        // what it exists to catch. TeClassBuilder passes the wire name for the
        // TeClass itself, where the two sides do have to agree on it.
        Assert.True(names.Contains("TeParticleSystem"),
            "DT_TEParticleSystem's blob is in the layout fingerprint the two sides compare");
        Assert.True(names.Contains("TeSparks"),
            "and so is DT_TESparks'");
        Assert.True(!names.Contains("TeBaseTempEntity"),
            "but DT_BaseTempEntity declares no fields, so it has no layout to fingerprint");

        // THIRTY-TWO, NOT TWENTY, AND THE CHANGE IS THE POINT.
        //
        // It was 20 — CTESparks' own three fields — when a declaration was a
        // struct that could not inherit. Now TeSparks : TeParticleSystem, so its
        // layout is the whole blob: Origin at 0, then the three at 12, 16 and 20.
        //
        // That is the more useful fingerprint. What the two sides must agree
        // about is the shape of the OBJECT the props read out of, and this now
        // covers all of it in one value instead of leaving the base to be
        // checked separately. Both sides load the same assembly and so compute
        // the same number; the wire is unchanged, which the byte-identity test
        // in TempEntDeclarationWriterTests is what actually proves.
        //
        // The TABLE is still its own fields only — TeClassBuilder asks for a
        // declared-only layout, because the base's props arrive through the
        // nested "baseclass" table and listing them twice would send them twice.
        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeSparks").Size == 32,
            "CTESparks' declaration inherits, so its layout is the whole blob: the base's "
          + "twelve bytes and then its own twenty");
    }

    [Fact]
    public void TheRegistryFindsThemByAttributeAndPublishesBasesFirst()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        int accepted = registry.RegisterAll("Test", TestServices.ServerGame, engine);

        Assert.True(accepted == registry.Count, "everything discovered was accepted");
        Assert.True(registry.Count >= 3, "the three S-Phase 1 declarations were found by attribute");

        Assert.True(registry.Find("CBaseTempEntity") is not null, "CBaseTempEntity was discovered");
        Assert.True(registry.Find("CTEParticleSystem") is not null, "CTEParticleSystem was discovered");
        Assert.True(registry.Find("CTESparks") is not null, "CTESparks was discovered");

        // Base before derived, and it is not cosmetic: a derived class's blob
        // size is its base's plus its own, and its native table has to nest one
        // that already exists.
        var order = registry.All.Select(t => t.Class.TableName).ToList();

        Assert.True(order.IndexOf("DT_BaseTempEntity") < order.IndexOf("DT_TEParticleSystem"),
            "DT_BaseTempEntity is published before the table that nests it");
        Assert.True(order.IndexOf("DT_TEParticleSystem") < order.IndexOf("DT_TESparks"),
            "and DT_TEParticleSystem before the table that nests IT");
    }

    /// <summary>
    /// A writer for CTESparks can set m_vecOrigin, which DT_TESparks does not
    /// declare — because the field is in the base's TABLE and in the derived's
    /// BLOB.
    /// </summary>
    [Fact]
    public void AWriterReachesTheBaseChainsFieldsAsWellAsItsOwn()
    {
        TempEntType sparks = Registered("CTESparks");

        Assert.True(sparks.Fields.ContainsKey("Origin"),
            "Origin is addressable from CTESparks, though DT_TESparks does not declare it — "
          + "it is DT_TEParticleSystem's prop and CTESparks' blob");
        Assert.True(sparks.Fields.ContainsKey("Magnitude"), "and so is its own Magnitude");
        Assert.True(sparks.Fields.Count == 4, "four fields in all: one inherited and three of its own");
    }

    [Fact]
    public void TheWriterPutsEveryValueWhereTheOffsetsSay()
    {
        TempEntType sparks = Registered("CTESparks");

        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);

        Assert.True(writer.Vector("Origin", new Vector(1f, 2f, 3f)), "the origin is written");
        Assert.True(writer.Int("Magnitude", 9), "the magnitude is written");
        Assert.True(writer.Int("TrailLength", 5), "the trail length is written");
        Assert.True(writer.Vector("Direction", new Vector(-1f, 0f, 0.5f)), "the direction is written");

        Assert.True(BitConverter.ToSingle(blob, 0) == 1f, "origin.x is at byte 0, where the base's prop reads");
        Assert.True(BitConverter.ToSingle(blob, 4) == 2f, "origin.y at 4");
        Assert.True(BitConverter.ToSingle(blob, 8) == 3f, "origin.z at 8");
        Assert.True(BitConverter.ToInt32(blob, 12) == 9, "the magnitude at 12, after the base");
        Assert.True(BitConverter.ToInt32(blob, 16) == 5, "the trail length at 16");
        Assert.True(BitConverter.ToSingle(blob, 20) == -1f, "dir.x at 20");
        Assert.True(BitConverter.ToSingle(blob, 28) == 0.5f, "dir.z at 28");
    }

    /// <summary>
    /// A name nothing declares writes nothing, and says so. The alternative is
    /// an effect that happens at the world origin because a misspelling left a
    /// coordinate as zero — which is a plausible value, not a missing one.
    /// </summary>
    [Fact]
    public void AMisspelledFieldWritesNothing()
    {
        TempEntType sparks = Registered("CTESparks");

        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);

        Assert.True(!writer.Int("Magnitide", 9), "a misspelled field is refused");
        Assert.True(blob.All(b => b == 0), "and nothing was written anywhere");
    }

    /// <summary>
    /// The blob is cleared when the writer takes it, so a field nobody set is a
    /// zero rather than whatever the last effect left behind.
    /// </summary>
    [Fact]
    public void TheWriterClearsWhatItIsGiven()
    {
        TempEntType sparks = Registered("CTESparks");

        var blob = new byte[sparks.BlobBytes];
        Array.Fill(blob, (byte)0xAB);

        var writer = new TempEntWriter(sparks, blob);
        writer.Int("Magnitude", 1);

        Assert.True(BitConverter.ToSingle(blob, 0) == 0f, "the origin nobody set is zero, not 0xABABABAB");
        Assert.True(BitConverter.ToInt32(blob, 16) == 0, "and so is the trail length");
    }

    // ---------------------------------------------------------------------
    // The one method ITempEnts has so far, end to end on this side.
    // ---------------------------------------------------------------------

    [Fact]
    public void SparksAsksTheEngineToPlayTheBytesItPacked()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 7);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);

        temps.Sparks(TeFilter.Broadcast, 0f, new Vector(64f, -32f, 8f), 6, 3,
                     new Vector(0f, 0f, 1f));

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 1, "one temp entity was sent");

        var call = engine.Last("Effects.TempEntPlayback")!.Value;

        Assert.True(call.ArgAs<int>(0) == 7, "under the handle native answered with");
        Assert.True(call.ArgAs<int>(1) == 32, "carrying a whole DT_TESparks blob");
        Assert.True(call.ArgAs<TeFilter>(2).Kind == TeFilterKind.Broadcast, "to everyone, as stock's TE_Sparks does");
        Assert.True(call.ArgAs<float>(3) == 0f, "with no delay");
    }

    /// <summary>
    /// A null direction is <c>vec3_origin</c>, which is stock's "no particular
    /// direction" rather than a direction of zero length — the distinction
    /// <c>te_sparks.cpp:84</c> makes and the one the client's effect reads.
    /// </summary>
    [Fact]
    public void ANullDirectionIsTheZeroVectorStockWrites()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 1);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        TempEntType sparks = registry.Find("CTESparks")!;

        var withNull = new byte[sparks.BlobBytes];
        var a = new TempEntWriter(sparks, withNull);
        a.Vector("Direction", default(Vector?) ?? Vector.Zero);

        var withZero = new byte[sparks.BlobBytes];
        var b = new TempEntWriter(sparks, withZero);
        b.Vector("Direction", Vector.Zero);

        Assert.True(withNull.SequenceEqual(withZero),
            "a null direction packs identically to an explicit zero");
    }

    /// <summary>
    /// A class native refused fires nothing and does not throw. A refusal is
    /// loud in the console at registration; a call afterwards must not be.
    /// </summary>
    [Fact]
    public void ARefusedClassFiresNothing()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", -1);

        var registry = new TempEntRegistry(new RecordingLog());
        int accepted = registry.RegisterAll("Test", TestServices.ServerGame, engine);

        Assert.True(accepted == 0, "native refused every one of them");

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);
        temps.Sparks(TeFilter.Broadcast, 0f, Vector.Zero, 1, 1, null);

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 0,
            "and nothing was sent, rather than a blob under handle -1");
    }

    // ---------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------

    private static TeClass Sparks()
        => TeClassBuilder.Build(typeof(TeSparks),
                                TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes);

    private static TempEntType Registered(string className)
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry.Find(className)
            ?? throw new InvalidOperationException($"{className} was not discovered");
    }
}

