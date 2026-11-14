using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The melee rusher (plan_descent_logic HO6; plan_tf2_rpg 5c's melee attack, stock's melee
/// bots as the reference): with a hero in sight it runs at it and never stops at range.
///
/// <list type="number">
/// <item><b>Rush.</b> Farther than <see cref="FamilyNumbers.RushStraightUnits"/>, it runs to its
/// own approach point: a ring <see cref="FamilyNumbers.RushRingUnits"/> round the target, at an
/// angle off its own bearing that depends on its place among the pack's rushers in its view
/// (ordered by body), <see cref="FamilyNumbers.RushSpreadDeg"/> apart; so a pack's rushers come in
/// from a fan of sides rather than one point.</item>
/// <item><b>Swing.</b> Nearer, it runs straight at the target and keeps on it, facing it; it asks
/// for a swing at every decide while the target is inside its reach and its arc (the hands keep
/// the swing's own cadence).</item>
/// <item>No hero in sight: the hunt (<see cref="HuntDecider"/>).</item>
/// </list>
/// </summary>
public sealed class RusherDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : FamilyDecider(setup, routes, numbers)
{
    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        var look = new LookIntent(LookKind.Actor, hero.Origin, hero.Id, AimPoint.Body, 0f, 0);
        float d = self.DistanceTo(hero.Origin);
        if (d > FamilyNumbers.RushStraightUnits)
        {
            AiVector point = Approach(view, self, hero.Origin);
            return End(tick, seq, FamilyNode.Rush, Route.Follow(tick, view.Self, self, point, MoveSpeed.Run), look, ActionIntent.None, point, hero.Id);
        }

        float reach = HordeWeapons.EngageRange(HordeWeapons.Melee) + FamilyNumbers.SwingSlackUnits;
        AiVector eye = EyeOf(self);
        bool inArc = HordeView.AimOff(view.Self.Eye, eye, hero.Origin + new AiVector(0f, 0f, FamilyNumbers.ChestUnits)) <= FamilyNumbers.SwingToleranceDeg;
        var action = d <= reach && inArc ? FireAction : ActionIntent.None;
        return End(tick, seq, FamilyNode.Swing, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, action, hero.Origin, hero.Id);
    }

    /// <summary>
    /// This rusher's point on the ring round <paramref name="target"/>: its bearing from the target,
    /// turned by its place among the melee pack-mates in view (and itself), centred on zero.
    /// </summary>
    private AiVector Approach(PerceivedView view, AiVector self, AiVector target)
    {
        int rank = 0, count = 1;
        int me = Setup.Bot.Slot;
        foreach (ref readonly ActorFacts m in view.Sighted)
        {
            if (!HordeView.IsMate(m) || HordeView.MateFamily(m) != HordeWeapons.Melee)
                continue;
            count++;
            if (m.Id.Slot < me)
                rank++;
        }

        AiVector from = Flat(self - target);
        if (from == AiVector.Zero)
            from = new AiVector(1f, 0f, 0f);
        float turn = (rank - (count - 1) * 0.5f) * FamilyNumbers.RushSpreadDeg * (MathF.PI / 180f);
        float c = MathF.Cos(turn), s = MathF.Sin(turn);
        var dir = new AiVector(from.X * c - from.Y * s, from.X * s + from.Y * c, 0f);
        return target + dir * FamilyNumbers.RushRingUnits;
    }
}
