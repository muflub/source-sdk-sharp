using SourceSharp.Abi;
using TF2.Descent.Server;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// A level's way down or up asks every 3 s while a hero stands in it (map commit 218175fc0's
/// <c>trigger_multiple</c>, <c>"wait" "3"</c>): its refusal is told once per stay, again when the
/// words change or after he has left and come back (<see cref="WayStays"/>).
/// </summary>
public sealed class WayStaysTests
{
    private const int Hero = 3;
    private const string Refused = "#Descent_Travel_NoLevel";

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 100f;
    }

    private readonly Clock _clock = new();
    private readonly RecordingEngine _net = new();
    private readonly ClientMessages _messages;
    private readonly WayStays _stays;

    public WayStaysTests()
    {
        _messages = new ClientMessages(_net);
        _stays = new WayStays(_messages, _clock);
    }

    private int Sent(int player = Hero) =>
        _net.Calls.Count(c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:notice" && c.ArgAs<int>(2) == player);

    private void AskAt(float now, string arg = "2", int player = Hero)
    {
        _clock.Now = now;
        _stays.Ask(player, () => _messages.Notice(player, Refused, arg));
    }

    [Fact]
    public void A_refusal_repeated_while_the_hero_stands_in_the_way_is_told_once()
    {
        AskAt(100f);
        AskAt(103f);
        AskAt(106f);

        Assert.Equal(1, Sent());
    }

    [Fact]
    public void A_refusal_whose_words_change_is_told_again()
    {
        AskAt(100f, "2");
        AskAt(103f, "3");

        Assert.Equal(2, Sent());
    }

    [Fact]
    public void A_hero_who_left_and_came_back_is_told_again()
    {
        AskAt(100f);
        AskAt(103f);
        AskAt(120f);

        Assert.Equal(2, Sent());
    }

    [Fact]
    public void A_clock_started_again_by_a_new_level_is_a_new_stay()
    {
        AskAt(100f);
        AskAt(2f);

        Assert.Equal(2, Sent());
    }

    [Fact]
    public void Another_heros_ask_does_not_quiet_this_one()
    {
        AskAt(100f, player: Hero);
        AskAt(101f, player: Hero + 1);

        Assert.Equal((1, 1), (Sent(Hero), Sent(Hero + 1)));
    }

    [Fact]
    public void A_notice_outside_a_way_is_always_sent()
    {
        AskAt(100f);
        _messages.Notice(Hero, Refused, "2");
        _messages.Notice(Hero, Refused, "2");

        Assert.Equal(3, Sent());
    }
}
