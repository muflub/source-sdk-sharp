using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The PRE-ENTITY half of level init, which had no caller at all before
/// plan_gamedll_after.md Phase A3.
///
/// <para>
/// <b>This is the gap, stated as facts.</b> Part 3a's stage split added
/// <c>IGameLifecycle.OnLevelInitPreEntity</c> and
/// <c>LifecycleDriver.LevelInitPreEntity</c>, and <c>ServerModule</c> only ever
/// called the PostEntity one — so a participant that declared the pre-entity
/// stage was silently never reached. It was silent in the exact way this
/// project keeps recording: nothing failed to compile, nothing warned, and the
/// participant simply did not run.
/// </para>
///
/// <para>
/// It matters for A3 because both of that phase's systems do their work in that
/// half: a decal index and a gib model have to be PRECACHED, and the precache
/// window is opened by the world entity's own precache and closed by the first
/// <c>Spawn()</c>.
/// </para>
/// </summary>
public class LevelInitPreEntityTests
{
    /// <summary>A participant that records which halves it was reached on.</summary>
    private sealed class Watcher : IGameLifecycle
    {
        public Watcher(int order) => LifecycleOrder = order;

        public int LifecycleOrder { get; }

        public List<string> Seen { get; } = new();

        void IGameLifecycle.OnLevelInitPreEntity(string mapName) => Seen.Add("pre:" + mapName);

        void IGameLifecycle.OnLevelInitPostEntity(string mapName) => Seen.Add("post:" + mapName);
    }

    /// <summary>
    /// A second watcher TYPE, because the container is keyed by type and a
    /// second instance of one class replaces the first rather than joining it.
    /// The ordering fact needs two participants that are genuinely two.
    /// </summary>
    private sealed class OtherWatcher : IGameLifecycle
    {
        public OtherWatcher(int order) => LifecycleOrder = order;

        public int LifecycleOrder { get; }

        public List<string> Seen { get; } = new();

        void IGameLifecycle.OnLevelInitPreEntity(string mapName) => Seen.Add("pre:" + mapName);
    }

    private static LifecycleDriver DriverOver(params IGameLifecycle[] participants)
    {
        var services = new ServiceContainer();

        foreach (var p in participants)
            services.AddInstance(p.GetType(), p);

        return new LifecycleDriver(new RecordingLog(), services);
    }

    [Fact]
    public void ThePreEntityHalfReachesAParticipantThatDeclaresIt()
    {
        var watcher = new Watcher(10);
        var driver = DriverOver(watcher);

        driver.LevelInitPreEntity("dm_lockdown");

        Assert.True(watcher.Seen.SequenceEqual(new[] { "pre:dm_lockdown" }),
            "a participant that declares the pre-entity stage is reached on it, and only on it");
    }

    [Fact]
    public void TheTwoHalvesAreSeparateEventsAndNotOneCalledTwice()
    {
        var watcher = new Watcher(10);
        var driver = DriverOver(watcher);

        driver.LevelInitPreEntity("dm_lockdown");
        driver.LevelInitPostEntity("dm_lockdown");

        Assert.True(watcher.Seen.SequenceEqual(new[] { "pre:dm_lockdown", "post:dm_lockdown" }),
            "the pre-entity half runs first and the post-entity half after it");
    }

    [Fact]
    public void TheParticipantsRunInStageOrder()
    {
        var first = new Watcher(100);
        var second = new OtherWatcher(200);
        var driver = DriverOver(second, first);

        driver.LevelInitPreEntity("dm_lockdown");

        // Registered second-then-first on purpose: the ORDER is the stage
        // value, not the registration, and A3's two participants depend on it
        // -- propdata parses before the decal script because CPropData was row
        // 9 of kServerSystems[] and CDecalEmitterSystem row 11.
        Assert.True(first.Seen.Count == 1 && second.Seen.Count == 1, "both ran");
        Assert.True(driver.Participants.First() == first,
            "the lower stage value runs first whatever order they were registered in");
    }

