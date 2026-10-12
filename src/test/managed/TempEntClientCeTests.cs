extern alias client;
using System;
using System.Linq;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Lane CE's client halves: the six temp entities whose whole
/// <c>PostDataUpdate</c> now lives in C#.
///
/// <para>
/// THESE ARE NOT THE SCHEMA TESTS. <see cref="TempEntDebrisTests"/>,
/// <see cref="TempEntEffectDataTests"/> and their siblings check the shared
/// declarations against the stock <c>te_*.cpp</c> tables read off disk, which is
/// the only check that can catch a wrong-but-consistent width
/// (docs/te-lane-contract.md's first "thing a green dump cannot tell you").
/// What is asserted here is the OTHER half — that a blob carrying known numbers
/// reaches the drawing primitive as those numbers, through the reader, the
/// router and the player.
/// </para>
///
/// <para>
/// EVERY ONE ASSERTS THE ARGUMENTS AND NOT ONLY THE CALL, for lane CD's reason:
/// a player that called <c>ClientSmoke</c> with three zeros would satisfy "one
/// puff was drawn", and a puff at the world origin is a plausible-looking value
/// rather than an obviously broken one — which is precisely the failure 2E-bis
/// records from a live join, where a spark fired at (-2932, 3218, 60) decoded to
/// (0,0,0) and the counters said "1 played, 0 dropped" throughout.
/// </para>
///
/// <para>
/// TWO OF THE SIX CARRY MORE THAN A DECODE AND HAVE MORE TESTS FOR IT.
/// <c>CTEEffectDispatch</c> is the only temp entity in the inventory with a
/// nested datatable that has PROPS in it, and it is the one place in the port
/// where a wrong number plays the WRONG effect rather than none — so it is
/// checked through the real server writer, end to end.
/// <c>CTEShatterSurface</c> is the one drawing path in the lane that needed a
/// backend slot of its own, and its six colour bytes are six separate props
/// that look interchangeable.
/// </para>
///
/// <para>
/// AND ONE OF THE SIX DRAWS NOTHING, WHICH IS ALSO ASSERTED.
/// <c>C_TEImpact::PostDataUpdate</c> is a <c>VPROF</c> and nothing else in
/// stock, so <see cref="ClientTeImpact"/> reaching ANY engine member would be
/// this port inventing behaviour — the mirror image of closing a ledger row by
/// deleting the call that exposed it.
/// </para>
/// </summary>
public class TempEntClientCeTests
{
    /// <summary>
    /// Component-wise and EXACT. Nothing on this path encodes — the COORD
    /// rounding is the engine's and no test here reaches it — so a byte that
    /// moved is a byte that moved.
    /// </summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// A registry over the real shared declarations, native handing out a
    /// DISTINCT handle per registration as it does.
    /// </summary>
    private static TempEntRegistry Registered(RecordingEngine engine, int handle = 5)
    {
        int next = handle;

        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        return registry;
    }

    // =====================================================================
    // Discovery.
    // =====================================================================

