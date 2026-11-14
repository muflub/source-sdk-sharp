using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Ai.Hl2mp.Senses;

namespace SourceSharp.Ai.Hl2mp.Teams;

/// <summary><c>ai_bot_orders</c> (plan_bots_teams_tf2 Q5): 1, bots follow a human teammate's "follow me", "help", "go" and "hold" in team chat; 0, they do not.</summary>
[ConVar("ai_bot_orders", "1", Help = "1: bots follow a teammate's follow me / help / go / hold said in team chat; 0: they do not")]
public sealed class AiBotOrdersConVar : BoolConVar { }

/// <summary>
/// A person's orders in team chat (B.5, Q5), as a player on the team reads them: a line a
/// bot hears (<see cref="HostSay.CanHear"/>, team only) that is an order goes to that bot
/// as an <see cref="AiEventKind.Order"/>. A bot's own lines are no orders.
/// </summary>
[SideService(Side.Server)]
public sealed class TeamOrderTap(Hl2mpSenses senses, Hl2mpBots bots, HostSay say, IBotPlayers players, IEngineEntities entities, IEngineFrame frame,
                                 IRenderedTeams teams, AiBotOrdersConVar enabled) : IPlayerChatHook, IOrderedHook
{
    /// <summary>
    /// After the bots' chat (<c>Hl2mpBotChat</c>, the default 0): both only listen, so
    /// the order between them changes nothing, but a stage value is unique per hook.
    /// </summary>
    public int HookOrder => 1;

    /// <summary>Orders handed to bots, for the facts and the counters.</summary>
    public int Handed { get; private set; }

    public void OnPlayerChat(PlayerChat chat)
    {
        if (!enabled.Value || !chat.TeamOnly || !teams.Teamplay || players.IsBot(chat.Speaker))
            return;
        OrderKind order = TeamOrders.Parse(chat.Text);
        if (order == OrderKind.None)
            return;
        int slot = entities.GetIndex(chat.Speaker);
        ActorId from = ActorDirectory.HumanActor(slot, chat.Speaker);
        var tick = new Tick(frame.TickCount());
        foreach (Hl2mpBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            if (p == EntityHandle.None || !say.CanHear(p, chat.Speaker, teamOnly: true))
                continue;
            senses.Push(AiEvent.Order(b.Id, tick, from, order));
            Handed++;
        }
    }
}
