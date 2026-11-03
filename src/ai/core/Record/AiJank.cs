using System.Globalization;
using System.Text;

namespace SourceSharp.Ai;

/// <summary>Ticks of no shot over ticks with an opponent seen in weapon range.</summary>
public readonly record struct JankShare(int NotFiring, int Ticks)
{
    /// <summary>The share of ticks with no shot, or NaN with none in sight.</summary>
    public double Share => Ticks > 0 ? NotFiring / (double)Ticks : double.NaN;

    public static JankShare operator +(JankShare a, JankShare b) => new(a.NotFiring + b.NotFiring, a.Ticks + b.Ticks);
}

/// <summary>One actor's parts of the jank score (or a total's), by <see cref="AiJank"/>'s definitions.</summary>
public sealed record JankActor(
    ActorId Actor,
    bool Bot,
    double AliveSeconds,
    double StuckSeconds,
    double WallBumpSeconds,
    double IdleSeconds,
    int BranchFlips,
    int LeafFlips,
    JankShare Close,
    JankShare Far,
    double[] StrafeIntervalMs,
    double ReversalsPerMinute,
    double CommandMinutes)
{
    public double AliveMinutes => AliveSeconds / 60.0;

    private double PerMinute(double value) => AliveSeconds > 0 ? value / AliveMinutes : 0;

    public double StuckPerMinute => PerMinute(StuckSeconds);

    public double WallBumpPerMinute => PerMinute(WallBumpSeconds);

    public double IdlePerMinute => PerMinute(IdleSeconds);

    public double BranchFlipsPerMinute => PerMinute(BranchFlips);

    public double LeafFlipsPerMinute => PerMinute(LeafFlips);
}

/// <summary>The in-sight, not-firing ticks under one leaf.</summary>
public sealed record JankLeaf(string Leaf, JankShare Close, JankShare Far);

/// <summary>A place a part of the score happened: where, how long, how many times, who.</summary>
public sealed record JankSpot(AiVector Position, double Seconds, int Count, IReadOnlyList<ActorId> Actors);

/// <summary>What <c>ai-replay jank</c> prints: per actor, the totals, the leaves and the worst places.</summary>
public sealed record JankReport(
    IReadOnlyList<JankActor> Actors,
    JankActor Bots,
    JankActor People,
    IReadOnlyList<JankLeaf> Leaves,
    IReadOnlyDictionary<string, IReadOnlyList<JankSpot>> Places,
    string TreeSource);

/// <summary>
/// The headline: each part over the value that reads as plainly wrong, times its
/// weight, summed (plan_hl2mp_ai §3.4.5). The parts are what matter; this says
/// only whether to go and read them. Not clamped, so a regression past "plainly
/// wrong" still moves it.
/// </summary>
public static class JankScore
{
    /// <summary>A part: its name, what reads as plainly wrong, its weight and how it is read from an actor.</summary>
    public sealed record Part(string Name, string Unit, double Full, double Weight, Func<JankActor, double> Of);

    /// <summary>Yaw reversals per minute up to this are a person's (twice the 58 of the 2026-11-02 session, §12.3): only those above it count.</summary>
    public const double ReversalsAllowed = 120;

    /// <summary>A median strafe interval from this up is a person's (that session's is 1,230 ms): only the shortfall counts, as a share of it.</summary>
    public const double StrafeAllowedMs = 300;

    public static readonly IReadOnlyList<Part> Parts =
    [
        new("stuck", "s/min", 6, 25, a => a.StuckPerMinute),
        new("wall bump", "s/min", 6, 10, a => a.WallBumpPerMinute),
        new("branch flips", "/min", 10, 10, a => a.BranchFlipsPerMinute),
        new("leaf flips", "/min", 20, 10, a => a.LeafFlipsPerMinute),
        new("no shot <400", "share", 0.5, 20, a => Zero(a.Close.Share)),
        new("no shot >=400", "share", 0.8, 5, a => Zero(a.Far.Share)),
        new("idle", "s/min", 12, 10, a => a.IdlePerMinute),
        new("yaw reversals over allowed", "/min", 120, 5, a => Math.Max(0, a.ReversalsPerMinute - ReversalsAllowed)),
        new("strafe short of allowed", "share", 1, 5, a => Strafe(a.StrafeIntervalMs)),
    ];

