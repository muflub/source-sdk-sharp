//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Econ;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Medic's heal (plan_bots_teams_tf2 C.3, P7; medic.md §4.4 with the Fable review's fixes), on stock's
/// <c>CTFBotMedicHeal</c> (tf_bot_medic_heal.cpp) with only what a player has:
/// <list type="bullet">
/// <item>The patient: a teammate who called "Medic!" within <see cref="CallReach"/> first (any teammate's call, the
/// review's fix 4); else the flag carrier; else the best by class (Heavy = Soldier = Pyro, then Demoman; never a Medic,
/// Sniper, Engineer or Spy as the primary), keeping the one it has unless the other is much nearer (stock's
/// <c>SelectPreferred</c>, :176-217); while the charge runs or is ready on a good Über target, kept (:260-269).</item>
/// <item>A momentary heal target while the patient is stable (:589-604): a burning teammate within 0.9 of the beam's
/// reach first, else, out of a fight, one the HUD shows calling (at or under 75 %) or the target ID shows hurt. Stable
/// is read off the heal-target panel: full health, unhurt for <see cref="StableSeconds"/>, not burning.</item>
/// <item>The beam held on the heal target; when the medigun is on someone else, the trigger let go one decide in 1 to
/// 2 s to retarget (:609-629).</item>
/// <item>The medigun out, unless no charge is ready, the beam is on nobody and an enemy in sight is nearer than the
/// patient, the patient is beyond 1.1 x 600 u, or out of sight (:735-756): then the primary at the enemy.</item>
/// <item>The place (:758-782, :902-1047): with the charge ready or running, or the beam not on whom it wants, within
/// <see cref="StopFollow"/> and sight of the patient; else, unseen by any enemy in its sight, held near the patient
/// (behind him when he has been quiet 5 s and the bot stands before him), and seen, the nearest point on rings round the
/// patient no enemy it sees can see.</item>
/// <item>The pop (:631-701): the patient under half health and not already invulnerable, the bot under
/// <see cref="SelfSaveHealth"/> and hurt in the last second, or under <see cref="LastHealth"/>; and P7's push (the
/// review's fix 5): the patient firing with two or more enemies in sight within <see cref="PushReach"/> of him; and the
/// trade: an enemy seen invulnerable within <see cref="TradeReach"/>. Never in setup, never for a flag carrier patient
/// unless to save the bot, after the bot's reaction time.</item>
/// </list>
/// Exact health is read only for the beam's patient (the HUD's heal-target panel) and the player under the crosshair
/// (the target ID, <see cref="TFTargetIdMeters"/>); a caller is a flag and a place (the coordinator's ruling,
/// 2026-11-20).
/// </summary>
public sealed partial class TFDecider
{
    private sealed partial class MedicPlay
    {
        /// <summary>Stock's follow ranges and heal range (tf_bot_medic_heal.cpp:21-24): close to 75 u, follow from 250 u, heal to 600 u.</summary>
        public const float StopFollow = 75f, StartFollow = 250f, MaxHealRange = 600f;

        /// <summary>A "Medic!" call is answered within this (stock's tf_bot_medic_max_call_response_range) and for this long.</summary>
        public const float CallReach = 1000f, CallKeepSeconds = 8f;

        /// <summary>The medigun's beam connects at 450 u and holds to 1.2 x that (TFMedigunWeapon's stick rule).</summary>
        public const float BeamReach = 450f, BeamHold = 540f;

        /// <summary>Stock's preference tolerances (SelectPreferred): same rank, switch only to one 300 u nearer; better rank, only within 750 u.</summary>
        public const float SameRankTolerance = 300f, BetterRankReach = 750f;

        /// <summary>A patient unhurt this long at full health and not burning is stable (stock's IsStable, :302-321).</summary>
        public const float StableSeconds = 3f;

        /// <summary>A patient who has not fired in this long is quiet: the bot goes behind him (:937-947).</summary>
        public const float QuietSeconds = 5f;

        /// <summary>The pop's health lines (stock's GetUberHealthThreshold 50, "about to die" 25) and the patient's share (0.5).</summary>
        public const int SelfSaveHealth = 50, LastHealth = 25;

        public const float PatientLowShare = 0.5f;

        /// <summary>P7's push trigger: two or more enemies in sight within this of a firing patient; the trade: an Über seen within this.</summary>
        public const float PushReach = 900f, TradeReach = 1500f;

        /// <summary>The cover search's rings (stock's, :958-1047): from the stop range by 100 u to 600 u, 250 u when the patient runs or the charge is ready, 16 ways.</summary>
        public const float CoverStep = 100f, CoverFar = 600f, CoverNear = 250f;

        public const int CoverWays = 16;

        /// <summary>Teammates remembered this long after last seen are still candidates.</summary>
        public const float RememberSeconds = 4f;

        /// <summary>The patient out of sight is still gone after, to where he was last seen or heard, for this long, unless that place is reached first without seeing him (<see cref="LastPlaceReach"/>).</summary>
        public const float PatientMemorySeconds = 8f;

        /// <summary>A patient moving faster than this is walked after, behind him, not waited for (live, ctf_2fort 2026-11-21: a Medic that held within 250 u lost a walking Soldier round corners).</summary>
        public const float PatientMovingSpeed = 60f;

        /// <summary>The place branch's score with a patient: above the fight's 0.8, below delivering a flag; with none, going to find one (below a near fight).</summary>
        public const float PatientUtility = 0.85f, SeekUtility = 0.7f;


