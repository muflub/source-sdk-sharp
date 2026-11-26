using System.Collections.Immutable;
using SourceSharp.Ai;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// The horde's first decider (plan_descent_logic HO4; plan_tf2_rpg B.5): one
/// robot's hunt, read from its memory and view, written as an intent. It never
/// fires (attacks are HO7's), never says anything (the session's team layer is
/// off, and a recording of words would not replay: see HO4's hand-back), and
/// reads only what a recording writes: its own origin and weapon family
/// (<see cref="SelfState.ActiveWeapon"/>, <see cref="HordeWeapons"/>), the heroes it
/// holds sight of, and the memory those and its events built.
///
/// <list type="number">
/// <item><b>Chase.</b> A hero in sight: the nearest. Closer than its family's range
/// times its <c>preferredRangeScale</c> (Aggression: lower closes in), it stands and
/// faces the hero; farther, it runs to the hero along its route (<see cref="HordeRoute"/>: the
/// level graph's path, or the room route), facing it.</item>
/// <item><b>Last known.</b> Sight lost: it runs to where it last saw the hero, then
/// searches there for <c>searchSeconds</c> (Tenacity), facing the place.</item>
/// <item><b>Investigate.</b> A sound it heard (or a hit from out of sight, towards the
/// hit's direction at its family's range, or a pack-mate hurt or killed, where the mate
/// was): it walks there and searches as long.</item>
/// <item><b>Home.</b> Nothing to hunt: it walks back to its post (<see cref="BrainSetup.Home"/>:
/// its roster marker; the place it stood at its first decide for a robot with none, one
/// spawned by hand), and holds there.</item>
/// </list>
///
/// <b>The leash</b> (plan L25, <see cref="HordeRoute.Leash"/>): a hero in sight out of its engage
/// range, and a place to go and look, past the leash from its home room (<see cref="HordeLeash"/>:
/// the room it would go to next, or the hero's or the place's, too many doors from home) are let
/// go: the place is searched without going, and it walks home. A hero in sight within its engage
/// range is fought wherever he stands: a robot in a fight finishes it.
///
/// A lost contact outranks a sound. Each contact, sound or hit is searched once:
/// one newer than the last searched starts a new hunt. Every threshold is a
/// temperament parameter; the one number of its own is <see cref="ArriveUnits"/>,
/// how near a point counts as there (geometry, not temperament).
/// </summary>
public sealed class HuntDecider : IDecider, IHordeRouted
{
    /// <summary>How near a goal the robot is there.</summary>
    public const float ArriveUnits = 40f;

    public enum Node : short
    {
        Hunt,
        Hold,
        Chase,
        Engage,
        LastKnown,
        Search,
        Investigate,
        Home,
    }

    /// <summary>The tree's node names by <see cref="Node"/> (what a recording's DecisionTree record names).</summary>
    public static ImmutableArray<string> Names => [NameOf(Node.Hunt), NameOf(Node.Hold), NameOf(Node.Chase), NameOf(Node.Engage),
                                                    NameOf(Node.LastKnown), NameOf(Node.Search), NameOf(Node.Investigate), NameOf(Node.Home)];

    /// <summary>A node's name.</summary>
    public static string NameOf(Node node) => node switch
    {
        Node.Hunt => "hunt",
        Node.Hold => "hold",
        Node.Chase => "chase",
        Node.Engage => "engage",
        Node.LastKnown => "last-known",
        Node.Search => "search",
        Node.Investigate => "investigate",
        _ => "home",
    };

    /// <summary>Each node's branch.</summary>
    public static ImmutableArray<int> Branches => [0, 1, 2, 2, 3, 3, 4, 5];

    private enum GoalKind : byte
    {
        None,
        Contact,
        Sound,
        Hit,
        Ally,
    }

    private readonly BrainSetup _setup;
    private readonly HordeRoute _route;

    private bool _started;
    private AiVector _post;
    private int _homeRoom = -1;
    private GoalKind _goal;
    private AiVector _goalAt;
    private Tick _goalStamp;
    private bool _searching;
    private Tick _searchUntil;
    private Tick _searchedContact, _searchedSound, _searchedHit, _searchedAlly;
    private DoingRecord _doing;

    public HuntDecider(BrainSetup setup, HordeRoute route)
    {
        _setup = setup;
        _route = route;
    }

    /// <summary>The way this brain walks to its goals: the level graph's paths, or the room route.</summary>
    public HordeRoute Route => _route;

    public BtTrace Trace { get; } = new();

    public DoingRecord Doing => _doing;

    public IReadOnlyList<string> NodeNames => Names;

    public IReadOnlyList<int> NodeBranches => Branches;

    /// <summary>The robot's post: where it stood at its first decide.</summary>
    public AiVector Post => _post;

    /// <summary>The node the last decide ended in.</summary>
    public Node Last { get; private set; }

