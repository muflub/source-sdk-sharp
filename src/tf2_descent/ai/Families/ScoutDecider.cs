using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The scattergun scout (plan_descent_logic HO12; plan_tf2_rpg 5c, stock's scout bot as the
/// reference): a fast flanker that fights inside its band and never stands still in it.
///
/// <list type="number">
/// <item><b>The band</b> is <see cref="FamilyNumbers.ScoutBandUnits"/> times its
/// <c>preferredRangeScale</c> (Aggression). Past the band's outer edge it closes along its
/// route.</item>
/// <item><b>Circle.</b> Inside, it runs round its hero: across the hero's line to a side it
/// chooses again every <c>strafeMeanSeconds</c> or so (Agility), drawn in or out to hold the
/// band.</item>
/// <item><b>Back off.</b> Its life under its <c>retreatHealthPct</c> (Nerve) with a hero inside
/// one and a half bands: it runs straight away, still shooting.</item>
/// <item><b>Leash.</b> Past its <c>leashUnits</c> from its pack-mates' centre (Soloness: a
/// lone scout roams), it circles back towards them rather than out.</item>
/// <item><b>Fire</b> when the hero is inside the band's outer edge times its
/// <c>fireRangeGate</c> (Discipline), the gun points within
/// <see cref="FamilyNumbers.SpreadToleranceDeg"/> of its aim (the hero's chest thrown off by its
/// <c>aimErrorDeg</c>, Accuracy: a hitscan needs no lead), the line is clear and no pack-mate
/// stands in it.</item>
/// <item>No hero in sight: the hunt.</item>
/// </list>
/// </summary>
public sealed class ScoutDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : FamilyDecider(setup, routes, numbers)
{
    /// <summary>The band's outer edge, a share of the band.</summary>
    public const float BandOuter = 1.15f;

    /// <summary>Where the last decide aimed.</summary>
    public AiVector LastAim { get; private set; }

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        DecisionParams p = Params;
        float band = FamilyNumbers.ScoutBandUnits * p.PreferredRangeScale;
        float d = self.DistanceTo(hero.Origin);
        AiVector eye = EyeOf(self);
        AiVector aim = Err(tick, eye, Chest(hero), p.AimErrorDeg);
        LastAim = aim;
        var look = new LookIntent(LookKind.Point, aim, hero.Id, AimPoint.Body, 0f, 0);

        bool aimed = HordeView.AimOff(view.Self.Eye, eye, aim) <= FamilyNumbers.SpreadToleranceDeg;
        bool inRange = d <= band * BandOuter * p.FireRangeGate;
        bool clear = LineClear(view, eye, aim, FamilyNumbers.SplashSlackUnits), mate = MateInLine(view, eye, aim);
        bool fire = inRange && aimed && clear && !mate;
        var action = fire ? FireAction : ActionIntent.None;
        string verdict = fire ? "fire" : !inRange ? "hold: out of range" : !aimed ? "hold: off aim" : !clear ? "hold: line short" : "hold: mate in line";

        AiVector away = Flat(self - hero.Origin);
        if (Hurt(view) && d < band * 1.5f)
            return End(tick, seq, FamilyNode.BackOff, Along(MoveKind.Retreat, away), look, action, hero.Origin, hero.Id, verdict);
        if (d > band * BandOuter)
            return End(tick, seq, FamilyNode.Close, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, action, hero.Origin, hero.Id, verdict);

        Strafe(tick, out float side, always: true);
        var across = new AiVector(-away.Y * side, away.X * side, 0f);
        float hold = Math.Clamp((d - band) / Math.Max(1f, band) * 3f, -1f, 1f);   // + draws in, - pushes out
        AiVector dir = across - away * hold;
        if (Leashed(view, self, out AiVector centre))
            dir = Flat(centre - self) + across * 0.5f;
        return End(tick, seq, FamilyNode.Circle, Along(MoveKind.Strafe, dir), look, action, hero.Origin, hero.Id, verdict);
    }
}