        /// <summary>A patient moved this far from where he stood is on the move for <see cref="RunningSeconds"/> (:526-532).</summary>
        public const float AnchorRadius = 200f, RunningSeconds = 3f;

        private const string MedigunClass = "tf_weapon_medigun";

        /// <summary>A teammate as the bot knows him this decide.</summary>
        private struct Mate
        {
            public ActorId Id;
            public AiVector At;
            public AiVector Velocity;
            public TFClass Class;
            public bool Sighted;
            public Tick SeenTick;
            public ActorConditions Conditions;
            public ActorActivity Activity;
            public AiAngles Facing;

            public readonly bool Known => Id.IsValid;
        }

        private readonly Mate[] _mates = new Mate[33];
        private int _mateCount;
        private Mate _patient, _heal;
        private Tick _refreshedAt = new(-1);
        private bool _active;
        private AiRandom? _medRng;

        // The HUD this decide.
        private float _charge, _minCharge = 1f;
        private bool _releasing, _ready, _setup;
        private int _beamSlot, _medigun = -1, _chargeType;
        private int _patientHealth = -1, _patientMost = -1;

        // What outlives a decide.
        private int _lastSelfHealth = -1, _lastPatientHealth = -1, _lastPatientSlot;
        private Tick _selfHurtAt = new(-100000), _patientHurtAt = new(-100000), _patientFiredAt = new(-100000);
        private Tick _retargetUntil, _coverAt, _popDueAt, _runningUntil, _callUntil;
        private bool _popDue, _readyWas;
        private ActorId _caller;
        private AiVector _anchor, _followGoal;
        private int _popRow = -2, _readyRow = -2;

        // The counters.
        private long _peakCharge, _beamOnDecides, _popSends, _beamDrops;
        private int _usesSeen;
        private bool _beamedLast;

        private long _ubers, _dropped, _beamDecides, _aliveDecides, _patientChanges, _callsAnswered, _readySaid, _nearDecides;

        /// <summary>Reads this decide's HUD and picks the patient, once a decide; false when the bot carries no medigun its kit shows (the general play then).</summary>
        private bool Refresh()
        {
            if (_refreshedAt == D._now)
                return _active;
            _refreshedAt = D._now;
            _medRng ??= new AiRandom(D._seed ^ 0x4D45_4443UL);
            _active = false;
            if (D.Kit is not { } hud || !hud.TryWielded(TFLoadoutSlot.Secondary, out TFKitItem gun) || gun.ItemClass != MedigunClass)
                return false;
            _active = true;
            _aliveDecides++;
            // The beam let go by a one-shot (the pop, a voice line): the last decide held it at a friend, and an ability one-shot was sent
            // in that decide after all (an ability intent holds no trigger: the Fable review, C5; the applier's to fix, measured here).
            int uses = 0;
            foreach (int u in D.AbilityUses)
                uses += u;
            _beamDrops += _beamedLast && uses > _usesSeen ? 1 : 0;
            (_usesSeen, _beamedLast) = (uses, false);
            _medigun = gun.WeaponKind;
            _chargeType = (int)gun.Attribute("set_charge_type", 0f);
            _minCharge = _chargeType == 3 ? 0.25f : 1f;
            ReadKit(hud);
            _charge = hud.Meter(TFMedigunMeters.Charge, 0f);
            _peakCharge = Math.Max(_peakCharge, (long)(_charge * 100f));
            bool releasing = hud.Meter(TFMedigunMeters.Releasing, 0f) > 0.5f;
            // An Über is counted when the HUD shows the charge going, not when the pop is sent (a refused release is no Über).
            _ubers += releasing && !_releasing ? 1 : 0;
            _releasing = releasing;
            _beamSlot = (int)hud.Meter(TFMedigunMeters.Patient, 0f);
            _beamOnDecides += _beamSlot != 0 ? 1 : 0;
            _setup = false;
            if (D._view.Objectives is { } objectives)
            {
                foreach (Objective o in objectives.All)
                    _setup |= o.Setup;
            }

            _ready = _charge >= _minCharge - 0.001f && !_setup;
            if (_lastSelfHealth >= 0 && D._self.Health < _lastSelfHealth)
                _selfHurtAt = D._now;
            _lastSelfHealth = D._self.Health;

            GatherMates();
            ChoosePatient(hud);
            ReadPatient(hud);
            ChooseHealTarget(hud);
            return true;
        }

        private float Seconds(Tick since) => (D._now - since) * D._tick;

        /// <summary>The teammates the bot sees, and those it saw within <see cref="RememberSeconds"/>, as its client draws them.</summary>
        private void GatherMates()
        {
            _mateCount = 0;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (_mateCount < _mates.Length && f.Id != D._bot && TeamRule.IsGameTeam(f.Team) && TeamRule.Friends(D._self.Team, f.Team))
                {
                    _mates[_mateCount++] = new Mate
                    {
                        Id = f.Id, At = f.Origin, Velocity = f.Velocity, Class = (TFClass)f.Class, Sighted = true, Conditions = f.Conditions, Activity = f.Activity, Facing = f.Facing,
                    };
                }
            }

