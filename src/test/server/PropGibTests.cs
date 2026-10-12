using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A physics prop breaking into the pieces its model was compiled with.
///
/// <para>
/// The user-visible half of the lane: a prop that broke fired its output, made
/// its bang and vanished, and nothing at all appeared where it had been. What
/// is asserted here is the three moments that has to happen at — the precache
/// during the level load, the creation during the break, and the piece's own
/// life afterwards — because each one is silent when it is missing.
/// </para>
/// </summary>
public class PropGibTests
{
    private const string Crate = "models/props_junk/wood_crate001a.mdl";

    private static (string, BreakPiece) Plank(string model, Vector offset, float fade = 20f)
        => (model, new BreakPiece
        {
            Offset = offset,
            BurstScale = PropBreakPlan.DefaultBurstScale,
            CollisionGroup = (int)CollisionGroup.Debris,
            FadeTime = fade,
            Health = 1f,
        });

    /// <summary>
    /// A prop that has spawned, built the way the game builds it — through the
    /// registry, so the engine binding and the injected areas are the ones the
    /// class really gets.
    /// </summary>
    private static (PropPhysics Prop, RecordingEngine Engine) Spawned(
        uint handle, (string, BreakPiece)[] pieces, string className,
        params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);

        engine.Returns("Precache.BreakPieceCount", pieces.Length);
        engine.Returns("Precache.BreakPieceAt.pieces", (IReadOnlyList<(string, BreakPiece)>)pieces);
        engine.Returns("Entities.Count", 880);
        engine.Returns("Entities.Create", new EntityHandle(0x900));
        engine.Returns("Entities.SetKeyValue", true);
        engine.Returns("Entities.GetOrigin", new Vector(64f, 0f, 32f));
        engine.Returns("Entities.GetAngles", Vector.Zero);
        engine.Returns("Entities.GetVelocity", Vector.Zero);
        engine.Returns("Entities.GetAngularVelocity", Vector.Zero);
        engine.Returns("Entities.GetSpawnFlags", 0);
        engine.Returns("EntityPhysics.GetCollisionGroup", CollisionGroup.None);
        engine.Returns("EntityRender.GetModelName", "models/gibs/splinter.mdl");
        engine.Returns("EntityRender.GetModelKeyValueText", null);
        engine.Returns("Entities.GetHealth", 0);
        engine.Returns("Scripted.SetNextThink", 0);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = services;

        var h = new EntityHandle(handle);

        // Keys BEFORE the spawn, which is the order Source works in and the
        // order that matters here: a piece's fade time decides during its own
        // spawn what it is, so a value that arrived afterwards would arrive
        // after the decision.
        foreach (var (key, value) in keys)
            registry.KeyValue(h, className, key, value);

        registry.Spawn(h, className);

