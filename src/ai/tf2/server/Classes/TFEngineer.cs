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
    /// <summary>
    /// Whether the composition fields the Engineer (C.4). Ready since round 2 (2026-11-24, build 76d59b9921, six runs an arm,
    /// one pinned Nest among Stock Soldiers against all Stock Soldiers): on cp_granary the enemy's first capture came at a
    /// median over 232 s against the control's 152 s (three of six games none in 300 s), its captures 0.5 against 1; on
    /// ctf_2fort its flag pickups 0 against 3.5, its first pickup over 141 s against 100 s (four games none).
    /// </summary>
    public const bool Ready = true;

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
    /// The Battle Engie (round 2, the subclasses guide's "Battle Engie": Competitive, All-Rounder/Support, tier A): more aggressive
    /// than the Nest. His teleporter's exit goes close to the front first, his gun by the point his team takes, and he fights
    /// beside it with his own guns.
    /// </summary>
    public const string BattleEngie = "Battle";

    /// <summary>
    /// The Ninjaneer (round 2, the guide's: Competitive, Defensive/Flank, tier B, "high risk, high reward"): a nest behind the
    /// enemy's lines, by the enemy's next point behind the one his team takes, where their reinforcements pass and do not look.
    /// </summary>
    public const string Ninjaneer = "Ninjaneer";

    /// <summary>The Short Circuit's item class (<c>tf_weapon_mechanical_arm</c>): its shots cost metal, so it is never a gun to fight with.</summary>
    public const string MechanicalArm = "tf_weapon_mechanical_arm";

    /// <summary>
    /// The Engineer's archetypes (C.3.1), drawn by the bot's seed and weight (the research's §4.2 weights, one table for every
    /// mode until the pick knows the mode). <see cref="Nest"/>, stock's: Shotgun 9, Pistol 22, Wrench 7, each standing for its
    /// reskins; a sentry to level 3 with a dispenser behind it. <see cref="Gunslinger"/>: Frontier Justice 141 (else the
    /// Shotgun), Pistol 22, the Gunslinger 142; a mini-sentry, never upgraded, and his own fight. <see cref="Wrangler"/>: Shotgun 9,
    /// the Wrangler 140, Wrench 7; the Nest that aims its sentry by hand. <see cref="RescueRanger"/>: the Rescue Ranger 997,
    /// Pistol 22, Wrench 7; the Nest that repairs from range, standing back. <see cref="BattleEngie"/>: Frontier Justice 141 (else
    /// the Widowmaker 527), the Short Circuit 528, Wrench 7 (the Pomson 588 is a primary too, so it cannot pair with either; the
    /// round 2 plan listed it as the second gun). <see cref="Ninjaneer"/>: stock's items (the guide names none); pin-only.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Engineer,
    [
        new TFArchetype(Nest, 0.4f, [new(TFLoadoutSlot.Primary, [9]), new(TFLoadoutSlot.Secondary, [22]), new(TFLoadoutSlot.Melee, [7])]),
        new TFArchetype(Gunslinger, 0.25f, [new(TFLoadoutSlot.Primary, [141, 9]), new(TFLoadoutSlot.Secondary, [22]), new(TFLoadoutSlot.Melee, [142])]),
        new TFArchetype(Wrangler, 0.2f, [new(TFLoadoutSlot.Primary, [9]), new(TFLoadoutSlot.Secondary, [140]), new(TFLoadoutSlot.Melee, [7])]),
        new TFArchetype(RescueRanger, 0.15f, [new(TFLoadoutSlot.Primary, [997]), new(TFLoadoutSlot.Secondary, [22]), new(TFLoadoutSlot.Melee, [7])]),
        // Round 2's measure (cp_granary, six runs an arm, 76d59b9921): the Battle Engie inside the control's spread but below the
        // Nest on his own damage (385 against 780), deaths (2 against 0) and sentry kills (1.5 against 3.5): drawn rarely. The
        // Ninjaneer fails the Ready rule (the enemy's first capture at a median of 75 s against the control's 152 s): pin-only.
        new TFArchetype(BattleEngie, 0.05f, [new(TFLoadoutSlot.Primary, [141, 527]), new(TFLoadoutSlot.Secondary, [528]), new(TFLoadoutSlot.Melee, [7])]),
        new TFArchetype(Ninjaneer, 0f, [new(TFLoadoutSlot.Primary, [9]), new(TFLoadoutSlot.Secondary, [22]), new(TFLoadoutSlot.Melee, [7])]),
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

        /// <summary>
        /// He holds a work stand within this; he puts the dispenser down from this far to his gun's side, his box clear of the
        /// gun's (<see cref="TFEngineerSpots.WorkClearUnits"/>; round 1's 40 u stood him in it).
        /// </summary>
        public const float WorkArriveUnits = 20f, DispenserSideUnits = 64f;

        /// <summary>How far ahead of him a sentry's blueprint goes: its build box's half-diagonal (24 u by 24 u), a body's, and 4 u.</summary>
        public const float BuildDistanceUnits = 72f;

        /// <summary>A teleporter's: its build box is 28 u a side (TFObjectModels' 24 and the 4 u pad), so 78 u (CalculatePlacementPos).</summary>
        public const float TeleporterBuildDistanceUnits = 78f;

        /// <summary>Stock's exit retry (<c>exitRebuildInterval</c>, tf_bot_engineer_building.cpp:426): a lost or refused exit is not tried sooner.</summary>
        public const float ExitRetrySeconds = 30f;

        /// <summary>
        /// A life that starts this far from his entrance (his team's spawn moved up with a capture: on cp_granary red respawns by
        /// the middle once it holds it) finds it stale: no teammate passes it. He destroys it for a new one on this life's leg.
        /// Ours: stock never moves its entrance.
        /// </summary>
        public const float StaleEntranceUnits = 3000f;

        /// <summary>The team spawn points by his own whose walks out the entrance is put on (beside his own).</summary>
        public const int EntranceHomes = 12;

        /// <summary>He refills to this after his entrance (stock: "go back to our resupply cabinet after planting the teleporter entrance", IsAmmoFull's 200).</summary>
        public const int FullMetal = 200;

        /// <summary>A blueprint is ordered again at most this often, and clicked at most this often.</summary>
        public const float OrderSeconds = 1f, ClickSeconds = 0.6f;

        /// <summary>The view must be within this of the build yaw before a click: the blueprint goes where he faces.</summary>
        public const float AimDegrees = 8f;

        /// <summary>Stock's out-of-position rules (:326-364): a sentry farther than this from the point, quiet this long, comes down.</summary>
        public const float OutOfPositionUnits = 2500f, QuietSeconds = 10f;

        /// <summary>
        /// The nest holds through this many moves of his point after its gun stood (the review: following every capture, no gun
        /// reached level 3 on cp_granary); stock's 2,500 u is a walk, here a straight line.
        /// </summary>
        public const int HoldMoves = 2;

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

        /// <summary>Moved this far between two decides with no death, he was put somewhere (the round's restart sends him home): a respawn.</summary>
        public const float RespawnJumpUnits = 600f;

        /// <summary>A spawn this far from the one the candidates were worked out from works them out again.</summary>
        public const float SpotsHomeUnits = 1000f;

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
        /// The Short Circuit's orb (its alt-fire, CTFMechanicalArm::SecondaryAttack, tf_weapon_mechanical_arm.cpp:222-292) eats the
        /// enemy projectiles it passes (TFMechanicalArmOrb's CheckForProjectiles) and costs 65 metal (<c>tf_mecharm_orb_cost</c>,
        /// :48). The Battle Engie fires it at a rocket, pipe, sticky, flare or arrow of the enemy's he sees within
        /// <see cref="OrbReachUnits"/> closing on him or his gun, or a sticky lying within <see cref="OrbGuardUnits"/> of his gun;
        /// aimed where it will be in <see cref="OrbLeadSeconds"/>.
        /// </summary>
        public const float OrbReachUnits = 600f, OrbGuardUnits = 450f, OrbLeadSeconds = 0.1f;

        public const int OrbMetal = 65;

        /// <summary>The Battle Engie's forward exit this many times its farthest from the point (stock's travel, here straight) is taken down for a new one.</summary>
        public const float FrontExitStale = 1.2f;

        /// <summary>
        /// A flag nest's gun stands within this of his flag's home, best this far from it: in its room or at its door (the research's
        /// CTF row: "a sentry in or at the door of his own intelligence room"), where whoever takes the flag must pass it.
        /// </summary>
        public const float FlagNestReach = 700f, FlagNestBest = 300f;

        /// <summary>The Battle Engie's gun stands nearer the point it covers than the Nest's (<see cref="TFEngineerSpots.BestRange"/>, 600 u): the fight is there.</summary>
        public const float BattleBestRange = 400f;

        /// <summary>The Ninjaneer's spot is scored this much for being out of sight (the Nest's 0.75): behind the enemy's lines, hidden is the nest's life.</summary>
        public const float NinjaExposureWeight = 2f;

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
        private float _dispenserSide = 1f;
        private int _pointMoves;
        private TFSpotCensus _census;
        private AiVector _ownSpawn, _enemySpawn, _home, _lastPlace, _spotsHome, _theirHome, _ownFlagHome;
        private bool _ownFlagKnown, _flagNest;
        private bool _theirHomeKnown;
        private bool _homeKnown, _lastPlaceKnown;
        private Tick _spotSince;

        // This decide's read.
        private Tick _plannedAt = new(-1);
        private TFOwnBuilding _sentry, _dispenser, _entrance, _exit;
        private int _placing;
        private TFEngineerStep _step;
        private bool _fighting;

        // The placement in hand.
        private float _buildYaw, _sentryYaw, _offset;
        private int _tries;
        private Tick _orderAt = new(-100000), _clickAt = new(-100000), _dispenserRetry, _exitRetry;

        // The teleporter: his way out of spawn this life (stock's entrance leg), its spot, the nest exit's, and a refill after it.
        private bool _entranceLeg, _refill, _entranceStale;
        private AiVector _entranceAt;
        private TFSentrySpot? _entranceSpot, _exitSpot;
        private int _buildEntranceRow = -1, _buildExitRow = -1, _destroyExitRow = -1, _destroyEntranceRow = -1;

        // The Short Circuit's mark this decide: an enemy projectile closing on him or his gun.
        private bool _incoming;
        private AiVector _incomingAt;
        private long _orbs;

        // What his panel and body told him before.
        private int _lastHealth, _lastShells = -1, _lastKills;
        private Tick _hurtAt = new(-100000), _sentryFiredAt, _lifeAt, _countedAt;
        private bool _sentryWas, _dispenserWas, _entranceWas, _exitWas, _relocating, _respawned = true, _noSpot;
        private TFEngineerJob _lastJob;

        // The harness's counters.
        private long _sentriesBuilt, _dispensersBuilt, _sentryTicks, _levelThreeTicks, _refused, _relocations, _metalTrips, _sentryKills;
        private long _wrangleTicks, _bolts, _swings, _topLevel, _topProgress, _dryWork;
        private long _entrancesBuilt, _exitsBuilt, _teleporterTicks;
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

        /// <summary>Whether he plays the Gunslinger: a mini by the point, his own guns, no nest to sit in.</summary>
        private bool Mini => Archetype == TFEngineerClass.Gunslinger;

        /// <summary>Whether he plays the Battle Engie: a nest by the front, the exit first, his guns beside it.</summary>
        private bool Battle => Archetype == TFEngineerClass.BattleEngie;

        /// <summary>Whether he plays the Ninjaneer: a nest on the enemy's side of the point his team takes.</summary>
        private bool Ninja => Archetype == TFEngineerClass.Ninjaneer;

        /// <summary>Whether he builds by the point his team takes before his own (the attacking archetypes).</summary>
        private bool Attacks => Mini || Battle || Ninja;

        private bool _wrangling;
        private Tick _wrangleSeen;

        /// <summary>The Wrangler's kind when his kit has one in the secondary slot, else -1.</summary>
        private int WranglerKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Secondary, out Senses.TFKitItem item)
                                    && item.ItemClass == TFEngineerClass.LaserPointer ? item.WeaponKind : -1;

        /// <summary>The Short Circuit's kind when his kit has one in the secondary slot, else -1: its shots spend his building metal.</summary>
        private int ArmKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Secondary, out Senses.TFKitItem item)
                               && item.ItemClass == TFEngineerClass.MechanicalArm ? item.WeaponKind : -1;

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
                // By hand only while it helps (the review's C5): the enemy beyond the gun's reach, or the gun hurt. With the enemy
                // inside its reach and the gun whole the gun fights on its own, and a wrangled gun does not.
                _wrangling = D._targetPos.DistanceTo(_sentry.At) > TFEngineerSpots.SentryRange || share < WrangleUnderShare;
            }
            else if (Seconds(_wrangleSeen) > WrangleQuietSeconds)
            {
                _wrangling = false;
            }
        }

        /// <summary>The Short Circuit's mark (<see cref="OrbReachUnits"/>): the nearest enemy projectile closing on him or his gun.</summary>
        private void Incoming()
        {
            _incoming = false;
            if (!Battle || ArmKind < 0 || D._self.Metal < OrbMetal)
                return;
            float best = OrbReachUnits;
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                var type = (Senses.TFThingType)t.Type;
                if (t.Kind != ThingKind.Projectile || type is not (Senses.TFThingType.Rocket or Senses.TFThingType.Pipe or Senses.TFThingType.Sticky
                        or Senses.TFThingType.Flare or Senses.TFThingType.Arrow)
                    || (TeamRule.IsGameTeam(t.Team) && t.Team == D._self.Team))
                    continue;
                float d = t.Origin.DistanceTo(D._self.Origin);
                if (d > best)
                    continue;
                bool closing = t.Velocity.LengthSquared > 1f
                               && ((D._self.Origin - t.Origin).Dot(t.Velocity) > 0f || (_sentry.Exists && (_sentry.At - t.Origin).Dot(t.Velocity) > 0f));
                bool lying = type == Senses.TFThingType.Sticky && _sentry.Exists && t.Origin.DistanceTo(_sentry.At) < OrbGuardUnits;
                if (!closing && !lying)
                    continue;
                (best, _incoming, _incomingAt) = (d, true, t.Origin + t.Velocity * OrbLeadSeconds);
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
            _entrance = TFEngineerPanel.Read(hud, TFEngineerPanel.Entrance);
            if (_entrance.Exists)
                _entranceAt = _entrance.At;
            _exit = TFEngineerPanel.Read(hud, TFEngineerPanel.Exit);
            _placing = (int)(hud?.Meter(TFEngineerPanel.Placing, 0f) ?? 0f);
            Note();
            ReadPoint();
            Relocate();
            if (_dispenser.Exists)
                _dispenserRetry = D._now + (int)(TFEngineerNest.DispenserRetrySeconds / D._tick);
            if (_exit.Exists)
                _exitRetry = D._now + (int)(ExitRetrySeconds / D._tick);
            Leg();
            _step = _hasPoint || _sentry.Exists
                ? TFEngineerNest.Next(in _sentry, in _dispenser, D._self.Metal, D._now >= _dispenserRetry && !UnderAttack, upgrades: !Mini,
                                      sentryCost: Mini ? TFEngineerNest.MiniSentryCost : TFEngineerNest.SentryCost, teleporters: Teleporters())
                : default;
            // Stock's refill after the entrance: back to the cabinet by his spawn before he walks to build.
            if (_refill && _step.Job == TFEngineerJob.Build && _step.Kind == TFEngineerPanel.Sentry && D._self.Metal < FullMetal)
                _step = new TFEngineerStep(TFEngineerJob.Metal, TFEngineerPanel.Sentry);
            if (_step.Job == TFEngineerJob.Metal && _lastJob != TFEngineerJob.Metal)
                _metalTrips++;
            // Back from a trip for metal: the walk to the spot starts now, not when he first chose it.
            if (_lastJob == TFEngineerJob.Metal && _step.Job == TFEngineerJob.Build)
                _spotSince = D._now;
            // A gun to build with no spot to build it at (no candidate at the point): the nest gives way to the general play.
            _noSpot = _step is { Job: TFEngineerJob.Build, Kind: TFEngineerPanel.Sentry } && SentrySpot() is null;
            _lastJob = _step.Job;
            _fighting = ShouldFight();
            Incoming();
            Wrangle();
            if (_wrangling)
                _wrangleTicks += DecideSchedule.Period;
        }

        /// <summary>What changed on his panel and his body since the last decide: hurt, the gun firing, built, the counters.</summary>
        private void Note()
        {
            // A respawn: after a death, or a jump of his own place with no death into his team's respawn room (the round's
            // restart teleports him home, the review's C6): the spot's patience and his home start again.
            if (_homeKnown && _lastPlaceKnown && _lastPlace.DistanceTo(D._self.Origin) > RespawnJumpUnits && InOwnSpawnRoom(D._self.Origin))
                _respawned = true;
            (_lastPlace, _lastPlaceKnown) = (D._self.Origin, true);
            if (_respawned)
            {
                (_lifeAt, _respawned, _spotSince, _home, _homeKnown) = (D._now, false, D._now, D._self.Origin, true);
                // Stock's first job each life, from his spawn room (the leg is the walk out of it).
                (_entranceLeg, _entranceSpot, _refill) = (InOwnSpawnRoom(D._self.Origin), null, false);
                _entranceStale = _entranceLeg && _entranceWas && _entranceAt.DistanceTo(D._self.Origin) > StaleEntranceUnits;
            }
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
                (_tries, _offset, _relocating, _pointMoves) = (0, 0f, false, 0);
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
            if (_entrance.Exists && !_entranceWas)
                (_entrancesBuilt, _tries, _offset, _refill) = (_entrancesBuilt + 1, 0, 0f, _entranceLeg);
            if (_exit.Exists && !_exitWas)
                (_exitsBuilt, _tries, _offset, _exitSpot) = (_exitsBuilt + 1, 0, 0f, null);
            if (_entrance.Exists && _entrance.Built && _exit.Exists && _exit.Built)
                _teleporterTicks += Math.Max(0, D._now - _countedAt);
            (_sentryWas, _dispenserWas, _entranceWas, _exitWas, _countedAt) = (_sentry.Exists, _dispenser.Exists, _entrance.Exists, _exit.Exists, D._now);
        }

        /// <summary>
        /// Stock's entrance leg (CTFBotEngineerBuildTeleportEntrance): each life in his spawn room starts on it; it ends once he is farther than
        /// <see cref="TFEngineerSpots.EntranceWalkUnits"/> from where he came into the world, the entrance stands, or none can be
        /// placed. The refill after the entrance ends with a full pocket or the leg.
        /// </summary>
        private void Leg()
        {
            if (_entranceStale && !_entrance.Exists)
                _entranceStale = false;
            if (_entranceLeg && !_entranceStale && (_entrance.Exists || D._self.Origin.DistanceTo(_home) > TFEngineerSpots.EntranceWalkUnits))
                _entranceLeg = false;
            if (_refill && (D._self.Metal >= FullMetal || D._self.Origin.DistanceTo(_home) > TFEngineerSpots.EntranceWalkUnits))
                _refill = false;
        }

        /// <summary>
        /// The teleporter ends he wants now: the entrance on his leg out of spawn, with a spot for it; the nest's exit once his gun
        /// stands and his entrance does, stock's 30 s retry done, nothing hurting him.
        /// </summary>
        private TFTeleporterWant Teleporters()
        {
            // The Gunslinger's minis move with every fight: no teleporter (round 1's play, unchanged).
            if (Mini)
                return default;
            bool entrance = _entranceLeg && !_entrance.Exists && !UnderAttack && EntranceSpot() is not null;
            bool ready = !_exit.Exists && _entrance.Exists && D._now >= _exitRetry && !UnderAttack;
            // The Battle Engie's exit goes forward first (stock's offensive exit); the nests' behind the gun once it stands.
            bool exit = Battle ? ready && FrontExit() is not null : ready && _sentry.Exists && _sentry.Built && ExitSpot() is not null;
            return new TFTeleporterWant(entrance, exit, ExitFirst: Battle);
        }

        private int _frontKey = int.MinValue;
        private TFSentrySpot? _frontExit;

        /// <summary>The Battle Engie's forward exit for his point (<see cref="TFEngineerSpots.FrontExitSpot"/>), worked out once a point and entrance.</summary>
        private TFSentrySpot? FrontExit()
        {
            if (_frontKey == _pointKey)
                return _frontExit;
            if (D._graph is not { } graph || !_hasPoint)
                return null;
            int own = D._self.Team, enemy = own == (int)TFTeam.Red ? (int)TFTeam.Blue : (int)TFTeam.Red;
            _frontExit = TFEngineerSpots.FrontExitSpot(graph, D._memory.World.Sight, _point, own, D._map.TeamZones, SpawnOf(own), SpawnOf(enemy), _entrance.At);
            _frontKey = _pointKey;
            if (_frontExit is null)
                _exitRetry = D._now + (int)(ExitRetrySeconds / D._tick);
            return _frontExit;
        }

        /// <summary>The entrance's spot on his way out (worked out once a leg), toward his sentry's spot or his point.</summary>
        private TFSentrySpot? EntranceSpot()
        {
            if (_entranceSpot is { } known || D._graph is not { } graph || !_hasPoint)
                return _entranceSpot;
            // His team's way out: his own walk and those from his team's spawn points in his room.
            var homes = new List<AiVector> { _home };
            foreach (TeamSpawn sp in D._map.TeamSpawns)
            {
                if (sp.Team == D._self.Team && sp.Position.DistanceTo(_home) < TFEngineerSpots.EntranceWalkUnits && homes.Count < EntranceHomes)
                    homes.Add(sp.Position);
            }

            _entranceSpot = TFEngineerSpots.EntranceSpot(graph, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(homes), _spot?.At ?? _point, D._self.Team, D._map.TeamZones);
            if (_entranceSpot is null)
                _entranceLeg = false;
            return _entranceSpot;
        }

        /// <summary>The nest exit's spot behind his gun (worked out until one is built there).</summary>
        private TFSentrySpot? ExitSpot()
        {
            if (_exitSpot is { } known && known.At.DistanceTo(_sentry.At) < 400f)
                return known;
            if (D._graph is not { } graph)
                return null;
            Span<AiVector> boxes = stackalloc AiVector[2];
            int n = 0;
            boxes[n++] = _sentry.At;
            if (_dispenser.Exists)
                boxes[n++] = _dispenser.At;
            _exitSpot = TFEngineerSpots.NestExitSpot(graph, _sentry.At, _sentryYaw, boxes[..n], D._self.Team, D._map.TeamZones);
            if (_exitSpot is null)
                _exitRetry = D._now + (int)(ExitRetrySeconds / D._tick);
            return _exitSpot;
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
                    // His own flag: where its home is, as he last saw it there (taken, it is still the place he defends).
                    if (o.State == ObjectiveState.Home)
                        (_ownFlagHome, _ownFlagKnown) = (o.Position, true);
                    flag = _ownFlagKnown ? o with { Position = _ownFlagHome } : o;
                }
                else if (o.Kind == ObjectiveKind.Carry)
                {
                    // The enemy's flag: where its home is, as he last saw it there (a carried or dropped flag keeps its home).
                    if (o.State == ObjectiveState.Home)
                        (_theirHome, _theirHomeKnown) = (o.Position, true);
                    theirs = _theirHomeKnown ? o with { Position = _theirHome } : null;
                }
            }

            // The battle Engineer attacks: the point his team may take comes first; on a flag map the enemy flag's home, carried
            // or not (the review's C4: his point went home when his team took the flag).
            // On a flag map the nest defends his own flag where it lies at home (stock builds by the capture zone, with Valve's note
            // "should defend location of our team's flag", tf_bot_engineer_move_to_build.cpp:68-79): live on ctf_2fort a gun by the
            // capture zone killed nobody in nine runs while the enemy took the flag.
            Objective? pick = Ninja ? Beyond(objectives, take, team) ?? take ?? defend ?? theirs ?? zone ?? flag
                : Attacks ? take ?? defend ?? theirs ?? zone ?? flag : defend ?? take ?? flag ?? zone;
            _flagNest = !Attacks && defend is null && take is null && flag is not null;
            _hasPoint = pick is not null;
            if (pick is { } p)
            {
                if (p.Id != _pointKey && _pointKey != int.MinValue && _sentry.Exists)
                    _pointMoves++;
                (_point, _pointKey) = (p.Position, p.Id);
            }
        }

        /// <summary>
        /// The Ninjaneer's point: the enemy's next point behind the one his team takes (the nearest to it the enemy owns, locked or
        /// not), where their reinforcements pass on their way to the front; null for none (the one his team takes, then).
        /// </summary>
        private static Objective? Beyond(ObjectiveKnowledge objectives, Objective? take, int team)
        {
            if (take is not { } front)
                return null;
            Objective? best = null;
            float bd = float.PositiveInfinity;
            foreach (Objective o in objectives.All)
            {
                float d = o.Position.DistanceTo(front.Position);
                if (o.Kind == ObjectiveKind.Capture && o.Id != front.Id && o.OwnerTeam != team && TeamRule.IsGameTeam(o.OwnerTeam) && d < bd)
                    (bd, best) = (d, o);
            }

            return best;
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

            bool away = Mini ? Flat(_sentry.At, _point) > MiniOutOfPositionUnits
                               : Flat(_sentry.At, _point) > OutOfPositionUnits && Seconds(_sentryFiredAt) >= QuietSeconds && _pointMoves >= HoldMoves;
            if (!_hasPoint || _relocating || !away)
                return;
            // Not down before a spot at the new point is known (the review's C3: a working nest came down and the finder had
            // none to give).
            if (D._graph is null || Spots().Count == 0)
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
            if (target.ClassSeen == (int)TFClass.Spy || ((Mini || Battle) && D._targetDist < RetaliateUnits))
                return true;
            if (!_sentry.Exists || !_sentry.Built)
                return D._targetDist < BuildingThreatUnits || !Nesting;
            return D._targetDist < CloseThreatUnits && D._self.Origin.DistanceTo(_sentry.At) > KeepToSentryUnits;
        }

        /// <summary>
        /// Whether he has a nest to look after (a job by a point, or a sentry standing). The battle Engineer only while his mini is
        /// to build, to finish, to unsap or to take down, near it: else he goes with his team.
        /// </summary>
        private bool Nesting => _step.Job != TFEngineerJob.None && !_noSpot
                                && (!Mini || _relocating || _step.Job == TFEngineerJob.Build || _step.Job == TFEngineerJob.Metal
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
                case TFEngineerJob.Build when _step.Kind == TFEngineerPanel.Entrance:
                    if (EntranceSpot() is not { } way)
                        return false;
                    _buildYaw = way.Yaw;
                    return PadStand(in way, TFEngineerPanel.Entrance, "to the entrance spot");
                case TFEngineerJob.Build when _step.Kind == TFEngineerPanel.Exit:
                    if ((Battle ? FrontExit() : ExitSpot()) is not { } pad)
                        return false;
                    _buildYaw = pad.Yaw;
                    return PadStand(in pad, TFEngineerPanel.Exit, "to the exit spot");
                case TFEngineerJob.Build:
                    // Beside his gun, facing back from the point: the dispenser lands behind and to the side of it, about 87 u off,
                    // so a stand between the two is inside the dispenser's 64 u reach and the wrench's reach of the gun.
                    _buildYaw = _sentryYaw + 180f;
                    if (DispenserStand() is not { } beside)
                    {
                        // No floor by the gun for it (a ledge): the gun's upgrade meanwhile, and stock's retry.
                        _dispenserRetry = D._now + (int)(TFEngineerNest.DispenserRetrySeconds / D._tick);
                        return false;
                    }

                    return Stand(beside, "to the dispenser spot", WorkArriveUnits);
                case TFEngineerJob.Work when Ranged:
                    return Stand(RangedStand(), "covering the nest", WorkUnits);
                case TFEngineerJob.Work:
                    return Stand(WorkStand(), "working the nest", WorkArriveUnits, (_step.Kind == TFEngineerPanel.Dispenser ? _dispenser : _sentry).At);
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
        private AiVector BuildStand(in TFSentrySpot spot, float distance = BuildDistanceUnits)
        {
            AiVector at = spot.At - TFEngineerSpots.Forward(spot.Yaw) * distance;
            // Floor at the spot's own height (a cell of the floor below is no stand: live on ctf_2fort an exit's stand lay off its
            // floor, and he walked at it for 56 s).
            return D._graph is { } g && TFEngineerSpots.FloorAt(g, at, spot.At.Z) is { } z ? new AiVector(at.X, at.Y, z) : spot.At;
        }

        /// <summary>
        /// The walk to a teleporter end's stand, with the sentry spot's patience (<see cref="SpotPatienceSeconds"/> and the walk at
        /// <see cref="PatienceSpeed"/> from where he set off): not reached in it (a way the map does not give, a rail he cannot
        /// pass), the pad is given up as five refused clicks give it up (the review's fix 5: a Battle Engie could walk at an
        /// unreachable exit for a life and build no gun; a Nest's exit stand could hold the upgrade's rung).
        /// </summary>
        private bool PadStand(in TFSentrySpot pad, int kind, string why)
        {
            AiVector stand = BuildStand(pad, TeleporterBuildDistanceUnits);
            if (_padKind != kind || _padAt.DistanceTo(stand) > StandMovedUnits)
                (_padKind, _padAt, _padSince, _padFrom) = (kind, stand, D._now, D._self.Origin);
            if (Seconds(_padSince) > SpotPatienceSeconds + _padFrom.DistanceTo(stand) / PatienceSpeed)
            {
                GiveUpPad(kind);
                _padKind = -1;
                return false;
            }

            return Stand(stand, why, StandUnits);
        }

        /// <summary>A teleporter end given up: an entrance is left for this life; an exit waits stock's 30 s retry and another spot.</summary>
        private void GiveUpPad(int kind)
        {
            if (kind == TFEngineerPanel.Entrance)
                _entranceLeg = false;
            else
                (_exitRetry, _exitSpot, _frontKey) = (D._now + (int)(ExitRetrySeconds / D._tick), null, int.MinValue);
        }

        private int _padKind = -1;
        private AiVector _padAt, _padFrom;
        private Tick _padSince;

        /// <summary>
        /// Where he stands to put the dispenser down (<see cref="TFEngineerSpots.DispenserStand"/>): to his gun's side, the side
        /// last refused second; null when neither side has floor at the gun's height for him and the dispenser.
        /// </summary>
        private AiVector? DispenserStand()
            => D._graph is { } g ? TFEngineerSpots.DispenserStand(g, _sentry.At, _sentryYaw, _dispenserSide, DispenserSideUnits, BuildDistanceUnits) : _sentry.At;

        /// <summary>
        /// Walks to <paramref name="at"/> and holds there within <paramref name="arrive"/> on its floor: a stand under a ledge
        /// is not the stand on it. Live on cp_granary, flat arrival held him 256 u under his nest for 74 s, no wrench reaching it.
        /// </summary>
        private bool Stand(AiVector at, string why, float arrive = StandUnits * 0.5f, AiVector? workOn = null)
        {
            if (_standAt.DistanceTo(at) > StandMovedUnits)
                (_standAt, _standHeld) = (at, false);
            // Held up close (a rail or a corner the walk cannot pass: live on cp_granary's mid ledge he jumped at a dispenser stand
            // 50 u off for 52 s) for the patience, he works from where he stands. The patience runs from when he first came that
            // close, not from when the stand was chosen (else any long walk ended 96 u short: the review's 4b, the 2fort gun 83 u
            // off its spot in six runs of six); a work stand counts only inside the wrench's swing of its building (4a).
            bool near = TFEngineerSpots.Arrived(D._self.Origin, at, StandCloseUnits)
                        && (workOn is not { } building || D._self.Origin.DistanceTo(building) <= SwingReachUnits);
            if (!near)
                _standHeld = false;
            else if (!_standHeld)
                (_standHeld, _standSince) = (true, D._now);
            bool close = near && Seconds(_standSince) > StandPatienceSeconds;
            if (!TFEngineerSpots.Arrived(D._self.Origin, at, arrive) && !close)
                D.GoTo(at, MoveSpeed.Run);
            D._verdict = why;
            return true;
        }

        /// <summary>
        /// A stand he has been within <see cref="StandCloseUnits"/> of for this long is taken as reached; a stand that moves this far
        /// is a new one.
        /// </summary>
        public const float StandPatienceSeconds = 6f, StandCloseUnits = 96f, StandMovedUnits = 16f;

        /// <summary>The wrench's swing gate (<see cref="Swing"/>: <see cref="WorkUnits"/> and its slack).</summary>
        public const float SwingReachUnits = WorkUnits * 1.3f;

        private AiVector _standAt;
        private Tick _standSince;
        private bool _standHeld;

        private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>
        /// Where he stands to work (stock's UpgradeAndMaintainBuildings, :85-147): by the job's building, within the wrench's
        /// reach of it, his box clear of every building's (<see cref="TFEngineerSpots.WorkStand"/>); when his dispenser stands by
        /// the gun, within its reach too, which feeds him the metal the upgrades spend. Behind the gun from the point first.
        /// </summary>
        private AiVector WorkStand()
        {
            TFOwnBuilding target = _step.Kind == TFEngineerPanel.Dispenser ? _dispenser : _sentry;
            AiVector? feeder = _sentry.Exists && _dispenser.Exists && _sentry.At.DistanceTo(_dispenser.At) < TFEngineerSpots.WorkReachUnits + TFEngineerSpots.NestFeedUnits ? _dispenser.At : null;
            Span<AiVector> boxes = stackalloc AiVector[2];
            int n = 0;
            if (_sentry.Exists)
                boxes[n++] = _sentry.At;
            if (_dispenser.Exists)
                boxes[n++] = _dispenser.At;
            AiVector back = _hasPoint ? target.At - _point : D._self.Origin - target.At;
            return TFEngineerSpots.WorkStand(D._graph, target.At, feeder, boxes[..n], back, D._self.Origin);
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
            Spots();

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

        /// <summary>Whether <paramref name="at"/> is inside one of his team's respawn rooms (map knowledge).</summary>
        private bool InOwnSpawnRoom(AiVector at)
        {
            foreach (TeamZone z in D._map.TeamZones)
            {
                if (z.Kind == ZoneKind.RespawnRoom && z.Team == D._self.Team && z.Contains(at + new AiVector(0f, 0f, 16f)))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The candidates at his point, worked out again when the point changes or he comes into the world somewhere else
        /// (the sides are measured from his spawn: the review's C3, a forward spawn's list kept from the first).
        /// </summary>
        private List<TFSentrySpot> Spots()
        {
            if (D._graph is not { } graph)
                return _spots;
            if (_spotsKey == _pointKey && _spotsHome.DistanceTo(_homeKnown ? _home : D._self.Origin) <= SpotsHomeUnits)
                return _spots;
            int own = D._self.Team, enemy = own == (int)TFTeam.Red ? (int)TFTeam.Blue : (int)TFTeam.Red;
            (_ownSpawn, _enemySpawn) = (SpawnOf(own), SpawnOf(enemy));
            ICellSight? sight = D._memory.World.Sight;
            _spots = Mini ? TFEngineerSpots.Candidates(graph, sight, _point, own, D._map.TeamZones, _ownSpawn, _enemySpawn, out _census, MiniReach, MiniBestRange)
                : Battle ? TFEngineerSpots.Candidates(graph, sight, _point, own, D._map.TeamZones, _ownSpawn, _enemySpawn, out _census, best: BattleBestRange)
                // The Ninjaneer's by the enemy's next point (ReadPoint's Beyond), the least seen of them: behind their lines.
                : Ninja ? TFEngineerSpots.Candidates(graph, sight, _point, own, D._map.TeamZones, _ownSpawn, _enemySpawn, out _census, exposure: NinjaExposureWeight, ownSide: false)
                : _flagNest ? TFEngineerSpots.Candidates(graph, sight, _point, own, D._map.TeamZones, _ownSpawn, _enemySpawn, out _census, FlagNestReach, FlagNestBest)
                : TFEngineerSpots.Candidates(graph, sight, _point, own, D._map.TeamZones, _ownSpawn, _enemySpawn, out _census);
            (_spotsKey, _spotsHome) = (_pointKey, _homeKnown ? _home : D._self.Origin);
            _failed.Clear();
            _spot = null;
            return _spots;
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
            if (_incoming && ArmKind >= 0 && D._self.ActiveWeapon == ArmKind)
            {
                D._look = new LookIntent(LookKind.Point, _incomingAt, ActorId.None, AimPoint.Body, 0f, 0);
                D._action = new ActionIntent(ActionKind.AltFire, FireStyle.Tap, 0, 0);
                _orbs++;
                return;
            }

            // Wrangled, the sentry fires where he aims (TFSentryWrangle): the general fire at the target is the gun's. On his way to
            // build, a gun in hand, he shoots what he sees as he walks.
            if (_fighting || (_wrangling && D._self.ActiveWeapon == WranglerKind) || ShootsOnTheWay)
                D.GeneralEngage();
        }

        /// <summary>Walking to build (not yet at his stand, no blueprint out) with a gun in hand and an enemy in sight.</summary>
        private bool ShootsOnTheWay => D._targetSighted && _placing == 0 && D._hasGoal && _step.Job is TFEngineerJob.Build or TFEngineerJob.Metal
                                       && D._self.ActiveWeapon != WranglerKind && D._self.ActiveWeapon != ArmKind
                                       && (D._self.ActiveWeapon == D.PrimaryKind || D._self.ActiveWeapon == D.SecondaryKind);

        /// <summary>The nest's hands after the fire: the blueprint ordered and clicked down, the wrench swung at the job's building, an out-of-place nest taken down.</summary>
        public override void OneShots()
        {
            Plan();
            if (_fighting || _wrangling || D._root.Chosen?.Name != ClassBranch || (_incoming && ArmKind >= 0))
                return;
            if (_relocating)
            {
                TakeDown();
                return;
            }

            if (_entranceStale && _entrance.Exists && Seconds(_orderAt) >= OrderSeconds
                && (_destroyEntranceRow = RowOf(_destroyEntranceRow, "destroy_teleporter_entrance")) >= 0)
            {
                D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(_destroyEntranceRow, 0), ++D._oneShot);
                _orderAt = D._now;
                return;
            }

            // Stock's offensive exit too far from the point is destroyed for a new one (tf_bot_engineer_move_to_build.cpp:363-390).
            if (Battle && _exit.Exists && _hasPoint && Flat(_exit.At, _point) > TFEngineerSpots.FrontExitFar * FrontExitStale && Seconds(_orderAt) >= OrderSeconds
                && (_destroyExitRow = RowOf(_destroyExitRow, "destroy_teleporter_exit")) >= 0)
            {
                D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(_destroyExitRow, 0), ++D._oneShot);
                (_orderAt, _frontKey) = (D._now, int.MinValue);
                return;
            }

            switch (_step.Job)
            {
                case TFEngineerJob.Build:
                    Place(_step.Kind);
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
        private void Place(int kind)
        {
            TFObjectType type = kind switch
            {
                TFEngineerPanel.Sentry => TFObjectType.Sentrygun,
                TFEngineerPanel.Dispenser => TFObjectType.Dispenser,
                _ => TFObjectType.Teleporter,
            };
            if (D._hasGoal)
                return;
            if (_placing != (int)type + 1)
            {
                if (Seconds(_orderAt) < OrderSeconds)
                    return;
                int row = kind switch
                {
                    TFEngineerPanel.Sentry => _buildSentryRow = RowOf(_buildSentryRow, "build_sentry"),
                    TFEngineerPanel.Dispenser => _buildDispenserRow = RowOf(_buildDispenserRow, "build_dispenser"),
                    TFEngineerPanel.Entrance => _buildEntranceRow = RowOf(_buildEntranceRow, "build_teleporter_entrance"),
                    _ => _buildExitRow = RowOf(_buildExitRow, "build_teleporter_exit"),
                };
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
                    // Given up: a sentry's spot is failed for another; a dispenser waits stock's retry (10 s) with the gun's
                    // upgrade done meanwhile, and goes to the gun's other side next time; an entrance is left for this life,
                    // an exit waits stock's 30 s retry and another spot.
                    if (kind == TFEngineerPanel.Sentry && _spot is { } failed)
                    {
                        _failed.Add(failed.Cell);
                        _spot = null;
                    }
                    else if (kind == TFEngineerPanel.Dispenser)
                        (_dispenserRetry, _dispenserSide) = (D._now + (int)(TFEngineerNest.DispenserRetrySeconds / D._tick), -_dispenserSide);
                    else
                        GiveUpPad(kind);
                    (_tries, _offset) = (0, 0f);
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
            if (!b.Exists || D._self.Origin.DistanceTo(b.At) > SwingReachUnits)
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

            if (_incoming && ArmKind >= 0 && _placing == 0)
            {
                if (D._self.ActiveWeapon != ArmKind)
                    D.Select(ArmKind);
                return;
            }

            if (_fighting)
            {
                int primary = D.PrimaryKind, secondary = WranglerKind >= 0 || ArmKind >= 0 ? -1 : D.SecondaryKind;
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

            // A blueprint out for the job in hand stays out; one the job no longer wants (the gun hurt while the dispenser's was
            // out) is put away for the wrench, which takes the builder out of his hands (the review's C1).
            if (_placing > 0 && (_step.Job != TFEngineerJob.Work || D._root.Chosen?.Name != ClassBranch))
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
            into.Add(new("engineer.nobuild_zones", D._map.TeamZones.Count(z => z.Kind == ZoneKind.NoBuild)));
            into.Add(new("engineer.entrances_built", _entrancesBuilt));
            into.Add(new("engineer.orbs", _orbs));
            into.Add(new("engineer.exits_built", _exitsBuilt));
            into.Add(new("engineer.teleporter_up_s", (long)(_teleporterTicks * D._tick)));
            into.Add(new("engineer.sentry_x", (long)_sentry.At.X));
            into.Add(new("engineer.sentry_y", (long)_sentry.At.Y));
        }
    }
}