            Blackboard board = D._memory.Board;
            for (int s = 0; s < board.Slots && _mateCount < _mates.Length; s++)
            {
                KnownActor a = board.Actor(s);
                float keep = _patient.Known && a.Id == _patient.Id ? PatientMemorySeconds : RememberSeconds;
                if (!a.Known || a.Dead || D._view.TryActor(a.Id, out _) || a.Id == D._bot || !D._memory.IsFriend(a) || !a.EverSeen || Seconds(a.LastSeenTick) > keep)
                    continue;
                // Out of sight: where he was last seen or heard (a teammate has no predictor, so BelievedAt answers that place, a fixed
                // point). Reached without seeing him, the place is spent: he is no candidate until he is seen again (the Fable review, C1).
                AiVector place = a.BelievedAt(D._graph);
                if (Spent(a.Id, a.LastPerceivedTick) || D._self.Origin.DistanceTo(place) < LastPlaceReach)
                {
                    Spend(a.Id, a.LastPerceivedTick);
                    continue;
                }

                _mates[_mateCount++] = new Mate { Id = a.Id, At = place, Velocity = a.LastSeenVel, Class = (TFClass)a.ClassBelieved, SeenTick = a.LastPerceivedTick };
            }
        }

        /// <summary>A teammate's last place within this of the Medic, him not in sight, is spent: going there found nobody.</summary>
        public const float LastPlaceReach = 150f;

        private readonly Tick[] _spentAt = new Tick[33];

        /// <summary>Whether teammate <paramref name="id"/>'s place of <paramref name="seen"/> was reached already without finding him.</summary>
        private bool Spent(ActorId id, Tick seen) => (uint)id.Slot < (uint)_spentAt.Length && _spentAt[id.Slot] == seen && seen.Value > 0;

        private void Spend(ActorId id, Tick seen)
        {
            if ((uint)id.Slot < (uint)_spentAt.Length)
                _spentAt[id.Slot] = seen;
        }

        private bool TryMate(ActorId id, out Mate mate)
        {
            for (int i = 0; i < _mateCount; i++)
            {
                if (_mates[i].Id == id)
                {
                    mate = _mates[i];
                    return true;
                }
            }

            mate = default;
            return false;
        }

        private bool TryMateAt(int slot, out Mate mate)
        {
            for (int i = 0; i < _mateCount; i++)
            {
                if (_mates[i].Id.Slot == slot)
                {
                    mate = _mates[i];
                    return true;
                }
            }

            mate = default;
            return false;
        }

        private float DistanceTo(in Mate m) => D._self.Origin.DistanceTo(m.At);

        /// <summary>Whether <paramref name="m"/> carries the enemy's flag (the HUD's objectives name the carrier).</summary>
        private bool Carries(in Mate m) => D._enemyFlag is { State: ObjectiveState.Carried } flag && flag.CarrierSlot == m.Id.Slot && m.Id.Slot != 0;

        /// <summary>Stock's class rank for a primary patient (SelectPreferred): Heavy, Soldier, Pyro 0, Demoman 3; any other none (999).</summary>
        public static int Rank(TFClass c) => c switch
        {
            TFClass.HeavyWeapons or TFClass.Soldier or TFClass.Pyro => 0,
            TFClass.Demoman => 3,
            _ => 999,
        };

        /// <summary>Whether <paramref name="c"/> may be the primary patient (stock's Inspect, :224-233): never a Medic, Sniper, Engineer or Spy, unless carrying the flag.</summary>
        public static bool MayBePrimary(TFClass c) => c is not (TFClass.Medic or TFClass.Sniper or TFClass.Engineer or TFClass.Spy);

        /// <summary>Whether a charge on <paramref name="c"/> does its job (the wiki's rule; stock's IsGoodUberTarget returns false for all, the review's fix 6).</summary>
        public static bool GoodUberTarget(TFClass c) => c is not (TFClass.Medic or TFClass.Sniper or TFClass.Engineer or TFClass.Scout or TFClass.Spy);

        /// <summary>Stock's SelectPreferred: the contender over the current one when nearer by <see cref="SameRankTolerance"/> at the same rank, or within <see cref="BetterRankReach"/> at a better one.</summary>
        private bool Prefer(in Mate current, in Mate contender)
        {
            // A teammate in sight always beats one only remembered (the Fable review, C1): a stale place must not hold the beam off a
            // Soldier in plain sight.
            if (current.Sighted != contender.Sighted)
                return contender.Sighted;
            int was = RankFor(current.Class), now = RankFor(contender.Class);
            if (was == now)
                return DistanceTo(current) - DistanceTo(contender) > SameRankTolerance;
            return was > now && DistanceTo(contender) < BetterRankReach;
        }

        /// <summary>The patient: kept while the charge runs or is ready on a good target; a caller; the flag carrier; else the best by class.</summary>
        private void ChoosePatient(TFHud hud)
        {
            Mate current = default;
            bool held = _patient.Known && TryMate(_patient.Id, out current);
            if (!held && _beamSlot > 0 && TryMateAt(_beamSlot, out Mate beamed))
                (current, held) = (beamed, true);
            if (held && (_releasing || (_ready && GoodTarget(current.Class) && !Carries(current))))
            {
                Take(current);
                return;
            }

            if (Called(out Mate caller))
            {
                Take(caller);
                return;
            }

            // The flag carrier first, but not with the charge ready: he gets nothing from it, so the Über goes on his escort (medic.md §1.2,
            // the Fable review's C7).
            for (int i = 0; i < _mateCount && !_ready; i++)
            {
                if (Carries(_mates[i]))
                {
                    Take(_mates[i]);
                    return;
                }
            }

            Mate chosen = held && MayBePrimary(current.Class) ? current : default;
            for (int i = 0; i < _mateCount; i++)
            {
                ref readonly Mate m = ref _mates[i];
                if (!MayBePrimary(m.Class))
                    continue;
                if (!chosen.Known || Prefer(chosen, m))
                    chosen = m;
            }

            // None of the preferred classes known: the nearest teammate in sight who is no Medic, rather than nobody (stock retreats to spawn).
            if (!chosen.Known)
            {
                float best = float.MaxValue;
                for (int i = 0; i < _mateCount; i++)
                {
                    ref readonly Mate m = ref _mates[i];
                    if (m.Sighted && m.Class != TFClass.Medic && DistanceTo(m) < best)
                        (best, chosen) = (DistanceTo(m), m);
                }
            }

            Take(chosen);
        }

