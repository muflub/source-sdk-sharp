using System.Globalization;
using System.Text;

using SourceSharp.Server.Interface;

namespace Debug.Api;

/// <summary>What one evaluation of a wait found.</summary>
/// <param name="State">Met, not yet, or failed (a wait that can never be met, such as its entity being gone).</param>
/// <param name="Value">What was read, as a JSON value, for the reply.</param>
/// <param name="Seq">The event sequence it was met at, for an event wait; else null.</param>
/// <param name="Status">For a failure, the status the reply names: "failed" unless a condition knows better ("stuck").</param>
public readonly record struct WaitCheck(WaitState State, string Value, long? Seq = null, string Status = "failed")
{
    public static WaitCheck Met(string value, long? seq = null) => new(WaitState.Met, value, seq);
    public static WaitCheck NotYet(string value) => new(WaitState.NotYet, value);
    public static WaitCheck Failed(string why) => new(WaitState.Failed, DebugApiServer.Quote(why));

    /// <summary>A failure with its own status and a JSON value, for a reply that says what was read.</summary>
    public static WaitCheck FailedAs(string status, string value) => new(WaitState.Failed, value, null, status);
}

public enum WaitState { NotYet, Met, Failed }

/// <summary>What a condition reads, on the game thread, once per server tick.</summary>
public sealed class WaitContext(IDebugServerHooks hooks, EntityEventLog events, Action noteEventInterest)
{
    public IDebugServerHooks Hooks { get; } = hooks;
    public IDebugWorld World => Hooks.World;
    public EntityEventLog Events { get; } = events;

    /// <summary>Keeps the entity sampler running, so spawns and removals become events while a wait wants them.</summary>
    public void NoteEventInterest() => noteEventInterest();

    /// <summary>The tick being evaluated.</summary>
    public int Tick { get; internal set; }

    /// <summary>Ticks since this wait's first evaluation.</summary>
    public int Waited { get; internal set; }
}

/// <summary>
/// One condition a <c>/wait</c> holds. Evaluated on the game thread, once per
/// server tick, by <see cref="TickWaits"/>; the first evaluation is the tick
/// the wait was registered at, so a condition already true is met at once with
/// <c>waitedTicks</c> 0.
/// </summary>
public abstract class WaitCondition
{
    /// <summary>What is being waited for, for the reply.</summary>
    public abstract string Describe();

    public abstract WaitCheck Check(WaitContext context);

    internal static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary><c>ticks=N</c>: N server ticks, which replaces a wall-clock sleep.</summary>
public sealed class TicksCondition(int ticks) : WaitCondition
{
    public override string Describe() => $"{ticks} tick(s)";

    public override WaitCheck Check(WaitContext c)
        => c.Waited >= ticks ? WaitCheck.Met(c.Waited.ToString(CultureInfo.InvariantCulture))
                             : WaitCheck.NotYet(c.Waited.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// A comparison, numeric when both sides are numbers and a case-insensitive
/// string match otherwise. <c>changed</c> compares against the value the
/// first evaluation read.
/// </summary>
public enum WaitOp { Eq, Ne, Lt, Le, Gt, Ge, Changed }

internal static class WaitCompare
{
    public static bool TryParse(string? text, out WaitOp op)
    {
        op = WaitOp.Eq;
        switch (text?.ToLowerInvariant())
        {
            case null or "" or "eq" or "==": op = WaitOp.Eq; return true;
            case "ne" or "!=": op = WaitOp.Ne; return true;
            case "lt" or "<": op = WaitOp.Lt; return true;
            case "le" or "<=": op = WaitOp.Le; return true;
            case "gt" or ">": op = WaitOp.Gt; return true;
            case "ge" or ">=": op = WaitOp.Ge; return true;
            case "changed": op = WaitOp.Changed; return true;
            default: return false;
        }
    }

    public static bool Holds(WaitOp op, string actual, string? wanted, double eps)
    {
        bool numeric = double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                     & double.TryParse(wanted, NumberStyles.Float, CultureInfo.InvariantCulture, out double w);

        if (!numeric)
        {
            bool same = string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                WaitOp.Eq => same,
                WaitOp.Ne or WaitOp.Changed => !same,
                _ => false,
            };
        }

        return op switch
        {
            WaitOp.Eq => Math.Abs(a - w) <= eps,
            WaitOp.Ne or WaitOp.Changed => Math.Abs(a - w) > eps,
            WaitOp.Lt => a < w,
            WaitOp.Le => a <= w,
            WaitOp.Gt => a > w,
            WaitOp.Ge => a >= w,
            _ => false,
        };
    }
}

/// <summary>
/// Shared by the conditions about one entity: the selector resolves to a
/// handle once, and the handle is what is followed after that. A name that
/// matches nothing yet is retried each tick until it does.
/// </summary>
public abstract class EntityCondition(EntitySelector selector) : WaitCondition
{
    protected EntitySelector Selector { get; } = selector;

