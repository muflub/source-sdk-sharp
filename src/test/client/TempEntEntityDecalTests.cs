using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ClientEntityDecal"/> — the managed <c>C_BaseEntity::AddDecal</c>,
/// which is what closed behaviour ledger rows C2 and C3.
///
/// <para>
/// <see cref="TempEntClientDecalCxTests"/> asserts the two TEMP ENTITIES reach
/// this and with what. What is asserted here is the SWITCH itself, because
/// three of its four outcomes are silent in stock and only one of them draws:
/// a port that took the wrong arm produces a decal in a plausible place through
/// the wrong subsystem, and a port that fell off the end produces the state the
/// rows were opened for, with nothing saying so.
/// </para>
///
/// <para>
/// EVERY VECTOR HERE IS DISTINCT FROM EVERY OTHER. Stock's <c>AddDecal</c> takes
/// <c>rayStart</c>, <c>rayEnd</c> and <c>decalCenter</c>, and only the studio
/// arm reads the first two while only the brush arm reads the third —
/// <c>c_te_decal.cpp</c> passes the same vector for <c>rayEnd</c> and
/// <c>decalCenter</c> and <c>c_te_projecteddecal.cpp</c> does not. A test
/// written with two of the three equal would agree with a port that had
/// collapsed them, and that port moves every projected decal in the game.
/// </para>
/// </summary>
public class TempEntEntityDecalTests
{
    /// <summary>Component-wise and EXACT — nothing on this path encodes.</summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private static readonly Vector RayStart = new(11f, 12f, 13f);
    private static readonly Vector RayEnd = new(21f, 22f, 23f);
    private static readonly Vector Centre = new(31f, 32f, 33f);

    private static EntityDecalArm Stamp(RecordingEngine engine, int entIndex = 17,
                                        int decalIndex = 7)
        => ClientEntityDecal.AddDecal(engine, engine, DecalSettings.On(), entIndex,
                                      RayStart, RayEnd, Centre, decalIndex);

    // =====================================================================
    // The four arms.
    // =====================================================================

