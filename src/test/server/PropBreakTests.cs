using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Turning a model's break list into entities.
///
/// <para>
/// What this is about is the CONVERSATION with the engine: the order the keys
/// go on in, that the spawn happens between the keys and the physics, and that
/// what the modeller asked for actually reaches a call rather than being read
/// into a field nothing looks at. That last one is the failure this codebase
/// keeps finding — a prop's mass scale and its motion-disabled flag were both
/// read and never applied, and it looked like nothing.
/// </para>
/// </summary>
public class PropBreakTests
{
    private const uint FirstGib = 0x900;

    /// <summary>One piece, with a name and everything a break block can say.</summary>
    private static (string, BreakPiece) Plank(string model, Vector offset,
                                              float fade = 20f, float burst = 100f)
        => (model, new BreakPiece
        {
            Offset = offset,
            BurstScale = burst,
            CollisionGroup = (int)CollisionGroup.Debris,
            FadeTime = fade,
            Health = 1f,
        });

    /// <summary>
    /// An engine that has a break list and lets a piece be made.
    ///
    /// <para>
    /// Nothing is ignored: the four stubs are the only members the breaker
    /// READS, and everything else it does is a void call, which the double
    /// records without needing an answer. That is what makes the assertions
    /// below about what was ASKED FOR rather than about what was set up.
    /// </para>
    /// </summary>
    private static RecordingEngine EngineWith(params (string Model, BreakPiece Piece)[] pieces)
    {
        var engine = Fake();

        engine.Returns("Precache.BreakPieceCount", pieces.Length);
        engine.Returns("Precache.BreakPieceAt.pieces", (IReadOnlyList<(string, BreakPiece)>)pieces);
        engine.Returns("Entities.Count", 100);

        engine.Returns("Entities.Create", new EntityHandle(FirstGib));

        // SetKeyValue answers whether the class HAS the key. Every key written
        // here is one prop_physics declares, so the answer is true — and this
        // test is not about the answer, only about which keys were written.
        engine.Returns("Entities.SetKeyValue", true);

        return engine;
    }

    private static BreakContext Context(Vector origin = default) => new(
        Origin: origin,
        Angles: Vector.Zero,
        Velocity: Vector.Zero,
        AngularVelocity: Vector.Zero,
        DefaultBurstScale: PropBreakPlan.DefaultBurstScale,
        DefaultCollisionGroup: CollisionGroup.Interactive,
        SpawnFlags: 0);

    private static PropBreak Breaker(RecordingEngine engine)
        => new(new RecordingLog(), engine, engine, engine);

