using System.Collections.Immutable;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Death;

/// <summary>Where a party member stands: on its feet, down and bleeding out, or dead (R5 applies).</summary>
public enum Standing
{
    Up,
    Down,
    Dead,
}

/// <summary>
/// One party member in the downed machine. <see cref="DownsThisLevel"/> counts the downs
/// already taken on this level (each shortens the next window). <see cref="BleedOutTick"/>
/// is when a down member dies; <see cref="GraceUntilTick"/> is when a revived (or respawned)
/// member's grace ends, and <see cref="RespawnGrace"/> says the grace is a respawn's: no damage
/// at all, and it ends early when the member acts (<see cref="DownedEvent.Acted"/>). A member
/// reviving another names it in <see cref="Reviving"/>, from <see cref="ReviveStartTick"/>.
/// </summary>
public sealed record DownedMember(
    string Id,
    Standing Standing = Standing.Up,
    bool InInstance = true,
    int DownsThisLevel = 0,
    long BleedOutTick = 0,
    long GraceUntilTick = 0,
    string? Reviving = null,
    long ReviveStartTick = 0,
    bool RespawnGrace = false);

/// <summary>
/// A party as the downed machine sees it, at <see cref="Tick"/>, on a server running
/// <see cref="TicksPerSecond"/> (the engine's rate, an input: the table's numbers are seconds).
/// </summary>
public sealed record DownedParty(ImmutableArray<DownedMember> Members, double TicksPerSecond, long Tick = 0)
{
    public DownedMember Member(string id) =>
        Members.FirstOrDefault(m => m.Id == id) ?? throw new KeyNotFoundException($"'{id}' is not in the party");

    internal DownedParty With(DownedMember member) =>
        this with { Members = Members.Select(m => m.Id == member.Id ? member : m).ToImmutableArray() };
}

/// <summary>Something that happens to the party (plan_tf2_rpg 2g, R26).</summary>
public abstract record DownedEvent
{
    private DownedEvent() { }

    /// <summary>Time passes: revives complete and windows lapse.</summary>
    public sealed record Tick : DownedEvent;

    /// <summary>Damage that would kill <paramref name="Member"/>.</summary>
    public sealed record LethalDamage(string Member) : DownedEvent;

    /// <summary>Damage that does not kill <paramref name="Member"/>: it interrupts a revive the member is making.</summary>
    public sealed record Damaged(string Member) : DownedEvent;

    /// <summary><paramref name="Reviver"/> starts holding use on <paramref name="Target"/>, <paramref name="Distance"/> units away.</summary>
    public sealed record ReviveStart(string Reviver, string Target, double Distance) : DownedEvent;

    /// <summary><paramref name="Reviver"/> lets go of use.</summary>
    public sealed record ReviveStop(string Reviver) : DownedEvent;

    /// <summary><paramref name="Reviver"/> is now <paramref name="Distance"/> units from its target.</summary>
    public sealed record ReviverMoved(string Reviver, double Distance) : DownedEvent;

    /// <summary><paramref name="Member"/> arrives in the instance.</summary>
    public sealed record Arrived(string Member) : DownedEvent;

    /// <summary><paramref name="Member"/> leaves the instance (travel, disconnect).</summary>
    public sealed record Left(string Member) : DownedEvent;

    /// <summary>The party is on a new level: the downs counted on the last one are forgotten.</summary>
    public sealed record LevelChanged : DownedEvent;

    /// <summary><paramref name="Member"/> joins the party, up and in the instance; one already in it has <see cref="Arrived"/>.</summary>
    public sealed record Joined(string Member) : DownedEvent;

    /// <summary><paramref name="Member"/>, dead, stands again (a respawn): up, keeping the downs counted on the level.</summary>
    public sealed record Respawned(string Member) : DownedEvent;

    /// <summary>
    /// <paramref name="Member"/> died by a road the downed state did not decide (a suicide, a kill the
    /// game made outright): dead whatever its standing, and the party falls if nobody is left up.
    /// </summary>
    public sealed record Killed(string Member) : DownedEvent;

    /// <summary><paramref name="Member"/> attacked or used a skill: a respawn's grace ends there.</summary>
    public sealed record Acted(string Member) : DownedEvent;
}

/// <summary>What a step decided, for the server to carry out and the client to show.</summary>
public abstract record DownedOutcome
{
    private DownedOutcome() { }

    /// <summary><paramref name="Member"/> is down until <paramref name="BleedOutTick"/>; it drops nothing.</summary>
    public sealed record Downed(string Member, long BleedOutTick) : DownedOutcome;

    /// <summary><paramref name="Member"/> dies: R5 applies (corpse, town, the run back).</summary>
    public sealed record Died(string Member, DeathCause Cause) : DownedOutcome;

