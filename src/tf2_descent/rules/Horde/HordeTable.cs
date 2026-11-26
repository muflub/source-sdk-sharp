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
/// How a robot sees a hero (the aggro lane, 2026-11-15; plan_tf2_rpg B.3): within
/// <see cref="Range"/> units times its Alertness (the curve's <c>sightRangeScale</c>; a
/// robot's size does not lengthen it), inside a cone of <see cref="HalfConeDeg"/> either side of
/// its facing (times the curve's <c>sightConeScale</c>) or within <see cref="AroundRadius"/> of
/// it whichever way it faces, and never through a wall (the PVS and a line, as before).
/// <para><b>The room's light</b> (the level's <c>LITE</c>, level 1 = 100): a hero in a room of
/// light L is seen at the range times <see cref="LightScale"/>: <c>clamp(L / FullLight,
/// DarkScale, 1)</c>, <see cref="DarkScale"/> in a room dark by design, 1 in a room the level does
/// not measure. <b>The lamp</b>: a hero whose lamp is lit is seen within its radius times
/// <see cref="LampReachScale"/> however dark the room, up to the robot's own range.
/// <b>Lamplighter</b> (the user's ruling L26, 2026-11-22: "robots notice it when on"): while a toggle
/// carrying <c>lamp_beacon</c> is on, that reach is times <see cref="BeaconReachScale"/> as well
/// (<see cref="LampReach"/>, the one place the horde's sight reads a lamp).</para>
/// </summary>
public sealed record SightSenses(double Range, double HalfConeDeg, double AroundRadius, double FullLight, double DarkScale, double LampReachScale,
                                 double BeaconReachScale)
{
    /// <summary>
    /// How far a lamp of <paramref name="radius"/> units (0: out) shows its hero however dark the
    /// room, before the robot's own range caps it: the radius times <see cref="LampReachScale"/>,
    /// and times <see cref="BeaconReachScale"/> while his Lamplighter is on (<paramref name="beacon"/>).
    /// </summary>
    public double LampReach(double radius, bool beacon) => radius * LampReachScale * (beacon ? BeaconReachScale : 1);

    /// <summary>The multiplier on a robot's range for a hero in a room of <paramref name="light"/> (null: unmeasured), <paramref name="darkByDesign"/> or not.</summary>
    public double LightScale(float? light, bool darkByDesign)
    {
        if (darkByDesign)
            return DarkScale;
        if (light is not { } l || float.IsNaN(l) || FullLight <= 0)
            return 1;
        return Math.Clamp(l / FullLight, DarkScale, 1);
    }
}

/// <summary>
/// How far a robot hears a sound (the aggro lane, 2026-11-15; plan_tf2_rpg B.3): the walk from it
/// through open doors, never through a wall, within its kind's reach (units, before the hearer's
/// <c>hearingScale</c>), and no more than <see cref="Rooms"/> doors from the robot's room (0: its
/// own room only). A sound no hero made (a robot's rocket bursting, a barrel: the engine names no
/// hero as its owner) carries no more than <see cref="UnownedRooms"/> doors. A landing is a blast
/// jump's (<see cref="Landing"/>); a sound of a kind no robot listens for reaches nobody.
/// </summary>
public sealed record HearingSenses(int Rooms, int UnownedRooms, double Gunfire, double Explosion, double Footstep, double Landing, double Pain, double Death,
                                   double Door)
{
    /// <summary>The doors a sound carries through: <see cref="Rooms"/> for one a hero made (<paramref name="seat"/> above 0), else <see cref="UnownedRooms"/>.</summary>
    public int RoomsFor(int seat) => seat > 0 ? Rooms : UnownedRooms;
}

/// <summary>
/// The horde's senses (the aggro lane, 2026-11-15): <see cref="Sight"/>, <see cref="Hearing"/>, and
/// the pack call: a robot that sees a hero alerts its own pack, and a pack-mate with a body within
/// <see cref="PackCallReach"/> units of walk (and the hearing's rooms) is told where the hero was;
/// no other pack is told.
/// </summary>
public sealed record HordeSenses(SightSenses Sight, HearingSenses Hearing, double PackCallReach);

