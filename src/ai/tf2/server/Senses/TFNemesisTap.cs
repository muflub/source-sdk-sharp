using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// Dominations as a bot's HUD shows them (plan_bots_teams_tf2_part_g G.2.5): twice a second each
/// bot's own nemesis rows are read through the view (<see cref="TFBotGameView.DominatedBy"/>, the
/// row a client reads for its scoreboard's mark), and a row that changed is told to that bot
/// alone as a <see cref="AiEventKind.Nemesis"/>: the mod sets the row at the fourth unanswered
/// kill and clears it at the revenge kill, so a bot's memory holds the mark exactly as long as
/// its HUD does. Who dominates anyone else, a bot is not told.
/// </summary>
[SideService(Side.Server)]
public sealed class TFNemesisTap(AiSenses senses, AiBots bots, TFBotGameView view, IEngineFrame frame) : IFrameLifecycle, IGameLifecycle
{
    /// <summary>How often the rows are read: twice a second, as the scoreboard is.</summary>
    public const int ReadTicks = 33;

    /// <summary>After the senses (First + 600), the combat tap (First + 610) and the experience feed (First + 660), at an order of its own.</summary>
    public int FrameOrder => ModStages.First + 665;

    public int LifecycleOrder => ModStages.First + 665;

    private readonly Dictionary<(ActorId Bot, ActorId Actor), bool> _told = new();
    private readonly List<AiEvent> _events = new();

    /// <summary>Marks told to a bot, in all.</summary>
    public long Told { get; private set; }

    public void OnLevelInitPostEntity(string mapName) => _told.Clear();

    public void OnPostThink(float frameTime)
    {
        int tick = frame.TickCount();
        if (bots.Bots.Count == 0 || tick % ReadTicks != 0)
            return;
        _events.Clear();
        foreach (AiBot b in bots.Bots)
        {
            EntityHandle player = bots.Applier.PlayerOf(b.Id);
            if (player == EntityHandle.None)
                continue;
            for (int slot = 1; slot < VisionPass.Slots; slot++)
            {
                ActorId actor = slot == b.Id.Slot ? ActorId.None : senses.ActorAt(slot);
                if (actor.IsValid)
                    Tell(b.Id, actor, view.DominatedBy(player, slot), _told, new Tick(tick), _events);
            }
        }

        foreach (AiEvent e in _events)
            senses.Push(e);
        Told += _events.Count;
    }

    /// <summary>
    /// Tells <paramref name="bot"/> that its HUD marks <paramref name="actor"/> (or no longer does),
    /// when that differs from what it was last told; someone never marked is not news.
    /// </summary>
    public static void Tell(ActorId bot, ActorId actor, bool marked, Dictionary<(ActorId Bot, ActorId Actor), bool> told, Tick tick, List<AiEvent> into)
    {
        ArgumentNullException.ThrowIfNull(told);
        ArgumentNullException.ThrowIfNull(into);
        if (told.GetValueOrDefault((bot, actor)) == marked)
            return;
        told[(bot, actor)] = marked;
        into.Add(AiEvent.Nemesis(bot, tick, actor, marked));
    }
}
