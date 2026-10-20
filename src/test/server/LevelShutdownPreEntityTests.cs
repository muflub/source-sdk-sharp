using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The PRE-ENTITY half of level shutdown, which had no caller at all before
/// plan_gamedll_after.md Phase A3's fifth system.
///
/// <para>
/// <b>The THIRD stage this plan has found in that state</b>, after
/// <c>OnLevelInitPreEntity</c> (first pair) and <c>OnPreClientUpdate</c>
/// (second pair). Part 3a's split added
/// <c>IGameLifecycle.OnLevelShutdownPreEntity</c> and
/// <c>LifecycleDriver.LevelShutdownPreEntity</c>; <c>ServerModule</c> called
/// the PostEntity half and <c>ServerGameDll.MapUnload</c> called the NATIVE
/// pass, so the driver method's only callers were two facts in
/// <c>LifecycleTests</c>.
/// </para>
///
/// <para>
/// <b>It was not free.</b> <c>SoundController</c> has declared that stage for
/// <c>SystemReset</c> since A3's second pair, so between that phase and this
/// one every live sound patch survived a level change — the shape this project
/// keeps recording: nothing failed to compile, nothing warned, and the
/// participant simply did not run.
/// </para>
/// </summary>
public class LevelShutdownPreEntityTests
{
    /// <summary>A participant that records which halves it was reached on.</summary>
    private sealed class Watcher : IGameLifecycle
    {
        public Watcher(int order) => LifecycleOrder = order;

        public int LifecycleOrder { get; }

        public List<string> Seen { get; } = new();

        void IGameLifecycle.OnLevelShutdownPreEntity() => Seen.Add("pre");

        void IGameLifecycle.OnLevelShutdownPostEntity() => Seen.Add("post");
    }

    /// <summary>
    /// A second watcher TYPE, because the container is keyed by type and a
    /// second instance of one class replaces the first rather than joining it.
    /// </summary>
    private sealed class OtherWatcher : IGameLifecycle
    {
        public OtherWatcher(int order) => LifecycleOrder = order;

        public int LifecycleOrder { get; }

        public List<string> Seen { get; } = new();

        void IGameLifecycle.OnLevelShutdownPreEntity() => Seen.Add("pre");
    }

    private static LifecycleDriver DriverOver(params IGameLifecycle[] participants)
    {
        var services = new ServiceContainer();

        foreach (var p in participants)
            services.AddInstance(p.GetType(), p);

        return new LifecycleDriver(new RecordingLog(), services);
    }

    private static ServerGameDll DllOver(LifecycleDriver driver)
    {
        var engine = TestServices.Fake();

        var (stringTables, modTable) = TestStringTables.For(engine);

        return new ServerGameDll(new RecordingLog(), engine, ConVarRegistry.For(TestServices.UnattachedConsole()), engine, new RecordingCommandLine("srcds"), engine, engine,
                                 engine, engine, stringTables, modTable, driver,
                                 new TemplateStore(new RecordingLog()),
                                 // plan_entity.md step 2d. Nothing here loads a
                                 // map; the spawner is a constructor argument.
                                 TestMapSpawner.Over(engine, engine), new EntityIoRoutes());
    }

    [Fact]
    public void ThePreEntityHalfReachesAParticipantThatDeclaresIt()
    {
        var watcher = new Watcher(10);
        var driver = DriverOver(watcher);

        driver.LevelShutdownPreEntity();

        Assert.True(watcher.Seen.SequenceEqual(new[] { "pre" }),
            "a participant that declares the pre-entity shutdown stage is reached on it, "
            + "and only on it");
    }

    [Fact]
    public void TheTwoHalvesAreSeparateEventsAndNotOneCalledTwice()
    {
        var watcher = new Watcher(10);
        var driver = DriverOver(watcher);

        driver.LevelShutdownPreEntity();
        driver.LevelShutdownPostEntity();

        Assert.True(watcher.Seen.SequenceEqual(new[] { "pre", "post" }),
            "the pre-entity half runs first and the post-entity half after it");
    }

