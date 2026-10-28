using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The frame lifecycle: three points, participants found by the container.
///
/// <para>
/// Everything here that also applies to <see cref="IGameLifecycle"/> is covered
/// once, in <see cref="LifecycleTests"/> — discovery, laziness, the identity
/// dedupe. Both drivers go through the same <c>ResolveAll</c>. What is tested
/// HERE is what is different about a per-frame sequence: that the three points
/// are three, that they are in the right places in the native frame, and that a
/// participant which throws does not write a line 66 times a second.
/// </para>
/// </summary>
public class FrameLifecycleTests
{
    private sealed class Recorder : IFrameLifecycle, IContract
    {
        private readonly List<string> _log;
        private readonly int _order;

        public Recorder(List<string> log, int order, string name)
        {
            _log = log;
            _order = order;
            Name = name;
        }

        public string Name { get; }
        int IFrameLifecycle.FrameOrder => _order;

        void IFrameLifecycle.OnPreThink(float dt) => _log.Add(Name + ":pre");
        void IFrameLifecycle.OnThink(float dt) => _log.Add(Name + ":think");
        void IFrameLifecycle.OnPostThink(float dt) => _log.Add(Name + ":post");
        void IFrameLifecycle.OnPreClientUpdate(float dt) => _log.Add(Name + ":preclient");
    }

    /// <summary>
    /// The PRE-PreClientUpdate participant shape: the three points that existed
    /// before the fourth was added, and nothing else. Every real frame
    /// participant is this shape.
    /// </summary>
    private sealed class ThreePointParticipant : IFrameLifecycle
    {
        public readonly List<string> Seen = new();
        int IFrameLifecycle.FrameOrder => 100;
        void IFrameLifecycle.OnPreThink(float dt) => Seen.Add("pre");
        void IFrameLifecycle.OnThink(float dt) => Seen.Add("think");
        void IFrameLifecycle.OnPostThink(float dt) => Seen.Add("post");
    }

    /// <summary>Throws at the last two points, to prove their fault counters are distinct.</summary>
    private sealed class LastTwoEnds : IFrameLifecycle
    {
        void IFrameLifecycle.OnPostThink(float dt) => throw new InvalidOperationException("post");
        void IFrameLifecycle.OnPreClientUpdate(float dt) => throw new InvalidOperationException("preclient");
    }

    /// <summary>Implements nothing, to prove the defaults are real.</summary>
    private sealed class Silent : IFrameLifecycle { }

    private sealed class Thrower : IFrameLifecycle
    {
        public int Calls;
        int IFrameLifecycle.FrameOrder => -10;
        void IFrameLifecycle.OnThink(float dt)
        {
            Calls++;
            throw new InvalidOperationException("boom");
        }
    }

    private interface IContract { }
    private sealed class Timekeeper : IContract, IFrameLifecycle
    {
        public float Last;
        void IFrameLifecycle.OnThink(float dt) => Last = dt;
    }

    // ------------------------------------------------------------------
    // Three points, and they are three.
    // ------------------------------------------------------------------

