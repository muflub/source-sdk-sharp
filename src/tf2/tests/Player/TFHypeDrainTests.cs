using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Tests.Combat;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFHypeDrain"/>: the drink's metered drain
/// (<c>CTFPlayerShared::UpdateEnergyDrinkMeter</c>'s hype half,
/// tf_player_shared.cpp:14071-14078) on the player's frames, the component
/// stage the composite runs it at.
/// </summary>
public sealed class TFHypeDrainTests
{
    private static readonly EntityHandle Player = new(0x0001_0000u | 7u);

    private static (TFPlayerNetState State, TFHypeDrain Drain, RigConditions Conditions) Drain(
        TFScoutEnergydrinkConsumeRateConVar? consume = null)
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(7, TFClass.Scout);
        var conditions = new RigConditions(players);
        var drain = new TFHypeDrain(players, conditions, consume);
        drain.Attach(Player);
        return (state, drain, conditions);
    }

    [Fact]
    public void The_drink_pays_its_frame_at_the_consume_rates_three_quarters()
    {
        var (state, drain, conditions) = Drain();
        state.HypeMeter = TFHype.FullMeter;
        conditions.Add(Player, TFCond.SodapopperHype);

        drain.Tick(10f); // the anchor frame: the first pays nothing (the rage drain's shape)
        drain.Tick(11f);

        Assert.Equal(TFHype.FullMeter - 12.5f * 0.75f, state.HypeMeter, 4);
        Assert.True(conditions.InCond(Player, TFCond.SodapopperHype));
    }

    [Fact]
    public void The_emptied_meter_takes_the_buff_with_it()
    {
        var (state, drain, conditions) = Drain();
        state.HypeMeter = 1f;
        conditions.Add(Player, TFCond.SodapopperHype);

        drain.Tick(10f);
        drain.Tick(11f);

        // Stock clamps nothing here (:14073-14077): the meter runs past zero
        // until the next SetScoutHypeMeter's clamp takes it.
        Assert.Equal(1f - 12.5f * 0.75f, state.HypeMeter, 4);
        Assert.False(conditions.InCond(Player, TFCond.SodapopperHype));
    }

    [Fact]
    public void The_convar_is_the_drains_rate()
    {
        var consume = new TFScoutEnergydrinkConsumeRateConVar();
        consume.Bind(-1, "tf_scout_energydrink_consume_rate", "20");
        var (state, drain, conditions) = Drain(consume);
        state.HypeMeter = TFHype.FullMeter;
        conditions.Add(Player, TFCond.SodapopperHype);

        drain.Tick(10f);
        drain.Tick(11f);

        Assert.Equal(TFHype.FullMeter - 20f * 0.75f, state.HypeMeter, 4);
    }

    [Fact]
    public void A_player_the_buff_leaves_pays_nothing()
    {
        var (state, drain, _) = Drain();
        state.HypeMeter = TFHype.FullMeter;

        drain.Tick(10f);
        drain.Tick(11f);

        Assert.Equal(TFHype.FullMeter, state.HypeMeter, 4);
    }
}
