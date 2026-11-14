using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Doors;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Motion;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde.Motion;

namespace TF2.Descent.Server.Horde.Doors;

/// <summary>What the door shove asks of a hero: his knockback resistance (a fraction), and whether he is exempt (downed).</summary>
public interface IHeroShoveTraits
{
    (double KnockbackResist, bool Exempt) Of(int seat);
}

/// <summary>
/// The heroes' own: the sheet's knockback resistance (Brawn's, <c>HeroStats.KnockbackResist</c>),
/// and exempt while downed (the hero state's <c>Downed</c>, on the wire and false until the downed
/// package, WO6). A seat with no hero resists nothing and is not exempt.
/// </summary>
[SideService(Side.Server, As = [typeof(IHeroShoveTraits)])]
public sealed class HeroShoveTraits(Heroes heroes) : IHeroShoveTraits
{
    public (double KnockbackResist, bool Exempt) Of(int seat) =>
        heroes.Of(seat) is { } hero ? (hero.Stats?.KnockbackResist ?? 0, hero.State?.Downed == true) : (0, false);
}

/// <summary>One door's shove as the dev command prints it.</summary>
public readonly record struct DoorShoveState(int RobotsBlocked, long BlockedTick, long LastShove, int LastHero, int LastBody, AiVector LastImpulse, int Shoves);

