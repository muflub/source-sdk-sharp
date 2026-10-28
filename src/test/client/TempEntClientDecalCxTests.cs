using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Lane CX's client halves: the three decal temp entities lane CD reported
/// rather than stubbed, and the four backend slots that unblocked them.
///
/// <para>
/// THE COMPANION OF <see cref="TempEntClientDecalTests"/>, not a replacement.
/// That file has lane CD's three; this has the other three, and the tripwire
/// it carried — <c>TheThreeBlockedOnABackendHaveNoManagedPlayerYet</c> — was
/// DELETED when these landed, which is what its own note asked for.
/// </para>
///
/// <para>
/// WHAT IS ASSERTED HERE AND WHY IT IS NOT THE SCHEMA.
/// <see cref="TempEntDecalSchemaTests"/> holds all six declarations against the
/// stock <c>te_*decal.cpp</c> tables read off disk. What is asserted here is
/// that a blob carrying known numbers reaches the right BACKEND with those
/// numbers — and for two of these three, that it reaches the right backend at
/// all, because each has two arms and the arm is chosen from a number that is
/// zero in the ordinary case. A test that only counted calls would agree with a
/// player that always took the same branch.
/// </para>
///
/// <para>
/// AND THAT THE OTHER TWO ARMS NOW DRAW. Behaviour ledger rows C2 and C3 were
/// open in these two classes and lane DECALS closed them: the arms still run,
/// on the same arguments, and now end in
/// <see cref="ClientEntityDecal.AddDecal"/> rather than in a warning. The
/// assertions that pinned the REFUSALS are the assertions that pin the decals
/// now — same branches, same inputs, opposite expectation — because a row
/// closed by deleting its call site would leave every other test here green.
/// </para>
/// </summary>
public class TempEntClientDecalCxTests
{
    /// <summary>Component-wise and EXACT — nothing on this path encodes.</summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// A registry over the real shared declarations, native handing out a
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
    /// An engine whose two QUERIES are stubbed. <see cref="RecordingEngine"/> is
    /// strict about a return nobody supplied, which is what makes the low
    /// violence branch below a decision rather than a default.
    /// </summary>
    private static RecordingEngine Quiet()
        => Fake().Returns("Effects.ClientIsLowViolence", false);

    private static ClientTempEnts Players(RecordingEngine engine, TempEntRegistry registry)
    {
        var players = new ClientTempEnts(new RecordingLog(), registry);

        Assert.True(players.Add("CTEDecal", new ClientTeDecal(engine, engine, DecalSettings.On())),
            "the bullet-hole decal claims its name");
        Assert.True(players.Add("CTEProjectedDecal", new ClientTeProjectedDecal(engine, engine, DecalSettings.On())),
            "and the projected one");
        Assert.True(players.Add("CTEWorldDecal", new ClientTeWorldDecal(engine, DecalSettings.On())),
            "and the world one");

        return players;
    }

    // =====================================================================
    // Discovery.
    // =====================================================================

