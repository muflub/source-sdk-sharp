using SourceSharp.Tests;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// Stock's sentry has a second pass over the NextBot list (<c>TheNextBots().CollectAllBots</c>,
/// tf_obj_sentrygun.cpp:976-996) for bots that are not players: Halloween's skeletons, MvM's tanks.
/// This build's bots are players (plan_bots_teams_tf2 P6 step 9), so the players pass reaches
/// them, and nothing the sentry finds asks whether a player is a bot: a bot is shot as a person is.
/// </summary>
public sealed class TFSentryBotsFacts
{
    private static System.Reflection.Assembly Server => typeof(TF2.Server.Buildings.TFSentryGun).Assembly;

    [Fact]
    public void TheSentryAsksNoPlayerWhetherItIsABot()
    {
        // The control: the census sees the game's own IsBot readers (the jointeam override's arm).
        Assert.Contains(typeof(TF2.Server.Rules.TFJoinTeamCommand).FullName!, IlCallCensus.Callers(m => m.Name == "IsBot", Server));
        Assert.DoesNotContain(IlCallCensus.Callers(m => m.Name == "IsBot", Server), c => c.StartsWith("TF2.Server.Buildings.TFSentryGun", StringComparison.Ordinal));
    }
}
