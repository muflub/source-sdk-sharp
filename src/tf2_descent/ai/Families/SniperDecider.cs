using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The sniper (plan_descent_logic HO12; plan_tf2_rpg 5c's sniper hold, stock's sniper bot as the
/// reference): it holds far back with its hero in sight, aims at the head, and shows its laser
/// before every shot.
///
/// <list type="number">
/// <item><b>Aim.</b> With a hero in sight it holds where it stands (it walked to a post to see
/// from; it does not close), looks at the hero's head thrown off by its <c>aimErrorDeg</c>
/// (Accuracy), and asks for the shot while its gun points within
/// <see cref="FamilyNumbers.SniperToleranceDeg"/>, the line is clear and no pack-mate is in it.
/// The hands charge the shot for the rules' windup, drawing the laser along the gun the while
/// (the telegraph), and fire where the gun points when the charge ends: a hero who sees the laser
/// and moves gets out of it.</item>
/// <item><b>Relocate.</b> A hero nearer than its range times its <c>crowdedShare</c> (Nerve: a
/// fearless sniper lets one closer) has rushed it: it runs straight away and does not shoot, for
/// <see cref="FamilyNumbers.SniperRelocateSeconds"/>; still rushed then (cornered), it stands and shoots
/// for as long, and runs again.</item>
/// <item>No hero in sight: the hunt.</item>
/// </list>
/// </summary>
public sealed class SniperDecider : FamilyDecider
{
    private readonly float _crowded;
    private Tick _rushedAt;

    public SniperDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : base(setup, routes, numbers) =>
        _crowded = HordeTemperament.At(numbers.Curves, "crowdedShare", HordeTemperament.Nerve(numbers.Curves, setup.Params));

    /// <summary>How near a hero this sniper lets come before it runs.</summary>
    public float RushUnits => HordeWeapons.EngageRange(HordeWeapons.SniperRifle) * _crowded;

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        float d = self.DistanceTo(hero.Origin);
        AiVector eye = EyeOf(self);
        AiVector head = hero.Origin + new AiVector(0f, 0f, FamilyNumbers.HeadUnits);
        AiVector aim = Err(tick, eye, head, Params.AimErrorDeg);
        var look = new LookIntent(LookKind.Point, aim, hero.Id, AimPoint.Head, 0f, 0);

        // Rushed: it runs for a spell; still rushed after it (cornered), it stands and shoots for as long, then runs again.
        int spell = Math.Max(1, (int)MathF.Ceiling(FamilyNumbers.SniperRelocateSeconds / Setup.TickSeconds));
        bool rushed = d < RushUnits;
        if (!rushed)
            _rushedAt = default;
        else if (_rushedAt == default)
            _rushedAt = tick;
        bool cornered = rushed && (tick.Value - _rushedAt.Value) / spell % 2 == 1;
        if (rushed && !cornered)
            return End(tick, seq, FamilyNode.Relocate, Along(MoveKind.Retreat, Flat(self - hero.Origin)), look, ActionIntent.None, hero.Origin, hero.Id,
                       "hold: rushed");

        bool aimed = HordeView.AimOff(view.Self.Eye, eye, aim) <= FamilyNumbers.SniperToleranceDeg;
        bool clear = LineClear(view, eye, aim, FamilyNumbers.HullReachUnits), mate = MateInLine(view, eye, aim);
        bool fire = aimed && clear && !mate;
        string verdict = fire ? (cornered ? "fire: cornered" : "fire") : !aimed ? "hold: settling" : !clear ? "hold: line short" : "hold: mate in line";
        return End(tick, seq, FamilyNode.Aim, MoveIntent.Hold, look, fire ? FireAction : ActionIntent.None, hero.Origin, hero.Id, verdict);
    }
}
