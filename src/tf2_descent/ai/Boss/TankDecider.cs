using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Rules.Bosses;

namespace TF2.Descent.Ai.Boss;

/// <summary>
/// The Tank's brain (mine plan D11, M14; plan_descent_logic HO13): it fights nobody. It rolls
/// along its route (<see cref="TankTrack.Route"/>) at its own speed, whatever the heroes do, from
/// the route's first point in order, round the loop. At each station not yet drilled it stops for
/// <c>stopSeconds</c>, drilling the pillar there: it tells the server it has arrived
/// (<see cref="HordeAbilities.TankStation"/>) and, when the drill ends, that the station is drilled
/// (<see cref="HordeAbilities.TankDrilled"/>), each with the station's number. A point that is no
/// station, or a station drilled already, it rolls through. The final station it drills only once
/// every other station is drilled (rolling past it round the closed loop before then), for
/// <c>finalSeconds</c>; then it brings the roof down (<see cref="HordeAbilities.TankRoof"/>, once,
/// with the final's number) and holds. A Tank given a body again starts its route over (the server
/// keeps no track state of its own). Replay-safe: the route is the level's and the stops are
/// counted in view ticks.
/// </summary>
public sealed class TankDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers, TankSettings tank, float margin)
    : FamilyDecider(setup, routes, numbers)
{
    private TankStop[]? _route;
    private AiVector[]? _track;
    private bool[]? _drilled;
    private int _at = -1, _left;
    private Tick _stopUntil;
    private bool _stopped, _roofed;

    /// <summary>The route's points (null before its first decide).</summary>
    public IReadOnlyList<AiVector>? Track => _track;

    /// <summary>The route, with each point's station number and the final (null before its first decide).</summary>
    public IReadOnlyList<TankStop>? Stops => _route;

    /// <summary>The route point it is going to or stopped at.</summary>
    public int Station => _at;

    /// <summary>Whether it is stopped at a station, drilling, and whether it has brought the roof down.</summary>
    public bool Drilling => _stopped;

    public bool Roofed => _roofed;

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (_route is null)
        {
            var arena = BossArena.Of(Level, Setup.Home ?? self, margin);
            _route = TankTrack.Route(Level, arena, Setup.Home ?? self, (float)tank.CentreClearUnits);
            _track = Array.ConvertAll(_route, s => s.Position);
            _drilled = new bool[_route.Length];
            foreach (var s in _route)
                if (s.Station > 0 && !s.Final)
                    _left++;
            _at = 0;
        }

        var here = _route[_at];
        var look = new LookIntent(LookKind.Point, here.Position + new AiVector(0f, 0f, 48f), ActorId.None, AimPoint.Body, 0f, 0);
        if (_roofed)
            return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, ActionIntent.None, here.Position, ActorId.None, "the roof is down");

        if (_stopped)
        {
            if (tick < _stopUntil)
                return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, ActionIntent.None, here.Position, ActorId.None, "drilling");
            _stopped = false;
            if (here.Final)
            {
                _roofed = true;
                return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, Ask(HordeAbilities.TankRoof, here.Station), here.Position, ActorId.None, "the roof");
            }

            _drilled![_at] = true;
            _left--;
            _at = (_at + 1) % _route.Length;
            return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, Ask(HordeAbilities.TankDrilled, here.Station), here.Position, ActorId.None, "drilled");
        }

        // Roll through every point it has no stop at; at most one lap before a stop is due.
        for (int k = 0; k <= _route.Length; k++)
        {
            here = _route[_at];
            if (self.DistanceTo(here.Position with { Z = self.Z }) > tank.ArriveUnits)
                break;
            bool stop = here.Final ? _left == 0 : here.Station > 0 && !_drilled![_at];
            if (stop)
            {
                _stopped = true;
                double seconds = here.Final ? tank.FinalSeconds : tank.StopSeconds;
                _stopUntil = tick + Math.Max(1, (int)MathF.Ceiling((float)seconds / Setup.TickSeconds));
                look = new LookIntent(LookKind.Point, here.Position + new AiVector(0f, 0f, 48f), ActorId.None, AimPoint.Body, 0f, 0);
                return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, Ask(HordeAbilities.TankStation, here.Station), here.Position, ActorId.None, "station");
            }

            _at = (_at + 1) % _route.Length;
        }

        var goal = _route[_at].Position;
        look = new LookIntent(LookKind.Point, goal + new AiVector(0f, 0f, 48f), ActorId.None, AimPoint.Body, 0f, 0);
        return End(tick, seq, FamilyNode.Track, Route.Follow(tick, view.Self, self, goal, MoveSpeed.Run), look, ActionIntent.None, goal,
                   ActorId.None, "rolling");
    }

    private static ActionIntent Ask(int ability, int station) => new(ActionKind.Ability, FireStyle.Tap, ability, station);
}
