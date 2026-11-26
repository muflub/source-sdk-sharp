using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// TF2's feed for the bots is its notices' (the Spy research §3.3 hole 3): a Dead Ringer's feign fires a notice and no
/// death (TFSpy.FeignDeath: notice.Fire, then the corpse), so the tap that hears notices puts it in the feed as a kill, as
/// a player's feed shows it (research fact 9). The view says TF2 owns its feed, so the engine's deaths add no second line.
/// </summary>
public sealed class TFKillFeedTests
{
    private sealed class HeardFeed : IBotKillFeed
    {
        public readonly List<(EntityHandle Victim, EntityHandle Killer, bool Silent)> Lines = new();

        public void Feed(EntityHandle victim, EntityHandle killer, bool silent) => Lines.Add((victim, killer, silent));
    }

    private static readonly EntityHandle Spy = new(0x0001_0005u), Soldier = new(0x0001_0002u);

    /// <summary>A notice with no player scoring (a feign struck by the world, a fall) asks nothing of the game to be told.</summary>
    [Fact]
    public void AFeignsNoticeIsAFeedLineAsAKill()
    {
        var feed = new HeardFeed();
        // Only the silent rule is asked of the notice, and with no scorer it reads nothing (no record, no attribute).
        var tap = new TFKillFeedTap(feed, new global::TF2.Server.Combat.TFDeathNotice(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!));
        tap.Fill(new DeathNoticeFacts(Spy, EntityHandle.None, EntityHandle.None, EntityHandle.None, "world"), null!);
        Assert.Equal((Spy, EntityHandle.None, false), Assert.Single(feed.Lines));
        Assert.Equal(1, tap.Told);
    }

    [Fact]
    public void TheTF2ViewOwnsItsFeed() => Assert.True(new TFBotGameView(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!).OwnKillFeed);
}
