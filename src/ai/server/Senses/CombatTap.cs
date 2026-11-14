using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.Server.Senses;

/// <summary>One scoreboard row as every client's HUD has it (the player resource's kills and deaths).</summary>
public readonly record struct ScoreRow(ActorId Actor, int Frags, int Deaths);

/// <summary>When a bot last gave or took a hit, as its own HUD showed it (§3.6's "in a fight"); -1 for never.</summary>
public interface IBotFightClock
{
    int LastHitTick(int slot);
}

/// <summary>
/// The damage, death and scoreboard taps (plan_hl2mp_ai §4.3, Phase 4 step
/// 4), each what the bot's own HUD shows: the hits it takes (with the damage
/// indicator's direction), the hits it deals (its hit marker), the kill
/// feed's every death, its own death and respawn, and the scoreboard's rows
/// with their ranks, pushed when they change (Headhunter reads the leader,
/// §5.4).
/// </summary>
[SideService(Side.Server, As = [typeof(IPlayerHurtHook), typeof(IPlayerDeathHook), typeof(IBotFightClock)])]
public sealed class CombatTap(AiSenses senses, VisionPass vision, AiBots bots, IEnginePlayers players, IEngineEntities entities,
                              IPlayerDirectory directory, IEngineFrame frame, ShotTap shots, AimMeter aim, IRenderedTeams teams) : IPlayerHurtHook, IPlayerDeathHook, IGameLifecycle, IFrameLifecycle, IBotFightClock
{
    /// <summary>How often the scoreboard is read: twice a second.</summary>
    public const int ScoreTicks = 33;

    /// <summary>How often who is watching is read: as often as a brain decides.</summary>
    public const int SpectatorTicks = 6;

    private readonly ActorId[] _watching = new ActorId[VisionPass.Slots];

    private readonly List<AiEvent> _events = new();
    private readonly List<ScoreRow> _rows = new();
    private readonly Dictionary<ActorId, (int Frags, int Deaths, int Rank)> _shown = new();
    private readonly Dictionary<int, bool> _alive = new();
    private readonly Dictionary<int, int> _lastHit = new();
    private readonly Dictionary<(ActorId Bot, ActorId Actor), int> _toldTeam = new();
    private readonly Dictionary<(ActorId Bot, int Team), int> _toldScores = new();
    private readonly List<(ActorId Actor, int Team)> _teamRows = new();
    private readonly Dictionary<ActorId, bool> _toldFriendlyFire = new();

    public int LastHitTick(int slot) => _lastHit.GetValueOrDefault(slot, -1);

    public int LifecycleOrder => ModStages.First + 610;

    public int FrameOrder => ModStages.First + 610;

    public void OnLevelInitPostEntity(string mapName)
    {
        _shown.Clear();
        _alive.Clear();
        _lastHit.Clear();
        _toldTeam.Clear();
        _toldScores.Clear();
        _toldFriendlyFire.Clear();
        Array.Clear(_watching);
    }

    private ActorId ActorOf(EntityHandle p, out int slot)
    {
        slot = 0;
        if (p == EntityHandle.None)
            return ActorId.None;
        slot = entities.GetIndex(p);
        if (slot is < 1 or >= VisionPass.Slots || Spectators.Is(players, p))
            return ActorId.None;
        ActorId bot = BotIn(slot);
        return bot.IsValid ? bot : ActorDirectory.HumanActor(slot, p);
    }

    private ActorId BotIn(int slot)
    {
        foreach (AiBot b in bots.Bots)
        {
            if (b.Id.Slot == slot)
                return b.Id;
        }

        return ActorId.None;
    }

    public void OnPlayerHurt(in PlayerHurt hurt)
    {
        shots.OnPlayerHurt(hurt);
        if (bots.Bots.Count == 0)
            return;
        aim.Hurt(in hurt);
        ActorId victim = ActorOf(hurt.Player, out int victimSlot);
        ActorId attacker = ActorOf(hurt.Attacker, out int attackerSlot);
        _lastHit[victimSlot] = _lastHit[attackerSlot] = frame.TickCount();
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
            senses.Push(ev);
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
        if (tick.Value % SpectatorTicks == 0)
        {
            Watchers(tick);
            Teams(tick);
        }

        if (tick.Value % ScoreTicks == 0)
            Scores(tick);
        Push();
    }

    /// <summary>A bot's own death and respawn, from its HUD: alive again after dead is <c>Spawned</c>.</summary>
    private void Lives(Tick tick)
    {
        foreach (AiBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            bool alive = p != EntityHandle.None && players.IsAlive(p) && players.GetObserverMode(p) == ObserverMode.None;
            if (_alive.TryGetValue(b.Id.Slot, out bool was) && !was && alive)
                _events.Add(AiEvent.Spawned(b.Id, tick, vision.ReadOf(b.Id.Slot)?.Origin ?? AiVector.Zero));
            _alive[b.Id.Slot] = alive;
        }
    }

    /// <summary>
    /// Who has gone to watch or come back, for every bot's memory (§4.1). A bot added
    /// later is not told of one already watching: it has nothing to forget, and nothing
    /// tells it of them (the rows and the taps leave them out).
    /// </summary>
    private void Watchers(Tick tick)
    {
        foreach ((EntityHandle player, PlayerRecord _) in directory.All)
        {
            int slot = entities.GetIndex(player);
            if (slot is < 1 or >= VisionPass.Slots || BotIn(slot).IsValid)
                continue;
            ActorId actor = ActorDirectory.HumanActor(slot, player);
            if (!WatchingChanged(_watching, slot, actor, Spectators.Is(players, player), out bool watching))
                continue;
            // Back on the board as anyone new is.
            _shown.Remove(actor);
            foreach (AiBot b in bots.Bots)
                _events.Add(AiEvent.ActorSpectating(b.Id, tick, actor, watching));
        }
    }

    /// <summary>Whether <paramref name="actor"/> in <paramref name="slot"/> has just gone to watch or come back; <paramref name="watched"/> is kept by slot.</summary>
    public static bool WatchingChanged(ActorId[] watched, int slot, ActorId actor, bool now, out bool watching)
    {
        watching = now;
        if (now == (watched[slot] == actor))
            return false;
        watched[slot] = now ? actor : ActorId.None;
        return true;
    }

    /// <summary>
    /// The rows of those in the game (§4.1, §5.4): a spectator atop the board is nobody's leader.
    /// </summary>
    public static void RowsInTheGame(IEnumerable<(EntityHandle Player, PlayerRecord Record)> all, Func<EntityHandle, int> slotOf, Func<int, ActorId> botIn, IEnginePlayers players, List<ScoreRow> into)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(slotOf);
        ArgumentNullException.ThrowIfNull(botIn);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        foreach ((EntityHandle player, PlayerRecord record) in all)
        {
            int slot = slotOf(player);
            if (slot is < 1 or >= VisionPass.Slots || Spectators.Is(players, player))
                continue;
            ActorId bot = botIn(slot);
            into.Add(new ScoreRow(bot.IsValid ? bot : ActorDirectory.HumanActor(slot, player), record.Kills, record.Deaths));
        }
    }

    /// <summary>
    /// Every player's team column and the team scores as each bot's HUD has them
    /// (plan_bots_teams_tf2 B.2, B.9): a change, or a bot that has not been told, is an
    /// <c>ActorTeam</c> (the bot's own included) and a <c>MatchState</c>.
    /// </summary>
    private void Teams(Tick tick)
    {
        _teamRows.Clear();
        foreach ((EntityHandle player, PlayerRecord _) in directory.All)
        {
            int slot = entities.GetIndex(player);
            if (slot is < 1 or >= VisionPass.Slots || Spectators.Is(players, player))
                continue;
            ActorId bot = BotIn(slot);
            _teamRows.Add((bot.IsValid ? bot : ActorDirectory.HumanActor(slot, player), teams.TeamOf(player)));
        }

        ActorId[] ids = BotIds();
        TellTeams(_teamRows, ids, _toldTeam, tick, _events);
        for (int team = TeamRule.FirstGameTeam; team <= TeamRule.LastGameTeam; team++)
            TellScore(team, teams.Score(team), ids, _toldScores, tick, _events);
        bool ff = teams.FriendlyFire;
        foreach (ActorId b in ids)
        {
            if (_toldFriendlyFire.TryGetValue(b, out bool was) && was == ff)
                continue;
            _toldFriendlyFire[b] = ff;
            _events.Add(AiEvent.FriendlyFireSet(b, tick, ff));
        }
    }

    /// <summary>
    /// Each bot is told each row's team when it differs from what that bot was last told
    /// (nothing for a team 0 it was never told otherwise): the <c>player_team</c> event a
    /// client gets, and the column a client joining later reads.
    /// </summary>
    public static void TellTeams(IReadOnlyList<(ActorId Actor, int Team)> rows, ReadOnlySpan<ActorId> bots,
                                 Dictionary<(ActorId Bot, ActorId Actor), int> told, Tick tick, List<AiEvent> into)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(told);
        ArgumentNullException.ThrowIfNull(into);
        foreach (ActorId b in bots)
        {
            foreach ((ActorId actor, int team) in rows)
            {
                int was = told.GetValueOrDefault((b, actor));
                if (was == team)
                    continue;
                told[(b, actor)] = team;
                into.Add(AiEvent.ActorTeam(b, tick, actor, team, was));
            }
        }
    }

    /// <summary>Each bot is told <paramref name="team"/>'s score when it differs from what it was last told (0 before).</summary>
    public static void TellScore(int team, int score, ReadOnlySpan<ActorId> bots, Dictionary<(ActorId Bot, int Team), int> told,
                                 Tick tick, List<AiEvent> into)
    {
        ArgumentNullException.ThrowIfNull(told);
        ArgumentNullException.ThrowIfNull(into);
        foreach (ActorId b in bots)
        {
            if (told.GetValueOrDefault((b, team)) == score)
                continue;
            told[(b, team)] = score;
            into.Add(AiEvent.MatchState(b, tick, team, score));
        }
    }

    private void Scores(Tick tick)
    {
        RowsInTheGame(directory.All, entities.GetIndex, BotIn, players, _rows);
        int[] ranks = Ranks(_rows);
        for (int i = 0; i < _rows.Count; i++)
        {
            ScoreRow r = _rows[i];
            if (_shown.TryGetValue(r.Actor, out var was) && was == (r.Frags, r.Deaths, ranks[i]))
                continue;
            _shown[r.Actor] = (r.Frags, r.Deaths, ranks[i]);
            foreach (AiBot b in bots.Bots)
                _events.Add(AiEvent.ScoreboardRow(b.Id, tick, r.Actor, r.Frags, r.Deaths, ranks[i]));
        }
    }
}
