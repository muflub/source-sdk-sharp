namespace SourceSharp.Ai;

/// <summary>
/// What a bot's memory works over for one map (plan_hl2mp_ai §5.0): the map's
/// knowledge, its graph (null with no navigation), the tick length, and the
/// identity an opponent's habits are kept under (§5.2).
/// </summary>
public sealed record MemoryWorld(MapKnowledge Map, NavGraph? Graph, float TickSeconds, Func<ActorId, string> IdentityOf)
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
            for (int i = 0; i < slots; i++)
                Board.Actor(i).Where = new PositionPredictor(graph);
        }
    }

    /// <summary>The speed an unseen opponent is predicted to move at: HL2MP's run (190 u/s, hl2mp_player.cpp).</summary>
    public const float OpponentSpeed = 190f;

    public ActorId Bot { get; }

    public Blackboard Board { get; }

    public MemoryWorld World { get; }

    public MemoryParams Params => _params;

    /// <summary>Where the bot itself was at its last view.</summary>
    public AiVector Self { get; private set; }

    public bool Alive { get; private set; }

    /// <summary>The habits of <paramref name="opponent"/> on this map.</summary>
    public OpponentHabits HabitsOf(ActorId opponent)
        => _habits.For(World.Map.Map, World.IdentityOf(opponent), World.Graph?.CellCount ?? 0);

    public bool TryHabits(ActorId opponent, out OpponentHabits? habits)
        => _habits.TryGet(World.Map.Map, World.IdentityOf(opponent), out habits);

    private float Dt => World.TickSeconds;

    private CellId CellAt(AiVector p) => World.Graph?.FindCell(p + new AiVector(0f, 0f, 1f)) ?? CellId.None;

    private bool Facts(int weapon, out WeaponFacts f) => World.Map.Weapons.TryGet(weapon, out f);

    /// <summary>One decide step's inputs: the events since the last, then the view, then time.</summary>
    public void Step(Tick tick, PerceivedView view, ReadOnlySpan<AiEvent> events)
    {
        foreach (AiEvent e in events)
            Apply(e);
        Observe(view);
        Advance(tick);
    }

    public void Apply(in AiEvent e)
    {
        // The bot is not one of the actors it remembers (its own scoreboard
        // row and kill-feed line are its HUD's, not an opponent's).
        if (e.Other == Bot && e.Kind is not (AiEventKind.Killed or AiEventKind.Damaged))
            return;

        switch (e.Kind)
        {
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
                    attacker.DamageTakenFrom += e.Amount;
                    if (attacker.EverSeen)
                        HabitsOf(attacker.Id).Engaged(Self.DistanceTo(attacker.LastSeenPos));
                }

                break;
            case AiEventKind.DealtDamage:
                if (Board.Of(e.Other, make: true) is { } victim)
                {
                    victim.HealthEstimate = e.Code;
                    victim.TimesEngaged++;
                    _track[victim.Id.Slot].HurtTick = e.Tick;
                    _track[victim.Id.Slot].Hurt = true;
                    if (victim.EverSeen)
                        HabitsOf(victim.Id).Engaged(Self.DistanceTo(victim.LastSeenPos));
                }

                break;
            case AiEventKind.KillFeed:
                if (Board.Of(e.Other, make: true) is { } dead)
                {
                    // It comes back at full health with a clip nobody knows (§5.2).
                    dead.Dead = true;
                    dead.InSight = false;
                    dead.HealthEstimate = 100;
                    dead.Ammo.Reset();
                    dead.Where?.Clear();
                    dead.Confidence = 0f;
                }

                break;
            case AiEventKind.Killed:
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

            if (e.Code2 >= 0)
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

        // Unexplained: a sound whose source the bot does not see.
        if (a is null || !a.InSight)
            Board.Investigate(new Investigation(e.Position, e.Amount, e.Tick, kind, e.Other), e.Tick, Dt);
    }

    /// <summary>
    /// A perceived shot: one heard and one seen at the same tick are the same
    /// shot, so a tick counts the most rounds either sense reported.
    /// </summary>
    private void Fired(KnownActor a, int weapon, int rounds, Tick tick)
    {
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
        if (!Facts(weapon, out WeaponFacts f))
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
        foreach (ref readonly ActorFacts f in view.Sighted)
        {
            if (Board.Of(f.Id, make: true) is not { } a)
                continue;
            a.InSight = true;
            a.Dead = false;
            Seen(a, f.Origin, f.Velocity, view.Tick);
            if (f.WeaponSeen != a.WeaponSeen && f.WeaponSeen >= 0)
            {
                a.WeaponSeen = f.WeaponSeen;
                if (Facts(f.WeaponSeen, out WeaponFacts w))
                    a.Ammo.SeeWeapon(w, view.Tick);
            }

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