    /// <summary>The handle followed, once resolved; else <see cref="IDebugWorld.NoEntity"/>.</summary>
    protected uint Pinned { get; private set; } = IDebugWorld.NoEntity;

    /// <summary>Resolves (once) and answers whether the followed entity is live now.</summary>
    protected bool Live(WaitContext c)
    {
        if (Pinned == IDebugWorld.NoEntity)
        {
            Pinned = Selector.Resolve(c.World);
            return Pinned != IDebugWorld.NoEntity;
        }

        return c.World.IsValid(Pinned);
    }

    protected bool EverResolved => Pinned != IDebugWorld.NoEntity;
}

/// <summary>
/// <c>field=F</c>: one of the entity's readings, or a networked payload field
/// by name, compared with <c>op</c> and <c>value</c>.
/// </summary>
public sealed class FieldCondition(EntitySelector selector, string field, WaitOp op, string? wanted, double eps)
    : EntityCondition(selector)
{
    private string? _first;

    public override string Describe()
        => $"{Selector} {field} {op.ToString().ToLowerInvariant()}{(op == WaitOp.Changed ? "" : " " + wanted)}";

    public override WaitCheck Check(WaitContext c)
    {
        if (!Live(c))
        {
            return EverResolved ? WaitCheck.Failed($"{Selector} was removed before {field} matched")
                                : WaitCheck.NotYet("null");
        }

        string? actual = Read(c, Pinned, field);
        if (actual is null)
            return WaitCheck.Failed($"{Selector} has no field '{field}'");

        _first ??= actual;

        bool holds = WaitCompare.Holds(op, actual, op == WaitOp.Changed ? _first : wanted, eps);
        string value = DebugApiServer.Quote(actual);
        return holds ? WaitCheck.Met(value) : WaitCheck.NotYet(value);
    }

    /// <summary>
    /// A reading by name: the entity's own (origin.x, angles.y, velocity.z,
    /// speed, health, asleep, ground, classname, name), else a field of its
    /// networked payload. Null when it has neither.
    /// </summary>
    internal static string? Read(WaitContext c, uint entity, string field)
    {
        var w = c.World;
        switch (field.ToLowerInvariant())
        {
            case "origin.x": return Num(w.GetOrigin(entity).X);
            case "origin.y": return Num(w.GetOrigin(entity).Y);
            case "origin.z": return Num(w.GetOrigin(entity).Z);
            case "angles.x": return Num(w.GetAngles(entity).X);
            case "angles.y": return Num(w.GetAngles(entity).Y);
            case "angles.z": return Num(w.GetAngles(entity).Z);
            case "velocity.x": return Num(w.GetVelocity(entity).X);
            case "velocity.y": return Num(w.GetVelocity(entity).Y);
            case "velocity.z": return Num(w.GetVelocity(entity).Z);
            case "speed": return Num(DebugApiServer.Speed(w.GetVelocity(entity)));
            case "health": return w.GetHealth(entity).ToString(CultureInfo.InvariantCulture);
            case "asleep": return w.IsAsleep(entity) is { } a ? (a ? "true" : "false") : "null";
            case "ground": return w.GroundIndex(entity) is { } g ? g.ToString(CultureInfo.InvariantCulture) : "null";
            case "classname": return w.GetClassName(entity);
            case "name": return w.GetName(entity);
        }

        var payload = c.Hooks.NetSchema.Payload(w.GetIndex(entity));
        if (!payload.Found)
            return null;

        foreach (var decoded in payload.Decoded)
        {
            if (!string.Equals(decoded.Field.Name, field, StringComparison.OrdinalIgnoreCase) || !decoded.Present)
                continue;

            return decoded.Number is { } n ? Num(n) : decoded.Value;
        }

        return null;
    }
}

/// <summary><c>exists=1</c>: until the selector, or any entity of <c>class</c>, names something live.</summary>
public sealed class ExistsCondition(EntitySelector? selector, string? className) : WaitCondition
{
    public override string Describe() => selector is not null ? $"{selector} exists" : $"a {className} exists";

