using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D7: the server's <see cref="LivenessMirror"/> belongs to
/// the server's composition. The entity dispatch (Server.Interop) and the
/// registry's removal notify write it, the level shutdown clears it, and the
/// server's <c>EntityIsValid</c> (Abi) reads it through the attachment the
/// composition makes. All of them must reach the one instance the module
/// registered, and a second composition has its own.
///
/// <para>
/// Each composition is over an engine of its own (plan_abi_hotreload D8: the
/// module is handed its <see cref="ModuleEngine"/>), so the reader is that
/// engine's <c>EntityIsValid</c>, never the process's. <b>Process-wide</b>
/// still, because the hot-reload fact carries records through the process's
/// reload store. In a test process the slot behind the mirror answers false,
/// so "valid" can only have come from the mirror.
/// </para>
/// </summary>
public class LivenessMirrorCompositionTests
{
    private static readonly EntityHandle Prop = new(0x0005_0123u);

    // No thread marks a side since plan_side_isolation P5: the engine read is
    // the server's own, whichever side ran last.
    private static T OnServer<T>(Func<T> read) => read();

    private static bool Valid(EngineSide engine, EntityHandle entity) => OnServer(() => engine.EntityIsValid(entity));

    private static unsafe void Spawn(ServerModule module, EntityHandle entity)
        => OnServer(() => module.NewEntitiesBinding().Dispatch(entity.Value, (int)EntityOp.Spawn, null));

    private static ServerModule Composed(out EngineSide engine)
    {
        engine = new EngineSide(Side.Server);
        return Composed(engine);
    }

    private static ServerModule Composed(EngineSide engine)
    {
        var module = new ServerModule(new ModuleEngine(engine));
        module.ComposeEngineAreas();
        return module;
    }

    [Fact]
    public void ASpawnPushReachesTheServersEntityIsValid()
    {
        var module = Composed(out var engine);

        try
        {
            Assert.False(Valid(engine, Prop), "unknown to the mirror, the slot answers false (the negative arm)");

            Spawn(module, Prop);

            Assert.True(Valid(engine, Prop), "the dispatch and EntityIsValid reach one mirror");
        }
        finally { module.Shutdown(); }
    }

    [Fact]
    public void ASecondCompositionDoesNotSeeTheFirstsRecords()
    {
        var first = Composed(out var firstEngine);
        Spawn(first, Prop);
        var second = Composed(out var secondEngine);

        try
        {
            Assert.True(first.Services.Require<LivenessMirror>().TryLookup(Prop, out _),
                        "the first composition holds the record (the positive arm)");
            Assert.False(second.Services.Require<LivenessMirror>().TryLookup(Prop, out _),
                         "a second composition has a mirror of its own");
            Assert.True(Valid(firstEngine, Prop), "the first composition's engine answers from the first mirror");
            Assert.False(Valid(secondEngine, Prop), "and the second's engine answers from its own");
        }
        finally
        {
            second.Shutdown();
            first.Shutdown();
        }
    }

    [Fact]
    public void ShutdownTakesTheAttachmentBack()
    {
        var module = Composed(out var engine);
        var mirror = module.Services.Require<LivenessMirror>();

        Assert.Same(mirror, engine.Liveness);

        module.Shutdown();

        Assert.Null(engine.Liveness);
    }

    [Fact]
    public void TheRegistrysRemovalNotifyWritesTheSameMirror()
    {
        var module = Composed(out var engine);

        try
        {
            Spawn(module, Prop);
            Assert.True(Valid(engine, Prop), "alive before the removal (the positive arm)");

            module.Registry.NotifyRemoved(Prop);

            Assert.True(module.Services.Require<LivenessMirror>().TryLookup(Prop, out bool alive) && !alive,
                        "the death is recorded, not dropped");
            Assert.False(Valid(engine, Prop), "and EntityIsValid reads it");
        }
        finally { module.Shutdown(); }
    }

    [Fact]
    public void LevelShutdownClearsTheMirrorItIsHanded()
    {
        var module = Composed(out var engine);

        try
        {
            Spawn(module, Prop);
            Assert.True(Valid(engine, Prop), "alive before the shutdown (the positive arm)");

            new FrameBinding(new Crossings(), new RecordingLog(), new QuietModule(), module.Services.Require<LivenessMirror>(), engine.Models, engine.Breaks).LevelShutdown();

            Assert.Equal(0, module.Services.Require<LivenessMirror>().RecordCount);
        }
        finally { module.Shutdown(); }
    }

    [Fact]
    public void TheFrameHandlerTheRootBuildsHoldsThisCompositionsMirror()
    {
        var module = Composed(out _);

        try
        {
            var held = typeof(FrameBinding)
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(LivenessMirror))
                .GetValue(module.NewFrameBinding());

            Assert.Same(module.Services.Require<LivenessMirror>(), held);
        }
        finally { module.Shutdown(); }
    }

    [Fact]
    public void TheRerouteCommandTurnsThisCompositionsMirror()
    {
        var module = Composed(out _);

        try
        {
            var command = module.Services is ServiceContainer services
                ? services.Create<LivenessRerouteCommand>()
                : throw new InvalidOperationException("the module's container is a ServiceContainer");

            OnServer(() => { command.Execute("0"); return 0; });

            Assert.False(module.Services.Require<LivenessMirror>().Rerouted,
                         "cs_sv_liveness_reroute 0 reached the mirror EntityIsValid reads");
        }
        finally { module.Shutdown(); }
    }

    [Fact]
    public void AHotReloadCarriesTheRecordsToTheNextComposition()
    {
        // One engine across the reload, as in the game: the side's engine
        // outlives the module that is reloaded over it.
        var before = Composed(out var engine);
        Spawn(before, Prop);
        before.ReloadSave();
        before.Shutdown();

        var after = Composed(engine);

        try
        {
            Assert.False(Valid(engine, Prop), "the new composition starts empty (the setup took effect)");

            after.ReloadRestore();

            Assert.True(Valid(engine, Prop), "the handle spawned before the reload is answered without crossing");
        }
        finally { after.Shutdown(); }
    }

    /// <summary>
    /// TWO COMPOSITIONS, TWO SIDES (plan_abi_hotreload D8): the registry's
    /// shell calls and the liveness oracle each go to the side the module was
    /// composed over, never to a shared or active one.
    /// </summary>
    [Fact]
    public void EachCompositionsRegistryAndOracleAskItsOwnSide()
    {
        var first = Composed(out EngineSide firstSide);
        var second = Composed(out EngineSide secondSide);

        try
        {
            Assert.NotSame(firstSide, secondSide);
            Assert.Same(firstSide, first.Registry.Engine.Api);
            Assert.Same(secondSide, second.Registry.Engine.Api);
            Assert.Same(firstSide, first.Services.Require<LivenessSlotReference>().Side);
            Assert.Same(secondSide, second.Services.Require<LivenessSlotReference>().Side);
        }
        finally
        {
            second.Shutdown();
            first.Shutdown();
        }
    }

    private sealed class QuietModule : ISideModule
    {
        public void Initialize(Side side) { }
        public void Frame(float frameTime) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void ConCommandDispatch(int commandId, string args) { }
        public void Shutdown() { }
        public void DispatchGameEvent(string eventName, IntPtr handle) { }
    }
}
