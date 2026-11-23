using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Horde;

namespace TF2.Descent.Ai.Embodiment;

/// <summary>
/// Who gets a body and who gives one up (plan_descent_logic 3.4). Pure: <see cref="Decide"/>
/// reads the roster, the pool, the heroes and the caller's sight and writes orders, never a
/// change; <see cref="HordeOrderApplier"/> carries them out. <see cref="Observe"/> is the one
/// write: it stamps when each body and room last had a hero near, which the demotion and sleep
/// delays read. The caller runs Observe, then Decide, then applies, each decision tick.
///
/// <list type="bullet">
/// <item><b>Wake.</b> A room wakes when a hero is within <see cref="HordeSettings.WakeRooms"/>
/// rooms of it; its robots are claimants at their markers at once.</item>
/// <item><b>Never in view.</b> A body is taken only at a point no hero can see, and given up
/// (demoted, or taken under pressure) only when no hero can see it.</item>
/// <item><b>Claimants</b> are far robots (and a waking room's sleepers) within the wake distance
/// of a hero, and a far robot queued at a door a hero sees through at any distance (it can go no
/// further bodiless; live on level 5's maze a hero's PVS reached past the wake distance and a
/// queued pack waited out its alert there), ranked: same room as a hero, then a pack alerted to a hero, then nearer by
/// room-graph distance; at the same rung and distance a champion first, then the lower row.</item>
/// <item><b>Demotion.</b> No hero within the wake distance and none in sight for
/// <see cref="HordeSettings.DemoteDelaySeconds"/>. Under pressure, a claimant with no free body
/// takes the lowest-ranked unseen body that ranks below it.</item>
/// <item><b>The boss</b> takes only the boss body, is never taken under pressure, and is not
/// demoted while a hero stands in its arena; once a hero stands there it takes its body even in
/// view (HO13: its entrance).</item>
/// <item><b>Sleep.</b> An awake room with no hero within the wake distance for
/// <see cref="HordeSettings.SleepDelaySeconds"/> and no body standing in it sleeps.</item>
/// <item>A dead robot's body is released at once.</item>
/// </list>
/// </summary>
public sealed class EmbodimentRules
{
    private readonly HordeSettings _settings;
    private readonly HordeLevel _level;
    private readonly long _demoteTicks;
    private readonly long _sleepTicks;
    private readonly int[] _heroDistance;   // per room
    private readonly int[] _bodiesIn;       // per room
    private readonly bool[] _free;          // per body index
    private readonly long[] _claimKey;      // per claimant
    private readonly int[] _claimRow;
    private readonly long[] _holdKey;       // per body holder
    private readonly int[] _holdRow;
    private readonly bool[] _holdSeen;

    public EmbodimentRules(HordeSettings settings, HordeLevel level, long demoteTicks, long sleepTicks, int[] heroDistance, int[] bodiesIn,
        bool[] free, long[] claimKey, int[] claimRow, long[] holdKey, int[] holdRow, bool[] holdSeen)
    {
        _settings = settings;
        _level = level;
        _demoteTicks = demoteTicks;
        _sleepTicks = sleepTicks;
        _heroDistance = heroDistance;
        _bodiesIn = bodiesIn;
        _free = free;
        _claimKey = claimKey;
        _claimRow = claimRow;
        _holdKey = holdKey;
        _holdRow = holdRow;
        _holdSeen = holdSeen;
    }

    /// <summary>The rules for one level, with their scratch sized once so a decision allocates nothing.</summary>
    public static EmbodimentRules For(HordeSettings settings, HordeLevel level, double ticksPerSecond)
    {
        int bodies = settings.Bodies + 1;
        return new EmbodimentRules(settings, level,
            (long)Math.Ceiling(settings.DemoteDelaySeconds * ticksPerSecond),
            (long)Math.Ceiling(settings.SleepDelaySeconds * ticksPerSecond),
            new int[level.RoomCount], new int[level.RoomCount], new bool[bodies],
            new long[settings.RosterCapacity], new int[settings.RosterCapacity],
            new long[bodies], new int[bodies], new bool[bodies]);
    }

    /// <summary>
    /// Stamps the rooms a hero is within the wake distance of, and the bodies a hero is within
    /// the wake distance of or can see, as near at <paramref name="now"/>.
    /// </summary>
    public void Observe(RobotRoster roster, ReadOnlySpan<HeroPlace> heroes, IHeroSight sight, long now)
    {
        HeroDistances(heroes);
        for (int r = 0; r < _level.RoomCount; r++)
            if (_heroDistance[r] <= _settings.WakeRooms) roster.MarkRoomNear(r, now);
        for (int i = 0; i < roster.Count; i++)
            if (roster.StateAt(i) == RobotState.Embodied
                && (_heroDistance[roster.RoomRef(i)] <= _settings.WakeRooms || sight.CanSee(roster.PositionRef(i))))
                roster.MarkNear(i, now);
    }

