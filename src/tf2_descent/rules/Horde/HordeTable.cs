using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Rules.Horde;

/// <summary>
/// Where a far robot waits when the room ahead is one a hero can see into (plan_descent_logic
/// 3.4, 3.5): the first in line stands <see cref="Backoff"/> units short of the door on its own
/// side, each one after it <see cref="Spacing"/> further back.
/// </summary>
public sealed record DoorQueueSettings(double Backoff, double Spacing);

/// <summary>
/// The spawn director's numbers (plan_descent_logic HO9; plan_tf2_rpg 5d): a pack's members
/// stand at posts of their own round their marker, <see cref="PostGap"/> units between two
/// hulls, on up to <see cref="PostRings"/> rings round it; a robot leaves its room only by a
/// door its scaled hull fits with <see cref="DoorMargin"/> to spare on each side; a robot of a
/// size class in <see cref="FloorBoundSizes"/> (one that cannot climb stairs: plan 4.7, CR3's
/// finding) leaves only by a door whose sill is within <see cref="StepHeight"/> of the floor it
/// stands on; a robot that fits no marker of its room is drawn again up to
/// <see cref="RefitAttempts"/> times, then left out.
/// </summary>
public sealed record DirectorSettings(
    double PostGap,
    int PostRings,
    double DoorMargin,
    double StepHeight,
    IReadOnlyList<string> FloorBoundSizes,
    int RefitAttempts);

/// <summary>
/// The door shove (plan_descent_logic L9, 3.9, HO11): a hero whose feet are inside a door's
/// opening (its width, its height, and <see cref="HalfDepth"/> units either side of its plane)
/// and who holds up a robot whose path runs through that door, and within <see cref="Reach"/>
/// units of the door's plane, for <see cref="DwellSeconds"/> (at once for a robot whose
/// discipline is at most <see cref="LowDiscipline"/>), is shoved along the door's normal away
/// from the robot: <see cref="Speed"/> units a second at scale 1, times the robot's scale,
/// times one less the hero's knockback resistance, and <see cref="Lift"/> up, so the ground
/// does not eat it. One shove a door each <see cref="CooldownSeconds"/>. No damage.
/// </summary>
public sealed record ShoveSettings(
    double HalfDepth,
    double Reach,
    double DwellSeconds,
    int LowDiscipline,
    double CooldownSeconds,
    double Speed,
    double Lift);

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
/// its goal has arrived. <see cref="Director"/> is the spawn director's, <see cref="Shove"/>
/// the door shove's.
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
    DoorQueueSettings DoorQueue,
    DirectorSettings Director,
    ShoveSettings Shove)
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
        var d = f.Obj("director");
        var director = new DirectorSettings(d.Num("postGap"), d.Int("postRings"), d.Num("doorMargin"), d.Num("stepHeight"),
            d.Strings("floorBoundSizes", required: true), d.Int("refitAttempts"));
        d.End();
        var sh = f.Obj("shove");
        var shove = new ShoveSettings(sh.Num("halfDepth"), sh.Num("reach"), sh.Num("dwellSeconds"), sh.Int("lowDiscipline"),
            sh.Num("cooldownSeconds"), sh.Num("speed"), sh.Num("lift"));
        sh.End();
        return new HordeSettings(
            f.Int("rosterCapacity"), f.Int("poolBodies"), f.Int("bossBodies"), f.Int("wakeRooms"),
            f.Num("demoteDelaySeconds"), f.Num("sleepDelaySeconds"), f.Num("farTickHz"),
            f.Num("patrolDwellSeconds"), f.Num("alertMemorySeconds"), f.Num("arriveRadius"), queue, director, shove);
    }

    /// <summary>
    /// The pool and its boss bodies fit the edict budget's 64 (plan section 2), so an AI slot
    /// per body stays inside a session; at most one boss body; a tick rate above zero; every
    /// distance and delay non-negative; the director's floor-bound sizes are size classes of
    /// robot_rules.json; the shove's numbers in range, its discipline on the skill scale.
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

        var d = s.Director;
        ContentChecks.Range(errors, t, null, "director.postGap", d.PostGap, 0, 256);
        ContentChecks.Range(errors, t, null, "director.postRings", d.PostRings, 0, 16);
        ContentChecks.Range(errors, t, null, "director.doorMargin", d.DoorMargin, 0, 64);
        ContentChecks.Range(errors, t, null, "director.stepHeight", d.StepHeight, 0, 64);
        ContentChecks.Range(errors, t, null, "director.refitAttempts", d.RefitAttempts, 0, 64);
        if (content.TryGet<RobotRulesSettings>(ContentTables.RobotRules, out var robots))
            foreach (var size in d.FloorBoundSizes.Where(z => robots.Sizes.All(x => x.Id != z)))
                errors.Add(t, null, ContentRule.DanglingReference, $"'director.floorBoundSizes' names '{size}', which is not a size class of '{ContentTables.RobotRules}'");

        var v = s.Shove;
        ContentChecks.Range(errors, t, null, "shove.halfDepth", v.HalfDepth, 1, 256);
        ContentChecks.Range(errors, t, null, "shove.reach", v.Reach, 1, 1024);
        ContentChecks.Range(errors, t, null, "shove.dwellSeconds", v.DwellSeconds, 0, 10);
        int skillMax = robots?.Temperament.SkillMax ?? 10;
        ContentChecks.Range(errors, t, null, "shove.lowDiscipline", v.LowDiscipline, -1, skillMax);
        ContentChecks.Range(errors, t, null, "shove.cooldownSeconds", v.CooldownSeconds, 0.05, 10);
        ContentChecks.Range(errors, t, null, "shove.speed", v.Speed, 1, 3000);
        ContentChecks.Range(errors, t, null, "shove.lift", v.Lift, 0, 1000);
    }
}