    public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
    {
        ArgumentNullException.ThrowIfNull(view);
        Trace.Begin(tick);
        AiVector self = view.Self.Origin;
        if (!_started)
        {
            _started = true;
            _post = _setup.Home ?? self;
            _homeRoom = _route.Level.RoomAt(_post);
        }

        DecisionParams p = _setup.Params;
        float engage = HordeWeapons.EngageRange(view.Self.ActiveWeapon) * p.PreferredRangeScale;

        // 1. A hero in sight: the nearest, unless he is out of its engage range and past its leash.
        bool letGo = false;
        if (Nearest(view, self) is { } seen)
        {
            _goal = GoalKind.None;
            _searching = false;
            // What it heard before this is the fight it now sees.
            _searchedSound = tick;
            _searchedHit = tick;
            _searchedAlly = tick;
            letGo = _route.Leash.LetsGo(_route.Level, _homeRoom, self, seen.Origin, engage);
            if (!letGo)
            {
                var look = new LookIntent(LookKind.Actor, seen.Origin, seen.Id, AimPoint.Body, 0f, 0);
                return self.DistanceTo(seen.Origin) > engage
                    ? End(tick, seq, Node.Chase, _route.Follow(tick, view.Self, self, seen.Origin, MoveSpeed.Run), look, seen.Origin, seen.Id)
                    : End(tick, seq, Node.Engage, MoveIntent.Hold, look, seen.Origin, seen.Id);
            }
        }

        // 2.-3. Something to go and look at: a lost contact first, else the newest sound or hit; one
        // past the leash is searched without going.
        Blackboard board = _setup.Memory.Board;
        ActorId lost = ActorId.None;
        if (letGo)
            _goal = GoalKind.None;
        else if (LostContact(board, out AiVector lostAt, out Tick lostStamp, out lost))
            Aim(GoalKind.Contact, lostAt, lostStamp);
        else if (Unsearched(board, tick, self, engage, out GoalKind kind, out AiVector at, out Tick stamp))
            Aim(kind, at, stamp);
        else
            _goal = GoalKind.None;

        if (_goal != GoalKind.None && !_searching && _route.Leash.Past(_route.Level, _homeRoom, self, _goalAt))
            Searched();

        if (_goal != GoalKind.None)
        {
            Node going = _goal == GoalKind.Contact ? Node.LastKnown : Node.Investigate;
            if (!_searching && !There(self, _goalAt))
            {
                MoveIntent way = _route.Follow(tick, view.Self, self, _goalAt, _goal == GoalKind.Contact ? MoveSpeed.Run : MoveSpeed.Walk);

                // A place off its floor: the route has brought it as near as it can stand and holds
                // there (HordeRoute.Held), and that is where it searches from. Left going there it
                // would hold for good and never go home.
                if (!_route.Held)
                {
                    var toward = new LookIntent(LookKind.Point, _goalAt, ActorId.None, AimPoint.Body, 0f, 0);
                    return End(tick, seq, going, way, toward, _goalAt, lost);
                }
            }

            if (!_searching)
            {
                _searching = true;
                _searchUntil = tick + Math.Max(1, (int)MathF.Ceiling(p.SearchSeconds / _setup.TickSeconds));
            }

            if (tick < _searchUntil)
            {
                var watch = new LookIntent(LookKind.Watch, _goalAt, ActorId.None, AimPoint.Body, 0f, _searchUntil.Value);
                return End(tick, seq, Node.Search, MoveIntent.Hold, watch, _goalAt, lost);
            }

            Searched();
        }

        // 4.-5. Home, then hold there.
        if (self.DistanceTo(_post) > ArriveUnits)
            return End(tick, seq, Node.Home, _route.Follow(tick, view.Self, self, _post, MoveSpeed.Walk), LookIntent.AlongPath, _post, ActorId.None);
        return End(tick, seq, Node.Hold, MoveIntent.Hold, LookIntent.AlongPath, _post, ActorId.None);
    }

    /// <summary>
    /// The robot fought a hero in sight at <paramref name="tick"/> (a family decider fighting on its
    /// own): as this hunt's sighted branch does, every sound, hit and hurt pack-mate before it is
    /// that fight, searched, and no goal is held.
    /// </summary>
    public void Fought(Tick tick)
    {
        _goal = GoalKind.None;
        _searching = false;
        _searchedSound = tick;
        _searchedHit = tick;
        _searchedAlly = tick;
    }

    /// <summary>
    /// Whether a robot at <paramref name="self"/> is at a goal at <paramref name="at"/>: within
    /// <see cref="ArriveUnits"/> on the floor plane. Its height is not asked: a hero last seen or
    /// heard in the air (a jump, a fall, a rocket jump) is searched for from the floor under him,
    /// where the robot's walk ends, and a robot standing there is never left following a place it
    /// cannot rise to.
    /// </summary>
    public static bool There(AiVector self, AiVector at) => (at - self).Length2D <= ArriveUnits;

