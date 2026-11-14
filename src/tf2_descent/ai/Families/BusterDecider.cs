using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The sentry buster (plan_descent_logic HO12; stock's <c>CTFBotMissionSuicideBomber</c> as the
/// reference): it runs at a hero and blows itself up beside it. No hero builds anything here, so
/// a hero is always its target.
///
/// <list type="number">
/// <item><b>Run.</b> With a hero in sight it runs at the nearest along its route, facing it.</item>
/// <item><b>Detonate.</b> Inside the rules' trigger with a clear line to the hero (stock: a third
/// of the blast's reach, line of fire clear), it stops and asks for its blast once; from then on
/// it holds and asks again every decide (the hands spin it up once, stock's two seconds and
/// its spin sound, and refuse the rest). It never turns back.</item>
/// <item>No hero in sight and not yet spinning: the hunt (it runs to where it last saw one).</item>
/// </list>
/// </summary>
public sealed class BusterDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : FamilyDecider(setup, routes, numbers)
{
    private bool _detonating;
    private AiVector _at;

    /// <summary>Whether it has started its blast.</summary>
    public bool Detonating => _detonating;

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        bool seen = NearestHero(view, self, out ActorFacts hero);
        if (_detonating)
        {
            var hold = seen ? new LookIntent(LookKind.Actor, hero.Origin, hero.Id, AimPoint.Body, 0f, 0) : LookIntent.AlongPath;
            return End(tick, seq, FamilyNode.Detonate, MoveIntent.Hold, hold, FireAction, _at, seen ? hero.Id : ActorId.None);
        }

        if (!seen)
            return Hunt(tick, view, seq);

        var look = new LookIntent(LookKind.Actor, hero.Origin, hero.Id, AimPoint.Body, 0f, 0);
        // The line of fire runs along the look at the hero, so it stops on his hull: clear when it reaches him.
        AiVector eye = EyeOf(self);
        if (self.DistanceTo(hero.Origin) <= Numbers.BusterTrigger && LineClear(view, eye, Chest(hero), FamilyNumbers.HullReachUnits))
        {
            _detonating = true;
            _at = self;
            return End(tick, seq, FamilyNode.Detonate, MoveIntent.Hold, look, FireAction, self, hero.Id);
        }

        return End(tick, seq, FamilyNode.Run, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, ActionIntent.None, hero.Origin, hero.Id);
    }
}
