namespace SourceSharp.Ai;

/// <summary>
/// What a bot's memory works over for one map (plan_hl2mp_ai §5.0): the map's
/// knowledge, its graph (null with no navigation), the tick length, the
/// identity an opponent's habits are kept under (§5.2), and the map's static
/// sight between cells for planning cover (null with none).
/// </summary>
public sealed record MemoryWorld(MapKnowledge Map, NavGraph? Graph, float TickSeconds, Func<ActorId, string> IdentityOf, ICellSight? Sight = null, ChokeSlot? Chokes = null)
{
    public static MemoryWorld Empty(float tickSeconds = 0.015f)
        => new(MapKnowledge.Empty(new WeaponKnowledge([])), null, tickSeconds, id => id.ToString());
}

/// <summary>
/// A bot's memory at work (plan_hl2mp_ai §5.1): applies each event the bot
/// perceived and each tick's view to its <see cref="Blackboard"/> and to the
/// habits it keeps of each opponent, and lets time pass (confidence and
/// habits decay, ammo estimates widen). Every input is a perception: nothing
/// here can read the truth. Deterministic: integer and float updates in a
/// fixed order.
/// </summary>
public sealed class BotMemory
{
    /// <summary>How long after the bot hurt an opponent a loss of sight counts as a retreat when hurt.</summary>
    public const float HurtRetreatSeconds = 2f;

    /// <summary>How long standing in one cell is camping (§5.1: 3 s).</summary>
    public const float CampSeconds = 3f;

    /// <summary>How long into a contact its direction (closing or not) is judged.</summary>
    public const float ContactSeconds = 1f;

    private readonly MemoryParams _params;
    private readonly HabitStore _habits;
    private readonly Track[] _track;
    private Tick _lastAdvance;
    private bool _chokesKnown;

    public BotMemory(ActorId bot, int slots, MemoryParams p, MemoryWorld world, HabitStore habits)
    {
        Bot = bot;
        _params = p;
        World = world;
        _habits = habits;
        Board = new Blackboard(slots);
        Board.KnowItems(world.Map);
        _track = new Track[slots];
        if (world.Graph is { } graph)
        {
            var scratch = new PredictorScratch(graph.CellCount);
            for (int i = 0; i < slots; i++)
                Board.Actor(i).Where = new PositionPredictor(graph, scratch);
        }
    }

    /// <summary>The speed an unseen opponent is predicted to move at: HL2MP's run (190 u/s, hl2mp_player.cpp).</summary>
    public const float OpponentSpeed = 190f;

    public ActorId Bot { get; }

    /// <summary>The last time the bot killed someone it held a grudge against (§5.5).</summary>
    public (ActorId Who, Tick When) LastRevenge { get; private set; }

    public Blackboard Board { get; }

    public MemoryWorld World { get; }

    public MemoryParams Params => _params;

    /// <summary>Where the bot itself was at its last view.</summary>
    public AiVector Self { get; private set; }

    public bool Alive { get; private set; }

    /// <summary>The bot's own team, as its HUD shows it (plan_bots_teams_tf2 B.2); 0 when the mode plays none.</summary>
    public int Team { get; private set; }

    /// <summary>Whether <paramref name="a"/> is on the bot's team now (<see cref="TeamRule.Friends"/>).</summary>
    public bool IsFriend(KnownActor a) => TeamRule.Friends(Team, a.Team);

    /// <summary>
    /// The last teammate whose hit or kill the bot took (plan_bots_teams_tf2 B.3): a
    /// <c>friendly_fire</c> moment, never a grudge. Who the attacker was is judged by
    /// the team on its row when the hit is applied, so a swapper's earlier hits stay a foe's.
    /// </summary>
    public (ActorId Who, Tick When) LastFriendlyFire { get; private set; }

    /// <summary>Whether a teammate can be hurt (<c>mp_friendlyfire</c>, as the bot was told): the splash gate's (B.5).</summary>
    public bool FriendlyFireOn { get; private set; }

    /// <summary>A teammate's callout as the bot heard it (plan_bots_teams_tf2 B.4).</summary>
    public readonly record struct HeardCallout(CalloutKind Kind, AiVector Place, int Count, Tick Tick, ActorId Speaker);

