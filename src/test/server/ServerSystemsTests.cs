using System;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>kServerSystems[]</c>'s order, held in C# since plan_server_native_v2 T4e
/// (<see cref="ServerSystems"/>). Each fact is one ordering the native table
/// and stock's <c>IGameSystem</c> walks gave: rows CGameStringPool,
/// CPhysicsHook, SourceSharpManaged; init passes forwards, shutdown passes
/// backwards; "the string system must init first + shutdown last" and
/// "physics must occur before the sound envelope manager" (gameinterface.cpp).
/// The managed row writes to the same call list as the engine's steps, so an
/// order across the two is one sequence.
/// </summary>
public class ServerSystemsTests
{
    private sealed class Row(RecordingEngine engine) : IServerSystemRow
    {
        public bool InitAnswer = true;
        public bool Throw;

        private void Note(string stage)
        {
            engine.Note("Row." + stage);
            if (Throw)
                throw new InvalidOperationException("row " + stage);
        }

        public bool Init() { Note("Init"); return InitAnswer; }
        public void LevelInitPreEntity(string mapName) => Note("LevelInitPreEntity");
        public void LevelInitPostEntity(string mapName) => Note("LevelInitPostEntity");
        public void LevelShutdownPreEntity() => Note("LevelShutdownPreEntity");
        public void LevelShutdownPostEntity() => Note("LevelShutdownPostEntity");
        public void FramePreThink(float frameTime) => Note("FramePreThink");
        public void Frame(float frameTime) => Note("Frame");
        public void PhysicsFrame() => Note("PhysicsFrame");
    }

    private readonly RecordingEngine _engine;
    private readonly Row _row;
    private readonly RecordingLog _log = new();
    private readonly ServerSystems _systems;

    public ServerSystemsTests()
    {
        _engine = Fake();
        _engine.Returns("Level.PhysicsInit", true);
        _row = new Row(_engine);
        _systems = new ServerSystems(_engine, _engine, _row, _log);
    }

    /// <summary>Every call, the lock's two ends included.</summary>
    private string[] Calls() => _engine.Calls.Select(c => c.Member).ToArray();

    /// <summary>The calls with the model-cache lock's two ends taken out.</summary>
    private string[] Steps() => Calls().Where(n => !n.EndsWith("ModelCacheLock", StringComparison.Ordinal)).ToArray();

    // ---- Init: InitAllSystems, forwards, stopping at the first refusal -------

    [Fact]
    public void InitRunsPhysicsBeforeTheManagedRow()
    {
        Assert.True(_systems.Init());
        Assert.Equal(new[] { "Level.PhysicsInit", "Row.Init" }, Steps());
    }

    [Fact]
    public void APhysicsRefusalStopsInitBeforeTheManagedRow()
    {
        _engine.Returns("Level.PhysicsInit", false);

        Assert.False(_systems.Init());
        Assert.Equal(new[] { "Level.PhysicsInit" }, Steps());
        Assert.Contains(_log.Lines, l => l.Warning && l.Text.Contains("'CPhysicsHook' refused to init", StringComparison.Ordinal));
    }

    [Fact]
    public void AManagedRowRefusalFailsInit()
    {
        _row.InitAnswer = false;

        Assert.False(_systems.Init());
        Assert.Contains(_log.Lines, l => l.Warning && l.Text.Contains("'SourceSharpManaged' refused to init", StringComparison.Ordinal));
    }

    // ---- the level passes ------------------------------------------------

    [Fact]
    public void ThePreEntityPassBuildsPhysicsBeforeTheManagedRow()
    {
        _systems.LevelInitPreEntity("dm_lockdown");
        Assert.Equal(new[] { "Level.PhysicsLevelInitPreEntity", "Row.LevelInitPreEntity" }, Steps());
    }

    [Fact]
    public void ThePostEntityPassUnpausesPhysicsBeforeTheManagedLevelInit()
    {
        _systems.LevelInitPostEntity("dm_lockdown");
        Assert.Equal(new[] { "Level.PhysicsLevelInitPostEntity", "Row.LevelInitPostEntity" }, Steps());
    }

    [Fact]
    public void ThePreEntityShutdownPassIsBackwards()
    {
        _systems.LevelShutdownPreEntity();
        Assert.Equal(new[] { "Row.LevelShutdownPreEntity", "Level.PhysicsLevelShutdownPreEntity" }, Steps());
    }

    /// <summary>
    /// Backwards, with the managed row's slot reset after its shutdown, and the
    /// string pool's FreeAll the very last call: "shutdown last".
    /// </summary>
    [Fact]
    public void ThePostEntityShutdownPassIsBackwardsWithTheStringPoolLast()
    {
        _systems.LevelShutdownPostEntity();
        Assert.Equal(new[]
        {
            "Row.LevelShutdownPostEntity", "Level.ResetEntitySlots",
            "Level.PhysicsLevelShutdownPostEntity", "Level.StringPoolLevelShutdownPostEntity",
        }, Steps());
    }

    // ---- the frame passes ------------------------------------------------