    public override WaitCheck Check(WaitContext c)
    {
        uint found = selector is not null
            ? selector.Resolve(c.World)
            : c.World.FindAllByClassname(className!).DefaultIfEmpty(IDebugWorld.NoEntity).First();

        return found == IDebugWorld.NoEntity
            ? WaitCheck.NotYet("null")
            : WaitCheck.Met($"{{\"handle\":{found},\"index\":{c.World.GetIndex(found)}}}");
    }
}

/// <summary>
/// <c>gone=1</c>: until the entity the selector named AT THE START is no longer
/// live. Nothing matching at the start is met at once and says so.
/// </summary>
public sealed class GoneCondition(EntitySelector selector) : EntityCondition(selector)
{
    private bool _checked;

    public override string Describe() => $"{Selector} is gone";

    public override WaitCheck Check(WaitContext c)
    {
        bool first = !_checked;
        _checked = true;

        if (Live(c))
            return WaitCheck.NotYet($"{{\"handle\":{Pinned}}}");

        return WaitCheck.Met(first && !EverResolved ? "\"absent at the start\"" : $"{{\"handle\":{Pinned}}}");
    }
}

/// <summary>
/// <c>event=K</c>: until an event matching the filter arrives at or after
/// <c>since</c> (default: the head when the wait was made, so only what
/// happens from now on).
/// </summary>
public sealed class EventCondition(EventFilter filter, long since) : WaitCondition
{
    public override string Describe() => $"an event from seq {since}";

    public override WaitCheck Check(WaitContext c)
    {
        c.NoteEventInterest();

        var hit = c.Events.Since(since, 1, filter, out _);
        if (hit.Count == 0)
            return WaitCheck.NotYet("null");

        var json = new StringBuilder();
        DebugApiServer.AppendEventJson(json, hit[0]);
        return WaitCheck.Met(json.ToString(), hit[0].Sequence);
    }
}

/// <summary>
/// <c>settled=1</c>: until the entity's speed has stayed at or under
/// <c>eps</c> for <c>for</c> consecutive ticks, or its physics object reports
/// asleep. With <c>asleep=1</c>, only the sleep counts.
/// </summary>
public sealed class SettledCondition(EntitySelector selector, double eps, int forTicks, bool asleepOnly)
    : EntityCondition(selector)
{
    private int _still;

    public override string Describe()
        => asleepOnly ? $"{Selector} asleep" : $"{Selector} under {Num(eps)} u/s for {forTicks} tick(s), or asleep";

    public override WaitCheck Check(WaitContext c)
    {
        if (!Live(c))
        {
            return EverResolved ? WaitCheck.Failed($"{Selector} was removed before it settled")
                                : WaitCheck.NotYet("null");
        }

        double speed = DebugApiServer.Speed(c.World.GetVelocity(Pinned));
        bool? asleep = c.World.IsAsleep(Pinned);
        _still = speed <= eps ? _still + 1 : 0;

        string value = $"{{\"speed\":{Num(speed)},\"asleep\":{(asleep is { } a ? (a ? "true" : "false") : "null")},\"stillTicks\":{_still}}}";

        bool met = asleep == true || (!asleepOnly && _still >= forTicks);
        return met ? WaitCheck.Met(value) : WaitCheck.NotYet(value);
    }
}

/// <summary>
/// A teleported player has come to rest: standing on something (when it keeps
/// a ground at all) and at or under <c>eps</c> for <c>for</c> consecutive
/// ticks. The tick the wait starts on does not count -- the teleport and the
/// first check can share a tick, and the ground the player had before the move
/// is still the one it reports then.
/// </summary>
public sealed class LandedCondition(uint entity, double eps, int forTicks) : WaitCondition
{
    /// <summary>
    /// Off the ground and not moving for this many ticks is STUCK: a player in
    /// free air falls (gravity is 12 u/s a tick at the 15 ms tick), so one that
    /// neither falls nor stands is held -- by solid it was teleported into
    /// (stock's CheckStuck only nudges 2 units and PlayerMove returns early
    /// while stuck), or by no usercmds arriving to move it. Reported as its
    /// own status instead of a timeout that reads like "it is still falling".
    /// </summary>
    public const int StuckTicks = 20;

