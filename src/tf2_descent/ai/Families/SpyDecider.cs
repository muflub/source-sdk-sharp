using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The spy (plan_descent_logic HO12; plan_tf2_rpg 5c's spy cloak-and-stab, stock's spy bot as
/// the reference): it comes at its hero cloaked and from behind, uncloaks close (its telegraph),
/// and knifes the back; the hands deal the rules' backstab from behind and a plain swing from
/// anywhere else. Which way the hero faces is in the view (<see cref="HordeView.HeroYaw"/>).
///
/// <list type="number">
/// <item><b>The approach</b>, chosen once each time it sights a hero: by the way round to the
/// hero's back with the chance its <c>spyFlankShare</c> gives (Cunning), else straight in.</item>
/// <item><b>Stalk.</b> Farther than <see cref="FamilyNumbers.SpyCloseUnits"/>, it runs along its
/// route to a point <see cref="FamilyNumbers.SpyStalkRingUnits"/> behind the hero (straight at the
/// hero when it goes straight in), cloaked (<see cref="HordeAbilities.Cloak"/>).</item>
/// <item><b>Flank.</b> Nearer, going round, with the hero facing it (it stands outside the knife's
/// cone behind the hero): it steps round the hero on a ring of <see cref="FamilyNumbers.SpyFlankRingUnits"/>,
/// <see cref="FamilyNumbers.SpyFlankStepDeg"/> at a time, the short way to the back.</item>
/// <item><b>Stab.</b> Behind the hero, or going straight in: it runs to the hero's back (or the
/// hero), uncloaks inside <see cref="FamilyNumbers.SpyDecloakUnits"/>
/// (<see cref="HordeAbilities.Decloak"/>), and swings while the hero is in its reach and arc.</item>
/// <item>No hero in sight: the hunt.</item>
/// </list>
/// </summary>
public sealed class SpyDecider : FamilyDecider
{
    private readonly float _flankShare;
    private bool _engaged, _flanking;

    public SpyDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : base(setup, routes, numbers) =>
        _flankShare = HordeTemperament.At(numbers.Curves, "spyFlankShare", HordeTemperament.Cunning(numbers.Curves, setup.Params));

    /// <summary>The share of its approaches this spy takes round to the hero's back.</summary>
    public float FlankShare => _flankShare;

    /// <summary>Whether this approach goes round to the back.</summary>
    public bool Flanking => _flanking;

    /// <summary>Approaches begun, and those that went round to the back.</summary>
    public int Approaches { get; private set; }

    public int Flanks { get; private set; }

    /// <summary>Whether <paramref name="self"/> stands inside the knife's cone behind <paramref name="hero"/>.</summary>
    public static bool Behind(in ActorFacts hero, AiVector self, float coneDeg)
    {
        AiVector to = Flat(self - hero.Origin);
        if (to == AiVector.Zero)
            return false;
        return (HordeView.HeroForward(hero) * -1f).Dot(to) >= MathF.Cos(coneDeg * (MathF.PI / 180f));
    }

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
        {
            _engaged = false;
            return Hunt(tick, view, seq);
        }

        if (!_engaged)
        {
            _engaged = true;
            _flanking = Roll(tick, 21) < _flankShare;
            Approaches++;
            if (_flanking)
                Flanks++;
        }

        float d = self.DistanceTo(hero.Origin);
        AiVector back = HordeView.HeroForward(hero) * -1f;
        bool behind = Behind(hero, self, Numbers.BackstabConeDeg);
        var look = new LookIntent(LookKind.Actor, hero.Origin, hero.Id, AimPoint.Body, 0f, 0);

        if (d > FamilyNumbers.SpyCloseUnits)
        {
            AiVector to = _flanking ? hero.Origin + back * FamilyNumbers.SpyStalkRingUnits : hero.Origin;
            return End(tick, seq, FamilyNode.Stalk, Route.Follow(tick, view.Self, self, to, MoveSpeed.Run), look, Ability(HordeAbilities.Cloak), to, hero.Id);
        }

        if (_flanking && !behind)
        {
            // Round the ring the short way to the back: turn the spy's bearing from the hero towards the back.
            AiVector from = Flat(self - hero.Origin);
            if (from == AiVector.Zero)
                from = back;
            float toBack = Delta(YawOf(from), YawOf(back));
            float step = Math.Clamp(toBack, -FamilyNumbers.SpyFlankStepDeg, FamilyNumbers.SpyFlankStepDeg);
            AiVector ring = hero.Origin + Turned(from, step) * FamilyNumbers.SpyFlankRingUnits;
            var cloak = d > FamilyNumbers.SpyDecloakUnits ? Ability(HordeAbilities.Cloak) : ActionIntent.None;
            return End(tick, seq, FamilyNode.Flank, Route.Follow(tick, view.Self, self, ring, MoveSpeed.Run), look, cloak, ring, hero.Id);
        }

        AiVector stand = _flanking ? hero.Origin + back * FamilyNumbers.SpyBackUnits : hero.Origin;
        float reach = HordeWeapons.EngageRange(HordeWeapons.Knife) + FamilyNumbers.SwingSlackUnits;
        AiVector eye = EyeOf(self);
        bool inArc = HordeView.AimOff(view.Self.Eye, eye, Chest(hero)) <= FamilyNumbers.SwingToleranceDeg;
        ActionIntent action = d <= reach && inArc ? FireAction
            : d <= FamilyNumbers.SpyDecloakUnits ? Ability(HordeAbilities.Decloak)
            : Ability(HordeAbilities.Cloak);
        return End(tick, seq, FamilyNode.Stab, Route.Follow(tick, view.Self, self, stand, MoveSpeed.Run), look, action, stand, hero.Id,
                   behind ? "behind" : "in front");
    }
}