/// <summary>
/// horde.json (plan_descent_logic L1, section 3, defaults E1 and E2): the horde controller's
/// numbers. <see cref="RosterCapacity"/> robots a level at most; <see cref="PoolBodies"/>
/// pooled bodies and <see cref="BossBodies"/> boss bodies of its own; a room wakes when a hero
/// is within <see cref="WakeRooms"/> rooms of it on the room graph; an embodied robot with no
/// hero near and none in sight gives its body back after <see cref="DemoteDelaySeconds"/>; an
/// awake room with no hero near sleeps after <see cref="SleepDelaySeconds"/>; the far
/// simulation ticks at <see cref="FarTickHz"/>; a patrolling far robot waits
/// <see cref="PatrolDwellSeconds"/> at each marker; a pack forgets its last contact after
/// <see cref="AlertMemorySeconds"/>; a robot hunting a hero gives up when the room it would go
/// to next, or the hero's, is more than <see cref="LeashDoors"/> doors from its home room
/// (plan L25, the leash: <c>HordeLeash</c>); a far robot within <see cref="ArriveRadius"/> units
/// of its goal has arrived. <see cref="Director"/> is the spawn director's, <see cref="Shove"/>
/// the door shove's, <see cref="Senses"/> the robots' sight and hearing.
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
    int LeashDoors,
    double ArriveRadius,
    DoorQueueSettings DoorQueue,
    DirectorSettings Director,
    ShoveSettings Shove,
    HordeSenses Senses)
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
        var se = f.Obj("senses");
        var si = se.Obj("sight");
        var sight = new SightSenses(si.Num("range"), si.Num("halfConeDeg"), si.Num("aroundRadius"), si.Num("fullLight"), si.Num("darkScale"),
                                    si.Num("lampReachScale"), si.Num("beaconReachScale"));
        si.End();
        var he = se.Obj("hearing");
        var hearing = new HearingSenses(he.Int("rooms"), he.Int("unownedRooms"), he.Num("gunfire"), he.Num("explosion"), he.Num("footstep"), he.Num("landing"), he.Num("pain"),
                                        he.Num("death"), he.Num("door"));
        he.End();
        var senses = new HordeSenses(sight, hearing, se.Num("packCallReach"));
        se.End();
        return new HordeSettings(
            f.Int("rosterCapacity"), f.Int("poolBodies"), f.Int("bossBodies"), f.Int("wakeRooms"),
            f.Num("demoteDelaySeconds"), f.Num("sleepDelaySeconds"), f.Num("farTickHz"),
            f.Num("patrolDwellSeconds"), f.Num("alertMemorySeconds"), f.Int("leashDoors"), f.Num("arriveRadius"), queue, director, shove, senses);
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
        ContentChecks.Range(errors, t, null, "leashDoors", s.LeashDoors, 1, 32);
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

        var si = s.Senses.Sight;
        ContentChecks.Range(errors, t, null, "senses.sight.range", si.Range, 64, 8192);
        ContentChecks.Range(errors, t, null, "senses.sight.halfConeDeg", si.HalfConeDeg, 5, 85);
        ContentChecks.Range(errors, t, null, "senses.sight.aroundRadius", si.AroundRadius, 0, si.Range);
        ContentChecks.Range(errors, t, null, "senses.sight.fullLight", si.FullLight, 1, 1000);
        ContentChecks.Range(errors, t, null, "senses.sight.darkScale", si.DarkScale, 0.05, 1);
        ContentChecks.Range(errors, t, null, "senses.sight.lampReachScale", si.LampReachScale, 0, 4);
        ContentChecks.Range(errors, t, null, "senses.sight.beaconReachScale", si.BeaconReachScale, 1, 4);   // L26: a lamplighter is never seen from nearer
        var he = s.Senses.Hearing;
        ContentChecks.Range(errors, t, null, "senses.hearing.rooms", he.Rooms, 0, 16);
        ContentChecks.Range(errors, t, null, "senses.hearing.unownedRooms", he.UnownedRooms, 0, he.Rooms);
        foreach (var (name, reach) in new[] { ("gunfire", he.Gunfire), ("explosion", he.Explosion), ("footstep", he.Footstep), ("landing", he.Landing),
                                              ("pain", he.Pain), ("death", he.Death), ("door", he.Door) })
            ContentChecks.Range(errors, t, null, $"senses.hearing.{name}", reach, 0, 8192);
        ContentChecks.Range(errors, t, null, "senses.packCallReach", s.Senses.PackCallReach, 0, 8192);
    }
}