        private void Take(in Mate m)
        {
            if (m.Known && (!_patient.Known || _patient.Id != m.Id))
            {
                _patientChanges++;
                // A new patient's anchor is where he stands: he is not "on the move" for having been taken.
                _anchor = m.At;
            }

            _patient = m;
        }

        private AiVector _seekAt;

        /// <summary>
        /// Where to find a patient with none known (stock retreats to spawn for one; ours goes to the team), held for
        /// <see cref="SeekCommitSeconds"/> once chosen or until reached (the Fable review, C2: re-asked each decide, the 250 u test on a
        /// sighting flipped the root against the objective branch 30-55 times a cp_granary run). In order: the nearest teammate its HUD
        /// shows calling (a place through walls); the teammate it
        /// perceived last within <see cref="SeekSeenSeconds"/>, where it did, unless that place is spent; then the team's way: the
        /// teammate carrying the enemy's flag, the points' goal, the enemy's flag.
        /// </summary>
        private bool Seek(out AiVector at)
        {
            if (_seekHeld && D._now < _seekUntil && D._self.Origin.DistanceTo(_seekGoal) > ArriveUnits)
            {
                at = _seekGoal;
                return true;
            }

            if (_seekHeld && _seekFrom.IsValid)
                Spend(_seekFrom, _seekFromTick);
            _seekHeld = false;
            // A teammate to go to; with none, look round first (LookingForTeam), and only then the team's way.
            if (!SeekPlace(out at, out _seekFrom, out _seekFromTick, teamWay: false)
                && (LookingForTeam || !SeekPlace(out at, out _seekFrom, out _seekFromTick, teamWay: true)))
                return false;
            (_seekHeld, _seekGoal, _seekUntil) = (true, at, D._now + (int)(SeekCommitSeconds / D._tick));
            _seekPicks++;
            return true;
        }

        /// <summary>How long a seek goal is held once chosen (reached first, it is let go).</summary>
        public const float SeekCommitSeconds = 4f;

        /// <summary>A teammate perceived this long ago is still sought where he was.</summary>
        public const float SeekSeenSeconds = 15f;

        private bool _seekHeld;
        private AiVector _seekGoal;
        private Tick _seekUntil, _seekFromTick;
        private ActorId _seekFrom;
        private long _seekPicks;

        private bool SeekPlace(out AiVector at, out ActorId from, out Tick fromTick, bool teamWay)
        {
            (at, from, fromTick) = (default, default, default);
            if (teamWay)
                return TeamWay(out at);
            float best = float.MaxValue;
            if (D.Kit is { } hud)
            {
                foreach (TFMateHealth m in hud.Mates)
                {
                    float d = D._self.Origin.DistanceTo(m.At);
                    if ((m.Why & TFMateSeen.Calling) != 0 && d < best && d > LastPlaceReach)
                        (at, best) = (m.At, d);
                }
            }

            if (best < float.MaxValue)
                return true;

            Blackboard board = D._memory.Board;
            Tick latest = new(-1);
            for (int s = 0; s < board.Slots; s++)
            {
                KnownActor a = board.Actor(s);
                if (!a.Known || a.Dead || !(a.EverSeen || a.EverHeard) || a.Id == D._bot || !D._memory.IsFriend(a) || a.LastPerceivedTick <= latest
                    || Seconds(a.LastPerceivedTick) > SeekSeenSeconds || Spent(a.Id, a.LastPerceivedTick))
                    continue;
                AiVector place = a.BelievedAt(D._graph);
                if (D._self.Origin.DistanceTo(place) > LastPlaceReach)
                    (latest, at, from, fromTick) = (a.LastPerceivedTick, place, a.Id, a.LastPerceivedTick);
            }

            return latest.Value >= 0;
        }

        /// <summary>The team's way when no teammate is known: its flag carrier, the points' goal, the enemy's flag.</summary>
        private bool TeamWay(out AiVector at)
        {
            at = default;
            // The team's way: its flag carrier, the points' goal (5CP, A/D), the enemy's flag (CTF, where the attack goes).
            if (D._enemyFlag is { State: ObjectiveState.Carried } carried && carried.CarrierSlot != D._bot.Slot && Far(carried.Position))
                at = carried.Position;
            else if (D._pointsGoal is { } goal && Far(goal.Position))
                at = goal.Position;
            else if (D._enemyFlag is { } flag && flag.State != ObjectiveState.Carried && Far(flag.Position))
                at = flag.Position;
            else
                return false;
            return true;
        }

        private bool Far(AiVector at) => D._self.Origin.DistanceTo(at) > ArriveUnits;

        /// <summary>The place branch's score: with a patient his place; with none, finding one.</summary>
        private float PlaceScore()
        {
            if (_patient.Known)
            {
                _lookingSince = default;
                return PatientUtility;
            }

            return Seek(out _seekAt) || LookingForTeam ? SeekUtility : 0f;
        }

