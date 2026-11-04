using TF2.Server;

using Xunit;

namespace TF2.Tests;

/// <summary>What TF2's players can vote on.</summary>
public sealed class TFVoteSettingsTests
{
    [Fact]
    public void Rounds_and_wins_rather_than_frags()
        => Assert.Equal(["mp_friendlyfire", "mp_timelimit", "mp_maxrounds", "mp_winlimit"], new TFVoteSettings().VoteSettings.Select(s => s.ConVar));
}
