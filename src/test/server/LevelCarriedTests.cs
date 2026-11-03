using System.Collections.Generic;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A hot reload swaps the composition under a running level, and no level init
/// runs for the new one: <see cref="IGameLifecycle.OnLevelCarried"/> is how a
/// participant holding level state learns the level it came up in. Without it
/// the AI's navigation answered "no level is loaded" on a loaded level until
/// the next map, and the daily gate's bots step, run after hot-reload, refused
/// every bot.
/// </summary>
public class LevelCarriedTests
{
    private sealed class Watcher(int order) : IGameLifecycle
    {
        public List<string> Seen { get; } = new();

        public int LifecycleOrder => order;

        void IGameLifecycle.OnLevelInitPostEntity(string mapName) => Seen.Add("init:" + mapName);

        void IGameLifecycle.OnLevelCarried(string mapName) => Seen.Add("carried:" + mapName);
    }

    private sealed class Thrower : IGameLifecycle
    {
        public int LifecycleOrder => ModStages.First;

        void IGameLifecycle.OnLevelCarried(string mapName) => throw new System.InvalidOperationException("no");
    }

    private static LifecycleDriver DriverOver(params IGameLifecycle[] participants)
    {
        var services = new ServiceContainer();
        foreach (var p in participants)
            services.AddInstance(p.GetType(), p);
        return new LifecycleDriver(new RecordingLog(), services);
    }

    private static (ServerModule Module, ReloadStore Store) Composed()
    {
        var engine = new ServerModuleEngine(TestSides.Create(Side.Server));
        var module = new ServerModule(engine);
        module.ComposeEngineAreas();
        return (module, engine.Reload);
    }

    [Fact]
    public void ACarriedLevelReachesTheCarriedStageAndNotTheLevelInit()
    {
        var watcher = new Watcher(ModStages.First + 1);

        DriverOver(watcher).LevelCarried("dm_lockdown");

        Assert.Equal(new[] { "carried:dm_lockdown" }, watcher.Seen);
    }

    [Fact]
    public void ALevelInitDoesNotReachTheCarriedStage()
    {
        var watcher = new Watcher(ModStages.First + 1);

        DriverOver(watcher).LevelInitPostEntity("dm_lockdown");

        Assert.Equal(new[] { "init:dm_lockdown" }, watcher.Seen);
    }

    [Fact]
    public void AThrowingParticipantDoesNotStopTheOnesAfterIt()
    {
        var watcher = new Watcher(ModStages.First + 1);

        DriverOver(new Thrower(), watcher).LevelCarried("dm_lockdown");

        Assert.Equal(new[] { "carried:dm_lockdown" }, watcher.Seen);
    }

    [Fact]
    public void ARestoreWithACarriedLevelKnowsItsMapAndThatItIsReady()
    {
        var (module, store) = Composed();
        try
        {
            Assert.Null(module.RunningLevel.Map);

            store.Put(ServerModule.LevelReloadSection, Encoding.UTF8.GetBytes("dm_lockdown"), 1);
            module.ReloadRestore();

            Assert.Equal(("dm_lockdown", true), (module.RunningLevel.Map, module.RunningLevel.IsReady));
            Assert.Null(store.Peek(ServerModule.LevelReloadSection));
        }
        finally { module.Shutdown(); }
    }

    [Fact]
    public void ARestoreWithNoCarriedLevelKnowsNoMap()
    {
        var (module, _) = Composed();
        try
        {
            module.ReloadRestore();

            Assert.Equal(((string?)null, false), (module.RunningLevel.Map, module.RunningLevel.IsReady));
        }
        finally { module.Shutdown(); }
    }
}
