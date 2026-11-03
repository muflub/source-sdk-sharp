using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The numbers the HUD shows for what the local player's hits did.</summary>
public class DamageDealtNumbersTests
{
    private const int Me = 5, Them = 7, Other = 8;

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Players : IKillFeedPlayers
    {
        public bool TryFind(int userId, out string name, out int team, out bool local)
        {
            (name, team, local) = ("x", 0, userId == Me);
            return userId != 0;
        }
    }

    private static (DamageDealtNumbers Numbers, Clock Clock) Make()
    {
        var clock = new Clock();
        return (new DamageDealtNumbers(clock, new Players()), clock);
    }

    private static PlayerHurtArgs Hit(int attacker, int victim, int amount)
        => new(victim, 50, attacker, amount);

    [Fact]
    public void A_hit_by_me_shows_how_much_it_did()
    {
        var (numbers, _) = Make();

        numbers.Add(Hit(Me, Them, 23));

        Assert.Equal(1, numbers.NumberCount);
        Assert.Equal(23, numbers.Number(0).Amount);
        Assert.Equal(0f, numbers.Number(0).Age);
    }

    [Theory]
    [InlineData(Other, Them, 23)]   // somebody else's hit
    [InlineData(0, Me, 23)]         // the world's
    [InlineData(Me, Me, 23)]        // my own blast on me
    [InlineData(Me, Them, 0)]       // a hit that did nothing
    public void Only_my_hits_on_somebody_else_that_did_something_show(int attacker, int victim, int amount)
    {
        var (numbers, _) = Make();

        numbers.Add(Hit(attacker, victim, amount));

        Assert.Equal(0, numbers.NumberCount);
    }

    [Fact]
    public void A_shotguns_pellets_are_one_number()
    {
        var (numbers, _) = Make();

        for (int pellet = 0; pellet < 7; pellet++)
            numbers.Add(Hit(Me, Them, 8));

        Assert.Equal(1, numbers.NumberCount);
        Assert.Equal(56, numbers.Number(0).Amount);
    }

    [Fact]
    public void A_later_hit_or_another_victim_is_its_own_number()
    {
        var (numbers, clock) = Make();
        numbers.Add(Hit(Me, Them, 10));

        numbers.Add(Hit(Me, Other, 20));
        clock.Now += DamageDealtNumbers.MergeSeconds + 0.01f;
        numbers.Add(Hit(Me, Other, 30));

        Assert.Equal(3, numbers.NumberCount);
        Assert.Equal(30, numbers.Number(2).Amount);
    }

    [Fact]
    public void A_number_ages_and_goes()
    {
        var (numbers, clock) = Make();
        numbers.Add(Hit(Me, Them, 10));

        clock.Now += DamageDealtNumbers.LifeSeconds / 2f;
        Assert.Equal(1, numbers.NumberCount);
        Assert.Equal(0.5f, numbers.Number(0).Age, 3);

        clock.Now += DamageDealtNumbers.LifeSeconds / 2f;
        Assert.Equal(0, numbers.NumberCount);
    }

    [Fact]
    public void The_oldest_gives_way_when_full()
    {
        var (numbers, clock) = Make();

        for (int i = 1; i <= DamageDealtNumbers.MaxNumbers + 1; i++)
        {
            numbers.Add(Hit(Me, Them, i));
            clock.Now += DamageDealtNumbers.MergeSeconds + 0.01f;
        }

        Assert.Equal(DamageDealtNumbers.MaxNumbers, numbers.NumberCount);
        Assert.Equal(2, numbers.Number(0).Amount);
    }

    [Fact]
    public void A_level_change_taking_the_clock_back_clears_them()
    {
        var (numbers, clock) = Make();
        numbers.Add(Hit(Me, Them, 10));

        clock.Now = 1f;

        Assert.Equal(0, numbers.NumberCount);
    }
}
