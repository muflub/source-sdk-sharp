using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The lifecycle: participants found by the container, called in sequence.
///
/// <para>
/// <b>What this replaces, and therefore what it has to protect.</b>
/// <c>ServerModule.LevelInit</c> was six hand-written lines, one per service
/// that needed clearing on a map change. The failure mode of a list like that
/// is silence: a new service does not join it, nothing says so, and the damage
/// shows up later as handles that outlived the map they came from. So the
/// checks below are about DISCOVERY and ORDER, which are the two things that
/// can now go wrong instead.
/// </para>
/// </summary>
public class LifecycleTests
{
    private sealed class Recorder : IGameLifecycle, IContract
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
        int IGameLifecycle.LifecycleOrder => _order;

        void IGameLifecycle.OnLevelInit(string mapName) => _log.Add(Name + ":init:" + mapName);
        void IGameLifecycle.OnLevelShutdown() => _log.Add(Name + ":shutdown");
        void IGameLifecycle.OnShutdown() => _log.Add(Name + ":quit");
    }

    private sealed class Thrower : IGameLifecycle
    {
        int IGameLifecycle.LifecycleOrder => 5;
        void IGameLifecycle.OnLevelInit(string mapName) => throw new InvalidOperationException("boom");
    }

    /// <summary>A service that is registered but is NOT a participant.</summary>
    private sealed class Bystander { }

    private interface IContract { }
    private sealed class ByContract : IContract, IGameLifecycle
    {
        public int Inits;
        int IGameLifecycle.LifecycleOrder => 200;
        void IGameLifecycle.OnLevelInit(string mapName) => Inits++;
    }

    /// <summary>
    /// Declares no stage at all, so it sits at the interface default. Two of
    /// these is the collision a participant reaches by writing nothing.
    /// </summary>
    private sealed class Undeclared : IGameLifecycle { }
    private sealed class AlsoUndeclared : IContract, IGameLifecycle { }

    /// <summary>
    /// The PRE-SPLIT participant shape, exactly: it implements OnLevelInit and
    /// OnLevelShutdown and knows nothing about PreEntity or PostEntity. Every
    /// one of the seven real participants is this shape, which is what makes
    /// what happens to this one a statement about them.
    /// </summary>
    private sealed class PreSplitParticipant : IGameLifecycle
    {
        public readonly List<string> Seen = new();
        int IGameLifecycle.LifecycleOrder => 100;
        void IGameLifecycle.OnLevelInit(string mapName) => Seen.Add("init:" + mapName);
        void IGameLifecycle.OnLevelShutdown() => Seen.Add("shutdown");
    }

    /// <summary>A participant written AFTER the split, saying which half it means.</summary>
    private sealed class SplitAwareParticipant : IGameLifecycle
    {
        public readonly List<string> Seen = new();
        int IGameLifecycle.LifecycleOrder => 300;
        void IGameLifecycle.OnPostInit() => Seen.Add("postinit");
        void IGameLifecycle.OnLevelInitPreEntity(string mapName) => Seen.Add("init.pre:" + mapName);
        void IGameLifecycle.OnLevelInitPostEntity(string mapName) => Seen.Add("init.post:" + mapName);
        void IGameLifecycle.OnLevelShutdownPreEntity() => Seen.Add("shutdown.pre");
        void IGameLifecycle.OnLevelShutdownPostEntity() => Seen.Add("shutdown.post");
    }

    /// <summary>A lazily registered type that records whether it was built.</summary>
    private sealed class LazyNonParticipant
    {
        public static int Built;
        public LazyNonParticipant() { Built++; }
    }

    // ------------------------------------------------------------------
    // Discovery.
    // ------------------------------------------------------------------

    [Fact]
    public void EveryRegisteredParticipantIsFoundAndNothingElseIs()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(log, 0, "a"));
        services.AddInstance(new Bystander());

        var found = services.ResolveAll<IGameLifecycle>();

        Assert.True(found.Count == 1,
            "found " + found.Count + " participant(s), want 1 -- the Bystander is "
            + "registered and does not implement IGameLifecycle, so returning it "
            + "would hand the driver something with no event to call.");

        // NOTE ON WHAT THIS DOES AND DOES NOT COVER. ResolveAll rejects a
        // non-participant TWICE: once on the registration's declared concrete
        // type, before building it, and again on the built instance. Only the
        // second is load-bearing for THIS check -- deleting the first leaves the
        // result identical, which was confirmed by mutating it. The first exists
        // solely so lazy registrations are not constructed, and that is what
        // FindingParticipantsDoesNotBuildTheServicesThatAreNotOne covers.
    }

    /// <summary>
    /// A participant registered under a CONTRACT is still found. This is not
    /// hypothetical: ServerModule registers GameRules as IGameRules
    /// deliberately, to keep ResetRound out of reach of anything running inside
    /// a tick, so a lookup that matched on the contract type would find none of
    /// the real participants.
    /// </summary>
    [Fact]
    public void AParticipantRegisteredUnderAContractIsStillFound()
    {
        var services = new ServiceContainer();
        var instance = new ByContract();
        services.AddInstance<IContract>(instance);

        var found = services.ResolveAll<IGameLifecycle>();

        Assert.True(found.Count == 1,
            "a participant registered as IContract was not found. The match has "
            + "to be on the INSTANCE's type, not on the contract it was "
            + "registered under.");
        Assert.Same(instance, found[0]);
    }

    /// <summary>
    /// Finding participants must not build every other registration.
    ///
    /// Lazy registration is what lets the module register in any order it likes
    /// — see ServiceContainer.Add — and an implementation that resolved
    /// everything to find out what it was would construct the whole graph at
    /// whatever moment the first lifecycle event arrived.
    /// </summary>
    [Fact]
    public void FindingParticipantsDoesNotBuildTheServicesThatAreNotOne()
    {
        LazyNonParticipant.Built = 0;

        var services = new ServiceContainer();
        services.Add<LazyNonParticipant>();
        services.AddInstance(new Recorder(new List<string>(), 0, "a"));

        services.ResolveAll<IGameLifecycle>();

        Assert.True(LazyNonParticipant.Built == 0,
            "a lazily registered NON-participant was constructed " +
            LazyNonParticipant.Built + " time(s) just by looking for "
            + "participants. Registrations are lazy on purpose; building them "
            + "all at the first lifecycle event throws that away.");
    }

    [Fact]
    public void AnObjectRegisteredTwiceIsCalledOnce()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        var one = new ByContract();

        services.AddInstance<IContract>(one);
        services.AddInstance(one);

        var driver = new LifecycleDriver(new RecordingLog(), services);
        driver.LevelInit("dm_test");

        Assert.True(one.Inits == 1,
            "a participant registered under two contracts was called "
            + one.Inits + " times. Dispatching the same event to one object "
            + "twice is not something any participant is written to expect.");
    }

    // ------------------------------------------------------------------
    // Order.
    // ------------------------------------------------------------------

    [Fact]
    public void ParticipantsRunInLifecycleOrderNotRegistrationOrder()
    {
        var log = new List<string>();
        var services = new ServiceContainer();

        // Registered back to front on purpose: this is the real shape, where
        // Arsenal registers early and has to run last.
        services.AddInstance(new Recorder(log, 30, "third"));
        services.AddInstance<IContract>(new Recorder(log, 10, "first"));

        new LifecycleDriver(new RecordingLog(), services).LevelInit("dm_test");

        Assert.Equal(new[] { "first:init:dm_test", "third:init:dm_test" }, log);
    }

    // ------------------------------------------------------------------
    // Ties are impossible, not resolved.
    //
    // WHAT THIS REPLACES. There used to be a fact here called
    // EqualOrdersKeepRegistrationOrder, asserting that two participants at the
    // same LifecycleOrder ran in the order the container returned them. It
    // passed, and it was documenting the defect: registration order is the
    // order the AddInstance lines happen to sit in inside
    // ServerModule.Initialize, so a sequence two participants depend on could
    // be changed by moving an unrelated line, and nothing anywhere would say
    // so. Refusing the tie is the fix, and the fact that asserted the old
    // behaviour cannot survive it.
    // ------------------------------------------------------------------

    [Fact]
    public void TwoParticipantsAtTheSameOrderAreRefusedWhenTheDriverIsBuilt()
    {
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(new List<string>(), 200, "a"));
        services.AddInstance<IContract>(new Recorder(new List<string>(), 200, "b"));

        Assert.Throws<StageCollisionException>(() => new LifecycleDriver(new RecordingLog(), services));
    }

    /// <summary>
    /// It fires when the DRIVER is built, which is startup — not at the first
    /// map change. A collision found on the first level change is a collision
    /// that shipped.
    /// </summary>
    [Fact]
    public void TheCollisionIsRefusedBeforeAnyEventIsDispatched()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(log, 200, "a"));
        services.AddInstance<IContract>(new Recorder(log, 200, "b"));

        Assert.Throws<StageCollisionException>(() => new LifecycleDriver(new RecordingLog(), services));

        Assert.True(log.Count == 0,
            "the collision was reported, but " + log.Count + " participant(s) had "
            + "already been called. Detection has to happen before the first "
            + "dispatch or something has already run in the undecided order.");
    }

    /// <summary>
    /// The message names BOTH participants and the value. A collision is a
    /// two-line fix in one file only if the reader is told which two lines; a
    /// bare "duplicate order 200" leaves them grepping for whoever else claimed
    /// it, which is exactly the cross-file search this whole mechanism exists
    /// to remove.
    /// </summary>
    [Fact]
    public void TheCollisionMessageNamesBothParticipantsAndTheValue()
    {
        var services = new ServiceContainer();
        services.AddInstance(new Recorder(new List<string>(), 200, "a"));
        services.AddInstance<IContract>(new ByContract());   // also 200, below

        var e = Assert.Throws<StageCollisionException>(() => new LifecycleDriver(new RecordingLog(), services));

        Assert.Contains(nameof(Recorder), e.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ByContract), e.Message, StringComparison.Ordinal);
        Assert.Contains("200", e.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LifecycleStage), e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Unspecified</c> is NOT exempt, and it is the case that matters most:
    /// it is the interface default, so it is where a participant that forgot to
    /// declare a stage lands. Two of those is two participants whose relative
    /// order nobody chose, arrived at by nobody writing anything.
    /// </summary>
    [Fact]
    public void TwoParticipantsThatBothLeaveTheStageUnspecifiedAreRefusedToo()
    {
        var services = new ServiceContainer();
        services.AddInstance(new Undeclared());
        services.AddInstance<IContract>(new AlsoUndeclared());

        var e = Assert.Throws<StageCollisionException>(() => new LifecycleDriver(new RecordingLog(), services));

        Assert.Contains(nameof(Undeclared), e.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AlsoUndeclared), e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One participant at <c>Unspecified</c> is fine. The rule is uniqueness,
    /// not "everyone must declare" — a single participant with no opinion has
    /// nobody to be ambiguous against.
    /// </summary>
    [Fact]
    public void OneParticipantAtUnspecifiedIsAccepted()
    {
        var services = new ServiceContainer();
        services.AddInstance(new Undeclared());
        services.AddInstance<IContract>(new Recorder(new List<string>(), 100, "a"));

        var driver = new LifecycleDriver(new RecordingLog(), services);

        Assert.Equal(2, driver.Participants.Count);
    }

    // ------------------------------------------------------------------
    // Isolation.
    // ------------------------------------------------------------------

    /// <summary>
    /// One participant throwing must not eat the rest.
    ///
    /// With the hand-written list this was a real hazard: a throwing Reset
    /// aborted LevelInit, the services after it never ran, and the map loaded
    /// anyway — so the failure surfaced later as stale handles rather than at
    /// the line that caused it.
    /// </summary>
    [Fact]
    public void AThrowingParticipantDoesNotStopTheOnesAfterIt()
    {
        var log = new List<string>();
        var services = new ServiceContainer();
        services.AddInstance(new Thrower());                    // order 5
        services.AddInstance<IContract>(new Recorder(log, 10, "after"));

        new LifecycleDriver(new RecordingLog(), services).LevelInit("dm_test");

        Assert.Contains("after:init:dm_test", log);
    }

    // ------------------------------------------------------------------
    // The real wiring.
    // ------------------------------------------------------------------

    /// <summary>
    /// The six services that used to be the hand-written list all declare
    /// participation now. Checked by TYPE rather than by running a server,
    /// because what regressed before was a class quietly not being on a list.
    /// </summary>
    [Fact]
    public void TheServicesThatUsedToBeTheHandWrittenListAllDeclareIt()
    {
        (Type Type, string Was)[] expected =
        {
            (typeof(GameRules),     "_rules?.ResetRound()"),
            (typeof(Pickups),       "_pickups.Reset()"),
            (typeof(Corpses),       "_corpses.Reset()"),
            (typeof(Players),       "_players.Reset()"),
            (typeof(PlayerButtons), "_buttons.Reset()"),
            (typeof(Arsenal),       "_arsenal.Reset() + PrecacheAll()"),
            (typeof(Soundscapes),   "_soundscapes.Reset() (level shutdown)"),
        };

        foreach (var (type, was) in expected)
        {
            Assert.True(typeof(IGameLifecycle).IsAssignableFrom(type),
                type.Name + " no longer implements IGameLifecycle. It used to be "
                + "the hand-written line `" + was + "` in ServerModule, and "
                + "dropping the interface without putting that line back means "
                + "it is never cleared on a map change and nothing reports it.");
        }
    }

    /// <summary>
    /// ...and their order is the order the six lines ran in. The numbers are
    /// spaced by ten so one can be inserted between two without renumbering.
    /// </summary>
    [Fact]
    public void TheOrderIsTheOrderTheOldLinesRanIn()
    {
        // Read the declared order off the type rather than constructing these
        // services, which need a live engine. The interface's default is 0, so
        // a participant that forgot to declare one would collide with every
        // other default and sort arbitrarily.
        int Declared(Type t) =>
            (int)t.GetInterfaceMap(typeof(IGameLifecycle))
                  .TargetMethods
                  .First(m => m.Name.EndsWith("get_LifecycleOrder", StringComparison.Ordinal))
                  .Invoke(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t), null)!;

        Assert.True(Declared(typeof(GameRules)) < Declared(typeof(Pickups)));
        Assert.True(Declared(typeof(Pickups)) < Declared(typeof(Corpses)));
        Assert.True(Declared(typeof(Corpses)) < Declared(typeof(Players)));
        Assert.True(Declared(typeof(Players)) < Declared(typeof(PlayerButtons)));
        Assert.True(Declared(typeof(PlayerButtons)) < Declared(typeof(Arsenal)));
    }

    // ------------------------------------------------------------------
    // The PreEntity/PostEntity split, and the claim that nothing moved.
    // ------------------------------------------------------------------

    /// <summary>
    /// Every concrete <see cref="IGameLifecycle"/> in the SERVER assembly.
    ///
    /// <para>
    /// Read off the shipped assembly rather than from a list written here. A
    /// list in the test is a list that has to be remembered, which is the
    /// failure IGameLifecycle exists to end; asking the assembly means a new
    /// participant is in the invariant the moment it compiles.
    /// </para>
    ///
    /// <para>
    /// It is a SUPERSET of what the container is given — registration happens
    /// in ServerModule.Initialize and needs a live engine — and a superset is
    /// the safe direction: a type that implements the interface and collides is
    /// caught here whether or not it happens to be registered today.
    /// </para>
    /// </summary>
    private static Type[] RealLifecycleParticipants() =>
        typeof(GameRules).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IGameLifecycle).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Reads the stage a type DECLARES, without constructing it.</summary>
    private static int DeclaredOrder(Type t) =>
        (int)t.GetInterfaceMap(typeof(IGameLifecycle))
              .TargetMethods
              .First(m => m.Name.EndsWith("get_LifecycleOrder", StringComparison.Ordinal))
              .Invoke(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t), null)!;

    /// <summary>
    /// THE INVARIANT: no two real participants share a stage value.
    ///
    /// <para>
    /// Both sides come from the assembly — the participants are found by
    /// reflection and each value is read off the type. Nothing here restates a
    /// number that the code also states, so adding an eighth participant that
    /// reuses Corpses' 300 fails this without anyone editing the test.
    /// </para>
    /// </summary>
    [Fact]
    public void NoTwoRealParticipantsShareALifecycleStage()
    {
        var byValue = new Dictionary<int, Type>();

        foreach (var t in RealLifecycleParticipants())
        {
            int value = DeclaredOrder(t);

            Assert.False(byValue.TryGetValue(value, out var first),
                (first?.Name ?? "?") + " and " + t.Name + " both declare LifecycleOrder "
                + value + ". Two participants at one value run in whatever order the "
                + "container resolved them, which is registration order -- the one "
                + "input nobody decided. Give one of them its own LifecycleStage.");

            byValue[value] = t;
        }

        Assert.NotEmpty(byValue);
    }

    /// <summary>
    /// The participants and the stages they declare, MEASURED on the commit
    /// before the split landed (fd4ba695) and recorded here so the split's own
    /// claim — that nothing moves — is checkable rather than asserted.
    ///
    /// <para>
    /// This is the one place a written-down list is the right thing: the
    /// expected side is a measurement of the past, which the code cannot
    /// supply.
    /// </para>
    /// </summary>
    private static readonly (Type Type, int Order)[] BeforeTheSplit =
    {
        (typeof(GameRules),     100),
        (typeof(Pickups),       200),
        (typeof(Corpses),       300),
        (typeof(Players),       400),
        (typeof(PlayerButtons), 500),
        (typeof(Arsenal),       600),
        (typeof(Soundscapes),   700),
    };

    /// <summary>
    /// The participants added since, each with the phase that added it — so
    /// that "nothing moved" stays checkable while the set is allowed to grow.
    ///
    /// <para>
    /// <b>This list is why the fact below did not simply get its expectation
    /// rewritten.</b> <see cref="BeforeTheSplit"/> is a measurement of a commit
    /// and must not be edited; a participant that appears without being named
    /// HERE is one nobody decided to add, which is exactly what the fact is
    /// for. plan_gamedll_after.md Phase A3 moved <c>CDecalEmitterSystem</c> and
    /// <c>CPropData</c> out of <c>kServerSystems[]</c> and into this list, and
    /// they are the first two entries it has ever had.
    /// </para>
    /// </summary>
    private static readonly (Type Type, int Order)[] AddedSinceTheSplit =
    {
        (typeof(PropData),      800),   // A3: propdata.txt's chunk table
        (typeof(DecalEmitter),  900),   // A3: the decal script
        (typeof(SoundController), 1000),  // A3 second pair: the envelope ramps
        (typeof(StandaloneHierarchy), 1100),  // A3 fifth: the hierarchy's teardown
        (typeof(TemplateStore), 1200),  // Wave 1 / D11: the template store
        (typeof(GlobalStateTable), 50),  // Phase 4 exit / p4-lifecycle: the
                                        // cross-level global state table's write
                                        // window. FIFTY, so it is shut before any
                                        // participant that destroys an entity in the
                                        // pre-entity shutdown pass reaches it.
        (typeof(WorldFog),      1300),  // 8e-render: the viewer's world fog
        (typeof(WorldSkybox),   1400),  // defect-skybox: the viewer's 3D skybox.
                                        // SHUTDOWN ONLY, like the fog: a map's sky
                                        // camera must be forgotten between levels or
                                        // the next map's first poll can skip its write.
        (typeof(PhysicsCallbacks), 1500),  // plan_physics_managed B2: vphysics'
                                           // callbacks on managed's own tables,
                                           // installed at pre-entity level init.
        (typeof(PhysicsEntitySolver), 1600),  // review 2026-10-10 F1: the stuck-debris
                                              // solvers cross a reload on the save
                                              // tier (physics_npc_solver.cpp:400-406).
        (typeof(PlayerShadowRecovery), 1700),  // review 2026-10-10 F2: each player's
                                               // m_oldOrigin crosses a reload on the
                                               // save tier (player.cpp:311).
        (typeof(PhysicsSolver), 1800),  // plan_physics_bones_native_zero 2.1: the
                                        // solver's level state (the disabled
                                        // pairs) goes with the environment.
        (typeof(ServerSkeletons), 1150),  // physics_bones_native_zero 1.2: the managed
                                          // server skeletons and their model images
                                          // go at level shutdown, after the hierarchy's
                                          // teardown has stopped asking for attachments.
        (typeof(PhysicsConstraints), 1900),  // plan_physics_bones_native_zero 2.4: the
                                             // constraint and spring table goes with the
                                             // environment and crosses a reload.
        (typeof(PhysicsMotionControllers), 2000),  // 2.4: the motion-controller table, the same.
        (typeof(PhysicsFrame), 2300),  // 2.3: physics is paused between the level's
                                       // pre- and post-entity init (CPhysicsHook's m_bPaused).
        (typeof(PhysicsSoundBatch), 2200),  // 2.3: the queued physics sounds go
                                            // with the level (CPhysicsHook's
                                            // LevelShutdownPostEntity emptied them).
        (typeof(PhysWorldReport), 2100),  // 2.3: the SS-PHYSWORLD report's automatic run
                                          // at post-entity init (sourcesharp_physworld_report),
                                          // native's CPhysicsHook::LevelInitPostEntity's.
        (typeof(NetTrafficProfiler), 0),  // managed DI: the payload traffic view's
                                          // snapshots are keyed by entity index, so
                                          // they go at level shutdown. Unordered: it
                                          // was a static cleared after the driver ran,
                                          // and nothing reads it during the change.
        (typeof(GameModes),          75),  // tf2 stage 0.7 (U1): the mode proxy's
                                           // pick, SEVENTY-FIVE — before Rules at
                                           // 100, because the rules' own level-init
                                           // reset asks the picked mode's round
                                           // machine. Inserted in the spacing's own
                                           // gap (between GlobalState 50 and Rules
                                           // 100) rather than appended, so no
                                           // existing participant moved.
        (typeof(PhysCannonWorld),   650),  // weapon_physcannon: the grab controllers
                                           // hold the level's objects and go with it;
                                           // the gun's sounds are precached at level
                                           // init. Inserted in the Arsenal/Soundscapes
                                           // gap; no recorded participant moved.
        (typeof(PlayerComposites),  450),  // tf2 stage 0.7 (D9/D10): the per-player
                                           // composite's level-shutdown detach,
                                           // FOUR HUNDRED FIFTY — beside the player
                                           // roster at 400 and after it, its
                                           // midpoint, since its components are
                                           // per-player state that must not cross
                                           // the reload. Inserted in the 400/500
                                           // gap; no recorded participant moved.
        (typeof(AreaPortals),       750),  // plan_server_native_v2 T4d: the area
                                           // portal records, which were native and
                                           // are the map's; cleared at level
                                           // shutdown, carried across a reload.
                                           // In the 700/1900 gap; none moved.
        (typeof(DefaultLightStyles), 760), // plan_server_native_v2 T4d: the
                                           // level's default light styles, which
                                           // CWorld::Precache wrote; at level init
                                           // pre-entity. In the same gap.
        (typeof(UseableLadders),    720),  // the useable ladders: the map's
                                           // func_useableladder and
                                           // info_ladder_dismount entities, emptied
                                           // at level shutdown. In the 700/750 gap;
                                           // none moved.
    };

    [Fact]
    public void TheSplitAddedNoParticipantAndRemovedNone()
    {
        var now = RealLifecycleParticipants();

        Assert.Equal(
            BeforeTheSplit.Select(x => x.Type.Name)
                          .Concat(AddedSinceTheSplit.Select(x => x.Type.Name))
                          .OrderBy(n => n, StringComparer.Ordinal),
            now.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every participant added since the split declares the stage recorded
    /// against it — the same property
    /// <see cref="EveryParticipantStillDeclaresTheStageItDeclaredBeforeTheSplit"/>
    /// holds for the original seven, so that a new one cannot be renumbered
    /// silently either.
    /// </summary>
    [Fact]
    public void EveryParticipantAddedSinceTheSplitDeclaresTheStageRecordedForIt()
    {
        foreach (var (type, order) in AddedSinceTheSplit)
        {
            Assert.True(DeclaredOrder(type) == order,
                type.Name + " declares LifecycleOrder " + DeclaredOrder(type)
                + ", and the list in this file records " + order + ".");
        }
    }

    [Fact]
    public void EveryParticipantStillDeclaresTheStageItDeclaredBeforeTheSplit()
    {
        foreach (var (type, order) in BeforeTheSplit)
        {
            Assert.True(DeclaredOrder(type) == order,
                type.Name + " declares LifecycleOrder " + DeclaredOrder(type)
                + ", was " + order + " before the PreEntity/PostEntity split. The "
                + "split is supposed to be additive; a changed stage means it "
                + "moved a participant relative to the others.");
        }
    }

    /// <summary>
    /// None of the seven overrides the PostEntity half, so all seven are still
    /// reached through the interface default that forwards to
    /// <c>OnLevelInit</c>. This is the structural half of the "nothing moved"
    /// claim; <see cref="ThePostEntityHalfReachesAParticipantThatOnlyImplementsOnLevelInit"/>
    /// is the behavioural half.
    /// </summary>
    [Fact]
    public void NoExistingParticipantOverridesThePostEntityHalf()
    {
        foreach (var (type, _) in BeforeTheSplit)
        {
            var map = type.GetInterfaceMap(typeof(IGameLifecycle));

            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                string name = map.InterfaceMethods[i].Name;
                if (name is not ("OnLevelInitPostEntity" or "OnLevelShutdownPostEntity"))
                    continue;

                Assert.True(map.TargetMethods[i].DeclaringType == typeof(IGameLifecycle),
                    type.Name + " overrides " + name + ". It was written before the "
                    + "split and declares OnLevelInit/OnLevelShutdown; an override "
                    + "here would shadow that body and the service would silently "
                    + "stop being reset on a map change.");
            }
        }
    }

    /// <summary>
    /// The behavioural half: a participant of the pre-split shape is reached by
    /// the PostEntity dispatch, and by the driver method ServerModule already
    /// calls.
    /// </summary>
    [Fact]
    public void ThePostEntityHalfReachesAParticipantThatOnlyImplementsOnLevelInit()
    {
        var p = new PreSplitParticipant();
        var services = new ServiceContainer();
        services.AddInstance(p);

        new LifecycleDriver(new RecordingLog(), services).LevelInitPostEntity("dm_test");

        Assert.Equal(new[] { "init:dm_test" }, p.Seen);
    }

    [Fact]
    public void TheDriverMethodTheModuleCallsIsThePostEntityHalf()
    {
        var p = new PreSplitParticipant();
        var services = new ServiceContainer();
        services.AddInstance(p);

        // What ServerModule.LevelInit calls, unchanged by this work.
        new LifecycleDriver(new RecordingLog(), services).LevelInit("dm_test");

        Assert.Equal(new[] { "init:dm_test" }, p.Seen);
    }

    /// <summary>
    /// ...and the PreEntity half does NOT reach it. If it did, the split would
    /// be cosmetic: every existing participant would run twice, once in a world
    /// with no entities, and the second run would look like the first working.
    /// </summary>
    [Fact]
    public void ThePreEntityHalfDoesNotReachAnOnLevelInitOnlyParticipant()
    {
        var p = new PreSplitParticipant();
        var services = new ServiceContainer();
        services.AddInstance(p);

        new LifecycleDriver(new RecordingLog(), services).LevelInitPreEntity("dm_test");

        Assert.Empty(p.Seen);
    }

    [Fact]
    public void LevelShutdownIsThePostEntityHalfToo()
    {
        var p = new PreSplitParticipant();
        var services = new ServiceContainer();
        services.AddInstance(p);

        var driver = new LifecycleDriver(new RecordingLog(), services);
        driver.LevelShutdownPreEntity();

        Assert.Empty(p.Seen);

        driver.LevelShutdown();

        Assert.Equal(new[] { "shutdown" }, p.Seen);
    }

    /// <summary>
    /// A participant written after the split gets four distinct moments, and
    /// they arrive in stock's order.
    /// </summary>
    [Fact]
    public void ASplitAwareParticipantSeesEachHalfSeparately()
    {
        var p = new SplitAwareParticipant();
        var services = new ServiceContainer();
        services.AddInstance(p);

        var driver = new LifecycleDriver(new RecordingLog(), services);
        driver.PostInit();
        driver.LevelInitPreEntity("dm_test");
        driver.LevelInitPostEntity("dm_test");
        driver.LevelShutdownPreEntity();
        driver.LevelShutdownPostEntity();

        Assert.Equal(
            new[] { "postinit", "init.pre:dm_test", "init.post:dm_test", "shutdown.pre", "shutdown.post" },
            p.Seen);
    }

    /// <summary>
    /// PostInit has no participant in this build and the pass is still safe to
    /// make. That is the whole argument for landing the stage early: it costs
    /// one empty loop now and an interface change later.
    /// </summary>
    [Fact]
    public void PostInitReachesNothingInTheCurrentBuildAndDoesNotThrow()
    {
        var p = new PreSplitParticipant();
        var services = new ServiceContainer();
        services.AddInstance(p);

        new LifecycleDriver(new RecordingLog(), services).PostInit();

        Assert.Empty(p.Seen);
    }

    /// <summary>
    /// The three stock stages this port deliberately does NOT carry. Recorded
    /// as a fact so a later reader finds a decision rather than an omission:
    /// OnSave/OnRestore are the save cluster (refused, plan_gamedll.md Part 7)
    /// and SafeRemoveIfDesired is a self-unregistration idiom with nothing in
    /// this build to unregister.
    /// </summary>
    [Fact]
    public void TheSaveClusterAndSafeRemoveAreNotOnTheInterface()
    {
        var names = typeof(IGameLifecycle).GetMethods().Select(m => m.Name).ToArray();

        Assert.DoesNotContain("OnSave", names);
        Assert.DoesNotContain("OnRestore", names);
        Assert.DoesNotContain("SafeRemoveIfDesired", names);
    }
}
