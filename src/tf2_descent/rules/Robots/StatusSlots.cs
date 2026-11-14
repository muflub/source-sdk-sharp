using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Robots;

/// <summary>
/// One status.json row's place on a robot (a slot a row, in table order): its stacks (0 when the
/// robot does not carry it), the tick it ends and its next damage tick, the hero seat that last
/// applied it (0 for none), and the order it was last applied in (what the robot's statuses are
/// listed by, as <see cref="StatusRules"/> lists them).
/// </summary>
public struct StatusSlot
{
    public int Stacks;
    public long EndTick;
    public long NextDamageTick;
    public int Applier;
    public long Applied;

    public readonly bool Carried => Stacks > 0;
}

/// <summary>
/// The damage one status owes at a tick, before the robot's resistance: the status's row, the
/// amount (every damage tick due, each a stack's share times the stacks), how many damage ticks
/// that is, and the hero seat that applied it (0 for none).
/// </summary>
public readonly record struct StatusDue(int Row, double Amount, int Ticks, int Applier);

/// <summary>
/// <see cref="StatusRules"/> over fixed slots (plan_descent_logic HO8, 3.10's zero-allocation
/// gate): the same apply, stacking, tick and expiry, with nothing allocated, so the roster can
/// keep a robot's statuses as a run of <see cref="StatusSlot"/> and tick 48 bodies a server tick
/// for nothing. Two things <see cref="StatusRules"/> has no word for: a skill names its own
/// duration (EMP Charge stuns 1 to 2 s; status.json's stun is 2 s), so an application may carry
/// one, the row's when it does not; and each slot keeps the hero seat that applied it, so a
/// damage tick is that hero's hit. Every number is status.json's.
/// </summary>
public sealed class StatusBook
{
    private readonly StatusRow[] _rows;

    public StatusBook(DescentContent content) => _rows = [.. content.Status.Rows];

    /// <summary>Rows, and so slots a robot needs.</summary>
    public int Count => _rows.Length;

    public StatusRow Row(int row) => _rows[row];

    /// <summary>The slot of status <paramref name="id"/>; -1 for no such status.</summary>
    public int RowOf(string id)
    {
        for (int r = 0; r < _rows.Length; r++)
            if (_rows[r].Id == id)
                return r;
        return -1;
    }

    /// <summary>
    /// Applies <paramref name="row"/> at <paramref name="now"/> to a robot immune to
    /// <paramref name="immunity"/> for <paramref name="seconds"/> (the row's duration when 0 or
    /// less), by <paramref name="applier"/>, as application number <paramref name="stamp"/>.
    /// The stacking is <see cref="StatusRules.Apply"/>'s: a fresh status starts with one stack;
    /// refresh moves the end, stack adds one to the maximum and moves the end, longest keeps the
    /// later end; a damaging status of the type the robot is immune to does not take.
    /// </summary>
    public StatusApplied Apply(Span<StatusSlot> slots, int row, long now, double ticksPerSecond, string? immunity, double seconds,
                               int applier, long stamp)
    {
        var r = _rows[row];
        if (r.DamageType is { } type && type == immunity)
            return StatusApplied.Immune;

        long end = now + StatusRules.Ticks(seconds > 0 ? seconds : r.DurationSeconds, ticksPerSecond);
        ref var s = ref slots[row];
        StatusApplied what;
        if (!s.Carried || s.EndTick <= now)
        {
            s.Stacks = 1;
            s.EndTick = end;
            s.NextDamageTick = r.DamagePerSecond > 0 ? now + StatusRules.Ticks(r.TickSeconds, ticksPerSecond) : long.MaxValue;
            what = StatusApplied.Applied;
        }
        else
        {
            switch (r.Stacking)
            {
                case StatusStacking.Refresh:
                    s.EndTick = end;
                    what = StatusApplied.Refreshed;
                    break;
                case StatusStacking.Stack:
                    s.EndTick = end;
                    s.Stacks = Math.Min(r.MaxStacks, s.Stacks + 1);
                    what = StatusApplied.Stacked;
                    break;
                case StatusStacking.Longest:
                    s.EndTick = Math.Max(s.EndTick, end);
                    what = StatusApplied.Refreshed;
                    break;
                default:
                    throw new InvalidOperationException($"status '{r.Id}' has no stacking policy");
            }
        }

        s.Applier = applier;
        s.Applied = stamp;
        return what;
    }