    private int _still;
    private int _hung;

    public override string Describe() => $"handle {entity} on the ground under {Num(eps)} u/s for {forTicks} tick(s)";

    public override WaitCheck Check(WaitContext c)
    {
        if (!c.World.IsValid(entity))
            return WaitCheck.Failed($"handle {entity} was removed before it landed");

        var origin = c.World.GetOrigin(entity);
        double speed = DebugApiServer.Speed(c.World.GetVelocity(entity));
        int? ground = c.World.GroundIndex(entity);

        bool resting = speed <= eps && ground is not -1;
        _still = c.Waited > 0 && resting ? _still + 1 : 0;
        _hung = c.Waited > 0 && ground == -1 && speed <= eps ? _hung + 1 : 0;

        string value = $"{{\"origin\":[{Num(origin.X)},{Num(origin.Y)},{Num(origin.Z)}],\"speed\":{Num(speed)}," +
                       $"\"ground\":{(ground is { } g ? g.ToString(CultureInfo.InvariantCulture) : "null")},\"stillTicks\":{_still}," +
                       $"\"hungTicks\":{_hung}}}";

        if (_hung >= StuckTicks)
            return WaitCheck.FailedAs("stuck", value);

        return _still >= forTicks ? WaitCheck.Met(value) : WaitCheck.NotYet(value);
    }
}

/// <summary>Every one of a set of handles is no longer live: a removal has happened.</summary>
public sealed class AllGoneCondition(IReadOnlyList<uint> handles) : WaitCondition
{
    public override string Describe() => $"{handles.Count} entit{(handles.Count == 1 ? "y" : "ies")} gone";

    public override WaitCheck Check(WaitContext c)
    {
        int left = handles.Count(c.World.IsValid);
        string value = left.ToString(CultureInfo.InvariantCulture);
        return left == 0 ? WaitCheck.Met(value) : WaitCheck.NotYet(value);
    }
}

/// <summary><c>census=N</c>: until the count of live entities of <c>class</c> (default all) compares with N.</summary>
public sealed class CensusCondition(string className, WaitOp op, int count) : WaitCondition
{
    public override string Describe() => $"census of {className} {op.ToString().ToLowerInvariant()} {count}";

    public override WaitCheck Check(WaitContext c)
    {
        int now = c.World.FindAllByClassname(className).Count;
        string value = now.ToString(CultureInfo.InvariantCulture);
        return WaitCompare.Holds(op, value, count.ToString(CultureInfo.InvariantCulture), 0)
            ? WaitCheck.Met(value) : WaitCheck.NotYet(value);
    }
}

/// <summary><c>io=empty</c>: until the entity I/O queue has nothing pending.</summary>
public sealed class IoEmptyCondition : WaitCondition
{
    public override string Describe() => "the I/O queue is empty";