    /// <summary>The score of an actor or a total.</summary>
    public static double Of(JankActor actor) => Parts.Sum(p => p.Weight * p.Of(actor) / p.Full);

    /// <summary>The weights, as the report prints them.</summary>
    public static string Formula()
        => "score = sum of weight x part / full: "
           + string.Join(", ", Parts.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} {p.Weight:G}/{p.Full:G} {p.Unit}")));

    private static double Zero(double share) => double.IsNaN(share) ? 0 : share;

    private static double Strafe(double[] intervals)
    {
        double median = AiMetrics.Quantile(intervals, 0.5);
        return double.IsNaN(median) ? 0 : Math.Max(0, (StrafeAllowedMs - median) / StrafeAllowedMs);
    }
}

/// <summary>
/// What makes a bot look wrong, as numbers (plan_hl2mp_ai §3.4.5,
/// <c>ai-replay jank</c>): from a recording, per actor, by the same definitions
/// for people where they apply (a person has no leaves, so no flips). Pure.
/// Speeds come from the frames' origins, not their velocities: it is where the
/// body went that looks wrong, and a recorded velocity is only what the engine
/// said it was. A step counts only while the actor lives (its frames), and not
/// across a respawn or teleport.
/// <list type="bullet">
/// <item><b>stuck</b>: a window of <see cref="StuckWindowTicks"/> whose every
/// tick holds a move key, with under <see cref="StuckProgress"/> units net
/// between its ends. Every tick of such a window is stuck. A fight's dodge is
/// not: a window in which <c>SideMove</c> changed sign with an opponent in
/// sight.</item>
/// <item><b>wall bump</b>: a move key held (any: a bot's wish is in its view's
/// frame, so one backing into a wall holds no forward) at under
/// <see cref="WallSpeed"/> u/s for over <see cref="WallMinTicks"/>; the whole
/// run counts.</item>
/// <item><b>decision flips</b>: A to B and back to A with B lasting at most
/// <see cref="FlipTicks"/>, branch and leaf apart, each return one flip. A death
/// ends the sequence.</item>
/// <item><b>in-sight not firing</b>: the ticks with an opponent seen (its own
/// sight events, a person's Observed ones) within the held weapon's reach and no
/// shot, over all such ticks, by leaf, apart under and from
/// <see cref="CloseRange"/> of the nearest. A shot is a <c>ShotFired</c> or a
/// press of attack, and lasts the weapon's cadence: a tap-firing pistol is
/// firing between its taps. A person's weapon is the one they last fired.</item>
/// <item><b>strafe interval, yaw reversals</b>: <see cref="AiMetrics"/>'s.</item>
/// <item><b>idle</b>: at least <see cref="IdleTicks"/> with no move key, no shot
/// and the view within <see cref="IdleLookDeg"/> of where the run began (a
/// bot's drift is no look); the whole run counts.</item>
/// </list>
/// </summary>
public static class AiJank
{
    public const int StuckWindowTicks = 66;       // 1 s
    public const float StuckProgress = 32f;
    public const float WallSpeed = 20f;
    public const int WallMinTicks = 34;           // over 0.5 s
    public const int FlipTicks = 66;              // 1 s
    public const float CloseRange = 400f;
    public const int IdleTicks = 66;              // 1 s
    public const double IdleLookDeg = 2;

    /// <summary>The cadence of a shot of a weapon the recording's world does not name.</summary>
    public const float DefaultCadenceSeconds = 0.25f;

    /// <summary>The side of a worst place's cell.</summary>
    public const float SpotCell = 128f;

    public const int TopSpots = 10;

    public const string Stuck = "stuck", WallBump = "wall bump", BranchFlip = "branch flips", LeafFlip = "leaf flips",
                        NoShotClose = "no shot <400", NoShotFar = "no shot >=400", Idle = "idle";

    private const int Attack = 1;     // IN_ATTACK
    private const int CommandStale = 8;
    private const int FrameStale = 4;

    /// <summary>
    /// Reads a recording. <paramref name="weaponRange"/> answers a weapon class's
    /// reach, the game's table; <paramref name="fallbackTree"/> names the leaves
    /// of a recording that carries no tree (before format 4).
    /// </summary>
    public static JankReport Read(Stream recording, Func<string, float> weaponRange, AiDecisionTree? fallbackTree)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(weaponRange);
        AiRecordFormat.ReadFileHeader(recording);
        var data = new Data();
        while (AiRecordFormat.TryReadRecord(recording, out AiRecordType type, out byte[] payload))
            data.Take(type, payload);