    /// <summary>
    /// A studio model takes <c>AddStudioDecal</c>, with the SEGMENT and not the
    /// centre.
    /// </summary>
    [Fact]
    public void AStudioModelIsDecalledThroughTheModelRendererWithTheSegment()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Studio);

        Assert.True(Stamp(engine) == EntityDecalArm.StudioModel,
            "an .mdl is AddStudioDecal's arm");

        Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 1, "one studio decal");
        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 0,
            "and NOT also the brush arm -- stock's switch takes one case and breaks");

        var call = engine.Last("Effects.ClientDecalStudioModel")!.Value;

        Assert.True(call.ArgAs<int>(0) == 17, "on the entity it was aimed at");
        Assert.True(Same(call.ArgAs<Vector>(1), RayStart), "from rayStart");
        Assert.True(Same(call.ArgAs<Vector>(2), ClientEntityDecal.StudioRayDelta(RayStart, RayEnd)),
            "along the segment to rayEnd lengthened a tenth (the delta, as stock's ray holds it) -- "
          + "and NOT to decalCenter, which is a third vector this arm does not read");
        Assert.True(call.ArgAs<int>(3) == 7, "with the decal index it was given");
    }

    /// <summary>
    /// A brush model takes <c>AddBrushModelDecal</c>, with the CENTRE and not
    /// the segment.
    ///
    /// <para>
    /// This is the arm the WORLD takes, which makes it row C3's ordinary case:
    /// every unnamed <c>info_projecteddecal</c> on map geometry lands here.
    /// </para>
    /// </summary>
    [Fact]
    public void ABrushModelIsDecalledThroughTheEnginesDecalSystemWithTheCentre()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);

        Assert.True(Stamp(engine) == EntityDecalArm.BrushModel,
            "a brush model is AddBrushModelDecal's arm");

        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 1, "one brush decal");
        Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 0,
            "and not the model renderer, which keeps decals per model INSTANCE and has "
          + "none for a brush");

        var call = engine.Last("Effects.ClientDecalBrushModel")!.Value;

        Assert.True(call.ArgAs<int>(0) == 17, "on the entity it was aimed at");
        Assert.True(Same(call.ArgAs<Vector>(1), Centre),
            "at decalCenter -- stock's THIRD argument, and not rayEnd, which is a "
          + "different vector at c_te_projecteddecal.cpp's call site");
        Assert.True(call.ArgAs<int>(2) == 7, "with the decal index it was given");
    }

    /// <summary>
    /// ENTITY ZERO IS THE WORLD AND IS AN ORDINARY SUBJECT, which is the one
    /// thing on this path that must not be tidied into "the index must be
    /// positive".
    ///
    /// <para>
    /// <c>cl_entitylist-&gt;GetEnt( 0 )</c> is the world entity, the world is a
    /// brush model, and that is where row C3 lands. A guard that refused zero
    /// would delete the row's ordinary case while leaving the static prop arm
    /// working — which reads as a port that landed.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWorldIsEntityZeroAndIsDecalledLikeAnyOtherBrushModel()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);

        Assert.True(Stamp(engine, entIndex: 0) == EntityDecalArm.BrushModel,
            "entity 0 is the world, not the absence of an entity");

        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 1,
            "and it is decalled");
        Assert.True(engine.Last("Effects.ClientDecalBrushModel")!.Value.ArgAs<int>(0) == 0,
            "onto entity 0 itself, which is what DecalShoot wants for a world decal");
    }

    /// <summary>
    /// No such entity draws nothing — stock's line ABOVE the call.
    ///
    /// <para>
    /// <c>if ( ( ent = cl_entitylist-&gt;GetEnt( entity ) ) == NULL ) return;</c>.
    /// It is not one of the switch's cases, and folding it into
    /// <see cref="ModelType.Bad"/> would make an entity that is not there
    /// indistinguishable from one that is there wearing nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void NoSuchEntityDrawsNothingAndIsNotAModelType()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", (ModelType?)null);

        Assert.True(Stamp(engine, entIndex: 47) == EntityDecalArm.NoSuchEntity,
            "there is no entity 47, which is its own answer");

        Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 0, "nothing drawn");
        Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 0, "either way");

        Assert.True(engine.Last("WorldQuery.ClientEntityModelType")!.Value.ArgAs<int>(0) == 47,
            "and the client was asked about the entity the caller named, not about "
          + "some other one");
    }

    /// <summary>
    /// A model that is neither draws nothing and says nothing — stock's
    /// <c>default:</c>, whose whole body is <c>tr.fraction = 1.0f</c> against a
    /// <c>trace_t</c> both call sites throw away.
    ///
    /// <para>
    /// SELECTED BY ROLE AND NOT BY VALUE. The two kinds asserted here are named
    /// because they are the two that have no decal path, not because a filter
    /// over the enum happened to leave them — a rule written over the value
    /// under test cannot see a wrong value, which is
    /// <c>MaterialCatalogueTests.ToolMaterialsAreToolMaterials</c>'s bug.
    /// </para>
    /// </summary>
    [Fact]
    public void ASpriteOrABadModelHasNoDecalPathAndThatIsStock()
    {
        foreach (ModelType kind in new[] { ModelType.Sprite, ModelType.Bad })
        {
            var engine = Fake().Returns("WorldQuery.ClientEntityModelType", kind);

            Assert.True(Stamp(engine) == EntityDecalArm.NoDecalPath,
                $"{kind} is stock's default arm, which draws nothing");

            Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 0,
                $"{kind} does not reach the model renderer");
            Assert.True(engine.CountOf("Effects.ClientDecalBrushModel") == 0,
                $"{kind} does not reach the engine's decal system either");
        }
    }

    // =====================================================================
    // The two temp entities pass DIFFERENT vectors, which is why AddDecal
    // takes three.
    // =====================================================================

    /// <summary>
    /// <c>CTEDecal</c> passes <c>*start, *pos, *pos</c> and
    /// <c>CTEProjectedDecal</c> passes <c>*pos, endpos, endpos</c> — so the
    /// centre equals the ray's end in the first and not in the second.
    ///
    /// <para>
    /// THIS IS THE ASSERTION A COLLAPSED PORT FAILS. Deriving the centre from
    /// the segment agrees with <c>c_te_decal.cpp</c> and silently moves every
    /// projected decal by the length of the projection, which is up to the
    /// distance the mapper asked for.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCentreIsAThirdArgumentAndNotTheRaysEnd()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Brush);

        // The projected decal's shape: centre equal to the end.
        ClientEntityDecal.AddDecal(engine, engine, DecalSettings.On(), 0,
                                   rayStart: new Vector(100f, 200f, 300f),
                                   rayEnd: new Vector(612f, 200f, 300f),
                                   decalCenter: new Vector(612f, 200f, 300f),
                                   decalIndex: 9);

        Assert.True(Same(engine.Last("Effects.ClientDecalBrushModel")!.Value.ArgAs<Vector>(1),
                         new Vector(612f, 200f, 300f)),
            "the projected decal's centre is its endpoint");

        // And a caller whose centre is NOT the end gets its own centre, rather
        // than the end it did not ask for.
        ClientEntityDecal.AddDecal(engine, engine, DecalSettings.On(), 0,
                                   rayStart: new Vector(100f, 200f, 300f),
                                   rayEnd: new Vector(612f, 200f, 300f),
                                   decalCenter: new Vector(-7f, -8f, -9f),
                                   decalIndex: 9);

        Assert.True(Same(engine.Last("Effects.ClientDecalBrushModel")!.Value.ArgAs<Vector>(1),
                         new Vector(-7f, -8f, -9f)),
            "and a centre that is not the endpoint is carried as given -- the third "
          + "argument exists because stock's two call sites disagree about it");
    }

    /// <summary>
    /// The two temp entities reach the managed <c>AddDecal</c> with the vectors
    /// their own files pass, asserted through the classes rather than through
    /// the helper.
    ///
    /// <para>
    /// The helper tests above would all pass with a player that had swapped its
    /// arguments on the way in. This is the one that would not.
    /// </para>
    /// </summary>
    [Fact]
    public void BothPlayersHandTheirOwnVectorsToTheManagedAddDecal()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Studio);

        // CTEDecal: rayStart is m_vecStart, rayEnd is m_vecOrigin.
        new ClientTeDecal(engine, engine, DecalSettings.On()).Play(new TeDecal
        {
            Start = new Vector(1f, 2f, 3f),
            Origin = new Vector(4f, 5f, 6f),
            Entity = 12,
            Hitbox = 0,
            Index = 30,
        });

        var decal = engine.Last("Effects.ClientDecalStudioModel")!.Value;

        Assert.True(decal.ArgAs<int>(0) == 12, "the bullet hole is aimed at the wire's entity");
        Assert.True(Same(decal.ArgAs<Vector>(1), new Vector(1f, 2f, 3f)),
            "from m_vecStart, which is stock's *start");
        Assert.True(Same(decal.ArgAs<Vector>(2),
                         ClientEntityDecal.StudioRayDelta(new Vector(1f, 2f, 3f), new Vector(4f, 5f, 6f))),
            "to m_vecOrigin, which is stock's *pos (handed over as the ray's lengthened delta)");

        // CTEProjectedDecal: rayStart is m_vecOrigin, rayEnd is the projection's
        // far end -- origin + forward * distance, with angles of zero facing +X.
        var world = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Studio)
                          .Returns("WorldQuery.ClientTraceLine",
                                   new ClientTraceResult(true, 0.5f, new Vector(50f, 0f, 0f),
                                                         Vector.Zero, 0, 12, 0, 0));

        new ClientTeProjectedDecal(world, world, DecalSettings.On()).Play(new TeProjectedDecal
        {
            Origin = new Vector(100f, 200f, 300f),
            Rotation = Vector.Zero,
            Distance = 512f,
            Index = 31,
        });

        var projected = world.Last("Effects.ClientDecalStudioModel")!.Value;

        Assert.True(projected.ArgAs<int>(0) == 12,
            "the projection is aimed at what the TRACE hit, not at anything off the wire "
          + "-- this table carries no entity");
        Assert.True(Same(projected.ArgAs<Vector>(1), new Vector(100f, 200f, 300f)),
            "from its own origin, which is stock's *pos");
        Assert.True(Same(projected.ArgAs<Vector>(2),
                         ClientEntityDecal.StudioRayDelta(new Vector(100f, 200f, 300f), new Vector(612f, 200f, 300f))),
            "to origin + forward * distance, which is stock's endpos (the ray's lengthened delta) -- "
          + "and NOT to tr.endpos (50, 0, 0), which is where the trace stopped");
    }

    // =====================================================================
    // AddStudioDecal: C_BaseEntity::AddStudioDecal's gate and its ray (lane M1-C).
    // =====================================================================

    /// <summary>
    /// <c>ray.Init( start, end ); ray.m_Delta *= 1.1f;</c> -- the bloat is on the
    /// DELTA and only the far end moves. (10, 10, 10) is exactly (11, 11, 11);
    /// scaling the endpoint about the origin, or the segment about its middle,
    /// gives neither.
    /// </summary>
    [Fact]
    public void TheStudioRayIsTheSegmentLengthenedATenthFromItsStart()
    {
        var engine = Fake();

        Assert.True(ClientEntityDecal.AddStudioDecal(engine, DecalSettings.On(), 17, RayStart, RayEnd, 7));

        var call = engine.Last("Effects.ClientDecalStudioModel")!.Value;
        Assert.True(Same(call.ArgAs<Vector>(1), RayStart), "the start is not moved");
        Assert.True(Same(call.ArgAs<Vector>(2), new Vector(11f, 11f, 11f)),
            $"the delta is (end - start) * 1.1, not {call.ArgAs<Vector>(2)}");
    }

    [Fact]
    public void AZeroLengthStudioRayStaysZeroLength()
        => Assert.True(Same(ClientEntityDecal.StudioRayDelta(RayStart, RayStart), new Vector(0f, 0f, 0f)));

    [Fact]
    public void AStudioRayAimedBackwardsLengthensBackwards()
        => Assert.True(Same(ClientEntityDecal.StudioRayDelta(RayEnd, RayStart), new Vector(-11f, -11f, -11f)));

    /// <summary>
    /// <c>r_drawmodeldecals 0</c>: stock's AddStudioDecal returns before the
    /// instance is made, so nothing reaches the model renderer.
    /// </summary>
    [Fact]
    public void RDrawModelDecalsOffPutsNoDecalOnAModel()
    {
        var engine = Fake().Returns("WorldQuery.ClientEntityModelType", ModelType.Studio);

        ClientEntityDecal.AddDecal(engine, engine, new DecalSettings(() => true, () => false), 17,
                                   RayStart, RayEnd, Centre, 7);

        Assert.True(engine.CountOf("Effects.ClientDecalStudioModel") == 0, "no studio decal with the toggle off");
    }
}
