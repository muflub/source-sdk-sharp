using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <c>CTFPlayer::CanHearAndReadChatFrom</c>'s dead-talk arm
/// (tf_player.cpp:19806-19814) as <see cref="TFChatAudience"/>: a dead
/// talker reaches a living listener over <c>tf_gravetalk</c>
/// (tf_gamerules.cpp:1112), always at the round's end
/// (<c>GR_STATE_TEAM_WIN</c> / <c>GR_STATE_GAME_OVER</c>), and every other
/// pair hears regardless of the convar.
/// </summary>
public class TFGravetalkFacts
{
    private static readonly EntityHandle Listener = new(0x010001);
    private static readonly EntityHandle Speaker = new(0x010002);

    private sealed class Round(RoundState state) : ITeamRoundState
    {
        public RoundState State { get; } = state;

        public bool InWaitingForPlayers => false;

        public bool InSetup => false;

        public float MapResetTime => 0f;

        public float StalemateStartTime => -1f;

        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    private static TFChatAudience Rig(bool speakerAlive, bool listenerAlive, string gravetalk, RoundState state)
    {
        var engine = new RecordingEngine();
        var convar = new TFGravetalkConVar();
        convar.Bind(-1, "tf_gravetalk", gravetalk);
        engine.ReturnsEachCall("Players.IsAlive", () =>
            engine.Calls[^1].Args[0] is EntityHandle h && h == Speaker ? speakerAlive : listenerAlive);
        return new TFChatAudience(convar, new Round(state), engine);
    }

    [Fact]
    public void A_living_pair_hears_with_the_gate_off()
    {
        var a = Rig(speakerAlive: true, listenerAlive: true, "0", RoundState.RoundRunning);
        Assert.True(a.CanHear(Listener, Speaker));
    }

    [Fact]
    public void A_dead_listener_hears_the_dead_talker_with_the_gate_off()
    {
        var a = Rig(speakerAlive: false, listenerAlive: false, "0", RoundState.RoundRunning);
        Assert.True(a.CanHear(Listener, Speaker));
    }

    [Fact]
    public void A_living_listener_hears_the_dead_talker_while_the_gate_is_on()
    {
        var a = Rig(speakerAlive: false, listenerAlive: true, "1", RoundState.RoundRunning);
        Assert.True(a.CanHear(Listener, Speaker));
    }

    [Fact]
    public void A_living_listener_misses_the_dead_talker_once_the_gate_is_off()
    {
        var a = Rig(speakerAlive: false, listenerAlive: true, "0", RoundState.RoundRunning);
        Assert.False(a.CanHear(Listener, Speaker));
    }

    [Theory]
    [InlineData(RoundState.TeamWin)]
    [InlineData(RoundState.GameOver)]
    public void The_rounds_end_is_heard_with_the_gate_off(RoundState state)
    {
        var a = Rig(speakerAlive: false, listenerAlive: true, "0", state);
        Assert.True(a.CanHear(Listener, Speaker));
    }
}