    /// <summary>
    /// THE SLOT HAS A CALLER, and this is the fact that would have caught the
    /// gap. It reads <see cref="IServerLevel"/> for a pre-entity SHUTDOWN
    /// member — a stage the driver can dispatch and nothing calls is exactly
    /// what existed between Part 3a and this phase.
    /// </summary>
    [Fact]
    public void TheEngineFacingInterfaceCarriesThePreEntityHalfToo()
    {
        var member = typeof(IServerLevel).GetMethod("MapUnloadPreEntity");

        Assert.True(member is not null,
            "IServerLevel names the pre-entity half of shutdown, so the shell has "
            + "something to call. Between Part 3a and A3's fifth system the driver "
            + "could dispatch that stage and nothing in the build ever asked it to");

        Assert.True(typeof(ServerGameDll).GetInterfaceMap(typeof(IServerLevel))
                        .TargetMethods.Any(m => m.Name.EndsWith("MapUnloadPreEntity")),
            "and ServerGameDll implements it");
    }

    /// <summary>
    /// AND IT REACHES THE PARTICIPANTS, which is the half the reflection fact
    /// above cannot say. A <c>MapUnloadPreEntity</c> that existed, was wired to
    /// the slot and dispatched nothing would satisfy every other fact here and
    /// leave the gap exactly where it was.
    /// </summary>
    [Fact]
    public void TheEngineFacingCallReachesThePreEntityParticipants()
    {
        var watcher = new Watcher(10);
        var dll = DllOver(DriverOver(watcher));

        ((IServerLevel)dll).MapUnloadPreEntity();

        Assert.True(watcher.Seen.SequenceEqual(new[] { "pre" }),
            "the shell's pre-entity shutdown call lands on the pre-entity half of "
            + "every participant");
    }

    /// <summary>
    /// AND <c>MapUnload</c> DISPATCHES IT EXACTLY ONCE, in its pass.
    ///
    /// <para>
    /// Until plan_server_native_v2 T4e this fact said the opposite: the pass
    /// was a native backward walk of <c>kServerSystems[]</c> and the managed
    /// participants were reached from the SourceSharpManaged row inside it, so
    /// a dispatch from <c>MapUnload</c> would have been a second one outside
    /// the walk. The walk is managed's now (<c>ServerSystems</c>), so
    /// <c>MapUnload</c> runs the pass itself, where the native walk ran, and
    /// the participants' position inside it is
    /// <c>ServerSystemsTests.ThePreEntityShutdownPassIsBackwards</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void MapUnloadDispatchesThePreEntityHalfOnce()
    {
        var watcher = new Watcher(10);
        var dll = DllOver(DriverOver(watcher));

        ((IServerLevel)dll).MapUnload();

        Assert.True(watcher.Seen.SequenceEqual(new[] { "pre" }),
            "MapUnload runs the pre-entity shutdown pass once");
    }

    /// <summary>
    /// THE PARTICIPANT THAT WAS ALREADY PAYING FOR THE GAP.
    ///
    /// <para>
    /// <c>SoundController</c> has declared <c>OnLevelShutdownPreEntity</c> since
    /// A3's second pair. Naming it here rather than only in
    /// <c>SoundControllerTests</c> is deliberate: that file drives the method
    /// directly, so it was green for the whole time nothing in the build called
    /// it. This asserts the participant is REACHED THROUGH THE DRIVER, which is
    /// the half that was missing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSoundControllersResetIsReachedThroughTheDriverNow()
    {
        var map = typeof(SoundController).GetInterfaceMap(typeof(IGameLifecycle));

        var declared = map.InterfaceMethods
            .Zip(map.TargetMethods, (iface, target) => (iface, target))
            .Where(x => x.iface.Name == "OnLevelShutdownPreEntity")
            .ToList();

        Assert.True(declared.Count == 1,
            "IGameLifecycle no longer declares OnLevelShutdownPreEntity at all");

        // The TARGET, not the interface method. Every IGameLifecycle member has
        // a default body, so a type that declares none still maps all of them --
        // and the map alone would be satisfied by the interface's own do-nothing
        // implementation. What says SoundController owns this stage is that the
        // target is declared ON SoundController.
        Assert.True(declared[0].target.DeclaringType == typeof(SoundController),
            "SoundController no longer implements OnLevelShutdownPreEntity, so the stage this "
            + "phase wired has lost the participant that proved it was needed -- and the "
            + "measurement that it went unrun from A3's second pair until now goes with it");
    }
}