    /// <summary>The last enemy callout heard from a teammate; Kind None before one.</summary>
    public HeardCallout LastEnemyCall { get; private set; }

    /// <summary>The last rally call heard (or made: the caller hears its own words); Kind None before one.</summary>
    public HeardCallout LastRally { get; private set; }

    /// <summary>The last push call heard.</summary>
    public HeardCallout LastPush { get; private set; }

    /// <summary>The last "going around" heard.</summary>
    public HeardCallout LastGoingAround { get; private set; }

    /// <summary>A teammate the bot saw die to an enemy it also saw (B.5's Trade): the killer and when; none before one.</summary>
    public (ActorId Killer, Tick When) TradeChance { get; private set; }

    /// <summary>When the bot's team last killed an enemy, the bot's own kills included (B.5's no-progress read); tick 0 for never.</summary>
    public Tick LastTeamKill { get; private set; }

    /// <summary>Callouts taken, and those dropped (a speaker no longer a friend, or about someone who now is).</summary>
    public int CalloutsHeard { get; private set; }

    public int CalloutsDropped { get; private set; }

    /// <summary>How many hits and kills by a teammate the bot has taken.</summary>
    public int FriendlyFireTaken { get; private set; }

    // An enemy's memory is built; a teammate's never is (B.3): no ammo estimate, habits or grudge.
    private bool Foe(KnownActor a) => !IsFriend(a);

    private void FriendlyFire(ActorId who, Tick tick)
    {
        LastFriendlyFire = (who, tick);
        FriendlyFireTaken++;
    }

    /// <summary>The weapons the bot holds (its HUD), a mask of catalog kinds; 0 before the first <see cref="AiEventKind.Holdings"/>.</summary>
    public int HeldKinds { get; private set; }

    /// <summary>The level's choke points once the bot was told they are worked out (<see cref="AiEventKind.ChokesKnown"/>), else null.</summary>
    public ChokeMap? Chokes => _chokesKnown ? World.Chokes?.Map : null;

    /// <summary>Whether a streak can start (§5.5): off until told, so a recording made before them replays as it was.</summary>
    public bool StreaksOn { get; private set; }

    /// <summary>A streak a console asked for: how many were, and the last's <see cref="AiEvent.StreakHot"/> or <see cref="AiEvent.StreakCold"/>.</summary>
    public (int Seq, int Code) StreakAsked { get; private set; }

    /// <summary>When the bot's last two kills were; tick 0 for none.</summary>
    public (Tick Last, Tick Before) OwnKills { get; private set; }

    /// <summary>When the bot's last two deaths were; tick 0 for none.</summary>
    public (Tick Last, Tick Before) OwnDeaths { get; private set; }

    /// <summary>The last answer to a look along a line (§5.4's prefire): the ask's number, the point, and whether it was clear.</summary>
    public (int Ask, AiVector Point, bool Clear) LastLine { get; private set; }

    /// <summary>The habits of <paramref name="opponent"/> on this map.</summary>
    public OpponentHabits HabitsOf(ActorId opponent)
    {
        if (Cached(opponent) is { } h)
            return h;
        h = _habits.For(World.Map.Map, World.IdentityOf(opponent), World.Graph?.CellCount ?? 0);
        Cache(opponent, h);
        return h;
    }

    public bool TryHabits(ActorId opponent, out OpponentHabits? habits)
    {
        habits = Cached(opponent);
        if (habits is not null)
            return true;
        if (!_habits.TryGet(World.Map.Map, World.IdentityOf(opponent), out habits) || habits is null)
            return false;
        Cache(opponent, habits);
        return true;
    }

    // An opponent's habits by slot, so a steady step asks no identity (a string) of the world.
    private (ActorId Id, OpponentHabits? Habits)[]? _habitCache;

    private OpponentHabits? Cached(ActorId id)
        => _habitCache is { } c && (uint)id.Slot < (uint)c.Length && c[id.Slot].Id == id ? c[id.Slot].Habits : null;

