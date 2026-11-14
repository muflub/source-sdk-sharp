extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientCarriers = client::TF2.Client.Player.TFClientCarriers;
using ClientProbe = client::TF2.Client.Player.TFClientItemMeters;

namespace TF2.Tests.Client;

/// <summary>
/// The client half of the held-aside item meter: the local player's carrier
/// rows (<see cref="client::TF2.Shared.Player.TFPlayerNetState.ItemMeters"/>
/// and <see cref="client::TF2.Shared.Player.TFPlayerNetState.ItemMeterTimes"/>,
/// the pair stock's per-slot charge rides the player with,
/// <c>tf_player_shared.cpp:7870-7904</c>) read by catalog row. A player whose
/// carrier has not arrived, or a row past the carrier's width, is
/// unpublished — the switch keeps the meter the hands' last deploy left.
/// </summary>
public sealed class TFClientItemMetersTests
{
    private const int Local = 3;

    [Fact]
    public void The_local_carriers_pair_is_what_the_switch_advances()
    {
        var (carriers, engine) = Carrier();

        var probe = new ClientProbe(carriers, engine);

        Assert.True(probe.MeterOf(2, out float meter, out float stamp));
        Assert.Equal(0.75f, meter, 4);
        Assert.Equal(9.5f, stamp, 4);
    }

    [Fact]
    public void A_player_with_no_carrier_is_unpublished()
    {
        var engine = Engine();

        var probe = new ClientProbe(new ClientCarriers(), engine);

        Assert.False(probe.MeterOf(2, out float meter, out float stamp));
        Assert.Equal(0f, meter, 4);
        Assert.Equal(0f, stamp, 4);
    }

    [Fact]
    public void A_row_the_player_does_not_carry_is_unpublished()
    {
        var (carriers, engine) = Carrier();

        var probe = new ClientProbe(carriers, engine);

        Assert.False(probe.MeterOf(1_000, out float meter, out float stamp));
        Assert.Equal(0f, meter, 4);
        Assert.Equal(0f, stamp, 4);
    }

    /// <summary>A row far past the slots' count reads through its slot: the jar was row 33 on 2fort.</summary>
    [Fact]
    public void A_row_far_past_the_carriers_slots_reads_through_its_slot()
    {
        var (carriers, engine) = Carrier(row: 120);

        var probe = new ClientProbe(carriers, engine);

        Assert.True(probe.MeterOf(120, out float meter, out float stamp));
        Assert.Equal(0.75f, meter, 4);
        Assert.Equal(9.5f, stamp, 4);
    }

    /// <summary>The local player's carrier, holding one pair: row 2 is 0.75 at 9.5.</summary>
    private static (ClientCarriers, client::SourceSharp.Abi.RecordingEngine) Carrier(int row = 2)
    {
        var engine = Engine();
        var carriers = new ClientCarriers();
        var data = new ClientPlayerData(engine, carriers, new NoStandalones(),
                                        new client::TF2.Client.Player.TFClientPlayerResources(),
                                        engine,
                                        new client::TF2.Shared.TFClassTable(
                                            new client::SourceSharp.Abi.RecordingLog(),
                                            new client::SourceSharp.Abi.RecordingEngine()),
                                        new client::TF2.Client.Player.TFClientConditionDriver(new NoHooks()),
                                        new client::SourceSharp.Abi.RecordingLog());
        data.State.BindLocal();
        data.State.OwnerIndex = Local;
        data.OnSpawn();
        data.State.CarriedRows[0] = row + 1;
        data.State.ItemMeters[0] = 0.75f;
        data.State.ItemMeterTimes[0] = 9.5f;
        return (carriers, engine);
    }

    private static client::SourceSharp.Abi.RecordingEngine Engine()
    {
        var engine = new client::SourceSharp.Abi.RecordingEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        return engine;
    }

    private sealed class NoStandalones : client::SourceSharp.Client.IClientStandalones
    {
        public System.Collections.Generic.IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [];

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class NoHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
    {
        public System.Collections.Generic.IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

        public void Each(string what, global::System.Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
    }
}
