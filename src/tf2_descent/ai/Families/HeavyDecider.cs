using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The minigun heavy (plan_descent_logic HO12; stock's heavy bot as the reference): slow, it
/// holds its ground and pours fire on, and its gun turns slowly, so a hero who runs round it
/// close gets out of its aim.
///
/// <list type="number">
/// <item><b>The band</b> is its family's range times its <c>preferredRangeScale</c>
/// (Aggression). Past its outer edge it closes along its route, firing as it comes; inside it
/// holds where it stands (<see cref="FamilyNode.Suppress"/>).</item>
/// <item><b>The turn.</b> Its look goes from where its gun points now (the view's look) towards
/// the hero's chest, thrown off by its <c>aimErrorDeg</c> (Accuracy), by at most the rules'
/// minigun turn rate times the time to its next decide: the gun never swings faster.</item>
/// <item><b>Fire</b> (the hands spin the gun up for the rules' time before its first round)
/// while the hero is inside the band's outer edge times its <c>fireRangeGate</c> (Discipline),
/// the gun points within <see cref="FamilyNumbers.SuppressToleranceDeg"/> of the hero, the line
/// is clear and no pack-mate is in it.</item>
/// <item>No hero in sight: the hunt.</item>
/// </list>
/// </summary>
public sealed class HeavyDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : FamilyDecider(setup, routes, numbers)
{
    /// <summary>The band's outer edge, a share of the band.</summary>
    public const float BandOuter = 1.15f;

    /// <summary>How far, degrees, the last decide's look was from the hero (the gun's lag).</summary>
    public float LastLag { get; private set; }

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        DecisionParams p = Params;
        float band = HordeWeapons.EngageRange(HordeWeapons.Minigun) * p.PreferredRangeScale;
        float d = self.DistanceTo(hero.Origin);
        AiVector eye = EyeOf(self);
        AiVector want = Err(tick, eye, Chest(hero), p.AimErrorDeg);

        // The gun turns from where it points now by at most its rate till the next decide.
        float now = view.Self.Eye.Yaw, wantYaw = YawOf(want - eye);
        float pitch = -MathF.Atan2(want.Z - eye.Z, (want - eye).Length2D) * (180f / MathF.PI);
        float step = Numbers.MinigunTurnRate > 0f ? Numbers.MinigunTurnRate * DecideSchedule.Period * Setup.TickSeconds : 360f;
        float turn = Math.Clamp(Delta(now, wantYaw), -step, step);
        AiVector aim = Along(eye, pitch, now + turn, MathF.Max(64f, eye.DistanceTo(want)));
        var look = new LookIntent(LookKind.Point, aim, hero.Id, AimPoint.Body, 0f, 0);
        LastLag = MathF.Abs(Delta(now, YawOf(Chest(hero) - eye)));

        bool onHero = HordeView.AimOff(view.Self.Eye, eye, Chest(hero)) <= FamilyNumbers.SuppressToleranceDeg;
        bool fire = d <= band * BandOuter * p.FireRangeGate && onHero && LineClear(view, eye, Chest(hero), FamilyNumbers.SplashSlackUnits)
                    && !MateInLine(view, eye, Chest(hero));
        var action = fire ? FireAction : ActionIntent.None;
        string verdict = fire ? "fire" : !onHero ? "hold: gun turning" : "hold";

        if (d > band * BandOuter)
            return End(tick, seq, FamilyNode.Close, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, action, hero.Origin, hero.Id, verdict);
        return End(tick, seq, FamilyNode.Suppress, MoveIntent.Hold, look, action, hero.Origin, hero.Id, verdict);
    }
}
