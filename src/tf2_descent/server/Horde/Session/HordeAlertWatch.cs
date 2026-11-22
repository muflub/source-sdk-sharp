using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Senses;

namespace TF2.Descent.Server.Horde.Session;

/// <summary>
/// <c>tf2_descent_horde_alert_log &lt;0|1&gt;</c>: the horde's alerts as log lines (dev): each robot's
/// first alert of a spell (<see cref="HordeAlerts"/>) and, once a second, who is hunting.
/// </summary>
[ConVar("tf2_descent_horde_alert_log", "0", Flags = ConVarFlags.Cheat,
        Help = "tf2_descent_horde_alert_log <0|1>: log each robot's first alert of a spell (what sense or rule, from where, the hero's distance, rooms apart, line of sight) and, once a second, the robots hunting")]
public sealed class HordeAlertLogConVar : BoolConVar { }

/// <summary>
/// The horde's alert log as lines (dev; <see cref="HordeAlertLogConVar"/>). After the horde's tick, the
/// alerts recorded since the last frame, each with the nearest hero's distance, the rooms between
/// them on the room graph and whether a line from the robot's eye reaches his: <c>ALERT</c> lines.
/// Once a second the robots hunting: a bodied robot whose decider is on a hunting leaf (not home,
/// hold or a medic's follow), and a far robot converging on its pack's contact: a <c>HUNT</c> line
/// when the set changed.
/// </summary>
[SideService(Side.Server)]
public sealed class HordeAlertWatch(HordeService horde, HordeSession session, IHordeHeroes heroes, ISightWorld world, IHordeClock clock,
                                    HordeAlertLogConVar enabled, ILog log) : IFrameLifecycle
{
    public const int Order = ModStages.First + 509;

    /// <summary>The leaves a bodied robot is not hunting on.</summary>
    public static bool Calm(string leaf) => leaf is "home" or "hold" or "follow" or "";

    private readonly AlertRecord[] _drained = new AlertRecord[HordeAlerts.Capacity];
    private readonly HeroPlace[] _places = new HeroPlace[HordeController.MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[HordeController.MaxHeroes];
    private long _nextHunt;
    private string _lastHunt = "";
    private HordeController? _level;

    public int FrameOrder => Order;

    public void OnThink(float frameTime)
    {
        if (!enabled.Value || horde.Current is not { Begun: true, Alerts: { } alerts, Level: { } level, Roster: { } roster } c)
            return;

        long now = clock.Tick;
        if (!ReferenceEquals(c, _level))
        {
            // A new level: the server's tick count starts again, so the census's clock does too.
            _level = c;
            _nextHunt = now;
            _lastHunt = "";
        }

        int n = alerts.Drain(_drained);
        bool hunt = now >= _nextHunt;
        if (n == 0 && !hunt)
            return;

        int heroCount = heroes.Gather(level, _places, _eyes);
        for (int k = 0; k < n; k++)
            log.Msg(Line(in _drained[k], level, roster, heroCount) + "\n");
        if (hunt)
        {
            _nextHunt = now + (long)Math.Ceiling(clock.TicksPerSecond);
            string line = Hunting(c, roster, heroCount);
            if (line != _lastHunt)
            {
                _lastHunt = line;
                log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Horde] HUNT t={now / clock.TicksPerSecond:0.0}s {line}\n"));
            }
        }
    }

    private string Line(in AlertRecord r, TF2.Descent.Ai.Level.HordeLevel level, RobotRoster roster, int heroCount)
    {
        var (dist, heroRoom, los) = Nearest(r.RobotAt, level, heroCount);
        int apart = r.RobotRoom >= 0 && heroRoom >= 0 ? level.Distance(r.RobotRoom, heroRoom) : -1;
        string cause = r.Cause is AlertCause.Sound or AlertCause.FarSound or AlertCause.PackCall && r.Kind != SoundKind.Unknown
            ? $"{r.Cause}:{r.Kind}" : r.Cause.ToString();
        string template = (uint)r.Row < (uint)roster.Count ? roster.Template(r.Row) : "?";
        return string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] ALERT t={r.Tick / clock.TicksPerSecond:0.0}s robot {r.Row} ({template}, pack {r.Pack}, {(r.Bodied ? "bodied" : "far")}) room {r.RobotRoom} at ({r.RobotAt.X:0},{r.RobotAt.Y:0},{r.RobotAt.Z:0}) "
            + $"cause {cause} from ({r.SourceAt.X:0},{r.SourceAt.Y:0},{r.SourceAt.Z:0}) room {r.SourceRoom} seat {r.Seat} source dist {r.RobotAt.DistanceTo(r.SourceAt):0}; "
            + $"hero dist {dist:0} room {heroRoom} rooms apart {apart} los {(los ? "yes" : "no")}");
    }

    private (float Distance, int Room, bool Los) Nearest(AiVector at, TF2.Descent.Ai.Level.HordeLevel level, int heroCount)
    {
        float best = float.PositiveInfinity;
        int k = -1;
        for (int h = 0; h < heroCount; h++)
        {
            float d = at.DistanceTo(_places[h].Position);
            if (d < best)
                (best, k) = (d, h);
        }

        if (k < 0)
            return (float.PositiveInfinity, -1, false);
        var eye = new Vector(at.X, at.Y, at.Z + 72f);
        return (best, _places[k].Room, world.LineClear(eye, _eyes[k].Eye));
    }

    private string Hunting(HordeController c, RobotRoster roster, int heroCount)
    {
        var bodied = new SortedDictionary<int, int>();
        var far = new SortedDictionary<int, int>();
        int nb = 0, nf = 0;
        float farthest = 0f;
        for (int i = 0; i < roster.Count; i++)
        {
            var state = roster.State(i);
            if (state == RobotState.Embodied && session.Brains is { } b && roster.Body(i) is var body and > 0 && b.BrainAt(body).IsValid)
            {
                string leaf = b.Session.TryReadBrain(body, br => br.Decider is { } d ? d.Doing.Leaf : "", out string? read) ? read ?? "" : "busy";
                if (Calm(leaf) || leaf == "busy")
                    continue;
                nb++;
                bodied[roster.Room(i)] = bodied.GetValueOrDefault(roster.Room(i)) + 1;
            }
            else if (state == RobotState.Far && roster.Activity(i) == FarActivity.Converge)
            {
                nf++;
                far[roster.Room(i)] = far.GetValueOrDefault(roster.Room(i)) + 1;
            }
            else
            {
                continue;
            }

            if (heroCount > 0)
                farthest = MathF.Max(farthest, Nearest(roster.Position(i), c.Level!, heroCount).Distance);
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"bodied {nb} in {bodied.Count} rooms [");
        sb.AppendJoin(' ', bodied.Select(kv => $"{kv.Key}:{kv.Value}"));
        sb.Append(CultureInfo.InvariantCulture, $"], far converging {nf} in {far.Count} rooms [");
        sb.AppendJoin(' ', far.Select(kv => $"{kv.Key}:{kv.Value}"));
        sb.Append(CultureInfo.InvariantCulture, $"], hero room {(heroCount > 0 ? _places[0].Room : -1)}, farthest {farthest:0}");
        return sb.ToString();
    }
}
