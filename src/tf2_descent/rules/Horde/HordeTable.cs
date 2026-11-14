using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Horde;

/// <summary>
/// Where a far robot waits when the room ahead is one a hero can see into (plan_descent_logic
/// 3.4, 3.5): the first in line stands <see cref="Backoff"/> units short of the door on its own
/// side, each one after it <see cref="Spacing"/> further back.
/// </summary>
public sealed record DoorQueueSettings(double Backoff, double Spacing);

/// <summary>
/// horde.json (plan_descent_logic L1, section 3, defaults E1 and E2): the horde controller's
/// numbers. <see cref="RosterCapacity"/> robots a level at most; <see cref="PoolBodies"/>
/// pooled bodies and <see cref="BossBodies"/> boss bodies of its own; a room wakes when a hero
/// is within <see cref="WakeRooms"/> rooms of it on the room graph; an embodied robot with no
/// hero near and none in sight gives its body back after <see cref="DemoteDelaySeconds"/>; an
/// awake room with no hero near sleeps after <see cref="SleepDelaySeconds"/>; the far
/// simulation ticks at <see cref="FarTickHz"/>; a patrolling far robot waits
/// <see cref="PatrolDwellSeconds"/> at each marker; a pack forgets its last contact after
/// <see cref="AlertMemorySeconds"/>; a far robot within <see cref="ArriveRadius"/> units of
/// its goal has arrived.
/// </summary>
public sealed record HordeSettings(
    int RosterCapacity,
    int PoolBodies,
    int BossBodies,
    int WakeRooms,
    double DemoteDelaySeconds,
    double SleepDelaySeconds,
    double FarTickHz,
    double PatrolDwellSeconds,
    double AlertMemorySeconds,
    double ArriveRadius,
    DoorQueueSettings DoorQueue)
{
    /// <summary>Every body index the horde lends: the pool's, then the boss's.</summary>
    public int Bodies => PoolBodies + BossBodies;
}

public sealed class HordeTable : SingleTable<HordeSettings>
{
    public override string Name => ContentTables.Horde;

    protected override HordeSettings Read(JsonFields f)
    {
        var q = f.Obj("doorQueue");
        var queue = new DoorQueueSettings(q.Num("backoff"), q.Num("spacing"));
        q.End();
        return new HordeSettings(
            f.Int("rosterCapacity"), f.Int("poolBodies"), f.Int("bossBodies"), f.Int("wakeRooms"),
            f.Num("demoteDelaySeconds"), f.Num("sleepDelaySeconds"), f.Num("farTickHz"),
            f.Num("patrolDwellSeconds"), f.Num("alertMemorySeconds"), f.Num("arriveRadius"), queue);
    }

    /// <summary>
    /// The pool and its boss bodies fit the edict budget's 64 (plan section 2), so an AI slot
    /// per body stays inside a session; at most one boss body; a tick rate above zero; every
    /// distance and delay non-negative.
    /// </summary>
    protected override void Validate(DescentContent content, HordeSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "rosterCapacity", s.RosterCapacity, 1, 4096);
        if (ContentChecks.Range(errors, t, null, "poolBodies", s.PoolBodies, 1, 64)
            && ContentChecks.Range(errors, t, null, "bossBodies", s.BossBodies, 0, 1))
            ContentChecks.Range(errors, t, null, "poolBodies+bossBodies", s.Bodies, 1, 64);
        ContentChecks.Range(errors, t, null, "wakeRooms", s.WakeRooms, 0, 16);
        ContentChecks.Range(errors, t, null, "demoteDelaySeconds", s.DemoteDelaySeconds, 0, 600);
        ContentChecks.Range(errors, t, null, "sleepDelaySeconds", s.SleepDelaySeconds, 0, 600);
        ContentChecks.Range(errors, t, null, "farTickHz", s.FarTickHz, 0.1, 10);
        ContentChecks.Range(errors, t, null, "patrolDwellSeconds", s.PatrolDwellSeconds, 0, 600);
        ContentChecks.Range(errors, t, null, "alertMemorySeconds", s.AlertMemorySeconds, 0, 3600);
        ContentChecks.Range(errors, t, null, "arriveRadius", s.ArriveRadius, 1, 1024);
        ContentChecks.Range(errors, t, null, "doorQueue.backoff", s.DoorQueue.Backoff, 0, 1024);
        ContentChecks.Range(errors, t, null, "doorQueue.spacing", s.DoorQueue.Spacing, 1, 1024);
    }
}