        /// <summary>
        /// With nothing known of any teammate, the Medic stands and looks round for <see cref="LookForTeamSeconds"/> (a player at his
        /// spawn turns to find his team; live, ctf_2fort 2026-11-21: a Medic that ran ahead of its slower Soldiers never saw one and
        /// healed nobody in 300 s). Past that, the general play.
        /// </summary>
        private bool LookingForTeam
        {
            get
            {
                if (_lookingSince == default)
                    (_lookingSince, _lookingYaw) = (D._now, D._self.Eye.Yaw);
                return Seconds(_lookingSince) < LookForTeamSeconds;
            }
        }

        /// <summary>How long a Medic with no teammate known looks round for one, and how long each look lasts.</summary>
        public const float LookForTeamSeconds = 6f, LookTurnSeconds = 0.75f;

        private Tick _lookingSince;

        /// <summary>Goes to the patient, or with none to where one may be found, or stands and looks round for one.</summary>
        private bool GoHealPlace()
        {
            if (_patient.Known)
                return FollowPatient();
            if (Seek(out _seekAt))
            {
                D.GoTo(_seekAt, MoveSpeed.Run);
                D._verdict = "to the team";
                return true;
            }

            if (!LookingForTeam)
                return false;
            D._move = MoveIntent.Hold;
            int turn = (int)(Seconds(_lookingSince) / LookTurnSeconds);
            float yaw = (_lookingYaw + 90f * (turn + 1)) * (MathF.PI / 180f);
            var at = D._self.Origin + new AiVector(MathF.Cos(yaw) * 400f, MathF.Sin(yaw) * 400f, 64f);
            D._look = new LookIntent(LookKind.Glance, at, ActorId.None, AimPoint.Body, 0f, 0);
            D._verdict = "looking for the team";
            return true;
        }

        private float _lookingYaw;

        /// <summary>
        /// A teammate's "Medic!" (any teammate's, a bot's too: the review's fix 4) heard within <see cref="CallReach"/>: he is the
        /// patient for <see cref="CallKeepSeconds"/> or until the heal-target panel shows him whole, wherever he was heard.
        /// </summary>
        private bool Called(out Mate caller)
        {
            caller = default;
            BotMemory.HeardCallout call = D._memory.LastHelpCall;
            if (call.Kind == CalloutKind.Help && call.Count == 1 && call.Speaker != D._bot && call.Speaker.IsValid && Seconds(call.Tick) < 1f
                && D._self.Origin.DistanceTo(call.Place) <= CallReach && call.Speaker != _caller)
            {
                (_caller, _callUntil) = (call.Speaker, D._now + (int)(CallKeepSeconds / D._tick));
                _callsAnswered++;
            }

            if (!_caller.IsValid || D._now >= _callUntil)
            {
                _caller = default;
                return false;
            }

            if (_patient.Known && _patient.Id == _caller && _patientMost > 0 && _patientHealth >= _patientMost)
            {
                // Healed: the call is answered.
                _caller = default;
                return false;
            }

            if (TryMate(_caller, out caller))
                return true;
            // Not seen: where he was heard.
            if (D._memory.Board.Of(_caller, make: false) is { Dead: true })
            {
                _caller = default;
                return false;
            }

            caller = new Mate { Id = _caller, At = call.Speaker == _caller ? call.Place : _patient.At, Class = TFClass.Undefined };
            return true;
        }

        /// <summary>The patient's health off the heal-target panel when the beam is on him; whether he was just hurt; whether he fired; whether he runs.</summary>
        private void ReadPatient(TFHud hud)
        {
            (_patientHealth, _patientMost) = (-1, -1);
            if (!_patient.Known)
                return;
            bool shown = hud.TryMate(_patient.Id.Slot, out TFMateHealth m);
            if (shown && (m.Why & TFMateSeen.HealTarget) != 0 && m.MaxHealth > 0)
            {
                (_patientHealth, _patientMost) = (m.Health, m.MaxHealth);
                (_panelSlot, _panelHealth, _panelMost, _panelAt) = (_patient.Id.Slot, m.Health, m.MaxHealth, D._now);
            }
            else if (TargetIdOf(hud, _patient.Id.Slot, out int health, out int most))
            {
                (_patientHealth, _patientMost) = (health, most);
            }
            else if (_panelSlot == _patient.Id.Slot && Seconds(_panelAt) < PanelKeepSeconds && (_patient.Conditions & ActorConditions.Burning) == 0
                     && !(shown && (m.Why & TFMateSeen.Calling) != 0))
            {
                // The beam is away on a momentary target: the panel's last reading stands until a sign a player has says otherwise (he
                // burns, the HUD flags him calling) or it is old (the Fable review, C4: stock's IsStable reads the patient wherever the beam is).
                (_patientHealth, _patientMost) = (_panelHealth, _panelMost);
            }
            if (_patientHealth >= 0 && _lastPatientSlot == _patient.Id.Slot && _lastPatientHealth >= 0 && _patientHealth < _lastPatientHealth)
                _patientHurtAt = D._now;
            (_lastPatientSlot, _lastPatientHealth) = (_patient.Id.Slot, _patientHealth);
            if ((_patient.Activity & ActorActivity.Firing) != 0)
                _patientFiredAt = D._now;
            if (_patient.At.DistanceTo(_anchor) > AnchorRadius)
                (_anchor, _runningUntil) = (_patient.At, D._now + (int)(RunningSeconds / D._tick));
        }

