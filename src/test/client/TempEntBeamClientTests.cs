using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Lane CA's client half: the seven beam temp entities coming off the wire into
/// C# and reaching the six <c>BeamCreate*</c> backend slots, plus
/// <c>DT_BaseBeam</c> itself moving to the managed client.
///
/// <para>
/// WHAT THESE CATCH THAT A DUMP CANNOT. <c>cs_cl_te_dump</c> answers "the table
/// is the shape the declaration says", and both of its columns come from that
/// one declaration — so it is silent about everything downstream of the decode,
/// which is where a beam's mistakes live. The six primitives take between
/// twelve and eighteen arguments, eleven of them float, and five of those are
/// life, width, end width, fade length and amplitude IN A ROW: any two swapped
/// compiles, links, runs, and draws a beam that looks approximately right. So
/// every field in every blob below carries a DISTINCT value and every argument
/// is asserted by position.
/// </para>
///
/// <para>
/// THE THREE FACTS THIS LANE IS MOST EXPOSED TO, each with an assertion of its
/// own rather than a comment. <c>DT_BaseBeam</c> sends <c>m_nSpeed</c> BEFORE
/// <c>r g b a</c>, which is not the member order in <c>te_basebeam.h</c>.
/// <c>a</c> on the wire is BRIGHTNESS at every one of stock's call sites.
/// And <c>CreateBeamFollow</c>'s argument order is not its five siblings' — it
/// takes <c>r, g, b</c> before brightness and has no speed, start frame, frame
/// rate or amplitude at all.
/// </para>
///
/// <para>
/// The schema half of this lane — the eight tables against the stock
/// <c>te_beam*.cpp</c> they were transcribed from, read off disk — is
/// <see cref="TempEntBeamTests"/> and stays there. This file is only about what
/// the CLIENT does with a blob once it has one.
/// </para>
/// </summary>
public class TempEntBeamClientTests
{
    // ---------------------------------------------------------------------
    // The fifteen base values, distinct on purpose.
    //
    // No two are equal, so an argument that reached the primitive from the
    // wrong field is a failed assertion rather than a coincidence. The two
    // eight-bit ones that get scaled -- m_nFrameRate and m_nSpeed -- are 9 and
    // 13, which are two of the fifty values in 0..255 where a DOUBLE tenth and
    // a FLOAT tenth differ; see TheTenthIsDoneInDoubleAsEveryStockCallSiteDoes.
    // ---------------------------------------------------------------------

    private const int   ModelIndex = 601;
    private const int   HaloIndex  = 602;
    private const int   StartFrame = 3;
    private const int   FrameRate  = 9;
    private const float Life       = 2.5f;
    private const float Width      = 4.25f;
    private const float EndWidth   = 6.5f;
    private const int   FadeLength = 7;
    private const float Amplitude  = 1.75f;
    private const int   Speed      = 13;
    private const int   Red        = 21;
    private const int   Green      = 22;
    private const int   Blue       = 23;
    private const int   Alpha      = 24;
    private const int   Flags      = 0x40;

    /// <summary>
    /// <c>0.1 * n</c> with a DOUBLE tenth, narrowed once — which is what every
    /// <c>c_te_beam*.cpp</c> call site spells and what
    /// <see cref="TeBaseBeam.ScrollSpeed"/> reproduces. Written here as the same
    /// expression only because the tests that MATTER for it assert exact
    /// literals instead; see the double/float test below.
    /// </summary>
    private static float Tenth(int value) => (float)(0.1 * value);

    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// A registry over the real shared declarations, native answering a
    /// distinct handle to each registration.
    /// </summary>
    private static TempEntRegistry Registered(RecordingEngine engine, int handle = 5)
    {
        int next = handle;

        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        return registry;
    }

    /// <summary>The seven players, built by hand so the wiring is not what is under test.</summary>
    private static ClientTempEnts Players(TempEntRegistry registry, RecordingEngine engine)
    {
        var players = new ClientTempEnts(new RecordingLog(), registry);
        var beams = new RecordingBeams(engine);

        players.Add("CTEBeamPoints",    new ClientTeBeamPoints(beams));
        players.Add("CTEBeamEnts",      new ClientTeBeamEnts(beams));
        players.Add("CTEBeamEntPoint",  new ClientTeBeamEntPoint(beams));
        players.Add("CTEBeamLaser",     new ClientTeBeamLaser(beams));
        players.Add("CTEBeamRing",      new ClientTeBeamRing(beams));
        players.Add("CTEBeamRingPoint", new ClientTeBeamRingPoint(beams));
        players.Add("CTEBeamFollow",    new ClientTeBeamFollow(beams));

        return players;
    }

