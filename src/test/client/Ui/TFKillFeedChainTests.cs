using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The composed kill-feed chain, as the live run showed it (ctf_2fort: the
/// hook chain held exactly one listener, and a rocket kill stayed six
/// seconds): the hook chain the composition builds collects TF's listener
/// through its declared contract, and a death raised on the core feed
/// reaches TF's own queue with its names, its icon and the stock display
/// time.
/// </summary>
public class TFKillFeedChainTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    // The core feed's roster: user ids 2 (the local player) and 3.
    private sealed class Players : IKillFeedPlayers
    {
        public bool TryFind(int userId, out string name, out int team, out bool local)
        {
            (name, team, local) = userId switch
            {
                2 => ("Gordon", 3, true),
                3 => ("Alyx", 2, false),
                _ => ("", 0, false),
            };
            return name.Length > 0;
        }
    }

    [Fact]
    public void The_hook_chain_the_side_services_build_carries_a_death_into_the_TF_queue()
    {
        var clock = new Clock();
        var players = new Players();
        var services = new ServiceContainer();
        SideServices.Register(services, Side.Client, typeof(TFKillFeedSource).Assembly.GetTypes());
        services.AddInstance<IGameClock>(clock);
        services.AddInstance<IKillFeedPlayers>(players);
        // hud_deathnotice_time at its stock 6, published the way the game's
        // registry does (the core feed's facts set it the same way).
        var time = new HudDeathNoticeTimeConVar();
        time.NotifyChanged("6", "");
        services.AddInstance(time);
        services.AddInstance<ILog>(new RecordingLog());
        services.AddFactory<IHooks<IKillFeedListener>>(locator => new Hooks<IKillFeedListener>(locator, locator.Require<ILog>()));

        // The chain holds exactly TF's listener, and it is the queue itself:
        // the one singleton behind both the listener hook and the ITFKillFeed
        // contract, as ClientModule composes it.
        var hooks = services.Require<IHooks<IKillFeedListener>>();
        Assert.Single(hooks.All);
        Assert.Same(hooks.All[0], services.Require<ITFKillFeed>());

        // One rocket kill by the local player, through the core feed as the
        // game's event link delivers it (the death's order: victim, attacker).
        var kills = new KillFeed(clock, new RecordingLog(), players,
                                 services.Require<HudDeathNoticeTimeConVar>(), hooks);
        kills.Add(new PlayerDeathArgs(3, 2, "smg1", false));

        var queue = services.Require<ITFKillFeed>();
        Assert.Equal(1, queue.LineCount);
        var l = queue.Line(0);
        Assert.Equal(("Gordon", 3, "Alyx", 2, "d_smg1", true),
                     (l.Killer, l.KillerTeam, l.Victim, l.VictimTeam, l.Icon, l.LocalInvolved));
    }
}