    private void Cache(ActorId id, OpponentHabits h)
    {
        _habitCache ??= new (ActorId, OpponentHabits?)[Board.Slots];
        if ((uint)id.Slot < (uint)_habitCache.Length)
            _habitCache[id.Slot] = (id, h);
    }

    private float Dt => World.TickSeconds;

    private CellId CellAt(AiVector p) => World.Graph?.FindCell(p + new AiVector(0f, 0f, 1f)) ?? CellId.None;

    private bool Facts(int weapon, out WeaponFacts f) => World.Map.Weapons.TryGet(weapon, out f);

    /// <summary>One decide step's inputs: the events since the last, then the view, then time.</summary>
    public void Step(Tick tick, PerceivedView view, ReadOnlySpan<AiEvent> events)
    {
        ArgumentNullException.ThrowIfNull(view);
        // The teams the view renders hold for this step's events too: a newly seen
        // teammate's sight is a teammate's from its first event.
        if (view.Self.Team != Team)
            Swapped(view.Self.Team, tick);
        foreach (ref readonly ActorFacts f in view.Sighted)
        {
            if (Board.Of(f.Id, make: true) is { } seen && seen.Team != f.Team)
                Reclassified(seen, f.Team, tick);
        }

        foreach (AiEvent e in events)
            Apply(e);
        Observe(view);
        Advance(tick);
    }