        /// <summary>The crosshair's target ID (<see cref="TFTargetIdMeters"/>) when it shows player <paramref name="slot"/>: his health and most.</summary>
        private static bool TargetIdOf(TFHud hud, int slot, out int health, out int most)
        {
            (health, most) = (-1, -1);
            if ((int)hud.Meter(TFTargetIdMeters.Slot, 0f) != slot || slot == 0)
                return false;
            (health, most) = ((int)hud.Meter(TFTargetIdMeters.Health, -1f), (int)hud.Meter(TFTargetIdMeters.Most, -1f));
            return most > 0;
        }

        /// <summary>The patient is stable (stock's IsStable): the panel shows him whole, unhurt for <see cref="StableSeconds"/>, not burning.</summary>
        private bool PatientStable
            => _patient.Known && _patientMost > 0 && _patientHealth >= _patientMost && Seconds(_patientHurtAt) >= StableSeconds
               && (_patient.Conditions & ActorConditions.Burning) == 0;

        /// <summary>
        /// The momentary heal target (stock's :589-604 over CFindMostInjuredNeighbor with a player's reads): with the patient stable and
        /// no charge running, a teammate in sight within 0.9 of the beam's reach who burns, else, out of a fight, one the HUD shows calling
        /// or the target ID shows under his most; the nearest.
        /// </summary>
        private void ChooseHealTarget(TFHud hud)
        {
            _heal = _patient;
            if (!_patient.Known || _releasing || !PatientStable)
            {
                _momentary = default;
                return;
            }

            // The momentary target keeps the beam until the panel shows him whole and he burns no more (the Fable review, C4).
            float keepReach = 0.9f * BeamReach;
            if (_momentary.IsValid && _momentary != _patient.Id && TryMate(_momentary, out Mate kept) && kept.Sighted && DistanceTo(kept) <= keepReach
                && !Whole(hud, kept))
            {
                _heal = kept;
                return;
            }

            _momentary = default;
            bool fighting = Seconds(_patientFiredAt) < 1f;
            float reach = 0.9f * BeamReach, best = float.MaxValue;
            bool bestBurns = false;
            for (int i = 0; i < _mateCount; i++)
            {
                ref readonly Mate m = ref _mates[i];
                if (!m.Sighted || m.Id == _patient.Id || DistanceTo(m) > reach)
                    continue;
                bool burns = (m.Conditions & ActorConditions.Burning) != 0;
                bool hurt = (hud.TryMate(m.Id.Slot, out TFMateHealth shown) && (shown.Why & TFMateSeen.Calling) != 0)
                            || (TargetIdOf(hud, m.Id.Slot, out int health, out int most) && health < most);
                if (!burns && (fighting || !hurt))
                    continue;
                if ((burns && !bestBurns) || (burns == bestBurns && DistanceTo(m) < best))
                    (_heal, best, bestBurns) = (m, DistanceTo(m), burns);
            }

            if (_heal.Id != _patient.Id)
                _momentary = _heal.Id;
        }

        /// <summary>Whether the heal-target panel shows <paramref name="m"/> at his most, and he does not burn.</summary>
        private static bool Whole(TFHud hud, in Mate m)
            => (m.Conditions & ActorConditions.Burning) == 0 && hud.TryMate(m.Id.Slot, out TFMateHealth shown) && (shown.Why & TFMateSeen.HealTarget) != 0
               && shown.MaxHealth > 0 && shown.Health >= shown.MaxHealth;

        /// <summary>The patient's last panel reading stands this long while the beam is away.</summary>
        public const float PanelKeepSeconds = 5f;

        private ActorId _momentary;
        private int _panelSlot, _panelHealth, _panelMost;
        private Tick _panelAt;

        private bool HoldingMedigun => _medigun >= 0 && D._self.ActiveWeapon == _medigun;

        /// <summary>Whether the beam is on whom the bot wants, or on nobody yet (stock: only then is the trigger held, :609-614).</summary>
        private bool BeamOnWanted => _beamSlot == 0 || (_heal.Known && _beamSlot == _heal.Id.Slot);

        private AiVector BodyOf(in Mate m) => m.At + new AiVector(0f, 0f, TFKnowledge.AimHeight(AimPoint.Body, m.Class).Standing);

        /// <summary>The beam on the heal target; with the primary or melee out and an enemy in sight, the general fire.</summary>
        private void MedicEngage()
        {
            // Another weapon wanted than the one in hand (the medigun back for a patient in reach, the primary for a nearer enemy): no shot
            // and no beam this decide, so the hands may switch (the Fable review, C3: the general fire held the action and the hands never
            // asked, the syringe sprayed its clip dry first).
            if (WantWeapon() != D._self.ActiveWeapon)
                return;
            if (!HoldingMedigun)
            {
                if (!BoltAtMate())
                {
                    D.GeneralEngage();
                    CountSwing();
                }

                return;
            }

            // The Ubersaw at an enemy in reach: the beam is let go so the hands may take it out.
            if (SawWanted)
                return;
            if (!_heal.Known || !_heal.Sighted || DistanceTo(_heal) > BeamHold)
            {
                if (_heal.Known && _heal.Sighted)
                    D._look = new LookIntent(LookKind.Actor, BodyOf(_heal), _heal.Id, AimPoint.Body, 0f, 0);
                return;
            }

            if (!BeamOnWanted && D._now >= _retargetUntil)
            {
                // Let go for one decide so the medigun picks again, then hold again for 1 to 2 s (stock's change-patient timer).
                _retargetUntil = D._now + (int)((1f + _medRng!.NextFloat()) / D._tick);
                D._look = new LookIntent(LookKind.Actor, BodyOf(_heal), _heal.Id, AimPoint.Body, 0f, 0);
                return;
            }

            FireAtFriend(_heal.Id, BodyOf(_heal));
            _beamDecides++;
            _beamedLast = true;
        }

