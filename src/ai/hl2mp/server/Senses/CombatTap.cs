using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>One scoreboard row as every client's HUD has it (the player resource's kills and deaths).</summary>
public readonly record struct ScoreRow(ActorId Actor, int Frags, int Deaths);

/// <summary>
/// The damage, death and scoreboard taps (plan_hl2mp_ai §4.3, Phase 4 step
/// 4), each what the bot's own HUD shows: the hits it takes (with the damage
/// indicator's direction), the hits it deals (its hit marker), the kill
/// feed's every death, its own death and respawn, and the scoreboard's rows
/// with their ranks, pushed when they change (Headhunter reads the leader,
/// §5.4).
/// </summary>
[SideService(Side.Server, As = [typeof(IPlayerHurtHook), typeof(IPlayerDeathHook)])]
public sealed class CombatTap(Hl2mpSenses senses, VisionPass vision, Hl2mpBots bots, IEnginePlayers players, IEngineEntities entities,
                              IPlayerDirectory directory, IEngineFrame frame) : IPlayerHurtHook, IPlayerDeathHook, IGameLifecycle, IFrameLifecycle
{
    /// <summary>How often the scoreboard is read: twice a second.</summary>
    public const int ScoreTicks = 33;

    private readonly List<AiEvent> _events = new();
    private readonly List<ScoreRow> _rows = new();
    private readonly Dictionary<ActorId, (int Frags, int Deaths, int Rank)> _shown = new();
    private readonly Dictionary<int, bool> _alive = new();

    public int LifecycleOrder => ModStages.First + 610;

    public int FrameOrder => ModStages.First + 610;

    public void OnLevelInitPostEntity(string mapName)
    {
        _shown.Clear();
        _alive.Clear();
    }

    private ActorId ActorOf(EntityHandle p, out int slot)
    {
        slot = 0;
        if (p == EntityHandle.None)
            return ActorId.None;
        slot = entities.GetIndex(p);
        if (slot is < 1 or >= VisionPass.Slots)
            return ActorId.None;
        ActorId bot = BotIn(slot);
        return bot.IsValid ? bot : ActorDirectory.HumanActor(slot, p);
    }

    private ActorId BotIn(int slot)
    {
        foreach (Hl2mpBot b in bots.Bots)
        {
            if (b.Id.Slot == slot)
                return b.Id;
        }

        return ActorId.None;
    }

    public void OnPlayerHurt(in PlayerHurt hurt)
    {
        if (bots.Bots.Count == 0)
            return;
        ActorId victim = ActorOf(hurt.Player, out int victimSlot);
        ActorId attacker = ActorOf(hurt.Attacker, out int attackerSlot);
        _events.Clear();
        Hurt(victim, attacker, hurt.Taken, hurt.HealthLeft, vision.DirectionFrom(victimSlot, attackerSlot), BotIds(), Tick, _events);
        Push();
    }

    public void OnPlayerDied(PlayerDeath death)
    {
        if (bots.Bots.Count == 0)
            return;
        ActorId victim = ActorOf(death.Player, out _);
        ActorId killer = death.Attacker != death.Player ? ActorOf(death.Attacker, out _) : ActorId.None;
        _events.Clear();
        Died(victim, killer, BotIds(), Tick, _events);
        Push();
    }

    private Tick Tick => new(frame.TickCount());

    private ActorId[] BotIds() => [.. bots.Bots.Select(b => b.Id)];

    private void Push()
    {
        foreach (AiEvent ev in _events)
            senses.Session.Push(ev);
    }

    /// <summary>
    /// A hit: the victim, if a bot, takes <c>Damaged</c> (from the attacker,
    /// with the indicator's direction); the attacker, if a bot, its hit
    /// marker. Nobody else is told.
    /// </summary>
    public static void Hurt(ActorId victim, ActorId attacker, int amount, int healthLeft, AiVector dirFromVictim, ReadOnlySpan<ActorId> bots,
                            Tick tick, List<AiEvent> into)
    {
        foreach (ActorId b in bots)
        {
            if (b == victim)
                into.Add(AiEvent.Damaged(b, tick, attacker, amount, dirFromVictim, 0));
            else if (b == attacker && victim.IsValid)
                into.Add(AiEvent.DealtDamage(b, tick, victim, amount, healthLeft));
        }
    }

    /// <summary>A death: every bot's kill feed shows it; the victim, if a bot, is <c>Killed</c>.</summary>
    public static void Died(ActorId victim, ActorId killer, ReadOnlySpan<ActorId> bots, Tick tick, List<AiEvent> into)
    {
        if (!victim.IsValid)
            return;
        foreach (ActorId b in bots)
        {
            into.Add(AiEvent.KillFeed(b, tick, victim, killer));
            if (b == victim)
                into.Add(AiEvent.Killed(b, tick, killer));
        }
    }

    /// <summary>
    /// The ranks of <paramref name="rows"/>: by frags, most first, then by
    /// deaths, fewest first, then by slot; rank 1 is the leader (§5.4).
    /// </summary>
    public static int[] Ranks(IReadOnlyList<ScoreRow> rows)
    {
        int[] order = [.. Enumerable.Range(0, rows.Count).OrderByDescending(i => rows[i].Frags).ThenBy(i => rows[i].Deaths)
                                                         .ThenBy(i => rows[i].Actor.Slot)];
        int[] rank = new int[rows.Count];
        for (int r = 0; r < order.Length; r++)
            rank[order[r]] = r + 1;
        return rank;
    }

    public void OnPostThink(float frameTime)
    {
        Tick tick = Tick;
        _events.Clear();
        Lives(tick);
        if (tick.Value % ScoreTicks == 0)
            Scores(tick);
        Push();
    }

    /// <summary>A bot's own death and respawn, from its HUD: alive again after dead is <c>Spawned</c>.</summary>
    private void Lives(Tick tick)
    {
        foreach (Hl2mpBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            bool alive = p != EntityHandle.None && players.IsAlive(p) && players.GetObserverMode(p) == ObserverMode.None;
            if (_alive.TryGetValue(b.Id.Slot, out bool was) && !was && alive)
                _events.Add(AiEvent.Spawned(b.Id, tick, vision.ReadOf(b.Id.Slot)?.Origin ?? AiVector.Zero));
            _alive[b.Id.Slot] = alive;
        }
    }

    private void Scores(Tick tick)
    {
        _rows.Clear();
        foreach ((EntityHandle player, PlayerRecord record) in directory.All)
        {
            int slot = entities.GetIndex(player);
            if (slot is < 1 or >= VisionPass.Slots)
                continue;
            ActorId bot = BotIn(slot);
            _rows.Add(new ScoreRow(bot.IsValid ? bot : ActorDirectory.HumanActor(slot, player), record.Kills, record.Deaths));
        }

        int[] ranks = Ranks(_rows);
        for (int i = 0; i < _rows.Count; i++)
        {
            ScoreRow r = _rows[i];
            if (_shown.TryGetValue(r.Actor, out var was) && was == (r.Frags, r.Deaths, ranks[i]))
                continue;
            _shown[r.Actor] = (r.Frags, r.Deaths, ranks[i]);
            foreach (Hl2mpBot b in bots.Bots)
                _events.Add(AiEvent.ScoreboardRow(b.Id, tick, r.Actor, r.Frags, r.Deaths, ranks[i]));
        }
    }
}