/// <summary>
/// The door shove on the server (plan_descent_logic L9, 3.9, HO11), run by the horde controller
/// after the motion each tick. A lent body is a candidate at a door when a hero standing in it
/// holds the robot there (<see cref="Holding"/>): its motion reports that hero stopped it and its
/// path (its corners left) goes into or through the door (<see cref="DoorShoveRules.DoorFor"/>),
/// or it stands within its melee reach of him (<see cref="DoorShoveRules.InContact"/>, the
/// fighting robot, which a brain halts at its range and so is never stopped by him). A candidate
/// held for its dwell (none for a low-discipline robot) may shove; once a door's cooldown has
/// run, the largest such robot shoves the hero along the door's normal away from it, as a
/// velocity impulse through the engine. No damage, and the brains are not asked: 3.9's "next
/// melee intent is a shove" is carried out here, so a robot of any family clears a doorway.
/// The heroes are gathered each tick (a few origin reads).
/// </summary>
[SideService(Side.Server)]
public sealed class DoorShove(IRobotMotionReports reports, IHordeHeroes heroes, IEngineEntities entities, IHeroShoveTraits traits, IHordeRules rules)
{
    private readonly HeroPlace[] _places = new HeroPlace[HordeController.MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[HordeController.MaxHeroes];
    private readonly AiVector[] _path = new AiVector[MoveIntent.MaxCorners];

    private HordeLevel? _level;
    private DoorShoveRules? _rules;
    private int _discipline = -1;
    private DoorShoveState[] _doors = [];
    private long[] _contactSince = [];   // by body: the tick its contact with a hero in a doorway began; 0 for none
    private float _heroHalf;            // the standing hull's half-width (robot_rules.json's push hull): a hero's, and a robot's at scale 1

    // This tick's best candidate at each door: the body, its scale and its hero's place.
    private int[] _bestBody = [];
    private double[] _bestScale = [];
    private int[] _bestHero = [];

    /// <summary>Shoves given since the server started.</summary>
    public long Shoves { get; private set; }

    /// <summary>The shove's rules for the level, once the controller has ticked on it.</summary>
    public DoorShoveRules? Rules => _rules;

    /// <summary>Each door's state on the level last ticked (index by the level's door).</summary>
    public ReadOnlySpan<DoorShoveState> Doors => _doors;

    public void Tick(HordeController controller, long now)
    {
        if (controller.Level is not { } level || controller.Bodies is not { } bodies || controller.Roster is not { } roster)
            return;
        if (!ReferenceEquals(level, _level))
            Begin(level, roster.TicksPerSecond, bodies.LastBody);

        for (int d = 0; d < _doors.Length; d++)
        {
            _bestBody[d] = 0;
            if (_doors[d].BlockedTick != now) _doors[d] = _doors[d] with { RobotsBlocked = 0 };
        }

        int n = heroes.Gather(level, _places, _eyes);
        var shove = _rules!;
        for (int b = 1; b <= bodies.LastBody; b++)
        {
            var body = bodies.Body(b);
            if (body.Parked || !roster.IsLive(body.Robot) || n == 0 || !reports.TryView(b, out var view))
            {
                _contactSince[b] = 0;
                continue;
            }
            int i = body.Robot.Index;
            var (d, h, held) = Holding(level, roster, i, b, in view, n, now);
            if (d < 0)
                continue;

            var s = _doors[d];
            _doors[d] = s with { RobotsBlocked = s.BlockedTick == now ? s.RobotsBlocked + 1 : 1, BlockedTick = now };
            int discipline = roster.Temperament(i).Levels[_discipline];
            if (held < shove.DwellTicks(discipline))
                continue;
            double scale = roster.Scale(i);
            if (_bestBody[d] == 0 || scale > _bestScale[d])
            {
                _bestBody[d] = b;
                _bestScale[d] = scale;
                _bestHero[d] = h;
            }
        }

        for (int d = 0; d < _doors.Length; d++)
        {
            int b = _bestBody[d];
            if (b == 0 || (_doors[d].Shoves > 0 && now - _doors[d].LastShove < shove.CooldownTicks))
                continue;
            reports.TryView(b, out var view);
            var hero = _places[_bestHero[d]];
            var impulse = shove.Impulse(level.Doors[d], view.Position, hero.Position, _bestScale[d], traits.Of(hero.Seat).KnockbackResist);
            if (impulse == AiVector.Zero)
                continue;
            entities.ApplyVelocityImpulse(_eyes[_bestHero[d]].Player, HordeVectors.ToEngine(impulse), clearGround: impulse.Z > 0);
            Shoves++;
            _doors[d] = _doors[d] with { LastShove = now, LastHero = hero.Seat, LastBody = b, LastImpulse = impulse, Shoves = _doors[d].Shoves + 1 };
        }
    }

    /// <summary>
    /// The door robot <paramref name="i"/> on body <paramref name="b"/> is held at, the hero
    /// holding it, and for how many ticks; door -1 for none. Held up: its motion says that hero
    /// stopped it, and its path goes into or through a door he stands in, for as long as the
    /// motion's blocked run. Else in contact: it stands within its melee reach of a hero
    /// standing in a doorway, within the shove's reach of the door's plane, for as long as it
    /// has been so. An exempt (downed) hero holds nothing.
    /// </summary>
    private (int Door, int Hero, long Held) Holding(HordeLevel level, TF2.Descent.Ai.Roster.RobotRoster roster, int i, int b,
                                                    in RobotMotionView view, int n, long now)
    {
        var shove = _rules!;
        if (Blocked(b, now) && HeroAt(view.Blocked.Who, n) is var h && h >= 0 && !traits.Of(_places[h].Seat).Exempt)
        {
            int corners = 0;
            for (int k = view.Corner; k < view.Move.CornerCount; k++)
                _path[corners++] = view.Move.Corner(k);
            int d = shove.DoorFor(level, view.Position, _path.AsSpan(0, corners), _places[h].Position);
            if (d >= 0)
            {
                _contactSince[b] = 0;
                return (d, h, view.Blocked.For(now));
            }
        }

        float half = (float)(_heroHalf * roster.Scale(i));
        double reach = roster.Stats(i).Reach;
        for (int k = 0; k < n; k++)
        {
            if (!DoorShoveRules.InContact(view.Position, half, reach, _places[k].Position, _heroHalf) || traits.Of(_places[k].Seat).Exempt)
                continue;
            int d = shove.DoorHeld(level, view.Position, _places[k].Position);
            if (d < 0)
                continue;
            if (_contactSince[b] == 0) _contactSince[b] = now;
            return (d, k, now - _contactSince[b] + 1);
        }

        _contactSince[b] = 0;
        return (-1, -1, 0);
    }

    /// <summary>Whether body <paramref name="b"/>'s motion says a hero stopped it this tick or the last.</summary>
    private bool Blocked(int b, long now)
    {
        var r = reports.Blocked(b);
        return r.Kind == BlockerKind.Hero && r.Last >= now - 1;
    }

    private int HeroAt(int seat, int n)
    {
        for (int h = 0; h < n; h++)
            if (_places[h].Seat == seat) return h;
        return -1;
    }

    private void Begin(HordeLevel level, double ticksPerSecond, int lastBody)
    {
        _level = level;
        _contactSince = new long[lastBody + 1];
        _heroHalf = (float)(rules.Rules.Content.RobotRules.Push.HullWidth / 2);
        _rules = new DoorShoveRules(rules.Rules.Content.Horde, ticksPerSecond);
        _discipline = rules.Rules.Temperaments.Index("discipline");
        _doors = new DoorShoveState[level.Doors.Length];
        _bestBody = new int[level.Doors.Length];
        _bestScale = new double[level.Doors.Length];
        _bestHero = new int[level.Doors.Length];
    }
}
