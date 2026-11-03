using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The taunt half of the player think (tf_player.cpp:1485-1507): a held
/// taunt asks <c>ShouldStopTaunting</c> and a yes is the condition down —
/// here the water half of it (tf_player_shared.cpp:13140-13144).
/// </summary>
public sealed class TFTauntThinkTests
{
    /// <summary>A taunted player whose carrier carries the condition for its full window.</summary>
    private static (PlayerRig Rig, TFPlayerConditions Conditions) Taunted()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        Assert.True(conditions.Add(PlayerRig.Player, TFCond.Taunting, 3f) is TFCondAdd.Added or TFCondAdd.Refreshed);
        return (rig, conditions);
    }

    /// <summary>The player entity under the water level, as the think's <c>GetWaterLevel</c> reads it.</summary>
    private static SsPlayer InWaterOf(PlayerRig rig, int level)
    {
        var player = new SsPlayer(rig.Engine, rig.Engine, TestServices.Impacts(rig.Engine), TestServices.Fluid())
        {
            Handle = PlayerRig.Player,
        };
        player.State.BindLocal();
        player.State.WaterLevel = (byte)level;
        return player;
    }

    private static TFTauntThink ThinkOf(PlayerRig rig, TFPlayerConditions conditions, SsPlayer player)
    {
        var think = new TFTauntThink(conditions, new Pool(player));
        think.Attach(PlayerRig.Player);
        return think;
    }

    [Fact]
    public void WaterAboveTheWaistDropsTheTaunt()
    {
        var (rig, conditions) = Taunted();
        var think = ThinkOf(rig, conditions, InWaterOf(rig, GameMovement.WlEyes));

        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Taunting)); // the premise: held
        think.Tick(1f);

        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Taunting));
    }

    [Fact]
    public void WaterAtTheWaistHoldsTheTaunt()
    {
        var (rig, conditions) = Taunted();
        var think = ThinkOf(rig, conditions, InWaterOf(rig, GameMovement.WlWaist));

        think.Tick(1f);

        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Taunting));
    }

    /// <summary>The directory the think reads the water through: the player, nothing else.</summary>
    private sealed class Pool(SsPlayer player) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => handle == PlayerRig.Player ? player : null;

        public ScriptedEntity? FindByName(string name) => null;

        public IEnumerable<ScriptedEntity> OfClass(string className) => [];

        public bool IsRegistered(string className) => false;

        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }
}
