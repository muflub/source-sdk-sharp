using System;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The game DLL's managed answer: the tick interval read once, the four load
/// arms, and Part 6's six settable answers.
///
/// <para>
/// Built with <c>new</c> and handed what it needs — no container, so a failure
/// here is the object's and not the wiring's.
/// </para>
/// </summary>
public class ServerGameDllTests
{
    // The frame's I/O route, one per fact (plan_abi_hotreload D7: it was static).
    private readonly EntityIoRoutes _io = new();

    /// <summary>
    /// A real entity lump — braces and all — rather than the bare word
    /// <c>worldspawn</c> it used to be.
    ///
    /// <para>
    /// It had to become one at plan_entity.md step 2d: <c>MapSpawner</c> SPLITS
    /// the lump now and hands the native parse the blocks whose class the managed
    /// registry does not own, so a fixture that is not lump-shaped splits into no
    /// blocks and nothing reaches the parse. Two entities, so a fact can tell
    /// "all of it" from "some of it".
    /// </para>
    /// </summary>
    private static readonly byte[] Lump =
        """
        {
        "classname" "worldspawn"
        "message" "Lockdown"
        }
        {
        "classname" "info_node"
        "origin" "1 2 3"
        }
        """u8.ToArray();

    /// <summary>
    /// The object under test with no <c>-tickrate</c> on the command line.
    ///
    /// <para>
    /// <c>Ignores</c> nothing and sets the command line it reads,
    /// because the read is what half these facts are about. The environment is
    /// the same strict double, and nothing here asks it anything — which is
    /// itself checked below.
    /// </para>
    /// </summary>
    private (ServerGameDll Dll, RecordingEngine Engine) Build(string? tickRate = null)
        => Build(tickRate, out _);

    private (ServerGameDll Dll, RecordingEngine Engine) Build(string? tickRate, out RecordingCommandLine commandLine)
    {
        var engine = Fake();
        commandLine = tickRate is null
            ? new RecordingCommandLine("srcds")
            : new RecordingCommandLine("srcds", "-tickrate", tickRate);

        // The clock GameFrame services the entity I/O queue against -- the
        // sub-ioqueue lane. A free read of gpGlobals in the real build; a
        // stubbed answer here, because a strict recording engine refuses an
        // unstubbed one rather than inventing a time.
        engine.Returns("Host.CurrentTime", 0f);

        // The savegame arm asks the engine for the saved state; with no engine
        // there is nothing to restore, and TRUE is the answer that keeps the
        // facts below about the ORDER rather than about the warning a false
        // prints. The one fact that IS about the false says so and overrides it.
        engine.Returns("Level.LoadSaveGameState", true);

        // The particle manifests are read in C# now (plan_server_novalve.md
        // P2.1). No file anywhere: the facts here are about the load's ORDER,
        // and ParticleManifestTests is where the manifest's own rules are held.
        engine.Returns("LevelFiles.ReadFileOnPath", null);

        // The world arrives parsed now (plan_server_novalve.md P5) and native
        // answers whether it was built. TRUE keeps these facts about the ORDER.
        engine.Returns("Level.SpawnWorld", true);

        // The model-cache walk ANSWERS A COUNT now (lane diag-visibility), and a
        // strict double refuses an unstubbed answer rather than inventing one.
        // 0 rather than a number, deliberately: every fact below is about the
        // walk being REACHED, and the one fact that is about the number says so
        // and overrides this.
        engine.Returns("Entities.InvalidateMdlCacheAll", 0);

        // The same strict double answers for all seven areas, so the ORDER of
        // the load steps is recorded on the same call list as everything else
        // this object does -- which is what lets a fact assert that the purge
        // precedes the parse rather than merely that both happened.
        var (stringTables, modTable) = TestStringTables.For(engine);

        var dll = new ServerGameDll(new RecordingLog(), engine, new ConVarRegistry(TestServices.UnattachedConsole()), engine, commandLine, engine, engine, engine, engine,
                                    stringTables, modTable,
                                    // Phase A3: the lifecycle driver MapLoadPreEntity
                                    // dispatches into. An empty container here, so the
                                    // pass reaches no participant -- these facts are
                                    // about the engine steps, and a driver with
                                    // participants in it would put another object's
                                    // calls on the same recording list.
                                    new LifecycleDriver(new RecordingLog(), new ServiceContainer()),
                                    // The template store the precache step drains.
                                    // Empty, so the step is recorded with nothing in
                                    // it -- these facts are about the ORDER.
                                    new TemplateStore(new RecordingLog()),
                                    // The map spawner, plan_entity.md step 2d.
                                    // Over an EMPTY registry, so it owns no
                                    // classname, every block in the lump is
                                    // residue, and the native parse still gets
                                    // all of it -- which is what these facts about
                                    // the ORDER of the nine steps are about.
                                    TestMapSpawner.Over(engine, engine),
                                    // The I/O route the frame services, this
                                    // fact's own (plan_abi_hotreload D7).
                                    _io);
        return (dll, engine);
    }

    // ---- IServerLifetime: the tick interval, read once ----------------------

    [Fact]
    public void WithNoTickRateOnTheCommandLineTheIntervalIsStocksDefault()
    {
        var (dll, _) = Build();

        Assert.True(dll.TickInterval == ServerGameDll.DefaultTickInterval,
            "a server with no -tickrate runs at DEFAULT_TICK_INTERVAL");
    }

    [Fact]
    public void ATickRateAboveTenSetsTheInterval()
    {
        var (dll, _) = Build("66");

        Assert.True(dll.TickInterval == 1f / 66f, "-tickrate 66 is one sixty-sixth of a second per tick");
    }

    /// <summary>
    /// Stock's guard is <c>tickrate &gt; 10</c>, not <c>&gt;=</c>. It exists
    /// because the whole engine divides by this number, and relaxing the
    /// comparison in a transliteration would change behaviour at exactly the
    /// value somebody typed deliberately.
    /// </summary>
    [Fact]
    public void ATickRateOfExactlyTenIsIgnored()
    {
        var (dll, _) = Build("10");

        Assert.True(dll.TickInterval == ServerGameDll.DefaultTickInterval,
            "-tickrate 10 is refused and the default stands");
    }

    [Fact]
    public void ATickRateJustAboveTenIsHonoured()
    {
        var (dll, _) = Build("11");

        Assert.True(dll.TickInterval == 1f / 11f, "-tickrate 11 is on the other side of stock's guard");
    }

    [Fact]
    public void ATickRateThatIsNotANumberLeavesTheDefaultStanding()
    {
        var (dll, _) = Build("fast");

        Assert.True(dll.TickInterval == ServerGameDll.DefaultTickInterval,
            "an unparseable -tickrate is refused rather than read as zero and divided by");
    }

    /// <summary>
    /// Read ONCE. Stock's <c>GetTickInterval</c> is a <c>const</c> method the
    /// engine asks exactly once; a tick interval that changed mid-run would mean
    /// every duration already computed against the old one silently means
    /// something else.
    /// </summary>
    [Fact]
    public void TheTickIntervalIsReadOnceAndDoesNotFollowTheCommandLineAfterwards()
    {
        var (dll, _) = Build("66", out var commandLine);

        commandLine.Set("srcds", "-tickrate", "20");

        Assert.True(dll.TickInterval == 1f / 66f,
            "the tick interval is what it was at construction and is not re-read");
    }

    [Fact]
    public void TheCommandLineIsReadOnceAndNotOncePerAsk()
    {
        var (dll, _) = Build("66", out var commandLine);

        _ = dll.TickInterval;
        _ = dll.TickInterval;

        Assert.True(commandLine.Reads == 1,
            "the command line is read in the constructor and never again");
    }

    // ---- IServerLifetime: coming up and going down --------------------------

    [Fact]
    public void SystemStartSaysCarryOn()
    {
        var (dll, _) = Build();

        Assert.True(dll.SystemStart(), "the managed side lets DLLInit finish");
    }

    [Fact]
    public void SystemStartIsRecorded()
    {
        var (dll, _) = Build();
        dll.SystemStart();

        Assert.True(dll.IsSystemStarted, "and it knows it has started");
    }

    [Fact]
    public void GameStartSaysCarryOn()
    {
        var (dll, _) = Build();

        Assert.True(dll.GameStart(), "the managed side lets GameInit finish");
    }

    [Fact]
    public void GameStopUnwindsBothStarts()
    {
        var (dll, _) = Build();
        dll.SystemStart();
        dll.GameStart();

        dll.GameStop();

        Assert.True(!dll.IsSystemStarted && !dll.IsGameStarted, "GameShutdown leaves neither standing");
    }

    // ---- IServerLevel: the four arms ---------------------------------------

    [Fact]
    public void ANewGameLoadRecordsItsMap()
    {
        var (dll, _) = Build();
        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        Assert.True(dll.CurrentMap == "de_dust", "a fresh load names the map that is up");
    }

    [Fact]
    public void ANewGameLoadRecordsItsLoadType()
    {
        var (dll, _) = Build();
        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        Assert.True(dll.LoadType == MapLoadType.NewGame, "and how it arrived");
    }

