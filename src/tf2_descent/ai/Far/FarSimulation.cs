using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Senses;
using TF2.Descent.Rules.Horde;

namespace TF2.Descent.Ai.Far;

/// <summary>
/// A sound the far simulation hears (plan_descent_logic 3.5, RPG plan B.3): where it was made,
/// how far it carries (world units, walked through doors), and the hero seat that made it (-1
/// for none: a barrel, a door), and what it was.
/// </summary>
public readonly record struct SoundEvent(int Room, AiVector Position, float Loudness, int Seat, SoundKind Kind = SoundKind.Unknown);

/// <summary>
/// The far simulation (plan_descent_logic 3.5): one tick at horde.json's rate, a pure function
/// of the roster's state, the sounds, what the heroes can see into, and the tick. No combat
/// happens far.
///
/// <list type="number">
/// <item><b>Sound</b> reaches a sleeping or far robot when the walk from the sound to it through
/// doors (<see cref="HordeLevel.PathDistance"/>) is within its loudness and its room is within
/// <see cref="HearingSenses.RoomsFor"/> doors of the sound's. A robot that hears
/// remembers where and when; its pack is alerted to the sound, and the pack's sleepers wake.
/// Bodied robots hear through the server's senses, not here.</item>
/// <item>A pack whose contact is older than <see cref="HordeSettings.AlertMemorySeconds"/> calms.</item>
/// <item>Each far robot, in row order: its statuses tick (the rules, through the roster); a
/// stunned robot, the boss, and a robot in a room a hero can see into hold. An alerted pack's
/// robots walk to its contact; a calm robot walks home and patrols its room's markers (waiting
/// <see cref="HordeSettings.PatrolDwellSeconds"/> at each), or holds at its only marker.</item>
/// <item>A robot walks the room graph a door at a time at its stats' speed times its status
/// slow. It never enters a room a hero can see into: it queues at that door in its own room,
/// the first <see cref="DoorQueueSettings.Backoff"/> short of it and each after it
/// <see cref="DoorQueueSettings.Spacing"/> further back.</item>
/// </list>
/// </summary>
public sealed class FarSimulation
{
    private readonly HordeSettings _settings;
    private readonly HordeLevel _level;
    private readonly float _dt;
    private readonly long _dwellTicks;
    private readonly long _memoryTicks;
    private readonly int[] _queued;   // per door and side, this tick
    private readonly HordeAlerts? _alerts;

    public FarSimulation(HordeSettings settings, HordeLevel level, float dt, long dwellTicks, long memoryTicks, int[] queued, HordeAlerts? alerts = null)
    {
        _alerts = alerts;
        _settings = settings;
        _level = level;
        _dt = dt;
        _dwellTicks = dwellTicks;
        _memoryTicks = memoryTicks;
        _queued = queued;
    }

    /// <summary>The far simulation for one level, on a server ticking at <paramref name="ticksPerSecond"/>.</summary>
    public static FarSimulation For(HordeSettings settings, HordeLevel level, double ticksPerSecond, HordeAlerts? alerts = null) =>
        new(settings, level, (float)(1 / settings.FarTickHz),
            (long)Math.Ceiling(settings.PatrolDwellSeconds * ticksPerSecond),
            (long)Math.Ceiling(settings.AlertMemorySeconds * ticksPerSecond),
            new int[2 * level.Doors.Length], alerts);

    /// <summary>Server ticks between far ticks, for the caller's schedule.</summary>
    public static long Interval(HordeSettings settings, double ticksPerSecond) =>
        Math.Max(1, (long)Math.Round(ticksPerSecond / settings.FarTickHz));