    public override WaitCheck Check(WaitContext c)
    {
        int pending = c.Hooks.Status.Counters().Io.Pending;
        string value = pending.ToString(CultureInfo.InvariantCulture);
        return pending == 0 ? WaitCheck.Met(value) : WaitCheck.NotYet(value);
    }
}

/// <summary>
/// The waits in flight. Registered from the listener's threads, evaluated by
/// <see cref="Advance"/> on the game thread, once per server tick.
///
/// <para>
/// NOT A BLOCKED GAME THREAD. A wait is a check queued here; the game thread
/// runs each check once per tick inside <c>Pump</c> and moves on, and the
/// request that made it waits on its own thread for the answer. The game never
/// waits on a caller.
/// </para>
///
/// <para>
/// ONCE PER TICK, NOT PER PUMP. Pump runs twice a frame (the managed frame and
/// the engine's think) and also at the main menu, where no tick passes; a wait
/// counts server ticks, so a check runs when the tick has moved -- or when it
/// is new, which is its first evaluation.
/// </para>
/// </summary>
public sealed class TickWaits
{
    /// <summary>One wait: its condition, its budget, and how it ended.</summary>
    public sealed class Waiter(WaitCondition condition, int timeoutTicks)
    {
        public WaitCondition Condition { get; } = condition;
        public int TimeoutTicks { get; } = timeoutTicks;
        public int StartTick { get; internal set; } = -1;
        public int EndTick { get; internal set; } = -1;
        public string Status { get; private set; } = "waiting";
        public WaitCheck Last { get; internal set; } = WaitCheck.NotYet("null");
        public ManualResetEventSlim Done { get; } = new(false);
        private int _ended;

        /// <summary>Ends it once; a second ending (a wall cap racing a tick) is refused.</summary>
        internal bool End(string status)
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0)
                return false;

            Status = status;
            Done.Set();
            return true;
        }

        public bool Ended => Volatile.Read(ref _ended) != 0;
    }

    /// <summary>More than this many waits at once is refused rather than queued without bound.</summary>
    public const int MaxWaiters = 64;

    private readonly object _lock = new();
    private readonly List<Waiter> _waiting = new();
    private int _lastTick = int.MinValue;

    public int Count
    {
        get { lock (_lock) return _waiting.Count; }
    }

    /// <summary>Registers a wait, or null when <see cref="MaxWaiters"/> are already waiting.</summary>
    public Waiter? Add(WaitCondition condition, int timeoutTicks)
    {
        var waiter = new Waiter(condition, timeoutTicks);
        lock (_lock)
        {
            if (_waiting.Count >= MaxWaiters)
                return null;

            _waiting.Add(waiter);
        }

        return waiter;
    }

    /// <summary>Takes a wait out: its caller stopped waiting (the wall-clock cap).</summary>
    public void Remove(Waiter waiter)
    {
        lock (_lock)
            _waiting.Remove(waiter);
    }

    /// <summary>
    /// Evaluates the waits for this tick, on the game thread. A wait new since
    /// the last call is evaluated even when the tick has not moved: that is its
    /// start, and a condition already true is answered without waiting a tick.
    /// </summary>
    public void Advance(WaitContext context, int tick)
    {
        Waiter[] snapshot;
        lock (_lock)
        {
            if (_waiting.Count == 0)
            {
                _lastTick = tick;
                return;
            }

            snapshot = _waiting.ToArray();
        }

        bool ticked = tick != _lastTick;
        _lastTick = tick;

        foreach (var waiter in snapshot)
        {
            if (waiter.Ended)
                continue;

            bool fresh = waiter.StartTick < 0;
            if (!fresh && !ticked)
                continue;

            if (fresh)
                waiter.StartTick = tick;

            context.Tick = tick;
            context.Waited = tick - waiter.StartTick;

            WaitCheck check;
            try
            {
                check = waiter.Condition.Check(context);
            }
            catch (Exception e)
            {
                check = WaitCheck.Failed("the check threw: " + e.Message);
            }

            waiter.Last = check;
            waiter.EndTick = tick;

            string? ending = check.State switch
            {
                WaitState.Met => "met",
                WaitState.Failed => check.Status,
                _ when context.Waited >= waiter.TimeoutTicks => "timeout",
                _ => null,
            };

            if (ending is not null && waiter.End(ending))
                Remove(waiter);
        }
    }

    /// <summary>Ends every wait: the game thread is going away (shutdown or reload).</summary>
    public void CancelAll()
    {
        Waiter[] all;
        lock (_lock)
        {
            all = _waiting.ToArray();
            _waiting.Clear();
        }

        foreach (var waiter in all)
            waiter.End("cancelled");
    }
}
