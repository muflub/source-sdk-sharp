using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Lane CN's client halves: <c>CTEMetalSparks</c>, <c>CTEArmorRicochet</c> and
/// <c>CTEBeamSpline</c>, received in C#.
///
/// <para>
/// <b>WHAT THESE ARE FOR, given that the server half of all three is already
/// pinned elsewhere.</b> <see cref="TempEntGoreTests"/> and the odds tests
/// check the SCHEMA against stock's own <c>.cpp</c> read off disk, which is the
/// only check that can catch a wrong-but-consistent width (docs/te-lane-contract
/// "the three things a green dump cannot tell you"). Nothing there says the
/// client DECODES the blob at those offsets or that it reaches the right
/// drawing primitive, and those are two different failures with one symptom: an
/// effect that does not appear. So what is asserted below is the values
/// arriving at the backend, not that some bytes survived.
/// </para>
///
/// <para>
/// <b>THE PAIR IS THE POINT OF HALF THIS FILE.</b> <c>DT_TEMetalSparks</c> is
/// the only table in all forty-four that is both a concrete effect and somebody
/// else's base, its one user is <c>CTEArmorRicochet</c>, and both classes live
/// in one file named for the second of them. The client resolves a nested data
/// table BY NAME and cannot hold two of one (plan 2E-bis), so the two had to
/// move together — and the two effects differ only in a SOUND, which is exactly
/// the kind of difference a "simplification" deletes without anything going
/// red. <see cref="TheRicochetAndTheMetalSparksAreTwoDifferentBackends"/> is
/// the test that would notice.
/// </para>
/// </summary>
public class TempEntClientCnTests
{
    /// <summary>
    /// Component-wise equality. <see cref="Vector"/> declares no <c>==</c>, and
    /// EXACT is right here: nothing on this path encodes, so a byte that moved
    /// is a byte that moved. The COORD rounding is the engine's and none of
    /// these tests reach it.
    /// </summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// A registry over the real shared declarations, with native answering a
    /// distinct handle per registration as it really does.
    /// </summary>
    private static TempEntRegistry Registered(RecordingEngine engine, int handle = 5)
    {
        int next = handle;

        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        return registry;
    }

    /// <summary>Packs one class's blob through the same writer the server uses.</summary>
    private static byte[] Blob(TempEntType type, Action<TempEntWriter> fill)
    {
        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        fill(writer);
        return blob;
    }

    // ---------------------------------------------------------------------
    // CTEMetalSparks.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheMetalSparksReachTheDrawingPrimitiveWithWhatTheBlobCarried()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType sparks = registry.Find("CTEMetalSparks")!;

        byte[] blob = Blob(sparks, w =>
        {
            w.Vector("Position", new Vector(-2932f, 3218f, 60f));
            w.Vector("Direction", new Vector(0f, 0f, 1f));
        });

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEMetalSparks", new ClientTeMetalSparks(engine));