    public void Apply(in AiEvent e)
    {
        // The bot is not one of the actors it remembers (its own scoreboard
        // row and kill-feed line are its HUD's, not an opponent's).
        if (e.Kind == AiEventKind.ActorTeam && e.Other == Bot)
        {
            if (e.Code != Team)
                Swapped(e.Code, e.Tick);
            return;
        }

        if (e.Other == Bot && e.Kind is not (AiEventKind.Killed or AiEventKind.Damaged))
            return;

        if (e.Kind == AiEventKind.ActorSpectating)
        {
            Spectating(e.Other, e.Code != 0);
            return;
        }

        // Nothing is learnt of someone watching, whatever comes (§4.1): the taps tell nothing, and this holds if one does.
        if (Board.Of(e.Other, make: false) is { Spectating: true })
            return;

        switch (e.Kind)
        {
            case AiEventKind.Holdings:
                HeldKinds = e.Code;
                break;
            case AiEventKind.ChokesKnown:
                _chokesKnown = true;
                break;
            case AiEventKind.StreaksSet:
                if (e.Code is AiEvent.StreaksOff or AiEvent.StreaksOn)
                    StreaksOn = e.Code == AiEvent.StreaksOn;
                else
                    StreakAsked = (StreakAsked.Seq + 1, e.Code);
                break;
            case AiEventKind.LineAnswer:
                LastLine = (e.Code, e.Position, e.Amount > 0f);
                break;
            case AiEventKind.SightGained:
                Gained(e);
                break;
            case AiEventKind.SightLost:
                Lost(e);
                break;
            case AiEventKind.SoundHeard:
                Heard(e);
                break;
            case AiEventKind.SeenFire:
                if (Board.Of(e.Other, make: true) is { } shooter)
                    Fired(shooter, e.Code2, Math.Max(1, e.Code), e.Tick);
                break;
            case AiEventKind.SeenReload:
                if (Board.Of(e.Other, make: true) is { } reloader)
                    Reload(reloader, e.Code2 >= 0 ? e.Code2 : reloader.WeaponSeen, (ReloadPhase)e.Code, (int)e.Amount, e.Tick);
                break;
            case AiEventKind.Damaged:
                Board.LastDamageDir = e.Position;
                Board.LastDamageTick = e.Tick;
                if (Board.Of(e.Other, make: true) is { } attacker)
                {
                    if (!Foe(attacker))
                    {
                        FriendlyFire(attacker.Id, e.Tick);
                        break;
                    }

                    attacker.DamageTakenFrom += e.Amount;
                    if (attacker.EverSeen)
                        HabitsOf(attacker.Id).Engaged(Self.DistanceTo(attacker.LastSeenPos));
                }

                break;
            case AiEventKind.DealtDamage:
                if (Board.Of(e.Other, make: true) is { } victim)
                {
                    victim.HealthEstimate = e.Code;
                    if (!Foe(victim))
                        break;
                    victim.TimesEngaged++;
                    _track[victim.Id.Slot].HurtTick = e.Tick;
                    _track[victim.Id.Slot].Hurt = true;
                    if (victim.EverSeen)
                        HabitsOf(victim.Id).Engaged(Self.DistanceTo(victim.LastSeenPos));
                }

                break;
            case AiEventKind.KillFeed:
                if (e.Killer == Bot)
                    OwnKills = (e.Tick, OwnKills.Last);
                if (Board.Of(e.Other, make: true) is { } dead)
                {
                    TeamKillFeed(dead, e.Killer, e.Tick);
                    // It comes back at full health with a clip nobody knows (§5.2).
                    // Seen where it fell, a moment ago: one more time found there (§5.5).
                    if (Foe(dead) && dead.EverSeen && (e.Tick - dead.LastSeenTick) * World.TickSeconds < CampSeconds && World.Graph is { } graph)
                    {
                        CellId fell = graph.FindCell(dead.LastSeenPos + new AiVector(0f, 0f, 1f));
                        if (fell.IsValid)
                            HabitsOf(dead.Id).Camped(fell);
                    }

                    dead.Dead = true;
                    dead.InSight = false;
                    dead.HealthEstimate = 100;
                    dead.Ammo.Reset();
                    dead.Where?.Clear();
                    dead.Confidence = 0f;
                    if (new ActorId(e.Code, e.Code2) == Bot && Foe(dead) && dead.GrudgeAt(e.Tick, World.TickSeconds) > 0f)
                    {
                        dead.Avenged(e.Tick, World.TickSeconds);
                        LastRevenge = (dead.Id, e.Tick);
                    }
                }

                break;
            case AiEventKind.Killed:
                OwnDeaths = (e.Tick, OwnDeaths.Last);
                if (e.Other != Bot && Board.Of(e.Other, make: true) is { } killer)
                {
                    if (Foe(killer))
                        killer.AddGrudge(e.Tick, World.TickSeconds, 1f);
                    else
                        FriendlyFire(killer.Id, e.Tick);
                }

                Board.ForgetLife();
                break;
            case AiEventKind.ScoreboardRow:
                if (Board.Of(e.Other, make: true) is { } row)
                {
                    row.Frags = e.Code;
                    row.Deaths = e.Code2;
                    row.Rank = (int)e.Amount;
                }

                break;
            case AiEventKind.ItemSeen:
                Board.SeeItem(e.Code, e.Code2 != 0, e.Tick, Dt);
                break;
            case AiEventKind.ActorTeam:
                if (Board.Of(e.Other, make: true) is { } swapped)
                    Reclassified(swapped, e.Code, e.Tick);
                break;
            case AiEventKind.MatchState:
                Board.SetTeamScore(e.Code, e.Code2);
                break;
            case AiEventKind.FriendlyFireSet:
                FriendlyFireOn = e.Code != 0;
                break;
            case AiEventKind.Callout:
                Callout(e);
                break;
            case AiEventKind.Order:
                // A teammate's order only (B.9: one who swapped away is not listened to).
                if (Board.Of(e.Other, make: true) is { } from && IsFriend(from))
                    LastOrder = ((OrderKind)e.Code, e.Other, e.Tick);
                break;
        }
    }

    /// <summary>The last order a teammate gave in team chat (B.5, Q5): what, who, when; None before one.</summary>
    public (OrderKind Kind, ActorId From, Tick When) LastOrder { get; private set; }

    /// <summary>When the bot's own team last changed (B.9): its role and goal end there.</summary>
    public Tick OwnSwap { get; private set; }

    /// <summary>
    /// The bot itself was moved (B.9: roster balancing, a vote's scramble): its beliefs
    /// reset for the new friend list (every ammo estimate, every belief of where someone
    /// is, the sounds to look into); who it knows, their habits and its grudges stay.
    /// </summary>
    private void Swapped(int team, Tick tick)
    {
        bool had = TeamRule.IsGameTeam(Team);
        Team = team;
        if (!had)
            return;
        OwnSwap = tick;
        Board.ForgetLife();
        for (int s = 0; s < Board.Slots; s++)
            Board.Actor(s).Where?.Clear();
    }

