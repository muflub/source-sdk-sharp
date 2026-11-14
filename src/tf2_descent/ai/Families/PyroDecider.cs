using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The flame pyro (plan_descent_logic HO12; stock's pyro bot as the reference): it closes and
/// burns.
///
/// <list type="number">
/// <item><b>Close.</b> Farther than <see cref="FamilyNumbers.BurnShare"/> of its flame's reach
/// times its <c>preferredRangeScale</c> (Aggression), it runs at its hero along its route,
/// flame on as soon as the hero is inside the reach.</item>
/// <item><b>Burn.</b> Nearer, it keeps running at the hero, facing it, and asks for the flame
/// every decide while the hero is inside the reach and the cone (the rules' half-angle); the
/// hands keep the flame's own cadence and set the hero alight for the rules' afterburn.</item>
/// <item><b>Back off.</b> Its life under its <c>retreatHealthPct</c> (Nerve), it backs
/// straight away from the hero, flame still on a hero in reach.</item>
/// <item>No hero in sight: the hunt.</item>
/// </list>
/// </summary>
public sealed class PyroDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : FamilyDecider(setup, routes, numbers)
{
    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        float range = Numbers.FlameRange;
        float d = self.DistanceTo(hero.Origin);
        AiVector eye = EyeOf(self);
        AiVector chest = Chest(hero);
        var look = new LookIntent(LookKind.Point, chest, hero.Id, AimPoint.Body, 0f, 0);
        bool inCone = HordeView.AimOff(view.Self.Eye, eye, chest) <= Numbers.FlameConeDeg;
        bool fire = eye.DistanceTo(chest) <= range && inCone && LineClear(view, eye, chest, FamilyNumbers.SplashSlackUnits);
        var action = fire ? FireAction : ActionIntent.None;

        if (Hurt(view))
            return End(tick, seq, FamilyNode.BackOff, Along(MoveKind.Retreat, Flat(self - hero.Origin)), look, action, hero.Origin, hero.Id);
        if (d > range * FamilyNumbers.BurnShare * Params.PreferredRangeScale)
            return End(tick, seq, FamilyNode.Close, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, action, hero.Origin, hero.Id);
        var press = d > FamilyNumbers.PyroStandUnits ? Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run) : MoveIntent.Hold;
        return End(tick, seq, FamilyNode.Burn, press, look, action, hero.Origin, hero.Id);
    }
}