    /// <summary><paramref name="Member"/> stands at <paramref name="LifePct"/> of max life, untouchable until <paramref name="GraceUntilTick"/>.</summary>
    public sealed record Revived(string Member, string By, double LifePct, long GraceUntilTick) : DownedOutcome;

    /// <summary><paramref name="Member"/> stands again (a respawn), taking no damage until <paramref name="GraceUntilTick"/> or until it acts.</summary>
    public sealed record Respawned(string Member, long GraceUntilTick) : DownedOutcome;

    /// <summary><paramref name="Member"/>'s respawn grace ended early: it acted.</summary>
    public sealed record GraceEnded(string Member) : DownedOutcome;

    public sealed record ReviveStarted(string Reviver, string Target, long CompletesAtTick) : DownedOutcome;

    public sealed record ReviveInterrupted(string Reviver, string Target, ReviveBreak Why) : DownedOutcome;

    /// <summary>Lethal damage reached a member in its grace: it does not go down.</summary>
    public sealed record Shielded(string Member) : DownedOutcome;

    /// <summary>The event was refused and changed nothing.</summary>
    public sealed record Refused(DownedRefusal Why) : DownedOutcome;
}

public enum DeathCause
{
    /// <summary>Lethal damage with nobody else standing in the instance: solo, or the last one up.</summary>
    NoOneStanding,
    /// <summary>The downed window ran out.</summary>
    BledOut,
    /// <summary>Down when the last standing member went down or left.</summary>
    PartyFell,
    /// <summary>Left the instance while down.</summary>
    LeftWhileDown,
    /// <summary>Died by a road the downed state did not decide (<see cref="DownedEvent.Killed"/>).</summary>
    Outright,
}

public enum ReviveBreak
{
    LetGo,
    MovedAway,
    Damaged,
    ReviverDown,
    TargetGone,
}

public enum DownedRefusal
{
    NotUp,
    TargetNotDown,
    OutOfRange,
    AlreadyReviving,
    TargetTaken,
    NotReviving,
    NotInInstance,
    /// <summary>A respawn of a member who is not dead.</summary>
    NotDead,
    /// <summary>A kill of a member already dead.</summary>
    AlreadyDead,
}

/// <summary>A step: the party after it and what it decided.</summary>
public sealed record DownedStep(DownedParty Party, ImmutableArray<DownedOutcome> Outcomes);

/// <summary>
/// The downed state (plan_tf2_rpg 2g, R26; plan_descent_logic RU10) as a pure machine over
/// (party, event, tick). Lethal damage with another member up in the instance puts a hero
/// down for a window that starts at the table's 20 s and shortens 5 s with each earlier down
/// on the level, to a floor of 5 s. A member up and in range who holds use for 3 s, not
/// damaged and not moving out of range, revives the downed one at 30 % life with 2 s of
/// grace. A window that lapses kills that hero; when nobody is left standing in the
/// instance, everyone down dies. Solo there is no downed state. A dead member who respawns
/// stands with the table's respawn grace: no damage at all for its seconds, ended early by
/// his first attack or skill (a respawn is not an execution: the plan is silent, this lane's
/// rule). Every number is death.json's.
/// </summary>
public sealed class DownedRules
{
    private readonly DescentContent _content;

    public DownedRules(DescentContent content) => _content = content;

    private DownedSettings S => _content.Death.Downed;