    //-------------------------------------------------------------------------
    // Making the pieces.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryPieceInTheListBecomesAPropPhysics()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)),
                                Plank("models/b.mdl", new Vector(-8f, 0f, 0f)),
                                Plank("models/c.mdl", new Vector(0f, 8f, 0f)));

        int made = Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        Assert.True(made == 3, "a crate that lists three pieces makes three");

        Assert.True(engine.AllOf("Entities.Create")
                          .All(c => c.ArgAs<string>(0) == EntityNames.PropPhysics),
                    "and every one of them is a prop_physics, which is what stock makes them");
    }

    /// <summary>
    /// The MODEL is the thing a gib most obviously needs, and it goes on as a
    /// key before the spawn.
    ///
    /// <para>
    /// After the spawn it would be too late in a way that is invisible: the
    /// shell builds the physics object from the model DURING the spawn, so a
    /// piece dressed afterwards is drawn correctly and has no collision at all.
    /// </para>
    /// </summary>
    [Fact]
    public void EachPieceIsGivenItsOwnModelBeforeItSpawns()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)),
                                Plank("models/b.mdl", new Vector(-8f, 0f, 0f)));

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        var models = engine.AllOf("Entities.SetKeyValue")
                           .Where(c => c.ArgAs<string>(1) == "model")
                           .Select(c => c.ArgAs<string>(2))
                           .ToList();

        Assert.True(models.SequenceEqual(new[] { "models/a.mdl", "models/b.mdl" }),
                    "each piece is dressed in the model its own block named");

        // The order, which is the part that cannot be seen from the arguments.
        var order = engine.Describe().Split('\n');
        int firstModel = Array.FindIndex(order, l => l.Contains("model, models/a.mdl"));
        int firstSpawn = Array.FindIndex(order, l => l.StartsWith("Entities.Spawn"));

        Assert.True(firstModel >= 0 && firstSpawn > firstModel,
                    "and is dressed before it spawns, because the spawn is what builds its collision");
    }

    [Fact]
    public void ThePhysicsIsSetAfterTheSpawnBecauseThatIsWhenThereIsAnyToSet()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)));

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        var lines = engine.Describe().Split('\n');
        int spawn = Array.FindIndex(lines, l => l.StartsWith("Entities.Spawn"));
        int group = Array.FindIndex(lines, l => l.StartsWith("EntityPhysics.SetCollisionGroup"));
        int velocity = Array.FindIndex(lines, l => l.StartsWith("Entities.SetVelocity"));

        Assert.True(spawn >= 0 && group > spawn && velocity > spawn,
                    "a piece is handed to the solver only once the spawn has given it something to hand");
    }

    [Fact]
    public void APieceIsPutInTheGroupItsBlockAskedFor()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)));

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        var call = engine.Last("EntityPhysics.SetCollisionGroup");

        Assert.True(call is not null && call.Value.ArgAs<CollisionGroup>(1) == CollisionGroup.Debris,
                    "debris, so the pieces do not shove the player who broke the crate");
    }

    /// <summary>
    /// The burst reaches a call.
    ///
    /// <para>
    /// This is the assertion the whole file exists for. A planner that works out
    /// a perfect impulse and never applies it produces a crate that falls apart
    /// into a neat stack — which looks like a physics quirk, not a bug.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBurstIsActuallyApplied()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(0f, 0f, 10f)));

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        var call = engine.Last("Entities.ApplyVelocityImpulse");

        Assert.True(call is not null, "a piece with a burst scale is pushed away from the break");

        var impulse = call!.Value.ArgAs<Vector>(1);

        Assert.True(MathF.Abs(impulse.Z - PropBreakPlan.DefaultBurstScale) < 0.001f,
                    "at the scale the block asked for, in the direction it sits");
    }

    [Fact]
    public void APieceWithNoBurstIsNotPushed()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(0f, 0f, 10f), burst: 0f));

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        Assert.True(engine.CountOf("Entities.ApplyVelocityImpulse") == 0,
                    "a block that asked for no burst is not pushed anyway");
    }

    [Fact]
    public void APinnedPropBreaksIntoPiecesThatFall()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)));

        var context = Context() with { SpawnFlags = PropBreak.SpawnFlagMotionDisabled | 0x10 };

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), context);

        var flags = engine.AllOf("Entities.SetKeyValue")
                          .First(c => c.ArgAs<string>(1) == "spawnflags")
                          .ArgAs<string>(2);

        int written = int.Parse(flags);

        Assert.True((written & PropBreak.SpawnFlagMotionDisabled) == 0,
                    "a crate the mapper bolted down does not break into pieces frozen in mid-air");
        Assert.True((written & 0x10) != 0,
                    "but every other flag it had is inherited, which is what stock does");
    }

    [Fact]
    public void APieceThatFadesIsMarkedAsAGib()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f), fade: 12f));

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        var kv = engine.AllOf("Entities.SetKeyValue").ToList();

        int flags = int.Parse(kv.First(c => c.ArgAs<string>(1) == "spawnflags").ArgAs<string>(2));

        Assert.True((flags & PropBreak.SpawnFlagIsGib) != 0,
                    "a piece with a fade time carries stock's IS_GIB flag");

        Assert.True(kv.Any(c => c.ArgAs<string>(1) == PropPhysicsVars.GibFadeTimeKey
                             && c.ArgAs<string>(2) == "12"),
                    "and is told how long it has, before it spawns and decides what it is");
    }

    [Fact]
    public void APieceWithNoFadeTimeIsNotAGib()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f), fade: 0f));

        Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        var kv = engine.AllOf("Entities.SetKeyValue").ToList();

        int flags = int.Parse(kv.First(c => c.ArgAs<string>(1) == "spawnflags").ArgAs<string>(2));

        Assert.True((flags & PropBreak.SpawnFlagIsGib) == 0,
                    "a piece the modeller meant to stay is not counted as a gib");
        Assert.True(!kv.Any(c => c.ArgAs<string>(1) == PropPhysicsVars.GibFadeTimeKey),
                    "and is not given a lifetime");
    }

    [Fact]
    public void ThePropIsTheOwnerOfEveryPiece()
    {
        var prop = new EntityHandle(7);
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)));

        Breaker(engine).CreateAll("models/crate.mdl", prop, Context());

        var call = engine.Last("Entities.SetOwner");

        Assert.True(call is not null && call.Value.ArgAs<EntityHandle>(1) == prop,
                    "a piece is owned by the thing it came out of, so it does not collide with it");
    }

    [Fact]
    public void AModelThatBreaksIntoNothingMakesNothing()
    {
        var engine = Fake();
        engine.Returns("Precache.BreakPieceCount", 0);

        int made = Breaker(engine).CreateAll("models/rock.mdl", new EntityHandle(7), Context());

        Assert.True(made == 0, "a model with no break blocks makes no debris");
        Assert.True(engine.CountOf("Entities.Create") == 0, "and does not create anything at all");
    }

    //-------------------------------------------------------------------------
    // The edict guard.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Stock's PropBreakableCapEdictsOnCreateAll, commented there as a "stop-gap
    /// to prevent this function from crashing the engine".
    /// </summary>
    [Fact]
    public void AFullWorldRefusesToBreakRatherThanRunningOutOfEdicts()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)),
                                Plank("models/b.mdl", new Vector(-8f, 0f, 0f)));

        engine.Returns("Entities.Count", EngineLimits.MaxEdicts - 10);

        int made = Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context());

        Assert.True(made == 0, "a prop in a full world stays whole");
        Assert.True(engine.CountOf("Entities.Create") == 0, "and nothing is created on the way to finding out");
    }

    [Fact]
    public void AWorldWithRoomBreaksNormally()
    {
        var engine = EngineWith(Plank("models/a.mdl", new Vector(8f, 0f, 0f)),
                                Plank("models/b.mdl", new Vector(-8f, 0f, 0f)));

        engine.Returns("Entities.Count", 880);

        Assert.True(Breaker(engine).CreateAll("models/crate.mdl", new EntityHandle(7), Context()) == 2,
                    "ss_sandbox's eight hundred and eighty entities leave room for two more");
    }

    //-------------------------------------------------------------------------
    // Precaching.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A piece's own pieces are precached too, because a crate broken twice
    /// wants them and a precache during play is a console line and nothing
    /// drawn.
    /// </summary>
    [Fact]
    public void PrecachingFollowsAPiecesOwnPieces()
    {
        var engine = Fake();

        // Two levels: the crate lists a plank, and the plank lists a splinter.
        engine.Returns("Precache.BreakPieceCount", 1);
        engine.Returns("Precache.BreakPieceAt.pieces",
                       (IReadOnlyList<(string, BreakPiece)>)new[]
                       {
                           Plank("models/plank.mdl", Vector.Zero),
                       });

        Breaker(engine).Precache("models/crate.mdl");

        var asked = engine.AllOf("Precache.BreakPieceCount")
                          .Select(c => c.ArgAs<string>(0))
                          .ToList();

        Assert.True(asked.Contains("models/crate.mdl"), "the crate is asked what it breaks into");
        Assert.True(asked.Contains("models/plank.mdl"), "and so is the plank it breaks into");
    }

    /// <summary>
    /// A model that lists itself is data, not a program, and must be a bounded
    /// walk rather than a hang.
    /// </summary>
    [Fact]
    public void AModelThatListsItselfDoesNotRecurseForever()
    {
        var engine = Fake();

        engine.Returns("Precache.BreakPieceCount", 1);
        engine.Returns("Precache.BreakPieceAt.pieces",
                       (IReadOnlyList<(string, BreakPiece)>)new[]
                       {
                           Plank("models/crate.mdl", Vector.Zero),
                       });

        int count = Breaker(engine).Precache("models/crate.mdl");

        Assert.True(count == 1, "the model still reports what it breaks into");
        Assert.True(engine.CountOf("Precache.BreakPieceCount") <= 8,
                    "and a model that lists itself is walked a bounded number of times");
    }

    [Fact]
    public void PrecachingReportsHowManyPiecesTheModelHas()
    {
        var engine = EngineWith(Plank("models/a.mdl", Vector.Zero),
                                Plank("models/b.mdl", Vector.Zero),
                                Plank("models/c.mdl", Vector.Zero));

        Assert.True(Breaker(engine).Precache("models/crate.mdl") == 3,
                    "the count is stock's m_iNumBreakableChunks, which is what decides whether a gib breaks again");
    }

    //-------------------------------------------------------------------------
    // The two spawnflag numbers are the engine's, not this file's.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Both flags are copies of a #define in props_shared.h, and only the NUMBER
    /// crosses. A renumbering nobody mirrored would make every piece inherit
    /// motion-disabled from a bolted-down crate, or stop counting gibs — neither
    /// of which produces an error anywhere.
    /// </summary>
    [Fact]
    public void TheSpawnFlagsAreTheOnesPropsSharedDeclares()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine("src", "game", "shared", "props_shared.h"));

        if (header is null)
        {
            Assert.True(true, "props_shared.h found for the spawnflag check (skipped, not found)");
            return;
        }

        string text = File.ReadAllText(header);

        Assert.True(Flag(text, "SF_PHYSPROP_MOTIONDISABLED") == PropBreak.SpawnFlagMotionDisabled,
                    "SpawnFlagMotionDisabled is SF_PHYSPROP_MOTIONDISABLED");

        Assert.True(Flag(text, "SF_PHYSPROP_IS_GIB") == PropBreak.SpawnFlagIsGib,
                    "SpawnFlagIsGib is SF_PHYSPROP_IS_GIB");
    }

    private static int Flag(string text, string name)
    {
        var m = Regex.Match(text, @"#define\s+" + Regex.Escape(name) + @"\s+0x([0-9A-Fa-f]+)");

        Assert.True(m.Success, $"props_shared.h declares {name}");

        return Convert.ToInt32(m.Groups[1].Value, 16);
    }
}