        /// <summary>The medigun out, unless the bot must fight (stock's :735-756): no charge ready, not invulnerable, the beam on nobody, and an enemy in sight nearer than the patient, or the patient beyond 1.1 x 600 u or out of sight.</summary>
        private void MedicHands()
        {
            if (D._action.Kind != ActionKind.None || D.Busy || D._now < D._handsAt)
                return;
            int want = WantWeapon();
            if (D._self.ActiveWeapon != want)
                D.Select(want);
        }

        /// <summary>The weapon the Medic wants in hand now (<see cref="MedicHands"/>'s rule and the kit's).</summary>
        private int WantWeapon()
        {
            bool invulnerable = (D._self.Conditions & ActorConditions.Invulnerable) != 0;
            bool healing = _beamSlot != 0;
            bool threatened = D._targetSighted && (!_heal.Known || D._targetDist < DistanceTo(_heal));
            bool far = !_heal.Known || DistanceTo(_heal) > 1.1f * MaxHealRange;
            bool obscured = !_heal.Known || !_heal.Sighted;
            int primary = D.PrimaryKind;
            bool fight = D._targetSighted && primary >= 0 && !_ready && !invulnerable && !healing && (threatened || far || obscured);
            int want = fight ? primary : _medigun;
            if (KitWeapon() is >= 0 and var kit)
                want = kit;
            return want;
        }

        /// <summary>The root's branches as a Medic weighs them: with a patient, his place first; the flag and the fight much less; going to heal less while beaming.</summary>
        private float MedicWeigh(string branch)
        {
            // A Medic never goes for the flag on its own (it takes no charge while carrying, and stock's never does).
            if (!_patient.Known)
                return branch == Fetch ? 0.2f : 1f;
            return branch switch
            {
                Fetch => 0.2f,
                Fight or Defend or Search or Course or PointsBranch or TeamBranch or ChaseCarrier or Guard or Escort => 0.5f,
                Survive => _beamSlot != 0 ? 0.4f : 1f,
                _ => 1f,
            };
        }

        /// <summary>Goes where the Medic heals from (stock's :758-782): close and in sight with the charge ready or running or the beam on another; else the follow place, worked out again every 0.5 to 1 s or when seen.</summary>
        private bool FollowPatient()
        {
            if (!_patient.Known)
                return false;
            float dist = DistanceTo(_patient);
            if (dist <= BeamReach)
                _nearDecides++;
            bool invulnerable = (D._self.Conditions & ActorConditions.Invulnerable) != 0;
            if (invulnerable || _ready || !BeamOnWanted)
            {
                if (dist > StopRange || !_patient.Sighted)
                    D.GoTo(_patient.At, MoveSpeed.Run);
                else
                    D._move = MoveIntent.Hold;
                D._verdict = "close on patient";
                return true;
            }

            bool exposed = Exposed();
            if (D._now >= _coverAt || exposed)
            {
                _coverAt = D._now + (int)((0.5f + 0.5f * _medRng!.NextFloat()) / D._tick);
                _followGoal = FollowPlace(exposed);
            }

            if (D._self.Origin.DistanceTo(_followGoal) < 24f)
                D._move = MoveIntent.Hold;
            else
                D.GoTo(_followGoal, MoveSpeed.Run);
            D._verdict = exposed ? "patient, cover" : "patient";
            return true;
        }

