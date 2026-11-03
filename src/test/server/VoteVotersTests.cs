using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>Who a ballot counts: the base its quorum is taken of.</summary>
public class VoteVotersTests
{
    private const int Unassigned = 0, Spectator = 1, Red = 2, Blue = 3;

    private static int Count(params (int Team, bool Bot)[] players)
        => VoteController.CountVoters(players, allowSpectators: false, botsCount: false, onlyTeam: Unassigned);

    /// <summary>Every deathmatch player is unassigned: counting none passed any vote on the caller's yes alone.</summary>
    [Fact]
    public void In_a_deathmatch_every_player_counts()
        => Assert.Equal(3, Count((Unassigned, false), (Unassigned, false), (Unassigned, false)));

    [Fact]
    public void In_a_team_game_one_yet_to_join_a_team_does_not()
        => Assert.Equal(2, Count((Red, false), (Blue, false), (Unassigned, false)));

    [Fact]
    public void A_bot_cannot_answer_so_it_does_not_count()
        => Assert.Equal(1, Count((Unassigned, false), (Unassigned, true), (Unassigned, true)));

    [Fact]
    public void A_spectator_counts_only_if_the_server_allows()
    {
        Assert.Equal(1, Count((Red, false), (Spectator, false)));
        Assert.Equal(2, VoteController.CountVoters([(Red, false), (Spectator, false)], allowSpectators: true, botsCount: false, onlyTeam: Unassigned));
    }

    [Fact]
    public void A_team_vote_counts_that_team_alone()
        => Assert.Equal(1, VoteController.CountVoters([(Red, false), (Blue, false)], false, false, onlyTeam: Blue));
}
