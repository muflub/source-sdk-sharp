using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Guard archetype (research A2, cut to what the senses have, as the review asked: no sapped flag, no Spy-suspicion
    /// counter yet): Flame Thrower, Shotgun, Homewrecker. It keeps within <see cref="GuardLeash"/> of a teammate in sight (a
    /// Medic first, then a Heavy, an Engineer, anyone), goes back to him when further (its own place, the "Class" branch),
    /// gives up a target more than <see cref="GuardChase"/> from him, and shoves any enemy that comes within
    /// <see cref="ShoveRange"/> of the bot (the pushy draw is always yes for it).
    /// </summary>
    private sealed partial class PyroPlay
    {
        /// <summary>The Guard keeps within this of its charge (research A2's 400 u: a flame's reach and an airblast step).</summary>
        public const float GuardLeash = 400f;

        /// <summary>The Guard leaves a target further than this from its charge to the others (research A2's 600 u).</summary>
        public const float GuardChase = 600f;

        /// <summary>The Guard's own place, when its charge is past the leash, outweighs the objectives and a fight (0.8): it goes back.</summary>
        public const float GuardPlaceUtility = 0.85f;

        /// <summary>The Guard's charge out of sight this long is let go.</summary>
        public const float ChargeForgottenSeconds = 3f;

        private ActorId _charge = ActorId.None;
        private AiVector _chargeAt;
        private Tick _chargeSeen, _pickedAt;
        private long _guardDecides, _leashedDecides;

        private bool IsGuard => Archetype == TFPyroClass.Guard;

        /// <summary>The charge's rank: a Medic first, then a Heavy, an Engineer, a Sniper, anyone else.</summary>
        private static int ChargeRank(TFClass c) => c switch
        {
            TFClass.Medic => 0,
            TFClass.HeavyWeapons => 1,
            TFClass.Engineer => 2,
            TFClass.Sniper => 3,
            _ => 4,
        };

        /// <summary>Picks the Guard's charge among the teammates in sight (the one it had while it stays in sight, unless a better rank shows), and counts its leash.</summary>
        private void PickCharge()
        {
            if (!IsGuard || D._now == _pickedAt)
                return;
            _pickedAt = D._now;
            int bestRank = int.MaxValue;
            float bestDistance = float.MaxValue;
            ActorId best = ActorId.None;
            AiVector at = default;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (!TeamRule.Friends(D._self.Team, f.Team) || f.Id == D._bot)
                    continue;
                int rank = ChargeRank((TFClass)f.Class) - (f.Id == _charge ? 1 : 0);
                float d = D._self.Origin.DistanceTo(f.Origin);
                if (rank < bestRank || (rank == bestRank && d < bestDistance))
                    (bestRank, bestDistance, best, at) = (rank, d, f.Id, f.Origin);
            }

            if (best != ActorId.None)
                (_charge, _chargeAt, _chargeSeen) = (best, at, D._now);
            else if (_charge != ActorId.None && (D._now - _chargeSeen) * D._tick > ChargeForgottenSeconds)
                _charge = ActorId.None;
            if (_charge == ActorId.None)
                return;
            _guardDecides++;
            if (D._self.Origin.DistanceTo(_chargeAt) <= GuardLeash)
                _leashedDecides++;
        }

        /// <summary>The Guard's place: back to its charge when further than <see cref="GuardLeash"/>.</summary>
        public override float PlaceUtility()
        {
            PickCharge();
            return IsGuard && _charge != ActorId.None && D._self.Origin.DistanceTo(_chargeAt) > GuardLeash ? GuardPlaceUtility : 0f;
        }

        public override bool GoPlace()
        {
            if (!IsGuard || _charge == ActorId.None)
                return false;
            D.GoTo(_chargeAt, MoveSpeed.Run);
            D._verdict = "guarding";
            return true;
        }

        /// <summary>The Guard's fight with a target further than <see cref="GuardChase"/> from its charge weighs little: its charge first.</summary>
        private float GuardWeigh(string branch)
            => IsGuard && branch == Fight && _charge != ActorId.None && D._target is not null && D._targetPos.DistanceTo(_chargeAt) > GuardChase ? 0.3f : 1f;

        /// <summary>Whether <paramref name="at"/> is within <paramref name="reach"/> of the Guard's charge (false with none).</summary>
        private bool NearCharge(AiVector at, float reach) => _charge != ActorId.None && at.DistanceTo(_chargeAt) <= reach;

        private void CountGuard(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("pyro.guard_decides", _guardDecides));
            into.Add(new("pyro.leashed_decides", _leashedDecides));
        }

        private void GuardDied() => _charge = ActorId.None;
    }
}