    /// <summary>
    /// Someone changed team (B.9's table): turned friend, what was tracked of them as an
    /// enemy goes (ammo, belief), habits and a dormant grudge stay; turned enemy, they are a
    /// target only after a recognition, their last seen place kept as it was.
    /// </summary>
    private void Reclassified(KnownActor a, int team, Tick tick)
    {
        bool wasFriend = IsFriend(a);
        a.Team = team;
        bool friend = IsFriend(a);
        if (friend && !wasFriend)
        {
            a.Ammo.Reset();
            a.Where?.Clear();
        }
        else if (wasFriend && !friend)
        {
            a.FoeSince = tick;
        }
    }

    /// <summary>
    /// A teammate's callout (B.4, B.9): taken only while its speaker is a friend, and
    /// dropped when it is about someone who has since become one. An enemy's place is
    /// somewhere to look, heard rather than seen; the rest are kept for the team's roles.
    /// </summary>
    private void Callout(in AiEvent e)
    {
        if (Board.Of(e.Other, make: false) is not { } speaker || !IsFriend(speaker))
        {
            CalloutsDropped++;
            return;
        }

        int subject = e.Code2 - 1;
        KnownActor? about = (uint)subject < (uint)Board.Slots && Board.Actor(subject).Known ? Board.Actor(subject) : null;
        if (about is not null && IsFriend(about))
        {
            CalloutsDropped++;
            return;
        }

        CalloutsHeard++;
        var heard = new HeardCallout(e.CalloutKind, e.Position, e.CalloutCount, e.Tick, e.Other);
        switch (heard.Kind)
        {
            case CalloutKind.Enemy:
                LastEnemyCall = heard;
                if (about is { InSight: false, Dead: false })
                {
                    about.EverHeard = true;
                    about.LastHeardPos = e.Position;
                    about.LastHeardTick = e.Tick;
                    about.Confidence = Math.Max(about.Confidence, e.Amount);
                    about.LastKnownCell = CellAt(e.Position);
                    about.Where?.Heard(about.LastKnownCell, CalloutSpread);
                }

                Board.Investigate(new Investigation(e.Position, e.Amount, e.Tick, SoundKind.Unknown, ActorId.None), e.Tick, Dt);
                break;
            case CalloutKind.Rally:
                LastRally = heard;
                break;
            case CalloutKind.Push:
                LastPush = heard;
                break;
            case CalloutKind.GoingAround:
                LastGoingAround = heard;
                break;
        }
    }

    /// <summary>How wide a called place is: a room's worth, not a point.</summary>
    public const float CalloutSpread = 256f;

    /// <summary>
    /// The kill feed as a team reads it (B.5): a teammate dying in sight to an enemy in
    /// sight is a chance to trade; an enemy dying to the bot or a teammate is the team's progress.
    /// </summary>
    private void TeamKillFeed(KnownActor dead, ActorId killer, Tick tick)
    {
        if (!TeamRule.IsGameTeam(Team))
            return;
        KnownActor? by = killer == Bot ? null : Board.Of(killer, make: false);
        if (!Foe(dead) && SawDie(dead, tick) && by is { InSight: true } && Foe(by))
            TradeChance = (by.Id, tick);
        if (Foe(dead) && (killer == Bot || by is not null && !Foe(by)))
            LastTeamKill = tick;
    }

    /// <summary>A body leaves sight as it falls: a teammate seen this lately before the kill feed was seen to die.</summary>
    public const float DeathSightSeconds = 0.5f;

    /// <summary>
    /// Whether the bot saw <paramref name="dead"/> die: in sight, or lost from sight within
    /// <see cref="DeathSightSeconds"/> of the kill feed. The game drops a body from sight the
    /// tick it dies, the same tick as or before the kill feed arrives (ss_ai_tdm's trade scene).
    /// </summary>
    private bool SawDie(KnownActor dead, Tick tick)
        => dead.InSight || (dead.EverSeen && (tick - dead.LastSeenTick) * World.TickSeconds <= DeathSightSeconds);