    /// <summary>
    /// A blob for one beam class with all fifteen base fields filled. The
    /// caller adds its own class's fields and dispatches.
    /// </summary>
    private static byte[] BlobFor(TempEntType type, out TempEntWriter writer)
    {
        var blob = new byte[type.BlobBytes];
        writer = new TempEntWriter(type, blob);

        writer.Int("ModelIndex",  ModelIndex);
        writer.Int("HaloIndex",   HaloIndex);
        writer.Int("StartFrame",  StartFrame);
        writer.Int("FrameRate",   FrameRate);
        writer.Float("Life",      Life);
        writer.Float("Width",     Width);
        writer.Float("EndWidth",  EndWidth);
        writer.Int("FadeLength",  FadeLength);
        writer.Float("Amplitude", Amplitude);
        writer.Int("Speed",       Speed);
        writer.Int("ColorR",              Red);
        writer.Int("ColorG",              Green);
        writer.Int("ColorB",              Blue);
        writer.Int("ColorA",              Alpha);
        writer.Int("Flags",       Flags);

        return blob;
    }

    // =====================================================================
    // DT_BaseBeam ITSELF — the thing this lane could move and no other could.
    // =====================================================================

    /// <summary>
    /// The base's fifteen fields are readable out of every derived class's
    /// blob, at the offsets the base itself uses. That is what "nested at
    /// datatable offset zero" means; a base whose fields moved would put a
    /// derived class's first field where the base's last prop reads, and the
    /// beam would draw with plausible numbers.
    /// </summary>
    [Fact]
    public void EverySevenBeamsBlobCarriesTheBasesFifteenFieldsAtTheBasesOffsets()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);

        TempEntType baseBeam = registry.Find("CTEBaseBeam")!;

        // DT_BaseBeam's fifteen, in the TABLE's order and under the names the
        // wire carries since the declaration conversion: TeBaseBeam's members,
        // where stock's table said m_nModelIndex ... m_nSpeed, r, g, b, a,
        // m_nFlags. Typed out rather than read off the declaration, because a
        // list computed from the thing under test agrees with it however wrong
        // it is.
        string[] names =
        {
            "ModelIndex", "HaloIndex", "StartFrame", "FrameRate",
            "Life", "Width", "EndWidth", "FadeLength", "Amplitude",
            "Speed", "ColorR", "ColorG", "ColorB", "ColorA", "Flags",
        };

        foreach (string className in new[]
                 {
                     "CTEBeamPoints", "CTEBeamEnts", "CTEBeamEntPoint", "CTEBeamLaser",
                     "CTEBeamRing", "CTEBeamRingPoint", "CTEBeamFollow",
                 })
        {
            TempEntType derived = registry.Find(className)!;

            foreach (string name in names)
            {
                Assert.True(derived.Fields.ContainsKey(name),
                    $"{className} can address DT_BaseBeam's {name}");
                Assert.True(derived.Fields[name].Offset == baseBeam.Fields[name].Offset,
                    $"{className}'s {name} is at the offset the base's own prop reads from");
            }

            Assert.True(derived.Class.BaseTable == "DT_BaseBeam",
                $"{className} nests DT_BaseBeam and not something else");
        }
    }

    /// <summary>
    /// <c>m_nSpeed</c> is at a LOWER offset than <c>r</c>, which is the whole of
    /// the trap this base carries: <c>te_basebeam.h</c> declares the colour
    /// first and the send table sends the speed first, and a transcription of
    /// the header would decode speed as red and alpha as speed — four plausible
    /// numbers, no error anywhere.
    ///
    /// <para>
    /// Asserted on the BLOB rather than on the declaration, because the
    /// declaration's order is <see cref="TempEntBeamTests"/>' business and this
    /// is the half that decides what the client reads.
    /// </para>
    /// </summary>
    [Fact]
    public void SpeedSitsBeforeTheColourInEveryBeamsBlob()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType points = registry.Find("CTEBeamPoints")!;

        int speed = points.Fields["Speed"].Offset;

        Assert.True(speed < points.Fields["ColorR"].Offset,
            "Speed (stock's m_nSpeed) is in front of Red (stock's r)");
        Assert.True(points.Fields["ColorR"].Offset < points.Fields["ColorG"].Offset, "then Green");
        Assert.True(points.Fields["ColorG"].Offset < points.Fields["ColorB"].Offset, "then Blue");
        Assert.True(points.Fields["ColorB"].Offset < points.Fields["ColorA"].Offset, "then Alpha");
        Assert.True(points.Fields["ColorA"].Offset < points.Fields["Flags"].Offset,
            "and Flags last of the fifteen");

        // The read that would go wrong, done for real: distinct values in and
        // distinct values out. Offsets alone would not say it — a blob whose
        // speed and red had swapped is still self-consistent — so this
        // dispatches and reads the arguments back.
        byte[] blob = BlobFor(points, out TempEntWriter writer);
        writer.Vector("StartPoint", new Vector(1f, 2f, 3f));
        writer.Vector("EndPoint",   new Vector(4f, 5f, 6f));

        Players(registry, engine).Dispatch(points.Handle, blob);

        EngineCall call = engine.Last("Beams.CreateBeamPoints")!.Value;

        Assert.True(call.ArgAs<float>(11) == Tenth(Speed), "the speed reached the primitive scaled");
        Assert.True(call.ArgAs<float>(14) == Red, "and red is red");
        Assert.True(call.ArgAs<float>(15) == Green, "and green is green");
        Assert.True(call.ArgAs<float>(16) == Blue, "and blue is blue");
    }

    /// <summary>
    /// The tenth is a DOUBLE multiply narrowed once, which is what stock's
    /// <c>0.1 * m_nSpeed</c> is, and it is not <c>0.1f * n</c>.
    ///
    /// <para>
    /// Fifty of the 256 values an eight-bit prop can hold give a different
    /// float from the two spellings. 9 is one of them: the double form is
    /// 0.8999999761581421 and the float form is 0.9000000357627869. Both
    /// literals are written out, so this test fails if the production code
    /// switches to either the float form OR to a plain <c>n / 10f</c>, and it
    /// cannot be satisfied by restating whichever expression the code happens
    /// to use.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTenthIsDoneInDoubleAsEveryStockCallSiteDoes()
    {
        const float DoubleTenthOfNine = 0.8999999761581421f;
        const float FloatTenthOfNine  = 0.9000000357627869f;

        Assert.True(DoubleTenthOfNine != FloatTenthOfNine,
            "the two spellings really do differ at 9, which is what makes this test able to fail");

        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType points = registry.Find("CTEBeamPoints")!;

        byte[] blob = BlobFor(points, out TempEntWriter writer);
        writer.Vector("StartPoint", new Vector(0f, 0f, 0f));
        writer.Vector("EndPoint",   new Vector(0f, 0f, 128f));

        Players(registry, engine).Dispatch(points.Handle, blob);

        EngineCall call = engine.Last("Beams.CreateBeamPoints")!.Value;

        // m_nFrameRate is 9.
        Assert.True(call.ArgAs<float>(13) == DoubleTenthOfNine,
            "the frame rate arrived as the double tenth of 9");
        Assert.True(call.ArgAs<float>(13) != FloatTenthOfNine,
            "and NOT as the float tenth, which is a different number");
    }

    // =====================================================================
    // THE SEVEN, one [Fact] each, every argument by position.
    // =====================================================================

    [Fact]
    public void BeamPointsReachesItsPrimitiveWithEveryArgumentInPlace()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType te = registry.Find("CTEBeamPoints")!;

        byte[] blob = BlobFor(te, out TempEntWriter writer);
        writer.Vector("StartPoint", new Vector(-3667f, 3439f, 60f));
        writer.Vector("EndPoint",   new Vector(-3667f, 3439f, 188f));

        Players(registry, engine).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Beams.CreateBeamPoints") == 1, "one beam was created");

        EngineCall c = engine.Last("Beams.CreateBeamPoints")!.Value;

        Assert.True(Same(c.ArgAs<Vector>(0), new Vector(-3667f, 3439f, 60f)), "start point");
        Assert.True(Same(c.ArgAs<Vector>(1), new Vector(-3667f, 3439f, 188f)), "end point");
        Assert.True(c.ArgAs<int>(2)   == ModelIndex, "model index");
        Assert.True(c.ArgAs<int>(3)   == HaloIndex, "halo index");
        Assert.True(c.ArgAs<float>(4) == 0f,
            "halo SCALE is zero: DT_BaseBeam has no prop for it and all seven of stock's "
          + "beams pass 0.0f");
        Assert.True(c.ArgAs<float>(5) == Life, "life");
        Assert.True(c.ArgAs<float>(6) == Width, "width");
        Assert.True(c.ArgAs<float>(7) == EndWidth, "end width, which is NOT the width");
        Assert.True(c.ArgAs<float>(8) == FadeLength, "fade length");
        Assert.True(c.ArgAs<float>(9) == Amplitude, "amplitude");
        Assert.True(c.ArgAs<float>(10)  == Alpha,
            "BRIGHTNESS is the wire's `a` — the rename happens at this boundary and the "
          + "value does not change");
        Assert.True(c.ArgAs<float>(11) == Tenth(Speed), "speed, scaled by the tenth");
        Assert.True(c.ArgAs<int>(12)   == StartFrame, "start frame, NOT scaled");
        Assert.True(c.ArgAs<float>(13) == Tenth(FrameRate), "frame rate, scaled");
        Assert.True(c.ArgAs<float>(14)   == Red && c.ArgAs<float>(15) == Green
                 && c.ArgAs<float>(16)   == Blue, "r, g, b, in that order");
    }

    [Fact]
    public void BeamEntsReachesItsPrimitiveWithTheIndicesStillBeamEntEncoded()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType te = registry.Find("CTEBeamEnts")!;

        // What the server actually sends: index in the low twelve bits, an
        // ATTACHMENT of one above it. Passed on unsplit, because view_beams.cpp
        // is what splits it and a second splitter here would drift from it.
        const int PackedStart = (17 & 0x0FFF) | (1 << 12);
        const int PackedEnd   = (18 & 0x0FFF) | (1 << 12);

        byte[] blob = BlobFor(te, out TempEntWriter writer);
        writer.Int("StartEntity", PackedStart);
        writer.Int("EndEntity",   PackedEnd);

        Players(registry, engine).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Beams.CreateBeamEnts") == 1, "one beam was created");

        EngineCall c = engine.Last("Beams.CreateBeamEnts")!.Value;

        Assert.True(c.ArgAs<int>(0) == PackedStart,
            "the start endpoint arrives BEAMENT-encoded, attachment nibble and all");
        Assert.True(c.ArgAs<int>(1) == PackedEnd, "and so does the end");
        Assert.True(c.ArgAs<int>(0) != 17,
            "which is not the bare index — a player that unpacked here would draw at the "
          + "model's origin instead of at attachment 1");

        Assert.True(c.ArgAs<int>(2)    == ModelIndex && c.ArgAs<int>(3) == HaloIndex, "sprites");
        Assert.True(c.ArgAs<float>(4)  == 0f, "halo scale");
        Assert.True(c.ArgAs<float>(5)  == Life && c.ArgAs<float>(6) == Width
                 && c.ArgAs<float>(7)  == EndWidth && c.ArgAs<float>(8) == FadeLength
                 && c.ArgAs<float>(9)  == Amplitude, "the five floats in a row, unswapped");
        Assert.True(c.ArgAs<float>(10)   == Alpha, "brightness is `a`");
        Assert.True(c.ArgAs<float>(11) == Tenth(Speed), "speed");
        Assert.True(c.ArgAs<int>(12)   == StartFrame, "start frame");
        Assert.True(c.ArgAs<float>(13) == Tenth(FrameRate), "frame rate");
        Assert.True(c.ArgAs<float>(14)   == Red && c.ArgAs<float>(15) == Green
                 && c.ArgAs<float>(16)   == Blue, "colour");
        Assert.True(c.ArgAs<int>(17)   == -1,
            "and the TYPE is the primitive's own default, which is the whole difference "
          + "between this and CTEBeamLaser");
    }

    /// <summary>
    /// <c>DT_TEBeamLaser</c> is prop for prop <c>DT_TEBeamEnts</c> and reaches
    /// the same primitive. The whole of the difference is the last argument, so
    /// that is what this asserts — and it asserts the number rather than "not
    /// -1", because <c>TE_BEAMLASER</c> is 8 and a beam created as some other
    /// kind would still be a beam.
    /// </summary>
    [Fact]
    public void BeamLaserIsBeamEntsWithTheOneArgumentThatMakesItALaser()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType laser = registry.Find("CTEBeamLaser")!;
        TempEntType ents  = registry.Find("CTEBeamEnts")!;

        Assert.True(laser.BlobBytes == ents.BlobBytes,
            "the two blobs are the same size, because the two tables are the same shape");

        byte[] blob = BlobFor(laser, out TempEntWriter writer);
        writer.Int("StartEntity", 31);
        writer.Int("EndEntity",   32);

        Players(registry, engine).Dispatch(laser.Handle, blob);

        EngineCall c = engine.Last("Beams.CreateBeamEnts")!.Value;

        Assert.True(c.ArgAs<int>(17) == 8,
            "TE_BEAMLASER, game/shared/tempentity.h:31 — and not the -1 CTEBeamEnts passes");
        Assert.True(c.ArgAs<int>(0) == 31 && c.ArgAs<int>(1) == 32, "between the two entities");
        Assert.True(c.ArgAs<float>(10) == Alpha, "brightness is `a` here too");
        Assert.True(c.ArgAs<float>(11) == Tenth(Speed), "and the speed is scaled");
    }

    [Fact]
    public void BeamEntPointCarriesBothIndicesAndBothPoints()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType te = registry.Find("CTEBeamEntPoint")!;

        byte[] blob = BlobFor(te, out TempEntWriter writer);
        writer.Int("StartEntity", 0);            // zero: "use the point instead"
        writer.Int("EndEntity",   (5 & 0x0FFF) | (1 << 12));
        writer.Vector("StartPoint", new Vector(10f, 20f, 30f));
        writer.Vector("EndPoint",   new Vector(40f, 50f, 60f));

        Players(registry, engine).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Beams.CreateBeamEntPoint") == 1, "one beam was created");

        EngineCall c = engine.Last("Beams.CreateBeamEntPoint")!.Value;

        Assert.True(c.ArgAs<int>(0) == 0, "a start entity of zero, which the primitive reads as "
                                        + "'there is no entity, use the point'");
        Assert.True(Same(c.ArgAs<Vector>(1), new Vector(10f, 20f, 30f)), "the start point");
        Assert.True(c.ArgAs<int>(2) == ((5 & 0x0FFF) | (1 << 12)), "the packed end entity");
        Assert.True(Same(c.ArgAs<Vector>(3), new Vector(40f, 50f, 60f)), "the end point");

        // The interleaving is what makes this one worth asserting in full: it is
        // the only beam whose entity and point arguments alternate, so a decoder
        // that grouped them would compile and put the beam somewhere else.
        Assert.True(c.ArgAs<int>(4)    == ModelIndex && c.ArgAs<int>(5) == HaloIndex, "sprites");
        Assert.True(c.ArgAs<float>(6)  == 0f, "halo scale");
        Assert.True(c.ArgAs<float>(7)  == Life && c.ArgAs<float>(8) == Width
                 && c.ArgAs<float>(9)  == EndWidth && c.ArgAs<float>(10) == FadeLength
                 && c.ArgAs<float>(11) == Amplitude, "the five floats in a row");
        Assert.True(c.ArgAs<float>(12)   == Alpha, "brightness");
        Assert.True(c.ArgAs<float>(13) == Tenth(Speed), "speed");
        Assert.True(c.ArgAs<int>(14)   == StartFrame, "start frame");
        Assert.True(c.ArgAs<float>(15) == Tenth(FrameRate), "frame rate");
        Assert.True(c.ArgAs<float>(16)   == Red && c.ArgAs<float>(17) == Green
                 && c.ArgAs<float>(18)   == Blue, "colour");
    }

    /// <summary>
    /// The ring between two entities, and the one place in the lane where
    /// <c>m_nFlags</c> reaches the renderer.
    /// </summary>
    [Fact]
    public void BeamRingPassesTheWiresFlagsOn()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType te = registry.Find("CTEBeamRing")!;

        byte[] blob = BlobFor(te, out TempEntWriter writer);
        writer.Int("StartEntity", 41);
        writer.Int("EndEntity",   42);

        Players(registry, engine).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Beams.CreateBeamRing") == 1, "one ring was created");

        EngineCall c = engine.Last("Beams.CreateBeamRing")!.Value;

        Assert.True(c.ArgAs<int>(0) == 41 && c.ArgAs<int>(1) == 42, "between the two entities");
        Assert.True(c.ArgAs<int>(2)    == ModelIndex && c.ArgAs<int>(3) == HaloIndex, "sprites");
        Assert.True(c.ArgAs<float>(4)  == 0f, "halo scale");
        Assert.True(c.ArgAs<float>(5)  == Life && c.ArgAs<float>(6) == Width
                 && c.ArgAs<float>(7)  == EndWidth && c.ArgAs<float>(8) == FadeLength
                 && c.ArgAs<float>(9)  == Amplitude, "the five floats in a row");
        Assert.True(c.ArgAs<float>(10)   == Alpha, "brightness");
        Assert.True(c.ArgAs<float>(11) == Tenth(Speed), "speed");
        Assert.True(c.ArgAs<int>(12)   == StartFrame, "start frame");
        Assert.True(c.ArgAs<float>(13) == Tenth(FrameRate), "frame rate");
        Assert.True(c.ArgAs<float>(14)   == Red && c.ArgAs<float>(15) == Green
                 && c.ArgAs<float>(16)   == Blue, "colour");
        Assert.True(c.ArgAs<int>(17)   == Flags,
            "and m_nFlags, which is how FBEAM_* reaches the renderer at all — five of the "
          + "seven beams have no flags argument to put it in");
    }

    [Fact]
    public void BeamRingPointCarriesItsCentreAndTwoRadii()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType te = registry.Find("CTEBeamRingPoint")!;

        byte[] blob = BlobFor(te, out TempEntWriter writer);
        writer.Vector("Center", new Vector(-100f, 200f, 64f));
        writer.Float("StartRadius", 16f);
        writer.Float("EndRadius", 256f);

        Players(registry, engine).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Beams.CreateBeamRingPoint") == 1, "one ring was created");

        EngineCall c = engine.Last("Beams.CreateBeamRingPoint")!.Value;

        Assert.True(Same(c.ArgAs<Vector>(0), new Vector(-100f, 200f, 64f)), "the centre");
        Assert.True(c.ArgAs<float>(1) == 16f, "the start radius");
        Assert.True(c.ArgAs<float>(2) == 256f, "the end radius, which is a radius and not a width");
        Assert.True(c.ArgAs<int>(3)    == ModelIndex && c.ArgAs<int>(4) == HaloIndex, "sprites");
        Assert.True(c.ArgAs<float>(5)  == 0f, "halo scale");
        Assert.True(c.ArgAs<float>(6)  == Life && c.ArgAs<float>(7) == Width
                 && c.ArgAs<float>(8)  == EndWidth && c.ArgAs<float>(9) == FadeLength
                 && c.ArgAs<float>(10) == Amplitude, "the five floats in a row");
        Assert.True(c.ArgAs<float>(11)   == Alpha, "brightness");
        Assert.True(c.ArgAs<float>(12) == Tenth(Speed), "speed");
        Assert.True(c.ArgAs<int>(13)   == StartFrame, "start frame");
        Assert.True(c.ArgAs<float>(14) == Tenth(FrameRate), "frame rate");
        Assert.True(c.ArgAs<float>(15)   == Red && c.ArgAs<float>(16) == Green
                 && c.ArgAs<float>(17)   == Blue, "colour");
        Assert.True(c.ArgAs<int>(18)   == Flags, "and the flags, as the other ring does");
    }

    /// <summary>
    /// The odd one out, and the reason this file asserts by position rather than
    /// by eye: <c>CreateBeamFollow</c> takes <c>r, g, b</c> BEFORE brightness,
    /// and takes no speed, start frame, frame rate or amplitude at all. A reader
    /// tidying the seven players into one shape produces a beam whose colour is
    /// its alpha, and every value involved is a plausible 0..255.
    /// </summary>
    [Fact]
    public void BeamFollowTakesColourBeforeBrightnessAndDropsFourFields()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType te = registry.Find("CTEBeamFollow")!;

        byte[] blob = BlobFor(te, out TempEntWriter writer);
        writer.Int("EntIndex", (9 & 0x0FFF) | (1 << 12));

        Players(registry, engine).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Beams.CreateBeamFollow") == 1, "one trail was created");

        EngineCall c = engine.Last("Beams.CreateBeamFollow")!.Value;

        Assert.True(c.ArgAs<int>(0) == ((9 & 0x0FFF) | (1 << 12)),
            "the entity it follows, read from m_iEntIndex — stock's one odd-man-out field "
          + "name in the lane, where the siblings say m_nStartEntity");
        Assert.True(c.ArgAs<int>(1)   == ModelIndex && c.ArgAs<int>(2) == HaloIndex, "sprites");
        Assert.True(c.ArgAs<float>(3) == 0f, "halo scale");
        Assert.True(c.ArgAs<float>(4) == Life, "life");
        Assert.True(c.ArgAs<float>(5) == Width, "width");
        Assert.True(c.ArgAs<float>(6) == EndWidth, "end width");
        Assert.True(c.ArgAs<float>(7) == FadeLength, "fade length");

        // THE ORDER THAT IS NOT ITS SIBLINGS'.
        Assert.True(c.ArgAs<float>(8)  == Red, "then r");
        Assert.True(c.ArgAs<float>(9)  == Green, "then g");
        Assert.True(c.ArgAs<float>(10) == Blue, "then b");
        Assert.True(c.ArgAs<float>(11) == Alpha,
            "and brightness LAST, where every sibling takes it in the middle");
        Assert.True(c.ArgAs<float>(11) != Blue,
            "which is only a real check because brightness and blue are different numbers here");
    }

    /// <summary>
    /// The four fields <c>CreateBeamFollow</c> has no parameter for really are
    /// absent, and are not quietly folded into a neighbouring argument. Asserted
    /// by count: the primitive takes twelve arguments and the base carries
    /// fifteen fields, so four of them plus the flags have nowhere to go.
    /// </summary>
    [Fact]
    public void BeamFollowHasNowhereToPutSpeedFrameRateStartFrameAmplitudeOrFlags()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        TempEntType te = registry.Find("CTEBeamFollow")!;

        byte[] blob = BlobFor(te, out TempEntWriter writer);
        writer.Int("EntIndex", 4);

        Players(registry, engine).Dispatch(te.Handle, blob);

        EngineCall c = engine.Last("Beams.CreateBeamFollow")!.Value;

        Assert.True(c.Args.Length == 12,
            "CreateBeamFollow takes twelve arguments — no speed, no start frame, no frame "
          + "rate, no amplitude and no flags");

        // And none of the five that have no home leaked into one that does. All
        // five values are distinct from every argument's expected value, so this
        // would catch any of them arriving anywhere.
        for (int i = 0; i < c.Args.Length; i++)
        {
            object? arg = c.Arg(i);

            if (arg is int value)
            {
                Assert.True(value != Speed && value != FrameRate
                         && value != StartFrame && value != Flags,
                    $"argument {i} is not m_nSpeed, m_nFrameRate, m_nStartFrame or m_nFlags");
            }

            if (arg is float number)
            {
                Assert.True(number != Amplitude && number != Tenth(Speed)
                         && number != Tenth(FrameRate),
                    $"argument {i} is not m_fAmplitude or a scaled rate");
            }
        }
    }

    // =====================================================================
    // DISCOVERY, and the C8 refusal that is deliberately still reached.
    // =====================================================================

    /// <summary>
    /// All seven are found by attribute over the client assembly — no list
    /// file, which is what let this lane add eight files and touch nothing
    /// anybody else has.
    /// </summary>
    [Fact]
    public void AllSevenPlayersAreFoundByAttribute()
    {
        var engine = Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine));

        ServiceContainer services = Services();
        services.AddInstance<IClientBeams>(new RecordingBeams(engine));
        players.RegisterAll(typeof(ClientTeBeamPoints).Assembly, services);

        foreach (string name in new[]
                 {
                     "CTEBeamPoints", "CTEBeamEnts", "CTEBeamEntPoint", "CTEBeamLaser",
                     "CTEBeamRing", "CTEBeamRingPoint", "CTEBeamFollow",
                 })
        {
            Assert.True(players.PlayerNames.Contains(name),
                $"{name} is registered under the name the two sides pair on");
        }

        // And the base is NOT among them. c_te_basebeam.cpp's PostDataUpdate is
        // Assert( 0 ): a base is never sent, so a player for it would be code
        // nothing can reach. One arriving is counted as dropped and named once,
        // which is that assert in a build where asserts are compiled out.
        Assert.True(!players.PlayerNames.Contains("CTEBaseBeam"),
            "and CTEBaseBeam has none, because a base table is never sent");
    }

    /// <summary>
    /// The four entity-taking beams still CALL their backend — behaviour ledger
    /// row C8, which is now CLOSED: <c>view_beams.cpp</c> resolves an endpoint
    /// through <c>cl_entitylist</c>, which answers for both hierarchies, rather
    /// than through the unfiltered <c>CBaseHandle::Get()</c> that returned a
    /// managed entity reinterpreted as a <c>C_BaseEntity *</c> and killed the
    /// client on its first virtual call. The native slots no longer refuse.
    ///
    /// <para>
    /// THIS ASSERTION DID NOT CHANGE WHEN THE ROW DID, and that is the point.
    /// It never asserted the refusal — it asserted the CALL, which is the thing
    /// a lane is tempted to delete to look finished. It kept the row honest
    /// while the row was open, and it now keeps the closed capability reachable:
    /// a player that stopped calling its slot would be a beam that silently
    /// stopped being drawn, and every other test in this file would still pass,
    /// because they assert what the arguments are and not that the call happens
    /// at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFourBeamsRowC8ClosedStillReachTheirBackend()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);
        ClientTempEnts players = Players(registry, engine);

        (string Class, string Slot, string Field)[] entityTaking =
        {
            ("CTEBeamEnts",     "Beams.CreateBeamEnts",     "StartEntity"),
            ("CTEBeamLaser",    "Beams.CreateBeamEnts",     "StartEntity"),
            ("CTEBeamEntPoint", "Beams.CreateBeamEntPoint", "StartEntity"),
            ("CTEBeamRing",     "Beams.CreateBeamRing",     "StartEntity"),
            ("CTEBeamFollow",   "Beams.CreateBeamFollow",   "EntIndex"),
        };

        foreach ((string className, string slot, string field) in entityTaking)
        {
            TempEntType te = registry.Find(className)!;
            int before = engine.CountOf(slot);

            byte[] blob = BlobFor(te, out TempEntWriter writer);

            // A real entity index, which is exactly the case the backend
            // refuses when the entity is managed.
            writer.Int(field, (12 & 0x0FFF) | (1 << 12));

            players.Dispatch(te.Handle, blob);

            Assert.True(engine.CountOf(slot) == before + 1,
                $"{className} still calls {slot} with an entity endpoint — row C8 is closed "
              + "by the endpoint resolving, not by this call going away");
        }
    }

    // =====================================================================
    // RETIRED BY plan_shim.md PHASE 1f: EveryFieldTheNativeForwardsWriteIsAFieldTheDeclarationHas
    //
    // WHAT IT CLAIMED. shell_te_fwd_ca.cpp wrote this lane's seven beam blobs
    // by string, and this fact read those strings off disk because a
    // misspelled field name there is a runtime warning and a default rather
    // than a compile error.
    //
    // WHY IT IS NO LONGER A CLAIM ABOUT THIS BUILD. There is no client-side
    // native forward for these classes any more. All 52 client TE_* entry
    // points were reached only through c_te.cpp's C_TempEntsSystem vtable, and
    // the surviving `te->` call sites on the client link line are three —
    // te->Bubbles and te->Smoke in cdll_util.cpp and te->DispatchEffect in
    // c_te_effect_dispatch.cpp. c_te.cpp is out of client_csharp.vpc and the
    // nine shell_te_fwd_c*.cpp are deleted; what replaced them,
    // shell_te_forwards_client.cpp, carries TE_Bubbles and TE_Smoke and nothing
    // else. A fact that reads a file that is not there does not fail — it takes
    // its not-found arm and passes forever (SourceFileNamesExistTests exists
    // because of exactly that), so retiring it beats repointing it at a file
    // that cannot contain what it looks for.
    //
    // WHAT STILL CHECKS THE SURVIVORS. TempEntShimKeyTests and
    // TempEntDeclarationWriterTests sweep BOTH sides' forwarder files for the
    // same field-name agreement. Nothing
    // about the managed declarations, the players or the send path changed, and
    // the facts in this file that test those are untouched.
    // =====================================================================

    // =====================================================================
    // RETIRED BY plan_shim.md PHASE 1f: TheNativeForwardsAreExactlyTheLanesSevenClasses
    //
    // WHAT IT CLAIMED. shell_te_fwd_ca.cpp forwarded exactly this lane's seven
    // classes and no others, which is what kept the file and the declarations
    // from drifting apart.
    //
    // WHY IT IS NO LONGER A CLAIM ABOUT THIS BUILD. There is no client-side
    // native forward for these classes any more. All 52 client TE_* entry
    // points were reached only through c_te.cpp's C_TempEntsSystem vtable, and
    // the surviving `te->` call sites on the client link line are three —
    // te->Bubbles and te->Smoke in cdll_util.cpp and te->DispatchEffect in
    // c_te_effect_dispatch.cpp. c_te.cpp is out of client_csharp.vpc and the
    // nine shell_te_fwd_c*.cpp are deleted; what replaced them,
    // shell_te_forwards_client.cpp, carries TE_Bubbles and TE_Smoke and nothing
    // else. A fact that reads a file that is not there does not fail — it takes
    // its not-found arm and passes forever (SourceFileNamesExistTests exists
    // because of exactly that), so retiring it beats repointing it at a file
    // that cannot contain what it looks for.
    //
    // WHAT STILL CHECKS THE SURVIVORS. TempEntShimKeyTests and
    // TempEntDeclarationWriterTests sweep BOTH sides' forwarder files for the
    // same field-name agreement. Nothing
    // about the managed declarations, the players or the send path changed, and
    // the facts in this file that test those are untouched.
    // =====================================================================


    /// <summary>Every <c>SourceSharpTe_Set*</c> field name in a stretch of C++.</summary>
    private static string[] FieldsWritten(string body)
        => Regex.Matches(body, @"SourceSharpTe_Set(?:Int|Float|Vector)\(\s*""(?<name>\w+)""")
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The body of a C++ function, from its signature to the matching brace.</summary>
    private static string Body(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);

        if (at < 0)
            return string.Empty;

        int open = source.IndexOf('{', at);

        if (open < 0)
            return string.Empty;

        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..i];
        }

        return string.Empty;
    }
}
