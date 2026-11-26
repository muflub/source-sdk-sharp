//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Vaccinator's play (998, P7 round 2; medic.md §2.1 D, §4.5), read off its own HUD: the resist it shows (bullet, blast or
/// fire, <see cref="TFTargetIdMeters.Resist"/>) is kept matched to what is hurting the Medic and its patient, as a player
/// reads it: fire when either burns, else the damage the enemies in sight deal (a Soldier's or a Demoman's blast, a Pyro's
/// fire, every other class's and a sentry's bullets), firing ones counted double. A mismatch is one reload press (the
/// medigun cycles bullet, blast, fire on the press's edge), at most once a <see cref="ResistCycleSeconds"/>.
/// The bubble (one quarter of the bar, 3 s of the matched resist on the Medic and the patient) goes when the Medic or the
/// patient was hurt in the last second (stock's MEDIGUN_RESIST arm, tf_bot_medic_heal.cpp:635-647; stock reads the server's
/// injury clock, ours the Medic's own health and the heal-target panel), with the resist already on the threat, and not
/// again while the last bubble lasts.
/// </summary>
public sealed partial class TFDecider
{
    private sealed partial class MedicPlay
    {
        private const int BulletResist = TFMedicClass.ResistBullet, BlastResist = TFMedicClass.ResistBlast, FireResist = TFMedicClass.ResistFire;

        /// <summary>The shortest time between two resist presses, and the enemies counted for the threat (in sight, within this).</summary>
        public const float ResistCycleSeconds = 1f, ThreatReach = 1500f;

        /// <summary>A bubble lasts this (weapon_vaccinator_resist_duration, tf_weapon_medigun.cpp:89): no second one sent within it.</summary>
        public const float BubbleSeconds = 3f;

        private int _resist = -1;
        private Tick _resistPressAt = new(-100000), _bubbleAt = new(-100000);
        private long _resistCycles, _resistShown, _resistOnThreat, _resistOffThreat;

        /// <summary>The Vaccinator's live measure, a decide at a time: its resist shown, and with a threat known, on it or off it.</summary>
        private void CountResist()
        {
            if (_chargeType != ResistCharge || _resist < 0)
                return;
            _resistShown++;
            int want = ThreatResist();
            _resistOnThreat += want >= 0 && want == _resist ? 1 : 0;
            _resistOffThreat += want >= 0 && want != _resist ? 1 : 0;
        }

        /// <summary>The resist the threat calls for, or -1 with no threat known.</summary>
        private int ThreatResist()
        {
            if (((D._self.Conditions | (_patient.Known ? _patient.Conditions : ActorConditions.None)) & ActorConditions.Burning) != 0)
                return FireResist;
            Span<int> weight = stackalloc int[3];
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (!TeamRule.IsGameTeam(f.Team) || TeamRule.Friends(D._self.Team, f.Team) || f.Origin.DistanceTo(D._self.Origin) > ThreatReach)
                    continue;
                weight[ResistAgainst((TFClass)f.Class)] += (f.Activity & ActorActivity.Firing) != 0 ? 2 : 1;
            }

            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind == ThingKind.Building && t.Type == (int)TFThingType.Sentry && TeamRule.IsGameTeam(t.Team) && t.Team != D._self.Team
                    && t.Origin.DistanceTo(D._self.Origin) <= ThreatReach)
                    weight[BulletResist] += 2;
            }

            int best = -1, most = 0;
            for (int r = 0; r < 3; r++)
            {
                if (weight[r] > most || (weight[r] == most && most > 0 && r == _resist))
                    (best, most) = (r, weight[r]);
            }

            return best;
        }

        /// <summary>The damage a class deals, as the resist that answers it: a Soldier's and a Demoman's blast, a Pyro's fire, the rest bullets.</summary>
        public static int ResistAgainst(TFClass c) => c switch
        {
            TFClass.Soldier or TFClass.Demoman => BlastResist,
            TFClass.Pyro => FireResist,
            _ => BulletResist,
        };

        /// <summary>One reload press when the resist shown is not the threat's (at most once a <see cref="ResistCycleSeconds"/>): true when sent.</summary>
        private bool CycleResist()
        {
            if (_chargeType != ResistCharge || _resist < 0 || !HoldingMedigun || Seconds(_resistPressAt) < ResistCycleSeconds)
                return false;
            int want = ThreatResist();
            if (want < 0 || want == _resist)
                return false;
            D._action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++D._oneShot);
            _resistPressAt = D._now;
            _resistCycles++;
            return true;
        }

        /// <summary>The bubble: the Medic or the patient hurt in the last second, the resist on the threat, no bubble lasting.</summary>
        private bool VaccinatorPopWanted()
        {
            if (Seconds(_bubbleAt) < BubbleSeconds)
                return false;
            // Stock counts only the enemy team's injury (tf_bot_medic_heal.cpp:638, :643). The Medic's own: a hit another dealt him (the
            // memory's attack tick, not a fall). The patient's panel falling says nothing of who did it (a Soldier's own rocket jump): it
            // counts only with a threat in sight (the Fable review, C3).
            int want = ThreatResist();
            Tick attacked = D._memory.Board.LastAttackTick;
            bool selfHurt = Seconds(_selfHurtAt) < 1f && attacked.Value > 0 && Seconds(attacked) < 1f;
            bool patientHurt = _patient.Known && Seconds(_patientHurtAt) < 1f && want >= 0;
            if (!selfHurt && !patientHurt)
                return false;
            return want < 0 || want == _resist;
        }
    }
}