    /// <summary>This tick's orders, into <paramref name="orders"/> (cleared first). Changes nothing else.</summary>
    public void Decide(RobotRoster roster, BodyPool pool, ReadOnlySpan<HeroPlace> heroes, IHeroSight sight, long now, HordeOrders orders)
    {
        orders.Clear();
        HeroDistances(heroes);
        int wake = _settings.WakeRooms;

        Array.Clear(_bodiesIn);
        for (int i = 0; i < roster.Count; i++)
            if (roster.StateAt(i) is RobotState.Embodied or RobotState.Dead && roster.Body(i) != 0)
                _bodiesIn[roster.RoomRef(i)]++;

        // Rooms.
        for (int r = 0; r < _level.RoomCount; r++)
            if (!roster.RoomAwake(r) && _heroDistance[r] <= wake)
                orders.Add(HordeOrder.Wake(r));
        for (int r = 0; r < _level.RoomCount; r++)
            if (roster.RoomAwake(r) && _heroDistance[r] > wake && _bodiesIn[r] == 0 && roster.RoomLastNear(r) <= now - _sleepTicks)
                orders.Add(HordeOrder.Sleep(r));

        for (int b = 1; b <= pool.LastBody; b++) _free[b] = pool.IsFree(b);

        // Releases and demotions; the rest of the holders rank for pressure.
        int holders = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var state = roster.StateAt(i);
            int body = roster.Body(i);
            if (state == RobotState.Dead && body != 0)
            {
                orders.Add(HordeOrder.Release(roster.Id(i), body));
                _free[body] = true;
                continue;
            }
            if (state != RobotState.Embodied) continue;
            bool boss = roster.BossAt(i);
            bool seen = sight.CanSee(roster.PositionRef(i));
            if (!seen && !(boss && HeroIn(heroes, roster.HomeRoom(i))) && roster.LastNear(i) <= now - _demoteTicks)
            {
                orders.Add(HordeOrder.Demote(roster.Id(i), body));
                _free[body] = true;
                continue;
            }
            if (boss) continue;   // never taken under pressure
            _holdKey[holders] = Rank(roster, i);
            _holdRow[holders] = i;
            holders++;
        }
        _holdKey.AsSpan(0, holders).Sort(_holdRow.AsSpan(0, holders));
        for (int k = 0; k < holders; k++) _holdSeen[k] = sight.CanSee(roster.PositionRef(_holdRow[k]));

        // Claimants.
        int claims = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var state = roster.StateAt(i);
            int room = roster.RoomRef(i);
            bool waking = state == RobotState.Asleep && !roster.RoomAwake(room) && _heroDistance[room] <= wake;
            // Queued at a door a hero sees through: it can go no further bodiless, so it takes its body where it stands, however far.
            bool queued = state == RobotState.Far && roster.Activity(i) == FarActivity.Queue;
            if (!(state == RobotState.Far || waking) || (_heroDistance[room] > wake && !queued)) continue;
            if (roster.BossAt(i))
            {
                int bossBody = pool.BossBody;
                // HO13: once a hero stands in its arena the boss takes its body in view: its entrance (a hero
                // brought straight in, by a portal or a teleport, must not find an empty arena).
                if (bossBody != 0 && _free[bossBody] && (!sight.CanSee(roster.PositionRef(i)) || HeroIn(heroes, roster.HomeRoom(i))))
                {
                    orders.Add(HordeOrder.Promote(roster.Id(i), bossBody, room, roster.PositionRef(i), roster.YawRef(i)));
                    _free[bossBody] = false;
                }
                continue;
            }
            _claimKey[claims] = Rank(roster, i);
            _claimRow[claims] = i;
            claims++;
        }
        _claimKey.AsSpan(0, claims).Sort(_claimRow.AsSpan(0, claims));

        int worst = holders - 1;   // the lowest-ranked holder not yet taken
        int nextFree = 1;
        for (int c = 0; c < claims; c++)
        {
            int i = _claimRow[c];
            if (sight.CanSee(roster.PositionRef(i))) continue;   // never in view
            while (nextFree <= pool.PoolBodies && !_free[nextFree]) nextFree++;
            if (nextFree <= pool.PoolBodies)
            {
                orders.Add(HordeOrder.Promote(roster.Id(i), nextFree, roster.RoomRef(i), roster.PositionRef(i), roster.YawRef(i)));
                _free[nextFree] = false;
                continue;
            }
            while (worst >= 0 && _holdSeen[worst]) worst--;
            if (worst < 0 || _holdKey[worst] <= _claimKey[c]) break;   // no unseen body ranks below this claimant, nor below any after it
            int victim = _holdRow[worst--];
            int body = roster.Body(victim);
            orders.Add(HordeOrder.Demote(roster.Id(victim), body));
            orders.Add(HordeOrder.Promote(roster.Id(i), body, roster.RoomRef(i), roster.PositionRef(i), roster.YawRef(i)));
        }
    }

    /// <summary>
    /// A robot's rank, lower first: the rung (0 same room as a hero, 1 its pack alerted to a
    /// hero, 2 the rest), then room-graph distance to the nearest hero, then champion before
    /// ordinary, then the row. Every robot has its own rank, so the order is total.
    /// </summary>
    public long Rank(RobotRoster roster, int i)
    {
        int d = _heroDistance[roster.RoomRef(i)];
        long rung = d == 0 ? 0 : roster.PackAlertedToHero(roster.PackAt(i)) ? 1 : 2;
        long distance = Math.Min(d, 0xFFFF);
        long ordinary = roster.ChampionAt(i) ? 0 : 1;
        return (rung << 48) | (distance << 32) | (ordinary << 31) | (uint)i;
    }

    private void HeroDistances(ReadOnlySpan<HeroPlace> heroes)
    {
        for (int r = 0; r < _level.RoomCount; r++)
        {
            int best = HordeLevel.Unreachable;
            foreach (var h in heroes)
                if ((uint)h.Room < (uint)_level.RoomCount)
                    best = Math.Min(best, _level.Distance(h.Room, r));
            _heroDistance[r] = best;
        }
    }

    private static bool HeroIn(ReadOnlySpan<HeroPlace> heroes, int room)
    {
        foreach (var h in heroes)
            if (h.Room == room) return true;
        return false;
    }
}
