using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using TF2.Server.Combat;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// TF2's kill feed for the bots (the Spy research §3.3 hole 3, its review's finding 2): every line a TF2 client's feed
/// shows is a <c>player_death</c> notice, so the bots' feed is built from the notices, as they fire, not from the engine's
/// deaths. A Dead Ringer's feign fires a notice and no death (TFSpy's FeignDeath), so every bot reads a kill that did not
/// happen, as a player does; a silent kill (Your Eternal Reward's backstab, <c>silent_kill</c>) shows in no feed of the
/// victim's team (stock's <c>tf_hud_deathnotice.cpp:695-702</c>). The view says the game owns its feed
/// (<see cref="TFBotGameView.OwnKillFeed"/>), so the engine's deaths tell the victim only that it died.
/// </summary>
[SideService(Side.Server)]
public sealed class TFKillFeedTap(IBotKillFeed feed, TFDeathNotice notice) : IDeathNoticeHook, IOrderedHook
{
    /// <summary>After the game's own keys (TFDeathNotice's band, below the mods' first stage), in the AI's range beside CombatTap's.</summary>
    public int HookOrder => ModStages.First + 906;

    /// <summary>Notices told to the bots: <c>ai.tf.feed</c>, for the facts.</summary>
    public int Told { get; private set; }

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        feed.Feed(death.Victim, death.Scorer, notice.SilentKill(in death));
        Told++;
    }
}
