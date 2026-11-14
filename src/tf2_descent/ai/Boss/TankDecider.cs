using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Rules.Bosses;

namespace TF2.Descent.Ai.Boss;

/// <summary>
/// The Tank's brain (mine plan D11, M14; plan_descent_logic HO13): it fights nobody. It rolls
/// along its track (<see cref="TankTrack"/>) at its own speed, whatever the heroes do; at each
/// station it stops for <c>stopSeconds</c>, drilling the pillar there, and tells the server it
/// has arrived (<see cref="HordeAbilities.TankStation"/>, the station in the ask); at the last
/// station it drills for <c>finalSeconds</c>, then brings the roof down
/// (<see cref="HordeAbilities.TankRoof"/>, once) and holds. A Tank given a body again starts
/// from the station after the one nearest where it stands (the server keeps no track state of
/// its own). Replay-safe: the track is the level's and the stops are counted in view ticks.
/// </summary>
public sealed class TankDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers, TankSettings tank, float margin)
    : FamilyDecider(setup, routes, numbers)
{
    private AiVector[]? _track;
    private int _station = -1;
    private Tick _stopUntil;
    private bool _stopped, _roofed;

    /// <summary>The track it follows (null before its first decide).</summary>
    public IReadOnlyList<AiVector>? Track => _track;

    /// <summary>The station it is going to or stopped at; the last is its final.</summary>
    public int Station => _station;

    /// <summary>Whether it is stopped at a station, drilling, and whether it has brought the roof down.</summary>
    public bool Drilling => _stopped;

    public bool Roofed => _roofed;

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (_track is null)
        {
            var arena = BossArena.Of(Level, Setup.Home ?? self, margin);
            _track = TankTrack.Of(Level, arena, Setup.Home ?? self, (float)tank.CentreClearUnits);
            _station = 0;
        }

        int final = _track.Length - 1;
        var look = new LookIntent(LookKind.Point, _track[_station] + new AiVector(0f, 0f, 48f), ActorId.None, AimPoint.Body, 0f, 0);
        if (_roofed)
            return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, ActionIntent.None, _track[_station], ActorId.None, "the roof is down");

        if (_stopped)
        {
            if (tick < _stopUntil)
                return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, ActionIntent.None, _track[_station], ActorId.None, "drilling");
            _stopped = false;
            if (_station == final)
            {
                _roofed = true;
                return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look, Ability(HordeAbilities.TankRoof), _track[_station], ActorId.None, "the roof");
            }

            _station++;
        }

        var goal = _track[_station];
        if (self.DistanceTo(goal with { Z = self.Z }) <= tank.ArriveUnits)
        {
            _stopped = true;
            double seconds = _station == final ? tank.FinalSeconds : tank.StopSeconds;
            _stopUntil = tick + Math.Max(1, (int)MathF.Ceiling((float)seconds / Setup.TickSeconds));
            return End(tick, seq, FamilyNode.Drill, MoveIntent.Hold, look,
                       new ActionIntent(ActionKind.Ability, FireStyle.Tap, HordeAbilities.TankStation, _station), goal, ActorId.None, "station");
        }

        return End(tick, seq, FamilyNode.Track, Route.Follow(tick, view.Self, self, goal, MoveSpeed.Run), look, ActionIntent.None, goal,
                   ActorId.None, "rolling");
    }
}
