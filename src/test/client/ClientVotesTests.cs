using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The client's side of a vote: what the panel shows and what the wait says.</summary>
public class ClientVotesTests
{
    [Theory]
    [InlineData(134f, "Wait 2:14")]
    [InlineData(59.2f, "Wait 1:00")]
    [InlineData(5f, "Wait 0:05")]
    public void The_wait_reads_in_minutes_and_seconds_rounded_up(float seconds, string text)
        => Assert.Equal(text, ClientVotes.Wait(seconds));

    [Fact]
    public void The_two_sides_agree_on_the_phases_wire_values()
    {
        Assert.Equal(0, (byte)VotePhase.Idle);
        Assert.Equal(1, (byte)VotePhase.Voting);
        Assert.Equal(0, (byte)VoteRefusal.None);
    }
}

/// <summary>The vote key at the main menu: a vote is a level's.</summary>
public class ClientVotesOpenTests
{
    private sealed class Pointer : IRadialPointer
    {
        public void Centre()
        {
        }
    }

    private sealed class Clock : IGameClock
    {
        public float Now => 100f;
    }

    private readonly ChatFeedTests.RecordingEngineClient _engine = new([]);
    private readonly RadialMenus _radial = new(new Pointer());

    private ClientVotes Votes() => new(_radial, null!, null!, _engine, new Clock(), null!, null!);

    [Fact]
    public void In_a_game_the_key_opens_the_ring()
    {
        Votes().Open();
        Assert.True(_radial.Showing);
    }

    [Fact]
    public void Out_of_a_game_the_key_opens_nothing()
    {
        _engine.InGame = false;
        Votes().Open();
        Assert.False(_radial.Showing);
    }

    [Fact]
    public void Leaving_the_game_closes_the_ring()
    {
        Votes().Open();
        _engine.InGame = false;
        Assert.False(_radial.Showing);
    }
}
