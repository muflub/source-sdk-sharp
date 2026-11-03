extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientCarriers = client::TF2.Client.Player.TFClientCarriers;
using ClientProbe = client::TF2.Client.Player.TFClientWeaponClips;

namespace TF2.Tests.Client;

/// <summary>
/// The client half of the carried clip: the local player's carrier rows
/// (<see cref="client::TF2.Shared.Player.TFPlayerNetState.Clips"/>) read by
/// catalog row, stock's per-weapon <c>m_iClip1</c> arrival
/// (<c>basecombatweapon_shared.cpp</c>'s send table) that the switch deploys
/// with. A player whose carrier has not arrived, or a row past the carrier's
/// width, is unpublished.
/// </summary>
public sealed class TFClientWeaponClipsTests
{
    private const int Local = 3;

    [Fact]
    public void The_local_carriers_row_is_the_clip_the_switch_deploys_with()
    {
        var (carriers, engine) = Carrier();

        var probe = new ClientProbe(carriers, engine);

        Assert.True(probe.ClipOf(2, out int clip));
        Assert.Equal(5, clip);
    }

    [Fact]
    public void A_player_with_no_carrier_is_unpublished()
    {
        var (_, engine) = Carrier();

        var probe = new ClientProbe(new ClientCarriers(), engine);

        Assert.False(probe.ClipOf(2, out int clip));
        Assert.Equal(-1, clip);
    }

    [Fact]
    public void A_row_past_the_carriers_width_is_unpublished()
    {
        var (carriers, engine) = Carrier();

        var probe = new ClientProbe(carriers, engine);

        Assert.False(probe.ClipOf(1_000, out int clip));
        Assert.Equal(-1, clip);
    }

    /// <summary>The local player's carrier, holding one word: row 2 is 5.</summary>
    private static (ClientCarriers, client::SourceSharp.Abi.RecordingEngine) Carrier()
    {
        var engine = new client::SourceSharp.Abi.RecordingEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);
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
        data.State.Clips[2] = 5;
        return (carriers, engine);
    }

    private sealed class NoStandalones : client::SourceSharp.Client.IClientStandalones
    {
        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [];

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class NoHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
    {
        public global::System.Collections.Generic.IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

        public void Each(string what, global::System.Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
    }
}