    [Fact]
    public void EachPointCallsOnlyItsOwnMethod()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(log, 0, "a"));
        var driver = new FrameDriver(new RecordingLog(), services);

        driver.PreThink(0.015f);
        Assert.Equal(new[] { "a:pre" }, log);

        log.Clear();
        driver.Think(0.015f);
        Assert.Equal(new[] { "a:think" }, log);

        log.Clear();
        driver.PostThink(0.015f);
        Assert.Equal(new[] { "a:post" }, log);
    }

    [Fact]
    public void ParticipantsRunInFrameOrder()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(log, 30, "late"));
        services.AddInstance<IContract>(new Recorder(log, 10, "early"));

        new FrameDriver(new RecordingLog(), services).Think(0.015f);

        Assert.Equal(new[] { "early:think", "late:think" }, log);
    }

    /// <summary>
    /// The think point split at a stage: the entities' frame (up to and
    /// including the stage) runs before the physics step, the rest after it.
    /// </summary>
    [Fact]
    public void A_think_split_at_a_stage_runs_each_half_alone()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(log, 30, "late"));
        services.AddInstance<IContract>(new Recorder(log, 10, "early"));
        var driver = new FrameDriver(new RecordingLog(), services);

        driver.ThinkThrough(10, 0.015f);
        Assert.True(driver.EntitiesThought);
        log.Add("|");
        driver.ThinkRest(0.015f);

        Assert.Equal(new[] { "early:think", "|", "late:think" }, log);
        Assert.False(driver.EntitiesThought);
    }

    /// <summary>A frame whose entities' half never ran thinks every participant in its rest, once.</summary>
    [Fact]
    public void A_rest_with_no_entities_half_before_it_thinks_everyone()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(log, 30, "late"));
        services.AddInstance<IContract>(new Recorder(log, 10, "early"));
        var driver = new FrameDriver(new RecordingLog(), services);

        driver.ThinkThrough(10, 0.015f);
        driver.ThinkRest(0.015f);
        log.Clear();
        driver.ThinkRest(0.015f);

        Assert.Equal(new[] { "early:think", "late:think" }, log);
    }

    /// <summary>
    /// FrameOrder is not LifecycleOrder. A service in both sequences has no
    /// reason for its position in one to decide its position in the other, and
    /// the two real participants prove it: Soundscapes is LAST in the level
    /// sequence (it only cares about shutdown) and FIRST in the frame.
    /// </summary>
    [Fact]
    public void FrameOrderIsIndependentOfLifecycleOrder()
    {
        int Frame(Type t) => Declared<IFrameLifecycle>(t, "get_FrameOrder");
        int Level(Type t) => Declared<IGameLifecycle>(t, "get_LifecycleOrder");

        Assert.True(Level(typeof(Soundscapes)) > Level(typeof(Arsenal)),
            "Soundscapes is expected to sit AFTER Arsenal in the level sequence");
        Assert.True(Frame(typeof(Soundscapes)) < Frame(typeof(Arsenal)),
            "...and BEFORE it in the frame, which is the order the two lines had "
            + "in ServerModule.Frame. One number cannot express both.");
    }

    [Fact]
    public void AParticipantThatImplementsNothingIsStillCalledSafely()
    {
        var services = new ServiceContainer();
        services.AddInstance(new Silent());
        var driver = new FrameDriver(new RecordingLog(), services);

        driver.PreThink(0.015f);
        driver.Think(0.015f);
        driver.PostThink(0.015f);

        Assert.Single(driver.Participants);
    }

    [Fact]
    public void TheFrameTimeReachesTheParticipant()
    {
        var services = new ServiceContainer();
        var keeper = new Timekeeper();
        services.AddInstance<IContract>(keeper);

        new FrameDriver(new RecordingLog(), services).Think(0.0075f);

        Assert.Equal(0.0075f, keeper.Last);
    }

    // ------------------------------------------------------------------
    // A throw, sixty-six times a second.
    // ------------------------------------------------------------------

    [Fact]
    public void AThrowingParticipantDoesNotStopTheOnesAfterIt()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Thrower());                 // order -10
        services.AddInstance<IContract>(new Recorder(log, 10, "after"));

        new FrameDriver(new RecordingLog(), services).Think(0.015f);

        Assert.Equal(new[] { "after:think" }, log);
    }

    [Fact]
    public void AThrowingParticipantIsStillCalledOnLaterFrames()
    {
        var services = new ServiceContainer();
        var thrower = new Thrower();
        services.AddInstance(thrower);
        var driver = new FrameDriver(new RecordingLog(), services);

        for (int i = 0; i < 5; i++)
            driver.Think(0.015f);

        Assert.True(thrower.Calls == 5,
            "the thrower was called " + thrower.Calls + " times in 5 frames. "
            + "Dropping a participant on its first fault turns one bad frame "
            + "into a service disabled for the rest of the map.");
    }

    /// <summary>
    /// The reason this driver is not LifecycleDriver. A level event that throws
    /// writes one line per map. A frame event that throws writes one per tick,
    /// which buries the first and only useful copy under thousands of repeats.
    /// </summary>
    [Fact]
    public void AThrowingParticipantIsLoggedOnceNotOncePerFrame()
    {
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };
        try
        {

            var services = new ServiceContainer();
            services.AddInstance(new Thrower());
            var driver = new FrameDriver(testLog, services);

            for (int i = 0; i < 200; i++)
                driver.Think(0.015f);

            Assert.True(lines.Count == 1,
                "200 throwing frames wrote " + lines.Count + " log lines, want 1. "
                + "At 66 ticks a second an unthrottled line is ~4000 a minute, "
                + "which makes the console useless for every other service.");
            Assert.Contains("boom", lines[0]);
        }
        finally
        {
        }
    }

    [Fact]
    public void TheSuppressedThrowsAreCountedAndReportedAtLevelEnd()
    {
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };
        try
        {

            var services = new ServiceContainer();
            services.AddInstance(new Thrower());
            var driver = new FrameDriver(testLog, services);

            for (int i = 0; i < 200; i++)
                driver.Think(0.015f);

            lines.Clear();
            driver.ReportFaults();

            Assert.Single(lines);
            Assert.Contains("200", lines[0]);
        }
        finally
        {
        }
    }

    [Fact]
    public void ReportFaultsSaysNothingWhenNothingThrew()
    {
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };
        try
        {

            var services = new ServiceContainer();
            services.AddInstance(new Recorder(new List<string>(), 0, "a"));
            var driver = new FrameDriver(testLog, services);
            driver.Think(0.015f);
            driver.ReportFaults();

            Assert.Empty(lines);
        }
        finally
        {
        }
    }

    /// <summary>
    /// The count is per level, so a quiet second map does not re-report the
    /// first one's faults.
    /// </summary>
    [Fact]
    public void ReportFaultsClearsTheCount()
    {
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };
        try
        {

            var services = new ServiceContainer();
            services.AddInstance(new Thrower());
            var driver = new FrameDriver(testLog, services);

            for (int i = 0; i < 3; i++)
                driver.Think(0.015f);

            driver.ReportFaults();
            lines.Clear();
            driver.ReportFaults();

            Assert.Empty(lines);
        }
        finally
        {
        }
    }

    /// <summary>Each point counts separately: a throw in Think is not a throw in PreThink.</summary>
    [Fact]
    public void ThePointsAreCountedSeparately()
    {
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };
        try
        {

            var services = new ServiceContainer();
            services.AddInstance(new BothEnds());
            var driver = new FrameDriver(testLog, services);

            driver.PreThink(0.015f);
            driver.PostThink(0.015f);

            Assert.True(lines.Count == 2,
                "a participant throwing at two different points wrote "
                + lines.Count + " line(s), want 2 -- one point's first throw "
                + "must not silence another's.");
            Assert.Contains(lines, l => l.Contains("OnPreThink"));
            Assert.Contains(lines, l => l.Contains("OnPostThink"));
        }
        finally
        {
        }
    }

    private sealed class BothEnds : IFrameLifecycle
    {
        void IFrameLifecycle.OnPreThink(float dt) => throw new InvalidOperationException("pre");
        void IFrameLifecycle.OnPostThink(float dt) => throw new InvalidOperationException("post");
    }

    // ------------------------------------------------------------------
    // Where the three points are in the native frame.
    //
    // Source-scanned rather than run, because what is being checked is that
    // C++ calls managed at the right MOMENT, and the moment is a position in a
    // file the managed suite cannot execute. The ordering assertion below is
    // here because I got it wrong once: the first version of EndGameFrame put
    // the managed call after the drain, which holds a participant's writes
    // until the next frame.
    // ------------------------------------------------------------------

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative));

    // PreThinkIsCalledFromFrameUpdatePreEntityThink and
    // ThinkIsCalledFromFrameUpdatePostEntityThink WERE HERE and are retired with
    // their subject (plan_server_native_v2 T4e): they read the SourceSharpManaged
    // row of kServerSystems[] in shell_serversystems.cpp, which is gone, and their
    // second halves had read vacuously since E18 (they looked for a
    // g_ManagedApi.FramePreThink( call the bodies no longer made, and an IndexOf of
    // -1 passed the "within 400 characters" test). The subject -- the managed
    // pre-think inside the pre-entity-think pass, the managed frame inside the
    // post-entity-think pass after the physics step -- is held behaviourally now:
    // ServerSystemsTests.ThePreThinkPassIsTheManagedRowAlone and
    // .ThePostThinkPassStepsPhysicsThenTheManagedFrameThenDrains, and
    // ServerGameDllTests.AFrameRunsStocksStepsInStocksOrder for where the passes
    // sit in the frame.

    // PostThinkIsDispatchedBeforeTheLateDrain WAS HERE: a source scan of
    // CSourceSharpGameRules::EndGameFrame for the FramePostThink call before
    // SourceSharp_DrainEntitySlotsLate. Both are managed's calls since
    // plan_server_native_v2 T4e, so the order is held behaviourally:
    // ServerGameDllTests.TheLastManagedPointRunsAfterTheRulesAndBeforeTheLateDrain.

    // ------------------------------------------------------------------
    // The think sequence that used to be CSourceSharpGameRules::Think.
    // ------------------------------------------------------------------

    /// <summary>
    /// The three services reached through OnGameRulesThink all declare frame
    /// participation, and in the order the hand-written lines had.
    /// </summary>
    [Fact]
    public void TheFormerGameRulesThinkListAreAllFrameParticipants()
    {
        (Type Type, string Was)[] expected =
        {
            (typeof(GameRules), "_rules?.Think()"),
            (typeof(Players),   "_players.Think(_clock.Now, WantsToRespawn)"),
            (typeof(Pickups),   "_pickups.Think(_clock.Now)"),
        };

        foreach (var (type, was) in expected)
        {
            Assert.True(typeof(IFrameLifecycle).IsAssignableFrom(type),
                type.Name + " no longer implements IFrameLifecycle. It used to be "
                + "the line `" + was + "` in ServerModule.OnGameRulesThink, which "
                + "is not called any more -- so dropping the interface stops it "
                + "running at all, every frame, silently.");
        }

        int Order(Type t) => Declared<IFrameLifecycle>(t, "get_FrameOrder");

        Assert.True(Order(typeof(GameRules)) < Order(typeof(Players)));
        Assert.True(Order(typeof(Players)) < Order(typeof(Pickups)));
    }

    /// <summary>
    /// ...and they run AFTER the two that were ServerModule.Frame's own list.
    /// The two native hooks ran in that order — the gamesystem's before the
    /// rules object's — so the merged sequence has to preserve it.
    /// </summary>
    [Fact]
    public void TheFrameListRunsBeforeTheFormerGameRulesThinkList()
    {
        int Order(Type t) => Declared<IFrameLifecycle>(t, "get_FrameOrder");

        Assert.True(Order(typeof(Arsenal)) < Order(typeof(GameRules)),
            "the arsenal was the LAST line of ServerModule.Frame and the rules "
            + "think was the FIRST line of OnGameRulesThink, reached from a "
            + "later native hook. Ordering them the other way round reverses "
            + "two hooks the engine calls in sequence.");
        Assert.True(Order(typeof(Soundscapes)) < Order(typeof(GameRules)));
    }

    /// <summary>
    /// And the slot it used is left unassigned, so the pointer is NULL and the
    /// guarded native call cannot happen.
    /// </summary>
    [Fact]
    public void TheRetiredSlotIsNotAssigned()
    {
        var entry = Read("src/sourcesharp/managed/SourceSharp.Shared.Interop/SideLoad.cs");

        Assert.DoesNotContain("OnGameRulesThink    = &", entry);
        Assert.DoesNotContain("OnGameRulesThink = &", entry);
    }

    /// <summary>
    /// Every constructor dependency of a service ServerModule builds with
    /// <c>Create</c> must be registered BEFORE that Create runs.
    ///
    /// <para>
    /// Create resolves constructor arguments eagerly, so a registration that
    /// comes afterwards throws at server startup — and nothing in the managed
    /// build says so, because it is an ordering fact about one method body
    /// rather than a type error. This is not hypothetical: adding
    /// IPlayerButtons to Players put its registration 260 lines after the
    /// Create that needed it, and the suite stayed green.
    /// </para>
    /// </summary>
    [Fact]
    public void TheServicesCreateNeedsAreRegisteredBeforeTheCreate()
    {
        var module = Read("src/sourcesharp/managed/SourceSharp.Server/ServerModule.cs");

        // The two dependencies this change added, and the Creates that take them.
        (string Registration, string Create)[] pairs =
        {
            ("_services.AddInstance<IPlayerButtons>(_buttons);", "_services.Create<Players>()"),
            ("_services.AddInstance<IGameClock>(_engine.Clock());", "_services.Create<Players>()"),
            ("_services.AddInstance<IGameClock>(_engine.Clock());", "_services.Create<Pickups>()"),
        };

        foreach (var (registration, create) in pairs)
        {
            int reg = module.IndexOf(registration, StringComparison.Ordinal);
            int made = module.IndexOf(create, StringComparison.Ordinal);

            Assert.True(reg >= 0, "cannot find the registration: " + registration);
            Assert.True(made >= 0, "cannot find the Create: " + create);
            Assert.True(reg < made,
                registration + " is registered AFTER " + create + ", which needs "
                + "it. Create resolves constructor arguments eagerly, so this "
                + "throws at server startup -- and every managed test still "
                + "passes, because the tests construct these by hand.");
        }
    }

    // ------------------------------------------------------------------
    // PreClientUpdate — the fourth point.
    // ------------------------------------------------------------------

    [Fact]
    public void PreClientUpdateCallsOnlyItsOwnMethod()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(log, 0, "a"));

        new FrameDriver(new RecordingLog(), services).PreClientUpdate(0.015f);

        Assert.Equal(new[] { "a:preclient" }, log);
    }

    /// <summary>
    /// No participant in this build implements it, and the pass is still safe.
    /// The stage is here because a stage the driver calls and nobody implements
    /// costs one empty loop, whereas a stage that does not exist costs an
    /// interface change on the day something needs it (Part 3a).
    /// </summary>
    [Fact]
    public void PreClientUpdateReachesAThreePointParticipantAtNoneOfItsPoints()
    {
        var p = new ThreePointParticipant();
        var services = new ServiceContainer();
        services.AddInstance(p);

        new FrameDriver(new RecordingLog(), services).PreClientUpdate(0.015f);

        Assert.Empty(p.Seen);
    }

    /// <summary>
    /// The fourth point has its OWN fault counter.
    ///
    /// <para>
    /// This is the regression the fourth point could plausibly cause: the fault
    /// array is indexed <c>participant * points + point</c>, and a fourth point
    /// added without widening the stride would land on the previous
    /// participant's slot — silencing a real throw and mis-attributing a count.
    /// </para>
    /// </summary>
    [Fact]
    public void APreClientUpdateFaultIsCountedApartFromAPostThinkFault()
    {
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };
        try
        {

            var services = new ServiceContainer();
            services.AddInstance(new LastTwoEnds());
            var driver = new FrameDriver(testLog, services);

            driver.PostThink(0.015f);
            driver.PreClientUpdate(0.015f);

            Assert.True(lines.Count == 2,
                "a participant throwing at PostThink and at PreClientUpdate wrote "
                + lines.Count + " line(s), want 2 -- the two points share a fault "
                + "slot, so one point's first throw silenced the other's.");
            Assert.Contains(lines, l => l.Contains("OnPostThink"));
            Assert.Contains(lines, l => l.Contains("OnPreClientUpdate"));
        }
        finally
        {
        }
    }

    /// <summary>
    /// ...and the level-end report names it by its own name rather than by the
    /// point next to it.
    /// </summary>
    [Fact]
    public void TheLevelEndReportNamesPreClientUpdate()
    {
        var lines = new List<string>();
        var testLog = new RecordingLog();
        try
        {
            var services = new ServiceContainer();
            services.AddInstance(new LastTwoEnds());
            var driver = new FrameDriver(testLog, services);

            driver.PreClientUpdate(0.015f);
            driver.PreClientUpdate(0.015f);

            testLog.Line = (text, _) => lines.Add(text);
            driver.ReportFaults();

            Assert.Contains(lines, l => l.Contains("OnPreClientUpdate") && l.Contains("2 frames"));
        }
        finally
        {
        }
    }

    // ------------------------------------------------------------------
    // Ties are impossible here too.
    // ------------------------------------------------------------------

    [Fact]
    public void TwoFrameParticipantsAtTheSameOrderAreRefusedWhenTheDriverIsBuilt()
    {
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(new List<string>(), 300, "a"));
        services.AddInstance<IContract>(new Recorder(new List<string>(), 300, "b"));

        var e = Assert.Throws<StageCollisionException>(() => new FrameDriver(new RecordingLog(), services));

        Assert.Contains("FrameOrder", e.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(FrameStage), e.Message, StringComparison.Ordinal);
        Assert.Contains("300", e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The FRAME enum, not the level one. Two enums exist precisely so a frame
    /// position cannot be written where a level position was meant; a collision
    /// message that pointed at LifecycleStage would send the reader to the wrong
    /// list.
    /// </summary>
    [Fact]
    public void TheFrameCollisionDoesNotPointAtTheLevelEnum()
    {
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(new List<string>(), 300, "a"));
        services.AddInstance<IContract>(new Recorder(new List<string>(), 300, "b"));

        var e = Assert.Throws<StageCollisionException>(() => new FrameDriver(new RecordingLog(), services));

        Assert.DoesNotContain(nameof(LifecycleStage), e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every concrete <see cref="IFrameLifecycle"/> in the SERVER assembly —
    /// asked of the assembly rather than listed here, for
    /// <see cref="LifecycleTests"/>'s reason.
    /// </summary>
    private static Type[] RealFrameParticipants() =>
        typeof(GameRules).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IFrameLifecycle).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>THE INVARIANT, frame side: no two real participants share a stage.</summary>
    [Fact]
    public void NoTwoRealParticipantsShareAFrameStage()
    {
        var byValue = new Dictionary<int, Type>();

        foreach (var t in RealFrameParticipants())
        {
            int value = Declared<IFrameLifecycle>(t, "get_FrameOrder");

            Assert.False(byValue.TryGetValue(value, out var first),
                (first?.Name ?? "?") + " and " + t.Name + " both declare FrameOrder "
                + value + ". Two participants at one value run in whatever order the "
                + "container resolved them -- and a frame sequence's order is "
                + "observable 66 times a second and printed never. Give one of them "
                + "its own FrameStage.");

            byValue[value] = t;
        }

        Assert.NotEmpty(byValue);
    }

    /// <summary>
    /// The frame participants and their stages, measured before the fourth
    /// point was added and recorded so the addition's claim — that it is
    /// additive — is checkable.
    /// </summary>
    private static readonly (Type Type, int Order)[] BeforePreClientUpdate =
    {
        (typeof(Soundscapes), 100),
        (typeof(Arsenal),     200),
        (typeof(GameRules),   300),
        (typeof(Players),     400),
        (typeof(Pickups),     500),
    };

    /// <summary>
    /// The frame participants added since, each with the phase that added it.
    ///
    /// <para>
    /// <b>The same shape <c>LifecycleTests.AddedSinceTheSplit</c> already
    /// has, and for the same reason.</b> <see cref="BeforePreClientUpdate"/> is
    /// a measurement of a commit and must not be edited; a participant that
    /// appears without being named HERE is one nobody decided to add, which is
    /// what the fact below is for.
    /// </para>
    /// </summary>
    private static readonly (Type Type, int Order)[] AddedSincePreClientUpdate =
    {
        (typeof(SoundEnt), 600),   // A3 second pair: the AI sound list's sweep
        (typeof(SoundController), 700),   // A3 second pair: the envelope ramps

        // A3 FIFTH: the parallel hierarchy's own frame, and it is the first
        // entry here that is not an append. CSourceSharpStandaloneSystem was row
        // 11 of kServerSystems[] and SourceSharpManaged is row 12 with nothing
        // between them, so its post-entity think ran immediately before every
        // participant above -- all of which run from inside row 12's body. FIFTY
        // is the value that reproduces that, and the alternative was renumbering
        // the five in BeforePreClientUpdate, which
        // EveryFrameParticipantStillDeclaresTheStageItDeclaredBefore refuses by
        // name.
        (typeof(StandaloneHierarchy), 50),

        // 8e-render: what fog each viewer is standing in, into each player's
        // networked state. AT OnPreClientUpdate, which is where stock's
        // UpdateAllClientData runs -- "fills in CClientData values for local
        // player just before sending over wire" is exactly this job. Numbered
        // after SoundController only because the two share a point and a stage
        // value has to be unique.
        (typeof(WorldFog), 800),

        // defect-skybox: what 3D skybox each viewer is shown, into each
        // player's networked state. At the SAME point and with a stronger claim
        // on it than the fog has: the skybox is the thing stock's
        // ClientData_Update is FOR -- playerlocaldata.cpp's body is the sky
        // camera and nothing else. After WorldFog only because a stage value
        // has to be unique.
        (typeof(WorldSkybox), 900),

        // player-msgs: each player's client-data update, the frame's Damage
        // message (CHL2_Player::UpdateClientData). At the same point as the fog
        // and the skybox, after both only because a stage value is unique.
        (typeof(PlayerClientData), 1000),

        // plan_physics_bones_native_zero 2.3: the queued physics impact and
        // break sounds, CPhysicsHook::PreClientUpdate's. Stock ran that row
        // FIRST, before the sound controller, so it is numbered below every
        // other OnPreClientUpdate participant.
        (typeof(PhysicsSoundBatch), 650),

        // tf2 stage 0.7 (U1): the mode proxy's warmup, TWO HUNDRED FIFTY —
        // before Rules at 300, because when the warmup ends the round starts,
        // and the win check of a round that has not started must not run in
        // that frame. Inserted in the spacing's own gap (between Arsenal 200
        // and Rules 300), so none of the five recorded participants moved.
        (typeof(GameModes), 250),

        // tf2 stage 0.7 (D9/D10): the per-player composite's tick, FOUR
        // HUNDRED FIFTY — after the death cycle at 400, because a component
        // reads the frame's death outcome rather than the other way round.
        // Inserted in the spacing's own gap (400/500); the recorded
        // participants keep their numbers.
        (typeof(PlayerComposites), 450),
        // ManageObjectRelocation, which ends CHL2MPRules::Think: just after
        // the rules, in the 300/400 gap.
        (typeof(PlacedObjects), 310),
    };

    [Fact]
    public void TheFourthPointAddedNoFrameParticipantAndRemovedNone()
    {
        Assert.Equal(
            BeforePreClientUpdate.Select(x => x.Type.Name)
                                 .Concat(AddedSincePreClientUpdate.Select(x => x.Type.Name))
                                 .OrderBy(n => n, StringComparer.Ordinal),
            RealFrameParticipants().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every frame participant added since declares the stage recorded against
    /// it, so a new one cannot be renumbered silently either.
    /// </summary>
    [Fact]
    public void EveryFrameParticipantAddedSinceDeclaresTheStageRecordedForIt()
    {
        foreach (var (type, order) in AddedSincePreClientUpdate)
        {
            int now = Declared<IFrameLifecycle>(type, "get_FrameOrder");
            Assert.True(now == order,
                type.Name + " declares FrameOrder " + now
                + ", and the list in this file records " + order + ".");
        }
    }

    [Fact]
    public void EveryFrameParticipantStillDeclaresTheStageItDeclaredBefore()
    {
        foreach (var (type, order) in BeforePreClientUpdate)
        {
            int now = Declared<IFrameLifecycle>(type, "get_FrameOrder");
            Assert.True(now == order,
                type.Name + " declares FrameOrder " + now + ", was " + order
                + " before PreClientUpdate was added. Adding a point is not "
                + "supposed to move anyone.");
        }
    }

    /// <summary>
    /// None of the five implements the fourth point, so none of them gained a
    /// per-frame call it was not written for.
    /// </summary>
    [Fact]
    public void NoExistingFrameParticipantImplementsPreClientUpdate()
    {
        foreach (var (type, _) in BeforePreClientUpdate)
        {
            var map = type.GetInterfaceMap(typeof(IFrameLifecycle));

            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].Name != "OnPreClientUpdate")
                    continue;

                Assert.True(map.TargetMethods[i].DeclaringType == typeof(IFrameLifecycle),
                    type.Name + " implements OnPreClientUpdate. Nothing calls that "
                    + "point in this build yet, so a body there runs at a moment "
                    + "the shell does not reach -- which reads as code that does "
                    + "nothing rather than as an error.");
            }
        }
    }

    private static int Declared<TIface>(Type t, string getter)
    {
        var target = t.GetInterfaceMap(typeof(TIface)).TargetMethods
                      .FirstOrDefault(m => m.Name.EndsWith(getter, StringComparison.Ordinal));
        Assert.True(target is not null, t.Name + " does not declare " + getter);
        return (int)target!.Invoke(
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t), null)!;
    }
}
