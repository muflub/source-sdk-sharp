extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using client::SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// C-Phase 3, lane CP: the seven temp entities on <c>DT_TEParticleSystem</c>,
/// received in C# and played.
///
/// <para>
/// WHAT THESE ASSERT THAT THE SCHEMA TESTS CANNOT. <c>TempEntSchemaTests</c> and
/// the gtest hold the TABLE against stock; these hold the PLAYER against the
/// table — that the field a class reads by name is the field the sender wrote,
/// that the value arrives at the right ARGUMENT of the right primitive, and
/// that the decisions stock made on the client are still made on the client. A
/// table can be perfect and the effect still land at the world origin, or draw
/// the wrong one of two explosions, and neither shows up in a dump.
/// </para>
///
/// <para>
/// NOT A BUFFER COMPARED WITH ITSELF. Where a sender exists the round trip
/// starts at the real <c>TempEnts</c> method, so what is asserted is a value
/// chosen by a caller arriving at a drawing primitive through two independent
/// address computations. Two of the seven — BloodStream and ShowLine — have no
/// <c>ITempEnts</c> method, because neither had a live server caller to give
/// one to; their blobs are written through <c>TempEntWriter</c>, which is the
/// same code every sender's body uses.
/// </para>
/// </summary>
public class TempEntParticleClientTests
{
    /// <summary>
    /// Component-wise equality; <see cref="Vector"/> declares no <c>==</c>.
    /// EXACT, because nothing on this path encodes — the COORD rounding is the
    /// engine's and no test here reaches it.
    /// </summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// A registry over the real shared declarations, native answering a
    /// DISTINCT handle per registration as it does.
    /// </summary>
    private static TempEntRegistry Registered(RecordingEngine engine)
    {
        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        return registry;
    }

    /// <summary>
    /// Every player this lane owns, over one registry — which is also the
    /// arrangement the client runs in, so a name two players claimed would be
    /// caught here rather than at a join.
    /// </summary>
    /// <summary>The spark routines, recorded: the gauss discharge ends in C# (lane c5-particles).</summary>
    private sealed class Sparks : client::SourceSharp.Client.Effects.IClientSparks
    {
        public readonly List<(Vector Position, Vector Direction, int Type)> Gauss = [];

        public void ElectricSpark(Vector position, int magnitude, int trailLength, Vector? direction) { }
        public void MetalSpark(Vector position, Vector direction, Vector surfaceNormal, int scale = 1) { }
        public void EnergySplash(Vector position, Vector normal, bool explosive) { }
        public void GaussExplosion(Vector position, Vector direction, int type) => Gauss.Add((position, direction, type));
    }

    /// <summary>The explosions, recorded: fx_explosion.cpp's effects are C# (lane c5-particles).</summary>
    private sealed class Explosions : client::SourceSharp.Client.Effects.IClientExplosions
    {
        public readonly List<(client::SourceSharp.Client.Effects.ExplosionKind Kind, Vector Origin, float Force, float Scale, int Flags)> Made = [];

        public void Create(client::SourceSharp.Client.Effects.ExplosionKind kind, Vector position, float force, float scale, int flags)
            => Made.Add((kind, position, force, scale, flags));
    }

    // The explosions each fact's players draw through (a fresh class per fact).
    /// <summary>The funnel's sprites (LargeFunnel), as the player asks for them.</summary>
    private sealed class Funnels : client::SourceSharp.Client.Effects.ILargeFunnel
    {
        public readonly List<(Vector Origin, int Reversed)> Calls = [];

        public int Funnel(Vector origin, int reversed)
        {
            Calls.Add((origin, reversed));
            return 484;
        }
    }

    private readonly Funnels _funnels = new();

    private readonly DustPuffRecorder _puffs = new();

    private readonly TeParticleRecorder _te = new();

    private readonly Explosions _explosions = new();

    private ClientTempEnts Players(RecordingEngine engine, TempEntRegistry registry,
                                          IClientStandalones? standalones = null, ILog? log = null,
                                          Sparks? sparks = null, Explosions? explosions = null)
    {
        var players = new ClientTempEnts(log ?? new RecordingLog(), registry);

        // NO CLIENT ENTITIES BY DEFAULT, which is what every [Fact] here about
        // the DRAWING wants: behaviour ledger row C1's blast push walks the live
        // client entities, and an empty list means it asks the engine nothing at
        // all. A test about the push supplies its own bodies.
        standalones ??= Standalones();

        Assert.True(players.Add("CTESparks", new ClientTeSparks(engine)), "sparks claims its name");
        Assert.True(players.Add("CTEExplosion",
            new ClientTeExplosion(log ?? new RecordingLog(), engine, engine, standalones, new RecordingRagdolls(engine), engine, explosions ?? _explosions)), "and the explosion");
        Assert.True(players.Add("CTELargeFunnel", new ClientTeLargeFunnel(_funnels)), "and the funnel");
        Assert.True(players.Add("CTEBloodStream", new ClientTeBloodStream(_te)), "and the blood");
        Assert.True(players.Add("CTEShowLine", new ClientTeShowLine(_te)), "and the line");
        Assert.True(players.Add("CTEGaussExplosion", new ClientTeGaussExplosion(sparks ?? new Sparks())), "and the gauss");
        Assert.True(players.Add("CTEDust", new ClientTeDust(_puffs)), "and the dust");

        return players;
    }

    /// <summary>
    /// An engine whose one QUERY is stubbed. <see cref="RecordingEngine"/> is
    /// strict about a return nobody supplied, which is the behaviour that makes
    /// the water branch below a decision rather than a default.
    /// </summary>
    private static RecordingEngine Dry()
        => Fake().Returns("WorldQuery.PointContents", Contents.Empty);

    // ---------------------------------------------------------------------
    // The base, which is the whole reason this lane exists as one piece.
    // ---------------------------------------------------------------------

