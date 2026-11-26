using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Knowledge;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Sniper's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Sniper lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFSniperClass
{
    /// <summary>Whether the composition fields the Sniper (C.4): false until the Sniper lane says its play is ready: until then the composition fields no Sniper (a pin still may).</summary>
    public const bool Ready = false;

    /// <summary>The rifle's scope as a one-shot: the second trigger pressed for two commands, so the rifle toggles once (it re-arms after 0.3 s).</summary>
    public const string Zoom = "sniper_zoom";

    /// <summary>
    /// The Sniper's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [
        new AbilityRow(Zoom, AbilityRun.Buttons, Buttons: (int)InputButtons.Attack2, HoldTicks: 2),
    ];

    /// <summary>The stock Sniper's archetype (rifle, SMG, Kukri), the pub one with Jarate, and the one with a Razorback against Spies.</summary>
    public const string Marksman = "Marksman", Jarate = "Jarate", Guarded = "Guarded";

    /// <summary>
    /// The Sniper's archetypes (C.3.1; the research's ship order A, B, D, its weights 40, 30, 10), drawn by the bot's seed and
    /// weight. Each names its rifle, its secondary and its melee: the stock Sniper Rifle 14, the SMG 16, Jarate 58, the
    /// Razorback 57, the Kukri 3 (each standing for its reskins).
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Sniper,
    [
        new TFArchetype(Marksman, 40f, [new(TFLoadoutSlot.Primary, [14]), new(TFLoadoutSlot.Secondary, [16]), new(TFLoadoutSlot.Melee, [3])]),
        new TFArchetype(Jarate, 30f, [new(TFLoadoutSlot.Primary, [14]), new(TFLoadoutSlot.Secondary, [58]), new(TFLoadoutSlot.Melee, [3])]),
        new TFArchetype(Guarded, 10f, [new(TFLoadoutSlot.Primary, [14]), new(TFLoadoutSlot.Secondary, [57]), new(TFLoadoutSlot.Melee, [3])]),
    ]);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Sniper, Ready, Abilities(), Archetypes());

    /// <summary>
    /// The least rifle charge (damage points before the ×3 headshot) a headshot needs to kill a body of
    /// <paramref name="maxHealth"/>, overhealed by half when a Medic is seen by it: a shot does max(charge, 50), a headshot
    /// three times that (TFSniperRifleBehaviour.DamageAt, TFDamageLayer's headshot crit), so a 125 needs none, a Soldier's 200
    /// needs 67, a Heavy's 300 needs 100 and an overhealed Heavy the whole 150.
    /// </summary>
    public static float ChargeToKill(int maxHealth, bool medicNear)
    {
        float health = maxHealth * (medicNear ? OverhealGuess : 1f);
        float need = MathF.Ceiling(health / 3f);
        return need <= TFSniperRifleBehaviour.DamageMin ? 0f : MathF.Min(need, TFSniperRifleBehaviour.DamageMax);
    }

    /// <summary>A body with a Medic beside it is guessed overhealed by this much (ours: stock's overheal is +50 %).</summary>
    public const float OverhealGuess = 1.5f;
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Sniper's play (the research's §4.3, lane A of the review's split; the aim stays on today's motion model): it goes
    /// to a spot that holds a sight line onto the objective in play (<see cref="TFSniperSpots"/>), scopes there and parks its
    /// aim on the line, charges until a headshot kills the class it sees (<see cref="TFSniperClass.ChargeToKill"/>), leaves
    /// after a kill, when nothing comes, or when hit from range, glances behind now and then, and keeps the scope off anyone
    /// near: the SMG within 750 u, backing off within 400 u, the Kukri within 200 u (stock's tf_bot.cpp:3394-3398,
    /// tf_bot_sniper_attack.cpp:19, tf_bot_sniper_lurk.cpp:117-121). The Jarate archetype throws its jar into a group or
    /// onto a burning teammate; the Guarded one glances behind a third as often.
    /// </summary>
    private sealed class SniperPlay(TFDecider d) : ClassPlay(d, TFClass.Sniper)
    {
        /// <summary>Stock's ranges: the SMG within, the retreat within, the melee within and its give-up past.</summary>
        public const float SmgRange = 750f, FleeRange = 400f, MeleeRange = 200f, MeleeGiveUp = 250f;

        /// <summary>How near a spot is reached (stock's 25 u, tf_bot_sniper_lurk.cpp:161, widened by a cell's slack).</summary>
        public const float SpotArrive = 48f;

        /// <summary>No enemy seen this long at a spot: another one (stock's patience, tf_bot_sniper_lurk.cpp:29,39, ×U(0.9,1.1)).</summary>
        public const float BoredSeconds = 10f;

        /// <summary>After a kill the bot leaves within U(2,8) s (ours: the freezecam shows the killer's spot).</summary>
        public const float AfterKillLeast = 2f, AfterKillMost = 8f;

        /// <summary>The headshot's gate is 0.2 s of zoom (TFSniperRifleBehaviour.NoCritAfterZoomTime): waited, plus a decide's slack.</summary>
        public const float ZoomSettleSeconds = 0.3f;

        /// <summary>After a scoped shot the rifle unzooms in 0.5 s and rezooms 0.9 s later: no zoom press over that, or it cancels the rezoom.</summary>
        public const float AfterShotSeconds = 1.6f;

        /// <summary>The least time between two zoom presses: the rifle re-arms after 0.3 s, and the HUD must show the change first.</summary>
        public const float ZoomEverySeconds = 0.5f;

        /// <summary>A glance behind every U(6,14) s at a spot (ours), held this long; the Guarded archetype a third as often.</summary>
        public const float BehindLeast = 6f, BehindMost = 14f, BehindHold = 0.7f, GuardedBehindScale = 1f / 0.3f;

        /// <summary>The parked aim moves to another place on the watched line every U(3,8) s (ours): never jittering, never frozen.</summary>
        public const float ParkLeast = 3f, ParkMost = 8f;

        /// <summary>The class branch's weight: over the points' and the team's (0.5), under a fight in sight and a heal.</summary>
        public const float PlaceWeight = 0.55f;

        /// <summary>A Medic within this of a target is guessed to be overhealing it.</summary>
        public const float MedicNearUnits = 450f;

        /// <summary>An enemy facing the bot this squarely (cos) is aiming at it (stock's 0.8, tf_bot_sniper_attack.cpp:161-226).</summary>
        public const float AimedAtCos = 0.8f;

        /// <summary>Target bonuses over the general score (the research's tiers, ours): a Sniper, a Medic (on the general 0.35), a Demoman, a Heavy; a Scout far off is worth less.</summary>
        public const float SniperBonus = 0.5f, MedicExtra = 0.05f, DemoBonus = 0.25f, HeavyBonus = 0.25f, FarScoutMalus = -0.3f;

        /// <summary>While scoped, the target is held this long over a rival (stock's tf_bot_sniper_choose_target_interval 3 s, tf_bot_vision.cpp:16).</summary>
        public const float HoldTargetSeconds = 3f, HoldTargetBonus = 2f;

        /// <summary>Jarate (archetype B): a group is at least two enemies within the splash of a point 300-1,000 u off; a burning teammate within 600 u.</summary>
        public const float JarSplash = 200f, JarNear = 300f, JarFar = 1000f, JarMateReach = 600f, JarSpeed = 1000f, JarUpSpeed = 200f;

        /// <summary>A throw: the jar's deploy is waited at most this long, aimed this long, and the next not before this.</summary>
        public const float JarDeployMost = 1.5f, JarAimSeconds = 0.25f, JarEverySeconds = 3f;

        /// <summary>Stock's aim error (tf_bot_behavior.cpp:36, 0.01 rad), the Sniper's floor until human sessions show a tighter scoped hand.</summary>
        public const float StockErrorFloorDeg = 0.573f;

        private readonly AiRandom _rng = new(d._seed ^ 0x534E_4950UL);
        private int _zoomRow = -2;
        private Tick _readAt = new(-1);

        // The spot.
        private ImmutableArray<TFSniperSpot> _spots = [];
        private AiVector _spotsFor;
        private bool _spotsAttack, _spotsMade;
        private int _spotPick = -1;
        private bool _atSpot;
        private Tick _lastEnemyAt, _leaveAt;
        private bool _spotHadTargets;
        private AiVector _park;
        private Tick _parkUntil;
        private Tick _behindAt, _behindUntil;
        private AiVector _behindPoint;
        private bool _boredDrawn;
        private float _bored;

        // The rifle.
        private bool _zoomed;
        private Tick _zoomedAt, _zoomPressAt = new(-100000), _shotAt = new(-100000);
        private Tick _seenKill, _seenAttack;
        private KnownActor? _held;
        private Tick _heldSince;

        // The jar.
        private int _jarPhase;
        private Tick _jarPhaseAt, _jarNextAt;
        private AiVector _jarAt;

        // Counters.
        private long _zooms, _scopedShots, _chargedShots, _relocations, _spotDecides, _aliveDecides, _jarThrows, _behind, _smgPicks, _meleePicks;

        private float Seconds(Tick from) => (D._now - from) * D._tick;

        private Tick After(float seconds) => D._now + (int)(seconds / D._tick);

        private int ZoomRow => _zoomRow == -2 ? _zoomRow = D.AbilityRow(TFSniperClass.Zoom) : _zoomRow;

        private int RifleKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Primary, out Senses.TFKitItem i) && TFSniperKit.IsRifle(i.ItemClass) ? i.WeaponKind : -1;

        private int SmgKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Secondary, out Senses.TFKitItem i) && TFSniperKit.IsSmg(i.ItemClass) ? i.WeaponKind : -1;

        private int JarKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Secondary, out Senses.TFKitItem i) && i.ItemClass == TFSniperKit.JarClass ? i.WeaponKind : -1;

        private int MeleeKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Melee, out Senses.TFKitItem i) ? i.WeaponKind : -1;

        private bool HoldingRifle => RifleKind >= 0 && D._self.ActiveWeapon == RifleKind;

        private float Charge => D.Kit?.Meter(TFSniperMeters.Charge, 0f) ?? 0f;

        private float JarMeter => D.Kit?.Meter(TFSniperMeters.Jar, 0f) ?? 0f;

        /// <summary>Whether the bot's own scope is up, as its HUD shows it.</summary>
        public bool Zoomed => _zoomed;

        /// <summary>Whether the bot stands at its spot holding the line.</summary>
        public bool AtSpot => _atSpot;

        /// <summary>The spot the bot goes to or holds, if it has one.</summary>
        public TFSniperSpot? Spot => _spotPick >= 0 && _spotPick < _spots.Length ? _spots[_spotPick] : null;

        public override string Name => "Sniper";

        // ------------------------------------------------------------------ the decide's reads

        /// <summary>Once a decide, before the tree's branch is weighed: the HUD's scope, the kill feed's last kill, a hit, the counters.</summary>
        private void Read()
        {
            if (D._now == _readAt)
                return;
            _readAt = D._now;
            _aliveDecides++;
            if (_atSpot)
                _spotDecides++;
            bool zoomed = (D.Kit?.Meter(TFSniperMeters.Zoomed, 0f) ?? 0f) >= 0.5f;
            if (zoomed && !_zoomed)
                _zoomedAt = D._now;
            _zoomed = zoomed;
            if (D._targetSighted)
            {
                _lastEnemyAt = D._now;
                if (_atSpot)
                    _spotHadTargets = true;
            }

            // A kill of the bot's own (the kill feed): at a spot, leave it after a short hold.
            if (D._memory.OwnKills.Last != _seenKill)
            {
                _seenKill = D._memory.OwnKills.Last;
                if (_atSpot && _leaveAt.Value == 0)
                    _leaveAt = After(AfterKillLeast + _rng.NextFloat() * (AfterKillMost - AfterKillLeast));
            }

            // Hit by someone it does not see (a Sniper's shot, a far rocket), or by a Sniper it sees: off this line at once.
            Tick attack = D._memory.Board.LastAttackTick;
            if (attack != _seenAttack)
            {
                _seenAttack = attack;
                bool unseen = !D._targetSighted || D._target is { ClassBelieved: (int)TFClass.Sniper };
                if (_atSpot && unseen)
                    Relocate(near: false);
            }
        }

        // ------------------------------------------------------------------ the place

        /// <summary>The objectives' say: a Sniper with a scoped rifle holds lines rather than runs the objectives; with none (a bow, no kit read) the general play's.</summary>
        public override float Weigh(string branch) => RifleKind < 0 ? 1f : branch switch
        {
            // A remembered enemy is not chased off the line: only one in sight, or just lost, is fought.
            Fight when !D._targetSighted && !D.SightJustLost => 0.4f,
            Fetch or ChaseCarrier or Guard or Escort or Defend => 0.55f,
            PointsBranch or TeamBranch or Course or Search => 0.8f,
            _ => 1f,
        };

        public override float PlaceUtility()
        {
            Read();
            return RifleKind < 0 || SpotNow() is null ? 0f : PlaceWeight;
        }

        /// <summary>The objective the Sniper watches: the point in play, the middle between the flags, else the enemy's spawn.</summary>
        private bool Watched(out AiVector watch, out bool attack)
        {
            attack = D.Role == TFTeamRole.Attack;
            if (D._onPoints && D._pointsGoal is { } goal)
            {
                (watch, attack) = (goal.Position, !D._pointsGoalOwn);
                return true;
            }

            // CTF has no stock goal (tf_bot.cpp:2321-2325). The flags lie deep in the bases, past the reach of a line, so the
            // line is the middle the carriers must cross (2fort's bridge), held from the team's own side in either role.
            if (D._enemyFlag is { } theirs && D._ownFlag is { } ours)
            {
                (watch, attack) = ((theirs.Position + ours.Position) * 0.5f, true);
                return true;
            }

            foreach (TeamSpawn s in D._map.TeamSpawns)
            {
                if (TeamRule.IsGameTeam(s.Team) && s.Team != D._self.Team)
                {
                    watch = s.Position;
                    return true;
                }
            }

            watch = default;
            return false;
        }

        private AiVector OwnSpawn()
        {
            AiVector sum = default;
            int n = 0;
            foreach (TeamSpawn s in D._map.TeamSpawns)
            {
                if (s.Team == D._self.Team)
                    (sum, n) = (sum + s.Position, n + 1);
            }

            return n > 0 ? sum * (1f / n) : D._self.Origin;
        }

        /// <summary>The spot to hold now: the spots onto the watched objective, worked out again when it moves; one drawn by the bot's slot.</summary>
        private TFSniperSpot? SpotNow()
        {
            if (!Watched(out AiVector watch, out bool attack))
                return null;
            if (!_spotsMade || attack != _spotsAttack || watch.DistanceTo(_spotsFor) > 256f)
            {
                int other = D._self.Team == (int)TFTeam.Red ? (int)TFTeam.Blue : (int)TFTeam.Red;
                _spots = TFSniperSpots.Find(D._graph, D._memory.World.Sight, D._memory.World.Chokes?.Map, watch, OwnSpawn(), attack,
                                            at => D.InEnemyRoom(at, other), D._self.Origin);
                (_spotsFor, _spotsAttack, _spotsMade) = (watch, attack, true);
                _spotPick = _spots.IsEmpty ? -1 : (D._bot.Slot + _rng.NextInt(2)) % _spots.Length;
                _atSpot = false;
                _leaveAt = default;
            }

            return Spot;
        }

        /// <summary>Another spot: one within 500 u of this one when this one had targets (stock's tf_bot_sniper_lurk.cpp:321-347), else any other.</summary>
        private void Relocate(bool near)
        {
            _atSpot = false;
            _leaveAt = default;
            if (_spots.Length < 2 || Spot is not { } was)
                return;
            int pick = _spotPick;
            for (int tries = 0; tries < 8 && pick == _spotPick; tries++)
            {
                int i = _rng.NextInt(_spots.Length);
                if (i != _spotPick && (!near || _spots[i].At.DistanceTo(was.At) <= 500f))
                    pick = i;
            }

            _spotPick = pick == _spotPick ? (_spotPick + 1) % _spots.Length : pick;
            _relocations++;
        }

        public override bool GoPlace()
        {
            if (Spot is not { } spot)
                return false;
            if (!_atSpot && D._self.Origin.DistanceTo(spot.At) > SpotArrive)
            {
                D.GoTo(spot.At, MoveSpeed.Run);
                D._verdict = "to a sniper spot";
                return true;
            }

            if (!_atSpot)
            {
                (_atSpot, _lastEnemyAt, _spotHadTargets, _boredDrawn) = (true, D._now, false, false);
                (_park, _parkUntil, _behindAt) = (spot.Watch, D._now, After(BehindDelay()));
            }

            if (!_boredDrawn)
                (_bored, _boredDrawn) = (BoredSeconds * (0.9f + 0.2f * _rng.NextFloat()), true);
            if ((_leaveAt.Value > 0 && D._now >= _leaveAt) || Seconds(_lastEnemyAt) > _bored)
            {
                Relocate(near: _spotHadTargets);
                D.GoTo(Spot?.At ?? spot.At, MoveSpeed.Run);
                D._verdict = "leaving the spot";
                return true;
            }

            D._move = MoveIntent.Hold;
            HoldLook(spot);
            D._verdict = "holding a sight line";
            return true;
        }

        private float BehindDelay()
            => (BehindLeast + _rng.NextFloat() * (BehindMost - BehindLeast)) * (Archetype == TFSniperClass.Guarded ? GuardedBehindScale : 1f);

        private bool Glancing => D._now < _behindUntil;

        /// <summary>At the spot: the aim parked on the watched line at a body's height, moved now and then; a glance behind every so often.</summary>
        private void HoldLook(TFSniperSpot spot)
        {
            float body = TFKnowledge.AimHeight(AimPoint.Body, TFClass.Undefined).Standing;
            if (D._now >= _behindAt && !Glancing)
            {
                AiVector away = D._self.Origin - (spot.Watch - D._self.Origin).Normalised() * 400f;
                (_behindPoint, _behindUntil, _behindAt) = (new AiVector(away.X, away.Y, D._self.Origin.Z + body), After(BehindHold), After(BehindHold + BehindDelay()));
                _behind++;
            }

            if (Glancing)
            {
                D._look = new LookIntent(LookKind.Glance, _behindPoint, ActorId.None, AimPoint.Body, 0f, 0);
                return;
            }

            if (D._now >= _parkUntil)
            {
                // A place on the watched line's width: the watch itself, or beside it.
                AiVector to = spot.Watch - spot.At;
                AiVector across = new AiVector(-to.Y, to.X, 0f).Normalised();
                _park = spot.Watch + across * ((_rng.NextFloat() * 2f - 1f) * 160f);
                _parkUntil = After(ParkLeast + _rng.NextFloat() * (ParkMost - ParkLeast));
            }

            D._look = new LookIntent(LookKind.Point, new AiVector(_park.X, _park.Y, _park.Z + body), ActorId.None, AimPoint.Body, 0f, 0);
        }

        // ------------------------------------------------------------------ the fight

        public override float TargetBonus(KnownActor target, bool sighted, float distance)
        {
            float bonus = target.ClassBelieved switch
            {
                (int)TFClass.Sniper => SniperBonus,
                (int)TFClass.Medic => MedicExtra,
                (int)TFClass.Demoman => DemoBonus,
                (int)TFClass.HeavyWeapons => HeavyBonus,
                (int)TFClass.Scout when distance > SmgRange => FarScoutMalus,
                _ => 0f,
            };

            // Scoped, the target is not swapped for a slightly better one inside its hold, unless the bot was just hit.
            if (_zoomed && sighted && ReferenceEquals(target, _held) && Seconds(_heldSince) < HoldTargetSeconds && Seconds(_seenAttack) > 0.5f)
                bonus += HoldTargetBonus;
            return bonus;
        }

        public override bool FightInSight()
        {
            float dist = D._targetDist;
            if (D.HoldingMelee && dist < MeleeGiveUp)
                return D.FightAtRange();
            if (dist < FleeRange)
            {
                // Too near to scope: back off to cover, fighting with what is in hand.
                if (D.Feel.Fight)
                    D.LoadInCover();
                else
                    D.Strafe(-0.8f);
                D._verdict = "backing off (too near)";
                return true;
            }

            if (HoldingRifle)
            {
                // A rifle is held still: scoped walking is a crawl and spoils the aim.
                D._move = MoveIntent.Hold;
                D._verdict = _zoomed ? "scoped" : "taking aim";
                return true;
            }

            return D.FightAtRange();
        }

        public override void Engage()
        {
            Read();
            if (_jarPhase > 0)
            {
                JarEngage();
                return;
            }

            if (!HoldingRifle)
            {
                D.GeneralEngage();
                return;
            }

            if (!D._targetSighted || D._target is not { } target)
                return;
            if (!ReferenceEquals(target, _held))
                (_held, _heldSince) = (target, D._now);
            int cls = target.ClassBelieved;
            bool drawn = D._view.TryActor(target.Id, out ActorFacts f);
            if (drawn)
                cls = f.Class;
            bool crouched = drawn && (f.Activity & ActorActivity.Crouched) != 0;
            // The head scoped, the body unscoped; the error is today's, never under stock's 0.01 rad (§4.7's floor).
            AimPoint point = _zoomed ? AimPoint.Head : AimPoint.Body;
            float height = TFKnowledge.AimHeight(point, point == AimPoint.Head ? (TFClass)cls : TFClass.Undefined).At(crouched);
            var aim = new AiVector(D._targetPos.X, D._targetPos.Y, D._targetPos.Z + height);
            AiVector error = D.AimError(aim, feet: false);
            float size = D.AimErrorNow;
            if (size > 0f && size < StockErrorFloorDeg)
                error *= StockErrorFloorDeg / size;
            D._look = new LookIntent(LookKind.Actor, aim + error, target.Id, point, 0f, 0);

            if (!_zoomed)
            {
                // Unscoped (no SMG in the kit, someone near): the rifle's body shot.
                if (D._targetDist < SmgRange && Seconds(_shotAt) > AfterShotSeconds)
                    Fire(charged: false);
                return;
            }

            if (Seconds(_zoomedAt) < ZoomSettleSeconds || Seconds(_shotAt) < AfterShotSeconds)
                return;
            float need = TFSniperClass.ChargeToKill(MaxHealthOf(cls), MedicNear(target.Id, D._targetPos));
            if (Charge >= need || (drawn && AimsAtMe(in f)))
                Fire(charged: need > 0f);
        }

        private void Fire(bool charged)
        {
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0);
            D.Shots++;
            _shotAt = D._now;
            if (_zoomed)
                _scopedShots++;
            if (charged)
                _chargedShots++;
        }

        /// <summary>The class's spawn health from the class table (never the target's own number: a player is not shown it).</summary>
        private int MaxHealthOf(int cls)
        {
            foreach (TFClassFacts c in D._classes)
            {
                if ((int)c.Class == cls && c.MaxHealth > 0)
                    return c.MaxHealth;
            }

            return SoldierHealth;
        }

        /// <summary>Whether an enemy Medic is seen within <see cref="MedicNearUnits"/> of the target.</summary>
        private bool MedicNear(ActorId target, AiVector at)
        {
            foreach (ref readonly ActorFacts a in D._view.Sighted)
            {
                if (a.Id != target && a.Class == (int)TFClass.Medic && !TeamRule.Friends(D._self.Team, a.Team) && a.Origin.DistanceTo(at) <= MedicNearUnits)
                    return true;
            }

            return false;
        }

        /// <summary>Whether a sighted enemy Sniper's facing points at the bot: cos over <see cref="AimedAtCos"/>.</summary>
        private bool AimsAtMe(in ActorFacts f) => f.Class == (int)TFClass.Sniper && FacingCos(in f, D._self.Origin) > AimedAtCos;

        private static float FacingCos(in ActorFacts f, AiVector at)
        {
            float yaw = f.Facing.Yaw * (MathF.PI / 180f), pitch = f.Facing.Pitch * (MathF.PI / 180f);
            var forward = new AiVector(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
            AiVector to = (at - f.Origin).Normalised();
            return forward.X * to.X + forward.Y * to.Y + forward.Z * to.Z;
        }

        // ------------------------------------------------------------------ the hands and the scope

        public override void Hands()
        {
            // A fire with the wrong weapon for the range gives way to the change (a rifle shot at 150 u, an SMG at 1,200 u).
            if (_jarPhase > 0 || D._action.Kind is not (ActionKind.None or ActionKind.Fire) || D._now < D._handsAt)
                return;
            int want = WantedWeapon();
            if (D._action.Kind == ActionKind.Fire && (want < 0 || want == D._self.ActiveWeapon))
                return;
            if (want < 0)
            {
                D.GeneralHands();
                return;
            }

            if (D._self.ActiveWeapon == want)
                return;
            if (want == SmgKind)
                _smgPicks++;
            else if (want == MeleeKind)
                _meleePicks++;
            D.Select(want);
        }

        /// <summary>The weapon for now: the Kukri within 200 u (kept to 250), the SMG within 750 u, else the rifle; in a lull the rifle.</summary>
        private int WantedWeapon()
        {
            int rifle = RifleKind;
            if (rifle < 0)
                return -1;
            if (!D._targetSighted)
                return D.SightJustLost ? D._self.ActiveWeapon : rifle;
            float dist = D._targetDist;
            int melee = MeleeKind, smg = SmgKind;
            if (melee >= 0 && (dist < MeleeRange || (D._self.ActiveWeapon == melee && dist < MeleeGiveUp)))
                return melee;
            if (dist < SmgRange)
                return smg >= 0 ? smg : rifle;
            return rifle;
        }

        /// <summary>Whether the scope should be up: the rifle in hand, on the ground, held still at a spot or on a far target; never with anyone near or on the move.</summary>
        private bool WantZoom()
        {
            if (!HoldingRifle || _jarPhase > 0 || Glancing || (D._self.Flags & SelfFlags.Grounded) == 0)
                return false;
            if (D._targetSighted)
                return D._targetDist >= SmgRange;
            if (D.SightJustLost)
                return _zoomed;
            return _atSpot;
        }

        public override void OneShots()
        {
            if (JarOneShot())
                return;
            bool want = WantZoom();
            if (want == _zoomed || ZoomRow < 0 || Seconds(_zoomPressAt) < ZoomEverySeconds || Seconds(_shotAt) < AfterShotSeconds)
                return;
            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(ZoomRow, 0), ++D._oneShot);
            _zoomPressAt = D._now;
            if (want)
                _zooms++;
        }

        // ------------------------------------------------------------------ the jar (archetype B)

        /// <summary>
        /// Jarate (§4.4 B): with the jar's meter full, a throw at the middle of two or more enemies within the splash of a
        /// point 300-1,000 u off, or at a burning teammate within 600 u (it puts him out). Never at a Pyro facing the bot
        /// (the airblast sends it back). The jar is taken out, aimed up its arc, thrown once, and the hands go back.
        /// </summary>
        private bool JarOneShot()
        {
            int jar = JarKind;
            if (jar < 0)
            {
                _jarPhase = 0;
                return false;
            }

            switch (_jarPhase)
            {
                case 0:
                    if (D._now < _jarNextAt || JarMeter < 1f || D._now < D._handsAt || !JarTarget(out _jarAt))
                        return false;
                    D.Select(jar);
                    (_jarPhase, _jarPhaseAt) = (1, D._now);
                    JarEngage();
                    return true;
                case 1:
                    if (D._self.ActiveWeapon != jar)
                    {
                        if (Seconds(_jarPhaseAt) > JarDeployMost)
                            EndJar();
                        return false;
                    }

                    (_jarPhase, _jarPhaseAt) = (2, D._now);
                    return false;
                case 2:
                    if (Seconds(_jarPhaseAt) < JarAimSeconds)
                        return false;
                    D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0);
                    _jarThrows++;
                    (_jarPhase, _jarPhaseAt) = (3, D._now);
                    return true;
                default:
                    // Thrown when the meter emptied or the hands went back; given up after a while either way.
                    if (JarMeter < 1f || D._self.ActiveWeapon != jar || Seconds(_jarPhaseAt) > JarDeployMost)
                        EndJar();
                    else
                        D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0);
                    return true;
            }
        }

        private void EndJar() => (_jarPhase, _jarNextAt) = (0, After(JarEverySeconds));

        /// <summary>The look while the jar is out: up the arc that lands it on the place it was taken out for (the jar leaves with 200 u/s of lift).</summary>
        private void JarEngage()
        {
            AiVector eye = D.OwnEye;
            var at = new AiVector(_jarAt.X, _jarAt.Y, _jarAt.Z + TFKnowledge.FeetAim);
            float flat = new AiVector(at.X - eye.X, at.Y - eye.Y, 0f).Length;
            AiVector aim = AimLead.Lob(eye, at, JarSpeed, TFKnowledge.Gravity) ?? at;
            D._look = new LookIntent(LookKind.Point, new AiVector(aim.X, aim.Y, aim.Z - JarUpSpeed * flat / JarSpeed), ActorId.None, AimPoint.Feet, 0f, 0);
            D._verdict = "jarate";
        }

        /// <summary>Where a jar goes now, if anywhere: a burning teammate, or the middle of a group of enemies in its splash.</summary>
        private bool JarTarget(out AiVector at)
        {
            at = default;
            AiVector me = D._self.Origin;
            foreach (ref readonly ActorFacts a in D._view.Sighted)
            {
                bool friend = TeamRule.Friends(D._self.Team, a.Team);
                float d = a.Origin.DistanceTo(me);
                if (friend && (a.Conditions & ActorConditions.Burning) != 0 && d <= JarMateReach && d > 64f)
                {
                    at = a.Origin;
                    return true;
                }

                if (friend || d < JarNear || d > JarFar || (a.Class == (int)TFClass.Pyro && FacingCos(in a, me) > AimedAtCos))
                    continue;
                int group = 0;
                AiVector sum = default;
                foreach (ref readonly ActorFacts b in D._view.Sighted)
                {
                    if (!TeamRule.Friends(D._self.Team, b.Team) && b.Origin.DistanceTo(a.Origin) <= JarSplash)
                        (group, sum) = (group + 1, sum + b.Origin);
                }

                if (group >= 2)
                {
                    at = sum * (1f / group);
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ a life and the counters

        public override void Died()
        {
            (_atSpot, _zoomed, _jarPhase, _held, _leaveAt, _behindUntil) = (false, false, 0, null, default, default);
            _spotsMade = false;
        }

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("sniper.zooms", _zooms));
            into.Add(new("sniper.scoped_shots", _scopedShots));
            into.Add(new("sniper.charged_shots", _chargedShots));
            into.Add(new("sniper.relocations", _relocations));
            into.Add(new("sniper.spot_decides", _spotDecides));
            into.Add(new("sniper.alive_decides", _aliveDecides));
            into.Add(new("sniper.jar_throws", _jarThrows));
            into.Add(new("sniper.behind_glances", _behind));
            into.Add(new("sniper.smg_picks", _smgPicks));
            into.Add(new("sniper.melee_picks", _meleePicks));
        }
    }
}