    /// <summary>
    /// Runs every damage tick due by <paramref name="now"/> (a tick falling exactly on the end still
    /// lands) into <paramref name="due"/>, one entry a damaging status, then drops the statuses
    /// that have ended. Answers how many entries it wrote. <paramref name="due"/> must hold
    /// <see cref="Count"/> entries.
    /// </summary>
    public int Tick(Span<StatusSlot> slots, long now, double ticksPerSecond, Span<StatusDue> due)
    {
        int n = 0;
        for (int row = 0; row < _rows.Length; row++)
        {
            ref var s = ref slots[row];
            if (!s.Carried)
                continue;

            var r = _rows[row];
            if (r.DamagePerSecond > 0)
            {
                long step = Math.Max(1, StatusRules.Ticks(r.TickSeconds, ticksPerSecond));
                int ticks = 0;
                while (s.NextDamageTick <= now && s.NextDamageTick <= s.EndTick)
                {
                    ticks++;
                    s.NextDamageTick += step;
                }

                if (ticks > 0)
                    due[n++] = new StatusDue(row, r.DamagePerSecond * r.TickSeconds * s.Stacks * ticks, ticks, s.Applier);
            }

            if (s.EndTick <= now)
                s = default;
        }

        return n;
    }

    /// <summary>The next tick a status ends or deals damage; <see cref="long.MaxValue"/> with none.</summary>
    public long NextDue(ReadOnlySpan<StatusSlot> slots)
    {
        long due = long.MaxValue;
        for (int row = 0; row < slots.Length; row++)
            if (slots[row].Carried)
                due = Math.Min(due, Math.Min(slots[row].EndTick, slots[row].NextDamageTick));
        return due;
    }

    /// <summary>The move speed multiplier at <paramref name="now"/>: the strongest slow (a stun is a full stop).</summary>
    public double SpeedMultiplier(ReadOnlySpan<StatusSlot> slots, long now)
    {
        double slow = 0;
        for (int row = 0; row < slots.Length; row++)
            if (slots[row].Carried && slots[row].EndTick > now)
                slow = Math.Max(slow, _rows[row].SlowPct);
        return 1 - slow / 100;
    }

    /// <summary>The multiplier on damage taken at <paramref name="now"/>: each status's share, multiplied (marked: +35 %).</summary>
    public double DamageTakenMultiplier(ReadOnlySpan<StatusSlot> slots, long now)
    {
        double m = 1;
        for (int row = 0; row < slots.Length; row++)
            if (slots[row].Carried && slots[row].EndTick > now)
                m *= 1 + _rows[row].DamageTakenPct / 100;
        return m;
    }

    /// <summary>Whether a status stuns at <paramref name="now"/>: the robot can neither move nor act.</summary>
    public bool Stunned(ReadOnlySpan<StatusSlot> slots, long now)
    {
        for (int row = 0; row < slots.Length; row++)
            if (slots[row].Carried && slots[row].EndTick > now && _rows[row].Stuns)
                return true;
        return false;
    }

    /// <summary>A bit a carried status, by row (the first 32 rows).</summary>
    public static int Bits(ReadOnlySpan<StatusSlot> slots)
    {
        int bits = 0;
        for (int row = 0; row < slots.Length && row < 32; row++)
            if (slots[row].Carried)
                bits |= 1 << row;
        return bits;
    }

    /// <summary>The slots as <see cref="StatusRules"/>' value, in the order they were last applied. Allocates.</summary>
    public RobotStatuses View(ReadOnlySpan<StatusSlot> slots)
    {
        var active = new List<(long Applied, ActiveStatus Status)>();
        for (int row = 0; row < slots.Length; row++)
        {
            var s = slots[row];
            if (s.Carried)
                active.Add((s.Applied, new ActiveStatus(_rows[row].Id, s.Stacks, s.EndTick, s.NextDamageTick)));
        }

        return active.Count == 0 ? RobotStatuses.None : new([.. active.OrderBy(a => a.Applied).Select(a => a.Status)]);
    }

    /// <summary>
    /// Fills <paramref name="slots"/> from <paramref name="statuses"/> (a body handing its state
    /// back), stamped in their order from <paramref name="firstStamp"/>. A status the slots held
    /// keeps its applier; one they did not has none. Answers the next stamp.
    /// </summary>
    public long Load(Span<StatusSlot> slots, RobotStatuses statuses, long firstStamp)
    {
        Span<int> appliers = stackalloc int[slots.Length];
        for (int row = 0; row < slots.Length; row++)
            appliers[row] = slots[row].Carried ? slots[row].Applier : 0;
        slots.Clear();
        long stamp = firstStamp;
        foreach (var a in statuses.Active)
        {
            int row = RowOf(a.Id);
            if (row < 0)
                throw new ArgumentException($"'{a.Id}' is not a status", nameof(statuses));
            slots[row] = new StatusSlot
            {
                Stacks = a.Stacks, EndTick = a.EndTick, NextDamageTick = a.NextDamageTick, Applier = appliers[row], Applied = stamp++,
            };
        }

        return stamp;
    }
}