    /// <summary>
    /// All seven address <c>DT_TEParticleSystem</c>'s origin — stock's
    /// <c>m_vecOrigin</c>, this declaration's <c>Origin</c> — at the
    /// base's own offset, and none of them DECLARES it.
    ///
    /// <para>
    /// That is what "a base nested at datatable offset zero" means, and it is
    /// asserted for all seven at once rather than for one, because the base
    /// moved to managed only because all seven did. A seven-way agreement is
    /// also the cheapest statement that the subtree is complete: an eighth
    /// derived class arriving with a native client half would not be caught by
    /// a test that named one.
    /// </para>
    /// </summary>
    [Fact]
    public void AllSevenReadTheBasesOriginFromTheFrontOfTheirOwnBlob()
    {
        var engine = Dry();
        var registry = Registered(engine);

        TempEntType particles = registry.Find("CTEParticleSystem")!;

        foreach (string name in new[]
                 {
                     "CTESparks", "CTEExplosion", "CTELargeFunnel", "CTEBloodStream",
                     "CTEShowLine", "CTEGaussExplosion", "CTEDust",
                 })
        {
            TempEntType te = registry.Find(name)!;

            Assert.True(te.Class.BaseTable == "DT_TEParticleSystem",
                $"{name} nests DT_TEParticleSystem");
            Assert.True(!te.Class.Fields.Any(f => f.Name == "Origin"),
                $"{name} does not declare the origin itself — stock's m_vecOrigin is the "
              + "base's prop and this class's blob");
            Assert.True(te.Fields.ContainsKey("Origin"),
                $"{name} can still address it");
            Assert.True(te.Fields["Origin"].Offset == particles.Fields["Origin"].Offset,
                $"{name} reads it at the base's own offset");
            Assert.True(te.Fields["Origin"].Offset == 0,
                $"{name}'s blob starts with it");
        }
    }

    /// <summary>
    /// The base claims its CLASS on both sides, and that is not decoration.
    ///
    /// <para>
    /// A ServerClass with no matching ClientClass makes the client begin
    /// instantiating and stop (plan 2E). Stock's <c>CTEParticleSystem</c> was a
    /// registered ServerClass paired with a registered ClientClass; evicting
    /// only the client's half left the server's alone in the list, and a live
    /// join answered with <c>Client missing DT class CTEParticleSystem</c>,
    /// <c>InitFastCopy - missing client class 47</c> and a stack overflow. So
    /// the base's two halves move together or not at all, and this is the
    /// assertion that says so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBaseClaimsItsClassOnBothSides()
    {
        var engine = Dry();
        TempEntType particles = Registered(engine).Find("CTEParticleSystem")!;

        Assert.True(particles.Class.RegistersClass,
            "CTEParticleSystem is a managed class on both sides: te_particlesystem.cpp is "
          + "evicted and the C_TEParticleSystem block is guarded out, so nothing native "
          + "holds the name on either side. A table whose class only one side registers is "
          + "plan 2E's hang");
        Assert.True(particles.Class.BaseTable == "DT_BaseTempEntity",
            "and it nests the empty base, as stock does");
        Assert.True(particles.Class.Fields.Count() == 1,
            "with one field of its own, the sort origin");
    }