    /// <summary>
    /// A background map differs from a fresh one ONLY in the load type — which
    /// is why the shell has four arms rather than a flag.
    /// </summary>
    [Fact]
    public void ABackgroundLoadDiffersFromAFreshOneOnlyInItsType()
    {
        var (dll, _) = Build();
        dll.MapLoadBackground(new MapLoad("background01", Lump));

        Assert.True(dll.LoadType == MapLoadType.Background && dll.CurrentMap == "background01",
            "a background map is a fresh load with a different load type");
    }

    [Fact]
    public void ATransitionRecordsTheTransitionLoadType()
    {
        var (dll, _) = Build();
        dll.MapLoadTransition(new MapLoad("d1_town_02", Lump, "d1_town_01", "town_gate"));

        Assert.True(dll.LoadType == MapLoadType.Transition, "a level change through a landmark is a transition");
    }

    [Fact]
    public void ASaveGameLoadRecordsTheLoadGameType()
    {
        var (dll, _) = Build();
        dll.MapLoadSaveGame(new MapLoad("d1_town_01", Lump));

        Assert.True(dll.LoadType == MapLoadType.LoadGame, "a restored save is a LoadGame");
    }

    /// <summary>
    /// <b>THE SAVEGAME ARM SPAWNS THE MAP, and it is the only arm whose order
    /// is not stock's.</b>
    ///
    /// <para>
    /// Stock does not parse the lump here because ITS restore re-creates every
    /// entity out of the save's entity table. This port writes no entity table
    /// and its records are the managed HALF of an entity — nothing in a
    /// <c>SavedEntity</c> could bring a <c>prop_physics</c> into existence — so
    /// the world has to come from the lump and the save is laid over it.
    /// Measured with this arm doing nothing: <b>0 entities</b>, and the
    /// reconnecting player refused because the game rules are installed by
    /// <c>CWorld::Spawn</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void ASaveGameLoadSpawnsTheMapAndThenAsksTheEngineForTheSavedState()
    {
        var (dll, engine) = Build();

        dll.MapLoadSaveGame(new MapLoad("d1_town_01", Lump));

        string[] steps = StepsOf(engine);

        Assert.Equal("Level.ResetServerTime", steps[0]);
        Assert.Equal("Level.LoadSaveGameState", steps[^1]);
    }

    /// <summary>
    /// <b>THE RESTORE IS LAST, and that ORDER is the whole design.</b>
    ///
    /// <para>
    /// <c>engine-&gt;LoadGameState</c> drives <c>Restore</c> synchronously, and
    /// <c>Restore</c> lays managed state onto entities that must already exist.
    /// Asking for the state before the lump was parsed would restore onto an
    /// empty world; it would also answer a failed load with a server that has
    /// no world in it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSavedStateIsAskedForAFTERTheLumpIsParsed()
    {
        var (dll, engine) = Build();

        dll.MapLoadSaveGame(new MapLoad("d1_town_01", Lump));

        string[] steps = StepsOf(engine);

        Assert.True(Array.IndexOf(steps, "Level.SpawnWorld")
                  < Array.IndexOf(steps, "Level.LoadSaveGameState"),
            "the map is spawned before the save is read back over it");
    }

    /// <summary>
    /// An engine that could not restore leaves the map standing. FALSE from
    /// this arm would drop every client; a new-looking map plus a warning is a
    /// failure a player can see and keep playing through.
    /// </summary>
    [Fact]
    public void AnEngineThatCannotRestoreStillLeavesTheMapLoaded()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.LoadSaveGameState", false);

