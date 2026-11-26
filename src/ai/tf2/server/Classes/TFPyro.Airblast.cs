//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Projectiles;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Pyro's compression blast (research pyro.md §4.3-4.4; stock <c>CTFBot::ShouldFireCompressionBlast</c>,
    /// tf_bot.cpp:4073-4200), one <see cref="TFPyroClass.Airblast"/> one-shot at a time, by the bot's skill: a reflect of an
    /// enemy projectile inside the blast's reach in front of it (a rocket only when it heads at the bot), a burning teammate
    /// put out, and a shove of the enemy it fights within stock's 250 u (an Über always, a jumper half the time, else
    /// "pushy" half of each 3 s). Never with the flamethrower holstered, its reserve under the blast's cost, or a
    /// flamethrower that has no blast (the Phlogistinator).
    /// </summary>
    private sealed partial class PyroPlay
    {
        /// <summary>A blast costs this much of the flamethrower's reserve (<c>tf_flamethrower_burstammo</c>), times the item's <see cref="AirblastCostScale"/>.</summary>
        public const float AirblastCost = 20f;

        /// <summary>The blast refires after this long (TFShellBehaviours' flamethrower, stock's 0.75 s).</summary>
        public const float AirblastRefireSeconds = 0.75f;

        /// <summary>The enemy the Pyro fights is shoved within this (stock <c>tf_bot_pyro_shove_away_range</c>, tf_bot.cpp:49).</summary>
        public const float ShoveRange = 250f;

        /// <summary>A rocket is reflected only heading at the bot: its flat heading against the bot's flat aim under minus this (stock <c>tf_bot_pyro_deflect_tolerance</c>, tf_bot.cpp:45).</summary>
        public const float DeflectTolerance = 0.5f;

        /// <summary>A burning teammate this near is put out, unless the target is within <see cref="ExtinguishUnlessNear"/>.</summary>
        public const float ExtinguishReach = 200f, ExtinguishUnlessNear = 150f;

        /// <summary>An incoming enemy projectile the bot will reflect turns its view to it within this.</summary>
        public const float ReflectTurnReach = 600f;

        /// <summary>
        /// How far ahead of the decide the blast is judged: the press lands on the next command (one tick) and the decide's
        /// intent is applied after the view moves toward it (about one more): two ticks of flight.
        /// </summary>
        public const float PressLeadSeconds = 0.03f;

        /// <summary>A projectile flying at the bot is one whose line passes the eye within this (the blast's reach and a body's half width).</summary>
        public const float ChaseMiss = 128f + 24f;

        /// <summary>The view turns to an incoming projectile where it will be this far out; the counters call a reflect early when the turn began further out.</summary>
        public const float TurnFarUnits = 200f;

        /// <summary>The item attributes the blast reads: its cost's scale, and the Phlogistinator's none.</summary>
        public const string AirblastCostScale = "mult_airblast_cost", NoAirblast = "airblast_disabled";

        /// <summary>The Guard shoves an enemy that is not an Über only with this much left: the rest is for reflects and the flame.</summary>
        public const float GuardShoveFloor = 100f;

        /// <summary>The Guard shoves an enemy that is not an Über only this near its charge.</summary>
        public const float GuardShoveNearCharge = 300f;

        /// <summary>A blast pressed at a projectile counts as a reflect once the view shows the projectile the bot's own within this.</summary>
        public const float ReflectConfirmSeconds = 1f;

        private int _airblastRow = -2;
        private Tick _blastReadyAt;
        private bool _reflectingNow, _uberShovable;
        private long _reflects, _reflectPresses, _shoves, _uberShoves, _extinguishes, _predictedPresses, _reflectsPredicted, _reflectsTurnedFar;
        private readonly bool[] _pressedPredicted = new bool[4], _pressedFar = new bool[4];
        private readonly int[] _pressedIds = new int[4];
        private readonly Tick[] _pressedAt = new Tick[4];
        private int _pressedNext;

        /// <summary>The blast's ability row, found by name once the decider's map is there.</summary>
        private int AirblastRow => _airblastRow == -2 ? _airblastRow = D.AbilityRow(TFPyroClass.Airblast) : _airblastRow;

        /// <summary>
        /// Whether the bot reflects (and shoves) this second: its reflect chance (the Pyro's own curve in tf2_skills.json,
        /// stock's ladder by difficulty: Easy never, Normal half, Hard 0.9, Expert always, tf_bot.cpp:4081-4105), drawn for
        /// each second as stock's <c>TransientlyConsistentRandomValue( 1.0f )</c> is.
        /// </summary>
        private bool SkilledNow => Consistent(1f, 0x52464C43) < ReflectChance;

        public override void OneShots()
        {
            _reflectingNow = _uberShovable = false;
            ConfirmReflects();
            if (IsPhlog && !D.Busy && !D.HeldStill && Taunt())
                return;
            if (D.Busy || D.HeldStill || !Flamethrower(out TFKitItem flame) || flame.Has(NoAirblast) || AirblastRow < 0)
                return;
            bool skilled = SkilledNow;
            AiVector eye = D.OwnEye;
            AiVector forward = Forward(D._self.Eye);
            AiVector centre = eye + forward * TFAirBlastDefs.DeflectionRadius;
            // Able to blast now or at the refire: the flamethrower out with the blast's cost in it (the view is turned and
            // the back-off refused only for a blast it can make).
            bool armed = HoldingFlamethrower && D._self.Reserve >= AirblastCost * flame.Attribute(AirblastCostScale, 1f);
            bool ready = armed && D._now >= _blastReadyAt;

            if (skilled && armed && Reflectable(eye, forward, centre, out AiVector incoming, out bool inReach, out int id, out bool predicted, out float along,
                                                out float arrives))
            {
                // The dodge is given up only for a projectile the blast will be ready for when it arrives (review 2, T6).
                _reflectingNow = D._now + (int)(arrives / D._tick) >= _blastReadyAt;
                if (inReach && ready)
                {
                    Blast(incoming);
                    Pressed(id, predicted);
                    return;
                }

                if (!inReach && !(D._targetSighted && D._targetDist < ExtinguishUnlessNear))
                {
                    D._look = new LookIntent(LookKind.Point, incoming, ActorId.None, AimPoint.Body, 0f, 0);
                    Turned(id, along);
                }
            }

            ActorFacts victim = default;
            bool inCone = false;
            ShoveWhy why = skilled && armed ? ChooseShove(eye, forward, out victim, out inCone) : ShoveWhy.None;
            // Whatever the reason, a shove of an invulnerable enemy is the Über's answer: no back-off from him (review 2, T5).
            _uberShovable = why != ShoveWhy.None && (victim.Conditions & ActorConditions.Invulnerable) != 0;
            if (!ready)
                return;
            if (BurningMate(centre, out ActorId mate, out AiVector mateAt, out bool mateInReach))
            {
                if (mateInReach)
                {
                    Blast(mateAt);
                    _extinguishes++;
                }
                else
                {
                    D._look = new LookIntent(LookKind.Actor, mateAt, mate, AimPoint.Body, 0f, 0);
                }

                return;
            }

            if (why == ShoveWhy.None)
                return;
            // A shove of anyone but the target turns the view to him; the press waits for him to be in the push's cone.
            if (why != ShoveWhy.Pushy)
                D._look = new LookIntent(LookKind.Actor, victim.Origin + new AiVector(0f, 0f, 40f), victim.Id, AimPoint.Body, 0f, 0);
            if (inCone)
            {
                Blast(D._look.Point);
                CountShove(why);
            }
        }

        /// <summary>The reflect chance by the bot's skill (<see cref="SkilledNow"/>).</summary>
        private float ReflectChance => D._p.ReflectChance;

        /// <summary>
        /// A blast was pressed at projectile <paramref name="id"/> (<paramref name="predicted"/>: on where it would be at the press,
        /// not where it was): watched for the game's deflect (<see cref="ConfirmReflects"/>).
        /// </summary>
        private void Pressed(int id, bool predicted)
        {
            _reflectPresses++;
            _predictedPresses += predicted ? 1 : 0;
            _pressedIds[_pressedNext] = id;
            _pressedAt[_pressedNext] = D._now;
            _pressedPredicted[_pressedNext] = predicted;
            _pressedFar[_pressedNext] = TurnedFar(id);
            _pressedNext = (_pressedNext + 1) % _pressedIds.Length;
        }

        private readonly int[] _turnIds = new int[8];
        private readonly float[] _turnAlong = new float[8];
        private int _turnNext;

        /// <summary>The view first turned to projectile <paramref name="id"/> with it <paramref name="along"/> units out (kept for the counters).</summary>
        private void Turned(int id, float along)
        {
            for (int i = 0; i < _turnIds.Length; i++)
            {
                if (_turnIds[i] == id)
                    return;
            }

            (_turnIds[_turnNext], _turnAlong[_turnNext]) = (id, along);
            _turnNext = (_turnNext + 1) % _turnIds.Length;
        }

        /// <summary>Whether the view first turned to projectile <paramref name="id"/> while it was over <see cref="TurnFarUnits"/> out.</summary>
        private bool TurnedFar(int id)
        {
            for (int i = 0; i < _turnIds.Length; i++)
            {
                if (_turnIds[i] == id)
                    return _turnAlong[i] > TurnFarUnits;
            }

            return false;
        }

        /// <summary>
        /// A reflect counts when it happened: a projectile the bot pressed a blast at shows in its view as its own (the game's
        /// deflect re-points the owner, and the view carries only the viewer's own owner slot) within
        /// <see cref="ReflectConfirmSeconds"/>.
        /// </summary>
        private void ConfirmReflects()
        {
            for (int i = 0; i < _pressedIds.Length; i++)
            {
                if (_pressedAt[i].Value == 0)
                    continue;
                if ((D._now - _pressedAt[i]) * D._tick > ReflectConfirmSeconds)
                {
                    _pressedAt[i] = default;
                    continue;
                }

                foreach (ref readonly ThingFacts t in D._view.Things)
                {
                    if (t.Id == _pressedIds[i] && t.OwnerSlot == D._bot.Slot)
                    {
                        _reflects++;
                        _reflectsPredicted += _pressedPredicted[i] ? 1 : 0;
                        _reflectsTurnedFar += _pressedFar[i] ? 1 : 0;
                        _pressedAt[i] = default;
                        break;
                    }
                }
            }
        }

        /// <summary>Presses the blast with the view where it is (the look stays on <paramref name="at"/>), and waits the refire.</summary>
        private void Blast(AiVector at)
        {
            if (D._look.Kind is not (LookKind.Actor or LookKind.Point))
                D._look = new LookIntent(LookKind.Point, at, ActorId.None, AimPoint.Body, 0f, 0);
            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(AirblastRow, 0), ++D._oneShot);
            _blastReadyAt = D._now + (int)(AirblastRefireSeconds / D._tick);
        }

        /// <summary>
        /// The enemy projectile to reflect: one the game's blast sends back (<see cref="Reflects"/>), not the bot's own nor its
        /// team's, seen for its reaction time, heading at the bot when it is a rocket. <paramref name="inReach"/>: it is, or will be
        /// at the press (<see cref="PressLeadSeconds"/> on, by its own velocity), inside the blast's sphere in front of the view
        /// (TFFlameEffects.AirBlast: radius 128 centred 128 out); <paramref name="predicted"/> when only the prediction puts it
        /// there, <paramref name="at"/> where it will be. Else the nearest such projectile flying at the bot (its line passes within
        /// <see cref="ChaseMiss"/> of the eye) within <see cref="ReflectTurnReach"/>, <paramref name="at"/> where it will be when
        /// <see cref="TurnFarUnits"/> out, for the view to turn to, and <paramref name="along"/> how far out it is now.
        /// </summary>
        private bool Reflectable(AiVector eye, AiVector forward, AiVector centre, out AiVector at, out bool inReach, out int id, out bool predicted,
                                 out float along, out float arrives)
        {
            (at, inReach, id, predicted, along, arrives) = (default, false, 0, false, 0f, 0f);
            float nearest = float.MaxValue;
            var flatForward = new AiVector(forward.X, forward.Y, 0f).Normalised();
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind != ThingKind.Projectile || !Reflects((TFThingType)t.Type) || t.OwnerSlot == D._bot.Slot
                    || (TeamRule.IsGameTeam(t.Team) && t.Team == D._self.Team))
                    continue;
                // The Backburner spends 50 of its 200 on a blast: only a rocket is worth one, or a turn toward it.
                if (IsBackburner && (TFThingType)t.Type != TFThingType.Rocket)
                    continue;
                AiVector soon = t.Origin + t.Velocity * PressLeadSeconds;
                bool nowIn = t.Origin.DistanceTo(centre) <= TFAirBlastDefs.DeflectionRadius;
                bool soonIn = soon.DistanceTo(centre) <= TFAirBlastDefs.DeflectionRadius;
                var flatVelocity = new AiVector(t.Velocity.X, t.Velocity.Y, 0f);
                if (nowIn || soonIn)
                {
                    // Stock's test for a rocket (tf_bot.cpp:4180-4189): its flat heading against the flat aim.
                    if ((TFThingType)t.Type == TFThingType.Rocket && flatVelocity.LengthSquared >= 1f
                        && flatForward.Dot(flatVelocity.Normalised()) > -DeflectTolerance)
                        continue;
                    if (!Reacted(t.Id))
                        continue;
                    (at, inReach, id, predicted) = (soonIn ? soon : t.Origin, true, t.Id, !nowIn);
                    return true;
                }

                float speed = t.Velocity.Length;
                if (speed < 1f)
                    continue;
                AiVector heading = t.Velocity * (1f / speed);
                AiVector toMe = eye - t.Origin;
                float ahead = toMe.Dot(heading);
                // Flying at the bot: closing, and its line passes the eye within the blast's reach (a shot aimed elsewhere is not chased).
                if (ahead <= 0f || (toMe - heading * ahead).Length > ChaseMiss || ahead >= ReflectTurnReach || ahead >= nearest || !Reacted(t.Id))
                    continue;
                float toTurn = Math.Max(0f, ahead - TurnFarUnits) / speed;
                (nearest, at, id, along, arrives) = (ahead, t.Origin + t.Velocity * toTurn, t.Id, ahead, Math.Max(0f, ahead - 2f * TFAirBlastDefs.DeflectionRadius) / speed);
            }

            return nearest < float.MaxValue;
        }

        /// <summary>The projectiles the game's blast sends back (TFFlameEffects.AirBlast's arms): rockets, pipes, stickies, flares, arrows.</summary>
        private static bool Reflects(TFThingType type) => type is TFThingType.Rocket or TFThingType.Pipe or TFThingType.Sticky or TFThingType.Flare or TFThingType.Arrow;

        private readonly int[] _seenIds = new int[8];
        private readonly Tick[] _seenAt = new Tick[8];
        private int _seenNext;

        /// <summary>Whether projectile <paramref name="id"/> has been in view for the bot's reaction time; begins its watch when new.</summary>
        private bool Reacted(int id)
        {
            for (int i = 0; i < _seenIds.Length; i++)
            {
                if (_seenIds[i] == id && _seenAt[i].Value != 0)
                    return (D._now - _seenAt[i]) * D._tick >= D._p.ReactionMs * 0.001f;
            }

            _seenIds[_seenNext] = id;
            _seenAt[_seenNext] = D._now;
            _seenNext = (_seenNext + 1) % _seenIds.Length;
            return D._p.ReactionMs <= 0f;
        }

        /// <summary>A burning teammate in sight within <see cref="ExtinguishReach"/>, and whether the blast in front reaches him now; none while the target is within <see cref="ExtinguishUnlessNear"/>.</summary>
        private bool BurningMate(AiVector centre, out ActorId mate, out AiVector at, out bool inReach)
        {
            (mate, at, inReach) = (ActorId.None, default, false);
            if (D._targetSighted && D._targetDist < ExtinguishUnlessNear)
                return false;
            float best = ExtinguishReach;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if ((f.Conditions & ActorConditions.Burning) == 0 || !TeamRule.Friends(D._self.Team, f.Team))
                    continue;
                float d = D._self.Origin.DistanceTo(f.Origin);
                if (d > best)
                    continue;
                AiVector middle = f.Origin + new AiVector(0f, 0f, 40f);
                (best, mate, at) = (d, f.Id, middle);
                inReach = middle.DistanceTo(centre) <= TFAirBlastDefs.DeflectionRadius + Knowledge.TFKnowledge.BodyHalfWidth;
            }

            return mate != ActorId.None;
        }

        /// <summary>Where a view of <paramref name="a"/> points (Source's angles: pitch down positive).</summary>
        private static AiVector Forward(AiAngles a)
        {
            float p = a.Pitch * (MathF.PI / 180f), y = a.Yaw * (MathF.PI / 180f);
            return new AiVector(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
        }

        private void CountAirblasts(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("pyro.reflects", _reflects));
            into.Add(new("pyro.reflect_presses", _reflectPresses));
            into.Add(new("pyro.shoves", _shoves));
            into.Add(new("pyro.uber_shoves", _uberShoves));
            into.Add(new("pyro.extinguishes", _extinguishes));
            into.Add(new("pyro.reflect_presses_predicted", _predictedPresses));
            into.Add(new("pyro.reflects_predicted", _reflectsPredicted));
            into.Add(new("pyro.reflects_turned_far", _reflectsTurnedFar));
            CountShoves(into);
        }

        private void AirblastDied()
        {
            _blastReadyAt = default;
            _reflectingNow = _uberShovable = false;
            Array.Clear(_pressedAt);
        }
    }
}