    public void Tick(RobotRoster roster, IHeroSight sight, ReadOnlySpan<SoundEvent> sounds, long now)
    {
        foreach (ref readonly var s in sounds)
            Hear(roster, s, now);

        for (int p = 0; p < roster.PackCount; p++)
            if (roster.PackAlert(p) != AlertLevel.Calm && roster.ContactTick(p) < now - _memoryTicks)
                roster.CalmPack(p);

        Array.Clear(_queued);
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster.StateAt(i) != RobotState.Far) continue;
            roster.TickStatuses(i, now);
            if (roster.StateAt(i) != RobotState.Far) continue;   // the statuses killed it
            Move(roster, sight, i, now);
        }
    }

    private void Hear(RobotRoster roster, in SoundEvent s, long now)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            var state = roster.StateAt(i);
            if (state is not (RobotState.Asleep or RobotState.Far)) continue;
            if (_level.Distance(s.Room, roster.RoomRef(i)) > _settings.Senses.Hearing.RoomsFor(s.Seat)) continue;
            if (_level.PathDistance(s.Room, s.Position, roster.RoomRef(i), roster.PositionRef(i)) > s.Loudness) continue;
            roster.Hear(i, s.Position, now, s.Seat);
            roster.AlertPack(roster.PackAt(i), s.Room, s.Position, now, s.Seat);
            if (_alerts is not null)
                Logged(roster, i, s, now);
        }
    }

    /// <summary>The dev log's view of a far hearing: the hearer, then the rest of its pack, called.</summary>
    private void Logged(RobotRoster roster, int i, in SoundEvent s, long now)
    {
        int pack = roster.PackAt(i);
        for (int k = roster.PackFirst(pack), end = k + roster.PackSize(pack); k < end; k++)
            if (roster.StateAt(k) != RobotState.Dead)
                _alerts!.Add(new AlertRecord(now, k, pack, k == i ? AlertCause.FarSound : AlertCause.PackCall, s.Kind, roster.PositionRef(k),
                                             roster.RoomRef(k), s.Position, s.Room, s.Seat, roster.Body(k) != 0));
    }

    private void Move(RobotRoster roster, IHeroSight sight, int i, long now)
    {
        int room = roster.RoomRef(i);
        if (roster.Stunned(i) || roster.BossAt(i) || sight.CanSeeInto(room))
        {
            roster.SetActivity(i, FarActivity.Hold);
            return;
        }

        // The goal: the pack's contact, or home.
        int pack = roster.PackAt(i);
        int goalRoom;
        AiVector goal;
        FarActivity activity;
        var markers = _level.Rooms[roster.HomeRoom(i)].Markers;
        ref int patrol = ref roster.PatrolMarkerRef(i);
        if (roster.PackAlert(pack) != AlertLevel.Calm && roster.ContactRoom(pack) >= 0)
        {
            goalRoom = roster.ContactRoom(pack);
            goal = roster.Contact(pack);
            activity = FarActivity.Converge;
        }
        else
        {
            goalRoom = roster.HomeRoom(i);
            goal = markers.IsEmpty ? roster.PositionRef(i) : PatrolGoal(roster, i, markers, patrol);
            activity = room != goalRoom ? FarActivity.Return : markers.Length > 1 ? FarActivity.Patrol : FarActivity.Hold;
        }

        float step = (float)(roster.SpeedAt(i) * roster.SpeedFactor(i) * _dt);
        ref AiVector at = ref roster.PositionRef(i);
        if (room != goalRoom)
        {
            int next = _level.NextRoom(room, goalRoom);
            if (next < 0)
            {
                roster.SetActivity(i, FarActivity.Hold);
                return;
            }
            int d = _level.DoorBetween(room, next);
            var door = _level.Doors[d];
            if (sight.CanSeeInto(next))
            {
                int slot = _queued[2 * d + (room == door.RoomA ? 0 : 1)]++;
                var spot = door.Position - door.Into(room) * (float)(_settings.DoorQueue.Backoff + slot * _settings.DoorQueue.Spacing);
                Walk(roster, i, ref at, spot, step);
                roster.SetActivity(i, FarActivity.Queue);
                return;
            }
            if (Walk(roster, i, ref at, door.Position, step))
                roster.RoomRef(i) = next;
            roster.SetActivity(i, activity);
            return;
        }

        bool arrived = Walk(roster, i, ref at, goal, step) || at.DistanceTo(goal) <= _settings.ArriveRadius;
        roster.SetActivity(i, activity);
        if (activity != FarActivity.Patrol || !arrived) return;
        ref long dwell = ref roster.DwellUntilRef(i);
        if (dwell == 0) dwell = now + _dwellTicks;
        else if (now >= dwell)
        {
            patrol = (patrol + 1) % markers.Length;
            dwell = 0;
        }
    }

    /// <summary>
    /// Where a robot patrolling its home room stands at marker <paramref name="patrol"/>: its
    /// post (HO9) carried from its home marker to that one, so the pack keeps the places the
    /// director gave it at every marker and stands at its posts at home. A robot with no home
    /// marker (a dev command's) goes to the marker itself.
    /// </summary>
    private static AiVector PatrolGoal(RobotRoster roster, int i, ImmutableArray<PackMarker> markers, int patrol)
    {
        var marker = markers[patrol % markers.Length].Position;
        int home = roster.HomeMarker(i);
        return home < 0 || home >= markers.Length ? marker : roster.Post(i) + (marker - markers[home].Position);
    }

    /// <summary>Walks <paramref name="at"/> up to <paramref name="step"/> towards <paramref name="to"/>, facing the way it goes. Returns whether it got there.</summary>
    private static bool Walk(RobotRoster roster, int i, ref AiVector at, AiVector to, float step)
    {
        var delta = to - at;
        float length = delta.Length;
        if (length <= step)
        {
            if (length > 0) roster.YawRef(i) = MathF.Atan2(delta.Y, delta.X) * (180f / MathF.PI);
            at = to;
            return true;
        }
        if (step <= 0) return false;
        at += delta * (step / length);
        roster.YawRef(i) = MathF.Atan2(delta.Y, delta.X) * (180f / MathF.PI);
        return false;
    }
}
