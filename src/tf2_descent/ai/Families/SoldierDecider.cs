using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The rocket soldier (plan_descent_logic HO6; stock's soldier bot as the reference): it fights
/// from a band, leads its rockets, aims at the feet from above, and holds fire when its line is
/// not clear.
///
/// <list type="number">
/// <item><b>The band</b> is its family's range times its <c>preferredRangeScale</c>
/// (Aggression). Past the band it closes along the room route; nearer than
/// <c>crowdedShare</c> of it (Nerve: a fearless one lets a hero closer) it backs off, straight
/// away from the hero; inside, every <c>strafeMeanSeconds</c> or so it chooses again: to strafe
/// across the hero's line, to a seeded side, with the chance its <c>strafeWeight</c> gives
/// (Agility), or to hold.</item>
/// <item><b>The aim</b>: a hero's chest, or its feet when the soldier stands
/// <see cref="FamilyNumbers.AboveUnits"/> higher and a seeded roll falls under its
/// <c>feetAimShare</c> (Cunning); led by where the hero will be when a rocket at the rules' speed
/// gets there, times its <c>leadQuality</c> (Accuracy), then thrown off by up to its
/// <c>aimErrorDeg</c> (Accuracy). The look goes there; the motion turns the gun at its own rate.</item>
/// <item><b>Fire</b> when the hero is inside its band's outer edge times its <c>fireRangeGate</c>
/// (Discipline), the gun points within <see cref="FamilyNumbers.FireToleranceDeg"/> of the aim,
/// the line of fire the view carries runs to within <see cref="FamilyNumbers.SplashSlackUnits"/>
/// of the aim point, and no pack-mate stands in the line.</item>
/// <item>No hero in sight: the hunt (<see cref="HuntDecider"/>).</item>
/// </list>
/// </summary>
public sealed class SoldierDecider : FamilyDecider
{
    /// <summary>The band's outer edge, a share of the band: past it the soldier closes.</summary>
    public const float BandOuter = 1.15f;

    private readonly float _feetShare, _crowded;
    private Tick _flipAt;
    private float _side = 1f;
    private bool _strafing;

    public SoldierDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : base(setup, routes, numbers)
    {
        _feetShare = HordeTemperament.At(numbers.Curves, "feetAimShare", HordeTemperament.Cunning(numbers.Curves, setup.Params));
        _crowded = HordeTemperament.At(numbers.Curves, "crowdedShare", HordeTemperament.Nerve(numbers.Curves, setup.Params));
    }

    /// <summary>The share of its shots from above this soldier aims at the feet.</summary>
    public float FeetShare => _feetShare;

    /// <summary>The share of its band inside which it backs off.</summary>
    public float CrowdedShare => _crowded;

    /// <summary>Where the last decide aimed (the look's point), for an instrument.</summary>
    public AiVector LastAim { get; private set; }

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        DecisionParams p = Params;
        float band = HordeWeapons.EngageRange(HordeWeapons.RocketLauncher) * p.PreferredRangeScale;
        float d = self.DistanceTo(hero.Origin);
        AiVector eye = EyeOf(self);
        AiVector aim = Aim(tick, eye, self, hero);
        LastAim = aim;
        var look = new LookIntent(LookKind.Point, aim, hero.Id, AimPoint.Feet, 0f, 0);

        bool aimed = HordeView.AimOff(view.Self.Eye, eye, aim) <= FamilyNumbers.FireToleranceDeg;
        int line = view.Self.Reserve;
        bool clear = line == HordeView.NotTraced || line >= eye.DistanceTo(aim) - FamilyNumbers.SplashSlackUnits;
        // The gate scales the band's outer edge, never less than the edge: a soldier holding in its band always fires.
        bool inRange = d <= band * BandOuter * p.FireRangeGate, mate = MateInLine(view, eye, aim);
        bool fire = inRange && aimed && clear && !mate;
        var action = fire ? FireAction : ActionIntent.None;
        string verdict = fire ? "fire" : !inRange ? "hold: out of range" : !aimed ? "hold: off aim" : !clear ? "hold: line short" : "hold: mate in line";

        AiVector away = Flat(self - hero.Origin);
        if (d < band * _crowded)
            return End(tick, seq, FamilyNode.BackOff, Along(MoveKind.Retreat, away), look, action, hero.Origin, hero.Id, verdict);
        if (d > band * BandOuter)
            return End(tick, seq, FamilyNode.Close, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, action, hero.Origin, hero.Id, verdict);

        if (tick >= _flipAt)
        {
            _strafing = Roll(tick, 7) < p.StrafeWeight;
            _side = Roll(tick, 8) < 0.5f ? -1f : 1f;
            _flipAt = tick + Math.Max(1, (int)MathF.Ceiling(p.StrafeMeanSeconds * (0.5f + Roll(tick, 9)) / Setup.TickSeconds));
        }

        if (_strafing)
        {
            var across = new AiVector(-away.Y * _side, away.X * _side, 0f);
            return End(tick, seq, FamilyNode.Strafe, Along(MoveKind.Strafe, across), look, action, hero.Origin, hero.Id, verdict);
        }

        return End(tick, seq, FamilyNode.Band, MoveIntent.Hold, look, action, hero.Origin, hero.Id, verdict);
    }

    /// <summary>The aim point (see the class remarks): chest or feet, led, thrown off by the aim error.</summary>
    private AiVector Aim(Tick tick, AiVector eye, AiVector self, in ActorFacts hero)
    {
        bool feet = self.Z > hero.Origin.Z + FamilyNumbers.AboveUnits && Roll(tick, 3) < _feetShare;
        AiVector at = hero.Origin + new AiVector(0f, 0f, feet ? FamilyNumbers.FeetUnits : FamilyNumbers.ChestUnits);
        AiVector led = at;
        float speed = Numbers.RocketSpeed;
        for (int k = 0; k < 2; k++)
        {
            float t = eye.DistanceTo(led) / speed;
            led = at + hero.Velocity * (t * Params.LeadQuality);
        }

        return Err(tick, eye, led, Params.AimErrorDeg);
    }
}
