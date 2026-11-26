using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// What a Medic's kit changes in its play (plan_bots_teams_tf2 C.3.1, P7; medic.md §2.1, §4.5 with the Fable review's fixes),
/// read off its own kit as a player knows it, so a pinned archetype and a kit a human gave the bot play alike:
/// <list type="bullet">
/// <item>The Crusader's Crossbow (Combo): a teammate the HUD shows calling, in sight beyond the beam's hold and within
/// <see cref="BoltReach"/>, gets a healing bolt lobbed at him while the beam is on nobody, an enemy in sight or not (live,
/// ctf_2fort 2026-11-21: with "no enemy in sight" as a condition the Combo Medic fired 0 bolts in 300 s, a hurt teammate
/// being one in a fight).</item>
/// <item>The Ubersaw (Combo, Kritz): swung only at an enemy already within <see cref="SawReach"/>, with the charge under
/// <see cref="SawUnderCharge"/> and the Medic over <see cref="SawOverHealth"/> health; never walked to.</item>
/// <item>The Kritzkrieg: a good patient is a Soldier, Demoman or Heavy; the charge goes only on the push (the patient firing at
/// two or more enemies near him), never into an enemy sentry in sight nor within <see cref="KritzAfterUberSeconds"/> of an
/// enemy Über seen, never to save anyone (crits protect nobody); it stands <see cref="KritzBack"/> further back.</item>
/// <item>The Quick-Fix: a Soldier, Demoman or Scout first (the jumpers it keeps up with); it follows from
/// <see cref="QuickFixStart"/>; its megaheal goes to save (the patient under half, the Medic about to die), never on the
/// push or the trade (no invulnerability, and a megahealed player cannot capture).</item>
/// </list>
/// </summary>
public sealed partial class TFDecider
{
    private sealed partial class MedicPlay
    {
        /// <summary>The medigun's <c>set_charge_type</c>: 0 the stock Über, 1 the Kritzkrieg's crits, 2 the Quick-Fix's megaheal, 3 the Vaccinator's.</summary>
        public const int UberCharge = 0, KritzCharge = 1, MegahealCharge = 2, ResistCharge = 3;

        /// <summary>A healing bolt goes to a calling teammate this far at most (medic.md §4.5, a measure to confirm live).</summary>
        public const float BoltReach = 1500f;

        /// <summary>The Ubersaw's rule (medic.md §2.1, §4.5): an enemy within 96 u, the charge under 75 %, the Medic over 75 health.</summary>
        public const float SawReach = 96f, SawUnderCharge = 0.75f;

        public const int SawOverHealth = 75;

        /// <summary>The Kritz Medic's extra distance back, and how long after an enemy Über is seen it holds its charge.</summary>
        public const float KritzBack = 125f, KritzAfterUberSeconds = 8f;

        /// <summary>The Quick-Fix follows its patient from this (medic.md §4.5), not stock's 250 u.</summary>
        public const float QuickFixStart = 150f;

        private int _crossbow = -1, _ubersaw = -1;
        private Mate _boltMate;
        private Tick _enemyUberAt = new(-100000);
        private long _bolts, _swings;

