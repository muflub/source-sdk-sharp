using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4d: the level's default light styles, which
/// CWorld::Precache wrote (world.cpp's g_DefaultLightstyles, by way of
/// world_adapter.cpp): stock's thirteen presets into styles 0-12, in order,
/// then style 63 ("testing") dark. Set at level init before any entity spawns,
/// so a light that switches its own style afterwards wins.
///
/// <para>
/// The preset strings are <see cref="LightStyles"/>' (LightTests holds them to
/// stock's table); what is held here is that every one of them, and only
/// them, is written to its own slot, then 63.
/// </para>
/// </summary>
public sealed class DefaultLightStylesTests
{
    [Fact]
    public void LevelInitWritesStocksThirteenStylesThenDarkensStyle63()
    {
        var engine = new RecordingEngine();

        ((IGameLifecycle)new DefaultLightStyles(engine)).OnLevelInitPreEntity("dm_test");

        var written = engine.Calls.Where(c => c.Member == "Visibility.SetLightStyle")
                                  .Select(c => ((int)c.Arg(0)!, (string)c.Arg(1)!))
                                  .ToArray();

        Assert.Equal(13, LightStyles.PresetCount);
        Assert.Equal(Enumerable.Range(0, 13).Select(i => (i, LightStyles.Default(i))).Append((63, "a")), written);
        Assert.Equal((0, "m"), written[0]);
        Assert.Equal((12, "mmnnmmnnnmmnn"), written[12]);
    }

    [Fact]
    public void ItRunsBeforeAnyMapEntityCouldSwitchAStyle()
        => Assert.Equal((int)LifecycleStage.DefaultLightStyles,
                        ((IGameLifecycle)new DefaultLightStyles(new RecordingEngine())).LifecycleOrder);
}