        return ((PropPhysics)registry.Get(h)!, engine);
    }

    /// <summary>A prop wearing the crate, with the pieces its model lists.</summary>
    private static (PropPhysics Prop, RecordingEngine Engine) Prop(
        uint handle, params (string, BreakPiece)[] pieces)
        => Spawned(handle, pieces, EntityNames.PropPhysics, ("model", Crate));

    /// <summary>The same, but one of the pieces something else broke into.</summary>
    private static (PropPhysics Prop, RecordingEngine Engine) Gib(
        uint handle, float fadeTime, params (string, BreakPiece)[] pieces)
        => Spawned(handle, pieces, EntityNames.PropPhysics,
                   ("model", Crate),
                   (PropPhysicsVars.GibFadeTimeKey, fadeTime.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    //-------------------------------------------------------------------------
    // The precache, which happens during the level load or not at all.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Stock does this in <c>CBreakableProp::Precache</c>, and the reason it is
    /// there rather than at the break is the whole of why it is here: a piece
    /// exists only once something has broken, by which time the map is built
    /// and a precache is a console line and nothing drawn.
    /// </summary>
    [Fact]
    public void APropAsksWhatItBreaksIntoWhileTheLevelIsStillLoading()
    {
        var (prop, engine) = Prop(1,
                                  Plank("models/plank_a.mdl", new Vector(8f, 0f, 0f)),
                                  Plank("models/plank_b.mdl", new Vector(-8f, 0f, 0f)));

        Assert.True(engine.AllOf("Precache.BreakPieceCount")
                          .Any(c => c.ArgAs<string>(0) == Crate),
                    "a prop asks its own model what it breaks into, during its spawn");

        Assert.True(prop.BreakPieceCount == 2,
                    "and remembers the answer, which is stock's m_iNumBreakableChunks");
    }

    [Fact]
    public void APropWithNoModelAsksNothing()
    {
        var services = Services(out var engine);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = services;

        var h = new EntityHandle(2);
        registry.Spawn(h, EntityNames.PropPhysics);

        var prop = (PropPhysics)registry.Get(h)!;
        prop.OnSpawn();

        Assert.True(engine.CountOf("Precache.BreakPieceCount") == 0,
                    "a prop with no model has nothing to ask about, and asking would warn about ''");
    }

    //-------------------------------------------------------------------------
    // The break itself.
    //-------------------------------------------------------------------------

    [Fact]
    public void BreakingAPropMakesItsPieces()
    {
        var (prop, engine) = Prop(3,
                                  Plank("models/plank_a.mdl", new Vector(8f, 0f, 0f)),
                                  Plank("models/plank_b.mdl", new Vector(-8f, 0f, 0f)),
                                  Plank("models/plank_c.mdl", new Vector(0f, 8f, 0f)));

        prop.OnKilled(EntityHandle.None);

        Assert.True(prop.PiecesMade == 3, "a crate that lists three pieces breaks into three");

        Assert.True(engine.AllOf("Entities.Create")
                          .Count(c => c.ArgAs<string>(0) == EntityNames.PropPhysics) == 3,
                    "and each one is a prop_physics of its own");
    }

    /// <summary>
    /// The pieces are made by BREAKING, not by being removed.
    ///
    /// <para>
    /// prop_physics_respawnable is the case that tells the two apart: it
    /// overrides what becomes of the prop afterwards and hides itself instead of
    /// going. This class has already shipped that bug once — breaking was
    /// virtual, the respawnable prop replaced the whole of it, and every
    /// explosive barrel in dm_lockdown died quietly because the bang lived in
    /// the half that was replaced. Debris put in the same place would go the
    /// same way, and a map of respawning crates would break silently forever.
    /// </para>
    /// </summary>
    [Fact]
    public void ARespawningPropStillMakesItsPieces()
    {
        var (prop, _) = Spawned(4,
                                new[] { Plank("models/plank_a.mdl", new Vector(8f, 0f, 0f)) },
                                EntityNames.PropPhysicsRespawnable,
                                ("model", Crate));

        prop.OnKilled(EntityHandle.None);

        Assert.True(prop.PiecesMade == 1,
                    "a prop that comes back still leaves debris behind on the way");
        Assert.True(!prop.Gone,
                    "and does not remove itself, which is the whole difference between the two");
    }

    [Fact]
    public void ThePiecesAreThrownFromWhereThePropStood()
    {
        var (prop, engine) = Prop(5, Plank("models/plank_a.mdl", new Vector(0f, 0f, 10f)));

        prop.OnKilled(EntityHandle.None);

        var origin = engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1);

        // The prop is at (64, 0, 32) and the piece ten units above it.
        Assert.True(MathF.Abs(origin.X - 64f) < 0.001f
                 && MathF.Abs(origin.Z - 42f) < 0.001f,
                    "a piece lands where the prop was plus its own offset, not at the world origin");
    }

    [Fact]
    public void APropThatBreaksIntoNothingStillBreaks()
    {
        var (prop, _) = Prop(6);

        prop.OnKilled(EntityHandle.None);

        Assert.True(prop.PiecesMade == 0, "a model with no break blocks makes no debris");
        Assert.True(prop.Gone, "but the prop still goes, as it always did");
    }

    /// <summary>
    /// Breaking twice makes one set of pieces.
    ///
    /// <para>
    /// A prop hit twice in a tick, or broken by damage and then by an input,
    /// would otherwise double its debris — and the second set would be placed
    /// from an entity that had already been removed.
    /// </para>
    /// </summary>
    [Fact]
    public void APropBrokenTwiceOnlyBreaksOnce()
    {
        var (prop, engine) = Prop(7,
                                  Plank("models/plank_a.mdl", new Vector(8f, 0f, 0f)),
                                  Plank("models/plank_b.mdl", new Vector(-8f, 0f, 0f)));

        prop.OnKilled(EntityHandle.None);
        prop.OnKilled(EntityHandle.None);

        Assert.True(engine.CountOf("Entities.Create") == 2,
                    "the second break makes nothing, because there is nothing left to break");
    }

    //-------------------------------------------------------------------------
    // What a piece does once it exists.
    //-------------------------------------------------------------------------

    [Fact]
    public void AGibSchedulesItsOwnEndOnTheThinkItAlreadyHad()
    {
        var (gib, _) = Gib(8, 12.5f);

        gib.OnThink();

        Assert.True(MathF.Abs(gib.ThinkArmed - 12.5f) < 0.001f,
                    "the settle think is re-armed for the end of the gib's life rather than stopped");
    }

    [Fact]
    public void APropThatIsNotAGibStopsThinkingAfterItSettles()
    {
        var (prop, _) = Prop(9);

        prop.OnThink();

        Assert.True(prop.ThinkArmed < 0f,
                    "a crate in a map does not think for the rest of the level");
    }

    [Fact]
    public void AGibGoesWhenItsTimeIsUp()
    {
        var (gib, _) = Gib(10, 12.5f);

        gib.OnThink();   // the settle, which arms the end
        Assert.True(!gib.Gone, "a piece is still there while its time is running");

        gib.OnThink();   // the end
        Assert.True(gib.Gone, "and does not lie on the floor forever");
    }

    /// <summary>
    /// Stock's rule, from <c>BreakModelCreateSingle</c>: a fading piece with no
    /// pieces of its own is switched to DAMAGE_EVENTS_ONLY.
    ///
    /// <para>
    /// Without it a splinter is a breakable prop with one health that vanishes
    /// on the first pellet, so fifty entities' worth of debris evaporates the
    /// moment anyone shoots near it.
    /// </para>
    /// </summary>
    [Fact]
    public void AGibThatBreaksIntoNothingCannotBeHurt()
    {
        var (gib, engine) = Gib(11, 20f);

        // The MODEL gives it health, which is the whole point: without the gib
        // rule this splinter is a breakable prop and the damage lands. A version
        // of this test that left the prop data at zero passed with the rule
        // deleted, because scenery takes no damage either — it was checking the
        // wrong refusal.
        engine.Returns("EntityRender.GetModelKeyValueText", "mdlkeyvalue { prop_data { health 30 } }");

        Assert.True(gib.OnDamaged(EntityHandle.None, 50f, DamageType.Bullet) == 0,
                    "a splinter is not a breakable prop with one health");
    }

    [Fact]
    public void AGibThatHasPiecesOfItsOwnCanStillBeBroken()
    {
        var (gib, _) = Spawned(12,
                               new[] { Plank("models/splinter.mdl", new Vector(2f, 0f, 0f)) },
                               EntityNames.PropPhysics,
                               ("model", Crate),
                               (PropPhysicsVars.GibFadeTimeKey, "20"),
                               ("health", "30"));

        Assert.True(gib.OnDamaged(EntityHandle.None, 10f, DamageType.Bullet) == 10,
                    "a plank that breaks into splinters is still worth shooting");
    }
}