    /// <summary>The goal to go to; a new one (another kind or a newer stamp) starts its search over.</summary>
    private void Aim(GoalKind kind, AiVector at, Tick stamp)
    {
        if (kind != _goal || stamp != _goalStamp)
            _searching = false;
        _goal = kind;
        _goalAt = at;
        _goalStamp = stamp;
    }

    private void Searched()
    {
        switch (_goal)
        {
            case GoalKind.Contact: _searchedContact = _goalStamp; break;
            case GoalKind.Sound: _searchedSound = _goalStamp; break;
            case GoalKind.Hit: _searchedHit = _goalStamp; break;
            case GoalKind.Ally: _searchedAlly = _goalStamp; break;
        }

        _goal = GoalKind.None;
        _searching = false;
    }

    private static ActorFacts? Nearest(PerceivedView view, AiVector self)
    {
        ActorFacts? best = null;
        float bestD = float.MaxValue;
        foreach (ref readonly ActorFacts f in view.Sighted)
        {
            // A pack-mate in the view is the pack's board, not a quarry (HordeView).
            if (!HordeView.IsHero(f))
                continue;
            float d = self.DistanceTo(f.Origin);
            if (d < bestD)
                (best, bestD) = (f, d);
        }

        return best;
    }

    /// <summary>The newest hero lost from sight and not yet searched for.</summary>
    private bool LostContact(Blackboard board, out AiVector at, out Tick stamp, out ActorId who)
    {
        at = default;
        stamp = _searchedContact;
        who = ActorId.None;
        for (int s = HordeSlots.FirstHero; s < HordeSlots.FirstHero + HordeSlots.Heroes && s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || !a.EverSeen || a.InSight || a.LastSeenTick <= stamp)
                continue;
            at = a.LastSeenPos;
            stamp = a.LastSeenTick;
            who = a.Id;
        }

        return who.IsValid;
    }

    /// <summary>The newest sound, hit or hurt pack-mate not yet searched: a hit is looked for at the family's range along its direction, a mate where it was.</summary>
    private bool Unsearched(Blackboard board, Tick tick, AiVector self, float engage, out GoalKind kind, out AiVector at, out Tick stamp)
    {
        kind = GoalKind.None;
        at = default;
        stamp = default;
        if (board.TryBestInvestigation(tick, _setup.TickSeconds, out Investigation heard) && heard.Tick > _searchedSound)
            (kind, at, stamp) = (GoalKind.Sound, heard.Position, heard.Tick);

        Tick hit = board.LastDamageTick;
        // The place is fixed when the hit is taken as the goal: worked out from where the robot
        // stands at each decide it would keep its distance ahead of him, and he would walk the
        // hit's way to the end of the level without ever arriving.
        if (hit > _searchedHit && hit.Value > 0 && (kind == GoalKind.None || hit > stamp))
            (kind, at, stamp) = (GoalKind.Hit, _goal == GoalKind.Hit && _goalStamp == hit ? _goalAt : self + board.LastDamageDir.Normalised() * engage, hit);

        // A pack-mate hurt or killed (B.3's AllyDamaged, AllyDied): it goes to where the mate was,
        // unless the hero who did it has gone (dead: the core holds him as gone to watch).
        BotMemory memory = _setup.Memory;
        var (allyAt, allyTick, by) = memory.LastAllyDied.When > memory.LastAllyHurt.When
            ? (memory.LastAllyDied.Where, memory.LastAllyDied.When, memory.LastAllyDied.Killer)
            : (memory.LastAllyHurt.Where, memory.LastAllyHurt.When, memory.LastAllyHurt.Attacker);
        if (allyTick > _searchedAlly && allyTick.Value > 0 && !Gone(board, by) && (kind == GoalKind.None || allyTick > stamp))
            (kind, at, stamp) = (GoalKind.Ally, allyAt, allyTick);

        return kind != GoalKind.None;
    }

    /// <summary>Whether <paramref name="who"/> is an actor the board holds as gone to watch (a dead hero, forgotten).</summary>
    private static bool Gone(Blackboard board, ActorId who) =>
        who.IsValid && (uint)who.Slot < (uint)board.Slots && board.Actor(who.Slot) is { Spectating: true } a && a.Id == who;

    private IntentCommand End(Tick tick, int seq, Node node, in MoveIntent move, in LookIntent look, AiVector goal, ActorId target)
    {
        Last = node;
        Trace.Add((short)node, BtStatus.Success, 0f);
        var reason = new DecisionReason((short)node, 1f, 0f, 0f);
        Trace.End(reason);
        _doing = new DoingRecord(NameOf(Node.Hunt), NameOf(node), true, goal, target, "", 0f, 0f, 0f, 0f, 0f, 0f, tick + IntentCommand.LifetimeTicks);
        return new IntentCommand(_setup.Bot, tick, seq, tick + IntentCommand.LifetimeTicks, move, look, ActionIntent.None, reason);
    }
}
