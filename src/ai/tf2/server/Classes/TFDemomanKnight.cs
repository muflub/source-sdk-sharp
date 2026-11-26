using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Demoman's shield (research §4.3 "Demoknight", A2, A5): the charge, the second trigger with a shield worn and no
    /// launcher (TFClassSpecialSkill), begun when the HUD's meter is full and a target in sight lies along the eye at a
    /// distance the charge closes; the melee weapon swung when the charge brings the target into its reach. A knight with no
    /// launcher closes with what he holds and goes for the lone and the soft (a Sniper, a Medic, a Spy, an Engineer), never
    /// a Pyro (an airblast stops a charge) nor toward a sentry; with three heads he goes to heal sooner. The hybrid knight
    /// fights with the pipes at range and takes out the sword for a charge when the target comes into its band.
    /// </summary>
    private sealed partial class DemomanPlay
    {
        private int _chargeRow = -2;
        private Tick _chargeUntil = new(-1);
        private long _charges, _chargeSwings, _headsMost;

        private int ChargeRow => _chargeRow == -2 ? _chargeRow = D.AbilityRow(TFDemomanClass.Charge) : _chargeRow;

        /// <summary>A knight: a shield worn and no launcher of either kind.</summary>
        private bool Knight => Kit.Shield && !Kit.Pipes && !Kit.Stickies;

        /// <summary>The shield's charge meter as the HUD shows it (0..100), -1 with none.</summary>
        private float ChargeMeter => D.Kit?.Meter(TFDemomanMeters.Charge) ?? -1f;

        /// <summary>The sword's heads as the HUD shows them, 0 with none.</summary>
        private int Heads => (int)MathF.Max(0f, D.Kit?.Meter(TFDemomanMeters.Heads, 0f) ?? 0f);

        /// <summary>Whether the bot is in its charge (begun within the charge's 1.5 s).</summary>
        private bool Charging => D._now < _chargeUntil;

        /// <summary>
        /// The band a charge is begun in: from far enough that the meter has drained to a crit (40 or under, 0.9 s of 750 u/s
        /// and the swing's 0.2 s) to <see cref="TFDemomanClass.ChargeFar"/>; a Tide Turner never crits (TFShieldCharge.GrantCrit,
        /// the review's D3), so its band is the mini-crit's (75 or under after 0.375 s: about 560 u).
        /// </summary>
        private (float Near, float Far) ChargeBand => Kit.TideTurner ? (TFDemomanClass.ChargeNear, TFDemomanClass.TideTurnerFar)
                                                                      : (TFDemomanClass.ChargeNear, TFDemomanClass.ChargeFar);

        /// <summary>Whether the target in sight is one a charge goes at now: not a Pyro, not near a sentry the bot remembers.</summary>
        private bool ChargeTarget()
        {
            if (!D._targetSighted || D._target is null || !D._view.TryActor(D._target.Id, out ActorFacts f))
                return false;
            if (f.Class == (int)TFClass.Pyro || (f.Conditions & ActorConditions.Invulnerable) != 0)
                return false;
            foreach (SeenSentry s in D.Sentries)
            {
                if ((D._now - s.Last) * D._tick < SentryShySeconds && s.At.DistanceTo(D._targetPos) < Knowledge.TFKnowledge.SentryRange)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The charge (row <see cref="TFDemomanClass.Charge"/>), over the fire: the meter full, the melee weapon in hand, a
        /// <see cref="ChargeTarget"/> in the band and within <see cref="TFDemomanClass.ChargeAimDeg"/> of the eye (the charge
        /// steers little: TFMovementRules' yaw cap).
        /// </summary>
        private void ChargeShot()
        {
            if (!Kit.Shield || Kit.Stickies || Charging || !D.HoldingMelee || ChargeMeter < TFDemomanClass.ChargeFull || ChargeRow < 0 || !ChargeTarget())
                return;
            (float near, float far) = ChargeBand;
            if (D._targetDist < near || D._targetDist > far)
                return;
            AiVector to = D._targetPos - D._self.Origin;
            var flat = new AiVector(to.X, to.Y, 0f);
            AiVector eye = VisionMath.Forward(D._self.Eye);
            var eyeFlat = new AiVector(eye.X, eye.Y, 0f);
            if (flat.Length < 1f || eyeFlat.Length < 0.01f
                || flat.Normalised().Dot(eyeFlat.Normalised()) < MathF.Cos(TFDemomanClass.ChargeAimDeg * MathF.PI / 180f))
                return;
            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(ChargeRow, 0), ++D._oneShot);
            D._verdict = "charge";
            _chargeUntil = D._now + (int)(TFDemomanClass.ChargeSeconds / D._tick);
            _charges++;
        }

        /// <summary>In the charge, the swing goes when the target comes within the sword's charging reach and the 0.2 s swing's run.</summary>
        private void ChargeSwing()
        {
            if (!Charging || !D._targetSighted || !D.HoldingMelee)
                return;
            if (D._targetDist > TFDemomanClass.ChargeSwingReach)
                return;
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
            _chargeSwings++;
        }

        /// <summary>A knight's target bonus: the lone and the soft he is there for (research: Sniper, Medic, Spy, Engineer).</summary>
        public override float TargetBonus(KnownActor target, bool sighted, float distance)
            => (Kit.Shield && target.ClassBelieved is (int)TFClass.Sniper or (int)TFClass.Medic or (int)TFClass.Spy or (int)TFClass.Engineer)
                ? TFDemomanClass.KnightTargetBonus : 0f;

        /// <summary>With three heads or more a sword's bearer heals sooner (research: "risky if you die").</summary>
        public override float HurtShare => Kit.Sword && Heads >= 3 ? TFDemomanClass.HeadsHurtShare : TFDecider.HurtShare;

        private void NoteHeads() => _headsMost = Math.Max(_headsMost, Heads);
    }
}