        /// <summary>Whether an enemy the bot sees could see it: an enemy in its sight.</summary>
        private bool Exposed()
        {
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.IsGameTeam(f.Team) && !TeamRule.Friends(D._self.Team, f.Team))
                    return true;
            }

            return false;
        }

        /// <summary>Stock's ComputeFollowPosition (:902-1047) over what the bot sees.</summary>
        private AiVector FollowPlace(bool exposed)
        {
            bool near = DistanceTo(_patient) < StartRange && _patient.Sighted;
            if (!exposed)
            {
                if (!near)
                    return _patient.At;
                float yaw = _patient.Facing.Yaw * (MathF.PI / 180f);
                var forward = new AiVector(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
                AiVector to = _patient.At - D._self.Origin;
                if (!_setup && Seconds(_patientFiredAt) > QuietSeconds && forward.X * to.X + forward.Y * to.Y < 0f)
                    return _patient.At - forward * StopRange;
                var flat = new AiVector(_patient.Velocity.X, _patient.Velocity.Y, 0f);
                if (flat.Length > PatientMovingSpeed)
                    return _patient.At - flat.Normalised() * StopRange;
                return D._self.Origin;
            }

            return CoverNearPatient() ?? Behind();
        }

        /// <summary>The nearest point to the bot on rings round the patient whose cell no enemy in sight sees (the level's cell sight), or null.</summary>
        private AiVector? CoverNearPatient()
        {
            NavGraph? graph = D._graph;
            ICellSight? sight = D._memory.World.Sight;
            if (graph is null || sight is null)
                return null;
            Span<int> eyes = stackalloc int[16];
            int n = 0;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (n < eyes.Length && TeamRule.IsGameTeam(f.Team) && !TeamRule.Friends(D._self.Team, f.Team) && graph.FindCell(f.Origin + new AiVector(0f, 0f, 8f)) is { IsValid: true } c)
                    eyes[n++] = c.Index;
            }

            float far = _ready || D._now < _runningUntil ? CoverNear : CoverFar;
            // A cover spot keeps a line to the patient (stock traces out from his centre and stops at the wall, :1000-1020): the next
            // room is no cover to heal from (the Fable review, C6). With nothing unseen, the Medic stays where it is, as stock's does.
            CellId from = graph.FindCell(_patient.At + new AiVector(0f, 0f, 32f));
            AiVector? best = null;
            float bestDist = float.MaxValue;
            for (float r = StopRange + CoverStep * _medRng!.NextFloat(); r <= far; r += CoverStep)
            {
                for (int w = 0; w < CoverWays; w++)
                {
                    float a = w * (2f * MathF.PI / CoverWays);
                    AiVector p = _patient.At + new AiVector(MathF.Cos(a) * r, MathF.Sin(a) * r, 0f);
                    CellId cell = graph.FindCell(p + new AiVector(0f, 0f, 32f));
                    if (!cell.IsValid)
                        continue;
                    bool seen = false;
                    for (int e = 0; e < n && !seen; e++)
                        seen = sight.Visible(eyes[e], cell.Index);
                    float d = D._self.Origin.DistanceTo(p);
                    if (!seen && d < bestDist && (!from.IsValid || sight.Visible(from.Index, cell.Index)))
                        (best, bestDist) = (p, d);
                }
            }

            return best ?? D._self.Origin;
        }

        /// <summary>With no cell sight: behind the patient, away from the enemies in sight, at the stop range.</summary>
        private AiVector Behind()
        {
            AiVector sum = default;
            int n = 0;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.IsGameTeam(f.Team) && !TeamRule.Friends(D._self.Team, f.Team))
                    (sum, n) = (sum + f.Origin, n + 1);
            }

            if (n == 0)
                return _patient.At;
            AiVector away = new AiVector(_patient.At.X - sum.X / n, _patient.At.Y - sum.Y / n, 0f).Normalised();
            return _patient.At + away * (StopRange * 2f);
        }

        /// <summary>The pop when it is wanted (after the bot's reaction), else "ÜberCharge ready!" once a charge.</summary>
        private void MedicOneShots()
        {
            if (_popRow == -2)
                (_popRow, _readyRow) = (D.AbilityRow(TFMedicClass.Pop), D.AbilityRow(TFMedicClass.VoiceUberReady));
            bool newlyReady = _ready && !_readyWas;
            _readyWas = _ready;
            if (_ready && !_releasing && HoldingMedigun && PopWanted())
            {
                if (!_popDue)
                    (_popDue, _popDueAt) = (true, D._now + (int)(D._p.ReactionMs / 1000f / D._tick));
                if (D._now >= _popDueAt && _popRow >= 0)
                {
                    D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(_popRow, 0), ++D._oneShot);
                    _popDue = false;
                    _popSends++;
                }

                return;
            }

            // Stock's delay timer keeps running once started (m_delayUberTimer is let go only by the pop): a patient hovering at half health
            // does not start the wait again (the Fable review, C7). Without a charge to pop, the wait is over.
            if (!_ready || _releasing)
                _popDue = false;
            if (newlyReady && _readyRow >= 0 && D._action.Kind is ActionKind.None or ActionKind.Fire)
            {
                D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(_readyRow, 0), ++D._oneShot);
                _readySaid++;
            }
        }

        /// <summary>Whether the Über should go now (stock's :648-673 and P7's push and trade): the stock gun's rule; the Kritzkrieg's and the Quick-Fix's are their own (TFMedicKits.cs).</summary>
        private bool PopWanted() => _chargeType switch
        {
            KritzCharge => KritzPopWanted(),
            MegahealCharge => QuickFixPopWanted(),
            _ => UberPopWanted(),
        };

        private bool UberPopWanted()
        {
            bool selfSave = (D._self.Health < SelfSaveHealth && Seconds(_selfHurtAt) < 1f) || D._self.Health < LastHealth;
            if (selfSave)
                return true;
            if (!_patient.Known || Carries(_patient))
                return false;
            bool covered = (_patient.Conditions & ActorConditions.Invulnerable) != 0;
            if (!covered && _patientMost > 0 && _patientHealth < PatientLowShare * _patientMost)
                return true;
            return Push() || Trade();
        }

        /// <summary>P7's push (the review's fix 5): the patient in sight firing, with two or more enemies in sight within <see cref="PushReach"/> of him.</summary>
        private bool Push()
        {
            if (!_patient.Sighted || Seconds(_patientFiredAt) > 1f || !GoodTarget(_patient.Class))
                return false;
            int n = 0;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.IsGameTeam(f.Team) && !TeamRule.Friends(D._self.Team, f.Team) && f.Origin.DistanceTo(_patient.At) <= PushReach)
                    n++;
            }

            return n >= 2;
        }

        /// <summary>The trade: an enemy seen invulnerable within <see cref="TradeReach"/>, so both charges fade together.</summary>
        private bool Trade()
        {
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.IsGameTeam(f.Team) && !TeamRule.Friends(D._self.Team, f.Team) && (f.Conditions & ActorConditions.Invulnerable) != 0
                    && f.Origin.DistanceTo(D._self.Origin) <= TradeReach)
                    return true;
            }

            return false;
        }

        /// <summary>What a life's end forgets; a charge of 0.9 or more lost is a dropped Über.</summary>
        private void MedicDied()
        {
            _seekHeld = false;
            if (_charge >= 0.9f)
                _dropped++;
            (_patient, _heal, _caller, _charge, _readyWas, _popDue) = (default, default, default, 0f, false, false);
            (_lastSelfHealth, _lastPatientHealth, _lastPatientSlot, _lookingSince) = (-1, -1, 0, default);
        }
    }
}