        /// <summary>The kit's crossbow and Ubersaw, and an enemy Über in sight (the Kritz rule's memory), once a decide.</summary>
        private void ReadKit(TFHud hud)
        {
            _crossbow = hud.TryWielded(TFLoadoutSlot.Primary, out TFKitItem primary) && primary.ItemClass == "tf_weapon_crossbow" ? primary.WeaponKind : -1;
            _ubersaw = hud.TryWielded(TFLoadoutSlot.Melee, out TFKitItem melee) && melee.Has("add_onhit_ubercharge") ? melee.WeaponKind : -1;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.IsGameTeam(f.Team) && !TeamRule.Friends(D._self.Team, f.Team) && (f.Conditions & ActorConditions.Invulnerable) != 0)
                    _enemyUberAt = D._now;
            }
        }

        /// <summary>The class rank of a primary patient by the medigun: the Kritzkrieg's Soldier, Demoman, Heavy; the Quick-Fix's Soldier, Demoman, Scout; else stock's.</summary>
        private int RankFor(TFClass c) => _chargeType switch
        {
            KritzCharge => c is TFClass.Soldier or TFClass.Demoman or TFClass.HeavyWeapons ? 0 : c == TFClass.Pyro ? 3 : 999,
            MegahealCharge => c is TFClass.Soldier or TFClass.Demoman or TFClass.Scout ? 0 : c is TFClass.HeavyWeapons or TFClass.Pyro ? 3 : 999,
            _ => Rank(c),
        };

        /// <summary>Whether a charge on <paramref name="c"/> does its job with this medigun.</summary>
        private bool GoodTarget(TFClass c) => _chargeType switch
        {
            KritzCharge => c is TFClass.Soldier or TFClass.Demoman or TFClass.HeavyWeapons,
            MegahealCharge => GoodUberTarget(c) || c == TFClass.Scout,
            _ => GoodUberTarget(c),
        };

        /// <summary>How near the patient the Medic closes (stock's stop range; the Kritz Medic further back).</summary>
        private float StopRange => _chargeType == KritzCharge ? StopFollow + KritzBack : StopFollow;

        /// <summary>From how far it follows (stock's start range; the Quick-Fix closer, the Kritz Medic further).</summary>
        private float StartRange => _chargeType switch
        {
            MegahealCharge => QuickFixStart,
            KritzCharge => StartFollow + KritzBack,
            _ => StartFollow,
        };

        /// <summary>The weapon the kit wants out over the medigun's rule, or -1: the Ubersaw at an enemy in reach, the crossbow for a calling teammate too far to beam.</summary>
        private int KitWeapon()
        {
            if (_ready)
                return -1;
            if (SawWanted)
                return _ubersaw;
            // Only for a teammate the beam cannot reach: with the heal target in sight within the beam's hold, the medigun (live, ctf_2fort
            // 2026-11-21: holding the crossbow kept the beam off, so the bolt rule held while its patient stood near; the crossbow kits
            // were within 450 u of a teammate 24-40 % of their lives against the stock kit's 37-56 %).
            if (_crossbow >= 0 && _beamSlot == 0 && !HealInReach && BoltMate(out _))
                return _crossbow;
            return -1;
        }

        /// <summary>Whether the Ubersaw is wanted: an enemy in sight within <see cref="SawReach"/>, the bar under <see cref="SawUnderCharge"/>, the Medic over <see cref="SawOverHealth"/>; the beam is let go for it.</summary>
        private bool SawWanted => _ubersaw >= 0 && !_ready && D._targetSighted && D._targetDist < SawReach && _charge < SawUnderCharge && D._self.Health > SawOverHealth;

        /// <summary>Whether the heal target is in sight within the beam's hold: then the medigun heals him, not a bolt.</summary>
        private bool HealInReach => _heal.Known && _heal.Sighted && DistanceTo(_heal) <= BeamHold;

        /// <summary>The nearest teammate in sight the HUD shows calling, beyond the beam's hold and within <see cref="BoltReach"/>.</summary>
        private bool BoltMate(out Mate mate)
        {
            mate = default;
            if (D.Kit is not { } hud)
                return false;
            float best = float.MaxValue;
            for (int i = 0; i < _mateCount; i++)
            {
                ref readonly Mate m = ref _mates[i];
                float d = DistanceTo(m);
                if (!m.Sighted || d <= BeamHold || d > BoltReach || d >= best || !hud.TryMate(m.Id.Slot, out TFMateHealth shown) || (shown.Why & TFMateSeen.Calling) == 0)
                    continue;
                (mate, best) = (m, d);
            }

            return mate.Known;
        }

        /// <summary>With the crossbow out, a bolt lobbed at the calling teammate (FireAtFriend: the friend gate lets it go); false when there is none to heal.</summary>
        private bool BoltAtMate()
        {
            if (_crossbow < 0 || D._self.ActiveWeapon != _crossbow || HealInReach || !BoltMate(out _boltMate) || !D.HeldFacts(out WeaponFacts bolt))
                return false;
            AiVector aim = BodyOf(_boltMate);
            if (bolt.ProjectileSpeed > 0f && bolt.ProjectileGravity > 0f && AimLead.Lob(D.OwnEye, aim, bolt.ProjectileSpeed, bolt.ProjectileGravity) is { } lobbed)
                aim = lobbed;
            if (bolt.HasClip && D._self.Clip <= 0)
            {
                D._look = new LookIntent(LookKind.Actor, aim, _boltMate.Id, AimPoint.Body, 0f, 0);
                return true;
            }

            FireAtFriend(_boltMate.Id, aim);
            _bolts++;
            return true;
        }

        /// <summary>The Kritzkrieg's pop: on the push only, on a good patient, no enemy sentry in sight, no enemy Über seen lately.</summary>
        private bool KritzPopWanted()
        {
            if (!_patient.Known || Carries(_patient) || !GoodTarget(_patient.Class) || Seconds(_enemyUberAt) < KritzAfterUberSeconds)
                return false;
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind == ThingKind.Building && t.Type == (int)TFThingType.Sentry && TeamRule.IsGameTeam(t.Team) && t.Team != D._self.Team)
                    return false;
            }

            return Push();
        }

        /// <summary>The Quick-Fix's megaheal: to save the patient under half or the Medic about to die; never the push nor the trade.</summary>
        private bool QuickFixPopWanted()
        {
            if ((D._self.Health < SelfSaveHealth && Seconds(_selfHurtAt) < 1f) || D._self.Health < LastHealth)
                return true;
            return _patient.Known && !Carries(_patient) && _patientMost > 0 && _patientHealth < PatientLowShare * _patientMost;
        }

        /// <summary>A swing of the Ubersaw counted, for the harness.</summary>
        private void CountSwing()
        {
            if (_ubersaw >= 0 && D._self.ActiveWeapon == _ubersaw && D._action.Kind == ActionKind.Fire)
                _swings++;
        }
    }
}