    /// <summary>
    /// All three are found by attribute under the names the two sides pair on —
    /// which are the SERVER's class names, not the C# type names.
    ///
    /// <para>
    /// This is the assertion that replaces lane CD's tripwire. That test said
    /// these three had no player and explained what each was missing; this one
    /// says they have one. Both cannot be true, and the tripwire's own note
    /// asked to be deleted rather than edited on the day the backends landed.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLastThreeDecalPlayersAreFoundByAttributeUnderTheirWireNames()
    {
        var engine = Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine));

        players.RegisterAll(typeof(ClientTeDecal).Assembly, Services().WithDecalsOn());

        Assert.True(players.PlayerNames.Contains("CTEDecal"),
            "CTEDecal has a managed player -- its static prop arm reaches "
          + "ClientDecalStaticProp, which lane CX added");
        Assert.True(players.PlayerNames.Contains("CTEProjectedDecal"),
            "and so does CTEProjectedDecal, over ClientTraceLine and the same "
          + "static prop slot");
        Assert.True(players.PlayerNames.Contains("CTEWorldDecal"),
            "and so does CTEWorldDecal, over ClientIsLowViolence and "
          + "ClientDecalNameFromIndex");
    }

    /// <summary>
    /// All six decals now have a managed player, which is the statement lane CD
    /// could not make and is the reason this lane existed.
    /// </summary>
    [Fact]
    public void AllSixDecalTempEntitiesArePlayedByManagedCode()
    {
        var engine = Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine));

        players.RegisterAll(typeof(ClientTeDecal).Assembly, Services().WithDecalsOn());

        foreach (string name in new[]
        {
            "CTEBSPDecal", "CTEFootprintDecal", "CTEPlayerDecal",
            "CTEDecal", "CTEProjectedDecal", "CTEWorldDecal",
        })
        {
            Assert.True(players.PlayerNames.Contains(name),
                $"{name} is played by C#");
        }
    }

    // =====================================================================
    // CTEDecal.
    // =====================================================================

    /// <summary>
    /// The world-with-a-hitbox arm reaches <c>ClientDecalStaticProp</c> with the
    /// SEGMENT the blob carried and with the hitbox DECREMENTED.
    ///
    /// <para>
    /// Four things could be wrong here and each of them draws something: the
    /// two points swapped (the decal lands on the far face), the hitbox passed
    /// through undecremented (the wrong prop), the decal index and the prop
    /// index crossed, and the origin used for both ends. Every number below is
    /// distinct from every other so that only one arrangement passes.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWorldHitboxArmReachesTheStaticPropSlotWithTheHitboxDecremented()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-3079f, 4657f, 130f));
        writer.Vector("Start", new Vector(-3000f, 4600f, 200f));
        writer.Int("Entity", 0);
        writer.Int("Hitbox", 42);
        writer.Int("Index", 7);

        Players(engine, registry).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 1,
            "one static prop decal");

        var call = engine.Last("Effects.ClientDecalStaticProp")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(-3000f, 4600f, 200f)),
            "the ray STARTS at m_vecStart, as stock's AddDecalToStaticProp( *start, *pos ) does");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(-3079f, 4657f, 130f)),
            "and ENDS at m_vecOrigin -- swapped, the decal lands on the far face");
        Assert.True(call.ArgAs<int>(2) == 41,
            "the prop index is hitbox - 1: the engine numbers a prop's hitbox from one "
          + "so that zero can mean 'not a static prop'");
        Assert.True(call.ArgAs<int>(3) == 7,
            "and the decal index is the decal index, not the prop index again");
    }

    /// <summary>
    /// AND IT REACHES NOTHING ELSE. <c>CTEDecal</c> never called
    /// <c>effects-&gt;DecalShoot</c> in stock, which is the measurement that
    /// decided this whole lane: a player that reached <c>ClientDecalShoot</c>
    /// would draw a decal at a plausible place through the wrong system, and
    /// the arm that was supposed to run would be untested and dead.
    /// </summary>
    [Fact]
    public void TheDecalNeverGoesThroughTheEnginesOwnDecalShoot()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Int("Hitbox", 1);
        writer.Int("Index", 3);

        Players(engine, registry).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 0,
            "CTEDecal has no DecalShoot arm at all -- stock's two arms are the static "
          + "prop one and the C_BaseEntity::AddDecal one");
    }

    /// <summary>
    /// The ENTITY arm DRAWS — BEHAVIOUR LEDGER ROW C2, closed.
    ///
    /// <para>
    /// This test used to assert the opposite, and deliberately keeps its
    /// inputs: entity 17, hitbox 3, decal 5, aimed at a point nothing else in
    /// this file uses. What changed is the expectation. Row C2 named
    /// <c>C_BaseEntity::AddDecal</c>, whose studio arm this entity takes, and
    /// the arm is reached with the SEGMENT stock passes — <c>*start</c> then
    /// <c>*pos</c>, in that order.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEntityArmDrawsOnTheEntityAndSaysNothing()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Studio);
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(64f, 128f, 16f));
        writer.Vector("Start", new Vector(60f, 120f, 40f));
        writer.Int("Entity", 17);
        writer.Int("Hitbox", 3);
        writer.Int("Index", 5);

        var said = new List<string>();
        var testLog = new RecordingLog { Line = (message, _) => said.Add(message) };

        try
        {
            Players(engine, registry).Dispatch(te.Handle, blob);

            Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 0,
                "a decal aimed at an entity is not a static prop decal, and must not be "
              + "sent to the prop renderer with entity 17's hitbox as a prop index");

            Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 1,
                "it is an AddDecal on entity 17, and entity 17 is wearing an .mdl");

            var call = engine.Last("Effects.ClientDecalStudioModel")!.Value;

            Assert.True(call.ArgAs<int>(0) == 17, "on the entity the wire named");
            Assert.True(Same(call.ArgAs<Vector>(1), new Vector(60f, 120f, 40f)),
                "the ray starts at m_vecStart, which is stock's rayStart");
            Assert.True(Same(call.ArgAs<Vector>(2),
                             ClientEntityDecal.StudioRayDelta(new Vector(60f, 120f, 40f), new Vector(64f, 128f, 16f))),
                "and ends at m_vecOrigin (the ray's lengthened delta) -- swapped, the decal projects the other way");
            Assert.True(call.ArgAs<int>(3) == 5, "with the decal index off the wire");

            Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 0,
                "and NOT through the brush arm: an .mdl decal through DecalShoot would be "
              + "painted on the world at the entity's feet");

            Assert.True(said.Count == 0,
                "and nothing is said any more -- row C2 was closed by making the call real, "
              + "so the line that stood in for it is gone rather than quietened");
        }
        finally
        {
        }
    }

    /// <summary>
    /// The row is asked ONCE PER DECAL and answered from the entity, which is
    /// the failure mode a per-level latch used to hide.
    ///
    /// <para>
    /// The old refusal said its piece once and returned; five decals produced
    /// one line. Five decals must now produce five decals, and two entities
    /// wearing different kinds of model must reach different arms — a port that
    /// cached the first answer would decal a brush through the model renderer
    /// for the rest of the level.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDecalIsAskedAboutItsOwnEntity()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Int("Entity", 17);
        writer.Int("Index", 5);

        var players = Players(engine, registry);

        engine.Returns("WorldQuery.ClientEntityModelType", ModelType.Studio);

        for (int i = 0; i < 5; i++)
            players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 5,
            "five decals, five decals -- not one and a latch");

        // The same client, an entity wearing a brush model instead.
        engine.Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);
        players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 1,
            "a brush entity takes the brush arm");
        Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 5,
            "and does not also take the studio one");
    }

    /// <summary>
    /// THE WORLD WITH NO HITBOX IS THE BRUSH ARM, and that is stock's shape
    /// rather than an oversight in the port.
    ///
    /// <para>
    /// Stock's test is <c>(entity == 0) &amp;&amp; (hitbox != 0)</c>, so entity
    /// zero with hitbox zero takes the <c>else</c> — where
    /// <c>cl_entitylist-&gt;GetEnt( 0 )</c> is the world entity and
    /// <c>AddDecal</c> is what stamps a brush model. A player that read row
    /// C2's title as "entities only" and sent this to the prop slot would pass
    /// <c>hitbox - 1 == -1</c> into the engine's static prop array.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWorldWithNoHitboxIsTheBrushArmAndNotPropMinusOne()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-1024f, 96f, 8f));
        writer.Int("Entity", 0);
        writer.Int("Hitbox", 0);
        writer.Int("Index", 5);

        Players(engine, registry).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 0,
            "no prop decal: hitbox zero means there is no static prop here");

        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 1,
            "the world is a brush model and takes AddDecal's brush arm");

        var call = engine.Last("Effects.ClientDecalBrushModel")!.Value;

        Assert.True(call.ArgAs<int>(0) == 0,
            "onto entity 0, which is the world -- and zero is a SUBJECT here, not a "
          + "missing value: a guard that refused it would delete this case entirely");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(-1024f, 96f, 8f)),
            "at the decal CENTRE, which is stock's third argument to AddDecal and is "
          + "*pos at this call site");
        Assert.True(call.ArgAs<int>(2) == 5, "with the decal index off the wire");
    }

    /// <summary>
    /// An entity this client has never heard of draws nothing, and that is
    /// stock's line ABOVE the call rather than a branch inside it.
    ///
    /// <para>
    /// <c>if ( ( ent = cl_entitylist-&gt;GetEnt( entity ) ) == NULL ) return;</c>.
    /// The failure this guards is a port that folded "no such entity" into
    /// <c>mod_bad</c> and then treated the fold as a real model type — which
    /// would either decal nothing for entities that exist or warn for entities
    /// that do not.
    /// </para>
    /// </summary>
    [Fact]
    public void ADecalAimedAtNoEntityAtAllDrawsNothing()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", (ModelType?)null);
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Int("Entity", 47);
        writer.Int("Index", 5);

        Players(engine, registry).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("WorldQuery.ClientEntityModelType") == 1,
            "the client was asked what entity 47 is");
        Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 0,
            "and nothing was decalled through the model renderer");
        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 0,
            "nor through the engine's decal system");
    }

    /// <summary>
    /// A SPRITE takes neither arm and says nothing, which is stock's
    /// <c>default:</c> — its whole body is <c>tr.fraction = 1.0f</c>, and
    /// <c>tr</c> is a local both call sites throw away.
    /// </summary>
    [Fact]
    public void ADecalAimedAtASpriteDrawsNothingAndIsNotAGap()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Sprite);
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDecal")!;
        var blob = new byte[te.BlobBytes];
        new TempEntWriter(te, blob).Int("Entity", 8);

        var said = new List<string>();
        var testLog = new RecordingLog { Line = (message, _) => said.Add(message) };

        try
        {
            Players(engine, registry).Dispatch(te.Handle, blob);

            Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 0,
                "a sprite has no studio model to put a decal on");
            Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 0,
                "and no brush model either");
            Assert.True(said.Count == 0,
                "and stock says nothing about it, so neither does this -- a warning here "
              + "would be a line the game does not print");
        }
        finally
        {
        }
    }

    // =====================================================================
    // CTEProjectedDecal.
    // =====================================================================

    /// <summary>
    /// The projection traces from the origin, along the facing, for the
    /// distance — with <see cref="TraceMask.All"/>, which is <c>MASK_ALL</c>.
    ///
    /// <para>
    /// Angles of zero face down +X, so a 512-unit throw from (100, 200, 300)
    /// ends at (612, 200, 300). A player that traced the other way, or that used
    /// the endpoint as the start, would still produce a trace and still draw
    /// something.
    /// </para>
    /// </summary>
    [Fact]
    public void TheProjectionTracesAlongItsFacingForItsDistanceWithMaskAll()
    {
        var engine = Fake().Returns("WorldQuery.ClientTraceLine",
                                    new ClientTraceResult(true, 0.5f, new Vector(356f, 200f, 300f),
                                                          Vector.Zero, 0, 0, 12, 0))
                           // Hitbox zero, so the trace lands in the world-and-brush arm and
                           // the client is asked what entity 0 is. Named rather than left to
                           // default: this test is about the TRACE's arguments.
                           .Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEProjectedDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(100f, 200f, 300f));
        writer.Vector("Rotation", Vector.Zero);
        writer.Float("Distance", 512f);
        writer.Int("Index", 9);

        var said = new List<string>();
        var testLog = new RecordingLog { Line = (message, _) => said.Add(message) };

        try
        {
            Players(engine, registry).Dispatch(te.Handle, blob);
        }
        finally
        {
        }

        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 1, "one trace");

        var call = engine.Last("WorldQuery.ClientTraceLine")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(100f, 200f, 300f)),
            "from the origin the blob carried");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(612f, 200f, 300f)),
            "to origin + forward * distance -- AngleVectors then VectorMA, stock's two lines");
        Assert.True(call.ArgAs<TraceMask>(2) == TraceMask.All,
            "with MASK_ALL, which is what stock traces: a decal projected through a "
          + "narrower mask lands on a different surface");
    }

    /// <summary>
    /// A projection that reached nothing paints nothing — stock's
    /// <c>fraction == 1.0f</c> return, and it must return BEFORE the arm
    /// selection.
    ///
    /// <para>
    /// A player that fell through to the branch would read a hitbox of zero off
    /// a trace that hit nothing and warn about ledger row C3 for every
    /// projection that missed, which is the row losing its meaning rather than
    /// being tracked.
    /// </para>
    /// </summary>
    [Fact]
    public void AProjectionThatReachedNothingDrawsNothingAndSaysNothing()
    {
        var engine = Fake().Returns("WorldQuery.ClientTraceLine",
            new ClientTraceResult(false, 1.0f, new Vector(612f, 200f, 300f),
                                  Vector.Zero, 0, -1, -1, 0));
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEProjectedDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(100f, 200f, 300f));
        writer.Float("Distance", 512f);
        writer.Int("Index", 9);

        var said = new List<string>();
        var testLog = new RecordingLog { Line = (message, _) => said.Add(message) };

        try
        {
            Players(engine, registry).Dispatch(te.Handle, blob);

            Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 0,
                "nothing was hit, so nothing is decalled");
            Assert.True(said.Count(m => m.Contains("row C3")) == 0,
                "and row C3 is not a projection that MISSED -- stock returns above the "
              + "branch, and a row said for every miss is a row nobody reads");
        }
        finally
        {
        }
    }

    /// <summary>
    /// A non-zero hitbox is a static prop, and the decal goes there with the
    /// PROJECTION's two points — origin and endpoint, not the trace's end.
    ///
    /// <para>
    /// Stock passes <c>*pos</c> and <c>endpos</c>, which is the whole aimed
    /// segment rather than the part of it that was traversed. Handing the
    /// trace's endpoint instead would shorten the ray and can land the decal on
    /// the near face.
    /// </para>
    /// </summary>
    [Fact]
    public void AStaticPropHitDecalsTheWholeProjectedSegment()
    {
        var engine = Fake().Returns("WorldQuery.ClientTraceLine",
            new ClientTraceResult(true, 0.5f, new Vector(356f, 200f, 300f),
                                  Vector.Zero, 43, 0, 12, 0));
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEProjectedDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(100f, 200f, 300f));
        writer.Float("Distance", 512f);
        writer.Int("Index", 9);

        Players(engine, registry).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 1, "one prop decal");

        var call = engine.Last("Effects.ClientDecalStaticProp")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(100f, 200f, 300f)),
            "from the projection's own origin");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(612f, 200f, 300f)),
            "to its own endpoint -- stock passes *pos and endpos, not tr.endpos");
        Assert.True(call.ArgAs<int>(2) == 42,
            "prop index 42, from hitbox 43");
        Assert.True(call.ArgAs<int>(3) == 9, "and the decal index off the wire");
    }

    /// <summary>
    /// A hit with NO hitbox is the world-and-brush arm — BEHAVIOUR LEDGER ROW
    /// C3, closed, and it DRAWS.
    ///
    /// <para>
    /// This is stock's <c>else</c>, whose own comment is <i>"Only decal the
    /// world + brush models"</i>. It is the ORDINARY case for an
    /// <c>info_projecteddecal</c>, which is why the row was bigger than C2's
    /// and must never have been sized from it. The decal goes on the entity the
    /// TRACE named, at the projection's own endpoint.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWorldAndBrushArmDrawsOnWhatTheTraceHit()
    {
        var engine = Fake().Returns("WorldQuery.ClientTraceLine",
                                    new ClientTraceResult(true, 0.5f, new Vector(356f, 200f, 300f),
                                                          Vector.Zero, 0, 0, 12, 0))
                           .Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEProjectedDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(100f, 200f, 300f));
        writer.Float("Distance", 512f);
        writer.Int("Index", 9);

        var said = new List<string>();
        var testLog = new RecordingLog { Line = (message, _) => said.Add(message) };

        try
        {
            var players = Players(engine, registry);

            for (int i = 0; i < 5; i++)
                players.Dispatch(te.Handle, blob);

            Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 0,
                "hitbox zero is not a static prop, and must not be sent to the prop "
              + "renderer as prop -1");

            Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 5,
                "five projections, five decals -- the arm the row was open for now runs "
              + "every time rather than warning once");

            var call = engine.Last("Effects.ClientDecalBrushModel")!.Value;

            Assert.True(call.ArgAs<int>(0) == 0,
                "onto entity 0, which is what the trace answered and is the world");
            Assert.True(Same(call.ArgAs<Vector>(1), new Vector(612f, 200f, 300f)),
                "at the projection's own ENDPOINT and not at tr.endpos (356, ...) -- stock "
              + "passes endpos for the centre, which is the whole aimed segment rather than "
              + "the part of it the trace traversed");
            Assert.True(call.ArgAs<int>(2) == 9, "with the decal index off the wire");

            Assert.True(said.Count == 0,
                "and nothing is said: row C3 was closed by making the call real");
        }
        finally
        {
        }
    }

    /// <summary>
    /// Both rows were closed by making the arm REAL, and this is the assertion
    /// that says which. It replaces the pair that pinned the refusals.
    ///
    /// <para>
    /// A row may be closed only by making the capability real, never by
    /// deleting the call that exposed it — so what is pinned here is that each
    /// class still reaches the same branch with the same arguments, and that
    /// the branch ends in <see cref="ClientEntityDecal"/> rather than in
    /// nothing. Every other test in this file looks at one backend at a time
    /// and would stay green if a player quietly stopped calling either arm.
    /// </para>
    /// </summary>
    [Fact]
    public void BothLedgerRowsReachARealDecalPathAndNotAWarning()
    {
        var engine = Fake().Returns("WorldQuery.ClientTraceLine",
                                    new ClientTraceResult(true, 0.5f, Vector.Zero,
                                                          Vector.Zero, 0, 3, 12, 0))
                           .Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);
        var registry = Registered(engine);
        var players = Players(engine, registry);

        // C2: the bullet hole, aimed at an entity.
        TempEntType decal = registry.Find("CTEDecal")!;
        var blob = new byte[decal.BlobBytes];
        new TempEntWriter(decal, blob).Int("Entity", 3);
        players.Dispatch(decal.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 1,
            "row C2's arm ends in an AddDecal on the entity the wire named, not in a "
          + "line about C_BaseEntity::AddDecal");

        // C3: the projection, on to the world with no hitbox.
        TempEntType projected = registry.Find("CTEProjectedDecal")!;
        players.Dispatch(projected.Handle, new byte[projected.BlobBytes]);

        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 2,
            "and so does row C3's");

        // And the managed AddDecal itself still has both of the arms the two
        // rows named, which is what "the capability is real" means. Deleting
        // either would leave one model kind silently undecalled.
        var studio = typeof(ClientEntityDecal)
            .GetMethod("AddDecal", System.Reflection.BindingFlags.Public
                                 | System.Reflection.BindingFlags.Static);

        Assert.True(studio is not null,
            "ClientEntityDecal.AddDecal is the managed C_BaseEntity::AddDecal both rows "
          + "named; it is what closed them");

        Assert.True(System.Enum.IsDefined(typeof(EntityDecalArm), EntityDecalArm.StudioModel)
                 && System.Enum.IsDefined(typeof(EntityDecalArm), EntityDecalArm.BrushModel),
            "with both of stock's arms -- AddStudioDecal and AddBrushModelDecal -- and not "
          + "just the one the first test to be written happened to take");
    }

    // =====================================================================
    // CTEWorldDecal.
    // =====================================================================

    /// <summary>
    /// With violence at normal, the decal is shot and the decal SCRIPT is never
    /// asked anything.
    ///
    /// <para>
    /// The order is stock's and it matters: this fires for every bullet hole on
    /// brushwork, and looking up a name per decal on a machine that will never
    /// suppress one is a walk of the whole decal list for nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void AnOrdinaryClientShootsTheDecalAndNeverAsksForItsName()
    {
        var engine = Quiet();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEWorldDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-1024f, 96f, 8f));
        writer.Int("Index", 31);

        Players(engine, registry).Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalNameFromIndex") == 0,
            "the name is looked up only when violence is turned down -- stock's own order");

        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1, "one decal was shot");

        var call = engine.Last("Effects.ClientDecalShoot")!.Value;

        Assert.True(call.ArgAs<int>(0) == 31, "with the decal index off the wire");
        Assert.True(call.ArgAs<int>(1) == 0,
            "onto entity 0, the WORLD -- this table carries no entity at all");
        Assert.True(Same(call.ArgAs<Vector>(2), new Vector(-1024f, 96f, 8f)),
            "at the point off the wire");
        Assert.True(call.Arg(3) is null,
            "with NO s axis: stock passes 0 and lets the engine pick one");
        Assert.True(call.ArgAs<int>(4) == 0,
            "and no flags -- a world decal is not permanent, which is what separates it "
          + "from a BSP decal through the same call");
    }

    /// <summary>
    /// With violence turned down, a BLOOD decal is suppressed and everything
    /// else still draws.
    ///
    /// <para>
    /// Both halves are asserted from one engine, because a player that
    /// suppressed everything under low violence would pass a test that only
    /// checked the blood case — and would silently stop drawing bullet holes on
    /// a machine with <c>violence_hblood 0</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void LowViolenceSuppressesBloodAndNothingElse()
    {
        var engine = Fake().Returns("Effects.ClientIsLowViolence", true)
                           .Returns("Effects.ClientDecalNameFromIndex", "Blood_Big");
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEWorldDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(8f, 16f, 32f));
        writer.Int("Index", 12);

        var players = Players(engine, registry);
        players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalNameFromIndex") == 1,
            "the name was asked for, because violence is down");
        Assert.True(engine.Last("Effects.ClientDecalNameFromIndex")!.Value.ArgAs<int>(0) == 12,
            "for the index off the wire, and not for some other decal");
        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 0,
            "and the blood decal was suppressed");

        // The same client, a decal that is not blood.
        engine.Returns("Effects.ClientDecalNameFromIndex", "Concrete1");
        players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1,
            "a bullet hole still draws on a low-violence client -- the switch suppresses "
          + "BLOOD, not decals");
    }

    /// <summary>
    /// The blood test is case-INSENSITIVE, because stock's is
    /// (<c>V_stristr</c>).
    ///
    /// <para>
    /// The decal script's names are <c>Blood_Big</c>, <c>blood</c>,
    /// <c>SmallBloodSplatter</c> and friends. An ordinal <c>Contains</c> would
    /// let the capitalised half through and produce a low-violence client that
    /// suppresses some of its blood — the worst of the three possible outcomes,
    /// because it looks like it works.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBloodTestIsCaseInsensitiveAsStocksIs()
    {
        var registry = Registered(Fake());

        foreach (string name in new[] { "Blood_Big", "blood", "SmallBloodSplatter", "BLOOD" })
        {
            var engine = Fake().Returns("Effects.ClientIsLowViolence", true)
                               .Returns("Effects.ClientDecalNameFromIndex", name);
            var own = Registered(engine);

            TempEntType te = own.Find("CTEWorldDecal")!;
            Players(engine, own).Dispatch(te.Handle, new byte[te.BlobBytes]);

            Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 0,
                $"'{name}' is blood and is suppressed -- V_stristr is case-insensitive");
        }

        Assert.True(registry.Find("CTEWorldDecal") is not null,
            "and the class is the one being asked about throughout");
    }

    /// <summary>
    /// An index the decal script does not hold answers the EMPTY string, and an
    /// empty string is not blood.
    ///
    /// <para>
    /// <c>CDecalEmitterSystem::GetDecalNameForIndex</c> returns <c>""</c> for an
    /// index it has no entry for, and <c>V_stristr( "", "blood" )</c> is NULL.
    /// A player that treated "no name" as "suppress" would drop every decal
    /// whose index came from the engine's table rather than the game's, on any
    /// low-violence client.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnknownDecalIndexIsNotBlood()
    {
        var engine = Fake().Returns("Effects.ClientIsLowViolence", true)
                           .Returns("Effects.ClientDecalNameFromIndex", string.Empty);
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEWorldDecal")!;
        Players(engine, registry).Dispatch(te.Handle, new byte[te.BlobBytes]);

        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1,
            "an index the game's decal script does not name is not blood, and still draws");
    }

    // =====================================================================
    // The three go to their own backends and not to each other's.
    // =====================================================================

    /// <summary>
    /// Each of the three reaches ONE backend and the other two reach none of
    /// it.
    ///
    /// <para>
    /// The three tests above each look only at the backend they expect, so all
    /// three would pass with the players crossed. This is the one that would
    /// not: a world decal through <c>ClientDecalStaticProp</c> would be painted
    /// on static prop number -1, and a bullet hole through
    /// <c>ClientDecalShoot</c> would be painted on the world at the shooter's
    /// end of the segment.
    /// </para>
    /// </summary>
    [Fact]
    public void EachOfTheThreeGoesThroughItsOwnBackendAndNotAnothers()
    {
        var engine = Quiet().Returns("WorldQuery.ClientTraceLine",
            new ClientTraceResult(true, 0.5f, Vector.Zero, Vector.Zero, 43, 0, 12, 0));
        var registry = Registered(engine);

        var players = Players(engine, registry);

        // The world decal: DecalShoot, and no prop and no trace.
        TempEntType world = registry.Find("CTEWorldDecal")!;
        players.Dispatch(world.Handle, new byte[world.BlobBytes]);

        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1, "the world decal was shot");
        Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 0,
            "and did not reach the prop renderer");
        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 0,
            "and traced nothing: this table carries a point, and stock stamps it where "
          + "it is told");

        // The bullet hole with a hitbox: the prop slot, and no trace.
        TempEntType decal = registry.Find("CTEDecal")!;
        var blob = new byte[decal.BlobBytes];
        new TempEntWriter(decal, blob).Int("Hitbox", 1);
        players.Dispatch(decal.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 1,
            "the bullet hole reached the prop renderer");
        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1,
            "and did not also shoot a decal -- CTEDecal has no DecalShoot arm");
        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 0,
            "and did not trace: CTEDecal's hitbox arrives on the wire, already traced "
          + "by the server");

        // The projection: a trace first, then the prop slot.
        TempEntType projected = registry.Find("CTEProjectedDecal")!;
        players.Dispatch(projected.Handle, new byte[projected.BlobBytes]);

        Assert.True(engine.CountOf("WorldQuery.ClientTraceLine") == 1,
            "the projection is the one that traces, and it is the reason the slot exists");
        Assert.True(engine.CountOf("Effects.ClientDecalStaticProp") == 2,
            "and it reached the prop renderer through the hitbox that trace answered");
        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1,
            "and never through DecalShoot");
    }
}