    /// <summary>The bot's own rally or push call: it hears its own words as its teammates will.</summary>
    public void Said(CalloutKind kind, AiVector place, Tick tick)
    {
        var said = new HeardCallout(kind, place, 0, tick, Bot);
        if (kind == CalloutKind.Rally)
            LastRally = said;
        else if (kind == CalloutKind.Push)
            LastPush = said;
    }

    /// <summary>
    /// Someone went to watch: forgotten like a dead player and more, position, belief,
    /// sounds to look into and scoreboard row, and what it was doing with them; a
    /// grudge goes dormant. Coming back they are new, the grudge awake (§4.1, ruling
    /// 2026-11-02: "bots should ignore spectators as well").
    /// </summary>
    private void Spectating(ActorId actor, bool watching)
    {
        if (Board.Of(actor, make: true) is not { } a)
            return;
        if (watching)
        {
            a.GoneToWatch();
            Board.ForgetSource(actor);
            _track[actor.Slot] = default;
        }
        else
        {
            a.BackFromWatching();
        }
    }

    private void Gained(in AiEvent e)
    {
        if (Board.Of(e.Other, make: true) is not { } a)
            return;
        a.InSight = true;
        a.Dead = false;
        Seen(a, e.Position, e.Velocity, e.Tick);
        ref Track t = ref _track[a.Id.Slot];
        t.ContactFrom = Self.DistanceTo(e.Position);
        t.ContactTick = e.Tick;
        t.ContactJudged = false;
        Board.Resolve(e.Position, 0f, a.Id);
    }

    private void Lost(in AiEvent e)
    {
        if (Board.Of(e.Other, make: false) is not { } a)
            return;
        a.InSight = false;
        a.LastSeenPos = e.Position;
        a.LastSeenVel = e.Velocity;
        a.LastSeenTick = e.Tick;
        a.LastKnownCell = CellAt(e.Position);
        a.Where?.LostAt(a.LastKnownCell, e.Velocity);
        if (!Foe(a))
            return;
        ref Track t = ref _track[a.Id.Slot];
        OpponentHabits h = HabitsOf(a.Id);
        h.LostSight(t.Hurt && (e.Tick - t.HurtTick) * Dt <= HurtRetreatSeconds);
        if (a.Ammo.ReloadInProgress && !t.ReloadJudged)
        {
            h.WhileReloading(ReloadBehaviour.Retreated);
            t.ReloadJudged = true;
        }
    }

    private void Seen(KnownActor a, AiVector pos, AiVector vel, Tick tick)
    {
        a.EverSeen = true;
        a.LastSeenPos = pos;
        a.LastSeenVel = vel;
        a.LastSeenTick = tick;
        a.Confidence = 1f;
        CellId cell = CellAt(pos);
        if (!Foe(a))
        {
            a.LastKnownCell = cell;
            return;
        }

        ref Track t = ref _track[a.Id.Slot];
        if (cell != a.LastKnownCell)
        {
            if (cell.IsValid)
                HabitsOf(a.Id).Visit(cell);
            t.CellSince = tick;
            t.Camped = false;
        }
        else if (cell.IsValid && !t.Camped && (tick - t.CellSince) * Dt >= CampSeconds)
        {
            HabitsOf(a.Id).Camped(cell);
            t.Camped = true;
        }

        a.LastKnownCell = cell;

        // A pickup it stood on is one of its favourites (once per visit).
        for (int i = 0; i < World.Map.Items.Length; i++)
        {
            if (World.Map.Items[i].Position.DistanceTo(pos) <= 48f)
            {
                if (t.AtItem != i + 1)
                    HabitsOf(a.Id).PickedUp(i);
                t.AtItem = i + 1;
                return;
            }
        }

        t.AtItem = 0;
    }

