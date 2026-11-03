extern alias client;

using Xunit;

using ClientSide = client::TF2.Shared.Player;
using Dominations = client::TF2.Client.Ui.TFScoreboardDominations;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using State = client::TF2.Shared.Player.TFPlayerNetState;

namespace TF2.Tests.Client;

/// <summary>
/// The scoreboard's domination reads against the local player's own carrier
/// (stock's client arm: the shared answers only the local player's data,
/// tf_player_shared.cpp:7739-7751) — the nemesis pair of
/// tf_clientscoreboard.cpp:1507-1519.
/// </summary>
public sealed class TFScoreboardDominationsTests
{
    [Fact]
    public void A_row_that_dominates_the_local_player_reads_as_nemesis()
    {
        var data = new FakeData();
        data.Local!.PlayerDominatingMe[7] = true;           // player 7 dominates the local player
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 4);
        var source = new Dominations(new client::TF2.Client.Player.TFClientPlayerResources(), data, engine);

        Assert.True(source.DominatesLocal(7));
        Assert.False(source.DominatedByLocal(7));
    }

    [Fact]
    public void A_row_the_local_player_dominates_reads_as_dominated()
    {
        var data = new FakeData();
        data.Local!.PlayerDominated[7] = true;              // the local player dominates player 7
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 4);
        var source = new Dominations(new client::TF2.Client.Player.TFClientPlayerResources(), data, engine);

        Assert.True(source.DominatedByLocal(7));
        Assert.False(source.DominatesLocal(7));
    }

    [Fact]
    public void No_local_carrier_reads_no_pair()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 4);
        var source = new Dominations(new client::TF2.Client.Player.TFClientPlayerResources(),
                                     new FakeData { Local = null }, engine);

        Assert.False(source.DominatesLocal(7));
        Assert.False(source.DominatedByLocal(7));
        Assert.Equal(0, source.DominationsOf(7));
    }

    /// <summary>The local player's decoded carrier, as the client
    /// participant answers it.</summary>
    private sealed class FakeData : ClientSide.ITFPlayerData
    {
        public State? Local { get; set; } = new();

        public FakeData()
        {
            Local!.BindLocal();
            Local.OwnerIndex = 4;
        }

        public State? Predicted(int playerIndex) => playerIndex == 4 ? Local : null;

        public State? Networked(int playerIndex) => playerIndex == 4 ? Local : null;
    }
}
