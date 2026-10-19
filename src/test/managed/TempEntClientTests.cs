extern alias client;
// The client's copies of the shared interop types this file hands to client
// code; each side compiles its own (plan_shim_areas_split phase 3).
using RecordingEngine = client::SourceSharp.Abi.RecordingEngine;
using RecordingLog = client::SourceSharp.Abi.RecordingLog;
using TempEntRegistry = client::SourceSharp.Abi.TempEntRegistry;
using TempEntType = client::SourceSharp.Abi.TempEntType;
using TempEntWriter = client::SourceSharp.Abi.TempEntWriter;
using TempEntReader = client::SourceSharp.Abi.TempEntReader;
using TempEntState = client::SourceSharp.Abi.TempEntState;
using TeField = client::SourceSharp.Abi.TeField;
using TeFlags = client::SourceSharp.Abi.TeFlags;
using TePropKind = client::SourceSharp.Abi.TePropKind;
using ServiceContainer = client::SourceSharp.Abi.ServiceContainer;
using TeSparks = client::SourceSharp.Shared.TeSparks;
using TeEnergySplash = client::SourceSharp.Shared.TeEnergySplash;
using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// C-Phase 1's receiving half: the blob comes back out the way it went in, the
/// handle native answered resolves to a declaration, and CTESparks reaches the
/// drawing primitive with the numbers the wire carried.
///
/// <para>
/// THE ROUND TRIP IS THE POINT AND IT IS NOT A TAUTOLOGY. A writer and a reader
/// that agreed by construction would be one of the checks
/// <c>docs/writing-tests.md</c> calls "a buffer compared with itself" — so what
/// these assert is not "what I wrote is what I read" alone, but that the bytes
/// land at the offsets <see cref="TeClassBuilder"/> computed from the shared
/// declaration, including the base chain's, and that a field name nothing
/// declares reads a default rather than a plausible number out of a neighbour.
/// The mutation proof (plan gate 4) points at the DECLARATION, so a bit width
/// changed in <c>TeSparks.cs</c> reds
/// <see cref="TheDeclarationIsWhatBothSidesBuildFrom"/>.
/// </para>
///
/// <para>
/// No test here needs a running engine, because nothing on this path does: the
/// blob is bytes, the layout is reflection, and the only engine call is the one
/// at the very end that draws.
/// </para>
/// </summary>
public class TempEntClientTests
{
    /// <summary>
    /// Component-wise equality. <see cref="Vector"/> declares no <c>==</c>, and
    /// EXACT rather than approximate is right here: nothing on this path
    /// encodes, so a byte that moved is a byte that moved. The COORD rounding
    /// happens in the engine, which none of these tests reach.
    /// </summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// A registry over the real shared declarations, with native answering
    /// <paramref name="handle"/> to every registration.
    /// </summary>
    /// <summary>
    /// The server's writer, over the server's own double and a registry of the
    /// server's copy of the shared declarations. Each side compiles the shared
    /// interop (plan_shim_areas_split phase 3), so a sender and a receiver are
    /// two sides joined only by the blob, as in the game.
    /// </summary>
    private static (SourceSharp.Server.TempEnts Temps, global::SourceSharp.Abi.RecordingEngine Engine,
                    global::SourceSharp.Abi.TempEntRegistry Registry) ServerWriter()
    {
        var engine = TestServices.Fake();
        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);
        var registry = new global::SourceSharp.Abi.TempEntRegistry(new global::SourceSharp.Abi.RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);
        return (new SourceSharp.Server.TempEnts(new global::SourceSharp.Abi.RecordingLog(), registry, engine), engine, registry);
    }

    private static TempEntRegistry Registered(RecordingEngine engine, int handle = 5)
    {
        // A DISTINCT handle per registration, as native gives out, starting at
        // `handle`. A constant was fine while three temp entities existed; with
        // forty-four every class held the same number, so FindByHandle answered
        // whichever had been published last and the tests below were really
        // asserting registration order.
        //
        // A NEGATIVE `handle` is native REFUSING, which is not a handle and does
        // not count up: every registration gets the same refusal. Counting from
        // -1 would refuse the first declaration and hand real handles to the
        // rest, which is not a state native can produce.
        int next = handle;

        engine.ReturnsEachCall("Effects.TempEntRegister",
            handle < 0 ? () => handle : () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        return registry;
    }

    // ---------------------------------------------------------------------
    // The blob, both ways.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheReaderFindsWhatTheWriterPutThere()
    {
        var engine = ClientDoubles.Fake();
        TempEntType sparks = Registered(engine).Find("CTESparks")!;

        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);

        writer.Vector("Origin", new Vector(64f, -32f, 8.5f));
        writer.Int("Magnitude", 6);
        writer.Int("TrailLength", 3);
        writer.Vector("Direction", new Vector(0f, 0f, 1f));

        var reader = new TempEntReader(sparks, blob);

        Assert.True(Same(reader.Vector("Origin"), new Vector(64f, -32f, 8.5f)),
            "the sort origin comes back where it was written");
        Assert.True(reader.UInt("Magnitude") == 6, "and the magnitude");
        Assert.True(reader.UInt("TrailLength") == 3, "and the trail length");
        Assert.True(Same(reader.Vector("Direction"), new Vector(0f, 0f, 1f)), "and the direction");
    }

    /// <summary>
    /// <c>Origin</c> belongs to <c>DT_TEParticleSystem</c> and is read out
    /// of <c>CTESparks</c>' blob anyway — because a base nested at datatable
    /// offset zero puts its props at their own offsets in the SAME object. A
    /// reader that only knew its own class's fields would answer zero for the
    /// place every spark happens.
    /// </summary>
    [Fact]
    public void TheBasesFieldsAreInTheDerivedClassesBlob()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine);

        TempEntType sparks = registry.Find("CTESparks")!;
        TempEntType particles = registry.Find("CTEParticleSystem")!;

        Assert.True(sparks.Fields.ContainsKey("Origin"),
            "CTESparks can address DT_TEParticleSystem's origin");
        Assert.True(!sparks.Class.Fields.Any(f => f.Name == "Origin"),
            "even though DT_TESparks does not declare it");

        // And at the SAME offset the base itself uses, which is what "nested at
        // offset zero" means. A base whose fields moved would put the derived
        // class's first field where the base's last prop reads.
        Assert.True(sparks.Fields["Origin"].Offset == particles.Fields["Origin"].Offset,
            "at the offset the base's own props read from");
        Assert.True(sparks.Fields["Origin"].Offset == 0,
            "which is the front of the blob");
    }

    /// <summary>
    /// A name nothing declares reads its default and does not read a
    /// neighbour's bytes. Both halves matter: the second is what makes a
    /// misspelling a missing effect rather than an effect somewhere else.
    /// </summary>
    [Fact]
    public void AFieldNothingDeclaresReadsItsDefaultAndNotTheNextFieldsBytes()
    {
        var engine = ClientDoubles.Fake();
        TempEntType sparks = Registered(engine).Find("CTESparks")!;

        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);
        writer.Int("Magnitude", 9);

        var reader = new TempEntReader(sparks, blob);

        Assert.True(reader.UInt("Magnitude") == 9, "the real field reads what was written");
        Assert.True(reader.UInt("Magnitudee", 4242) == 4242,
            "a misspelled one reads the fallback the caller chose");
        Assert.True(Same(reader.Vector("m_vecNoSuchThing"), Vector.Zero),
            "and a misspelled vector is the zero vector, not a neighbour's floats");
    }

    /// <summary>
    /// A blob shorter than the declaration reads defaults rather than past its
    /// end. This is the shape of a managed build newer than the native one it
    /// is loaded into — the field was added here and the shell allocates the
    /// size native knows.
    /// </summary>
    [Fact]
    public void AShortBlobReadsDefaultsRatherThanPastItsEnd()
    {
        var engine = ClientDoubles.Fake();
        TempEntType sparks = Registered(engine).Find("CTESparks")!;

        var full = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, full);
        writer.Vector("Origin", new Vector(1f, 2f, 3f));
        writer.Int("Magnitude", 7);
        writer.Vector("Direction", new Vector(0f, 1f, 0f));

        // Everything up to and including Magnitude, and nothing after it.
        var truncated = new TempEntReader(sparks, full.AsSpan(0, 16));

        Assert.True(Same(truncated.Vector("Origin"), new Vector(1f, 2f, 3f)),
            "what fits still reads");
        Assert.True(truncated.UInt("Magnitude") == 7, "including the last field that fits");
        Assert.True(Same(truncated.Vector("Direction"), Vector.Zero),
            "and the field past the end is the default rather than whatever follows the span");
    }

    /// <summary>
    /// <c>SendPropEHandle</c>'s packing, undone. The proxy that produced it is
    /// gone (plan 2B) and the writer reproduces it, so the two have to agree on
    /// the bit layout: entry index in the low
    /// <c>MAX_EDICT_BITS</c>, ten bits of serial above it.
    /// </summary>
    [Fact]
    public void AnEntityHandleSurvivesThePackingStocksProxyWouldHaveDone()
    {
        var engine = ClientDoubles.Fake();
        TempEntType sparks = Registered(engine).Find("CTESparks")!;

        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);

        // Entry 37, serial 5, as CBaseHandle packs them: entry in the low
        // sixteen bits, serial in the high sixteen.
        writer.EHandle("Magnitude", new EntityHandle(37u | (5u << 16)));

        var reader = new TempEntReader(sparks, blob);

        Assert.True(reader.EHandle("Magnitude", out int entry, out int serial),
            "a real handle resolves");
        Assert.True(entry == 37, "to the entry index the writer packed");
        Assert.True(serial == 5, "and the ten serial bits above it");

        // And the sentinel. INVALID_NETWORKED_EHANDLE_VALUE is all twenty-one
        // bits set, which is not entry 2047 serial 1023 and must not read as one.
        var none = new byte[sparks.BlobBytes];
        var w2 = new TempEntWriter(sparks, none);
        w2.EHandle("Magnitude", EntityHandle.None);

        var r2 = new TempEntReader(sparks, none);

        Assert.True(!r2.EHandle("Magnitude", out int noEntry, out _),
            "a handle that resolves to nothing says so");
        Assert.True(noEntry == -1, "and answers no index at all rather than 2047");
    }

    /// <summary>
    /// Sign follows the DECLARED storage width, not the value. The only signed
    /// narrow int in all 44 tables is <c>DT_TEDynamicLight</c>'s exponent, and
    /// it is a one-byte field whose negative values are the whole point.
    /// </summary>
    [Fact]
    public void ANarrowFieldReadsBackAtTheWidthItWasDeclaredWith()
    {
        var engine = ClientDoubles.Fake();
        TempEntType sparks = Registered(engine).Find("CTESparks")!;

        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);

        // Magnitude is a four-byte field, so -3 round-trips as -3 through
        // Int and as -3 through UInt too: the reader's UInt only differs from
        // Int at one and two bytes, where a proxy actually narrowed the value.
        writer.Int("Magnitude", -3);

        var reader = new TempEntReader(sparks, blob);

        Assert.True(reader.Int("Magnitude") == -3,
            "a four-byte field carries a negative unchanged");
        Assert.True(sparks.Fields["Magnitude"].SizeBytes == 4,
            "and it IS four bytes, which is why the two readers agree here");
    }

    // ---------------------------------------------------------------------
    // The handle, which is the client's own question.
    // ---------------------------------------------------------------------

    /// <summary>
    /// An arriving temp entity carries nothing but the handle this side issued,
    /// so the lookup has to be BY HANDLE and not by position.
    ///
    /// <para>
    /// The two are the same number today only because native issues handles in
    /// the order it accepts them; they stop being the same the moment native
    /// refuses one, which is the ordinary state of a staged port. An index into
    /// a list that skipped a refusal plays the wrong effect and nothing says so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHandleIsNotAnIndexIntoTheRegistry()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine, handle: 5);

        Assert.True(registry.Count >= 3, "three declarations were registered");
        Assert.True(registry.All[0] is not null, "and there is something at index 0");

        Assert.True(registry.FindByHandle(5) is not null,
            "the first handle native answered resolves to a declaration");
        Assert.True(registry.FindByHandle(0) is null,
            "and index 0 is not handle 0: nothing answers to a handle nobody issued");
        Assert.True(registry.FindByHandle(-1) is null,
            "nor to the refusal value");
    }

    /// <summary>
    /// A class native refused is not reachable by its refusal value. -1 is an
    /// answer, not a key.
    /// </summary>
    [Fact]
    public void ARefusedClassIsNotReachableByItsRefusal()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine, handle: -1);

        Assert.True(registry.Count >= 3, "the declarations were still discovered");
        Assert.True(registry.Find("CTESparks") is not null, "and are findable by name");
        Assert.True(!registry.Find("CTESparks")!.Registered, "but none of them registered");
        Assert.True(registry.FindByHandle(-1) is null,
            "and -1 keys nothing, so a stray callback cannot land on one");
    }

    // ---------------------------------------------------------------------
    // The dispatcher.
    // ---------------------------------------------------------------------

    private sealed class CountingPlayer : IClientTempEnt
    {
        public int Calls { get; private set; }
        public Vector LastOrigin { get; private set; }

        public void Play(in client::SourceSharp.Abi.TempEntReader te)
        {
            Calls++;
            LastOrigin = te.Vector("Origin");
        }

        /// <summary>
        /// The locally-forwarded entry. This stub stands for a player that has
        /// not been ported onto <c>ClientTempEnt&lt;T&gt;</c>, so it reads the
        /// origin off the state's own type rather than through a declaration it
        /// does not name.
        /// </summary>
        public void Play(client::SourceSharp.Abi.TempEntState state)
        {
            Calls++;

            if (state is TeSparks sparks)
                LastOrigin = sparks.Origin;
        }
    }

    [Fact]
    public void AnArrivingTempEntityReachesThePlayerRegisteredForItsName()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine, handle: 5);

        // CTESparks' OWN handle, asked for rather than assumed. The double
        // hands out a distinct one per registration, as native does, so this no
        // longer depends on which class happened to be published last.
        TempEntType sparks = registry.Find("CTESparks")!;

        Assert.True(registry.FindByHandle(sparks.Handle)!.Class.ClassName == "CTESparks",
            "CTESparks' handle resolves back to CTESparks");

        var players = new ClientTempEnts(new RecordingLog(), registry);
        var spy = new CountingPlayer();
        players.Add("CTESparks", spy);
        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);
        writer.Vector("Origin", new Vector(10f, 20f, 30f));

        players.Dispatch(sparks.Handle, blob);

        Assert.True(spy.Calls == 1, "the player ran once");
        Assert.True(Same(spy.LastOrigin, new Vector(10f, 20f, 30f)),
            "over the blob that arrived, read through the shared declaration");
        Assert.True(players.Played == 1, "and the effect counted as played");
        Assert.True(players.Dropped == 0, "with nothing dropped");
    }

    [Fact]
    public void AHandleThisSideRegisteredNothingForIsDroppedAndNotGuessed()
    {
        var engine = ClientDoubles.Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine, handle: 5));

        players.Dispatch(11, new byte[32]);
        players.Dispatch(11, new byte[32]);

        Assert.True(players.Played == 0, "nothing played");
        Assert.True(players.Dropped == 2, "and both arrivals were counted as dropped");
    }

    /// <summary>
    /// A class that registered, paired and decoded, and that nobody has written
    /// the C# for. It is a different statement from an unknown handle and has to
    /// be visible: it is the ordinary state of a C-Phase 3 class before its lane
    /// lands, and silence there looks exactly like an effect that never arrived.
    /// </summary>
    [Fact]
    public void ARegisteredClassWithNoPlayerIsDroppedRatherThanSilentlyIgnored()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine, handle: 5);
        var players = new ClientTempEnts(new RecordingLog(), registry);

        TempEntType sparks = registry.Find("CTESparks")!;

        players.Dispatch(sparks.Handle, new byte[sparks.BlobBytes]);

        Assert.True(players.Played == 0, "nothing played, because nothing plays it");
        Assert.True(players.Dropped == 1, "and the arrival is counted rather than lost");
    }

    /// <summary>
    /// Discovery is by attribute over the client assembly — no list file, which
    /// is what lets a C-Phase 3 lane add one file and touch nothing anybody else
    /// has.
    /// </summary>
    [Fact]
    public void ThePlayersAreFoundByAttributeAndTheFirstPortedOneIsAmongThem()
    {
        var engine = ClientDoubles.Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine));

        int found = players.RegisterAll(typeof(ClientTeEnergySplash).Assembly, ClientDoubles.Services());

        Assert.True(found >= 1, "at least one client temp entity player was discovered");
        Assert.True(players.PlayerNames.Contains("CTEEnergySplash"),
            "and CTEEnergySplash is registered under the name the two sides pair on");
    }

    // ---------------------------------------------------------------------
    // CTEEnergySplash, end to end on this side.
    //
    // THE FIRST TEMP ENTITY WHOSE CLIENT HALF IS MANAGED, and not the one
    // C-Phase 1 was written against. CTESparks nests DT_TEParticleSystem, and
    // the client resolves a nested table BY NAME and cannot hold two of one, so
    // a base table moves only when every native user of it is gone -- six of
    // that base's seven are still native. DT_TEEnergySplash is NOBASE and moves
    // on its own. See ClientTeEnergySplash for the whole constraint.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheSplashReachesTheDrawingPrimitiveWithWhatTheBlobCarried()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine);

        TempEntType splash = registry.Find("CTEEnergySplash")!;
        var blob = new byte[splash.BlobBytes];
        var writer = new TempEntWriter(splash, blob);

        writer.Vector("Position", new Vector(-3667f, 3439f, 60f));
        writer.Vector("Direction", new Vector(0f, 0f, 1f));
        writer.Bool("Explosive", true);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEEnergySplash", new ClientTeEnergySplash(engine));

        players.Dispatch(splash.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientEnergySplash") == 1, "one splash was drawn");

        var call = engine.Last("Effects.ClientEnergySplash")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(-3667f, 3439f, 60f)),
            "where the wire said it hit");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(0f, 0f, 1f)),
            "along the surface normal the wire carried");
        Assert.True(call.ArgAs<bool>(2), "and the explosive bit that came with them");
    }

    /// <summary>
    /// The field this class was chosen to exercise: <c>m_bExplosive</c> is ONE
    /// BIT on the wire and ONE BYTE in the blob, and it is the LAST field.
    ///
    /// <para>
    /// <c>SendPropInt</c> and <c>RecvPropInt</c> both pick their proxy from the
    /// declared storage width (<c>dt_send.cpp:492</c>, <c>dt_recv.cpp:202</c>),
    /// so a byte field described as four reads three bytes of whatever follows
    /// — which, for the last field of a blob, is past the end of it. Stock's
    /// member is a <c>CNetworkVar( bool )</c> and <c>EntityLayout</c> gives a C#
    /// <c>bool</c> one byte, so the two agree; this is what says so.
    /// </para>
    ///
    /// <para>
    /// Asserted BOTH WAYS ROUND, because a reader that answered <c>true</c>
    /// unconditionally would pass the test above.
    /// </para>
    /// </summary>
    [Fact]
    public void TheExplosiveBitIsOneByteOfBlobAndTheLastOne()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine);

        TempEntType splash = registry.Find("CTEEnergySplash")!;
        TeField explosive = splash.Fields["Explosive"];

        Assert.True(explosive.SizeBytes == 1,
            "Explosive — stock's m_bExplosive — is one byte of blob, as its "
          + "CNetworkVar( bool ) is. The declaration dropped the explicit TePropKind.Int and "
          + "the byte survived that, because the SIZE is EntityLayout's answer to the C# type "
          + "and never the attribute's");
        Assert.True(explosive.Bits == 1, "and one bit on the wire");
        // LAST IN DECLARATION ORDER, which is wire order. The exact blob size is
        // EntityLayout's business and may carry padding, so what is asserted is
        // the thing that matters: no declared field starts after this one, so
        // the three bytes a four-byte read would take belong to no field at all.
        Assert.True(splash.Fields.Values.All(f => f.Offset <= explosive.Offset),
            "Explosive is the last field, so a four-byte read of it takes bytes "
            + "no field owns");
        Assert.True(explosive.Offset == 24,
            "at 24, after two twelve-byte vectors and nothing else");

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEEnergySplash", new ClientTeEnergySplash(engine));

        foreach (bool bit in new[] { true, false })
        {
            var blob = new byte[splash.BlobBytes];
            var writer = new TempEntWriter(splash, blob);
            writer.Bool("Explosive", bit);

            players.Dispatch(splash.Handle, blob);

            Assert.True(engine.Last("Effects.ClientEnergySplash")!.Value.ArgAs<bool>(2) == bit,
                $"an explosive bit of {bit} arrives as {bit}");
        }
    }

    // ---------------------------------------------------------------------
    // The SENDER, which is in this file rather than with the server's because
    // it exists for this lane: C-Phase 1's end-to-end class needs a managed
    // caller for gate 3, and the native chain to the same table
    // (g_pEffects->EnergySplash -> te->EnergySplash -> lane D's shim) has no
    // live caller in this build -- npc_strider.cpp is the only one and it is
    // not compiled here.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheSenderPacksTheBytesTheReceiverReadsBack()
    {
        var (temps, engine, registry) = ServerWriter();

        temps.EnergySplash(TeFilters.Broadcast, 0f, new Vector(64f, -32f, 8f),
                           new Vector(0f, 0f, 1f), explosive: true);

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 1, "one temp entity was sent");

        var call = engine.Last("Effects.TempEntPlayback")!.Value;
        global::SourceSharp.Abi.TempEntType splash = registry.Find("CTEEnergySplash")!;

        Assert.True(call.ArgAs<int>(0) == splash.Handle,
            "under the handle native answered for THIS class");
        Assert.True(call.ArgAs<int>(1) == splash.BlobBytes,
            "carrying a whole DT_TEEnergySplash blob");
        Assert.True(call.ArgAs<TeFilter>(2).Kind == TeFilterKind.Broadcast, "to everyone");
        Assert.True(call.ArgAs<float>(3) == 0f, "with no delay");
    }

    /// <summary>
    /// The round trip that matters: what the sender packs is what the receiver
    /// reads, through one declaration.
    ///
    /// <para>
    /// NOT A BUFFER COMPARED WITH ITSELF. The two halves are the real
    /// <c>TempEnts.EnergySplash</c> and the real <c>ClientTeEnergySplash</c>,
    /// and what is asserted is the VALUES arriving at the drawing primitive —
    /// not that some bytes survived. A wrong offset on either side reads a
    /// plausible number out of a neighbour, which is exactly what a live join
    /// produced before the client's table was right.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatTheSenderPacksIsWhatThePlayerDraws()
    {
        var engine = ClientDoubles.Fake();
        var registry = Registered(engine);

        var (temps, _, _) = ServerWriter();
        temps.EnergySplash(TeFilters.Broadcast, 0f, new Vector(-2932f, 3218f, 60f),
                           new Vector(0f, 0f, 1f), explosive: true);

        // The blob as it was packed. RecordingEngine logs the LENGTH rather than
        // the span (a span cannot be boxed), so the bytes are repacked here
        // through the same path the sender used -- which is the sender's own
        // body and not a second opinion about it. That path is the DECLARATION
        // now: TempEnts.EnergySplash fills a TeEnergySplash and hands it over,
        // so repacking it by wire name here would have been the second opinion
        // this comment says it is not.
        TempEntType splash = registry.Find("CTEEnergySplash")!;

        byte[] blob = new TeEnergySplash
        {
            Position  = new Vector(-2932f, 3218f, 60f),
            Direction = new Vector(0f, 0f, 1f),
            Explosive = true,
        }.ToBlob(splash);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEEnergySplash", new ClientTeEnergySplash(engine));
        players.Dispatch(splash.Handle, blob);

        var drawn = engine.Last("Effects.ClientEnergySplash")!.Value;

        Assert.True(Same(drawn.ArgAs<Vector>(0), new Vector(-2932f, 3218f, 60f)),
            "the point the sender chose is the point the player draws at");
        Assert.True(Same(drawn.ArgAs<Vector>(1), new Vector(0f, 0f, 1f)),
            "and the normal it chose");
        Assert.True(drawn.ArgAs<bool>(2), "and the explosive bit");
    }

    // ---------------------------------------------------------------------
    // The declaration, which is what the mutation proof points at.
    // ---------------------------------------------------------------------

    /// <summary>
    /// <c>DT_TESparks</c>' whole field list as both sides build it — the wire
    /// name, the blob offset, the storage width, the wire width and the flags.
    ///
    /// <para>
    /// THIS IS GATE 4'S TARGET. Change one prop's bit width in
    /// <c>TeSparks.cs</c> and this goes red naming the field; restore it and it
    /// goes green. Every number here is one a wrong value would make the
    /// bitstream stop making sense partway through, with no log line anywhere —
    /// which is why they are asserted exactly rather than as "it has three
    /// fields".
    /// </para>
    ///
    /// <para>
    /// The offsets are the ones te S1's live join measured through the engine
    /// (<c>cs_sv_te_dump</c>: 0/4/8 for the base's origin, then 12, 16, 20),
    /// arrived at there by <c>SendTable_InitTable</c> and here by
    /// <c>EntityLayout</c>. Two roads, one set of numbers.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDeclarationIsWhatBothSidesBuildFrom()
    {
        var engine = ClientDoubles.Fake();
        TempEntType sparks = Registered(engine).Find("CTESparks")!;

        Assert.True(sparks.BlobBytes == 32,
            "DT_TESparks' blob is twelve base bytes and twenty of its own");

        TeField origin = sparks.Fields["Origin"];
        Assert.True(origin.Offset == 0, "Origin is at the front of the blob");
        Assert.True(origin.Kind == TePropKind.Float, "and is three SendPropFloats, not one vector");
        Assert.True(origin.Elems == 3, "three of them");
        Assert.True(origin.ElemStride == 4, "four bytes apart");
        Assert.True(origin.Bits == -1, "with the COORD flag deciding the width");
        Assert.True(origin.Flags == (TeFlags.Coord | TeFlags.VectorElem),
            "COORD, declared the way SENDINFO_VECTORELEM declares");

        TeField magnitude = sparks.Fields["Magnitude"];
        Assert.True(magnitude.Offset == 12, "Magnitude sits after the base's twelve bytes");
        Assert.True(magnitude.Kind == TePropKind.Int, "as an int");
        Assert.True(magnitude.SizeBytes == 4, "four bytes of blob");
        Assert.True(magnitude.Bits == 4, "four bits on the wire, as te_sparks.cpp declares");
        Assert.True(magnitude.Flags == TeFlags.Unsigned, "unsigned, so 0..15");

        TeField trail = sparks.Fields["TrailLength"];
        Assert.True(trail.Offset == 16, "TrailLength follows it");
        Assert.True(trail.Bits == 4, "also four bits");
        Assert.True(trail.Flags == TeFlags.Unsigned, "also unsigned");

        TeField dir = sparks.Fields["Direction"];
        Assert.True(dir.Offset == 20, "Direction is last");
        Assert.True(dir.Kind == TePropKind.Vector, "and is ONE SendPropVector, unlike the origin");
        Assert.True(dir.SizeBytes == 12, "twelve bytes");
        Assert.True(dir.Elems == 0, "not expanded into elements");
        Assert.True(dir.Bits == -1, "with the COORD flag deciding the width");
        Assert.True(dir.Flags == TeFlags.Coord, "COORD, and NOT declared as a vector elem");
    }
}