    private void Heard(in AiEvent e)
    {
        var kind = (SoundKind)e.Code;
        KnownActor? a = Board.Of(e.Other, make: e.Other.IsValid);
        if (a is not null)
        {
            a.EverHeard = true;
            a.LastHeardPos = e.Position;
            a.LastHeardTick = e.Tick;
            a.LastHeardKind = kind;
            if (!a.InSight)
            {
                a.Confidence = Math.Max(a.Confidence, 0.5f + 0.5f * e.Amount);
                a.LastKnownCell = CellAt(e.Position);
                // Heard: the mass collapses round the heard place, its error's width (§5.3.1).
                a.Where?.Heard(a.LastKnownCell, MathF.Max(64f, Self.DistanceTo(e.Position) * 0.15f));
            }

            if (e.Code2 >= 0 && Foe(a))
            {
                switch (kind)
                {
                    case SoundKind.Gunfire:
                        Fired(a, e.Code2, 1, e.Tick);
                        break;
                    case SoundKind.Reload:
                        // A shell-by-shell weapon plays its reload sound once a shell.
                        bool shells = Facts(e.Code2, out WeaponFacts f) && f.ShellByShell;
                        Reload(a, e.Code2, shells ? ReloadPhase.Loop : ReloadPhase.Start, 1, e.Tick);
                        break;
                    case SoundKind.DryFire:
                        if (Facts(e.Code2, out WeaponFacts d))
                            a.Ammo.DryFire(d, e.Tick);
                        break;
                }
            }
        }

        // A teammate's own noise (steps, a reload, a pickup) is recognised for what it is;
        // its fight's (gunfire, a blast, pain) is still somewhere worth going.
        bool friendsNoise = a is not null && !Foe(a) && kind is SoundKind.Footstep or SoundKind.Reload or SoundKind.Pickup or SoundKind.DryFire or SoundKind.Door;

        // Unexplained: a sound whose source the bot does not see.
        if (!friendsNoise && (a is null || !a.InSight))
            Board.Investigate(new Investigation(e.Position, e.Amount, e.Tick, kind, e.Other), e.Tick, Dt);
    }

    /// <summary>
    /// A perceived shot: one heard and one seen at the same tick are the same
    /// shot, so a tick counts the most rounds either sense reported.
    /// </summary>
    private void Fired(KnownActor a, int weapon, int rounds, Tick tick)
    {
        if (!Foe(a))
        {
            if (weapon >= 0)
                a.WeaponSeen = weapon;
            return;
        }

        ref Track t = ref _track[a.Id.Slot];
        int counted = t.ShotTick == tick ? t.ShotRounds : 0;
        int add = rounds - counted;
        if (add <= 0)
            return;
        t.ShotTick = tick;
        t.ShotRounds = rounds;
        if (weapon >= 0)
            a.WeaponSeen = weapon;
        if (Facts(weapon, out WeaponFacts f))
            a.Ammo.Shot(f, add, tick);
    }

    private void Reload(KnownActor a, int weapon, ReloadPhase phase, int rounds, Tick tick)
    {
        if (!Foe(a) || !Facts(weapon, out WeaponFacts f))
            return;
        ref Track t = ref _track[a.Id.Slot];
        switch (phase)
        {
            case ReloadPhase.Start:
                if (a.Ammo.ReloadInProgress && a.Ammo.Weapon == weapon)
                    return;
                a.Ammo.ReloadStarted(f, tick, Dt);
                HabitsOf(a.Id).Reloaded(a.Ammo.LastReloadSpent);
                t.ReloadFrom = Self.DistanceTo(a.LastSeenPos);
                t.ReloadJudged = false;
                break;
            case ReloadPhase.Loop:
                bool started = a.Ammo.ReloadInProgress;
                a.Ammo.Shell(f, Math.Max(1, rounds), tick, Dt);
                if (!started)
                {
                    HabitsOf(a.Id).Reloaded(a.Ammo.LastReloadSpent);
                    t.ReloadFrom = Self.DistanceTo(a.LastSeenPos);
                    t.ReloadJudged = false;
                }

                break;
            default:
                if (a.Ammo.Weapon != weapon)
                    a.Ammo.SeeWeapon(f, tick);
                if (a.InSight && !t.ReloadJudged)
                {
                    float now = Self.DistanceTo(a.LastSeenPos);
                    HabitsOf(a.Id).WhileReloading(now < t.ReloadFrom - 32f ? ReloadBehaviour.Closed : ReloadBehaviour.Held);
                    t.ReloadJudged = true;
                }

                a.Ammo.ReloadFinished(tick);
                break;
        }
    }

