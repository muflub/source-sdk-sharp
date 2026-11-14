using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The shotgunner (plan_descent_logic HO12: the engineer robot's shotgun, the simplest fight
/// that is still one): it holds a band and fires.
///
/// <list type="number">
/// <item><b>The band</b> is <see cref="FamilyNumbers.GunnerBandUnits"/> times its
/// <c>preferredRangeScale</c> (Aggression). Past its outer edge it closes along its route;
/// nearer than its <c>crowdedShare</c> (Nerve) it backs straight away; inside it holds, or strafes
/// across the hero's line with the chance its <c>strafeWeight</c> gives (Agility).</item>
/// <item><b>Fire</b> as the scout does: inside the band's edge times its <c>fireRangeGate</c>
/// (Discipline), aimed within <see cref="FamilyNumbers.SpreadToleranceDeg"/> of the chest
/// thrown off by its <c>aimErrorDeg</c> (Accuracy), the line clear and no pack-mate in it.</item>
/// <item>No hero in sight: the hunt.</item>
/// </list>
/// </summary>
public sealed class GunnerDecider : FamilyDecider
{
    /// <summary>The band's outer edge, a share of the band.</summary>
    public const float BandOuter = 1.15f;

    private readonly float _crowded;

    public GunnerDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : base(setup, routes, numbers) =>
        _crowded = HordeTemperament.At(numbers.Curves, "crowdedShare", HordeTemperament.Nerve(numbers.Curves, setup.Params));

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        DecisionParams p = Params;
        float band = FamilyNumbers.GunnerBandUnits * p.PreferredRangeScale;
        float d = self.DistanceTo(hero.Origin);
        AiVector eye = EyeOf(self);
        AiVector aim = Err(tick, eye, Chest(hero), p.AimErrorDeg);
        var look = new LookIntent(LookKind.Point, aim, hero.Id, AimPoint.Body, 0f, 0);

        bool aimed = HordeView.AimOff(view.Self.Eye, eye, aim) <= FamilyNumbers.SpreadToleranceDeg;
        bool fire = d <= band * BandOuter * p.FireRangeGate && aimed && LineClear(view, eye, aim, FamilyNumbers.SplashSlackUnits) && !MateInLine(view, eye, aim);
        var action = fire ? FireAction : ActionIntent.None;

        AiVector away = Flat(self - hero.Origin);
        if (d < band * _crowded)
            return End(tick, seq, FamilyNode.BackOff, Along(MoveKind.Retreat, away), look, action, hero.Origin, hero.Id);
        if (d > band * BandOuter)
            return End(tick, seq, FamilyNode.Close, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, action, hero.Origin, hero.Id);
        if (Strafe(tick, out float side))
            return End(tick, seq, FamilyNode.Strafe, Along(MoveKind.Strafe, new AiVector(-away.Y * side, away.X * side, 0f)), look, action, hero.Origin, hero.Id);
        return End(tick, seq, FamilyNode.Band, MoveIntent.Hold, look, action, hero.Origin, hero.Id);
    }
}
