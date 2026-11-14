using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The medic (plan_descent_logic HO6; plan_tf2_rpg 5c's medic-follow-and-heal, stock's medic bot
/// as the reference): it keeps its pack alive and keeps out of the fight.
///
/// <list type="number">
/// <item><b>Its patient</b> is the most hurt pack-mate in its view (the pack's board: each
/// mate's life share, <see cref="HordeView"/>); with none hurt, the nearest, so it keeps by its
/// pack.</item>
/// <item><b>Flee.</b> A hero in sight nearer than its <c>fleeHeroUnits</c> (Nerve: a fearless
/// medic lets one closer): it backs straight away from the hero, still beaming a hurt patient
/// in reach.</item>
/// <item><b>Heal.</b> A hurt patient inside the beam's reach (the rules', less
/// <see cref="FamilyNumbers.MedicReachSlack"/>): it looks at the patient and asks for the beam
/// (<see cref="HordeAbilities.Heal"/>) every decide, holding within
/// <see cref="FamilyNumbers.MedicStandUnits"/> of it and closing when farther.</item>
/// <item><b>Follow.</b> Otherwise it goes to its patient (or stands by it), facing it.</item>
/// <item><b>Alone</b> (no pack-mate holding a body): it fights as a syringe gunner, firing at a
/// hero in its range it is aimed at, and hunts as every robot does.</item>
/// </list>
/// </summary>
public sealed class MedicDecider : FamilyDecider
{
    private readonly float _flee;

    public MedicDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : base(setup, routes, numbers) =>
        _flee = HordeTemperament.At(numbers.Curves, "fleeHeroUnits", HordeTemperament.Nerve(numbers.Curves, setup.Params));

    /// <summary>How near a hero this medic lets come before it runs.</summary>
    public float FleeUnits => _flee;

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        bool seen = NearestHero(view, self, out ActorFacts hero);
        if (!Patient(view, self, out ActorFacts patient))
            return Alone(tick, view, seq, self, seen, hero);

        float reach = Numbers.HealRange - FamilyNumbers.MedicReachSlack;
        float d = self.DistanceTo(patient.Origin);
        bool hurt = HordeView.MateLife(patient) < 0.999f;
        bool beam = hurt && d <= reach;
        var look = new LookIntent(LookKind.Actor, patient.Origin, patient.Id, AimPoint.Body, 0f, 0);
        var action = beam ? new ActionIntent(ActionKind.Ability, FireStyle.Tap, HordeAbilities.Heal, 0) : ActionIntent.None;

        if (seen && self.DistanceTo(hero.Origin) < _flee)
            return End(tick, seq, FamilyNode.Flee, Along(MoveKind.Retreat, Flat(self - hero.Origin)), look, action, hero.Origin, hero.Id);

        var move = d > FamilyNumbers.MedicStandUnits ? Route.Follow(tick, view.Self, self, patient.Origin, MoveSpeed.Run) : MoveIntent.Hold;
        return End(tick, seq, beam ? FamilyNode.Heal : FamilyNode.Follow, move, look, action, patient.Origin, patient.Id);
    }

    /// <summary>The most hurt pack-mate in view (the lower body on a tie); with none hurt, the nearest.</summary>
    private static bool Patient(PerceivedView view, AiVector self, out ActorFacts patient)
    {
        patient = default;
        bool any = false;
        (int Whole, float Key, int Slot) best = default;
        foreach (ref readonly ActorFacts m in view.Sighted)
        {
            if (!HordeView.IsMate(m))
                continue;
            float life = HordeView.MateLife(m);
            var key = life < 0.999f ? (0, life, m.Id.Slot) : (1, self.DistanceTo(m.Origin), m.Id.Slot);
            if (!any || key.CompareTo(best) < 0)
            {
                (patient, best, any) = (m, key, true);
            }
        }

        return any;
    }

    /// <summary>A medic with no pack-mate: the hunt, firing at a hero inside its range it is aimed at.</summary>
    private IntentCommand Alone(Tick tick, PerceivedView view, int seq, AiVector self, bool seen, in ActorFacts hero)
    {
        var hunt = Hunt(tick, view, seq);
        if (!seen)
            return hunt;
        AiVector eye = EyeOf(self);
        AiVector chest = hero.Origin + new AiVector(0f, 0f, FamilyNumbers.ChestUnits);
        float range = HordeWeapons.EngageRange(HordeWeapons.SyringeGun) * Params.PreferredRangeScale * Params.FireRangeGate;
        bool fire = self.DistanceTo(hero.Origin) <= range && HordeView.AimOff(view.Self.Eye, eye, chest) <= FamilyNumbers.FireToleranceDeg * 2f;
        return fire ? hunt with { Action = FireAction } : hunt;
    }
}
