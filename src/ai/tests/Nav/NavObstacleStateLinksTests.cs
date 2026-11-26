using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>
/// An agent's obstacle state carries the links it may take and the health it may spend on one
/// (lane G5c-2b): every copy carries them, and a shoved copy (a follower's, with no other way out)
/// follows its original's, which the bot's lane refreshes each step.
/// </summary>
public class NavObstacleStateLinksTests
{
    private static NavLinks Links()
    {
        var (g, ids) = Grids.Parse("..... .....");
        return NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
    }

    [Fact]
    public void ACloneAndACopyCarryTheLinksAndTheHealth()
    {
        NavLinks links = Links();
        var state = new NavObstacleState(0) { Links = links, LinkHealth = 70f };
        NavObstacleState clone = state.Clone();
        var copy = new NavObstacleState(0);
        copy.CopyFrom(state);
        Assert.Same(links, clone.Links);
        Assert.Same(links, copy.Links);
        Assert.Equal((70f, 70f), (clone.LinkHealth, copy.LinkHealth));
    }

    [Fact]
    public void AShovedCopyFollowsItsOriginalsLinksAndHealth()
    {
        var state = new NavObstacleState(0) { LinkHealth = 70f };
        NavObstacleState shoved = state.Shoved();
        NavLinks links = Links();
        state.LinkHealth = 12f;
        state.Links = links;
        Assert.Equal(12f, shoved.LinkHealth);
        Assert.Same(links, shoved.Links);
    }

    /// <summary>
    /// Round 2's S13: what the agent spends on each link rides every copy too (the bot's lane takes its own copy each step,
    /// BotDriveLanes' CopyFrom), and a shoved copy follows its original's.
    /// </summary>
    [Fact]
    public void EveryCopyCarriesTheAgentsOwnSpends()
    {
        float[] spends = [12f];
        var state = new NavObstacleState(0) { Links = Links(), LinkSpends = spends };
        var copy = new NavObstacleState(0);
        copy.CopyFrom(state);
        Assert.Same(spends, state.Clone().LinkSpends);
        Assert.Same(spends, copy.LinkSpends);
        NavObstacleState shoved = new NavObstacleState(0).Shoved();
        Assert.Null(shoved.LinkSpends);
        var live = new NavObstacleState(0);
        NavObstacleState follows = live.Shoved();
        live.LinkSpends = spends;
        Assert.Same(spends, follows.LinkSpends);
    }
}