    /// <summary>
    /// All six players are found by attribute, under the names the two sides
    /// pair on.
    ///
    /// <para>
    /// BY THE WIRE NAME AND NOT THE C# ONE, and two of these are exactly where
    /// that goes wrong: <see cref="ClientTeShatterSurface"/> plays
    /// <c>"CTEShatterSurface"</c> though its stock file is
    /// <c>c_te_glassshatter.cpp</c>, and <see cref="ClientTeImpact"/> plays
    /// <c>"CTEImpact"</c>, which is a different thing from the
    /// <c>DispatchEffect("Impact")</c> NAME that ledger row C7 was about.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSixCePlayersAreFoundByAttributeUnderTheirWireNames()
    {
        var engine = Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine));

        // CTEEffectDispatch's player takes the managed dispatch table; the
        // module registers it before the players are built, and so does this.
        var services = Services();
        services.AddInstance<SourceSharp.Client.Interop.IClientEffectDispatch>(new RecordingEffectDispatch());
        // And the shatter's shards (ShardEffects), likewise.
        services.AddInstance<IClientShatter>(new EngineShatter(engine));
        players.RegisterAll(typeof(ClientTeSmoke).Assembly, services);

        foreach (string wireName in new[]
        {
            "CTESmoke", "CTEMuzzleFlash", "CTEDynamicLight",
            "CTEImpact", "CTEEffectDispatch", "CTEShatterSurface",
        })
        {
            Assert.True(players.PlayerNames.Contains(wireName),
                $"{wireName} has a managed player, discovered by [ClientTempEnt] and by "
              + "nothing else — there is no list file for a lane to conflict over");
        }
    }

    // =====================================================================
    // CTESmoke.
    // =====================================================================

    /// <summary>
    /// A smoke puff reaches the engine with the numbers the blob carried, and
    /// with Valve's times-ten applied exactly once.
    /// </summary>
    [Fact]
    public void TheSmokeReachesTheEngineWithItsScaleMultipliedByTen()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTESmoke")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-1024f, 512f, 96f));
        writer.Int("ModelIndex", 61);
        writer.Float("Scale", 2.5f);
        writer.Int("FrameRate", 12);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTESmoke", new ClientTeSmoke(engine));

        players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientSmoke") == 1, "one puff was drawn");

        var call = engine.Last("Effects.ClientSmoke")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(-1024f, 512f, 96f)),
            "at the point off the wire, and not at the origin");
        Assert.True(call.ArgAs<int>(1) == 61, "with the sprite index off the wire");
        Assert.True(call.ArgAs<float>(2) == 25f,
            "and a scale of TWENTY-FIVE for a wire value of 2.5 — Valve's own "
          + "\"the number passed down is 10 times smaller\", applied in the decode where "
          + "stock applies it and not in the backend, so the ABI's flScale means what "
          + "every other flScale on that boundary means");
        Assert.True(call.ArgAs<float>(3) == 12f,
            "the frame rate, widened from the eight-bit int the table declares to the float "
          + "IEffects::Smoke takes — stock's own widening");
    }

    /// <summary>
    /// The multiply is applied ONCE and it is <see cref="ClientTeSmoke"/>'s
    /// constant, not a literal that could drift from it.
    /// </summary>
    [Fact]
    public void TheSmokeScaleFactorIsTheOneTheClassDeclares()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTESmoke")!;
        var blob = new byte[te.BlobBytes];
        new TempEntWriter(te, blob).Float("Scale", 1f);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTESmoke", new ClientTeSmoke(engine));
        players.Dispatch(te.Handle, blob);

        Assert.True(engine.Last("Effects.ClientSmoke")!.Value.ArgAs<float>(2)
                    == ClientTeSmoke.ScaleFromWire,
            "a wire scale of one draws at exactly ScaleFromWire, so the constant and the "
          + "behaviour cannot drift apart");
    }

    // =====================================================================
    // CTEMuzzleFlash.
    // =====================================================================

    /// <summary>
    /// A muzzle flash reaches the engine with its origin, its angles, its scale
    /// and — unlike the client-side <c>TE_MuzzleFlash</c> — the TYPE off the
    /// wire.
    /// </summary>
    [Fact]
    public void TheMuzzleFlashCarriesTheTypeOffTheWireAndNotAZero()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEMuzzleFlash")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(200f, -80f, 40f));
        writer.Vector("Angles", new Vector(-45f, 90f, 0f));
        writer.Float("Scale", 1.75f);
        writer.Int("Type", 3);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEMuzzleFlash", new ClientTeMuzzleFlash(engine));

        players.Dispatch(te.Handle, blob);

        var call = engine.Last("Effects.ClientMuzzleFlash")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(200f, -80f, 40f)),
            "at the point off the wire");
        Assert.True(call.ArgAs<float>(2) == 1.75f,
            "with the whole float the SPROP_NOSCALE prop carried");
        Assert.True(call.ArgAs<int>(3) == 3,
            "and the TYPE off the wire. Stock's client-side TE_MuzzleFlash passes a literal "
          + "zero and its PostDataUpdate passes m_nType; Valve's //FIXME sits over the second "
          + "of those, so the two stay different and this is the second one");
    }

    /// <summary>
    /// A NEGATIVE pitch survives, which is the whole of why
    /// <see cref="TeMuzzleFlash"/>'s angles are a <c>SendPropVector</c> rather
    /// than <c>SendPropQAngles</c>.
    ///
    /// <para>
    /// A player that treated them as angles would run <c>anglemod</c> and turn
    /// -45 into 315 — a plausible number, pointing the flash the other way,
    /// with nothing anywhere saying so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMuzzleFlashAnglesAreNotReRangedIntoZeroToThreeSixty()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEMuzzleFlash")!;

        Assert.True(te.Fields["Angles"].Kind == TePropKind.Vector,
            "the prop is a plain COORD vector, as stock's SendPropVector over a "
          + "CNetworkQAngle is");

        var blob = new byte[te.BlobBytes];
        new TempEntWriter(te, blob).Vector("Angles", new Vector(-45f, -90f, -12.5f));

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEMuzzleFlash", new ClientTeMuzzleFlash(engine));
        players.Dispatch(te.Handle, blob);

        Assert.True(Same(engine.Last("Effects.ClientMuzzleFlash")!.Value.ArgAs<Vector>(1),
                         new Vector(-45f, -90f, -12.5f)),
            "and all three components arrive negative rather than wrapped into [0,360]");
    }

    // =====================================================================
    // CTEImpact — the one that draws nothing.
    // =====================================================================

    /// <summary>
    /// <b>THE ASSERTION IS THAT NOTHING WAS DRAWN.</b> Stock's whole
    /// <c>C_TEImpact::PostDataUpdate</c> is a <c>VPROF</c>; its
    /// <c>Precache</c> is a <c>//TODO</c> and nothing; <c>PlayImpactSound</c>
    /// and <c>PerformCustomEffects</c> are empty. A managed player that reached
    /// <c>ImpactEffect</c> or <c>ClientDispatchEffect</c> would look like the
    /// port finishing Valve's job, and this port is not allowed to add
    /// behaviour any more than it is allowed to remove it.
    /// </summary>
    [Fact]
    public void TheImpactDecodesEverythingAndDrawsNothingBecauseStockDrawsNothing()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEImpact")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-2932f, 3218f, 60f));
        writer.Vector("Normal", new Vector(0f, 0f, 1f));
        writer.Int("Type", 7);

        var impact = new ClientTeImpact();
        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEImpact", impact);

        int before = engine.Calls.Count;

        players.Dispatch(te.Handle, blob);

        Assert.True(engine.Calls.Count == before,
            "an impact reaches the engine NOWHERE — not one call, because stock's client half "
          + "has been empty since it was written");

        Assert.True(players.Played == 1 && players.Dropped == 0,
            "and it still counts as PLAYED rather than DROPPED, which is the distinction "
          + "between an effect that does nothing and a lane that never landed — they look the "
          + "same from cs_cl_te_report otherwise");

        Assert.True(impact.Received == 1, "the player saw it");
        Assert.True(Same(impact.Origin, new Vector(-2932f, 3218f, 60f)),
            "and decoded the point, at the coordinates 2E-bis' live join used");
        Assert.True(Same(impact.Normal, new Vector(0f, 0f, 1f)), "and the normal");
        Assert.True(impact.Type == 7, "and the surface type");
    }

    /// <summary>
    /// <c>m_ucFlags</c> is in stock's RECV table and in no send table anywhere,
    /// so the managed declaration must not have it.
    ///
    /// <para>
    /// Adding it would make this build's wire differ from stock's for a value
    /// nothing reads — and a reader who found the recv prop and "fixed" the
    /// asymmetry would be doing exactly that. Asserted as an ABSENCE, which is
    /// the only way an absence stays deliberate.
    /// </para>
    /// </summary>
    [Fact]
    public void TheImpactHasNoFlagsFieldBecauseStocksSendTableHasNone()
    {
        var engine = Fake();
        TempEntType te = Registered(engine).Find("CTEImpact")!;

        Assert.True(!te.Fields.ContainsKey("m_ucFlags"),
            "DT_TEImpact declares three props and c_te_impact.cpp receives four; the fourth "
          + "has never had a sender, so C_TEImpact::m_ucFlags has always kept its "
          + "constructor's zero. The engine matches props by NAME and a recv prop with no "
          + "sender is never written");

        Assert.True(te.Fields.Count == 3,
            "three fields and no more: m_vecOrigin, m_vecNormal, m_iType");
    }

    // =====================================================================
    // CTEDynamicLight.
    // =====================================================================

    /// <summary>
    /// The light reaches the pool with everything the wire carried, at the temp
    /// entity's shared key.
    /// </summary>
    [Fact]
    public void TheDynamicLightReachesThePoolAtTheTempEntityKey()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDynamicLight")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(64f, -128f, 72f));
        writer.Int("R", 255);
        writer.Int("G", 200);
        writer.Int("B", 63);
        writer.Int("Exponent", 5);
        writer.Float("Radius", 384f);
        writer.Float("Time", 1.5f);
        writer.Float("Decay", 256f);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEDynamicLight", new ClientTeDynamicLight(engine));

        players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Visibility.DynamicLightUpdate") == 1, "one light was asked for");

        var light = engine.Last("Visibility.DynamicLightUpdate")!.Value.ArgAs<DynamicLight>(0);

        Assert.True(light.Key == ClientTeDynamicLight.TeDynamicLightKey,
            "at LIGHT_INDEX_TE_DYNAMIC, which every temp entity light in the build shares so "
          + "that a firefight MOVES one light rather than exhausting a fixed pool");
        Assert.True(light.IsElight == 0,
            "as a dlight and not an elight — stock asks CL_AllocDlight, and an elight would "
          + "leave the wall behind the flash dark");

        Assert.True(light.OriginX == 64f && light.OriginY == -128f && light.OriginZ == 72f,
            "at the point off the wire");
        Assert.True(light.Red == 255 && light.Green == 200 && light.Blue == 63,
            "in the colour off the wire");
        Assert.True(light.Radius == 384f, "with the radius off the wire");
        Assert.True(light.LiveFor == 1.5f,
            "living m_fTime seconds, which is what die = curtime + time means");
    }

    /// <summary>
    /// <b>THE DECAY, WHICH IS THE FIELD THIS CLASS IS THE FIRST CALLER OF.</b>
    ///
    /// <para>
    /// Every light on this slot before C-Phase 3 is refreshed every frame by its
    /// entity, and a light rewritten sixty times a second cannot fade — so
    /// <c>dlight_t::decay</c> being left alone was correct and untestable. A
    /// temp entity light is placed once and never touched again, so the decay IS
    /// the fade: without it the light would hold full brightness for its whole
    /// life and then vanish.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDynamicLightCarriesItsDecayAndNotOnlyItsLifetime()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDynamicLight")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Float("Time", 2f);
        writer.Float("Decay", 1280f);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEDynamicLight", new ClientTeDynamicLight(engine));
        players.Dispatch(te.Handle, blob);

        var light = engine.Last("Visibility.DynamicLightUpdate")!.Value.ArgAs<DynamicLight>(0);

        Assert.True(light.Decay == 1280f,
            "m_fDecay reaches the pool. It and m_fTime are two different numbers on two "
          + "different ranges — [0,2560] and [0,25.6] — so a player that passed the lifetime "
          + "for both would look right for a one-second light and wrong for every other");
        Assert.True(light.LiveFor == 2f, "and the lifetime is still the lifetime");
    }

    /// <summary>
    /// <c>exponent</c> is the inventory's ONE signed eight-bit int, and a
    /// negative one has to arrive negative.
    ///
    /// <para>
    /// Stock's own callers pass small positive falloff exponents, so an unsigned
    /// read would look correct in every screenshot this build can take. That is
    /// exactly the shape of bug the reader's two integer accessors exist to keep
    /// apart, and it is invisible to <c>cs_sv_te_dump</c>, which cannot see a
    /// sign.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDynamicLightExponentIsReadSignedWhereTheColoursAreNot()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEDynamicLight")!;

        Assert.True(te.Fields["Exponent"].Flags == TeFlags.None,
            "exponent's flags are literally 0, where r/g/b three props earlier are "
          + "SPROP_UNSIGNED");
        Assert.True((te.Fields["R"].Flags & TeFlags.Unsigned) != 0,
            "and r really is unsigned, so the pair is a contrast and not a coincidence");

        var blob = new byte[te.BlobBytes];
        new TempEntWriter(te, blob).Int("Exponent", -3);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEDynamicLight", new ClientTeDynamicLight(engine));
        players.Dispatch(te.Handle, blob);

        Assert.True(engine.Last("Visibility.DynamicLightUpdate")!.Value
                          .ArgAs<DynamicLight>(0).Exponent == -3,
            "a negative exponent arrives negative rather than as 253");
    }

    // =====================================================================
    // CTEEffectDispatch — the nested table, on the wire.
    // =====================================================================

    /// <summary>
    /// <b>THE WHOLE ROUND TRIP THROUGH THE NESTED TABLE</b>: the real server
    /// writer packs a <see cref="TeEffectData"/> into a
    /// <c>DT_TEEffectDispatch</c> blob, and the real client player reads it back
    /// out and hands it to the registry.
    ///
    /// <para>
    /// This is the test that measures the nesting rather than describing it.
    /// <c>DT_TEEffectDispatch</c> declares ONE prop and it is the datatable, so
    /// every value below lives one level down at <c>m_EffectData</c>'s offset
    /// plus its own — and if that shift were wrong, every field would decode as
    /// its neighbour and every one of these assertions would be a plausible
    /// number in the wrong place.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDispatchRoundTripsEveryNestedFieldFromTheServerWriter()
    {
        var engine = Fake();
        var registry = Registered(engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);

        var sent = new TeEffectData
        {
            Origin             = new Vector(10f, 20f, 30f),
            Start              = new Vector(-1f, -2f, -3f),
            Angles             = new Vector(45f, 90f, 0f),
            Normal             = new Vector(0f, 0f, 1f),
            Flags              = 3,
            Magnitude          = 2.5f,
            Scale              = 1.5f,
            AttachmentIndex    = -1,
            SurfaceProp        = 42,
            Material           = 71,
            DamageType         = 64,
            HitBox             = 5,
            EntIndex           = 33,
            Color              = 7,
            Radius             = 12.5f,
            CustomColors       = true,
            Color1             = new Vector(1f, 0f, 0f),
            Color2             = new Vector(0f, 1f, 0f),
            ControlPoint1      = true,
            ParticleAttachment = 2,
            ControlPointOffset = new Vector(4f, 5f, 6f),
        };

        temps.DispatchEffect(TeFilter.Broadcast, 0f, 9, sent);

        var playback = engine.Last("Effects.TempEntPlayback")!.Value;
        byte[] blob = playback.ArgAs<byte[]>(4)!;

        // The client's own side: the string table answers for index 9.
        engine.Returns("Effects.EffectDispatchName", "watersplash");

        var players = new ClientTempEnts(new RecordingLog(), registry);
        var dispatched = new RecordingEffectDispatch();
        var player = new ClientTeEffectDispatch(engine, dispatched);
        players.Add("CTEEffectDispatch", player);

        players.Dispatch(registry.Find("CTEEffectDispatch")!.Handle, blob);

        Assert.True(dispatched.Calls.Count == 1, "one effect was dispatched");

        var call = dispatched.Calls[^1];

        Assert.True(call.Name == "watersplash",
            "under the name the string table answered for the index the wire carried");

        var got = call.Data;

        Assert.True(Same(got.Origin, sent.Origin), "the origin survived the nesting");
        Assert.True(Same(got.Start, sent.Start), "and the start");
        Assert.True(Same(got.Angles, sent.Angles),
            "and the ANGLES, which the wire carries BEFORE the normal and CEffectData "
          + "declares after it — the one swap cs_sv_te_dump cannot see");
        Assert.True(Same(got.Normal, sent.Normal), "and the normal");
        Assert.True(got.Flags == 3, "the flag word");
        Assert.True(got.Magnitude == 2.5f, "the magnitude");
        Assert.True(got.Scale == 1.5f, "the whole NOSCALE float of scale");
        Assert.True(got.AttachmentIndex == -1,
            "the attachment index, still NEGATIVE — five bits and no SPROP_UNSIGNED, so -1 "
          + "means \"no attachment\" all the way through");
        Assert.True(got.Material == 71, "the material, twelve bits and not thirteen");
        Assert.True(got.DamageType == 64, "the damage bits");
        Assert.True(got.HitBox == 5, "the hitbox");
        Assert.True(got.EntIndex == 33, "the entity index, whose wire name is entindex");
        Assert.True(got.Color == 7, "the single colour byte");
        Assert.True(got.Radius == 12.5f, "the radius");
        Assert.True(got.CustomColors == 1, "the custom-colour flag");
        Assert.True(Same(got.Color1, sent.Color1) && Same(got.Color2, sent.Color2),
            "both custom colours");
        Assert.True(got.ControlPoint1 == 1, "the control point flag");
        Assert.True(got.ControlPointAttachment == 2, "its attachment");
        Assert.True(Same(got.ControlPointOffset, sent.ControlPointOffset),
            "and its offset, which is the LAST twelve bytes of the blob — a nesting shift "
          + "would have run off the end here rather than landing on a neighbour");
    }

    /// <summary>
    /// The <c>+1</c> the send proxy used to add comes back off, and the effect
    /// name index does not acquire one.
    ///
    /// <para>
    /// The two props sit three lines apart in stock's table and are treated
    /// differently on purpose: dropping the subtraction shifts every surface
    /// property by one — a different impact sound and different debris, with
    /// nothing anywhere saying so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSurfacePropertyLosesItsPlusOneAndMinusOneComesBackAsMinusOne()
    {
        var engine = Fake();
        var registry = Registered(engine);
        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        var dispatched = new RecordingEffectDispatch();
        players.Add("CTEEffectDispatch", new ClientTeEffectDispatch(engine, dispatched));

        engine.Returns("Effects.EffectDispatchName", "bloodspray");

        foreach (short surface in new short[] { -1, 0, 42, 254 })
        {
            var data = new TeEffectData { SurfaceProp = surface };

            temps.DispatchEffect(TeFilter.Broadcast, 0f, 4, data);

            byte[] blob = engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<byte[]>(4)!;

            players.Dispatch(registry.Find("CTEEffectDispatch")!.Handle, blob);

            Assert.True(dispatched.Calls[^1].Data.SurfaceProp == surface,
                $"a surface property of {surface} goes on the wire as {surface + 1} and comes "
              + "off it as itself. The proxies are gone (plan 2B) and the arithmetic lives on "
              + "both sides of this round trip");
        }
    }

    /// <summary>
    /// An index the string table does not hold dispatches NOTHING, which is
    /// stock's <c>if ( pEffectName )</c>.
    ///
    /// <para>
    /// And in particular it does not dispatch the EMPTY name, which would walk
    /// all fifty-two registrations and log about a name rather than about
    /// itself.
    /// </para>
    /// </summary>
    [Fact]
    public void AnIndexTheStringTableDoesNotHoldDispatchesNothing()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEEffectDispatch")!;
        var blob = new byte[te.BlobBytes];
        new TempEntWriter(te, blob).Int("m_iEffectName", 999);

        engine.Returns("Effects.EffectDispatchName", string.Empty);

        var dispatched = new RecordingEffectDispatch();
        var player = new ClientTeEffectDispatch(engine, dispatched);
        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEEffectDispatch", player);

        players.Dispatch(te.Handle, blob);

        Assert.True(dispatched.Calls.Count == 0,
            "nothing was dispatched, because stock's PostDataUpdate does nothing at all for a "
          + "NULL name and an empty string is how that crosses a boundary with no NULL");
        Assert.True(player.Unresolved == 1,
            "and it is COUNTED, so a string table that failed to replicate is tellable from a "
          + "client that received no dispatches");
        Assert.True(player.LastEffectName.Length == 0, "with no name remembered");
    }

    /// <summary>
    /// <b>BEHAVIOUR LEDGER ROW C7.</b> <c>"Impact"</c> is carried through
    /// UNCHANGED.
    ///
    /// <para>
    /// It is the busiest dispatch name in the build and for the whole life of
    /// this client it never played: <c>DECLARE_CLIENT_EFFECT( "Impact", ... )</c>
    /// existed only in <c>tf/</c>, <c>hl2/</c> and <c>sdk/</c>, none of them
    /// compiled here. The row could be closed only by making the capability
    /// real — never by substituting a name that resolves, which would turn a
    /// TRACKED gap into an untracked one.
    /// </para>
    ///
    /// <para>
    /// <b>THE ROW IS CLOSED and this test did not change</b>, which is the
    /// dividend it was written for. Lane <c>fx-impact</c> registered a callback
    /// under that exact spelling in
    /// <c>sourcesharp/game/client/shell_impact_effects.cpp</c>; the name on the
    /// wire, the string-table lookup and the walk this fact asserts on are all
    /// what they were. What it now guards is the other direction — a later lane
    /// renaming the dispatch would silently unclaim the registration and put
    /// the gap back.
    /// </para>
    /// </summary>
    [Fact]
    public void TheImpactDispatchNameIsCarriedThroughUnchangedAndRowC7StaysOpen()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEEffectDispatch")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);
        writer.Int("m_iEffectName", 1);
        writer.Vector("m_vOrigin", new Vector(1f, 2f, 3f));

        engine.Returns("Effects.EffectDispatchName", "Impact");

        var dispatched = new RecordingEffectDispatch();
        var player = new ClientTeEffectDispatch(engine, dispatched);
        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEEffectDispatch", player);

        players.Dispatch(te.Handle, blob);

        Assert.True(dispatched.Calls.Count == 1,
            "the dispatch still happens — the name resolves in the STRING TABLE, which the "
          + "server fills, and the client's CALLBACK REGISTRY is walked with it");
        Assert.True(dispatched.Calls[^1].Name
                    == "Impact",
            "and the name reaching DispatchEffectToCallback is \"Impact\", spelt exactly as "
          + "the server sent it. A player that substituted some OTHER name would have closed "
          + "ledger row C7 by hiding it, which is the one thing a lane may never do to a row "
          + "— and would now unclaim the registration fx-impact put under this spelling");
        Assert.True(player.LastEffectName == "Impact",
            "and the player remembers which of the fifty-two arrived, because the counters "
          + "cannot say");
    }

    // =====================================================================
    // CTEShatterSurface.
    // =====================================================================

    /// <summary>
    /// A shattered pane reaches its backend with all fourteen values, and the
    /// two faces' colours do not swap.
    /// </summary>
    [Fact]
    public void TheShatteredPaneCarriesAllFourteenValuesAndBothFaceColours()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEShatterSurface")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-512f, 256f, 64f));
        writer.Vector("Angles", new Vector(0f, 90f, 0f));
        writer.Vector("Force", new Vector(0f, -300f, 0f));
        writer.Vector("ForcePos", new Vector(-512f, 260f, 70f));
        writer.Float("Width", 32f);
        writer.Float("Height", 48f);
        writer.Float("ShardSize", 4f);
        writer.Int("SurfaceType", (int)ShatterSurfaceType.Tile);
        writer.Int("FrontColorR", 200);
        writer.Int("FrontColorG", 210);
        writer.Int("FrontColorB", 220);
        writer.Int("BackColorR", 10);
        writer.Int("BackColorG", 20);
        writer.Int("BackColorB", 30);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEShatterSurface", new ClientTeShatterSurface(new EngineShatter(engine)));

        players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientShatterSurface") == 1, "one pane shattered");

        var call = engine.Last("Effects.ClientShatterSurface")!.Value;

        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(-512f, 256f, 64f)), "the pane's middle");
        Assert.True(Same(call.ArgAs<Vector>(1), new Vector(0f, 90f, 0f)),
            "its orientation, which is what AngleVectors turns into the grid's two axes — "
          + "swapping it with the force would lay the shards out along the impact");
        Assert.True(Same(call.ArgAs<Vector>(2), new Vector(0f, -300f, 0f)), "the force");
        Assert.True(Same(call.ArgAs<Vector>(3), new Vector(-512f, 260f, 70f)),
            "and the point it was applied at, which is a DIFFERENT vector from the origin and "
          + "is what the per-shard falloff is measured from");

        Assert.True(call.ArgAs<float>(4) == 32f, "the width");
        Assert.True(call.ArgAs<float>(5) == 48f, "the height");
        Assert.True(call.ArgAs<float>(6) == 4f, "the shard size");
        Assert.True(call.ArgAs<int>(7) == (int)ShatterSurfaceType.Tile,
            "and the surface type, which picks tile's materials over glass's");

        Assert.True(call.ArgAs<Color32>(8) == new Color32(200, 210, 220),
            "the near face's three bytes, in order");
        Assert.True(call.ArgAs<Color32>(9) == new Color32(10, 20, 30),
            "and the FAR face's, which are six consecutive one-byte props with names that "
          + "differ by four characters — the swap this asserts against is the easiest mistake "
          + "in the table and draws a pane whose back is the wrong colour");
    }

    /// <summary>
    /// The six colour bytes are SIX PROPS OF ONE BYTE, and each is read at its
    /// own offset.
    ///
    /// <para>
    /// <c>RecvPropInt</c> picks its write proxy from the declared storage width
    /// (<c>dt_recv.cpp:202</c>), so a four-byte read at
    /// <c>m_uchFrontColor[0]</c> would be three bytes of its neighbours — which
    /// would look right in a test that only ever set one of them.
    /// </para>
    /// </summary>
    [Fact]
    public void EachColourByteIsReadAtItsOwnOffsetAndNotOverItsNeighbours()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEShatterSurface")!;

        foreach (string name in new[]
        {
            "FrontColorR", "FrontColorG", "FrontColorB",
            "BackColorR", "BackColorG", "BackColorB",
        })
        {
            Assert.True(te.Fields[name].SizeBytes == 1,
                $"{name} is ONE byte of blob, as stock's unsigned char is");
        }

        int[] offsets = new[]
        {
            te.Fields["FrontColorR"].Offset, te.Fields["FrontColorG"].Offset,
            te.Fields["FrontColorB"].Offset, te.Fields["BackColorR"].Offset,
            te.Fields["BackColorG"].Offset, te.Fields["BackColorB"].Offset,
        };

        for (int i = 1; i < offsets.Length; i++)
        {
            Assert.True(offsets[i] == offsets[i - 1] + 1,
                "and the six sit at six CONSECUTIVE offsets, which is what six fields produce "
              + "and what stock's SENDINFO_ARRAYELEM run produces — with no padding between "
              + "them for a four-byte store to hide in");
        }

        // Every byte distinct, so a read of the wrong width lands on a value
        // that is not the one asked for.
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);
        writer.Int("FrontColorR", 1);
        writer.Int("FrontColorG", 2);
        writer.Int("FrontColorB", 3);
        writer.Int("BackColorR", 4);
        writer.Int("BackColorG", 5);
        writer.Int("BackColorB", 6);
        writer.Float("ShardSize", 1f);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEShatterSurface", new ClientTeShatterSurface(new EngineShatter(engine)));
        players.Dispatch(te.Handle, blob);

        var call = engine.Last("Effects.ClientShatterSurface")!.Value;

        Assert.True(call.ArgAs<Color32>(8) == new Color32(1, 2, 3),
            "the front face reads 1,2,3 and not 1,0,0 or 0x030201");
        Assert.True(call.ArgAs<Color32>(9) == new Color32(4, 5, 6),
            "and the back reads 4,5,6");
    }

    /// <summary>
    /// The pane's angles are a plain COORD vector on this table too, and a
    /// negative one survives — <see cref="TeShatterSurface"/>'s note, asserted
    /// on the play path.
    /// </summary>
    [Fact]
    public void TheShatteredPaneAnglesAreAPlainVectorAndSurviveNegative()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEShatterSurface")!;

        Assert.True(te.Fields["Angles"].Kind == TePropKind.Vector,
            "a SendPropVector over a CNetworkQAngle, matched by c_te_glassshatter.cpp's "
          + "RecvPropVector — declaring it QAngles would be one prop count and one proxy away "
          + "from the client's table");

        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);
        writer.Vector("Angles", new Vector(-30f, -60f, -90f));
        writer.Float("ShardSize", 2f);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEShatterSurface", new ClientTeShatterSurface(new EngineShatter(engine)));
        players.Dispatch(te.Handle, blob);

        Assert.True(Same(engine.Last("Effects.ClientShatterSurface")!.Value.ArgAs<Vector>(1),
                         new Vector(-30f, -60f, -90f)),
            "and the three components arrive negative rather than wrapped into [0,360]");
    }

    // =====================================================================
    // What the lane did NOT take.
    // =====================================================================

    /// <summary>
    /// All six of this lane's classes nest <c>DT_BaseTempEntity</c>, and the base
    /// still registers no class of its own.
    ///
    /// <para>
    /// The nesting is the finding lane CD proved on a live join and wrote into
    /// <c>FindRegisteredTable</c>'s header: 2E-bis' refusal is about two tables
    /// of ONE NAME, and there is exactly one <c>DT_BaseTempEntity</c> in the
    /// process however its parents are split between managed and native.
    /// </para>
    ///
    /// <para>
    /// THE ABSTENTION OUTLIVED ITS ORIGINAL REASON, which is why this [Fact] was
    /// edited rather than deleted at step 7b. It used to abstain because stock's
    /// <c>c_basetempentity.cpp</c> registered a ClientClass of that name; 7b
    /// evicted that TU, and <c>game/shared/basetempentity.cpp</c> had already
    /// left <c>server_csharp.vpc</c>. So neither side registers
    /// <c>CBaseTempEntity</c> now, which is symmetric and is the end state —
    /// see <see cref="TeBaseTempEntity"/> for why flipping it to
    /// <c>true</c> would be a live-join risk taken for a table with no props.
    /// </para>
    /// </summary>
    [Fact]
    public void AllSixNestTheBaseTableAndTheBaseIsStillNotRegisteredAsAClass()
    {
        var engine = Fake();
        TempEntRegistry registry = Registered(engine);

        TempEntType baseTable = registry.Find("CBaseTempEntity")!;

        Assert.True(!baseTable.Class.RegistersClass,
            "CBaseTempEntity abstains from registering a class on BOTH sides — "
          + "neither basetempentity.cpp nor c_basetempentity.cpp is compiled any more, "
          + "and a class registered on one side only is a join that stops with nothing "
          + "in any log");

        foreach (string wireName in new[]
        {
            "CTESmoke", "CTEMuzzleFlash", "CTEDynamicLight",
            "CTEImpact", "CTEEffectDispatch", "CTEShatterSurface",
        })
        {
            Assert.True(registry.Find(wireName)!.Class.BaseTable == "DT_BaseTempEntity",
                $"{wireName} nests DT_BaseTempEntity, which the managed declaration owns "
              + "since 7b evicted c_basetempentity.cpp — the split-parent case lane CD "
              + "proved legal is recorded in FindRegisteredTable's header comment");
        }
    }

    /// <summary>The managed dispatch table's seat, recording what reached it.</summary>
    private sealed class RecordingEffectDispatch : SourceSharp.Client.Interop.IClientEffectDispatch
    {
        public List<(string Name, EffectData Data)> Calls { get; } = [];

        public void Dispatch(string name, in EffectData data) => Calls.Add((name, data));
    }

    /// <summary>The decoded pane, recorded where the engine call used to be (the shards are ShardEffects', ShardsOracleTests').</summary>
    private sealed class EngineShatter(RecordingEngine engine) : IClientShatter
    {
        public void ShatterSurface(Vector origin, Vector angles, Vector force, Vector forcePos, float width, float height, float shardSize,
                                   int surfaceType, Color32 front, Color32 back)
            => engine.RecordCall("Effects.ClientShatterSurface", origin, angles, force, forcePos, width, height, shardSize, surfaceType, front, back);
    }
}