    // ---------------------------------------------------------------------
    // One class at a time, from a caller's value to a drawing argument.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheSparksReachTheirPrimitiveWithWhatTheSenderChose()
    {
        var engine = Dry();
        var registry = Registered(engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);
        temps.Sparks(TeFilter.Broadcast, 0f, new Vector(-2932f, 3218f, 60f),
                     magnitude: 6, trailLength: 3, direction: new Vector(0f, 0f, 1f));

        TempEntType sparks = registry.Find("CTESparks")!;
        var blob = new byte[sparks.BlobBytes];
        var writer = new TempEntWriter(sparks, blob);
        writer.Vector("Origin", new Vector(-2932f, 3218f, 60f));
        writer.Int("Magnitude", 6);
        writer.Int("TrailLength", 3);
        writer.Vector("Direction", new Vector(0f, 0f, 1f));

        Players(engine, registry).Dispatch(sparks.Handle, blob);

        var call = engine.Last("Effects.ClientSparks")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(-2932f, 3218f, 60f)),
            "the spark is where the server put it, not at the world origin");

        // The two four-bit ints are DIFFERENT numbers on purpose: 6 and 3 read
        // the same way round only if the two reads are not transposed, and a
        // transposition here is a spark with the wrong shape rather than none.
        Assert.True(call.ArgAs<int>(1) == 6, "the magnitude is argument one");
        Assert.True(call.ArgAs<int>(2) == 3, "and the trail length argument two");

        Assert.True(Same(call.ArgAs<Vector?>(3)!.Value, new Vector(0f, 0f, 1f)),
            "and the direction is passed, not dropped");
    }

    [Fact]
    public void TheFunnelCarriesItsDirectionFlagAndDrawsNothingElse()
    {
        var engine = Dry();
        var registry = Registered(engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);
        temps.LargeFunnel(TeFilter.Broadcast, 0f, new Vector(100f, 200f, 300f),
                          modelIndex: 41, reversed: 1);

        TempEntType funnel = registry.Find("CTELargeFunnel")!;
        var blob = new byte[funnel.BlobBytes];
        var writer = new TempEntWriter(funnel, blob);
        writer.Vector("Origin", new Vector(100f, 200f, 300f));
        writer.Int("ModelIndex", 41);
        writer.Int("Reversed", 1);

        Players(engine, registry).Dispatch(funnel.Handle, blob);

        Assert.Single(_funnels.Calls);
        var call = _funnels.Calls[0];

        Assert.True(Same(call.Origin, new Vector(100f, 200f, 300f)), "at the origin sent");
        Assert.True(call.Reversed == 1, "reversed, which is the other direction entirely");

        // m_nModelIndex is on the wire, is read, and reaches nothing — stock
        // stores it and draws with sprites/flare6 regardless; ILargeFunnel takes
        // an origin and a flag and nothing else.
    }

    /// <summary>
    /// <c>m_nReversed</c> is two bits and stock tests it for TRUTH
    /// (<c>if ( m_nReversed )</c>), so 2 is a reversed funnel. A decoder that
    /// compared against 1 would draw the forward funnel for half the values the
    /// prop can carry.
    /// </summary>
    [Fact]
    public void AReversedFunnelIsAnyNonZeroValueAndNotJustOne()
    {
        var engine = Dry();
        var registry = Registered(engine);

        TempEntType funnel = registry.Find("CTELargeFunnel")!;
        var blob = new byte[funnel.BlobBytes];
        var writer = new TempEntWriter(funnel, blob);
        writer.Vector("Origin", Vector.Zero);
        writer.Int("ModelIndex", 0);
        writer.Int("Reversed", 2);

        Players(engine, registry).Dispatch(funnel.Handle, blob);

        Assert.True(_funnels.Calls.Single().Reversed == 2,
            "2 reaches the backend as 2 and is truth-tested there, as stock tests it — "
          + "clamping it to 0 or 1 here would decide the effect in the wrong place");
    }

    /// <summary>
    /// The blood stream's four colour props are spelled <c>r</c>, <c>g</c>,
    /// <c>b</c>, <c>a</c> on the wire — one letter, no <c>m_</c> — and they are
    /// four DISTINCT values here so a transposition cannot pass.
    /// </summary>
    [Fact]
    public void TheBloodStreamKeepsItsFourColourPropsApart()
    {
        var engine = Dry();
        var registry = Registered(engine);

        TempEntType blood = registry.Find("CTEBloodStream")!;
        var blob = new byte[blood.BlobBytes];
        var writer = new TempEntWriter(blood, blob);

        writer.Vector("Origin", new Vector(-64f, 8f, 40f));
        writer.Vector("Direction", new Vector(0f, 1f, 0f));
        writer.Int("ColorR", 247);
        writer.Int("ColorG", 11);
        writer.Int("ColorB", 22);
        writer.Int("ColorA", 33);
        writer.Int("Amount", 120);

        Players(engine, registry).Dispatch(blood.Handle, blob);

        // TE_BloodStream is C#'s (lane c5-particles: TeParticles).
        var call = _te.Blood.Last();

        Assert.True(Same(call.Origin, new Vector(-64f, 8f, 40f)), "the origin first");
        Assert.True(Same(call.Direction, new Vector(0f, 1f, 0f)), "then the direction");
        Assert.True(call.R == 247, "r is argument two");
        Assert.True(call.G == 11, "g three");
        Assert.True(call.B == 22, "b four");
        Assert.True(call.A == 33, "a five");
        Assert.True(call.Amount == 120, "and the amount LAST, which is also the speed");
    }

    [Fact]
    public void TheLineIsDrawnFromTheBasesOriginToItsOwnEnd()
    {
        var engine = Dry();
        var registry = Registered(engine);

        TempEntType line = registry.Find("CTEShowLine")!;
        var blob = new byte[line.BlobBytes];
        var writer = new TempEntWriter(line, blob);

        writer.Vector("Origin", new Vector(1f, 2f, 3f));
        writer.Vector("End", new Vector(101f, 202f, 303f));

        Players(engine, registry).Dispatch(line.Handle, blob);

        // The line is C#'s (lane c5-particles: TeParticles).
        var call = _te.Lines.Last();

        // The two ends come from DIFFERENT tables -- the start is
        // DT_TEParticleSystem's and the end is DT_TEShowLine's -- so a class
        // that read only its own fields would draw every line from the world
        // origin, and one that read them the other way round would draw it
        // backwards. Distinct values catch both.
        Assert.True(Same(call.Start, new Vector(1f, 2f, 3f)),
            "the start is the BASE's origin");
        Assert.True(Same(call.End, new Vector(101f, 202f, 303f)),
            "and the end is this class's own field");
    }

    [Fact]
    public void TheGaussDischargeCarriesItsTypeAndDirection()
    {
        var engine = Dry();
        var registry = Registered(engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);
        temps.GaussExplosion(TeFilter.Broadcast, 0f, new Vector(9f, 8f, 7f),
                             new Vector(1f, 0f, 0f), type: 2);

        TempEntType gauss = registry.Find("CTEGaussExplosion")!;
        var blob = new byte[gauss.BlobBytes];
        var writer = new TempEntWriter(gauss, blob);
        writer.Vector("Origin", new Vector(9f, 8f, 7f));
        writer.Int("Type", 2);
        writer.Vector("Direction", new Vector(1f, 0f, 0f));

        var sparks = new Sparks();
        Players(engine, registry, sparks: sparks).Dispatch(gauss.Handle, blob);

        var call = Assert.Single(sparks.Gauss);

        Assert.True(Same(call.Position, new Vector(9f, 8f, 7f)), "at the point sent");
        Assert.True(Same(call.Direction, new Vector(1f, 0f, 0f)), "along the direction sent");

        // The type picks the spark colour and whether a dust cloud comes with
        // it. It is two bits, and it is written BETWEEN the origin and the
        // direction in the declaration while arriving LAST at the primitive --
        // which is the transposition a positional read would make.
        Assert.True(call.Type == 2, "and the type is the last argument, not the first");
    }

    [Fact]
    public void TheDustPuffSeparatesItsSizeFromItsSpeed()
    {
        var engine = Dry();
        var registry = Registered(engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);
        temps.Dust(TeFilter.Broadcast, 0f, new Vector(5f, 6f, 7f),
                   new Vector(0f, 0f, 1f), size: 64f, speed: 12f);

        TempEntType dust = registry.Find("CTEDust")!;
        var blob = new byte[dust.BlobBytes];
        var writer = new TempEntWriter(dust, blob);
        writer.Vector("Origin", new Vector(5f, 6f, 7f));
        writer.Float("Size", 64f);
        writer.Float("Speed", 12f);
        writer.Vector("Direction", new Vector(0f, 0f, 1f));

        Players(engine, registry).Dispatch(dust.Handle, blob);

        // FX_Dust is C#'s (lane c5-particles: DustPuff), so the puff is the managed one's.
        var call = _puffs.Calls.Last();

        Assert.True(Same(call.Origin, new Vector(5f, 6f, 7f)), "at the point sent");
        Assert.True(Same(call.Direction, new Vector(0f, 0f, 1f)), "along the normal sent");

        // Two adjacent floats, and swapping them is a puff that is the wrong
        // size AND the wrong speed while still being a puff.
        Assert.True(call.Size == 64f, "the size is argument two");
        Assert.True(call.Speed == 12f, "and the speed argument three");
    }

    // ---------------------------------------------------------------------
    // The explosion, which is the only one of the seven with decisions in it.
    // ---------------------------------------------------------------------

    /// <summary>
    /// One blob for the explosion, written the way a sender writes one.
    /// </summary>
    private static byte[] ExplosionBlob(TempEntType te, Vector origin, int flags,
                                        int radius = 300, int magnitude = 100,
                                        float scale = 1.5f)
    {
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", origin);
        writer.Int("ModelIndex", 77);
        writer.Float("Scale", scale);
        writer.Int("FrameRate", 12);
        writer.Int("Flags", flags);
        writer.Vector("Normal", new Vector(0f, 0f, 1f));
        writer.Int("MaterialType", (int)'C');
        writer.Int("Radius", radius);
        writer.Int("Magnitude", magnitude);

        return blob;
    }

    [Fact]
    public void ADryExplosionDrawsTheFireballAndThenTheCore()
    {
        var engine = Dry();
        var registry = Registered(engine);

        TempEntType boom = registry.Find("CTEExplosion")!;

        Players(engine, registry).Dispatch(boom.Handle,
            ExplosionBlob(boom, new Vector(-1000f, 250f, 64f), flags: 0));

        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Water) == 0,
            "no splash on dry land");
        Assert.True(engine.CountOf("Effects.ClientExplosionFireball") == 1,
            "the fireball overlay, because no flag suppressed it");
        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Base) == 1,
            "and the explosion itself");

        var core = _explosions.Made.Last(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Base);

        Assert.True(Same(core.Origin, new Vector(-1000f, 250f, 64f)), "where it went off");

        // The FORCE is m_nMagnitude and the SCALE is m_fScale, and they are
        // adjacent floats at the primitive with very different meanings: stock's
        // Create( position, force, scale, flags ). 100 and 1.5 cannot be
        // transposed silently.
        Assert.True(core.Force == 100f, "the magnitude is the FORCE");
        Assert.True(core.Scale == 1.5f, "and m_fScale is the SCALE");
        Assert.True(core.Flags == 0, "with the flags it was sent with");

        Assert.True(Same(engine.Last("Effects.ClientExplosionFireball")!.Value.ArgAs<Vector>(0),
                         new Vector(-1000f, 250f, 64f)),
            "and the overlay is at the same point, not at the origin");
    }

    /// <summary>
    /// <c>TE_EXPLFLAG_NOFIREBALL</c> suppresses the overlay and NOTHING ELSE.
    /// The explosion still happens; a decoder that returned early on the flag
    /// would delete the whole effect for every grenade that set it.
    /// </summary>
    [Fact]
    public void TheNoFireballFlagRemovesTheOverlayAndLeavesTheExplosion()
    {
        var engine = Dry();
        var registry = Registered(engine);

        TempEntType boom = registry.Find("CTEExplosion")!;

        Players(engine, registry).Dispatch(boom.Handle,
            ExplosionBlob(boom, new Vector(0f, 0f, 0f), flags: 0x40));

        Assert.True(engine.CountOf("Effects.ClientExplosionFireball") == 0,
            "TE_EXPLFLAG_NOFIREBALL is 0x40 and it means no fireball");
        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Base) == 1,
            "and the explosion still happens, which is the half a flag test gets wrong");
    }

    /// <summary>
    /// The flag is tested as a BIT. Stock writes
    /// <c>!( m_nFlags &amp; TE_EXPLFLAG_NOFIREBALL )</c>, so any value with
    /// 0x40 set suppresses the overlay and any value without it does not —
    /// including values numerically larger than 0x40.
    /// </summary>
    [Fact]
    public void TheFireballFlagIsABitAndNotAThreshold()
    {
        var engine = Dry();
        var registry = Registered(engine);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry);

        // 0x80 is TE_EXPLFLAG_NOFIREBALLSMOKE: bigger than 0x40, and NOT it.
        players.Dispatch(boom.Handle, ExplosionBlob(boom, Vector.Zero, flags: 0x80));

        Assert.True(engine.CountOf("Effects.ClientExplosionFireball") == 1,
            "0x80 does not contain 0x40, so the fireball still draws");

        // 0x41 is NOFIREBALL with something else beside it.
        players.Dispatch(boom.Handle, ExplosionBlob(boom, Vector.Zero, flags: 0x41));

        Assert.True(engine.CountOf("Effects.ClientExplosionFireball") == 1,
            "and 0x41 does contain it, so no second overlay was drawn");
    }

    /// <summary>
    /// UNDERWATER IS A DIFFERENT EFFECT AND AN EARLY RETURN. Stock draws the
    /// water explosion and <c>return</c>s, so there is no fireball and no land
    /// explosion behind the splash.
    ///
    /// <para>
    /// This is the assertion that pays for the client arm of
    /// <c>PointContents</c>. Folding the test into the backend would have made
    /// it untestable from here, and defaulting it to "not water" — which is
    /// what a null slot answers — would have drawn a fireball under the sea in
    /// every map with water in it, silently.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnderwaterExplosionIsASplashAndNothingElse()
    {
        var engine = Fake().Returns("WorldQuery.PointContents", Contents.Water);
        var registry = Registered(engine);

        TempEntType boom = registry.Find("CTEExplosion")!;

        Players(engine, registry).Dispatch(boom.Handle,
            ExplosionBlob(boom, new Vector(12f, 34f, -56f), flags: 0));

        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Water) == 1, "the splash");
        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Base) == 0,
            "and NOT the land explosion as well");
        Assert.True(engine.CountOf("Effects.ClientExplosionFireball") == 0,
            "nor a fireball on top of it: stock returns before reaching either");

        var splash = _explosions.Made.Last(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Water);

        Assert.True(Same(splash.Origin, new Vector(12f, 34f, -56f)),
            "where it went off");
        Assert.True(splash.Force == 100f, "with the same force the dry one gets");
        Assert.True(splash.Scale == 1.5f, "and the same scale");

        // The question is asked at the EXPLOSION's point, which is the only
        // place stock asks it. Asking at the local player, or not asking, are
        // both effects that look right most of the time.
        Assert.True(Same(engine.Last("WorldQuery.PointContents")!.Value.ArgAs<Vector>(0),
                         new Vector(12f, 34f, -56f)),
            "and the contents were asked for AT the explosion");
    }

    /// <summary>
    /// Slime is not water, and stock's explosion does not test for it. A
    /// managed "am I submerged" that had generalised the test would change an
    /// effect nobody asked to change.
    /// </summary>
    [Fact]
    public void SlimeIsNotWaterHereBecauseItIsNotWaterInStock()
    {
        var engine = Fake().Returns("WorldQuery.PointContents", Contents.Solid);
        var registry = Registered(engine);

        TempEntType boom = registry.Find("CTEExplosion")!;

        Players(engine, registry).Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0));

        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Water) == 0,
            "only CONTENTS_WATER makes a splash");
        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Base) == 1,
            "everything else is an ordinary explosion");
    }

    // ---------------------------------------------------------------------
    // BEHAVIOUR LEDGER ROW C1 — CLOSED, and these are what hold it closed.
    //
    // The three [Fact]s that stood here pinned a STUB: that it warned once per
    // level, that it was silent where stock was silent, and that the call site
    // still existed by name. They are gone because the thing they described is
    // gone -- the push is real -- and what replaces them asserts the behaviour
    // rather than the refusal. The middle one survives in spirit and by name
    // (AnExplosionThatWouldNotHavePushedAnythingIsNotEvenLookedAt): stock's
    // guard is still stock's guard, and it is now asserted by nothing being
    // ASKED rather than by nothing being said.
    // ---------------------------------------------------------------------

    /// <summary>
    /// One body inside the blast, with the line of sight clear: it is pushed,
    /// and the impulse is stock's arithmetic to the unit.
    ///
    /// <para>
    /// THIS IS THE ROW. Every other [Fact] below narrows it — who is skipped
    /// and why — and none of them would notice if the push itself were wired to
    /// the wrong number, because a skipped body and a body pushed by (0,0,0)
    /// look identical from every counter on this client. The value is spelled
    /// out from stock's own four steps rather than recomputed from the code
    /// under test:
    /// </para>
    /// <code>
    /// force  = magnitude - ( magnitude / radius ) * dist    // 100 - (100/200)*100 = 50
    /// dir   *= force                                        // (1,0,0) * 50 = (50,0,0)
    /// start  = end - dir                                    // (100,0,0) - (50,0,0) = (50,0,0)
    /// start.z -= 32                                         // (50,0,-32)
    /// impulse = ( end - start ) * 500                       // (50,0,32) * 500
    /// </code>
    /// </summary>
    [Fact]
    public void ABodyInTheBlastIsPushedWithStocksOwnImpulse()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(100f, 0f, 0f));
        engine.Returns("WorldQuery.ClientTraceLine", Clear(new Vector(100f, 0f, 0f)));
        engine.Returns("EntityRender.RagdollApplyForceCenter", true);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        Assert.True(engine.CountOf("EntityRender.RagdollApplyForceCenter") == 1,
            "a jointed body inside the radius with a clear line to it is pushed");

        var push = engine.Last("EntityRender.RagdollApplyForceCenter")!.Value;

        Assert.True(Same(push.ArgAs<Vector>(1), new Vector(25000f, 0f, 16000f)),
            "and the impulse is stock's: the falloff, then the 32 units the blast centre is "
          + "dropped by so things fly higher, then C_ClientRagdoll::ImpactTrace's 500");

        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Base) == 1,
            "and the explosion still drew, because the push is not instead of the effect");
    }

    /// <summary>
    /// The blast line is stock's line: <c>MASK_SHOT_HULL</c>, from the blast
    /// centre to the body, filtered as <c>UTIL_TraceLine</c>'s NULL filter is.
    ///
    /// <para>
    /// SEPARATE FROM THE PUSH ABOVE because a mask is invisible to it. Every
    /// assertion in this file about where a body ended up would agree with
    /// <see cref="TraceMask.Shot"/>, which is MASK_SHOT — the same trace with
    /// the HITBOX bit in and the GRATE bit out. A blast that traced with that
    /// would be blocked by a player's hitbox and would pass through a floor
    /// grating, and no impulse anywhere would look wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBlastLineIsMaskShotHullAndNotMaskShot()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(0f, 60f, 0f));
        engine.Returns("WorldQuery.ClientTraceLine", Clear(new Vector(0f, 60f, 0f)));
        engine.Returns("EntityRender.RagdollApplyForceCenter", true);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, new Vector(0f, 0f, 0f), flags: 0, radius: 200, magnitude: 100));

        var line = engine.Last("WorldQuery.ClientTraceLine")!.Value;

        Assert.True(Same(line.ArgAs<Vector>(0), Vector.Zero),
            "the line starts at the blast centre");
        Assert.True(Same(line.ArgAs<Vector>(1), new Vector(0f, 60f, 0f)),
            "and ends where the SOLVER says the body is, not where it was networked");
        Assert.True(line.ArgAs<TraceMask>(2) == TraceMask.ShotHull,
            "MASK_SHOT_HULL, which is what CRagdollExplosionEnumerator traces with — not "
          + "MASK_SHOT, which carries the hitbox bit and drops the grate bit");
        Assert.True(line.ArgAs<TraceFilter>(3) == TraceFilter.Simple,
            "and CTraceFilterSimple, which is what UTIL_TraceLine's NULL filter builds");
    }

    /// <summary>
    /// A wall between the blast and the body: no push. Stock's test is
    /// <c>tr.fraction &lt; 1.0f &amp;&amp; tr.m_pEnt != pEnt</c>, and the second
    /// clause is why the trace's entity is compared with this body's index
    /// rather than the fraction being read alone.
    /// </summary>
    [Fact]
    public void AWallBetweenTheBlastAndTheBodyStopsThePush()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(100f, 0f, 0f));
        // Half way, and the world (index 0) is what stopped it.
        engine.Returns("WorldQuery.ClientTraceLine",
            new ClientTraceResult(true, 0.5f, new Vector(50f, 0f, 0f), new Vector(-1f, 0f, 0f),
                                  0, 0, 0, 0));
        engine.Returns("Entities.GetIndex", 7);
        engine.Returns("EntityRender.RagdollApplyForceCenter", true);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 1,
            "the line was traced");
        Assert.True(engine.CountOf("EntityRender.RagdollApplyForceCenter") == 0,
            "and nothing was pushed through the wall it stopped on");
    }

    /// <summary>
    /// The same trace, stopping on THIS body: pushed anyway. That is the whole
    /// of stock's second clause, and it is the half a fraction test alone would
    /// silently lose.
    /// </summary>
    [Fact]
    public void ALineThatStopsOnTheBodyItselfStillPushesIt()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(100f, 0f, 0f));
        engine.Returns("WorldQuery.ClientTraceLine",
            new ClientTraceResult(true, 0.9f, new Vector(90f, 0f, 0f), new Vector(-1f, 0f, 0f),
                                  0, 7, 0, 0));
        engine.Returns("Entities.GetIndex", 7);
        engine.Returns("EntityRender.RagdollApplyForceCenter", true);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        Assert.True(engine.CountOf("EntityRender.RagdollApplyForceCenter") == 1,
            "a line that stopped on the body it was aimed at has not been blocked by "
          + "anything, and stock pushes it");
    }

    /// <summary>
    /// An entity with no jointed body is skipped, and is skipped BEFORE the
    /// trace — a blast that traced a line to every entity on the map to decide
    /// it could not push any of them is the cost the row refused to pay.
    /// </summary>
    [Fact]
    public void AnEntityWithNoJointedBodyIsNeitherTracedNorPushed()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.None);

        // Stubbed although the answer says there is no body: the recorder fills
        // an out parameter before it records the call, which is the shape every
        // out-taking member on it has (Console.SnapshotEntry, GetAttachment).
        // A value here that the code under test must not read is the point --
        // if it were read, the body would be pushed from the world origin and
        // the [Fact] below would be counting a push that should not exist.
        engine.Returns("EntityRender.RagdollState.origin", new Vector(100f, 0f, 0f));

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        Assert.True(engine.CountOf("EntityRender.RagdollState") == 1, "it was asked about");
        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 0,
            "and nothing was traced to a thing that cannot be pushed");
        Assert.True(engine.CountOf("EntityRender.RagdollApplyForceCenter") == 0,
            "and nothing was pushed");
    }

    /// <summary>
    /// A body made on THIS TICK is skipped: stock's
    /// <c>WasRagdollCreatedOnCurrentTick</c>, whose reason is that the forces
    /// were already applied when it was made.
    /// </summary>
    [Fact]
    public void ABodyMadeOnThisTickAlreadyHasItsForceAndIsSkipped()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState",
            RagdollState.Present | RagdollState.MadeThisTick);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(100f, 0f, 0f));

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        Assert.True(engine.CountOf("EntityRender.RagdollApplyForceCenter") == 0,
            "a body created this tick carries its creation force already, and stock will not "
          + "add the blast on top of it");
        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 0,
            "and it is dropped before the trace, as EnumElement drops it before the list");
    }

    /// <summary>
    /// The sphere, and it is asserted where it is the ONLY thing that can
    /// answer: a radius that arrived NEGATIVE.
    ///
    /// <para>
    /// THIS TEST WAS FIRST WRITTEN AS "a body outside the radius is not even
    /// traced", AND IT COULD NOT FAIL. Deleting the sphere test left it green,
    /// because for any positive radius the falloff below dominates it — 
    /// <c>force &gt; 1</c> requires <c>dist &lt; radius × (1 − 1/magnitude)</c>,
    /// which is inside the sphere by construction — and the force cut runs
    /// before the trace. So a body 300 units from a 200-unit blast is dropped
    /// either way and nothing observable changes.
    /// </para>
    ///
    /// <para>
    /// WHERE THE TWO COME APART IS A NEGATIVE RADIUS, and that is reachable
    /// rather than theoretical: <c>m_nRadius</c> is <c>SPROP_UNSIGNED</c> over
    /// thirty-two bits (<c>te_explosion.cpp:107</c>) and lands in an
    /// <c>int</c>, so any value with the top bit set decodes negative. With a
    /// negative radius the falloff INVERTS — <c>magnitude − (magnitude/radius)
    /// × dist</c> becomes <c>magnitude + |magnitude/radius| × dist</c> — so
    /// every body on the map passes the cut and is pushed HARDER the further
    /// away it is. Stock is safe from that without a test of its own, because
    /// <c>EnumerateElementsInSphere</c> with a negative radius enumerates
    /// nothing. Walking a list has no such floor, so the test the partition was
    /// doing has to be done here, and this is the [Fact] that says so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSphereIsWhatStopsAnInvertedRadiusPushingTheWholeMap()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(4000f, 0f, 0f));
        engine.Returns("WorldQuery.ClientTraceLine", Clear(new Vector(4000f, 0f, 0f)));
        engine.Returns("EntityRender.RagdollApplyForceCenter", true);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: -200, magnitude: 100));

        Assert.True(engine.CountOf("EntityRender.RagdollApplyForceCenter") == 0,
            "a body 4000 units from a blast whose radius decoded negative is not pushed. "
          + "Without the sphere test the falloff inverts and pushes it with a force of "
          + "2100 — and the further away it is, the harder");
        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 0,
            "and it is dropped before the trace, where EnumerateElementsInSphere dropped it");
    }

    /// <summary>
    /// The ordinary case of the same thing: a body outside a positive radius is
    /// not pushed.
    ///
    /// <para>
    /// KEPT ALTHOUGH THE FALLOFF WOULD ALSO CATCH IT, and labelled as such
    /// rather than left to look like the sphere's guard. It is the statement
    /// that the two rules agree on the common case, which is what makes the
    /// negative-radius [Fact] above a corner rather than a contradiction.
    /// </para>
    /// </summary>
    [Fact]
    public void ABodyOutsideTheRadiusIsNotPushed()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(300f, 0f, 0f));

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 0,
            "a body 300 units from a 200-unit blast is not traced to");
        Assert.True(engine.CountOf("EntityRender.RagdollApplyForceCenter") == 0,
            "and it is not pushed");
    }

    /// <summary>
    /// Stock's <c>force &lt;= 1.0f</c> cut, both ways.
    ///
    /// <para>
    /// A RED/GREEN PAIR over one boundary, because a count survives a broken
    /// half: at 198 units of a 200-unit, magnitude-100 blast the force is
    /// exactly 1 and stock drops it; two units nearer it is 2 and stock keeps
    /// it. A cut written as <c>&lt; 1</c>, or against the wrong side of the
    /// falloff, passes one of these and fails the other.
    /// </para>
    /// </summary>
    [Fact]
    public void TheForceCutIsStocksAndIsAtOneRatherThanZero()
    {
        Assert.True(Pushes(dist: 196f) == 1,
            "at 196 units the force is 2, which is above stock's cut");
        Assert.True(Pushes(dist: 198f) == 0,
            "at 198 units it is exactly 1, and stock's test is force <= 1.0f");
    }

    /// <summary>How many bodies a 200/100 blast pushes with one at this distance.</summary>
    private int Pushes(float dist)
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(dist, 0f, 0f));
        engine.Returns("WorldQuery.ClientTraceLine", Clear(new Vector(dist, 0f, 0f)));
        engine.Returns("EntityRender.RagdollApplyForceCenter", true);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        return engine.CountOf("EntityRender.RagdollApplyForceCenter");
    }

    /// <summary>
    /// Stock's own guard is kept as stock's guard: a radius or a magnitude of
    /// zero is an explosion that pushes nothing, and neither was ever the gap.
    ///
    /// <para>
    /// The predecessor of this [Fact] asserted that the stub said NOTHING for
    /// these two. With the push real there is no warning to count, so what is
    /// asserted instead is stronger: the entity list is not even walked. An
    /// explosion with no radius that asked every body on the map whether it was
    /// a ragdoll would be a guard that had stopped guarding.
    /// </para>
    /// </summary>
    [Fact]
    public void AnExplosionThatWouldNotHavePushedAnythingIsNotEvenLookedAt()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));

        players.Dispatch(boom.Handle, ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 0));
        players.Dispatch(boom.Handle, ExplosionBlob(boom, Vector.Zero, flags: 0, magnitude: 0));

        Assert.True(engine.CountOf("EntityRender.RagdollState") == 0,
            "stock's AffectRagdolls returns on either being zero, before it builds an "
          + "enumerator or asks the world anything");

        // And the effect still drew, both times: the guard is on the push, not
        // on the explosion.
        Assert.True(_explosions.Made.Count(m => m.Kind == client::SourceSharp.Client.Effects.ExplosionKind.Base) == 2,
            "both explosions still happened");
    }

    /// <summary>
    /// A body the solver refuses to push is said out loud — once per level, and
    /// again on the next level.
    ///
    /// <para>
    /// THE ONCE-PER-LEVEL MACHINERY OUTLIVES THE ROW THAT NEEDED IT, and it is
    /// pointed at the one thing here that can still fail silently. Row C1 spent
    /// this whole port being a push that did not happen and said nothing; a
    /// body that passes every test and is still not moved is that shape exactly,
    /// so it is a warning rather than a <c>continue</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void ABodyTheSolverRefusesIsSaidOncePerLevel()
    {
        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(100f, 0f, 0f));
        engine.Returns("WorldQuery.ClientTraceLine", Clear(new Vector(100f, 0f, 0f)));
        engine.Returns("EntityRender.RagdollApplyForceCenter", false);

        var said = new List<string>();
        var testLog = new RecordingLog { Line = (message, _) => said.Add(message) };

        try
        {
            TempEntType boom = registry.Find("CTEExplosion")!;
            var players = Players(engine, registry, Standalones(body), testLog);

            for (int i = 0; i < 5; i++)
            {
                players.Dispatch(boom.Handle,
                    ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));
            }

            Assert.True(said.Count(m => m.Contains("row C1")) == 1,
                "five explosions, one line: a firefight sends hundreds a second and a "
              + "per-blast warning buries the line that explained it");
            Assert.True(said.Any(m => m.Contains("refused the impulse")),
                "and the line says what happened, not that 'a feature' is missing");

            players.LevelInit();

            players.Dispatch(boom.Handle,
                ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

            Assert.True(said.Count(m => m.Contains("row C1")) == 2,
                "and it is said again on the next level");
        }
        finally
        {
        }
    }

    /// <summary>
    /// The row is CLOSED, and this is what a later reader must not quietly
    /// undo: the call is still there, it is still per-level, and it still
    /// reaches the impulse.
    ///
    /// <para>
    /// Its predecessor asserted only that <c>AffectRagdolls</c> existed, which
    /// was the right guard for a stub and is the wrong one for a capability: a
    /// method that exists and pushes nothing would pass it. What is asserted
    /// here is that calling <see cref="ClientTeExplosion.Play"/> is what makes
    /// the push happen — nothing else on this client can call it.
    /// </para>
    /// </summary>
    [Fact]
    public void RowC1IsClosedByThePushAndNotByTheCallGoingAway()
    {
        var affect = typeof(ClientTeExplosion)
            .GetMethod("AffectRagdolls", System.Reflection.BindingFlags.NonPublic
                                       | System.Reflection.BindingFlags.Instance);

        Assert.True(affect is not null,
            "ClientTeExplosion.AffectRagdolls exists. Behaviour ledger row C1 is closed by "
          + "making the capability real, NEVER by deleting the call that exposed it");

        Assert.True(typeof(IClientTempEntLevel).IsAssignableFrom(typeof(ClientTeExplosion)),
            "and it is still per-level, which is what makes what is left of the refusal "
          + "visible in the map it happened in rather than once at load");

        var engine = Dry();
        var registry = Registered(engine);
        var body = Body(engine);

        engine.Returns("EntityRender.RagdollState", RagdollState.Present);
        engine.Returns("EntityRender.RagdollState.origin", new Vector(100f, 0f, 0f));
        engine.Returns("WorldQuery.ClientTraceLine", Clear(new Vector(100f, 0f, 0f)));
        engine.Returns("EntityRender.RagdollApplyForceCenter", true);

        TempEntType boom = registry.Find("CTEExplosion")!;
        var players = Players(engine, registry, Standalones(body));
        var explosion = (ClientTeExplosion)players.Player("CTEExplosion")!;

        Assert.True(explosion.RagdollsPushed == 0, "nothing has been pushed yet");

        players.Dispatch(boom.Handle,
            ExplosionBlob(boom, Vector.Zero, flags: 0, radius: 200, magnitude: 100));

        Assert.True(explosion.RagdollsPushed == 1 && explosion.BlastsThatPushed == 1,
            "and playing one explosion is what pushed a body — the counter cs_cl_te_report "
          + "reads is the same instance the client dispatches into");
    }

    /// <summary>A trace that reached the body it was aimed at, with nothing in the way.</summary>
    private static ClientTraceResult Clear(Vector end)
        => new(false, 1f, end, new Vector(0f, 0f, 1f), 0, -1, 0, 0);

    /// <summary>One client entity, of the only shape a test can build.</summary>
    private static ClientStandaloneEntity Body(RecordingEngine engine)
        => new TestBody((IEngineEntityRender)engine);

    /// <summary>A standalone registry standing in for the live one.</summary>
    private static IClientStandalones Standalones(params ClientStandaloneEntity[] live)
        => new TestStandalones(live);

    /// <summary>
    /// A client entity with no handle, because <c>Handle</c> is the registry's
    /// to set and is not writable from here. Nothing in the blast push depends
    /// on its VALUE — it is passed straight through to the engine double, which
    /// answers by member name — and a test that set one would be asserting the
    /// registry's job rather than the push's.
    /// </summary>
    private sealed class TestBody : ClientStandaloneEntity
    {
        public TestBody(IEngineEntityRender render) : base(render) { }

        public override string ClassName => "prop_physics";
    }

    private sealed class TestStandalones : IClientStandalones
    {
        private readonly IReadOnlyList<ClientStandaloneEntity> _live;

        public TestStandalones(IReadOnlyList<ClientStandaloneEntity> live) => _live = live;

        public IReadOnlyList<ClientStandaloneEntity> Live() => _live;

        // The blast push walks Live() and never looks one up by index — that is
        // ClientTempEntFrame's route into the registry, not this one's.
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }


    // ---------------------------------------------------------------------
    // The declaration, which is what the mutation proof points at.
    // ---------------------------------------------------------------------

    /// <summary>
    /// <c>DT_TEExplosion</c>'s whole field list as both sides build it — the
    /// wire name, the blob offset, the storage width and the wire width.
    ///
    /// <para>
    /// THIS IS GATE 4'S TARGET FOR THIS LANE. Change one prop's bit width in
    /// <c>TeExplosion.cs</c> and this goes red naming the field; restore it and
    /// it goes green. The explosion is the right target of the seven: it has
    /// eight props of five different shapes, including the one BYTE-sized field
    /// in the subtree (<c>m_chMaterialType</c>) and the only float with an
    /// explicit low/high range.
    /// </para>
    ///
    /// <para>
    /// The offsets follow the base's three floats, so the class's own fields
    /// start at 12 — which is the number that says <c>DT_TEParticleSystem</c> is
    /// nested at datatable offset zero and not appended after.
    /// </para>
    /// </summary>
    [Fact]
    public void TheExplosionDeclarationIsWhatBothSidesBuildFrom()
    {
        var engine = Dry();
        TempEntType boom = Registered(engine).Find("CTEExplosion")!;

        // Offset and STORAGE width, in declaration order. The offsets are the
        // ones a live cs_sv_te_dump measures through the engine, arrived at
        // there by SendTable_InitTable and here by EntityLayout: two roads, one
        // set of numbers.
        (string Name, int Offset, int Bytes)[] expected =
        {
            ("Origin",      0, 4),   // the BASE's: THREE SendPropFloats of
                                          // four bytes each, not one vector
            ("ModelIndex",   12, 4),
            ("Scale",        16, 4),
            ("FrameRate",    20, 4),
            ("Flags",        24, 4),
            ("Normal",     28, 12),  // ONE SendPropVector, unlike the origin
            ("MaterialType", 40, 1),  // the one BYTE-sized field in the subtree,
            ("Radius",       44, 4),   // and the three bytes after it are PADDING:
            ("Magnitude",    48, 4),   // EntityLayout aligns, so 41 is not an offset
        };

        foreach (var (name, offset, bytes) in expected)
        {
            Assert.True(boom.Fields.ContainsKey(name), $"DT_TEExplosion carries {name}");

            var field = boom.Fields[name];

            Assert.True(field.Offset == offset,
                $"{name} is at blob offset {offset} (found {field.Offset})");
            Assert.True(field.SizeBytes == bytes,
                $"{name} is {bytes} byte(s) of storage (found {field.SizeBytes})");
        }

        Assert.True(boom.Fields.Count == expected.Length,
            $"and nothing else: {expected.Length} fields including the base's origin");

        Assert.True(boom.BlobBytes == 52,
            "DT_TEExplosion's blob is twelve base bytes and forty of its own -- three of "
          + "which are the padding after the one-byte material type. A reader that assumed "
          + "packing would find the radius three bytes early and the magnitude with it");

        // THE WIRE WIDTHS, which is the half a blob round trip cannot see: a
        // field can sit at the right offset and still be encoded at the wrong
        // number of bits, and the symptom of that is a bitstream that stops
        // making sense partway through with no log line anywhere.
        Assert.True(boom.Fields["Origin"].Elems == 3, "the origin is three elements");
        Assert.True(boom.Fields["Origin"].ElemStride == 4, "four bytes apart");
        Assert.True(boom.Fields["Origin"].Flags == (TeFlags.Coord | TeFlags.VectorElem),
            "COORD, declared the way SENDINFO_VECTORELEM declares");

        Assert.True(boom.Fields["ModelIndex"].BitsFrom == TeBits.ModelIndex,
            "the model index is SP_MODEL_INDEX_BITS, symbolically -- a literal 13 here would "
          + "be a number that stops tracking the engine's the day it moves");

        var scale = boom.Fields["Scale"];
        Assert.True(scale.Bits == 9, "m_fScale is NINE bits");
        Assert.True(scale.Low == 0.0f && scale.High == 51.2f,
            "over 0..51.2, which is what makes nine bits enough: a range, not a scale factor");

        Assert.True(boom.Fields["FrameRate"].Bits == 8, "the frame rate is eight bits");
        Assert.True(boom.Fields["Flags"].Bits == 8, "and the flags eight");

        Assert.True(boom.Fields["Normal"].Bits == -1
                 && boom.Fields["Normal"].Flags == TeFlags.Coord,
            "the normal is a COORD vector, whose width the flag decides");

        Assert.True(boom.Fields["MaterialType"].Bits == 8,
            "the material type is eight bits and ONE byte of storage: reading four would take "
          + "three bytes of the radius beside it");

        Assert.True(boom.Fields["Radius"].Bits == 32, "the radius is a full 32 bits");
        Assert.True(boom.Fields["Magnitude"].Bits == 32, "and so is the magnitude");
    }

    /// <summary>The dust puff the dust temp entity starts (lane c5-particles: DustPuff), recorded.</summary>
    private sealed class DustPuffRecorder : client::SourceSharp.Client.Effects.IClientDustPuff
    {
        public readonly List<(Vector Origin, Vector Direction, float Size, float Speed)> Calls = [];

        public int Puff(Vector origin, Vector direction, float size, float speed)
        {
            Calls.Add((origin, direction, size, speed));
            return 1;
        }
    }

    /// <summary>The temp entity particles the blood stream and the line start (lane c5-particles: TeParticles), recorded.</summary>
    private sealed class TeParticleRecorder : client::SourceSharp.Client.Effects.IClientTeParticles
    {
        public readonly List<(Vector Origin, Vector Direction, int R, int G, int B, int A, int Amount)> Blood = [];
        public readonly List<(Vector Start, Vector End)> Lines = [];

        public int BloodStream(Vector origin, Vector direction, int r, int g, int b, int a, int amount)
        {
            Blood.Add((origin, direction, r, g, b, a, amount));
            return 1;
        }

        public int ShowLine(Vector start, Vector end)
        {
            Lines.Add((start, end));
            return 1;
        }
    }
}
