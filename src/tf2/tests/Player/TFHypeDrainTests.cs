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
        TFScoutEnergydrinkConsumeRateConVar? consume = null, TFScoutEnergydrinkRegenRateConVar? regen = null)
    {
        var players = new TFMovementFixtures.Players();
        var state = players.Add(7, TFClass.Scout);
        var conditions = new RigConditions(players);
        var drain = new TFHypeDrain(players, conditions, consume ?? DeclaredConVars.Fresh<TFScoutEnergydrinkConsumeRateConVar>(),
                                    regen ?? DeclaredConVars.Fresh<TFScoutEnergydrinkRegenRateConVar>());
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

    /// <summary>
    /// The drink halves (<c>UpdateEnergyDrinkMeter</c>, :14080-14095): while
    /// the boost stands, the drink meter pays the frame at the whole consume
    /// rate — stock's 0.75 belongs to the hype half only — and an emptied
    /// meter ends both names of the boost together (:14084-14088).
    /// </summary>
    [Fact]
    public void The_boost_drains_the_drink_meter_at_the_whole_consume_rate()
    {
        var (state, drain, conditions) = Drain();
        state.EnergyDrinkMeter = 40f;
        conditions.Add(Player, TFCond.Phase);

        drain.Tick(10f); // the anchor frame: the first pays nothing
        drain.Tick(11f);

        Assert.Equal(40f - 12.5f, state.EnergyDrinkMeter, 4);           // :14083, no 0.75 as the hype half's (:14073)
        Assert.True(conditions.InCond(Player, TFCond.Phase));
    }

    [Fact]
    public void The_emptied_drink_meter_ends_both_names_of_the_boost()
    {
        var (state, drain, conditions) = Drain();
        state.EnergyDrinkMeter = 1f;
        conditions.Add(Player, TFCond.Phase);
        conditions.Add(Player, TFCond.EnergyBuff);

        drain.Tick(10f);
        drain.Tick(11f);

        // Stock leaves the drained meter past zero; the refill's next frame
        // takes it back up (:14086-14104's chain).
        Assert.Equal(1f - 12.5f, state.EnergyDrinkMeter, 4);
        Assert.False(conditions.InCond(Player, TFCond.Phase));          // :14086-14088 end as one
        Assert.False(conditions.InCond(Player, TFCond.EnergyBuff));
    }

    /// <summary>Nothing boosting (:14101-14119): the meter regains toward full
    /// at the regen rate and stops at the top.</summary>
    [Fact]
    public void The_drink_meter_regains_when_nothing_boosts()
    {
        var (state, drain, _) = Drain();
        state.EnergyDrinkMeter = 40f;

        drain.Tick(10f); // the anchor frame: the first pays nothing
        drain.Tick(11f);

        Assert.Equal(43.3f, state.EnergyDrinkMeter, 4);                 // :14101, the regen rate's 3.3 points a second
    }

    [Fact]
    public void The_regained_drink_meter_stops_at_full()
    {
        var (state, drain, _) = Drain();
        state.EnergyDrinkMeter = 40f;

        drain.Tick(10f);
        drain.Tick(100f);                                               // 89 s at the regen rate, far past the cap

        Assert.Equal(100f, state.EnergyDrinkMeter);                     // :14101 ceiling
    }
}
