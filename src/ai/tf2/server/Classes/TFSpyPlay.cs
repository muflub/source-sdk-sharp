//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Spy's play (the Spy research §4.3, the stock <c>tf_bot_spy_*</c> behaviours in the bot's own senses): it puts a
    /// disguise on out of its enemies' sight, crosses toward a fight cloaked and drops the cloak short of it, walks round
    /// to its victim's back on the facing every client draws, and swings the knife only when the game's own test would
    /// give a backstab. Found out (hurt, undisguised), it fights with the revolver. The Saboteur goes for the enemy's
    /// buildings first and saps them. Its victims are chosen Medic first (pub order).
    /// </summary>
    internal sealed class SpyPlay(TFDecider d) : ClassPlay(d, TFClass.Spy)
    {
        /// <summary>Stock's <c>tf_bot_spy_knife_range</c> (tf_bot_spy_attack.cpp:17): the knife comes out inside it.</summary>
        public const float KnifeRange = 300f;

        /// <summary>
        /// Stock's melee swing (tf_weaponbase_melee.cpp:164-179, :405-440): a 48 u trace from the eye along the aim, swept
        /// with an 18 u box. The aim takes the swing up or down to the victim, so the reach is from the eye to the nearest
        /// point of the victim's body, not between the two origins (review F4: a Spy 43 u off and 46 u above its victim,
        /// 65 u apart by origin, never swung).
        /// </summary>
        public const float SwingRange = 48f, SwingHull = 18f;

        /// <summary>A body's half width and height, standing and crouched, as the hulls the swing hits.</summary>
        public const float BodyHalf = 24f, BodyTall = 82f, BodyCrouched = 62f;

        /// <summary>Within this the Spy counts as on its victim: there long without a swing (<see cref="StallSeconds"/>) it goes by the paths.</summary>
        public const float StallNear = 150f;

        /// <summary>Near the victim this long with no swing, the Spy re-paths to it; twice this, it lets that victim go for <see cref="LetGoSeconds"/>.</summary>
        public const float StallSeconds = 2f, LetGoSeconds = 10f;

        /// <summary>A victim this far above or below is on another level: the Spy goes by the paths, never a straight line.</summary>
        public const float OtherLevel = 40f;

        /// <summary>Whether a swing from <paramref name="eye"/> reaches a body standing (or crouched) at <paramref name="origin"/> (stock's trace, <see cref="SwingRange"/>).</summary>
        public static bool InSwingReach(AiVector eye, AiVector origin, bool crouched)
        {
            float top = origin.Z + (crouched ? BodyCrouched : BodyTall);
            var nearest = new AiVector(Math.Clamp(eye.X, origin.X - BodyHalf, origin.X + BodyHalf), Math.Clamp(eye.Y, origin.Y - BodyHalf, origin.Y + BodyHalf),
                                       Math.Clamp(eye.Z, origin.Z, top));
            return eye.DistanceTo(nearest) <= SwingRange + SwingHull * 0.5f;
        }

        /// <summary>Stock's circle-strafe radius (tf_bot_spy_attack.cpp:190-221): inside it, not behind, the Spy goes round to the back.</summary>
        public const float CircleRange = 250f;

        /// <summary>How far behind its victim the Spy aims to stand.</summary>
        public const float BehindStand = 36f;

        /// <summary>The cloak (research §4.3): pressed with at least this much meter, an enemy within <see cref="CloakNear"/>.</summary>
        public const float CloakFrom = 40f, CloakNear = 1500f;

        /// <summary>The cloak drops at this meter, or when no enemy is within <see cref="DecloakClear"/>, or the victim is within <see cref="DecloakShort"/>.</summary>
        public const float DecloakAt = 15f, DecloakClear = 1200f, DecloakShort = 650f;

        /// <summary>The least time between two presses of the watch's key: a decloak's fade and its no-attack (tf_player_shared.cpp:116, :168).</summary>
        public const float CloakEverySeconds = 2f;

        /// <summary>A disguise asked again at most this often, while one is not going on.</summary>
        public const float DisguiseEverySeconds = 3f;

        /// <summary>Found out: hurt by someone within this long (stock's cover-blown test, tf_bot_spy_attack.cpp:165-178, "hurt within 1 s").</summary>
        public const float BlownSeconds = 1f;

        /// <summary>The sapper's reach from the bot (TFSapperUse: 160 u in a 0.766 cone), sent a little inside it.</summary>
        public const float SapReach = 140f;

        /// <summary>The pub victims' order (research §1, Community Spy strategy; the numbers a ranking, to measure).</summary>
        public static float Priority(TFClass c) => c switch
        {
            TFClass.Medic => 1f,
            TFClass.Sniper => 0.9f,
            TFClass.Engineer => 0.8f,
            TFClass.HeavyWeapons => 0.7f,
            TFClass.Demoman => 0.6f,
            TFClass.Soldier => 0.5f,
            TFClass.Scout => 0.3f,
            TFClass.Pyro => 0.2f,
            _ => 0.4f,
        };

        /// <summary>
        /// How a disguise of class <paramref name="c"/> walks (research §4.3): the disguise clamps the Spy's speed to its
        /// class's (TFMaxSpeed.cs:108-109), so a slow one costs time.
        /// </summary>
        public static float DressSpeed(TFClass c) => c switch
        {
            TFClass.Pyro or TFClass.Engineer or TFClass.Sniper or TFClass.Medic => 1f,
            TFClass.Scout => 0.6f,
            TFClass.Demoman => 0.5f,
            TFClass.Soldier => 0.3f,
            TFClass.HeavyWeapons => 0.2f,
            _ => 0f,
        };

        private bool Saboteur => string.Equals(Archetype, TFSpyClass.Saboteur, StringComparison.OrdinalIgnoreCase);

        private bool Gunspy => string.Equals(Archetype, TFSpyClass.Gunspy, StringComparison.OrdinalIgnoreCase);

        /// <summary>The watch in the kit feigns (the Dead Ringer's <c>set_weapon_mode</c> 1): the key arms it, never a cloak to cross.</summary>
        private bool FeignWatch => D.Kit is { } kit && kit.TryItem(TFLoadoutSlot.Pda2, out TFKitItem w) && Mode(w) == 1;

        /// <summary>The revolver in the kit crits on a head shot (the Ambassador's <c>set_weapon_mode</c> 1 on the revolver).</summary>
        private bool HeadshotGun => D.Kit is { } kit && kit.TryItem(TFLoadoutSlot.Secondary, out TFKitItem g) && Mode(g) == 1;

        private static int Mode(TFKitItem item)
        {
            foreach (global::TF2.Server.Econ.TFOwnKitAttribute a in item.Attributes)
            {
                if (a.Class == "set_weapon_mode")
                    return (int)a.Value;
            }

            return 0;
        }

        private bool FeignArmed => Meter(TFSpyMeters.Feign) > 0.5f;

        /// <summary>The Gunspy's band (research §2 A5): the revolver from 300 to 1,200 u, the knife only on a back within 250.</summary>
        public const float GunFar = 1200f, GunKnifeNear = CircleRange;

        /// <summary>The Ambassador's head shot crits only after 1.0 s since the last shot (tf_weapon_revolver.cpp:85), with a tick's margin.</summary>
        public const float HeadshotEverySeconds = 1.05f;

        /// <summary>A feign's run (TFSpy's 3 s speed window): away while it lasts, the cloak dropped only out of sight after it.</summary>
        public const float FleeSeconds = 3f;

        /// <summary>The Dead Ringer is armed with a full meter (TFSpy: the arm needs it) and an enemy within this.</summary>
        public const float ArmFrom = 100f;

        private Tick _shotAt, _fleeUntil;
        private bool _wasArmed;
        private int _arms, _feigns, _cloakedDecides, _lives;
        private bool _deadNow;

        private Tick _disguiseAt, _cloakAt, _sapAt, _clearSince;
        private int _disguises, _cloaks, _decloaks, _swings, _stabSwings, _backstabs, _saps, _decides, _disguisedDecides, _gunShots;
        private Tick _stabbedAt, _killsSeen;
        private int _sapRow = -2, _cloakRow = -2;
        private readonly int[] _dressRows = [-2, -2, -2, -2, -2, -2, -2, -2, -2, -2];

        private float Meter(string name) => D.Kit?.Meter(name, 0f) ?? 0f;

        private bool Disguised => Meter(TFSpyMeters.Disguised) > 0.5f;

        private bool Disguising => Meter(TFSpyMeters.Disguising) > 0.5f;

        private bool Cloaked => Meter(TFSpyMeters.Cloaked) > 0.5f;

        private float CloakLeft => Meter(TFSpyMeters.CloakMeter);

        /// <summary>Found out: undisguised and uncloaked, or hurt by someone in the last second.</summary>
        private bool Blown => (!Disguised && !Disguising && !Cloaked) || (D._memory.Board.LastAttackTick.Value > 0
                                                                          && (D._now - D._memory.Board.LastAttackTick) * D._tick < BlownSeconds);

        private int Row(ref int cached, string name) => cached == -2 ? cached = D.AbilityRow(name) : cached;

        private bool Send(int row)
        {
            if (row < 0)
                return false;
            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(row, 0), ++D._oneShot);
            return true;
        }

        /// <summary>
        /// Whether the Spy stands behind <paramref name="victim"/> by the facing every client draws: stock's test on the
        /// flat vectors (tf_bot_spy_attack.cpp:131-151; the game's own, TFSpy.Swing, is a dot over 0), at a tolerance its
        /// Cunning sets, from Easy's 0.9 to Expert's 0 (stock's per difficulty; Easy's "always thinks it is behind" not copied).
        /// </summary>
        public static bool Behind(AiVector self, in ActorFacts victim, float tolerance)
        {
            AiVector to = new AiVector(victim.Origin.X - self.X, victim.Origin.Y - self.Y, 0f);
            if (to.Length < 1f)
                return false;
            AiVector forward = VisionMath.Forward(victim.Facing with { Pitch = 0f });
            return new AiVector(forward.X, forward.Y, 0f).Normalised().Dot(to.Normalised()) > tolerance;
        }

        /// <summary>Whether <paramref name="f"/> looks toward <paramref name="at"/> (within some 45°, by the facing a client draws).</summary>
        public static bool FacesAt(in ActorFacts f, AiVector at)
        {
            AiVector to = new AiVector(at.X - f.Origin.X, at.Y - f.Origin.Y, 0f);
            if (to.Length < 1f)
                return true;
            AiVector forward = VisionMath.Forward(f.Facing with { Pitch = 0f });
            return new AiVector(forward.X, forward.Y, 0f).Normalised().Dot(to.Normalised()) > 0.7f;
        }

        /// <summary>The behind tolerance by Cunning (its trust in a plan, <see cref="DecisionParams.FamilyTrust"/>: 0.1 to 0.95).</summary>
        private float Tolerance => Math.Clamp(0.9f * (1f - (D._p.FamilyTrust - 0.1f) / 0.85f), 0f, 0.9f);

        private bool TargetFacts(out ActorFacts f)
        {
            f = default;
            return D._targetSighted && D._target is { } t && D._view.TryActor(t.Id, out f);
        }

        private Tick _stallSince, _letGoUntil;
        private AiVector _stallSelf, _stallVictim, _letGoAt;
        private ActorId _letGo;
        private bool _repath;
        private int _stalls, _letGos;

        /// <summary>How far the Spy or its victim must go to count as moving (a step).</summary>
        public const float StallMove = 24f;

        /// <summary>
        /// The stall watch, every decide (review F4, and the Spy-against-Spy stand): with a victim within <see cref="StallNear"/>,
        /// <see cref="StallSeconds"/> with no swing and neither the Spy nor the victim moving more than <see cref="StallMove"/>
        /// re-paths; twice that lets the victim, and the place it stands at, go for <see cref="LetGoSeconds"/>. Watched by place
        /// and not by the victim's actor, whose id changes with every disguise (two Spies locked on each other changed theirs),
        /// and whether the victim is in sight or only believed (a chase).
        /// </summary>
        private void WatchStall()
        {
            if (D._target is not { } victim || D._targetDist > StallNear)
            {
                (_stallSince, _repath) = (default, false);
                return;
            }

            if (_stallSince.Value == 0 || D._self.Origin.DistanceTo(_stallSelf) > StallMove || D._targetPos.DistanceTo(_stallVictim) > StallMove)
            {
                (_stallSince, _stallSelf, _stallVictim, _repath) = (D._now, D._self.Origin, D._targetPos, false);
                return;
            }

            float still = (D._now - _stallSince) * D._tick;
            if (still >= 2f * StallSeconds)
            {
                (_letGo, _letGoAt, _letGoUntil) = (victim.Id, D._targetPos, D._now + (int)(LetGoSeconds / D._tick));
                (_stallSince, _repath) = (default, false);
                _letGos++;
                return;
            }

            if (still >= StallSeconds && !_repath)
                _stalls++;
            _repath = still >= StallSeconds;
        }

        /// <summary>
        /// Whether the Spy let this victim go (<see cref="WatchStall"/>) and the time has not run out: by its actor, or by its
        /// standing where the let-go victim stood (another disguise of the same body).
        /// </summary>
        private bool LetGo(ActorId victim, AiVector at)
            => D._now < _letGoUntil && (victim == _letGo || at.DistanceTo(_letGoAt) <= StallNear);

        /// <summary>
        /// The fight with a let-go victim weighs next to nothing, so the flag or a retreat takes the bot elsewhere: the Spy's own
        /// fight leaf can answer no, but a chase of the believed victim is the general fight's (the Spy-against-Spy stand).
        /// </summary>
        public override float Weigh(string branch)
            => branch == TFDecider.Fight && D._target is { } t && LetGo(t.Id, D._targetPos) ? LetGoWeight : 1f;

        /// <summary>What the fight with a let-go victim weighs against the other branches.</summary>
        public const float LetGoWeight = 0.05f;

        public override float TargetBonus(KnownActor target, bool sighted, float distance)
        {
            if (LetGo(target.Id, sighted ? target.LastSeenPos : target.BelievedAt(D._graph)))
                return -LetGoPenalty;
            float p = Priority((TFClass)target.ClassBelieved);
            // A victim near is worth more: stock changes victim for one 300 u nearer (tf_bot_spy_attack.cpp:18).
            return 2f * p + (distance < KnifeRange ? 0.5f : 0f);
        }

        /// <summary>What a let-go victim's priority loses: any other victim in sight comes first.</summary>
        public const float LetGoPenalty = 20f;

        /// <summary>A disguised Spy walks past a sentry: it does not shoot him (research §1); found out he keeps away, the Saboteur never.</summary>
        public override bool AvoidsSentries => !Saboteur && Blown;

        /// <summary>The Saboteur's own place (research §2 A3, stock tf_bot_spy_sap.cpp): the enemy sentry it last saw, while it has one.</summary>
        public override float PlaceUtility() => Saboteur && !Blown && NearestSentry(out _) ? SaboteurPull : 0f;

        /// <summary>How hard the Saboteur's sentry pulls against the objectives (a root utility; the Fetch branch's is about 0.6).</summary>
        public const float SaboteurPull = 0.9f;

        public override bool GoPlace()
        {
            if (!NearestSentry(out AiVector at))
                return false;
            D.GoTo(at, MoveSpeed.Run);
            D._verdict = "to the sentry";
            return true;
        }

        private bool NearestSentry(out AiVector at)
        {
            at = default;
            float best = float.MaxValue;
            foreach (SeenSentry s in D.Sentries)
            {
                float dist = D._self.Origin.DistanceTo(s.At);
                if (dist < best)
                    (best, at) = (dist, s.At);
            }

            return best < float.MaxValue;
        }

        public override void Engage()
        {
            if (!TargetFacts(out ActorFacts f))
                return;
            if (Cloaked || LetGo(f.Id, f.Origin))
            {
                // No attack while cloaked, nor at a victim it let go: look the way it goes.
                return;
            }

            if (D.HoldingMelee)
            {
                AiVector body = new(f.Origin.X, f.Origin.Y, f.Origin.Z + 40f);
                D._look = new LookIntent(LookKind.Actor, body, f.Id, AimPoint.Body, 0f, 0);
                if (!InSwingReach(D.OwnEye, f.Origin, (f.Activity & ActorActivity.Crouched) != 0))
                    return;
                bool behind = Behind(D._self.Origin, in f, Tolerance);
                // Found out within arm's reach, the knife is all there is (stock's knife-spam inside 100 u).
                if (!behind && !Blown)
                    return;
                D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0);
                _swings++;
                _stallSince = default;
                if (behind)
                {
                    _stabSwings++;
                    _stabbedAt = D._now;
                }

                return;
            }

            // The revolver only when found out (research §4.3): a shot ends the disguise. The Gunspy opens with it inside its band.
            if (Blown || (Gunspy && D._targetDist <= GunFar))
            {
                D.GeneralEngage();
                if (HeadshotGun)
                    HeadShot(in f);
                if (D._action.Kind == ActionKind.Fire)
                {
                    _gunShots++;
                    _shotAt = D._now;
                }
            }
        }

        /// <summary>
        /// The Ambassador's shot (research §4.3's cover blown): at the head the bot's client draws, and only when the gun's
        /// head shot would crit again (<see cref="HeadshotEverySeconds"/>) inside its 1,200 u.
        /// </summary>
        private void HeadShot(in ActorFacts f)
        {
            float eye = Knowledge.TFKnowledge.AimHeight(AimPoint.Head, (TFClass)f.Class).At((f.Activity & ActorActivity.Crouched) != 0);
            var head = new AiVector(f.Origin.X, f.Origin.Y, f.Origin.Z + eye);
            D._look = new LookIntent(LookKind.Actor, head + D.AimError(head, false), f.Id, AimPoint.Head, 0f, 0);
            if (D._action.Kind == ActionKind.Fire && ((_shotAt.Value > 0 && (D._now - _shotAt) * D._tick < HeadshotEverySeconds) || D._targetDist > GunFar))
                D._action = ActionIntent.None;
        }

        public override void Hands()
        {
            if (D._action.Kind != ActionKind.None || D.Busy || D._now < D._handsAt)
                return;
            int knife = D.KindIn(TFLoadoutSlot.Melee), gun = D.KindIn(TFLoadoutSlot.Secondary);
            bool gunFight = D._targetSighted && (Blown ? D._targetDist > 120f : Gunspy && !(D._targetDist < GunKnifeNear && TargetFacts(out ActorFacts f)
                                                                                                && Behind(D._self.Origin, in f, Tolerance)));
            int want = gunFight && gun >= 0 ? gun : knife >= 0 ? knife : gun;
            if (want >= 0 && D._self.ActiveWeapon != want)
            {
                D.Select(want);
                return;
            }

            if (!D._targetSighted && D._self.ActiveWeapon == gun)
                D.GeneralHands();
        }

        public override bool FightInSight()
        {
            if ((Blown || Gunspy) && !D.HoldingMelee)
                return D.FightAtRange();
            if (!TargetFacts(out ActorFacts f))
                return D.FightAtRange();
            // A victim it let go (stalled on it twice as long): no fight with it, so the tree's other branches (the flag, a
            // retreat) take the bot elsewhere. A priority penalty alone left a sole victim the target (2fort, 18 s at a ledge).
            if (LetGo(f.Id, f.Origin))
            {
                D._verdict = "let the victim go";
                return false;
            }

            if (_repath)
            {
                // Long on the victim with no swing (a door, a step, the wrong level): by the paths, as stock's chase does.
                D.GoTo(f.Origin, MoveSpeed.Run);
                D._verdict = "re-pathing to the victim";
                return true;
            }

            AiVector forward = VisionMath.Forward(f.Facing with { Pitch = 0f });
            forward = new AiVector(forward.X, forward.Y, 0f).Normalised();
            AiVector back = f.Origin - forward * BehindStand;
            AiVector goal = back;
            bool behind = Behind(D._self.Origin, in f, Tolerance);
            if (D._targetDist < CircleRange && !behind)
            {
                // Round to the back on the side stock's cross product picks (tf_bot_spy_attack.cpp:190-221), not closing under 100 u.
                AiVector to = new AiVector(f.Origin.X - D._self.Origin.X, f.Origin.Y - D._self.Origin.Y, 0f);
                float cross = to.X * forward.Y - to.Y * forward.X;
                var side = new AiVector(-forward.Y, forward.X, 0f) * (cross > 0f ? 1f : -1f);
                goal = f.Origin + side * 110f - forward * 40f;
            }

            if (D._targetDist < KnifeRange && MathF.Abs(f.Origin.Z - D._self.Origin.Z) < OtherLevel)
            {
                AiVector dir = new AiVector(goal.X - D._self.Origin.X, goal.Y - D._self.Origin.Y, 0f);
                D._move = dir.Length < 8f ? MoveIntent.Hold
                    : new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, dir.Normalised(), false, 0);
            }
            else
            {
                D.GoTo(goal, MoveSpeed.Run);
            }

            D._verdict = behind ? "behind the victim" : "round to the back";
            return true;
        }

        public override void OneShots()
        {
            _decides++;
            _deadNow = false;
            WatchStall();
            if (Disguised)
                _disguisedDecides++;
            if (D._memory.OwnKills.Last != _killsSeen)
            {
                _killsSeen = D._memory.OwnKills.Last;
                if (_stabbedAt.Value > 0 && (_killsSeen - _stabbedAt) * D._tick < 0.5f)
                    _backstabs++;
            }

            if (D._action.Kind == ActionKind.Fire)
                return;
            if (Cloaked)
                _cloakedDecides++;
            // A carrier can neither disguise nor cloak (TFSpy.cs:297, :912): asking would only cost the decide.
            if (Sap() || (!D.Carrying && (Watch() || Dress())))
                return;
        }

        /// <summary>
        /// The sapper (research §4.3, stock tf_bot_spy_sap.cpp): an enemy building in sight inside <see cref="SapReach"/>,
        /// looked at, gets the <c>sap</c> verb; the building already sapped is the game's to refuse.
        /// </summary>
        private bool Sap()
        {
            if (D._now < _sapAt || Cloaked)
                return false;
            float best = float.MaxValue;
            AiVector at = default;
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind != ThingKind.Building || t.Team == D._self.Team || !TeamRule.IsGameTeam(t.Team))
                    continue;
                float dist = D._self.Origin.DistanceTo(t.Origin);
                if (dist < best)
                    (best, at) = (dist, t.Origin);
            }

            if (best > SapReach)
                return false;
            D._look = new LookIntent(LookKind.Point, at + new AiVector(0f, 0f, 20f), ActorId.None, AimPoint.Body, 0f, 0);
            if (!Send(Row(ref _sapRow, TFSpyClass.Sap)))
                return false;
            _saps++;
            _sapAt = D._now + (int)(1f / D._tick);
            return true;
        }

        private bool EnemyWithin(float range, bool facing)
        {
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.Friends(D._self.Team, f.Team) || D._self.Origin.DistanceTo(f.Origin) > range)
                    continue;
                if (!facing || FacesAt(in f, D._self.Origin))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The watch (research §4.3, A1/A3): pressed to cross toward a fight unseen (meter at least <see cref="CloakFrom"/>, an
        /// enemy within <see cref="CloakNear"/>, the victim not yet near), pressed again to drop it short of the victim, when
        /// the meter runs low, or with nobody near for a second. Never sooner than a decloak's fade after the last press.
        /// </summary>
        private bool Watch()
        {
            if (FeignWatch)
                return Ringer();
            if (D._now < _cloakAt)
                return false;
            bool cloaked = Cloaked;
            bool enemyNear = EnemyWithin(DecloakClear, facing: false);
            if (enemyNear)
                _clearSince = D._now;
            bool press;
            if (cloaked)
            {
                bool clear = !enemyNear && (D._now - _clearSince) * D._tick >= 1f;
                press = CloakLeft <= DecloakAt || clear || (D._targetSighted && D._targetDist < DecloakShort);
                if (press)
                    _decloaks++;
            }
            else
            {
                press = CloakLeft >= CloakFrom && EnemyWithin(CloakNear, facing: false) && !(D._targetSighted && D._targetDist < DecloakShort)
                        && !Disguising;
                if (press)
                    _cloaks++;
            }

            if (!press || !Send(Row(ref _cloakRow, TFSpyClass.Cloak)))
                return false;
            _cloakAt = D._now + (int)(CloakEverySeconds / D._tick);
            return true;
        }

        /// <summary>
        /// The Dead Ringer (research §2 A2, §4.3): never a cloak to cross. Armed with a full meter while an enemy is within
        /// <see cref="CloakNear"/>; a feign (armed, then cloaked and no longer armed) runs home for <see cref="FleeSeconds"/>
        /// and drops the cloak only with nobody near for a second after it.
        /// </summary>
        private bool Ringer()
        {
            bool armed = FeignArmed, cloaked = Cloaked;
            if (_wasArmed && !armed && cloaked)
            {
                _feigns++;
                _fleeUntil = D._now + (int)(FleeSeconds / D._tick);
            }

            _wasArmed = armed;
            bool enemyNear = EnemyWithin(DecloakClear, facing: false);
            if (enemyNear)
                _clearSince = D._now;
            if (cloaked && D._now < _fleeUntil)
                Flee();
            if (D._now < _cloakAt)
                return false;
            bool press = cloaked
                ? D._now >= _fleeUntil && !enemyNear && (D._now - _clearSince) * D._tick >= 1f
                : !armed && CloakLeft >= ArmFrom && EnemyWithin(CloakNear, facing: false);
            if (!press || !Send(Row(ref _cloakRow, TFSpyClass.Cloak)))
                return false;
            if (!cloaked)
                _arms++;
            _cloakAt = D._now + (int)(CloakEverySeconds / D._tick);
            return true;
        }

        /// <summary>Runs for its own team's spawn: the way out of the fight it feigned in.</summary>
        private void Flee()
        {
            foreach (TeamSpawn s in D._map.TeamSpawns)
            {
                if (s.Team == D._self.Team)
                {
                    D.GoTo(s.Position, MoveSpeed.Run);
                    D._verdict = "feigned, running";
                    return;
                }
            }
        }

        /// <summary>
        /// A disguise (research §4.3): put on when it wears none and none is going on, with no enemy facing it near (the
        /// smoke gives it away), or cloaked. The class: one the enemy fields (seen or on its scoreboard rows), by how fast it
        /// walks, never one whose only known player stands in sight now (the borrowed name would be his).
        /// </summary>
        private bool Dress()
        {
            if (Disguised || Disguising || D._now < _disguiseAt)
                return false;
            if (!Cloaked && EnemyWithin(1200f, facing: true))
                return false;
            TFClass dress = Choose();
            if (dress == TFClass.Undefined || !Send(Row(ref _dressRows[(int)dress], TFSpyClass.DisguiseRow(dress))))
                return false;
            _disguises++;
            _disguiseAt = D._now + (int)(DisguiseEverySeconds / D._tick);
            return true;
        }

        private readonly float[] _fielded = new float[(int)TFClass.CountAll];
        private readonly int[] _inSight = new int[(int)TFClass.CountAll], _known = new int[(int)TFClass.CountAll];

        /// <summary>The disguise's class: the fielded one walking fastest, a class not seen at a third of its weight, ties to the bot's slot.</summary>
        internal TFClass Choose()
        {
            Array.Clear(_known);
            Array.Clear(_inSight);
            Blackboard board = D._memory.Board;
            for (int s = 0; s < board.Slots; s++)
            {
                KnownActor a = board.Actor(s);
                if (!a.Known || D._memory.IsFriend(a) || a.ClassBelieved is <= 0 or >= (int)TFClass.CountAll)
                    continue;
                _known[a.ClassBelieved]++;
                if (D._view.TryActor(a.Id, out _))
                    _inSight[a.ClassBelieved]++;
            }

            TFClass best = TFClass.Undefined;
            float bestWeight = 0f;
            for (int i = 0; i < TFSpyClass.DressCount; i++)
            {
                // A rotation by slot, so a team's Spies do not all wear one class (the dresses are the classes 1 to 9).
                var c = (TFClass)(1 + ((i + D._bot.Slot) % TFSpyClass.DressCount));
                if (c == TFClass.Spy || (_known[(int)c] > 0 && _known[(int)c] == _inSight[(int)c]))
                    continue;
                float w = (_known[(int)c] > 0 ? 1f : 0.3f) * DressSpeed(c);
                if (w > bestWeight)
                    (best, bestWeight) = (c, w);
            }

            return best;
        }

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("spy.disguises", _disguises));
            into.Add(new("spy.cloaks", _cloaks));
            into.Add(new("spy.decloaks", _decloaks));
            into.Add(new("spy.swings", _swings));
            into.Add(new("spy.stab_swings", _stabSwings));
            into.Add(new("spy.backstabs", _backstabs));
            into.Add(new("spy.saps", _saps));
            into.Add(new("spy.gun_shots", _gunShots));
            into.Add(new("spy.decides", _decides));
            into.Add(new("spy.disguised_decides", _disguisedDecides));
            into.Add(new("spy.stalls", _stalls));
            into.Add(new("spy.let_gos", _letGos));
            into.Add(new("spy.cloaked_decides", _cloakedDecides));
            into.Add(new("spy.lives", _lives));
            into.Add(new("spy.arms", _arms));
            into.Add(new("spy.feigns", _feigns));
        }

        public override void Died()
        {
            // Called each decide the bot lies dead: a life is counted once, at its first.
            if (!_deadNow)
                _lives++;
            _deadNow = true;
            _disguiseAt = _cloakAt = _sapAt = _fleeUntil = _shotAt = _stallSince = default;
            _repath = false;
            _stabbedAt = default;
            _wasArmed = false;
        }
    }
}