        string source = data.Tree is not null ? "the recording's" : fallbackTree is not null ? "this build's (the recording carries none)" : "none";
        AiDecisionTree? tree = data.Tree ?? fallbackTree;
        var places = new Places();
        var leaves = new SortedDictionary<string, (JankShare Close, JankShare Far)>(StringComparer.Ordinal);
        var actors = new List<JankActor>();
        foreach (ActorId actor in data.Frames.Keys.OrderBy(a => a.Slot).ThenBy(a => a.Serial))
            actors.Add(new Sweep(actor, data, tree, weaponRange, places, leaves).Run());

        return new JankReport(actors, Total(actors, true), Total(actors, false),
                              [.. leaves.Select(kv => new JankLeaf(kv.Key, kv.Value.Close, kv.Value.Far))],
                              places.Top(TopSpots), source);
    }

    /// <summary>The sum of the bots' or the people's parts, as one actor.</summary>
    private static JankActor Total(List<JankActor> all, bool bots)
    {
        List<JankActor> of = [.. all.Where(a => a.Bot == bots)];
        double minutes = of.Sum(a => a.CommandMinutes);
        return new JankActor(ActorId.None, bots, of.Sum(a => a.AliveSeconds), of.Sum(a => a.StuckSeconds), of.Sum(a => a.WallBumpSeconds), of.Sum(a => a.IdleSeconds),
                             of.Sum(a => a.BranchFlips), of.Sum(a => a.LeafFlips),
                             new JankShare(of.Sum(a => a.Close.NotFiring), of.Sum(a => a.Close.Ticks)),
                             new JankShare(of.Sum(a => a.Far.NotFiring), of.Sum(a => a.Far.Ticks)),
                             [.. of.SelectMany(a => a.StrafeIntervalMs)],
                             minutes > 0 ? of.Sum(a => a.ReversalsPerMinute * a.CommandMinutes) / minutes : 0, minutes);
    }

    /// <summary>The records the sweep reads, by actor.</summary>
    private sealed class Data
    {
        public readonly HashSet<ActorId> Bots = [];
        public readonly Dictionary<ActorId, List<AiUserCmd>> Commands = [];
        public readonly Dictionary<ActorId, SortedList<int, AiActorFrame>> Frames = [];
        public readonly List<AiEvent> Sights = [];
        public readonly List<AiEvent> Shots = [];
        public readonly Dictionary<int, WeaponFacts> Weapons = [];
        public readonly Dictionary<int, string> WeaponNames = [];
        public readonly Dictionary<ActorId, List<(int Tick, int Weapon)>> Held = [];
        public readonly Dictionary<ActorId, List<(int Tick, int Node)>> Decisions = [];
        public AiDecisionTree? Tree;

        public void Take(AiRecordType type, byte[] payload)
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.BotAdded:
                    Bots.Add(AiRecordFormat.ReadActor(r));
                    break;
                case AiRecordType.DecisionTree:
                    Tree = AiRecordFormat.ReadDecisionTree(r);
                    break;
                case AiRecordType.UserCmd:
                    AiUserCmd c = AiRecordFormat.ReadUserCmd(r);
                    Of(Commands, c.Actor).Add(c);
                    break;
                case AiRecordType.Frame:
                    AiActorFrame f = AiRecordFormat.ReadFrame(r);
                    if (!Frames.TryGetValue(f.Actor, out SortedList<int, AiActorFrame>? at))
                        Frames[f.Actor] = at = [];
                    at[f.Tick.Value] = f;
                    break;
                case AiRecordType.Intent:
                    IntentCommand i = AiRecordFormat.ReadIntent(r);
                    Of(Decisions, i.Bot).Add((i.FromTick.Value, i.Reason.NodeId));
                    break;
                case AiRecordType.Snapshot:
                    TakeSnapshot(r);
                    break;
                case AiRecordType.Event:
                case AiRecordType.Observed:
                    AiEvent e = AiRecordFormat.ReadEvent(r);
                    if (e.Kind is AiEventKind.SightGained or AiEventKind.SightLost)
                        Sights.Add(e);
                    else if (e.Kind is AiEventKind.ShotFired or AiEventKind.HitDealt)
                        Shots.Add(e);
                    break;
                case AiRecordType.World:
                    foreach (WeaponFacts w in AiRecordFormat.ReadWorld(r).Map.Weapons.All)
                    {
                        Weapons[w.Kind] = w;
                        WeaponNames[w.Kind] = w.Name;
                    }

                    break;
            }
        }

        /// <summary>What each bot held, by when it changed: its HUD's weapon, from its view.</summary>
        private void TakeSnapshot(BinaryReader r)
        {
            int tick = r.ReadInt32();
            int views = r.ReadUInt16();
            for (int i = 0; i < views; i++)
            {
                ActorId bot = AiRecordFormat.ReadActor(r);
                SelfState self = AiRecordFormat.ReadSelf(r);
                int sighted = r.ReadUInt16();
                for (int j = 0; j < sighted; j++)
                    AiRecordFormat.ReadFacts(r);
                List<(int Tick, int Weapon)> held = Of(Held, bot);
                if (held.Count == 0 || held[^1].Weapon != self.ActiveWeapon)
                    held.Add((tick, self.ActiveWeapon));
            }
        }

        private static List<T> Of<T>(Dictionary<ActorId, List<T>> by, ActorId actor)
        {
            if (!by.TryGetValue(actor, out List<T>? list))
                by[actor] = list = [];
            return list;
        }
    }

    /// <summary>The worst places, by metric and cell.</summary>
    private sealed class Places
    {
        private readonly Dictionary<string, Dictionary<(int, int, int), Cell>> _by = [];

        private sealed class Cell
        {
            public double X, Y, Z, Seconds;
            public int Count, Samples;
            public readonly HashSet<ActorId> Actors = [];
        }

        public void Add(string metric, AiVector at, double seconds, int count, ActorId actor)
        {
            if (!_by.TryGetValue(metric, out Dictionary<(int, int, int), Cell>? cells))
                _by[metric] = cells = [];
            var key = ((int)MathF.Floor(at.X / SpotCell), (int)MathF.Floor(at.Y / SpotCell), (int)MathF.Floor(at.Z / SpotCell));
            if (!cells.TryGetValue(key, out Cell? c))
                cells[key] = c = new Cell();
            c.X += at.X;
            c.Y += at.Y;
            c.Z += at.Z;
            c.Samples++;
            c.Seconds += seconds;
            c.Count += count;
            c.Actors.Add(actor);
        }

        public IReadOnlyDictionary<string, IReadOnlyList<JankSpot>> Top(int n)
        {
            var top = new Dictionary<string, IReadOnlyList<JankSpot>>();
            foreach ((string metric, Dictionary<(int, int, int), Cell> cells) in _by)
            {
                top[metric] = [.. cells.Values.OrderByDescending(c => metric is BranchFlip or LeafFlip ? c.Count : c.Seconds).ThenByDescending(c => c.Seconds).Take(n)
                                       .Select(c => new JankSpot(new AiVector((float)(c.X / c.Samples), (float)(c.Y / c.Samples), (float)(c.Z / c.Samples)), c.Seconds, c.Count,
                                                                 [.. c.Actors.OrderBy(a => a.Slot).ThenBy(a => a.Serial)]))];
            }

            return top;
        }
    }

    /// <summary>One actor's walk along its frames.</summary>
    private sealed class Sweep(ActorId actor, Data data, AiDecisionTree? tree, Func<string, float> weaponRange, Places places,
                               SortedDictionary<string, (JankShare Close, JankShare Far)> leaves)
    {
        private readonly bool _bot = data.Bots.Contains(actor);
        private readonly SortedList<int, AiActorFrame> _at = data.Frames[actor];
        private readonly List<AiUserCmd> _commands = data.Commands.GetValueOrDefault(actor) ?? [];
        private readonly List<(int Tick, int Node)> _decisions = data.Decisions.GetValueOrDefault(actor) ?? [];

        private double _stuck, _wall, _idle;
        private JankShare _close, _far;
        private int _branchFlips, _leafFlips;

        public JankActor Run()
        {
            IList<int> ticks = _at.Keys;
            IList<AiActorFrame> frames = _at.Values;
            List<AiEvent> sights = [.. data.Sights.Where(e => e.Bot == actor).OrderBy(e => e.Tick.Value)];
            List<AiEvent> shots = [.. data.Shots.Where(e => e.Bot == actor && e.Kind == AiEventKind.ShotFired).OrderBy(e => e.Tick.Value)];
            List<(int Tick, int Weapon)> held = data.Held.GetValueOrDefault(actor) ?? [];
            var seen = new HashSet<ActorId>();

            int nextCommand = 0, nextSight = 0, nextShot = 0, nextHeld = 0, nextDecision = 0, windowFrom = 0;
            int weapon = -1, node = -1, lastShot = int.MinValue, lastSideFlip = int.MinValue, sideSign = 0;
            int moveFrom = -1, stuckTo = int.MinValue, wallFrom = -1, idleFrom = -1;
            AiAngles idleAnchor = default, view = default;
            bool hasCommand = false;
            AiUserCmd last = default;
            long aliveTicks = 0;

            for (int i = 0; i < ticks.Count; i++)
            {
                int t = ticks[i];
                AiActorFrame f = frames[i];
                int dt = i > 0 ? t - ticks[i - 1] : 1;
                bool joined = i > 0 && dt <= CommandStale && frames[i - 1].Origin.DistanceTo(f.Origin) <= AiMetrics.TeleportUnitsPerTick * dt;
                aliveTicks += joined ? dt : 1;
                int span = joined ? dt : 1;

                // What happened up to this tick.
                while (nextCommand < _commands.Count && _commands[nextCommand].Tick.Value <= t)
                {
                    last = _commands[nextCommand++];
                    hasCommand = true;
                    if ((last.Buttons & Attack) != 0)
                        lastShot = last.Tick.Value;
                    int s = Math.Sign(last.SideMove);
                    if (s != 0)
                    {
                        if (sideSign != 0 && s != sideSign)
                            lastSideFlip = last.Tick.Value;
                        sideSign = s;
                    }
                }

                while (nextShot < shots.Count && shots[nextShot].Tick.Value <= t)
                {
                    AiEvent shot = shots[nextShot++];
                    lastShot = Math.Max(lastShot, shot.Tick.Value);
                    if (held.Count == 0)
                        weapon = shot.Code;   // a person's weapon: the one they last fired
                }

                while (nextHeld < held.Count && held[nextHeld].Tick <= t)
                    weapon = held[nextHeld++].Weapon;
                while (nextDecision < _decisions.Count && _decisions[nextDecision].Tick <= t)
                    node = _decisions[nextDecision++].Node;
                while (nextSight < sights.Count && sights[nextSight].Tick.Value <= t)
                {
                    AiEvent e = sights[nextSight++];
                    if (e.Kind == AiEventKind.SightGained)
                        seen.Add(e.Other);
                    else
                        seen.Remove(e.Other);
                }

                bool fresh = hasCommand && t - last.Tick.Value <= CommandStale;
                bool moving = fresh && (MathF.Abs(last.ForwardMove) >= 1f || MathF.Abs(last.SideMove) >= 1f);
                if (fresh)
                    view = last.View;
                float cadence = data.Weapons.TryGetValue(weapon, out WeaponFacts facts) && facts.CadenceSeconds > 0 ? facts.CadenceSeconds : DefaultCadenceSeconds;
                bool firing = lastShot != int.MinValue && t - lastShot <= (int)MathF.Ceiling(cadence / AiMetrics.TickSeconds - 0.001f);

                // The nearest opponent seen in reach; any opponent seen at all.
                bool anySeen = false;
                float nearest = float.MaxValue;
                float reach = weaponRange(data.WeaponNames.GetValueOrDefault(weapon, ""));
                foreach (ActorId other in seen)
                {
                    if (FrameOf(other, t) is not { } them)
                        continue;
                    anySeen = true;
                    float d = f.Eye.DistanceTo(them.Origin + (them.Eye - them.Origin) * 0.5f);
                    if (d <= reach && d < nearest)
                        nearest = d;
                }

                if (nearest != float.MaxValue)
                    InSight(nearest, firing, node, span, f.Origin);

                // Stuck: the window ending here, its every tick moving, little net.
                if (!moving || !joined)
                    moveFrom = moving ? t : -1;
                else if (moveFrom < 0)
                    moveFrom = t;
                if (moving && t - moveFrom + 1 >= StuckWindowTicks)
                {
                    int start = t - StuckWindowTicks + 1;
                    while (windowFrom + 1 < ticks.Count && ticks[windowFrom + 1] <= start)
                        windowFrom++;
                    bool dodge = anySeen && t - lastSideFlip < StuckWindowTicks;
                    if (!dodge && frames[windowFrom].Origin.DistanceTo(f.Origin) < StuckProgress)
                    {
                        int from = Math.Max(stuckTo + 1, start);
                        double seconds = (t - from + 1) * AiMetrics.TickSeconds;
                        _stuck += seconds;
                        stuckTo = t;
                        places.Add(Stuck, f.Origin, seconds, 1, actor);
                    }
                }

                // Wall bump: a move key at almost no speed, long enough.
                bool bumping = moving && joined && frames[i - 1].Origin.DistanceTo(f.Origin) / (dt * AiMetrics.TickSeconds) < WallSpeed;
                if (!bumping)
                    wallFrom = -1;
                else if (wallFrom < 0)
                    wallFrom = t;
                if (bumping)
                    CountRun(ref _wall, WallBump, wallFrom, t, WallMinTicks, span, f.Origin);

                // Idle: no key, no shot, the same look, long enough.
                bool still = fresh && !moving && !firing && joined;
                if (still && idleFrom >= 0 && Angle(idleAnchor, view) > IdleLookDeg)
                    idleFrom = -1;
                if (!still)
                    idleFrom = -1;
                else if (idleFrom < 0)
                {
                    idleFrom = t;
                    idleAnchor = view;
                }

                if (still)
                    CountRun(ref _idle, Idle, idleFrom, t, IdleTicks, span, f.Origin);
            }

            Flips();
            ActorMetrics metrics = AiMetrics.For(actor, _bot, _commands, data.Frames, data.Sights, data.Shots, data.WeaponNames);
            return new JankActor(actor, _bot, aliveTicks * AiMetrics.TickSeconds, _stuck, _wall, _idle, _branchFlips, _leafFlips, _close, _far,
                                 metrics.StrafeIntervalMs, metrics.ReversalsPerMinute, metrics.Minutes);
        }

        /// <summary>A run counts whole once it is long enough: all of it on the tick it gets there, each tick after.</summary>
        private void CountRun(ref double total, string metric, int from, int t, int minimum, int span, AiVector at)
        {
            int length = t - from + 1;
            if (length < minimum)
                return;
            double seconds = (length - span < minimum ? length : span) * AiMetrics.TickSeconds;
            total += seconds;
            places.Add(metric, at, seconds, 1, actor);
        }

        private void InSight(float nearest, bool firing, int node, int ticks, AiVector at)
        {
            bool close = nearest < CloseRange;
            var part = new JankShare(firing ? 0 : ticks, ticks);
            if (close)
                _close += part;
            else
                _far += part;
            if (!firing)
                places.Add(close ? NoShotClose : NoShotFar, at, ticks * AiMetrics.TickSeconds, 1, actor);
            if (!_bot)
                return;
            string leaf = tree?.Name(node) ?? $"node {node}";
            leaves.TryGetValue(leaf, out (JankShare Close, JankShare Far) l);
            leaves[leaf] = close ? (l.Close + part, l.Far) : (l.Close, l.Far + part);
        }

        /// <summary>The decisions as runs of one value; each return to the value before last, its excursion short, is a flip.</summary>
        private void Flips()
        {
            var leaf = new FlipRuns();
            var branch = new FlipRuns();
            foreach ((int tick, int node) in _decisions)
            {
                if (FrameOf(actor, tick) is not { } f)
                {
                    leaf.Break();
                    branch.Break();
                    continue;
                }

                if (leaf.Add(node, tick) is int leafAway)
                {
                    _leafFlips++;
                    places.Add(LeafFlip, f.Origin, leafAway * AiMetrics.TickSeconds, 1, actor);
                }

                if (tree is not null && branch.Add(tree.BranchOf(node), tick) is int branchAway)
                {
                    _branchFlips++;
                    places.Add(BranchFlip, f.Origin, branchAway * AiMetrics.TickSeconds, 1, actor);
                }
            }
        }

        private AiActorFrame? FrameOf(ActorId who, int tick)
        {
            if (!data.Frames.TryGetValue(who, out SortedList<int, AiActorFrame>? at))
                return null;
            for (int t = tick; t >= tick - FrameStale; t--)
            {
                if (at.TryGetValue(t, out AiActorFrame f))
                    return f;
            }

            return null;
        }

        private static double Angle(AiAngles a, AiAngles b)
        {
            (double dy, double dp) = AiMetrics.Delta(a, b);
            return Math.Sqrt(dy * dy + dp * dp);
        }
    }

    /// <summary>A sequence of values as runs: answers how long the excursion was when one returns.</summary>
    private sealed class FlipRuns
    {
        private int _before = int.MinValue, _current = int.MinValue, _since;

        public void Break()
        {
            _before = int.MinValue;
            _current = int.MinValue;
        }

        public int? Add(int value, int tick)
        {
            if (value == _current)
                return null;
            int away = tick - _since;
            bool returned = _current != int.MinValue && value == _before && away <= FlipTicks;
            _before = _current;
            _current = value;
            _since = tick;
            return returned ? away : null;
        }
    }

    /// <summary>What <c>ai-replay jank</c> prints.</summary>
    public static string Table(JankReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine(JankScore.Formula());
        sb.AppendLine("actor                 kind   alive min  score | stuck s/min | wall s/min | flips/min branch leaf | no shot <400 >=400 | strafe ms | yaw rev/min | idle s/min");
        foreach (JankActor a in report.Actors)
            Row(sb, $"{a.Actor.Slot}#{a.Actor.Serial}", a);
        Row(sb, "total", report.Bots);
        Row(sb, "total", report.People);

        sb.AppendLine("in-sight not firing, bots, by leaf: ticks of no shot over ticks with an opponent seen in weapon range");
        foreach (JankLeaf l in report.Leaves.OrderByDescending(l => l.Close.NotFiring + l.Far.NotFiring))
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {l.Leaf,-22} <400 {Share(l.Close),-20} >=400 {Share(l.Far)}"));

        sb.AppendLine("worst places:");
        foreach (string metric in new[] { Stuck, WallBump, BranchFlip, LeafFlip, NoShotClose, NoShotFar, Idle })
        {
            if (!report.Places.TryGetValue(metric, out IReadOnlyList<JankSpot>? spots))
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {metric}: none"));
                continue;
            }

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {metric}:"));
            foreach (JankSpot s in spots)
            {
                string how = metric is BranchFlip or LeafFlip ? $"{s.Count} flips, {s.Seconds:F1} s away" : $"{s.Seconds:F1} s";
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    ({s.Position.X:F0} {s.Position.Y:F0} {s.Position.Z:F0}) {how}, {string.Join(" ", s.Actors.Select(a => $"{a.Slot}#{a.Serial}"))}"));
            }
        }

        // The one line a gate reads.
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"jank: bots score {JankScore.Of(report.Bots):F1} over {report.Bots.AliveMinutes:F1} bot-minutes; people score {JankScore.Of(report.People):F1} over {report.People.AliveMinutes:F1}"));
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string name, JankActor a)
        => sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{name,-21} {(a.Bot ? "bot" : "human"),-5} {a.AliveMinutes,10:F2} {JankScore.Of(a),6:F1} | {a.StuckPerMinute,11:F2} | {a.WallBumpPerMinute,10:F2} | {(a.Bot ? a.BranchFlipsPerMinute.ToString("F1", CultureInfo.InvariantCulture) : "-"),16} {(a.Bot ? a.LeafFlipsPerMinute.ToString("F1", CultureInfo.InvariantCulture) : "-"),4} | {Pct(a.Close.Share),12} {Pct(a.Far.Share),5} | {F0(AiMetrics.Quantile(a.StrafeIntervalMs, 0.5)),9} | {a.ReversalsPerMinute,11:F0} | {a.IdlePerMinute,10:F2}"));

    private static string Share(JankShare s) => string.Create(CultureInfo.InvariantCulture, $"{s.NotFiring}/{s.Ticks} {Pct(s.Share)}");

    private static string Pct(double share) => double.IsNaN(share) ? "-" : string.Create(CultureInfo.InvariantCulture, $"{share * 100:F0}%");

    private static string F0(double v) => double.IsNaN(v) ? "-" : v.ToString("F0", CultureInfo.InvariantCulture);
}