    /// <summary>The tick's view: the bot's own state, and each sighted actor's place and weapon.</summary>
    public void Observe(PerceivedView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Self = view.Self.Origin;
        Alive = view.Self.Alive;
        if (view.Self.Team != Team)
            Swapped(view.Self.Team, view.Tick);
        foreach (ref readonly ActorFacts f in view.Sighted)
        {
            if (Board.Of(f.Id, make: true) is not { } a)
                continue;
            if (a.Team != f.Team)
                Reclassified(a, f.Team, view.Tick);
            a.InSight = true;
            a.Dead = false;
            Seen(a, f.Origin, f.Velocity, view.Tick);
            if (f.WeaponSeen != a.WeaponSeen && f.WeaponSeen >= 0)
            {
                a.WeaponSeen = f.WeaponSeen;
                if (Foe(a) && Facts(f.WeaponSeen, out WeaponFacts w))
                    a.Ammo.SeeWeapon(w, view.Tick);
            }

            if (!Foe(a))
                continue;
            ref Track t = ref _track[a.Id.Slot];
            if (!t.ContactJudged && (view.Tick - t.ContactTick) * Dt >= ContactSeconds)
            {
                HabitsOf(a.Id).Contact(Self.DistanceTo(f.Origin) < t.ContactFrom - 32f);
                t.ContactJudged = true;
            }
        }
    }

    /// <summary>Time passes to <paramref name="now"/>: reloads end when due, estimates widen, confidence and habits decay.</summary>
    public void Advance(Tick now)
    {
        for (int s = 0; s < Board.Slots; s++)
        {
            KnownActor a = Board.Actor(s);
            if (a.Known && a.Ammo.Known && Facts(a.Ammo.Weapon, out WeaponFacts f) && !a.InSight)
                a.Ammo.Advance(f, now, Dt, InHearing(a, now));
            else if (a.Known && a.Ammo.Known && Facts(a.Ammo.Weapon, out WeaponFacts g) && a.Ammo.ReloadInProgress)
                a.Ammo.Advance(g, now, Dt);
        }

        if (_lastAdvance.Value > 0 && now > _lastAdvance)
        {
            float step = (now - _lastAdvance) * Dt;
            for (int s = 0; s < Board.Slots; s++)
            {
                KnownActor a = Board.Actor(s);
                if (a.Known && !a.InSight && !a.Dead && a.Where is { Active: true } where)
                    where.Step(step, OpponentSpeed, TryHabits(a.Id, out OpponentHabits? h) ? h!.Route : []);
            }
        }

        Board.Decay(now, Dt, _params);
        if (_lastAdvance.Value > 0 && now > _lastAdvance)
        {
            float seconds = (now - _lastAdvance) * Dt;
            for (int s = 0; s < Board.Slots; s++)
            {
                KnownActor a = Board.Actor(s);
                if (a.Known && TryHabits(a.Id, out OpponentHabits? h))
                    h!.Decay(seconds, _params.HabitHalfLifeSeconds);
            }
        }

        _lastAdvance = now;
    }

    /// <summary>How long after hearing an opponent the bot takes its silence as evidence (§5.1).</summary>
    public const float HearingSeconds = 3f;

    /// <summary>Whether the bot would hear the opponent fire now: it heard or saw it within <see cref="HearingSeconds"/>.</summary>
    private bool InHearing(KnownActor a, Tick now) => (now - a.LastPerceivedTick) * Dt <= HearingSeconds;

    private struct Track
    {
        public Tick ShotTick;
        public int ShotRounds;
        public Tick HurtTick;
        public bool Hurt;
        public Tick CellSince;
        public bool Camped;
        public int AtItem;
        public float ContactFrom;
        public Tick ContactTick;
        public bool ContactJudged;
        public float ReloadFrom;
        public bool ReloadJudged;
    }
}