    /// <summary>Neither native row declares FrameUpdatePreEntityThink.</summary>
    [Fact]
    public void ThePreThinkPassIsTheManagedRowAlone()
    {
        _systems.FramePreThink(0.015f);
        Assert.Equal(new[] { "Row.FramePreThink" }, Steps());
    }

    /// <summary>
    /// The physics row's step, then the managed frame, then the slots drained
    /// after every managed write of the frame.
    /// </summary>
    [Fact]
    public void ThePostThinkPassStepsPhysicsThenTheManagedFrameThenDrains()
    {
        _systems.FramePostThink(0.015f);
        Assert.Equal(new[] { "Row.PhysicsFrame", "Row.Frame", "Frame.DrainEntitySlots" }, Steps());
    }

    // ---- the model-cache lock -----------------------------------------

    /// <summary>
    /// Every managed stage runs inside mdlcache's lock, as each row's stage
    /// did inside the native walk's MDLCACHE_CRITICAL_SECTION; the lock is
    /// taken and released once per pass.
    /// </summary>
    [Fact]
    public void EveryManagedStageRunsInsideTheModelCacheLock()
    {
        _systems.Init();
        _systems.LevelInitPreEntity("m");
        _systems.LevelInitPostEntity("m");
        _systems.LevelShutdownPreEntity();
        _systems.LevelShutdownPostEntity();
        _systems.FramePreThink(0.015f);
        _systems.FramePostThink(0.015f);

        int depth = 0, rowCalls = 0;
        foreach (string call in Calls())
        {
            if (call == "Frame.BeginModelCacheLock") depth++;
            else if (call == "Frame.EndModelCacheLock") depth--;
            else if (call.StartsWith("Row.", StringComparison.Ordinal))
            {
                rowCalls++;
                Assert.True(depth == 1, $"{call} ran inside the lock");
            }
            Assert.InRange(depth, 0, 1);
        }

        Assert.Equal(0, depth);
        Assert.Equal(8, rowCalls);
    }

    [Fact]
    public void TheLockIsReleasedWhenAManagedStageThrows()
    {
        _row.Throw = true;

        Assert.Throws<InvalidOperationException>(() => _systems.FramePostThink(0.015f));

        Assert.Equal("Frame.EndModelCacheLock", Calls().Last());
    }
}

/// <summary>
/// The managed row over the module (plan_server_native_v2 T4e). Each stage
/// was a managed-built slot whose generated thunk caught and reported what it
/// threw, so a fault stayed in its stage; the row keeps that.
/// </summary>
public class ManagedSystemRowTests
{
    private sealed class Module : ISideModule
    {
        public readonly List<string> Ran = [];
        public bool Throw;

        private void Run(string what)
        {
            Ran.Add(what);
            if (Throw)
                throw new InvalidOperationException(what);
        }

        public void Initialize(Side side) { }
        public void Frame(float frameTime) => Run("Frame");
        public void FramePreThink(float frameTime) => Run("FramePreThink");
        public void LevelInit(string mapName) => Run("LevelInit " + mapName);
        public void LevelShutdown() => Run("LevelShutdown");
        public void ConCommandDispatch(int commandId, string args) { }
        public void Shutdown() { }
        public void DispatchGameEvent(string eventName, IntPtr handle) { }
    }

    private readonly RecordingLog _log = new();
    private readonly Module _module = new();
    private bool _soundAnswer = true;

    private ManagedSystemRow Row()
        => new(_log, _module, () => _soundAnswer, map => _module.Ran.Add("pre " + map),
               () => _module.Ran.Add("shutdown pre"), () => _module.Ran.Add("physics"),
               new LivenessMirror(), Descriptors(),
               new BreakModels(_ => 0, _ => -1, _ => null, Descriptors(), new RecordingLog()));

    private static ModelDescriptors Descriptors() => new(_ => -1, _ => string.Empty, new RecordingLog());

    [Fact]
    public void InitIsTheSoundSystemsAnswer()
    {
        _soundAnswer = false;
        Assert.False(Row().Init());
        _soundAnswer = true;
        Assert.True(Row().Init());
    }

    [Fact]
    public void EachStageReachesItsOwnBody()
    {
        var row = Row();
        row.LevelInitPreEntity("m");
        row.LevelInitPostEntity("m");
        row.FramePreThink(0.015f);
        row.PhysicsFrame();
        row.Frame(0.015f);
        row.LevelShutdownPreEntity();
        row.LevelShutdownPostEntity();

        Assert.Equal(new[] { "pre m", "LevelInit m", "FramePreThink", "physics", "Frame", "shutdown pre", "LevelShutdown" },
                     _module.Ran);
    }

    /// <summary>A stage that throws reports it as its slot's thunk did, and does not unwind into the pass.</summary>
    [Fact]
    public void AThrowingStageIsReportedAndContained()
    {
        _module.Throw = true;

        Row().Frame(0.015f);

        Assert.Contains(_log.Lines, l => l.Warning && l.Text.Contains("EXCEPTION IN ServerSystems.Frame", StringComparison.Ordinal));
    }
}