        Assert.True(dll.MapLoadSaveGame(new MapLoad("d1_town_01", Lump)),
            "a failed restore is not a failed map load");
        Assert.True(dll.LastLoadSpawnedTheMap, "and the map is still there");
        Assert.False(dll.RestoredFromSave, "but it says the save did not come back");
    }

    [Fact]
    public void ASuccessfulSaveGameLoadSaysTheSaveCameBack()
    {
        var (dll, _) = Build();

        dll.MapLoadSaveGame(new MapLoad("d1_town_01", Lump));

        Assert.True(dll.RestoredFromSave);
    }

    [Fact]
    public void EveryArmLetsTheMapLoad()
    {
        var (dll, _) = Build();

        Assert.True(dll.MapLoadNewGame(new MapLoad("a", Lump))
                 && dll.MapLoadBackground(new MapLoad("b", Lump))
                 && dll.MapLoadTransition(new MapLoad("c", Lump))
                 && dll.MapLoadSaveGame(new MapLoad("d", Lump)),
            "none of the four arms aborts the load");
    }

    // ---- Part 4: the LevelInit ORDER, which is this class's whole job -------
    //
    // Every fact below asserts a POSITION, not that a step happened. "The map
    // loaded and nine calls were made" is true of nine calls in any order, and
    // three of these positions are load-bearing: purge before parse, templates
    // after parse, cleanup last. Read off the strict double's call log, which
    // records every area on one list precisely so an order across two areas
    // (Level and Entities) is still one sequence.

    /// <summary>The steps a fresh map load makes, in the order it made them.</summary>
    private static string[] StepsOf(RecordingEngine engine)
        => engine.Calls
            .Select(c => c.Member)
            .Where(n => n.StartsWith("Level.", StringComparison.Ordinal)
                     || n == "Entities.PurgeMapRefs"
                     || n == "Entities.CleanupDeleteList")
            .ToArray();

    [Fact]
    public void AFreshMapLoadRunsStocksNineStepsInStocksOrder()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        Assert.Equal(new[]
        {
            "Level.ResetServerTime",
            // The two particle manifest steps were here; they are read in C#
            // now (plan_server_novalve.md P2.1), through LevelFiles, between
            // these two -- see TheParticleManifestsAreReadBetweenTheClockAndThePurge.
            "Entities.PurgeMapRefs",
            "Level.SpawnWorld",
            // SetLowViolence (gameinterface.cpp:1049) BEFORE
            // PrecachePointTemplates (:1053). This pair was pinned the other
            // way round here for the life of this file, under this method's own
            // name -- the code and the test agreed with each other and neither
            // agreed with stock. tools/gate5 found it on its first real run.
            "Level.SetLowViolence",
            "Level.PrecachePointTemplates",
            "Level.LoadMessageOfTheDay",
            "Entities.CleanupDeleteList",
        }, StepsOf(engine));
    }

    /// <summary>
    /// The pair that was inverted, held on its own so that a failure names it.
    ///
    /// <para>
    /// <c>g_RagdollLVManager.SetLowViolence( pMapName )</c> is
    /// gameinterface.cpp:1049 and <c>PrecachePointTemplates()</c> is :1053. The
    /// whole-sequence fact above would catch a re-inversion, but it would
    /// report it as "nine strings differ from nine strings"; this one reports
    /// the two that moved.
    /// </para>
    /// </summary>
    [Fact]
    public void LowViolenceIsSetBeforeThePointTemplatesArePrecached()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        var steps = StepsOf(engine);

        Assert.True(Array.IndexOf(steps, "Level.SetLowViolence")
                  < Array.IndexOf(steps, "Level.PrecachePointTemplates"),
            "stock sets low violence (gameinterface.cpp:1049) before it precaches "
            + "point templates (:1053); this pair was inverted here and tools/gate5 found it");
    }

    /// <summary>
    /// The parse's filter FILLS <c>g_MapEntityRefs</c> as it creates each
    /// entity, so a purge after it throws away exactly what was just recorded.
    /// </summary>
    [Fact]
    public void TheMapRefsArePurgedBeforeTheParseAndNotAfterIt()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        var steps = StepsOf(engine);

        Assert.True(Array.IndexOf(steps, "Entities.PurgeMapRefs")
                  < Array.IndexOf(steps, "Level.SpawnWorld"),
            "the map-entity reference list is purged before the parse fills it");
    }

    /// <summary>
    /// A <c>point_template</c>'s parameters cannot be read before the template
    /// entity exists, and the parse is what makes it exist.
    /// </summary>
    [Fact]
    public void ThePointTemplatesArePrecachedAfterTheEntitiesExist()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        var steps = StepsOf(engine);

        Assert.True(Array.IndexOf(steps, "Level.SpawnWorld")
                  < Array.IndexOf(steps, "Level.PrecachePointTemplates"),
            "point templates are precached after the entities they read");
    }

    /// <summary>
    /// Anything above it can leave an entity that removed itself during its own
    /// precache, so the reap is last or it misses some of them.
    /// </summary>
    [Fact]
    public void TheDeleteListIsCleanedUpLast()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        var steps = StepsOf(engine);

        Assert.True(steps.Length > 0 && steps[^1] == "Entities.CleanupDeleteList",
            "the delete list is reaped after every step that can add to it");
    }

    /// <summary>
    /// <b>The step the map is made of.</b> Without it the server runs an empty
    /// world that boots, ticks and reports nothing wrong.
    ///
    /// <para>
    /// <b>What reaches it changed at plan_entity.md step 2d</b> and this fact
    /// changed with it. It used to be the WHOLE lump, and the assertion was a
    /// length; it is now the blocks whose class the managed registry does not
    /// own, so the assertion is about CONTENT — this spawner is over an empty
    /// registry, so that is every block, and both of the fixture's entities have
    /// to be in what arrived.
    /// </para>
    /// </summary>
    /// <summary>
    /// plan_server_novalve.md P5: native no longer parses the residue. The world
    /// crosses as its pairs and the rest of the residue does not cross at all --
    /// it is declared as unanswered, which MapSpawnerTests covers.
    /// </summary>
    [Fact]
    public void TheWorldReachesNativeAndTheRestOfTheResidueDoesNot()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        string world = WorldKeys(engine);

        Assert.True(world.Contains("classname=worldspawn") && !world.Contains("info_node"),
            $"the world crosses and info_node does not -- got: {world}");
    }

    /// <summary>
    /// The keys go down with the world. A hand-off that kept the classname and
    /// dropped everything else would pass the fact above and build a world with
    /// no properties.
    /// </summary>
    [Fact]
    public void TheWorldReachesNativeWithItsKeys()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        Assert.Equal("classname=worldspawn;message=Lockdown", WorldKeys(engine));
    }

    /// <summary>The world's pairs native was last handed, as key=value joined by ';'.</summary>
    private static string WorldKeys(RecordingEngine engine)
        => engine.Last("Level.SpawnWorld") is { } call
            ? string.Join(';', call.ArgAs<KeyValuePair<string, string>[]>(0)
                                   .Select(kv => kv.Key + "=" + kv.Value))
            : string.Empty;

    /// <summary>
    /// Both particle manifests, and the map's one by the map's name — read in
    /// C# now (plan_server_novalve.md P2.1): the game's, then the map's three
    /// candidates in stock's order.
    /// </summary>
    [Fact]
    public void TheMapsOwnParticleManifestIsParsedForThatMap()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        var reads = engine.Calls.Where(c => c.Member == "LevelFiles.ReadFileOnPath")
                                .Select(c => c.ArgAs<string>(1) + ":" + c.ArgAs<string>(0))
                                .ToArray();
        Assert.Equal(new[]
        {
            "GAME:particles/particles_manifest.txt",
            "BSP:particles.txt",
            "BSP:maps/de_dust_particles.txt",
            "GAME:maps/de_dust_particles.txt",
        }, reads);
    }

    /// <summary>The manifests are read where stock's two steps stood.</summary>
    [Fact]
    public void TheParticleManifestsAreReadBetweenTheClockAndThePurge()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        var members = engine.Calls.Select(c => c.Member).ToList();
        int clock = members.IndexOf("Level.ResetServerTime");
        int read = members.IndexOf("LevelFiles.ReadFileOnPath");
        int purge = members.IndexOf("Entities.PurgeMapRefs");
        Assert.True(clock >= 0 && clock < read && read < purge,
            "reset the clock, read the manifests, then purge the map refs");
    }

    [Fact]
    public void TheLowViolenceSettingIsCheckedForThisMap()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        Assert.True(engine.Last("Level.SetLowViolence") is { } call
                 && call.ArgAs<string>(0) == "de_dust",
            "low violence is a per-map setting and is asked per map");
    }

    /// <summary>
    /// A background map differs from a fresh one ONLY in <c>eLoadType</c>, and
    /// the shell has already assigned that before this class is reached — so
    /// the step list has to be identical, not merely similar.
    /// </summary>
    [Fact]
    public void ABackgroundMapRunsTheSameNineStepsAsAFreshOne()
    {
        var (fresh, freshEngine) = Build();
        var (background, backgroundEngine) = Build();

        fresh.MapLoadNewGame(new MapLoad("de_dust", Lump));
        background.MapLoadBackground(new MapLoad("de_dust", Lump));

        Assert.Equal(StepsOf(freshEngine), StepsOf(backgroundEngine));
    }

    // ---- Part 7: the two refused arms, which must spawn NOTHING -------------

    /// <summary>
    /// A transition restores entities out of a save. That is the save cluster,
    /// which Part 7 refuses — and a refusal that ran the fresh-load steps
    /// anyway would spawn the map's entities on top of a restore that never
    /// happened, which is worse than refusing.
    /// </summary>
    [Fact]
    public void ARefusedTransitionSpawnsNothingAtAll()
    {
        var (dll, engine) = Build();

        dll.MapLoadTransition(new MapLoad("d1_town_02", Lump, "d1_town_01", "town_gate"));

        Assert.Empty(StepsOf(engine));
    }

    /// <summary>
    /// <b>THE SAVEGAME ARM NO LONGER HAS A TWIN HERE, and its absence is the
    /// change.</b> This was <c>ARefusedSaveGameLoadSpawnsNothingAtAll</c>,
    /// asserting an empty step list; stage 2 makes that arm spawn the map and
    /// then read the save back over it, so the fact that replaces it is
    /// <c>ASaveGameLoadSpawnsTheMapAndThenAsksTheEngineForTheSavedState</c>.
    /// The transition arm above still refuses and still spawns nothing.
    /// </summary>
    [Fact]
    public void TheSavegameArmIsNoLongerOneOfTheArmsThatSpawnNothing()
    {
        var (dll, engine) = Build();

        dll.MapLoadSaveGame(new MapLoad("d1_town_01", Lump));

        Assert.NotEmpty(StepsOf(engine));
    }

    /// <summary>
    /// The refusal is visible from outside, so "the level came up empty" has an
    /// answer other than reading the log.
    /// </summary>
    [Fact]
    public void OnlyTheTransitionArmFailsToSpawnTheMap()
    {
        var (a, _) = Build();
        var (b, _) = Build();
        var (c, _) = Build();
        var (d, _) = Build();

        a.MapLoadNewGame(new MapLoad("m", Lump));
        b.MapLoadBackground(new MapLoad("m", Lump));
        c.MapLoadTransition(new MapLoad("m", Lump, "old", "lm"));
        d.MapLoadSaveGame(new MapLoad("m", Lump));

        // THE SAVEGAME ARM IS TRUE NOW, and that one flip is stage 2. It was
        // false for the whole of stage 1 and the world it produced was empty.
        Assert.Equal(new[] { true, true, false, true },
            new[] { a.LastLoadSpawnedTheMap, b.LastLoadSpawnedTheMap,
                    c.LastLoadSpawnedTheMap, d.LastLoadSpawnedTheMap });
    }

    /// <summary>
    /// A refused arm still returns <c>true</c>. <c>false</c> from
    /// <c>LevelInit</c> means "the map failed to load", which drops every client
    /// with no explanation; the warning plus an empty level is a refusal a
    /// player can see.
    /// </summary>
    [Fact]
    public void ARefusedArmStillLetsTheLevelComeUp()
    {
        var (dll, _) = Build();

        Assert.True(dll.MapLoadTransition(new MapLoad("m", Lump, "old", "lm")),
            "a refusal is not a failed map load");
    }

    /// <summary>
    /// The one measurement taken of the borrowed span, taken inside the call
    /// because that is the only place it is valid. The lump itself is not kept
    /// and cannot be — <see cref="MapLoad"/> is a <c>ref struct</c>.
    /// </summary>
    [Fact]
    public void TheEntityLumpSizeIsTakenWhileTheSpanIsStillValid()
    {
        var (dll, _) = Build();
        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        Assert.True(dll.EntityLumpBytes == Lump.Length, "the load records how big the entity lump was");
    }

    [Fact]
    public void UnloadingAMapForgetsIt()
    {
        var (dll, _) = Build();
        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        dll.MapUnload();

        Assert.True(dll.CurrentMap.Length == 0, "there is no map up between levels");
    }

    [Fact]
    public void UnloadingAMapForgetsItsLump()
    {
        var (dll, _) = Build();
        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));

        dll.MapUnload();

        Assert.True(dll.EntityLumpBytes == 0, "and no entity lump either");
    }

    // ---- IServerFrame ------------------------------------------------------

    /// <summary>
    /// The two stubs are Phase 7's, and they are why this fact FOLLOWED the work
    /// rather than being weakened: the frame has a body now, so the strict double
    /// refuses it until the two questions it asks the engine are answered.
    /// </summary>
    [Fact]
    public void AFrameIsCounted()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);
        dll.GameFrame(true);

        Assert.True(dll.FramesRun == 2, "the game DLL sees each frame once");
    }

    /// <summary>
    /// <b>Both directions, because one of them cannot fail.</b> As first written
    /// this asserted only <c>!WasSimulating</c> after <c>GameFrame(false)</c> —
    /// which is also what a <c>GameFrame</c> that ignored its argument entirely
    /// answers, since the field starts false. Deleting the assignment left the
    /// test green. Asserting the flag FOLLOWS the argument is the check.
    /// </summary>
    [Fact]
    public void TheSimulatingFlagFollowsTheFrameRatherThanStayingWhereItStarted()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);
        bool afterSimulating = dll.WasSimulating;

        dll.GameFrame(false);

        Assert.True(afterSimulating && !dll.WasSimulating,
            "a paused frame is carried through as one, and a simulating frame as one too");
    }

    // ---- IServerListing: Part 6's six --------------------------------------

    [Fact]
    public void TheGameDescriptionHasAStockAnswer()
    {
        var (dll, _) = Build();

        Assert.True(dll.GameDescription == "SourceSharp", "a mod that sets nothing is still named");
    }

    [Fact]
    public void TheGameDescriptionIsWhatAModSet()
    {
        var (dll, _) = Build();
        dll.GameDescription = "Half-Life 2 Deathmatch";

        Assert.True(((IServerListing)dll).GameDescription == "Half-Life 2 Deathmatch",
            "the answer a mod set is the answer the interface gives");
    }

    [Fact]
    public void AServerIsListedUnlessSomethingHidesIt()
    {
        var (dll, _) = Build();

        Assert.True(!dll.ShouldHideServer, "stock's answer: the server appears in the master list");
    }

    [Fact]
    public void AHiddenServerSaysSo()
    {
        var (dll, _) = Build();
        dll.ShouldHideServer = true;

        Assert.True(((IServerListing)dll).ShouldHideServer, "and a mod can take it out of the list");
    }

    [Fact]
    public void TheBrowserShowsTheRealMapUntilSomethingOverridesIt()
    {
        var (dll, _) = Build();

        Assert.True(dll.BrowserMapOverride is null, "null means the engine uses the real map name");
    }

    [Fact]
    public void ABrowserMapOverrideIsCarried()
    {
        var (dll, _) = Build();
        dll.BrowserMapOverride = "The Docks";

        Assert.True(((IServerListing)dll).BrowserMapOverride == "The Docks", "and a mod can name it something else");
    }

    [Fact]
    public void ThereIsNoBrowserGameDataUntilSomethingSetsSome()
    {
        var (dll, _) = Build();

        Assert.True(dll.BrowserGameData is null, "null means the browser entry carries no game data");
    }

    [Fact]
    public void AManualMapChangeIsAllowedByDefault()
    {
        var (dll, _) = Build();

        Assert.True(dll.IsManualMapChangeOkay(out _), "stock's answer: an operator may change the map");
    }

    [Fact]
    public void AnAllowedManualMapChangeCarriesNoReason()
    {
        var (dll, _) = Build();
        dll.IsManualMapChangeOkay(out string? reason);

        Assert.True(reason is null, "there is no reason to give when nothing is refused");
    }

    [Fact]
    public void ARefusedManualMapChangeIsRefused()
    {
        var (dll, _) = Build();
        dll.ManualMapChangeRefusal = "a match is in progress";

        Assert.True(!dll.IsManualMapChangeOkay(out _), "a refusal reason refuses the change");
    }

    [Fact]
    public void ARefusedManualMapChangeCarriesItsReason()
    {
        var (dll, _) = Build();
        dll.ManualMapChangeRefusal = "a match is in progress";
        dll.IsManualMapChangeOkay(out string? reason);

        Assert.True(reason == "a match is in progress", "and the engine is told why");
    }

    // ---- the seam itself ---------------------------------------------------

    /// <summary>
    /// One class, four interfaces. The interfaces are what the bootstrap's
    /// thunks land on, so a split of this class in Part 13 must not move a
    /// member off the interface that declares it.
    /// </summary>
    [Fact]
    public void OneClassAnswersAllFiveInterfaces()
    {
        var (dll, _) = Build();

        Assert.True(dll is IServerLifetime and IServerLevel and IServerFrame and IServerListing
                        and IServerHosting,
            "ServerGameDll implements the five interfaces the shell's slots fan out into");
    }

    // ---- IServerHosting: plan_gamedll.md Part 10's three loose ends ---------

    [Fact]
    public void NoCvarQueryHasComeBackBeforeOneIsAnswered()
    {
        var (dll, _) = Build();

        Assert.True(dll.LastQueriedCvar is null,
            "nothing in this build issues a StartQueryCvarValue, so the managed half "
            + "of that evidence starts empty");
    }

    [Fact]
    public void AnAnsweredCvarQueryIsKeptWithItsCookieAndPlayer()
    {
        var (dll, _) = Build();

        dll.QueryCvarValueFinished(7, 3, QueryCvarStatus.ValueIntact, "cl_interp", "0.1");

        Assert.True(dll.LastQueriedCvar == new QueriedCvar(7, 3, QueryCvarStatus.ValueIntact,
                                                           "cl_interp", "0.1"),
            "the cookie, the player and the value all survive the crossing — a query "
            + "answer without its cookie cannot be paired with the question");
    }

    [Fact]
    public void ACvarTheClientDoesNotHaveKeepsANullValueRatherThanAnEmptyOne()
    {
        var (dll, _) = Build();

        dll.QueryCvarValueFinished(1, 2, QueryCvarStatus.CvarNotFound, "cl_nonsense", null);

        Assert.True(dll.LastQueriedCvar?.Value is null,
            "\"the client has no such cvar\" is not \"the client has an empty one\", and "
            + "collapsing the two would make a missing cvar read as a blank setting");
    }

    [Fact]
    public void AServerIsNotHibernatingUntilItIsToldItIs()
    {
        var (dll, _) = Build();

        Assert.True(!dll.IsHibernating,
            "a listen server never hibernates, so the flag starts false and stays there");
    }

    [Fact]
    public void HibernationIsCarriedBothWays()
    {
        var (dll, _) = Build();

        dll.SetHibernating(true);
        bool onceOn = dll.IsHibernating;
        dll.SetHibernating(false);

        Assert.True(onceOn && !dll.IsHibernating,
            "hibernation is a state the engine sets and clears — it is NOT the inverse "
            + "of ServerActivate, which fires once per map and never clears");
    }

    [Fact]
    public void TheStatusLinesNameTheMapWhenThereIsOne()
    {
        var (dll, _) = Build();

        dll.MapLoadNewGame(new MapLoad("dm_lockdown", Lump));

        Assert.Contains(dll.StatusLines!, line => line.Contains("dm_lockdown"));
    }

    [Fact]
    public void TheStatusLinesSayThereIsNoMapRatherThanSayingNothing()
    {
        var (dll, _) = Build();

        Assert.Contains(dll.StatusLines!, line => line.Contains("no map loaded"));
    }

    [Fact]
    public void AModsExtraStatusLinesAreCarriedThrough()
    {
        var (dll, _) = Build();

        dll.ExtraStatusLines.Add("gamemode: deathmatch");

        Assert.Contains(dll.StatusLines!, line => line == "gamemode: deathmatch");
    }

    [Fact]
    public void NoStatusLineCarriesItsOwnNewlineBecauseTheShellSplitsOnThem()
    {
        var (dll, _) = Build();

        dll.MapLoadNewGame(new MapLoad("dm_lockdown", Lump));
        dll.ExtraStatusLines.Add("gamemode: deathmatch");

        Assert.True(dll.StatusLines!.All(line => !line.Contains('\n')),
            "the lines are joined with '\\n' for the crossing and split back apart by "
            + "the shell, so a line that contains one silently becomes two");
    }

    /// <summary>
    /// It HOLDS the environment rather than reaching for one — Part 0a #1's whole
    /// claim is that there is a single acquisition point — and it asks it nothing
    /// during construction, which the strict double would otherwise report.
    /// </summary>
    [Fact]
    public void ItHoldsTheEnvironmentAndAsksItNothingToStartWith()
    {
        var (dll, engine) = Build();

        Assert.True(dll.Environment is not null && engine.CountOf("Environment.GetInterface") == 0,
            "the game DLL holds the environment and acquires nothing at construction");
    }
    // ---- plan_gamedll.md Phase 7 --------------------------------------------
    //
    // THE FRAME, SERVERACTIVATE AND LEVELSHUTDOWN, ASSERTED AS ORDERS.
    //
    // Every fact below reads the strict double's ONE call list, which is what
    // lets a sequence spanning three areas (Frame, Level, Entities) be asserted
    // as a single order rather than as three sets. "These calls happened" is
    // true of the same calls in any order, and the order is the entire content
    // of two of these three methods.

    /// <summary>Every step of the frame, in the order it is recorded.</summary>
    private static string[] FrameStepsOf(RecordingEngine engine)
        => engine.Calls
            .Select(c => c.Member)
            .Where(n => n.StartsWith("Frame.", StringComparison.Ordinal)
                     || n == "Entities.CleanupDeleteList")
            .ToArray();

    /// <summary>
    /// <b>The whole of stock's <c>GameFrame</c>, as a sequence.</b>
    /// gameinterface.cpp:1319-1418, with the alternate-tick arm not taken.
    /// </summary>
    [Fact]
    public void AFrameRunsStocksStepsInStocksOrder()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        Assert.Equal(new[]
        {
            "Frame.IsSimulatingOnAlternateTicks",
            "Frame.SetServerCurTime",
            "Frame.GetFrameTime",
            "Entities.CleanupDeleteList",
            "Frame.ServerSystemsPreThink",
            "Frame.GamestatsUpdateConnection",
            // THREE OF STOCK'S STEPS ARE NOT HERE, and each is a retired
            // IShimFrame slot rather than a step this port forgot --
            // plan_shim.md 5 3a, lane 3b-enginebind, rows in
            // docs/abi-retirements.md:
            //
            //   PhysicsRunThinkFunctions was here, between the two system
            //   passes. physics_main.cpp left the vpc and the thinks are
            //   managed: they drain inside ServerSystemsPostThink, on the next
            //   line, from StandaloneHierarchy's hook.
            //
            //   ServiceEventQueue was between ServerSystemsPostThink and the
            //   second reap. cbase.cpp defined the queue and both producers
            //   left the vpc; EntityIoRoutes.Service is the one I/O point and
            //   it is at stock's position.
            //
            //   FireNetworkPropertyEvents was between EndGameFrame and
            //   SetFrameTime. g_NetworkPropertyEventMgr went at Phase 1d and
            //   nothing could ever have registered with it.
            "Frame.ServerSystemsPostThink",
            "Entities.CleanupDeleteList",
            "Frame.UpdateAllClientData",
            "Frame.EndGameFrame",
            "Frame.SetFrameTime",
        }, FrameStepsOf(engine));
    }

    /// <summary>
    /// The two system passes run in that order, and NOTHING RUNS BETWEEN THEM.
    ///
    /// <para>
    /// <b>This assertion used to be a different one</b> and the difference is
    /// worth stating rather than losing. It read "PreThink, then
    /// PhysicsRunThinkFunctions, then PostThink" — the entity thinks bracketed
    /// by the two passes, which is the whole reason they are named
    /// PreEntityThink and PostEntityThink. That landmark is gone:
    /// PhysicsRunThinkFunctions is a retired IShimFrame slot (lane
    /// 3b-enginebind), because physics_main.cpp left the vpc and the thinks are
    /// managed now — they drain through EntityDirectory.RunFrame from
    /// StandaloneHierarchy's hook INSIDE ServerSystemsPostThink.
    /// </para>
    ///
    /// <para>
    /// So the thinks' position is no longer observable from the frame's step
    /// order at all — it is inside a step, and a recording engine sees one
    /// call. What IS still observable, and is what this test now pins, is that
    /// the passes are adjacent: the managed frame inherits the position
    /// SourceSharpManaged held as a ROW of that table, and a step appearing
    /// between them would be a call AROUND the passes, which is the shape this
    /// file exists to refuse.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingRunsBetweenTheTwoSystemPasses()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        var steps = FrameStepsOf(engine).ToList();

        int pre = steps.IndexOf("Frame.ServerSystemsPreThink");
        int post = steps.IndexOf("Frame.ServerSystemsPostThink");

        Assert.True(pre >= 0, "the pre-entity pass ran");
        Assert.Equal(pre + 2, post);
        Assert.Equal("Frame.GamestatsUpdateConnection", steps[pre + 1]);
    }

    //=========================================================================
    // THE ONE ENTITY I/O POINT -- the sub-ioqueue lane.
    //=========================================================================

    /// <summary>
    /// <b>The managed I/O pass runs at stock's position and NOT one step
    /// earlier.</b>
    ///
    /// <para>
    /// status/p4-base.md recorded two entity I/O queues serviced at two points
    /// in one frame on two independent clocks: the managed one inside
    /// <c>ServerSystemsPostThink</c>, as a row of the managed frame, and the
    /// native one here. That is measurable and it is not stock — stock has one
    /// list and re-reads its head, so a zero-delay chain drains in one pass
    /// however it started. This is the fact that pins the pass where stock
    /// runs it: after every post-think system, immediately before
    /// <c>ServiceEventQueue</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void TheManagedIoPassRunsAfterThePostThinkSystems()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        string[] beforeThePass = Array.Empty<string>();
        int passes = 0;
        var kept = _io.ServiceQueue;

        try
        {
            _io.BindServiceQueue(_ =>
            {
                passes++;
                beforeThePass = FrameStepsOf(engine);
                return 0;
            });

            dll.GameFrame(true);
        }
        finally
        {
            _io.BindServiceQueue(kept);
        }

        Assert.Equal(1, passes);

        // The post-think pass is the step IMMEDIATELY before the managed I/O
        // pass. It used to be the step before last, with the native
        // ServiceEventQueue between them; that slot is retired (lane
        // 3b-enginebind) because cbase.cpp defined the queue and both producers
        // -- ent_fire in baseentity.cpp, the output-firing path in
        // entityoutput.cpp -- left the vpc with it. There is one queue now, and
        // it is serviced at the position the native one held.
        Assert.Equal("Frame.ServerSystemsPostThink", beforeThePass[^1]);
    }

    /// <summary>
    /// And the managed pass comes immediately after the native queue, with
    /// nothing between — so the frame has ONE entity I/O point rather than two.
    ///
    /// <para>
    /// <b>The native one first, and that order is the fix.</b> Events cross the
    /// boundary in one direction only: <c>ent_fire</c> posts into
    /// <c>g_EventQueue</c>, its delivery reaches a managed entity, and that
    /// entity's own outputs post into the MANAGED queue. Nothing managed ever
    /// posts back. Serviced managed-first, that hop lands after the managed
    /// pass has finished and waits a whole frame, which stock cannot do.
    /// </para>
    /// </summary>
    [Fact]
    public void TheManagedPassIsServicedImmediatelyAfterTheNativeQueue()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        int stepsAtThePass = -1;
        var kept = _io.ServiceQueue;

        try
        {
            _io.BindServiceQueue(_ =>
            {
                stepsAtThePass = FrameStepsOf(engine).Length;
                return 0;
            });

            dll.GameFrame(true);
        }
        finally
        {
            _io.BindServiceQueue(kept);
        }

        string[] whole = FrameStepsOf(engine);

        Assert.NotEqual(-1, stepsAtThePass);

        // The post-think pass is the step BEFORE the managed pass, and the step
        // after it is the delete-list reap -- so nothing runs between the
        // systems and the one I/O point, and nothing between it and the reap.
        //
        // The native queue used to sit between the pass and the managed one.
        // Its slot is retired (lane 3b-enginebind): no armed server TU can post
        // a native event, so the "native first" ordering argument above now has
        // one queue to order, at the position it argued for.
        Assert.Equal("Frame.ServerSystemsPostThink", whole[stepsAtThePass - 1]);
        Assert.Equal("Entities.CleanupDeleteList", whole[stepsAtThePass]);
    }

    /// <summary>
    /// The frame is serviced ONCE. A second pass would be a second clock again,
    /// which is the whole defect.
    /// </summary>
    [Fact]
    public void TheIoQueueIsServicedExactlyOncePerFrame()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        int passes = 0;
        var kept = _io.ServiceQueue;

        try
        {
            _io.BindServiceQueue(_ => { passes++; return 0; });

            dll.GameFrame(true);
            dll.GameFrame(true);
            dll.GameFrame(true);
        }
        finally
        {
            _io.BindServiceQueue(kept);
        }

        Assert.Equal(3, passes);
    }

    /// <summary>
    /// The rules' end of frame is AFTER the event queue and after the client
    /// data — which is what makes it different from the post-entity pass rather
    /// than a second copy of it: an input delivered this frame has been acted on
    /// by the time it runs. The third managed frame point is inside it.
    /// </summary>
    [Fact]
    public void TheRulesEndOfFrameComesAfterTheEventQueueAndTheClientData()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        var steps = FrameStepsOf(engine).ToList();

        int end = steps.IndexOf("Frame.EndGameFrame");
        int postThink = steps.IndexOf("Frame.ServerSystemsPostThink");
        int clientData = steps.IndexOf("Frame.UpdateAllClientData");

        // EVERY INDEX IS REQUIRED TO BE FOUND. IndexOf answers -1 for a step
        // that is not there, and -1 is less than everything -- so an ordering
        // assertion written as two bare comparisons passes VACUOUSLY the moment
        // a landmark is renamed or retired. This one did: ServiceEventQueue was
        // the first landmark, its slot was retired by lane 3b-enginebind, and
        // the assertion stayed green while measuring nothing.
        Assert.True(postThink >= 0, "the post-entity pass ran");
        Assert.True(clientData >= 0, "the client data step ran");
        Assert.True(end >= 0, "the rules' end of frame ran");

        Assert.True(postThink < end && clientData < end,
            "EndGameFrame runs after the post-entity pass and the client data");
    }

    /// <summary>
    /// Stock reaps the delete list TWICE a frame and the two are not
    /// interchangeable: the first clears what a console command removed between
    /// frames, the second what a think function removed during this one.
    /// </summary>
    [Fact]
    public void TheDeleteListIsReapedTwiceAFrameAndTheThinksAreBetweenThem()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        var steps = FrameStepsOf(engine).ToList();
        int first = steps.IndexOf("Entities.CleanupDeleteList");
        int last = steps.LastIndexOf("Entities.CleanupDeleteList");

        // The thinks are inside ServerSystemsPostThink now -- see
        // NothingRunsBetweenTheTwoSystemPasses for why that landmark replaced
        // the retired PhysicsRunThinkFunctions slot -- so the pass is what the
        // two reaps must bracket.
        int thinks = steps.IndexOf("Frame.ServerSystemsPostThink");

        Assert.True(thinks >= 0, "the post-entity pass, which the thinks run inside, ran");
        Assert.True(first >= 0 && last > first && first < thinks && thinks < last,
            "two reaps, with the entity thinks between them");
    }

    /// <summary>
    /// <b>The alternate-tick arm, on an ODD tick.</b> Stock does one thing and
    /// returns: <c>UpdateAllClientData()</c>. Nothing else in the frame runs —
    /// no think pass, no event queue, no drain — and this is the case a
    /// "cleverer" transliteration gets wrong, because an alternate-tick server
    /// is a configuration almost nobody runs locally.
    /// </summary>
    [Fact]
    public void OnASkippedAlternateTickOnlyTheClientDataRuns()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", true);
        engine.Returns("Frame.TickCount", 7);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        Assert.Equal(new[]
        {
            "Frame.IsSimulatingOnAlternateTicks",
            "Frame.TickCount",
            "Frame.UpdateAllClientData",
        }, FrameStepsOf(engine));
    }

    /// <summary>
    /// On an EVEN alternate tick the frame runs, and the frame time is doubled
    /// first — stock's "If we're skipping frames, then the frametime is 2x the
    /// normal tick". The multiply is in C# so it can be read beside the
    /// condition that asks for it.
    /// </summary>
    [Fact]
    public void OnASimulatedAlternateTickTheFrameTimeIsDoubledFirst()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", true);
        engine.Returns("Frame.TickCount", 8);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        var doubled = engine.Calls.First(c => c.Member == "Frame.SetFrameTime");

        Assert.True(Math.Abs(doubled.ArgAs<float>(0) - 0.030f) < 1e-6f,
            "the first SetFrameTime of an even alternate tick is 2x the tick's frame time");
    }

    /// <summary>
    /// And it is PUT BACK. Stock saves <c>oldframetime</c> before the frame and
    /// restores it after, so the doubling is not visible to the next frame — a
    /// restore that was dropped would compound.
    /// </summary>
    [Fact]
    public void TheFrameTimeIsRestoredAtTheEndOfAnAlternateTick()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", true);
        engine.Returns("Frame.TickCount", 8);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        var sets = engine.Calls.Where(c => c.Member == "Frame.SetFrameTime").ToList();

        Assert.True(sets.Count == 2
                 && Math.Abs(sets[^1].ArgAs<float>(0) - 0.015f) < 1e-6f
                 && FrameStepsOf(engine)[^1] == "Frame.SetFrameTime",
            "the last step of the frame puts the saved frame time back");
    }

    /// <summary>
    /// Stock's first line is <c>if ( g_InRestore ) return;</c>. Nothing in a
    /// multiplayer build sets that flag, so it is dead code that reads as live
    /// (Part 11c #9) — and this fact is what stops it being "helpfully" added
    /// back, which would silently stop every frame while a save restores in a
    /// build that never restores one.
    /// </summary>
    [Fact]
    public void TheFrameHasNoRestoreGateAndAsksTheEngineForNoSuchThing()
    {
        var (dll, engine) = Build();
        engine.Returns("Frame.IsSimulatingOnAlternateTicks", false);
        engine.Returns("Frame.GetFrameTime", 0.015f);

        dll.GameFrame(true);

        Assert.True(FrameStepsOf(engine).Length > 3
                 && !engine.Calls.Any(c => c.Member.Contains("Restor", StringComparison.OrdinalIgnoreCase)),
            "the frame runs its whole order and never asks whether a save is restoring");
    }

    // ---- ServerActivate ----------------------------------------------------

    /// <summary>Every step of the activate, in the order it is recorded.</summary>
    private static string[] ActivateStepsOf(RecordingEngine engine)
        => engine.Calls.Select(c => c.Member)
                 .Where(n => n.StartsWith("Level.", StringComparison.Ordinal))
                 .ToArray();

    /// <summary>
    /// <c>CServerGameDLL::ServerActivate</c> (gameinterface.cpp:1220-1275), as a
    /// sequence. This is the pass that closes Phase 6's 590-versus-473 gap.
    /// </summary>
    [Fact]
    public void ServerActivateRunsStocksStepsInStocksOrder()
    {
        var (dll, engine) = Build();
        engine.Returns("Console.FindConVarValue", "0");

        dll.ServerActivate();

        // TWO OF STOCK'S FOUR STEPS ARE NOT HERE, and each is a retired
        // IShimLevel slot rather than a step this port forgot — plan_shim.md
        // §5 3a, lane 3b-enginebind, rows in docs/abi-retirements.md:
        //
        //   ResetDeleteList was first. It asked how many SDK entities were
        //   queued for deletion at level start, and armed there is no SDK
        //   delete list at all, so the native body was a literal `return 0;`.
        //
        //   ActivateAllEntities was second. Every line of stock's walk
        //   (gameinterface.cpp:1233-1250) reaches the hierarchy
        //   server_csharp.vpc evicts, so the body had been empty since that
        //   eviction. The parallel hierarchy's half — _spawner.ActivateAll() —
        //   is managed, runs where the native call was, and is the whole of
        //   ServerActivate's activation pass.
        Assert.Equal(new[]
        {
            "Level.InitPostEntitySystems",
            "Level.SetPrecacheAllowed",
        }, ActivateStepsOf(engine));
    }

    /// <summary>
    /// "No more precaching after PostEntityAllSystems!!!" is stock's own shout,
    /// and it is the only ordering in ServerActivate that is load-bearing rather
    /// than tidy: the close has to FOLLOW the pass, or a participant's own
    /// precache is refused.
    /// </summary>
    [Fact]
    public void ThePrecacheWindowClosesAfterThePostEntityPassAndNotBefore()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.ResetDeleteList", 0);
        engine.Returns("Console.FindConVarValue", "0");

        dll.ServerActivate();

        var steps = ActivateStepsOf(engine).ToList();

        Assert.True(steps.IndexOf("Level.InitPostEntitySystems")
                  < steps.IndexOf("Level.SetPrecacheAllowed"),
            "the precache window closes after the post-entity pass");
    }

    /// <summary>
    /// The activate walk comes BEFORE the post-entity pass, because that pass is
    /// where the managed level init runs and it is entitled to find every map
    /// entity already activated.
    /// </summary>
    [Fact]
    public void TheActivateWalkComesBeforeThePostEntityPass()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.ResetDeleteList", 0);
        engine.Returns("Console.FindConVarValue", "0");

        dll.ServerActivate();

        var steps = ActivateStepsOf(engine).ToList();

        Assert.True(steps.IndexOf("Level.ActivateAllEntities")
                  < steps.IndexOf("Level.InitPostEntitySystems"),
            "entities activate before the post-entity systems pass");
    }

    // ANonEmptyDeleteQueueOnLevelStartIsReported IS RETIRED WITH ITS SLOT, and
    // it is worth saying what it actually asserted, because the name promised
    // more than the body delivered. It set Level.ResetDeleteList to answer 3
    // and then checked only that the slot was CALLED ONCE — never that the
    // warning fired, never what it said. The report in its name was not tested.
    //
    // THE SLOT IS RETIRED (plan_shim.md §5 3a, lane 3b-enginebind): armed there
    // is no SDK delete list, so the native body was a literal `return 0;` and
    // the warning could not fire on any build this port has ever produced. The
    // `Returns(..., 3)` in this fact was feeding the managed side a number
    // native cannot answer.
    //
    // THE QUESTION IS STILL WORTH ASKING, of the hierarchy that HAS a queue:
    // EntityDirectory.PendingRemovalCount. ServerGameDll cannot reach it — it
    // holds IEngineEntities, not the directory — so restoring the warning means
    // a constructor change, which is a larger edit than this lane is making and
    // is called out in its report rather than done quietly.


    /// <summary>
    /// Stock's last act in ServerActivate: <c>think_limit</c> is clamped to zero
    /// unless <c>developer</c> is set. A CONDITION, so it is managed and there is
    /// no native step for it at all.
    /// </summary>
    [Fact]
    public void WithDeveloperOffTheThinkLimitIsClamped()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.ResetDeleteList", 0);
        engine.Returns("Console.FindConVarValue", "0");

        dll.ServerActivate();

        Assert.True(engine.Calls.Any(c => c.Member == "Console.SetConVarValueByName"
                                       && c.ArgAs<string>(0) == "think_limit"
                                       && c.ArgAs<string>(1) == "0"),
            "developer 0 clamps think_limit to 0");
    }

    /// <summary>And with <c>developer</c> set it is left alone.</summary>
    [Fact]
    public void WithDeveloperOnTheThinkLimitIsLeftAlone()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.ResetDeleteList", 0);
        engine.Returns("Console.FindConVarValue", "2");

        dll.ServerActivate();

        Assert.True(!engine.Calls.Any(c => c.Member == "Console.SetConVarValueByName"
                                        && c.ArgAs<string>(0) == "think_limit"),
            "developer 2 leaves think_limit where it is");
    }

    /// <summary>
    /// ServerActivate turns off the engine's partial-change local transfer
    /// (<c>dt_UsePartialChangeEnts 0</c>). In this 64-bit engine.so that path
    /// cannot work: <c>CDatatableStack::Init(true)</c> fills the proxy slots
    /// with <c>memset(0xFF)</c>, a 64-bit -1, and the lookup only rebuilds a
    /// slot equal to <c>0xFFFFFFFF</c>. Every slot, the root included, then
    /// reads as a valid base of -1, and the first send proxy on a
    /// partially-changed entity dereferences <c>-1 + offset</c>. A single-player
    /// listen server (<c>-maxplayers 1</c>, <c>cl_LocalNetworkBackdoor 1</c>)
    /// crashed in <c>SendProxy_VectorToVector</c> that way.
    /// </summary>
    [Fact]
    public void ServerActivateTurnsOffPartialChangeLocalTransfer()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.ResetDeleteList", 0);
        engine.Returns("Console.FindConVarValue", "0");

        dll.ServerActivate();

        Assert.True(engine.Calls.Any(c => c.Member == "Console.SetConVarValueByName"
                                       && c.ArgAs<string>(0) == "dt_UsePartialChangeEnts"
                                       && c.ArgAs<string>(1) == "0"),
            "ServerActivate sets dt_UsePartialChangeEnts 0");
    }

    /// <summary>
    /// The partial-change switch is not tied to <c>developer</c>: a developer
    /// server runs the same broken engine path.
    /// </summary>
    [Fact]
    public void WithDeveloperOnPartialChangeLocalTransferIsStillOff()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.ResetDeleteList", 0);
        engine.Returns("Console.FindConVarValue", "2");

        dll.ServerActivate();

        Assert.True(engine.Calls.Any(c => c.Member == "Console.SetConVarValueByName"
                                       && c.ArgAs<string>(0) == "dt_UsePartialChangeEnts"
                                       && c.ArgAs<string>(1) == "0"),
            "developer 2 still sets dt_UsePartialChangeEnts 0");
    }

    /// <summary>
    /// A <c>developer</c> the engine does not have reads as zero, which is what
    /// <c>GetInt()</c> on a missing convar would have answered anyway — and NOT
    /// as "developer is on", which would leave the think limit reporting on
    /// every server that never declared the convar.
    /// </summary>
    [Fact]
    public void AMissingDeveloperConVarReadsAsOff()
    {
        var (dll, engine) = Build();
        engine.Returns("Level.ResetDeleteList", 0);
        engine.Returns("Console.FindConVarValue", null);

        dll.ServerActivate();

        Assert.True(engine.Calls.Any(c => c.Member == "Console.SetConVarValueByName"),
            "a missing developer convar clamps the think limit, like developer 0");
    }

    // ---- LevelShutdown -----------------------------------------------------

    /// <summary>
    /// <b>The order IS the method.</b> <c>CServerGameDLL::LevelShutdown</c>
    /// (gameinterface.cpp:1511-1559) is nine calls whose sequence is its entire
    /// content, which is why every one of them is a step and none of them is a
    /// decision.
    /// </summary>
    [Fact]
    public void LevelShutdownRunsStocksStepsInStocksOrder()
    {
        var (dll, engine) = Build();

        dll.MapUnload();

        Assert.Equal(new[]
        {
            "Level.ShutdownPreClearSteamSystems",
            // Level.ClearSteamAPIContext was here: the Steam context is C#'s
            // own (plan_server_novalve.md P2.5), cleared by Host.SteamGameServerClear
            // at this point -- TheSteamContextIsClearedAfterThePassNamedForIt.
            "Level.ShutdownPreEntitySystems",
            "Level.ShutdownSoundEnt",
            "Level.ClearEntityList",
            "Level.ShutdownPostEntitySystems",
            "Level.SetPrecacheAllowed",
            // UncacheAllParticleSystems and RecreateParticleDictionary were the
            // last two: CParticleSystemMgr's, gone with it (P2.1). The managed
            // manifest forgets its definitions at the same point.
        }, ActivateStepsOf(engine));
    }

    /// <summary>
    /// The steam context is cleared AFTER the pass named for it. That name is
    /// the whole reason the pass exists separately: a system that reads the
    /// context has to be told before it goes.
    /// </summary>
    [Fact]
    public void TheSteamContextIsClearedAfterThePassNamedForIt()
    {
        var (dll, engine) = Build();

        dll.MapUnload();

        // The clear is C#'s own now (plan_server_novalve.md P2.5), a Host call
        // rather than a Level step, and it still sits where stock's did: after
        // the pass named for it and before the pre-entity pass.
        var calls = engine.Calls.Select(c => c.Member).ToList();
        int preClear = calls.IndexOf("Level.ShutdownPreClearSteamSystems");
        int clear = calls.IndexOf("Host.SteamGameServerClear");
        int preEntity = calls.IndexOf("Level.ShutdownPreEntitySystems");

        Assert.True(preClear >= 0 && clear > preClear && preEntity > clear,
            "the pre-clear pass runs before the clear, and the clear before the pre-entity pass");
    }

    /// <summary>
    /// The entity list is cleared BETWEEN the pre- and post-entity passes, which
    /// is what those two names mean. A clear before the pre-entity pass would
    /// hand every participant a list that had already gone.
    /// </summary>
    [Fact]
    public void TheEntityListIsClearedBetweenTheTwoEntityPasses()
    {
        var (dll, engine) = Build();

        dll.MapUnload();

        var steps = ActivateStepsOf(engine).ToList();

        Assert.True(steps.IndexOf("Level.ShutdownPreEntitySystems")
                  < steps.IndexOf("Level.ClearEntityList")
                 && steps.IndexOf("Level.ClearEntityList")
                  < steps.IndexOf("Level.ShutdownPostEntitySystems"),
            "pre-entity pass, clear, post-entity pass");
    }

    /// <summary>
    /// <c>CSoundEnt::ShutdownSoundEnt</c> comes before the list is cleared, and
    /// stock's comment is the reason: "This entity pointer is going away now and
    /// is corrupting memory on level transitions/restarts".
    /// </summary>
    [Fact]
    public void TheSoundEntityIsDroppedBeforeTheListIsCleared()
    {
        var (dll, engine) = Build();

        dll.MapUnload();

        var steps = ActivateStepsOf(engine).ToList();

        Assert.True(steps.IndexOf("Level.ShutdownSoundEnt")
                  < steps.IndexOf("Level.ClearEntityList"),
            "the sound entity goes before the list it lives in");
    }

    /// <summary>
    /// The native particle manager's two teardown steps are no longer asked for:
    /// the manager left server.so in P2.1 (plan_server_novalve.md).
    /// </summary>
    [Fact]
    public void TheParticleManagerIsNoLongerToldToUncache()
    {
        var (dll, engine) = Build();

        dll.MapUnload();

        var steps = ActivateStepsOf(engine).ToList();

        Assert.DoesNotContain("Level.UncacheAllParticleSystems", steps);
        Assert.DoesNotContain("Level.RecreateParticleDictionary", steps);
    }

    /// <summary>
    /// The map name is dropped BEFORE the passes, not after: a participant
    /// reached from one of those passes that asks which map is loaded should be
    /// told "none", not the one going away.
    /// </summary>
    [Fact]
    public void TheMapIsForgottenBeforeTheShutdownPassesRun()
    {
        var (dll, engine) = Build();

        dll.MapLoadNewGame(new MapLoad("de_dust", Lump));
        engine.Clear();
        dll.MapUnload();

        Assert.True(dll.CurrentMap.Length == 0 && dll.EntityLumpBytes == 0
                 && ActivateStepsOf(engine).Length == 6,
            "the map is forgotten and all six steps still ran (nine until the "
            + "particle manager's two left, plan_server_novalve.md P2.1, and "
            + "seven until the Steam context clear became C#'s own, P2.5)");
    }

    // ---- PreClientUpdate ---------------------------------------------------

    /// <summary>
    /// <c>CServerGameDLL::PreClientUpdate</c>. The debug overlays are drawn
    /// FIRST — stock's order — and are not behind a debug guard, because stock's
    /// own comment beside the call is "allow this in release for now". This is
    /// what makes an NDebugOverlay call from managed code visible at all.
    /// </summary>
    [Fact]
    public void PreClientUpdateDrawsTheOverlaysThenRunsTheSystemsPass()
    {
        var (dll, engine) = Build();

        dll.PreClientUpdate();

        Assert.Equal(new[]
        {
            "Frame.DrawAllDebugOverlays",
            "Frame.ServerSystemsPreClientUpdate",
        }, FrameStepsOf(engine));
    }

    // ---- plan_gamedll_after.md Phase A2: Part 11a2's three gaps ------------
    //
    // Three IServerGameDLL methods that were EMPTY BODIES with no TODO. They
    // are implemented rather than refused because all three are reached by
    // design, and the facts below are about the two things a reader cannot see
    // by looking at them: that the mdl-cache walk is ASKED OF NATIVE rather
    // than answered managed-side, and that the Steam pair's order INVERTS.

    /// <summary>
    /// The engine's model-cache flush reaches the native gEntList walk.
    ///
    /// <para>
    /// THIS IS THE WHOLE PORT OF THE METHOD. plan_gamedll.md Part 11a2 proposes
    /// a managed walk over an <c>IMdlCacheEvents</c> interface instead; the
    /// state being freed is <c>CBaseAnimating::m_pStudioHdr</c>, a native
    /// member, so a managed-only version would leave the loss exactly where it
    /// was. A body that counted the flush and asked the engine nothing would
    /// pass every other gate in the tree.
    /// </para>
    /// </summary>
    [Fact]
    public void AModelCacheFlushReachesTheNativeEntityWalk()
    {
        var (dll, engine) = Build();

        dll.InvalidateMdlCache();

        Assert.True(engine.CountOf("Entities.InvalidateMdlCacheAll") == 1,
            "a flush asks the engine to walk the entity list exactly once");
    }

    /// <summary>
    /// AND HOW MANY IT REACHED IS KEPT -- lane diag-visibility. A counter of
    /// FLUSHES cannot tell a working walk from one that reaches nothing, which
    /// is the state the CLIENT half of this was found in.
    /// </summary>
    [Fact]
    public void AModelCacheFlushKeepsHowManyEntitiesTheWalkReached()
    {
        var (dll, engine) = Build();
        engine.Returns("Entities.InvalidateMdlCacheAll", 391);

        dll.InvalidateMdlCache();

        Assert.True(dll.MdlCacheEntitiesInvalidated == 391,
            "the population the walk reached is what separates an empty body "
            + "from a working one, and it is the number the console line carries");
    }

    /// <summary>
    /// The server's skeleton is managed (plan physics_bones_native_zero 1.2),
    /// so the flush reaches it here rather than in the native walk: every
    /// skeleton is invalidated and every copied model image dropped.
    /// </summary>
    [Fact]
    public void AModelCacheFlushInvalidatesTheManagedSkeletons()
    {
        var (dll, _) = Build();
        var skeletons = new SourceSharp.Server.ServerSkeletons(new NoBlocks(), new SourceSharp.Shared.Bones.StudioModels(new NoMemory()),
                                                               new NoClock(), new RecordingLog());
        skeletons.States.GetOrCreate(new EntityHandle(0x00010003u)).CacheKey.ModelIndex = 5;
        dll.Skeletons = skeletons;

        dll.InvalidateMdlCache();

        Assert.True(dll.MdlCacheSkeletonsInvalidated == 1, "the one skeleton was invalidated");
        Assert.True(skeletons.States.TryGet(new EntityHandle(0x00010003u), out var state)
                    && state!.CacheKey.ModelIndex != 5, "its cache key went never-valid");
    }

    private sealed class NoBlocks : SourceSharp.Abi.IStandaloneBlocks
    {
        public Span<byte> Block(EntityHandle entity) => Span<byte>.Empty;
    }

    private sealed class NoMemory : IStudioMemory
    {
        public byte[]? ModelImage(int modelIndex) => null;
        public byte[]? ModelAnimBlock(int modelIndex, int block) => null;
        public byte[]? IncludeImage(int modelIndex, string includeName) => null;
        public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block) => null;
    }

    private sealed class NoClock : IGameClock
    {
        public float Now => 0f;
    }

    [Fact]
    public void AModelCacheFlushIsCounted()
    {
        var (dll, engine) = Build();

        dll.InvalidateMdlCache();
        dll.InvalidateMdlCache();

        Assert.True(dll.MdlCacheInvalidations == 2,
            "the count is the only evidence a live instance can offer that the "
            + "engine reaches this method at all");
        Assert.True(engine.CountOf("Entities.InvalidateMdlCacheAll") == 2,
            "and every one of them reaches the walk");
    }

    /// <summary>
    /// Steam activation re-acquires the context before anything managed reads
    /// it. <c>Clear()</c> then <c>Init()</c> is a re-acquisition, and stale
    /// pointers are what a handler would otherwise be handed.
    /// </summary>
    [Fact]
    public void SteamActivationReacquiresTheContext()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", false);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", false);

        dll.SteamGameServerActivated();

        Assert.True(engine.CountOf("Host.SteamGameServerReacquire") == 1,
            "activation re-acquires the Steam game-server context");
    }

    [Fact]
    public void SteamActivationCarriesBackWhetherTheGameServerAnswered()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", true);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", false);

        Assert.False(dll.SteamGameServerAvailable,
            "nothing is available before the first activation");

        dll.SteamGameServerActivated();

        Assert.True(dll.SteamGameServerAvailable,
            "the re-acquire's answer is what SteamGameServerAvailable reports");
    }

    /// <summary>
    /// Stock's one decision, and both halves of it are managed now:
    /// gameinterface.cpp:1319-1322 asks Steam for gameplay stats only when the
    /// game server came back AND this is a dedicated server.
    /// </summary>
    [Fact]
    public void ADedicatedServerWithSteamUpAsksForGameplayStats()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", true);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", true);

        dll.SteamGameServerActivated();

        Assert.True(engine.CountOf("Host.SteamGameServerRequestGameplayStats") == 1,
            "a dedicated server with Steam up asks for its gameplay stats");
    }

    [Fact]
    public void AListenServerDoesNotAskForGameplayStatsEvenWithSteamUp()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", true);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", false);

        dll.SteamGameServerActivated();

        Assert.True(engine.CountOf("Host.SteamGameServerRequestGameplayStats") == 0,
            "stock guards the stats request on IsDedicatedServer and so does this");
    }

    [Fact]
    public void ADedicatedServerWithNoSteamDoesNotAskForGameplayStats()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", false);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", true);

        dll.SteamGameServerActivated();

        Assert.True(engine.CountOf("Host.SteamGameServerRequestGameplayStats") == 0,
            "with no game server to ask there is nothing to ask -- stock's "
            + "condition is both halves and not either");
    }

    /// <summary>
    /// THE ORDER INVERTS BETWEEN THE PAIR, and this is the fact that says so.
    /// On the way up native acquires first; on the way down managed runs first
    /// and the clear is the last thing that happens, because a handler that ran
    /// after the clear would have nothing to read.
    /// </summary>
    [Fact]
    public void SteamShutdownClearsTheContextAndAsksForNothingElse()
    {
        var (dll, engine) = Build();

        dll.SteamGameServerShutdown();

        string[] host = engine.Calls.Select(c => c.Member)
                              .Where(n => n.StartsWith("Host.Steam", StringComparison.Ordinal))
                              .ToArray();

        Assert.Equal(new[] { "Host.SteamGameServerClear" }, host);
    }

    [Fact]
    public void SteamShutdownStopsReportingTheGameServerAsAvailable()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", true);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", false);

        dll.SteamGameServerActivated();
        dll.SteamGameServerShutdown();

        Assert.False(dll.SteamGameServerAvailable,
            "the context has been cleared, so there is nothing left to answer for");
    }

    /// <summary>
    /// Activation REPEATS — the engine calls it again whenever Steam becomes
    /// available, including after a reconnect — and re-acquires every time. A
    /// body that initialised once would hold the previous session's pointers.
    /// </summary>
    [Fact]
    public void ASecondActivationReacquiresAgainRatherThanShortCircuiting()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", true);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", false);

        dll.SteamGameServerActivated();
        dll.SteamGameServerActivated();

        Assert.True(engine.CountOf("Host.SteamGameServerReacquire") == 2,
            "every activation is a re-acquisition; the pointers from the last "
            + "one are stale");
        Assert.True(dll.SteamActivations == 2, "and both are counted");
    }

    /// <summary>
    /// None of the three touches the level. They are the process's Steam
    /// session and the DLL's model caches; a body that reached
    /// <see cref="IEngineLevel"/> would be saying they are a map's business,
    /// which is the wrong lifetime for all three.
    /// </summary>
    [Fact]
    public void NoneOfThePartElevenA2ThreeTouchesTheLevel()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", false);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", false);

        dll.InvalidateMdlCache();
        dll.SteamGameServerActivated();
        dll.SteamGameServerShutdown();

        Assert.Empty(ActivateStepsOf(engine));
    }

    /// <summary>
    /// All three say so on the console, once each.
    ///
    /// <para>
    /// <b>This is the only live evidence any of them is reached.</b> Phase A2's
    /// whole difficulty was that an empty body and a working one are
    /// indistinguishable from outside — Part 7's rule, on three methods nobody
    /// could refuse, so the counter it prescribes is the wrong instrument. What
    /// is left is that each one says it ran. All three are rare (an operator
    /// flushing, Steam coming up, Steam going away), so a line each is a
    /// measurement rather than noise — and a "tidy the logging" pass that
    /// deletes them takes the evidence with it, which is what this refuses.
    /// </para>
    /// </summary>
    [Fact]
    public void EachOfThePartElevenA2ThreeSaysOnceThatItRan()
    {
        var (dll, engine) = Build();
        engine.Returns("Host.SteamGameServerReacquire", false);
        engine.Returns("Host.SteamGameServerLoggedOn", false);
        engine.Returns("Host.IsDedicatedServer", false);

        dll.InvalidateMdlCache();
        dll.SteamGameServerActivated();
        dll.SteamGameServerShutdown();

        Assert.True(engine.CountOf("Console.Msg") == 3,
            "each of the three says exactly once that it ran -- three calls for "
            + "three events, not two and not six");
    }
}
