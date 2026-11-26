//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Engineer's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Engineer lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFEngineerClass
{
    /// <summary>Whether the composition fields the Engineer (C.4): false until the Engineer lane says its play is ready: until then the composition fields no Engineer (a pin still may).</summary>
    public const bool Ready = false;

    /// <summary>
    /// The Engineer's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [];

    /// <summary>The stock Engineer's archetype: a sentry nest at the point (research A1).</summary>
    public const string Nest = "Nest";

    /// <summary>The battle Engineer (research A4): the Gunslinger's mini-sentry by the point he attacks, and his own guns.</summary>
    public const string Gunslinger = "Gunslinger";

    /// <summary>The Wrangler nest (research A2): the Nest, aiming his sentry himself when it is out-ranged or under siege.</summary>
    public const string Wrangler = "Wrangler";

    /// <summary>The Wrangler's item class (<c>tf_weapon_laser_pointer</c>): the kit read tells him he carries one.</summary>
    public const string LaserPointer = "tf_weapon_laser_pointer";

    /// <summary>The mobile nest (research A3): the Rescue Ranger's bolts repair his gun from range, so he stands back from it.</summary>
    public const string RescueRanger = "RescueRanger";

    /// <summary>The Rescue Ranger's item class: its bolts heal a friendly building they hit (<c>arrow_heals_buildings</c>).</summary>
    public const string RescueShotgun = "tf_weapon_shotgun_building_rescue";

    /// <summary>
    /// The Engineer's archetypes (C.3.1), drawn by the bot's seed and weight (the research's §4.2 weights, one table for every
    /// mode until the pick knows the mode). <see cref="Nest"/>, stock's: Shotgun 9, Pistol 22, Wrench 7, each standing for its
    /// reskins; a sentry to level 3 with a dispenser behind it. <see cref="Gunslinger"/>: Frontier Justice 141 (else the
    /// Shotgun), Pistol 22, the Gunslinger 142; a mini-sentry, never upgraded, and his own fight. <see cref="Wrangler"/>: Shotgun 9,
    /// the Wrangler 140, Wrench 7; the Nest that aims its sentry by hand. <see cref="RescueRanger"/>: the Rescue Ranger 997,
    /// Pistol 22, Wrench 7; the Nest that repairs from range, standing back.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Engineer,
    [
        new TFArchetype(Nest, 0.4f, [new(TFLoadoutSlot.Primary, [9]), new(TFLoadoutSlot.Secondary, [22]), new(TFLoadoutSlot.Melee, [7])]),
        new TFArchetype(Gunslinger, 0.25f, [new(TFLoadoutSlot.Primary, [141, 9]), new(TFLoadoutSlot.Secondary, [22]), new(TFLoadoutSlot.Melee, [142])]),
        new TFArchetype(Wrangler, 0.2f, [new(TFLoadoutSlot.Primary, [9]), new(TFLoadoutSlot.Secondary, [140]), new(TFLoadoutSlot.Melee, [7])]),
        new TFArchetype(RescueRanger, 0.15f, [new(TFLoadoutSlot.Primary, [997]), new(TFLoadoutSlot.Secondary, [22]), new(TFLoadoutSlot.Melee, [7])]),
    ]);

    /// <summary>
    /// Stock's defence roster limit (tf_bot.cpp:835): one Engineer for each 4 team members, at least one, at most 3 (its hard
    /// and expert limit; 1 on offence, which the composition's symmetric stance does not tell apart).
    /// </summary>
    public const int MaxPerTeam = 3, PerTeamMembers = 4;

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes, its cap.</summary>
    public static TFClassSpec Spec() => new(TFClass.Engineer, Ready, Abilities(), Archetypes(), MaxPerTeam, PerTeamMembers);
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Engineer's play (the research's A1 Nest, stock's <c>CTFBotEngineerBuild</c> family): he picks a sentry spot that
    /// sees the point he defends (<see cref="TFEngineerSpots"/>), walks there, orders the blueprint, faces the point and
    /// places it, retrying a refused spot as stock does; then works his nest by stock's order (<see cref="TFEngineerNest"/>),
    /// wrench out between his buildings, fetching metal when a job needs more than he carries. He fights a Spy always,
    /// anyone near while his sentry is not up, and anyone close when he is away from the gun; otherwise he keeps working and
    /// lets the sentry fight. When the point moves on and his sentry has not fired for <see cref="QuietSeconds"/> he takes
    /// the nest down and builds again by the new point (stock's out-of-position check). What he knows of his buildings is
    /// his HUD's panel (<see cref="TFEngineerMeters"/>).
    /// </summary>
    private sealed class EngineerPlay(TFDecider d) : ClassPlay(d, TFClass.Engineer)
    {
        /// <summary>Stock's <c>MaxPlacementAttempts</c> (tf_bot_engineer_building.cpp:34): refused this often, a spot is given up.</summary>
        public const int PlacementTries = 5;

        /// <summary>He stands this near his build spot before he orders the blueprint, and this near a building to swing at it (stock's 75 u).</summary>
        public const float StandUnits = 48f, WorkUnits = 75f;

        /// <summary>Between his buildings he stands this far from the dispenser's middle: inside its 64 u reach, which feeds him metal.</summary>
        public const float DispenserStandUnits = 40f, WorkArriveUnits = 20f, DispenserSideUnits = 40f;

        /// <summary>How far ahead of him a sentry's blueprint goes: its build box's half-diagonal (24 u by 24 u), a body's, and 4 u.</summary>
        public const float BuildDistanceUnits = 72f;

        /// <summary>A blueprint is ordered again at most this often, and clicked at most this often.</summary>
        public const float OrderSeconds = 1f, ClickSeconds = 0.6f;

        /// <summary>The view must be within this of the build yaw before a click: the blueprint goes where he faces.</summary>
        public const float AimDegrees = 8f;

        /// <summary>Stock's out-of-position rules (:326-364): a sentry farther than this from the point, quiet this long, comes down.</summary>
        public const float OutOfPositionUnits = 1800f, QuietSeconds = 10f;

        /// <summary>Stock's retaliation range (:28) and its "keep to the sentry" range (tf_bot_engineer_build.cpp:107-111); a threat this close is fought away from the gun.</summary>
        public const float RetaliateUnits = 750f, KeepToSentryUnits = 100f, CloseThreatUnits = 400f;

        /// <summary>
        /// While his gun is not up he keeps going to build it and shoots on the way (stock's EquipBestWeaponForThreat while the build
        /// goes on); only an enemy this close stops him to fight. Live on cp_granary a Nest that fought everything within 750 u at
        /// the middle stood and died at his spot three lives running, his gun up 14 s of 300.
        /// </summary>
        public const float BuildingThreatUnits = 300f;

        /// <summary>
        /// A sentry spot not reached in this long plus its walk at <see cref="PatienceSpeed"/> (a way the map does not give) is given
        /// up for another. Live on cp_granary the walk from red's spawn to the middle's ledge took 74 s, and a flat 45 s gave up a
        /// spot he was on his way to.
        /// </summary>
        public const float SpotPatienceSeconds = 30f, PatienceSpeed = 150f;

        private float SpotPatience(in TFSentrySpot spot) => SpotPatienceSeconds + _home.DistanceTo(spot.At) / PatienceSpeed;

        /// <summary>The points are looked at again this often (stock's 1-2 s <c>GetMyControlPoint</c> timer).</summary>
        public const float PointSeconds = 2f;

        /// <summary>The class branch's score while he has a nest to look after: over the objectives' (0.5) and a fight he leaves to the gun.</summary>
        public const float NestUtility = 0.75f;

        /// <summary>
        /// The Gunslinger's mini (research A4): its spot within this of the point he attacks, best at this range from it; a mini
        /// farther than this from his point comes down at once (a mini is cheap and quick: the wiki's rule, destroy and
        /// rebuild); he tends it only within this, else he plays the point with his team.
        /// </summary>
        public const float MiniReach = 800f, MiniBestRange = 350f, MiniOutOfPositionUnits = 1000f, MiniTendUnits = 600f;

        /// <summary>
        /// The Wrangler's rules (research A2 with the review's E3: on what he sees, not on who hurt the gun): he takes the gun when
        /// an enemy he sees stands beyond its reach of it, or the gun is under <see cref="WrangleUnderShare"/> of its health with an
        /// enemy in sight; he puts it away after <see cref="WrangleQuietSeconds"/> with nothing in sight, or when the gun is under
        /// <see cref="WrangleRepairShare"/> and wants the wrench.
        /// </summary>
        public const float WrangleUnderShare = 0.6f, WrangleRepairShare = 0.25f, WrangleQuietSeconds = 2f;

        /// <summary>
        /// The Rescue Ranger's stand (research A3: 250-600 u back, in cover): this far from his gun, on his side of it, away from
        /// the point; his bolts repair it from there. An upgrade, a construction or a sapper still wants the wrench up close.
        /// </summary>
        public const float RangedStandUnits = 320f, RangedReachUnits = 700f;

        private AiRandom? _rng;
        private int _buildSentryRow = -1, _buildDispenserRow = -1, _destroySentryRow = -1, _destroyDispenserRow = -1;

        // The point, and the spots by it.
        private bool _hasPoint;
        private AiVector _point;
        private int _pointKey = int.MinValue, _spotsKey = int.MinValue;
        private Tick _pointAt;
        private List<TFSentrySpot> _spots = [];
        private readonly HashSet<int> _failed = [];
        private TFSentrySpot? _spot;
        private TFSpotCensus _census;
        private AiVector _ownSpawn, _enemySpawn, _home;
        private bool _homeKnown;
        private Tick _spotSince;

        // This decide's read.
        private Tick _plannedAt = new(-1);
        private TFOwnBuilding _sentry, _dispenser;
        private int _placing;
        private TFEngineerStep _step;
        private bool _fighting;

        // The placement in hand.
        private float _buildYaw, _sentryYaw, _offset;
        private int _tries;
        private Tick _orderAt = new(-100000), _clickAt = new(-100000), _dispenserRetry;

        // What his panel and body told him before.
        private int _lastHealth, _lastShells = -1, _lastKills;
        private Tick _hurtAt = new(-100000), _sentryFiredAt, _lifeAt, _countedAt;
        private bool _sentryWas, _dispenserWas, _relocating, _respawned = true;
        private TFEngineerJob _lastJob;

        // The harness's counters.
        private long _sentriesBuilt, _dispensersBuilt, _sentryTicks, _levelThreeTicks, _refused, _relocations, _metalTrips, _sentryKills;
        private long _wrangleTicks, _bolts, _swings, _topLevel, _topProgress, _dryWork;
        private float _firstSentry = -1f;

        private AiRandom Rng => _rng ??= new AiRandom(D._seed ^ 0x454E_4749UL);

        /// <summary>The job of this decide, for the facts.</summary>
        internal TFEngineerStep Step => _step;

        /// <summary>The sentry spot he holds, for the facts.</summary>
        internal TFSentrySpot? Spot => _spot;

        /// <summary>The point he builds by, for the facts.</summary>
        internal AiVector? Point => _hasPoint ? _point : null;

        private float Seconds(Tick from) => (D._now - from) * D._tick;

        private bool UnderAttack => Seconds(_hurtAt) < 1f || _sentry.Sapped || _dispenser.Sapped;

        /// <summary>Whether he plays the battle Engineer: a mini by the point, his own guns, no nest to sit in.</summary>
        private bool Battle => Archetype == TFEngineerClass.Gunslinger;

        private bool _wrangling;
        private Tick _wrangleSeen;

        /// <summary>The Wrangler's kind when his kit has one in the secondary slot, else -1.</summary>
        private int WranglerKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Secondary, out Senses.TFKitItem item)
                                    && item.ItemClass == TFEngineerClass.LaserPointer ? item.WeaponKind : -1;

        /// <summary>The Rescue Ranger's kind when his kit has one in the primary slot, else -1.</summary>
        private int RescueKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Primary, out Senses.TFKitItem item)
                                  && item.ItemClass == TFEngineerClass.RescueShotgun ? item.WeaponKind : -1;

        /// <summary>
        /// Whether he works his gun from range this decide: a Rescue Ranger whose sentry stands built and unsapped, with a repair to
        /// do or nothing else to do (a bolt neither upgrades nor builds).
        /// </summary>
        private bool Ranged => Archetype == TFEngineerClass.RescueRanger && RescueKind >= 0 && _step is { Job: TFEngineerJob.Work, Kind: TFEngineerPanel.Sentry }
                               && _sentry.Built && !_sentry.UnderConstruction && !_sentry.Sapped && (_sentry.Hurt || _sentry.Level >= TFEngineerNest.TopLevel);

        /// <summary>Whether he aims his sentry himself this decide (<see cref="WrangleUnderShare"/>' rules).</summary>
        internal bool Wrangling => _wrangling;

        private void Wrangle()
        {
            bool can = Archetype == TFEngineerClass.Wrangler && WranglerKind >= 0 && _sentry.Exists && _sentry.Built && !_sentry.Sapped && !_fighting;
            float share = _sentry.Health / (float)Math.Max(1, _sentry.MaxHealth);
            if (!can || share < WrangleRepairShare)
            {
                _wrangling = false;
                return;
            }

            if (D._targetSighted)
            {
                _wrangleSeen = D._now;
                if (D._targetPos.DistanceTo(_sentry.At) > TFEngineerSpots.SentryRange || share < WrangleUnderShare)
                    _wrangling = true;
            }
            else if (Seconds(_wrangleSeen) > WrangleQuietSeconds)
            {
                _wrangling = false;
            }
        }

        /// <summary>Reads his panel and the points, and settles this decide's job; once a decide.</summary>
        private void Plan()
        {
            if (D._now == _plannedAt)
                return;
            _plannedAt = D._now;
            Senses.TFHud? hud = D.Kit;
            _sentry = TFEngineerPanel.Read(hud, TFEngineerPanel.Sentry);
            _dispenser = TFEngineerPanel.Read(hud, TFEngineerPanel.Dispenser);
            _placing = (int)(hud?.Meter(TFEngineerPanel.Placing, 0f) ?? 0f);
            Note();
            ReadPoint();
            Relocate();
            if (_dispenser.Exists)
                _dispenserRetry = D._now + (int)(TFEngineerNest.DispenserRetrySeconds / D._tick);
            _step = _hasPoint || _sentry.Exists
                ? TFEngineerNest.Next(in _sentry, in _dispenser, D._self.Metal, D._now >= _dispenserRetry && !UnderAttack, upgrades: !Battle,
                                      sentryCost: Battle ? TFEngineerNest.MiniSentryCost : TFEngineerNest.SentryCost)
                : default;
            if (_step.Job == TFEngineerJob.Metal && _lastJob != TFEngineerJob.Metal)
                _metalTrips++;
            _lastJob = _step.Job;
            _fighting = ShouldFight();
            Wrangle();
            if (_wrangling)
                _wrangleTicks += DecideSchedule.Period;
        }

        /// <summary>What changed on his panel and his body since the last decide: hurt, the gun firing, built, the counters.</summary>
        private void Note()
        {
            if (_respawned)
                (_lifeAt, _respawned, _spotSince, _home, _homeKnown) = (D._now, false, D._now, D._self.Origin, true);
            if (D._self.Health < _lastHealth)
                _hurtAt = D._now;
            _lastHealth = D._self.Health;
            if (_sentry.Exists && _sentry.Built)
            {
                if (_lastShells >= 0 && _sentry.Ammo < _lastShells)
                    _sentryFiredAt = D._now;
                _lastShells = _sentry.Ammo;
                int ticks = Math.Max(0, D._now - _countedAt);
                _sentryTicks += ticks;
                if (_sentry.Level >= TFEngineerNest.TopLevel)
                    _levelThreeTicks += ticks;
            }
            else
            {
                _lastShells = -1;
            }

            if (_sentry.Exists && !_sentryWas)
            {
                _sentriesBuilt++;
                _sentryFiredAt = D._now;
                if (_firstSentry < 0f)
                    _firstSentry = Seconds(_lifeAt);
                (_tries, _offset, _relocating) = (0, 0f, false);
            }

            // A spot's patience runs from when he last had to walk to it: a gun lost there starts it again.
            if (_sentry.Exists != _sentryWas)
                _spotSince = D._now;
            if (_sentry.Exists)
                (_topLevel, _topProgress) = (Math.Max(_topLevel, _sentry.Level), Math.Max(_topProgress, _sentry.Level * 100 + _sentry.Progress));
            if (_sentry.Exists && _sentry.Kills > _lastKills)
                _sentryKills += _sentry.Kills - _lastKills;
            _lastKills = _sentry.Exists ? _sentry.Kills : 0;
            if (_dispenser.Exists && !_dispenserWas)
                (_dispensersBuilt, _tries, _offset) = (_dispensersBuilt + 1, 0, 0f);
            (_sentryWas, _dispenserWas, _countedAt) = (_sentry.Exists, _dispenser.Exists, D._now);
        }

        /// <summary>
        /// The point he builds by (stock's <c>GetMyControlPoint</c> for an Engineer, tf_bot.cpp:1970-2008): an own point the
        /// enemy may capture now, the nearest; else the nearest point his team may capture; on a flag map his own capture
        /// zone (<c>GetFlagCaptureZone</c>, tf_bot_engineer_move_to_build.cpp:68-79), else his own flag.
        /// </summary>
        private void ReadPoint()
        {
            if (_pointKey != int.MinValue && D._now < _pointAt)
                return;
            _pointAt = D._now + (int)(PointSeconds / D._tick);
            if (D._view.Objectives is not { } objectives)
            {
                _hasPoint = false;
                return;
            }

            int team = D._self.Team, mine = 1 << team, enemies = ~mine;
            Objective? defend = null, take = null, zone = null, flag = null, theirs = null;
            float dd = float.PositiveInfinity, td = float.PositiveInfinity;
            foreach (Objective o in objectives.All)
            {
                float d = D._self.Origin.DistanceTo(o.Position);
                if (o.Kind == ObjectiveKind.Capture && o.State != ObjectiveState.Locked)
                {
                    if (o.OwnerTeam == team && (o.MayAct & enemies) != 0 && d < dd)
                        (dd, defend) = (d, o);
                    else if (o.OwnerTeam != team && (o.MayAct & mine) != 0 && d < td)
                        (td, take) = (d, o);
                }
                else if (o.Kind == ObjectiveKind.Deliver && o.OwnerTeam == team)
                {
                    zone = o;
                }
                else if (o.Kind == ObjectiveKind.Carry && o.OwnerTeam == team)
                {
                    flag = o;
                }
                else if (o.Kind == ObjectiveKind.Carry && o.State == ObjectiveState.Home)
                {
                    theirs = o;
                }
            }

            // The battle Engineer attacks: the point his team may take comes first; on a flag map the enemy's flag at home.
            Objective? pick = Battle ? take ?? defend ?? theirs ?? zone ?? flag : defend ?? take ?? zone ?? flag;
            _hasPoint = pick is not null;
            if (pick is { } p)
                (_point, _pointKey) = (p.Position, p.Id);
        }

        /// <summary>
        /// Stock's out-of-position check (:326-364): his sentry stands farther than <see cref="OutOfPositionUnits"/> from the
        /// point he now builds by and has not fired for <see cref="QuietSeconds"/>: it and the dispenser come down, and he
        /// builds again by the new point.
        /// </summary>
        private void Relocate()
        {
            if (!_sentry.Exists)
            {
                _relocating = false;
                return;
            }

            bool away = Battle ? Flat(_sentry.At, _point) > MiniOutOfPositionUnits
                               : Flat(_sentry.At, _point) > OutOfPositionUnits && Seconds(_sentryFiredAt) >= QuietSeconds;
            if (!_hasPoint || _relocating || !away)
                return;
            _relocating = true;
            _relocations++;
            _spot = null;
        }

        /// <summary>
        /// Stock's ShouldAttack (tf_bot_engineer_build.cpp:107-111, tf_bot_engineer_building.cpp:28): a Spy always; anyone within
        /// <see cref="RetaliateUnits"/> while his sentry is not up; anyone within <see cref="CloseThreatUnits"/> while he is away
        /// from the gun; otherwise the gun fights and he keeps working.
        /// </summary>
        private bool ShouldFight()
        {
            if (!D._targetSighted || D._target is not { } target)
                return false;
            if (target.ClassSeen == (int)TFClass.Spy || (Battle && D._targetDist < RetaliateUnits))
                return true;
            if (!_sentry.Exists || !_sentry.Built)
                return D._targetDist < BuildingThreatUnits || !Nesting;
            return D._targetDist < CloseThreatUnits && D._self.Origin.DistanceTo(_sentry.At) > KeepToSentryUnits;
        }

        /// <summary>
        /// Whether he has a nest to look after (a job by a point, or a sentry standing). The battle Engineer only while his mini is
        /// to build, to finish, to unsap or to take down, near it: else he goes with his team.
        /// </summary>
        private bool Nesting => _step.Job != TFEngineerJob.None
                                && (!Battle || _relocating || _step.Job == TFEngineerJob.Build || _step.Job == TFEngineerJob.Metal
                                    || ((_sentry.UnderConstruction || _sentry.Sapped) && D._self.Origin.DistanceTo(_sentry.At) < MiniTendUnits));

        public override float PlaceUtility()
        {
            Plan();
            return Nesting ? NestUtility : 0f;
        }

        public override float Weigh(string branch)
        {
            Plan();
            return branch switch
            {
                Fight => _fighting ? 1.3f : Nesting && D._targetSighted ? 0.35f : 1f,
                Survive => _dispenser.Built && D._self.Origin.DistanceTo(_dispenser.At) < 300f ? 0.3f : 1f,
                ClassBranch or Deliver => 1f,
                _ => Nesting ? 0.4f : 1f,
            };
        }

        public override bool GoPlace()
        {
            Plan();
            switch (_step.Job)
            {
                case TFEngineerJob.Build when _step.Kind == TFEngineerPanel.Sentry:
                    if (SentrySpot() is not { } spot)
                        return false;
                    _buildYaw = spot.Yaw;
                    return Stand(BuildStand(spot), "to the sentry spot", StandUnits);
                case TFEngineerJob.Build:
                    // Beside his gun, facing back from the point: the dispenser lands behind and to the side of it, about 87 u off,
                    // so a stand between the two is inside the dispenser's 64 u reach and the wrench's reach of the gun.
                    _buildYaw = _sentryYaw + 180f;
                    return Stand(DispenserStand(), "to the dispenser spot", WorkArriveUnits);
                case TFEngineerJob.Work when Ranged:
                    return Stand(RangedStand(), "covering the nest", WorkUnits);
                case TFEngineerJob.Work:
                    return Stand(WorkStand(), "working the nest", WorkArriveUnits);
                case TFEngineerJob.Metal:
                    if (!MetalPlace(out AiVector at))
                        return false;
                    D.GoTo(at, MoveSpeed.Run);
                    D._verdict = "fetching metal";
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Where he stands to put the blueprint on <paramref name="spot"/>: a build distance short of it, facing the point, since the
        /// blueprint goes that far ahead of him (TFBuilderPlacement.CalculatePlacementPos, the hull's diagonal and his, plus 4 u).
        /// Live on cp_granary, standing on a ledge's spot itself put the blueprint over the ledge's edge ("a corner of the spot
        /// slopes or hangs") for 8 to 22 refusals a game. On the spot itself when no floor lies there.
        /// </summary>
        private AiVector BuildStand(in TFSentrySpot spot)
        {
            AiVector at = spot.At - TFEngineerSpots.Forward(spot.Yaw) * BuildDistanceUnits;
            return D._graph is { } g && g.FindCell(at + new AiVector(0f, 0f, 8f)).IsValid ? at : spot.At;
        }

        /// <summary>Where he stands to put the dispenser down: <see cref="DispenserSideUnits"/> to his gun's side (the side with floor).</summary>
        private AiVector DispenserStand()
        {
            AiVector fwd = TFEngineerSpots.Forward(_sentryYaw), side = new(-fwd.Y, fwd.X, 0f);
            AiVector right = _sentry.At + side * DispenserSideUnits, left = _sentry.At - side * DispenserSideUnits;
            return D._graph is { } g && !g.FindCell(right + new AiVector(0f, 0f, 8f)).IsValid ? left : right;
        }

        /// <summary>Walks to <paramref name="at"/> and holds there within <paramref name="arrive"/>.</summary>
        private bool Stand(AiVector at, string why, float arrive = StandUnits * 0.5f)
        {
            if (Flat(D._self.Origin, at) > arrive)
                D.GoTo(at, MoveSpeed.Run);
            D._verdict = why;
            return true;
        }

        private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>
        /// Where he stands to work (stock's UpgradeAndMaintainBuildings, :85-147): by the job's building, on his side of it;
        /// between his sentry and dispenser when both stand near each other, in reach of either.
        /// </summary>
        private AiVector WorkStand()
        {
            TFOwnBuilding target = _step.Kind == TFEngineerPanel.Dispenser ? _dispenser : _sentry;
            if (_sentry.Exists && _dispenser.Exists && _sentry.At.DistanceTo(_dispenser.At) < WorkUnits * 2f)
            {
                // Inside the dispenser's reach (TFDispenser.DispenserRadius, 64 u from its middle), on the sentry's side of it: it
                // feeds him the metal the upgrades spend while the wrench reaches either.
                AiVector toSentry = _sentry.At - _dispenser.At;
                toSentry = new AiVector(toSentry.X, toSentry.Y, 0f);
                return toSentry.Length < 1f ? _dispenser.At : _dispenser.At + toSentry.Normalised() * MathF.Min(DispenserStandUnits, toSentry.Length * 0.5f);
            }
            AiVector away = D._self.Origin - target.At;
            away = new AiVector(away.X, away.Y, 0f);
            return away.Length < 1f ? target.At : target.At + away.Normalised() * (WorkUnits * 0.6f);
        }

        /// <summary>The Rescue Ranger's stand: <see cref="RangedStandUnits"/> behind his gun, away from the point it covers.</summary>
        private AiVector RangedStand()
        {
            AiVector back = _hasPoint ? _sentry.At - _point : D._self.Origin - _sentry.At;
            back = new AiVector(back.X, back.Y, 0f);
            if (back.Length < 1f)
                return _sentry.At;
            AiVector at = _sentry.At + back.Normalised() * RangedStandUnits;
            return D._graph is { } g && g.FindCell(at).IsValid ? at : _sentry.At + back.Normalised() * WorkUnits;
        }

        /// <summary>The sentry spot: the one he holds, else a draw from the point's candidates (worked out once per point) not yet failed.</summary>
        private TFSentrySpot? SentrySpot()
        {
            if (!_hasPoint || D._graph is not { } graph)
                return null;
            if (_spotsKey != _pointKey)
            {
                int own = D._self.Team, enemy = own == (int)TFTeam.Red ? (int)TFTeam.Blue : (int)TFTeam.Red;
                (_ownSpawn, _enemySpawn) = (SpawnOf(own), SpawnOf(enemy));
                _spots = Battle
                    ? TFEngineerSpots.Candidates(graph, D._memory.World.Sight, _point, own, D._map.TeamZones, _ownSpawn, _enemySpawn, out _census, MiniReach, MiniBestRange)
                    : TFEngineerSpots.Candidates(graph, D._memory.World.Sight, _point, own, D._map.TeamZones, _ownSpawn, _enemySpawn, out _census);
                _spotsKey = _pointKey;
                _failed.Clear();
                _spot = null;
            }

            if (_spot is { } held)
            {
                // Walking to it for too long (a way the map does not give): another spot.
                if (Seconds(_spotSince) <= SpotPatience(held) || Flat(D._self.Origin, held.At) <= StandUnits)
                    return held;
                _failed.Add(held.Cell);
                _spot = null;
            }

            for (int tries = 0; tries < TFEngineerSpots.DrawFrom && _spot is null; tries++)
            {
                if (TFEngineerSpots.Pick(_spots, _failed, Rng) is not { } pick)
                    break;
                if (TFEngineerSpots.Reachable(graph, D._self.Origin, in pick))
                    (_spot, _spotSince) = (pick, D._now);
                else
                    _failed.Add(pick.Cell);
            }

            return _spot;
        }

        /// <summary>
        /// The spawns the sides are measured from: his own is where he last came into the world (he was there); the enemy's is
        /// the enemy's team spawn farthest from it. Live on cp_granary the team spawns nearest the middle were a pair by the
        /// middle itself (a forward stage's), the red one on blue's side, and the finder took blue's side for red's.
        /// </summary>
        private AiVector SpawnOf(int team)
        {
            if (team == D._self.Team)
                return _homeKnown ? _home : D._self.Origin;
            AiVector home = _homeKnown ? _home : D._self.Origin, best = _point;
            float bd = -1f;
            foreach (TeamSpawn s in D._map.TeamSpawns)
            {
                float d = s.Position.DistanceTo(home);
                if (s.Team == team && d > bd)
                    (bd, best) = (d, s.Position);
            }

            return best;
        }

        /// <summary>The nearest ammo pack he has not seen taken, or his team's resupply cabinet.</summary>
        private bool MetalPlace(out AiVector at)
        {
            at = default;
            float best = float.PositiveInfinity;
            ReadOnlySpan<ItemBelief> beliefs = D._memory.Board.Items;
            for (int i = 0; i < D._map.Items.Length; i++)
            {
                ItemSpawn item = D._map.Items[i];
                if (item.Kind is not (ItemKind.AmmoSmall or ItemKind.AmmoMedium or ItemKind.AmmoLarge or ItemKind.Ammo)
                    || (i < beliefs.Length && beliefs[i].Present == false))
                    continue;
                float d = D._self.Origin.DistanceTo(item.Position);
                if (d < best)
                    (best, at) = (d, item.Position);
            }

            foreach (TeamZone z in D._map.TeamZones)
            {
                if (z.Kind != ZoneKind.Resupply || z.Team != D._self.Team)
                    continue;
                var mid = new AiVector((z.Mins.X + z.Maxs.X) * 0.5f, (z.Mins.Y + z.Maxs.Y) * 0.5f, z.Mins.Z);
                float d = D._self.Origin.DistanceTo(mid);
                if (d < best)
                    (best, at) = (d, mid);
            }

            return best < float.PositiveInfinity;
        }

        public override void Engage()
        {
            Plan();
            // Wrangled, the sentry fires where he aims (TFSentryWrangle): the general fire at the target is the gun's. On his way to
            // build, a gun in hand, he shoots what he sees as he walks.
            if (_fighting || (_wrangling && D._self.ActiveWeapon == WranglerKind) || ShootsOnTheWay)
                D.GeneralEngage();
        }

        /// <summary>Walking to build (not yet at his stand, no blueprint out) with a gun in hand and an enemy in sight.</summary>
        private bool ShootsOnTheWay => D._targetSighted && _placing == 0 && D._hasGoal && _step.Job is TFEngineerJob.Build or TFEngineerJob.Metal
                                       && (D._self.ActiveWeapon == D.PrimaryKind || D._self.ActiveWeapon == D.SecondaryKind);

        /// <summary>The nest's hands after the fire: the blueprint ordered and clicked down, the wrench swung at the job's building, an out-of-place nest taken down.</summary>
        public override void OneShots()
        {
            Plan();
            if (_fighting || _wrangling || D._root.Chosen?.Name != ClassBranch)
                return;
            if (_relocating)
            {
                TakeDown();
                return;
            }

            switch (_step.Job)
            {
                case TFEngineerJob.Build:
                    Place(_step.Kind == TFEngineerPanel.Sentry ? TFObjectType.Sentrygun : TFObjectType.Dispenser);
                    break;
                case TFEngineerJob.Work when Ranged:
                    Bolt();
                    break;
                case TFEngineerJob.Work:
                    Swing(_step.Kind == TFEngineerPanel.Dispenser ? _dispenser : _sentry);
                    break;
            }
        }

        /// <summary>
        /// At his stand: orders the blueprint (the build row) unless it is out, turns to the build yaw (the blueprint goes where he
        /// faces, TFBuilderPlacement.CalculatePlacementPos) and clicks; a click that leaves the blueprint out was refused, and he
        /// turns a little and clicks again, giving the spot up after <see cref="PlacementTries"/> refusals (stock's retries).
        /// </summary>
        private void Place(TFObjectType type)
        {
            if (D._hasGoal)
                return;
            if (_placing != (int)type + 1)
            {
                if (Seconds(_orderAt) < OrderSeconds)
                    return;
                int row = type == TFObjectType.Sentrygun ? (_buildSentryRow = RowOf(_buildSentryRow, "build_sentry"))
                                                         : (_buildDispenserRow = RowOf(_buildDispenserRow, "build_dispenser"));
                if (row < 0)
                    return;
                D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(row, 0), ++D._oneShot);
                (_orderAt, _clickAt) = (D._now, new Tick(-100000));
                return;
            }

            if (Seconds(_clickAt) < ClickSeconds)
                return;
            if (Seconds(_clickAt) < ClickSeconds * 3f)
            {
                // The last click left the blueprint out: refused. Turn a little, or give the spot up.
                _refused++;
                _clickAt = new Tick(-100000);
                if (++_tries >= PlacementTries)
                {
                    if (type == TFObjectType.Sentrygun && _spot is { } failed)
                        _failed.Add(failed.Cell);
                    (_spot, _tries, _offset) = (null, 0, 0f);
                    return;
                }

                _offset = (_tries % 2 == 1 ? 1f : -1f) * 35f * ((_tries + 1) / 2);
            }

            float yaw = _buildYaw + _offset;
            D._look = new LookIntent(LookKind.Point, D.OwnEye + TFEngineerSpots.Forward(yaw) * 256f - new AiVector(0f, 0f, 40f), ActorId.None, AimPoint.Body, 0f, 0);
            if (MathF.Abs(Angle(D._self.Eye.Yaw - yaw)) > AimDegrees)
                return;
            if (type == TFObjectType.Sentrygun)
                _sentryYaw = yaw;
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0);
            D._friendFire = true;
            _clickAt = D._now;
        }

        /// <summary>The wrench at <paramref name="b"/>'s middle, held, once he is in reach and holds it.</summary>
        private void Swing(in TFOwnBuilding b)
        {
            if (!b.Exists || D._self.Origin.DistanceTo(b.At) > WorkUnits * 1.3f)
                return;
            D._look = new LookIntent(LookKind.Point, b.At + new AiVector(0f, 0f, 24f), ActorId.None, AimPoint.Body, 0f, 0);
            if (!D.HoldingMelee)
                return;
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
            D._friendFire = true;
            _swings++;
            _dryWork += D._self.Metal <= 0 ? 1 : 0;
        }

        /// <summary>A bolt at his hurt gun from where he stands, the Rescue Ranger in hand: it heals the building it hits.</summary>
        private void Bolt()
        {
            if (!_sentry.Hurt || D._self.Origin.DistanceTo(_sentry.At) > RangedReachUnits)
                return;
            D._look = new LookIntent(LookKind.Point, _sentry.At + new AiVector(0f, 0f, 24f), ActorId.None, AimPoint.Body, 0f, 0);
            if (D._self.ActiveWeapon != RescueKind || D._self.Clip <= 0)
                return;
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0);
            D._friendFire = true;
            _bolts++;
        }

        /// <summary>The out-of-place nest comes down: the destroy rows, the dispenser then the sentry.</summary>
        private void TakeDown()
        {
            if (Seconds(_orderAt) < OrderSeconds)
                return;
            int row = _dispenser.Exists ? (_destroyDispenserRow = RowOf(_destroyDispenserRow, "destroy_dispenser"))
                                        : (_destroySentryRow = RowOf(_destroySentryRow, "destroy_sentry"));
            if (row < 0)
                return;
            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(row, 0), ++D._oneShot);
            _orderAt = D._now;
        }

        private int RowOf(int known, string name) => known >= 0 ? known : D.AbilityRow(name);

        private static float Angle(float degrees)
        {
            degrees %= 360f;
            return degrees > 180f ? degrees - 360f : degrees < -180f ? degrees + 360f : degrees;
        }

        /// <summary>The fight's guns when he fights (the shotgun near, the pistol far); the wrench out at the nest; never a switch with a blueprint out.</summary>
        public override void Hands()
        {
            Plan();
            if (D._action.Kind != ActionKind.None || D.Busy || D._now < D._handsAt)
                return;
            if (_wrangling)
            {
                if (D._self.ActiveWeapon != WranglerKind)
                    D.Select(WranglerKind);
                return;
            }

            if (_fighting)
            {
                int primary = D.PrimaryKind, secondary = WranglerKind >= 0 ? -1 : D.SecondaryKind;
                int want = D._targetDist > 900f && secondary >= 0 ? secondary : primary >= 0 ? primary : secondary;
                bool gun = D._self.ActiveWeapon == primary || D._self.ActiveWeapon == secondary;
                if (want >= 0 && !gun)
                {
                    D.Select(want);
                    return;
                }

                D.GeneralHands();
                return;
            }

            if (_placing > 0)
                return;
            if (Ranged && D._root.Chosen?.Name == ClassBranch)
            {
                if (D._self.ActiveWeapon != RescueKind)
                    D.Select(RescueKind);
                else
                    D.GeneralHands();
                return;
            }

            if (_step.Job == TFEngineerJob.Work && D._root.Chosen?.Name == ClassBranch)
            {
                int melee = D.KindIn(TFLoadoutSlot.Melee);
                if (melee >= 0 && D._self.ActiveWeapon != melee)
                    D.Select(melee);
                return;
            }

            D.GeneralHands();
        }

        public override void Died()
        {
            (_tries, _offset, _clickAt, _orderAt, _respawned) = (0, 0f, new Tick(-100000), new Tick(-100000), true);
            _spotSince = new Tick(int.MaxValue / 2);
        }

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("engineer.sentries_built", _sentriesBuilt));
            into.Add(new("engineer.dispensers_built", _dispensersBuilt));
            into.Add(new("engineer.sentry_up_s", (long)(_sentryTicks * D._tick)));
            into.Add(new("engineer.level3_s", (long)(_levelThreeTicks * D._tick)));
            into.Add(new("engineer.sentry_kills", _sentryKills));
            into.Add(new("engineer.refused", _refused));
            into.Add(new("engineer.relocations", _relocations));
            into.Add(new("engineer.metal_trips", _metalTrips));
            into.Add(new("engineer.first_sentry_s", _firstSentry < 0f ? -1 : (long)_firstSentry));
            into.Add(new("engineer.wrangled_s", (long)(_wrangleTicks * D._tick)));
            into.Add(new("engineer.bolts", _bolts));
            into.Add(new("engineer.swings", _swings));
            into.Add(new("engineer.dry_swings", _dryWork));
            into.Add(new("engineer.top_level", _topLevel));
            into.Add(new("engineer.top_progress", _topProgress));
            // What the spot finder saw at its last point, for the harness: the finder's own read of a live map.
            into.Add(new("engineer.spots_near", _census.Near));
            into.Add(new("engineer.spots_candidates", _census.Candidates));
            into.Add(new("engineer.spots_own_side", _census.OwnSide));
            into.Add(new("engineer.spots_cells", (_census.PointCell ? 1 : 0) + (_census.OwnField ? 2 : 0) + (_census.EnemyField ? 4 : 0)));
            into.Add(new("engineer.spot_x", _spot is { } sp ? (long)sp.At.X : 0));
            into.Add(new("engineer.spot_y", _spot is { } sq ? (long)sq.At.Y : 0));
            into.Add(new("engineer.own_spawn_y", (long)_ownSpawn.Y));
            into.Add(new("engineer.enemy_spawn_y", (long)_enemySpawn.Y));
            into.Add(new("engineer.point_y", (long)_point.Y));
        }
    }
}