        players.Dispatch(sparks.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientMetalSparks") == 1, "one metal spark was drawn");

        var call = engine.Last("Effects.ClientMetalSparks")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(-2932f, 3218f, 60f)),
            "where the wire said the round hit");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(0f, 0f, 1f)),
            "along the surface normal the wire carried");
        Assert.True(engine.CountOf("Effects.ClientRicochet") == 0,
            "and NOT through the ricochet, which is the same sparks plus a sound");
    }

    /// <summary>
    /// The field names, which are still not the names the neighbouring table in
    /// the same lane uses for the first of the two quantities.
    ///
    /// <para>
    /// STOCK SPELT THEM <c>m_vecPos</c> / <c>m_vecDir</c> HERE AND
    /// <c>m_vecOrigin</c> / <c>m_vecDirection</c> IN <c>DT_TEBloodSprite</c>,
    /// and the port kept both spellings because the two sides paired on the
    /// string. Both declarations have since dropped their wire names, so the
    /// props are called after their C# members — and the members kept the
    /// distinction: <c>Position</c> here, <c>Origin</c> there. A reader that
    /// used the other name would log a warning and draw at the world origin,
    /// which reads as a bug in <c>FX_MetalSpark</c> rather than in the port, so
    /// both halves are still asserted.
    /// </para>
    ///
    /// <para>
    /// The DIRECTION is now the same word on both, which is a real change and is
    /// harmless for the reason the whole rename is: a prop resolves inside ONE
    /// class's field map, and the class handle is what tells a spline's
    /// <c>Direction</c> from a blood sprite's. The offset assertions below are
    /// what would catch the two being confused.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMetalSparksPositionIsSpeltPositionAndNotOrigin()
    {
        var engine = Fake();
        TempEntType sparks = Registered(engine).Find("CTEMetalSparks")!;

        Assert.True(sparks.Fields.ContainsKey("Position"),
            "DT_TEMetalSparks carries Position, which is stock's m_vecPos");
        Assert.True(sparks.Fields.ContainsKey("Direction"), "and Direction, stock's m_vecDir");
        Assert.True(!sparks.Fields.ContainsKey("Origin"),
            "and NOT Origin, which is DT_TEBloodSprite's member for the same quantity — the "
          + "distinction stock drew with m_vecPos against m_vecOrigin survived into the members");
        Assert.True(!sparks.Fields.ContainsKey("m_vecPos"),
            "and neither stock spelling is still addressable, which is what a half-done rename "
          + "would have left behind");

        Assert.True(sparks.Fields["Position"].Offset == 0, "the position is at the front of the blob");
        Assert.True(sparks.Fields["Direction"].Offset == 12, "and the normal follows it");
        Assert.True(sparks.BlobBytes == 24, "which is the whole of a twenty-four byte NOBASE blob");
    }

    // ---------------------------------------------------------------------
    // CTEArmorRicochet — the chained base.
    // ---------------------------------------------------------------------

    /// <summary>
    /// <c>DT_TEArmorRicochet</c> declares no props of its own, and the two it
    /// reads belong to the table it nests. A base nested at datatable offset
    /// zero puts its props at their own offsets in the SAME object, so the
    /// derived class's blob IS the base's blob here.
    /// </summary>
    [Fact]
    public void TheRicochetReadsTheBasesFieldsOutOfItsOwnBlob()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType ricochet = registry.Find("CTEArmorRicochet")!;
        TempEntType sparks   = registry.Find("CTEMetalSparks")!;

        Assert.True(ricochet.Class.Fields.Length == 0,
            "DT_TEArmorRicochet declares no props of its own, as stock's empty table does");
        Assert.True(ricochet.Fields.ContainsKey("Position"),
            "and can still address Position, because the nested table's props are in this object "
          + "— and, since TeArmorRicochet : TeMetalSparks, because it inherits the member too");
        Assert.True(ricochet.Fields["Position"].Offset == sparks.Fields["Position"].Offset,
            "at the offset DT_TEMetalSparks' own props read from");
        Assert.True(ricochet.Fields["Direction"].Offset == sparks.Fields["Direction"].Offset,
            "and so does the direction");
        Assert.True(ricochet.BlobBytes == sparks.BlobBytes,
            "so the two blobs are the same size: everything on this wire is the base's");

        byte[] blob = Blob(ricochet, w =>
        {
            w.Vector("Position", new Vector(128f, -64f, 16f));
            w.Vector("Direction", new Vector(0f, 1f, 0f));
        });

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEArmorRicochet", new ClientTeArmorRicochet(engine));

        players.Dispatch(ricochet.Handle, blob);

        var call = engine.Last("Effects.ClientRicochet")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(128f, -64f, 16f)),
            "the ricochet draws where the base's field said");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(0f, 1f, 0f)), "along the base's normal");
    }

    /// <summary>
    /// THE TEST THAT WOULD NOTICE A TIDY-UP. The two tables are prop for prop
    /// identical, so the only thing that distinguishes them is which backend
    /// each ends at — <c>g_pEffects->Ricochet</c> is metal sparks AND the
    /// ricochet sound, <c>g_pEffects->MetalSparks</c> is the sparks alone.
    /// Routing both names at one backend would compile, would draw sparks for
    /// both, and would silently delete a sound.
    /// </summary>
    [Fact]
    public void TheRicochetAndTheMetalSparksAreTwoDifferentBackends()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType ricochet = registry.Find("CTEArmorRicochet")!;
        TempEntType sparks   = registry.Find("CTEMetalSparks")!;

        Assert.True(ricochet.Handle != sparks.Handle,
            "the two register as two classes under two handles, as stock's two names do");

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEArmorRicochet", new ClientTeArmorRicochet(engine));
        players.Add("CTEMetalSparks", new ClientTeMetalSparks(engine));

        players.Dispatch(ricochet.Handle, new byte[ricochet.BlobBytes]);

        Assert.True(engine.CountOf("Effects.ClientRicochet") == 1,
            "a CTEArmorRicochet is one ricochet");
        Assert.True(engine.CountOf("Effects.ClientMetalSparks") == 0, "and no bare metal spark");

        players.Dispatch(sparks.Handle, new byte[sparks.BlobBytes]);

        Assert.True(engine.CountOf("Effects.ClientMetalSparks") == 1,
            "a CTEMetalSparks is one bare metal spark");
        Assert.True(engine.CountOf("Effects.ClientRicochet") == 1,
            "and does not also ring, which is the whole difference between the two");
    }

    // ---------------------------------------------------------------------
    // CTEBeamSpline — the only array, and the one that draws nothing.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The array pair, as plan 3.3 describes it: ONE description field standing
    /// for the element template and the <c>DPT_Array</c> prop that stock's
    /// <c>SendPropArray</c> macro emits, both named <c>m_vecPoints</c>.
    ///
    /// <para>
    /// The two-props half is pinned natively (<c>tempent_plan_test.cpp</c>,
    /// which reads the plan the builders walk). What is checkable here is the
    /// half the plan is built FROM: the element count, the stride, and that the
    /// field measures ONE ELEMENT rather than the whole run — the number
    /// <c>SendPropVector</c> asserts against <c>sizeof(Vector)</c> on a debug
    /// build and says nothing about on a release one.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSplinesArrayIsSixteenTwelveByteElementsInOneField()
    {
        var engine = Fake();
        TempEntType spline = Registered(engine).Find("CTEBeamSpline")!;

        TeField points = spline.Fields["Spline"];

        Assert.True(points.Kind == TePropKind.Array,
            "Spline — stock's m_vecPoints — is the array kind, which is the ONE kind in this "
          + "declaration still written out by hand: inference reads the member's type and a "
          + "struct of sixteen Vectors cannot say whether it is an array or a vector");
        Assert.True(points.Elems == 16, "sixteen elements, which is MAX_SPLINE_POINTS");
        Assert.True(points.Elems == ClientTeBeamSpline.MaxSplinePoints,
            "and the player reads exactly that many, from the same constant");
        Assert.True(points.ElemStride == 12, "twelve bytes apart, which is the DPT_Array stride");
        Assert.True(points.SizeBytes == 12,
            "and the field measures ONE element, because that is what the element "
            + "template is handed as sizeofVar");
        Assert.True(points.Bits == -1, "with the COORD flag deciding the element width");
        Assert.True(points.Flags == TeFlags.Coord, "COORD, as SENDINFO_ARRAY hands the template");

        TeField count = spline.Fields["Points"];

        Assert.True(count.Offset < points.Offset,
            "Points comes first, because declaration order is wire order");
        Assert.True(count.Bits == 5, "five bits, which is stock's literal");
        Assert.True(count.Flags == TeFlags.Unsigned, "unsigned, so 0..31");

        Assert.True(points.Offset + points.Elems * points.ElemStride <= spline.BlobBytes,
            "and the whole run fits inside the blob it is addressed in");
    }

    /// <summary>
    /// All sixteen points come back, at the stride, INCLUDING THE LAST — which
    /// is where a stride error hides.
    ///
    /// <para>
    /// With a NULL length proxy every element is sent whatever
    /// <c>m_nPoints</c> says (plan 3.3), so a decode that stopped at the count
    /// would never look at the far end of the array. Each point is given a
    /// value derived from its index so that an off-by-one stride reads a
    /// neighbour rather than a plausible zero.
    /// </para>
    /// </summary>
    [Fact]
    public void EverySplinePointComesBackAtItsOwnStrideAndNotItsNeighbours()
    {
        var engine = Fake();
        engine.Returns("Console.FindConVarValue", "0");

        var registry = Registered(engine);
        TempEntType spline = registry.Find("CTEBeamSpline")!;

        var blob = new byte[spline.BlobBytes];
        var writer = new TempEntWriter(spline, blob);

        writer.Int("Points", 6);

        for (int i = 0; i < ClientTeBeamSpline.MaxSplinePoints; i++)
            writer.VectorAt("Spline", i, new Vector(i, i * 10f, i * 100f));

        var player = new ClientTeBeamSpline(engine);
        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEBeamSpline", player);

        players.Dispatch(spline.Handle, blob);

        Assert.True(player.PointCount == 6, "Points came back as the six the sender meant");
        Assert.True(player.Points.Length == 16,
            "and all sixteen elements were read, not the six the count named");

        for (int i = 0; i < ClientTeBeamSpline.MaxSplinePoints; i++)
        {
            Assert.True(Same(player.Points[i], new Vector(i, i * 10f, i * 100f)),
                $"point {i} came back from its own twelve bytes and not the next element's");
        }
    }

    /// <summary>
    /// <b>STOCK DRAWS NOTHING FOR A BEAM SPLINE, AND THIS IS THE TEST THAT SAYS
    /// SO OUT LOUD.</b>
    ///
    /// <para>
    /// The whole of <c>C_TEBeamSpline::PostDataUpdate</c> is a <c>VPROF</c> and
    /// a <c>DevMsg</c>; there is no <c>CreateBeam*</c>, no particle spawn and no
    /// <c>g_pEffects</c> call in <c>c_te_beamspline.cpp</c> at all. So the
    /// managed player must reach NO backend — and that has to be asserted
    /// rather than left as an absence, because "draws nothing" and "was never
    /// written" are the same silence otherwise, and a later reader adding a
    /// beam here would be adding behaviour stock does not have.
    /// </para>
    ///
    /// <para>
    /// The engine double is strict, so any backend this player reached would be
    /// an error naming it. What is asserted on top of that is the pair that
    /// tells a no-op apart from a hole: the effect COUNTED AS PLAYED and
    /// nothing was dropped.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSplineDrawsNothingBecauseStockDrawsNothingAndStillCountsAsPlayed()
    {
        var engine = Fake();
        engine.Returns("Console.FindConVarValue", "0");

        var registry = Registered(engine);
        TempEntType spline = registry.Find("CTEBeamSpline")!;

        var player = new ClientTeBeamSpline(engine);
        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEBeamSpline", player);

        players.Dispatch(spline.Handle, new byte[spline.BlobBytes]);

        Assert.True(players.Played == 1,
            "the spline played -- it registered, paired, arrived and decoded");
        Assert.True(players.Dropped == 0,
            "and was NOT dropped, which is what an unported class looks like");
        Assert.True(player.Received == 1, "and the player counted it");

        Assert.True(engine.CountOf("Effects.BeamCreatePoints") == 0,
            "and no beam was created, because stock creates none");
        Assert.True(engine.CountOf("Effects.ClientSparks") == 0, "and nothing else was drawn either");

        // The last blob survives the play, so cs_cl_te_report can read the
        // sixteen points back through the same declaration. That is the only
        // evidence a class that draws nothing can give a live join.
        Assert.True(players.LastPlayed is not null && players.LastPlayed.Class.ClassName == "CTEBeamSpline",
            "and the arrival is on the record for cs_cl_te_report to describe");
    }

    /// <summary>
    /// Stock's one line, at stock's own level. <c>DevMsg</c>'s first argument is
    /// a developer LEVEL, so at <c>developer 0</c> the class is silent — which
    /// is what a console comparing this build against stock's has to see.
    /// </summary>
    [Fact]
    public void TheSplineSaysStocksSentenceOnlyAtStocksDeveloperLevel()
    {
        var engine = Fake();
        var registry = Registered(engine);
        TempEntType spline = registry.Find("CTEBeamSpline")!;

        byte[] blob = Blob(spline, w => w.Int("Points", 6));

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEBeamSpline", new ClientTeBeamSpline(engine));

        engine.Returns("Console.FindConVarValue", "0");
        players.Dispatch(spline.Handle, blob);

        Assert.True(engine.CountOf("Console.Msg") == 0,
            "at developer 0 a beam spline says nothing, as DevMsg( 1, ... ) says nothing");

        engine.Returns("Console.FindConVarValue", "1");
        players.Dispatch(spline.Handle, blob);

        Assert.True(engine.CountOf("Console.Msg") == 1, "at developer 1 it says its one line");
        Assert.True(engine.Last("Console.Msg")!.Value.ArgAs<string>(0)
                    == "Beam spline with 6 points received\n",
            "spelt exactly as c_te_beamspline.cpp spells it, count included");
    }

    // ---------------------------------------------------------------------
    // Discovery and the round trip.
    // ---------------------------------------------------------------------

    /// <summary>
    /// All three are found by attribute over the client assembly — no list file,
    /// which is what let this lane add three files and touch nothing another
    /// lane has.
    /// </summary>
    [Fact]
    public void AllThreeOfThisLanesPlayersAreDiscoveredByAttribute()
    {
        var engine = Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine));

        players.RegisterAll(typeof(ClientTeBeamSpline).Assembly, Services());

        foreach (string name in new[] { "CTEMetalSparks", "CTEArmorRicochet", "CTEBeamSpline" })
        {
            Assert.True(players.PlayerNames.Contains(name),
                $"{name} is registered under the name the two sides pair on");
        }
    }

    // ---------------------------------------------------------------------
    // The native forward against the managed player -- TWO ARTEFACTS, NOT ONE.
    // ---------------------------------------------------------------------

    // =====================================================================
    // RETIRED BY plan_shim.md PHASE 1f: TheNativeForwardWritesTheFieldNamesTheManagedPlayersRead
    //
    // WHAT IT CLAIMED. shell_te_fwd_cn.cpp forwarded CTEMetalSparks and
    // CTEArmorRicochet and wrote the member names the managed players read,
    // which a Q_strcmp field lookup gets no compiler help with.
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
    // same field-name agreement, and TeForwardSignatureTests pins each
    // surviving forward's packing against the arm that unpacks it. Nothing
    // about the managed declarations, the players or the send path changed, and
    // the facts in this file that test those are untouched.
    // =====================================================================
}
