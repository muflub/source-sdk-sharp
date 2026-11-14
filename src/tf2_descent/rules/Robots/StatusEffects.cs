using System.Collections.Immutable;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Robots;

/// <summary>A status on a robot: its stacks, when it ends, and when its next damage tick falls (ticks of the server).</summary>
public sealed record ActiveStatus(string Id, int Stacks, long EndTick, long NextDamageTick);

/// <summary>Every status a robot carries (the roster row's "status effects with their end ticks", section 3.3).</summary>
public sealed record RobotStatuses(ImmutableArray<ActiveStatus> Active)
{
    public static RobotStatuses None { get; } = new([]);

    public ActiveStatus? Of(string id) => Active.FirstOrDefault(a => a.Id == id);
}

/// <summary>What an application did.</summary>
public enum StatusApplied
{
    Applied,
    Refreshed,
    Stacked,
    /// <summary>The robot is immune to the status's damage type (a fire-immune robot cannot burn).</summary>
    Immune,
}

/// <summary>Damage a status dealt on a tick.</summary>
public sealed record StatusDamage(string Status, string DamageType, double Amount, long Tick);

/// <summary>A tick: the statuses after it and the damage due by then.</summary>
public sealed record StatusTick(RobotStatuses After, ImmutableArray<StatusDamage> Damage);

/// <summary>
/// Robot status effects (default E9): burn, bleed, stun, slow and marked, as pure rules over a
/// robot's statuses at a server tick: apply (with the status's stacking policy), tick (its
/// damage, a stack at a time), expiry, and what they do to speed and damage taken. Every
/// number is status.json's; the tick rate is the engine's, an input.
/// </summary>
public sealed class StatusRules
{
    private readonly DescentContent _content;

    public StatusRules(DescentContent content) => _content = content;

    public StatusRow Row(string id) => _content.Status[id];

    public static long Ticks(double seconds, double ticksPerSecond) => (long)Math.Ceiling(seconds * ticksPerSecond - 1e-9);

    /// <summary>
    /// Applies <paramref name="id"/> at <paramref name="now"/> to a robot immune to
    /// <paramref name="immunity"/> (or nothing). Refresh: the end moves to now + duration.
    /// Stack: one more stack to the maximum, and the end moves. Longest: the later end stands.
    /// A damaging status of the type the robot is immune to does not take.
    /// </summary>
    public (RobotStatuses After, StatusApplied What) Apply(RobotStatuses statuses, string id, long now, double ticksPerSecond, string? immunity)
    {
        var row = Row(id);
        if (row.DamageType is { } type && type == immunity) return (statuses, StatusApplied.Immune);
        long end = now + Ticks(row.DurationSeconds, ticksPerSecond);
        var current = statuses.Of(id);
        if (current is null || current.EndTick <= now)
        {
            var fresh = new ActiveStatus(id, 1, end, row.DamagePerSecond > 0 ? now + Ticks(row.TickSeconds, ticksPerSecond) : long.MaxValue);
            return (Replace(statuses, id, fresh), StatusApplied.Applied);
        }
        var next = row.Stacking switch
        {
            StatusStacking.Refresh => (current with { EndTick = end }, StatusApplied.Refreshed),
            StatusStacking.Stack => (current with { EndTick = end, Stacks = Math.Min(row.MaxStacks, current.Stacks + 1) }, StatusApplied.Stacked),
            StatusStacking.Longest => (current with { EndTick = Math.Max(current.EndTick, end) }, StatusApplied.Refreshed),
            _ => throw new InvalidOperationException($"status '{id}' has no stacking policy"),
        };
        return (Replace(statuses, id, next.Item1), next.Item2);
    }

    /// <summary>
    /// Runs every damage tick due by <paramref name="now"/> (a tick falling exactly on the end
    /// still lands), then drops the statuses that have ended.
    /// </summary>
    public StatusTick Tick(RobotStatuses statuses, long now, double ticksPerSecond)
    {
        var damage = ImmutableArray.CreateBuilder<StatusDamage>();
        var kept = ImmutableArray.CreateBuilder<ActiveStatus>();
        foreach (var a in statuses.Active)
        {
            var row = Row(a.Id);
            var s = a;
            if (row.DamagePerSecond > 0)
            {
                long step = Math.Max(1, Ticks(row.TickSeconds, ticksPerSecond));
                while (s.NextDamageTick <= now && s.NextDamageTick <= s.EndTick)
                {
                    damage.Add(new StatusDamage(a.Id, row.DamageType!, row.DamagePerSecond * row.TickSeconds * s.Stacks, s.NextDamageTick));
                    s = s with { NextDamageTick = s.NextDamageTick + step };
                }
            }
            if (s.EndTick > now) kept.Add(s);
        }
        return new StatusTick(new RobotStatuses(kept.ToImmutable()), damage.ToImmutable());
    }

    /// <summary>The robot's move speed multiplier at <paramref name="now"/>: the strongest slow (a stun is a full stop).</summary>
    public double SpeedMultiplier(RobotStatuses statuses, long now) =>
        1 - statuses.Active.Where(a => a.EndTick > now).Select(a => Row(a.Id).SlowPct).DefaultIfEmpty(0).Max() / 100;

    /// <summary>The multiplier on damage the robot takes at <paramref name="now"/> (marked: +35 %).</summary>
    public double DamageTakenMultiplier(RobotStatuses statuses, long now) =>
        statuses.Active.Where(a => a.EndTick > now).Aggregate(1.0, (m, a) => m * (1 + Row(a.Id).DamageTakenPct / 100));

    /// <summary>Whether the robot is stunned at <paramref name="now"/>: it can neither move nor act.</summary>
    public bool Stunned(RobotStatuses statuses, long now) => statuses.Active.Any(a => a.EndTick > now && Row(a.Id).Stuns);

    private static RobotStatuses Replace(RobotStatuses s, string id, ActiveStatus status) =>
        new([.. s.Active.Where(a => a.Id != id), status]);
}