    /// <summary>
    /// THE SLOT HAS A CALLER, and this is the fact that would have caught the
    /// gap. It reads <c>IServerLevel</c> for a pre-entity member and
    /// <c>ServerGameDll</c> for one that dispatches the driver — a stage the
    /// driver can dispatch and nothing calls is exactly what existed between
    /// Part 3a and Phase A3.
    /// </summary>
    [Fact]
    public void TheEngineFacingInterfaceCarriesThePreEntityHalfToo()
    {
        var member = typeof(IServerLevel).GetMethod("MapLoadPreEntity");

        Assert.True(member is not null,
            "IServerLevel names the pre-entity half, so the shell has something to call. "
            + "Between Part 3a and Phase A3 the driver could dispatch that stage and "
            + "nothing in the build ever asked it to");

        Assert.True(typeof(ServerGameDll).GetInterfaceMap(typeof(IServerLevel))
                        .TargetMethods.Any(m => m.Name.EndsWith("MapLoadPreEntity")),
            "and ServerGameDll implements it");
    }

    /// <summary>
    /// AND IT REACHES THE PARTICIPANTS, which is the half the reflection fact
    /// above cannot say.
    ///
    /// <para>
    /// A <c>MapLoadPreEntity</c> that existed, was wired to the slot, and
    /// dispatched nothing would satisfy every other fact in this file and leave
    /// the port exactly where it was — with the decal table empty and every
    /// lookup answering −1, which is what a working one answers for a decal
    /// that is genuinely absent.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEngineFacingCallReachesThePreEntityParticipants()
    {
        var watcher = new Watcher(10);
        var engine = TestServices.Fake();

        // The world's precache around the pass (plan_server_native_v2 T4e)
        // reads g_Language and the globals; the strict double answers both.
        engine.Returns("Console.FindConVarValue", "0");
        engine.Returns("Environment.Globals", default(Globals));

        var (stringTables, modTable) = TestStringTables.For(engine);

        var dll = new ServerGameDll(new RecordingLog(), engine, new ConVarRegistry(TestServices.UnattachedConsole()), engine, new RecordingCommandLine("srcds"), engine, engine,
                                    engine, engine, stringTables, modTable,
                                    DriverOver(watcher), new TemplateStore(new RecordingLog()),
                                    // plan_entity.md step 2d: ServerGameDll owns
                                    // the map parse now. Over an empty registry,
                                    // so it owns no classname and this fact is
                                    // still about the pre-entity pass alone.
                                    TestMapSpawner.Over(engine, engine), new EntityIoRoutes());

        ((IServerLevel)dll).MapLoadPreEntity("dm_lockdown");

        Assert.True(watcher.Seen.SequenceEqual(new[] { "pre:dm_lockdown" }),
            "the shell's pre-entity call lands on the pre-entity half of every participant");
    }

    private sealed class PostInitWatcher : IGameLifecycle
    {
        public int Calls { get; private set; }
        public int LifecycleOrder => ModStages.First;
        void IGameLifecycle.OnPostInit() => Calls++;
    }

    [Fact]
    public void GamePostStartDispatchesPostInitOnceToEveryParticipant()
    {
        // plan_host_sdk P5: a mod's HostService decides hosted or unhosted in
        // OnPostInit. Until a participant existed nothing dispatched the stage,
        // and the first live run of the tf2_descent mod never started its SDK.
        var watcher = new PostInitWatcher();
        var engine = TestServices.Fake();
        var (stringTables, modTable) = TestStringTables.For(engine);

        var dll = new ServerGameDll(new RecordingLog(), engine, new ConVarRegistry(TestServices.UnattachedConsole()), engine, new RecordingCommandLine("srcds"), engine, engine,
                                    engine, engine, stringTables, modTable,
                                    DriverOver(watcher), new TemplateStore(new RecordingLog()),
                                    TestMapSpawner.Over(engine, engine), new EntityIoRoutes());

        dll.GamePostStart();

        Assert.Equal(1, watcher.Calls);
    }
}