    /// <summary>A party of these members, all up and in the instance, at tick 0.</summary>
    public static DownedParty NewParty(double ticksPerSecond, params string[] members)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        return new DownedParty([.. members.Select(m => new DownedMember(m))], ticksPerSecond);
    }

    /// <summary>Seconds as whole ticks at the party's rate, rounded up so a window is never shorter than its seconds.</summary>
    public static long Ticks(DownedParty party, double seconds) => (long)Math.Ceiling(seconds * party.TicksPerSecond - 1e-9);

    /// <summary>The window, in seconds, of a down that follows <paramref name="earlierDowns"/> on the same level.</summary>
    public double WindowSeconds(int earlierDowns) =>
        Math.Max(S.WindowFloorSeconds, S.WindowSeconds - S.ShortenPerDownSeconds * earlierDowns);

    /// <summary>Whether a member is in its post-revive grace at <paramref name="tick"/>.</summary>
    public static bool InGrace(DownedMember member, long tick) => member.Standing == Standing.Up && tick < member.GraceUntilTick;

    /// <summary>Whether a member takes no damage at all at <paramref name="tick"/>: in a respawn's grace.</summary>
    public static bool Untouchable(DownedMember member, long tick) => member.RespawnGrace && InGrace(member, tick);

    /// <summary>Applies <paramref name="e"/> at <paramref name="tick"/>, after the timers due by then have run.</summary>
    public DownedStep Step(DownedParty party, DownedEvent e, long tick)
    {
        if (tick < party.Tick)
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"the party is at tick {party.Tick}: time does not run back");
        var outcomes = ImmutableArray.CreateBuilder<DownedOutcome>();
        party = RunTimers(party with { Tick = tick }, tick, outcomes);
        party = e switch
        {
            DownedEvent.Tick => party,
            DownedEvent.LethalDamage l => Lethal(party, l.Member, tick, outcomes),
            DownedEvent.Damaged d => Interrupt(party, d.Member, ReviveBreak.Damaged, outcomes),
            DownedEvent.ReviveStart r => StartRevive(party, r, tick, outcomes),
            DownedEvent.ReviveStop r => Stop(party, r.Reviver, outcomes),
            DownedEvent.ReviverMoved m => m.Distance > S.ReviveRange ? Interrupt(party, m.Reviver, ReviveBreak.MovedAway, outcomes) : party,
            DownedEvent.Arrived a => party.With(party.Member(a.Member) with { InInstance = true }),
            DownedEvent.Left l => Leave(party, l.Member, outcomes),
            DownedEvent.LevelChanged => party with { Members = [.. party.Members.Select(m => m with { DownsThisLevel = 0 })] },
            DownedEvent.Joined j => party.Members.Any(m => m.Id == j.Member)
                ? party.With(party.Member(j.Member) with { InInstance = true })
                : party with { Members = party.Members.Add(new DownedMember(j.Member)) },
            DownedEvent.Respawned r => Respawn(party, r.Member, tick, outcomes),
            DownedEvent.Killed k => KillOutright(party, k.Member, outcomes),
            DownedEvent.Acted a => Act(party, a.Member, tick, outcomes),
            _ => throw new ArgumentOutOfRangeException(nameof(e), e, "not a downed event"),
        };
        return new DownedStep(party, outcomes.ToImmutable());
    }

    /// <summary>Revives due by <paramref name="tick"/> complete first, so one finishing on the tick a window lapses saves the hero.</summary>
    private DownedParty RunTimers(DownedParty party, long tick, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        long hold = Ticks(party, S.ReviveHoldSeconds);
        foreach (var reviver in party.Members.Where(m => m.Reviving is not null && m.ReviveStartTick + hold <= tick).ToList())
        {
            var target = party.Member(reviver.Reviving!);
            long done = reviver.ReviveStartTick + hold;
            long grace = done + Ticks(party, S.GraceSeconds);
            party = party.With(reviver with { Reviving = null, ReviveStartTick = 0 });
            party = party.With(target with { Standing = Standing.Up, BleedOutTick = 0, GraceUntilTick = grace, RespawnGrace = false });
            outcomes.Add(new DownedOutcome.Revived(target.Id, reviver.Id, S.ReviveLifePct, grace));
        }
        foreach (var lapsed in party.Members.Where(m => m.Standing == Standing.Down && m.BleedOutTick <= tick).ToList())
            party = Kill(party, lapsed.Id, DeathCause.BledOut, outcomes);
        return party;
    }

    private DownedParty Lethal(DownedParty party, string id, long tick, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        var m = party.Member(id);
        if (m.Standing != Standing.Up)
        {
            outcomes.Add(new DownedOutcome.Refused(DownedRefusal.NotUp));   // a down hero is not finished off; the dead stay dead
            return party;
        }
        if (InGrace(m, tick))
        {
            outcomes.Add(new DownedOutcome.Shielded(id));
            return party;
        }
        party = Interrupt(party, id, ReviveBreak.ReviverDown, outcomes);
        bool another = party.Members.Any(o => o.Id != id && o.Standing == Standing.Up && o.InInstance);
        if (!m.InInstance || !another)
        {
            party = Kill(party, id, DeathCause.NoOneStanding, outcomes);
            return Fall(party, outcomes);
        }
        long until = tick + Ticks(party, WindowSeconds(m.DownsThisLevel));
        party = party.With(party.Member(id) with
        {
            Standing = Standing.Down,
            DownsThisLevel = m.DownsThisLevel + 1,
            BleedOutTick = until,
            GraceUntilTick = 0,
        });
        outcomes.Add(new DownedOutcome.Downed(id, until));
        return party;
    }

    private DownedParty StartRevive(DownedParty party, DownedEvent.ReviveStart r, long tick, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        var reviver = party.Member(r.Reviver);
        var target = party.Member(r.Target);
        DownedRefusal? why =
            reviver.Standing != Standing.Up ? DownedRefusal.NotUp
            : !reviver.InInstance ? DownedRefusal.NotInInstance
            : target.Standing != Standing.Down ? DownedRefusal.TargetNotDown
            : r.Distance > S.ReviveRange ? DownedRefusal.OutOfRange
            : reviver.Reviving is not null ? DownedRefusal.AlreadyReviving
            : party.Members.Any(o => o.Reviving == target.Id) ? DownedRefusal.TargetTaken
            : null;
        if (why is { } refusal)
        {
            outcomes.Add(new DownedOutcome.Refused(refusal));
            return party;
        }
        outcomes.Add(new DownedOutcome.ReviveStarted(reviver.Id, target.Id, tick + Ticks(party, S.ReviveHoldSeconds)));
        return party.With(reviver with { Reviving = target.Id, ReviveStartTick = tick });
    }

    private static DownedParty Stop(DownedParty party, string reviver, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        if (party.Member(reviver).Reviving is null)
        {
            outcomes.Add(new DownedOutcome.Refused(DownedRefusal.NotReviving));
            return party;
        }
        return Interrupt(party, reviver, ReviveBreak.LetGo, outcomes);
    }

    /// <summary>Ends the revive <paramref name="reviver"/> is making, if any.</summary>
    private static DownedParty Interrupt(DownedParty party, string reviver, ReviveBreak why, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        var m = party.Member(reviver);
        if (m.Reviving is null) return party;
        outcomes.Add(new DownedOutcome.ReviveInterrupted(m.Id, m.Reviving, why));
        return party.With(m with { Reviving = null, ReviveStartTick = 0 });
    }

    private DownedParty Leave(DownedParty party, string id, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        party = Interrupt(party, id, ReviveBreak.TargetGone, outcomes);
        var m = party.Member(id);
        party = party.With(m with { InInstance = false });
        if (m.Standing == Standing.Down)
            party = Kill(party, id, DeathCause.LeftWhileDown, outcomes);
        return Fall(party, outcomes);
    }

    /// <summary>A dead member stands again, its downs on the level kept, in the respawn's grace; any other is refused.</summary>
    private DownedParty Respawn(DownedParty party, string id, long tick, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        var m = party.Member(id);
        if (m.Standing != Standing.Dead)
        {
            outcomes.Add(new DownedOutcome.Refused(DownedRefusal.NotDead));
            return party;
        }
        long grace = tick + Ticks(party, S.RespawnGraceSeconds);
        outcomes.Add(new DownedOutcome.Respawned(id, grace));
        return party.With(m with { Standing = Standing.Up, InInstance = true, BleedOutTick = 0, GraceUntilTick = grace, RespawnGrace = true });
    }

    /// <summary>A member acted: a respawn's grace it is in ends now; anything else is unchanged (an act is no refusal).</summary>
    private static DownedParty Act(DownedParty party, string id, long tick, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        var m = party.Member(id);
        if (!Untouchable(m, tick))
            return party;
        outcomes.Add(new DownedOutcome.GraceEnded(id));
        return party.With(m with { GraceUntilTick = tick, RespawnGrace = false });
    }

    /// <summary>A death the machine did not decide: the member is dead whatever it was, and the party falls with nobody up.</summary>
    private static DownedParty KillOutright(DownedParty party, string id, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        if (party.Member(id).Standing == Standing.Dead)
        {
            outcomes.Add(new DownedOutcome.Refused(DownedRefusal.AlreadyDead));
            return party;
        }
        party = Interrupt(party, id, ReviveBreak.ReviverDown, outcomes);
        party = Kill(party, id, DeathCause.Outright, outcomes);
        return Fall(party, outcomes);
    }

    /// <summary>With nobody up in the instance, everyone down dies (R26: the last standing member went down or left).</summary>
    private static DownedParty Fall(DownedParty party, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        if (party.Members.Any(m => m.Standing == Standing.Up && m.InInstance)) return party;
        foreach (var down in party.Members.Where(m => m.Standing == Standing.Down).ToList())
            party = Kill(party, down.Id, DeathCause.PartyFell, outcomes);
        return party;
    }

    private static DownedParty Kill(DownedParty party, string id, DeathCause cause, ImmutableArray<DownedOutcome>.Builder outcomes)
    {
        // Whoever was reviving the dead hero lets go: the target is gone.
        foreach (var r in party.Members.Where(o => o.Reviving == id).ToList())
            party = Interrupt(party, r.Id, ReviveBreak.TargetGone, outcomes);
        party = party.With(party.Member(id) with { Standing = Standing.Dead, BleedOutTick = 0, GraceUntilTick = 0, Reviving = null, RespawnGrace = false });
        outcomes.Add(new DownedOutcome.Died(id, cause));
        return party;
    }
}
