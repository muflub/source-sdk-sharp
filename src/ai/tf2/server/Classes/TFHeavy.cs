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
/// The Heavy's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Heavy lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFHeavyClass
{
    /// <summary>
    /// Whether the composition fields the Heavy (C.4): yes. On cp_granary (3 runs of 300 s, 8 bots, 4 pinned) Standard Heavies
    /// stood on the objective first at a mean 77.0 s against the all-Soldier control's 85.0 s, with more kills and damage.
    /// </summary>
    public const bool Ready = true;

    /// <summary>
    /// The Heavy's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only. None: the spin is the second
    /// trigger held from the hands, the eat a select and a press.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [];

    /// <summary>The stock Heavy's archetype's name (the plan's "Pocketed").</summary>
    public const string Standard = "Standard";

    /// <summary>The Tomislav's (silent, quicker spin-up), Natascha's (the slow) and the Brass Beast's (the hold) archetypes' names.</summary>
    public const string Ambusher = "Ambusher", Anchor = "Anchor", Siege = "Siege";

    /// <summary>
    /// The Heavy's archetypes (C.3.1; research heavy.md §2, §4.4, ship order A-D), drawn by the bot's seed and weight (the
    /// research's attack weights 4/3/2/1). Standard: the stock Minigun (15, standing for its reskins), the Sandvich (42), the
    /// Fists (5). Ambusher: the Tomislav (424), the Sandvich, the Killing Gloves of Boxing (43). Anchor: Natascha (41), the
    /// Shotgun (11), the Fists. Siege: the Brass Beast (312), the Shotgun, the Fists.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.HeavyWeapons,
    [
        new TFArchetype(Standard, 4f, [new(TFLoadoutSlot.Primary, [15]), new(TFLoadoutSlot.Secondary, [42]), new(TFLoadoutSlot.Melee, [5])]),
        new TFArchetype(Ambusher, 3f, [new(TFLoadoutSlot.Primary, [424]), new(TFLoadoutSlot.Secondary, [42]), new(TFLoadoutSlot.Melee, [43])]),
        new TFArchetype(Anchor, 2f, [new(TFLoadoutSlot.Primary, [41]), new(TFLoadoutSlot.Secondary, [11]), new(TFLoadoutSlot.Melee, [5])]),
        new TFArchetype(Siege, 1f, [new(TFLoadoutSlot.Primary, [312]), new(TFLoadoutSlot.Secondary, [11]), new(TFLoadoutSlot.Melee, [5])]),
    ]);

    /// <summary>
    /// How an archetype plays the minigun (research §4.4), each a change on Standard's numbers: the reach of fire at will, the
    /// spin's hold after a sighting, the pre-spin's reach and how long a belief keeps it, the band past which it closes (none:
    /// never closes while spun), the weight of a chase after a target out of sight, its retreat share, and its targets' factors
    /// by kind (a Scout, a target in the air or a Scout for the slow's sake).
    /// </summary>
    public readonly record struct Way(float FullReach, float SpinHold, float PreSpinReach, float PreSpinBelief, float BandFar, float ChaseWeight,
                                      float HurtShare, float ScoutFactor, float MobileFactor);

    /// <summary>The way <paramref name="archetype"/> plays (Standard's for an unknown name).</summary>
    public static Way WayOf(string archetype) => archetype switch
    {
        // Silent and quick to spin: spins at a farther belief and for longer after it, lets go sooner, fights a little further
        // out (the tighter spread), and leaves the Scouts (research: "slow targets rather than fast Scouts").
        Ambusher => new Way(1200f, 1f, 1200f, 10f, BandFar, 1f, TFDecider.HurtShare, 0.7f, 1f),
        // The slow lands at range and stops a charge: fights further out, ranks the mobile first, and with the
        // spun resistance under half health retreats later (0.3, stock's critical ratio, tf_bot_get_health.cpp:14).
        Anchor => new Way(1200f, SpinHoldSeconds, PreSpinReach, PreSpinBeliefSeconds, 900f, 1f, 0.3f, 1.4f, 1.4f),
        // A hold at 44 u/s spun: spins long, closes only to its reach of fire at will (a target further off it could neither hurt
        // nor burst at, and would stand before for ever: review finding 4), never chases a target out of sight (its fight's
        // score goes to nothing then, so another branch takes it at once).
        Siege => new Way(FullReach, 6f, PreSpinReach, PreSpinBeliefSeconds, FullReach, 0f, TFDecider.HurtShare, 1f, 1f),
        _ => new Way(FullReach, SpinHoldSeconds, PreSpinReach, PreSpinBeliefSeconds, BandFar, 1f, TFDecider.HurtShare, 1f, 1f),
    };

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.HeavyWeapons, Ready, Abilities(), Archetypes());

    /// <summary>The minigun's and the lunchbox's item class, as the kit names them.</summary>
    public const string MinigunClass = "tf_weapon_minigun", LunchboxClass = "tf_weapon_lunchbox";

    /// <summary>
    /// The minigun's damage falls to its floor (50 %) at 1,024 u (stock tf_gamerules.cpp:6548-6566): inside it the Heavy fires
    /// at will; out to <see cref="BurstReach"/> only bursts at a slow target; past it, never (research §4.3).
    /// </summary>
    public const float FullReach = 1024f, BurstReach = 1600f;

    /// <summary>A burst past <see cref="FullReach"/>: this long on, out of every <see cref="BurstEverySeconds"/>; only at a target slower than <see cref="SlowTarget"/> u/s.</summary>
    public const float BurstOnSeconds = 0.5f, BurstEverySeconds = 1.5f, SlowTarget = 100f;

    /// <summary>The spin is held this long after the last enemy seen (stock tf_bot_behavior.cpp:1284-1291's <c>spinTime = 3.0f</c>).</summary>
    public const float SpinHoldSeconds = 3f;

    /// <summary>A lost target believed within this, last perceived within <see cref="PreSpinBeliefSeconds"/>, is spun for before it shows (research §4.3's pre-spin).</summary>
    public const float PreSpinReach = 800f, PreSpinBeliefSeconds = 6f;

    /// <summary>The Heavy's fight band: it closes past this, and never backs off a player for being near (stock's desired range for spray weapons is 500, tf_bot.cpp:3213).</summary>
    public const float BandFar = 550f;

    /// <summary>The minigun's pool (stock's 200 for the Heavy): under <see cref="AmmoLow"/> it holds no spin (stock's <c>!IsAmmoLow()</c>) and goes for ammo in a lull.</summary>
    public const int MinigunPool = 200, AmmoLow = 50;

    /// <summary>Ammo is gone for within this; a lull is no enemy seen for <see cref="AmmoLullSeconds"/>.</summary>
    public const float AmmoReach = 1500f, AmmoLullSeconds = 1f;

    /// <summary>
    /// The ammo trip's utility: over the run for the enemy's flag (0.7-0.9 before the attacker's drive) and a remembered
    /// target's fight, since a Heavy under a quarter of his rounds cannot hold a fight: stock's bot goes for ammo first too.
    /// </summary>
    public const float AmmoUtility = 0.95f;

    /// <summary>A friendly dispenser seen within this long is an ammo place too (where the bot saw it; it may have been moved or lost since).</summary>
    public const float DispenserMemorySeconds = 60f;

    /// <summary>An ammo place not reached and paid in this long is given up for <see cref="AmmoSkipSeconds"/>.</summary>
    public const float AmmoPatienceSeconds = 12f, AmmoSkipSeconds = 30f;

    /// <summary>
    /// The eat (research §4.3): the Sandvich under this share of health, with no enemy seen for <see cref="EatQuietSeconds"/> and
    /// no target believed within <see cref="EatClearUnits"/>; never at full health (stock eats whenever it can, tf_bot.cpp:4587-4598).
    /// </summary>
    public const float EatShare = 0.55f, EatQuietSeconds = 1.5f, EatClearUnits = 600f;

    /// <summary>The chocolate lunchboxes (the Dalokohs Bar, the Fishcake) overheal, so they are eaten under this; the Second Banana heals 200, under <see cref="BananaShare"/>.</summary>
    public const float ChocolateShare = 0.85f, BananaShare = 0.5f;

    /// <summary>The eat is pressed until the taunt shows on the HUD (a voice line takes a decide's press), at most this long.</summary>
    public const float EatPressSeconds = 2f;

    /// <summary>
    /// A lunchbox taken out with nothing in it (the meter still filling), or one the game would not take out in
    /// <see cref="EatSelectSeconds"/>, is not tried again for this long.
    /// </summary>
    public const float LunchboxEmptySeconds = 15f, EatSelectSeconds = 2f;

    /// <summary>
    /// The seconds a lunchbox takes to come back after an eat: its <c>item_meter_charge_rate</c> (the Sandvich's 30, the
    /// Dalokohs Bar's 10) stretched by <c>mult_item_meter_charge_rate</c> (TFItemMeter's rule), 30 when the kit names none.
    /// </summary>
    public static float RefillSeconds(Senses.TFKitItem lunchbox)
        => lunchbox.Attribute(TFItemMeter.ChargeRateAttribute, SandvichRefillSeconds)
           * lunchbox.Attribute(TFItemMeter.ChargeRateModAttribute, 1f);

    /// <summary>The Sandvich's refill, stock's <c>item_meter_charge_rate</c> 30.</summary>
    public const float SandvichRefillSeconds = 30f;

    /// <summary>The lunchbox's modes (set_weapon_mode, TFLunchboxBehaviour): the chocolate ones overheal and are eaten sooner, the steak is no heal.</summary>
    public const int ModeChocolate = 1, ModeSteak = 2, ModeBanana = 6, ModeFishcake = 7;

    /// <summary>The attribute class that names a lunchbox's mode.</summary>
    public const string ModeAttribute = "set_weapon_mode";

    /// <summary>Stock's <c>GetThreatDanger</c> by class (tf_bot.cpp:3074-3098), for the Heavy's target bonus.</summary>
    public static float ThreatDanger(TFClass c) => c switch
    {
        TFClass.Pyro => 1f,
        TFClass.Soldier or TFClass.HeavyWeapons => 0.8f,
        TFClass.Scout or TFClass.Demoman or TFClass.Spy => 0.6f,
        TFClass.Engineer or TFClass.Sniper => 0.4f,
        TFClass.Medic => 0.2f,
        _ => 0f,
    };

    /// <summary>The share of <see cref="ThreatDanger"/> the target's priority gains.</summary>
    public const float DangerBonus = 0.3f;
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Heavy's play (research heavy.md §4.3, with the review's fixes): the minigun spun while an enemy was seen in the last
    /// <see cref="TFHeavyClass.SpinHoldSeconds"/> or is believed just round a corner, and let go to travel at full speed;
    /// fired at will inside its falloff, in bursts at slow targets out to <see cref="TFHeavyClass.BurstReach"/>, never past;
    /// never backing off a player; the secondary or the fists when the gun is dry; ammo fetched in a lull; the Sandvich eaten
    /// in a quiet moment, never at full health.
    /// </summary>
    private sealed class HeavyPlay(TFDecider d) : ClassPlay(d, TFClass.HeavyWeapons)
    {
        /// <summary>The doing record says "Heavy", the class menu's word, not the enum's.</summary>
        public override string Name => "Heavy";

        private Tick _heavyEnemySeen = new(-1_000_000), _heavyAnyEnemySeen = new(-1_000_000);
        private bool _heavySpun, _heavyEatPending;
        private int _heavyMinigunAmmo = -1;
        private Tick _heavyLunchboxEmptyUntil, _heavyPressedAt = new(-1), _heavySelectedAt;
        private KnownActor? _heavySightedBefore;
        private bool _heavyAmmoGoing;
        private AiVector _heavyAmmoAt, _heavyAmmoSkip;
        private Tick _heavyAmmoSince, _heavyAmmoLast, _heavyAmmoSkipUntil;
        private AiVector _heavyDispenserAt;
        private Tick _heavyDispenserSeen = new(-1_000_000);

        private long _spunDecides, _fireDecides, _burstDecides, _heldFire, _sightings, _sightingsSpun, _drySwaps, _ammoTrips, _eats, _eatMisses;
        private long _chaseDecides, _fireRange, _eatCalledOff, _aimingDecides, _sightingsAiming, _wantedSpun, _wantedIdle, _travelHeld;
        private double _moved;
        private AiVector _heavyLastAt;
        private bool _heavyHasLast;

        private float Seconds(Tick since) => (D._now - since) * D._tick;

        /// <summary>How this bot's archetype plays the minigun (<see cref="TFHeavyClass.WayOf"/>).</summary>
        private TFHeavyClass.Way Way
        {
            get
            {
                if (_heavyWayOf != Archetype)
                    (_heavyWay, _heavyWayOf) = (TFHeavyClass.WayOf(Archetype), Archetype);
                return _heavyWay;
            }
        }

        private TFHeavyClass.Way _heavyWay;
        private string? _heavyWayOf;

        /// <summary>Whether the weapon in hand is a minigun (by the level's table, so a replay with no kit reads it too).</summary>
        private bool HoldingMinigun => D._map.Weapons.TryGet(D._self.ActiveWeapon, out WeaponFacts w) && w.Name == TFHeavyClass.MinigunClass;

        /// <summary>The kit's wielded item in <paramref name="slot"/>, or null (no kit read).</summary>
        private Senses.TFKitItem? Item(TFLoadoutSlot slot) => D.Kit is { } kit && kit.TryWielded(slot, out Senses.TFKitItem item) ? item : null;

        private bool Holding(Senses.TFKitItem? item) => item is not null && item.WeaponKind == D._self.ActiveWeapon;

        /// <summary>
        /// Whether the bot is on its way to an objective (the enemy's flag, a point, the carrier it escorts): then only a target
        /// within <see cref="TFHeavyClass.BandFar"/> is fought or spun for. Live on ctf_2fort (c387e34e2, 3 runs) the Standard
        /// Heavies on Fetch moved at 165-170 u/s against the Soldiers' 179-185, slower than 120 u/s for 24-27 % of the
        /// seconds against 17-21 %: the gun spun for targets the attacker's tree does not stop for (past 480 u).
        /// </summary>
        private bool Travelling => D._root.Chosen?.Name is Fetch or PointsBranch or Escort;

        /// <summary>Whether the bot is on a branch that hurries (the flag home, a heal, the course, an ammo trip): no spin there.</summary>
        private bool Hurrying => D._root.Chosen?.Name is Deliver or Survive or Course or ClassBranch || D.Carrying;

        public override void Engage()
        {
            D.GeneralEngage();
            if (D._action.Kind != ActionKind.Fire || !D._targetSighted)
                return;
            float dist = D._targetDist;
            if (!HoldingMinigun)
            {
                Senses.TFKitItem? secondary = Item(TFLoadoutSlot.Secondary);
                // The lunchbox is no weapon: its trigger eats (or, held, does nothing with the plate empty), so the general fire
                // never presses it and the hands (Choose) eat or put it away. The shotgun is fought at the shotgun's reach (the
                // general fire knows only the Soldier's by name).
                if (Holding(secondary) && (secondary!.ItemClass == TFHeavyClass.LunchboxClass || dist > ShotgunReach))
                    D._action = ActionIntent.None;
                return;
            }

            if (D._self.Reserve <= 0)
            {
                // A dry gun only clicks: the hands take another (Choose).
                D._action = ActionIntent.None;
                return;
            }

            if (Travelling && dist > TFHeavyClass.BandFar)
            {
                // On the way to the objective a target off the Heavy's band is not fought: the gun spun, he walks at 110 u/s.
                D._action = ActionIntent.None;
                _travelHeld++;
                return;
            }

            if (dist <= Way.FullReach)
            {
                _fireDecides++;
                _fireRange += (long)dist;
                return;
            }

            float speed = new AiVector(D._targetVel.X, D._targetVel.Y, 0f).Length;
            float phase = D._now.Value * D._tick % TFHeavyClass.BurstEverySeconds;
            // A burst only from a gun already spun (by a nearer sighting): from cold its spin-up outlasts the burst.
            if (dist <= TFHeavyClass.BurstReach && speed < TFHeavyClass.SlowTarget && phase < TFHeavyClass.BurstOnSeconds && _heavySpun)
            {
                _fireDecides++;
                _fireRange += (long)dist;
                _burstDecides++;
                return;
            }

            // Out of the gun's reach: the look stays on the target, the trigger is let go (the spin may be held, in the hands).
            D._action = ActionIntent.None;
            _heldFire++;
        }

        public override void Hands()
        {
            if (EnemyInReach())
                _heavyEnemySeen = D._now;
            if (D.EnemyInSight())
                _heavyAnyEnemySeen = D._now;
            NoteSighting();
            NoteGun();
            NoteMoves();
            NoteDispensers();
            if ((D._self.Conditions & ActorConditions.Taunting) == 0)
                Choose();
            else if (_heavyEatPending)
            {
                // The taunt shows: the Sandvich is being eaten, and comes back only when its meter has filled again.
                (_heavyEatPending, _heavyPressedAt) = (false, new Tick(-1));
                _eats++;
                if (Item(TFLoadoutSlot.Secondary) is { ItemClass: TFHeavyClass.LunchboxClass } eaten)
                    _heavyLunchboxEmptyUntil = D._now + (int)(TFHeavyClass.RefillSeconds(eaten) / D._tick);
            }
            _heavySpun = HoldingMinigun && D._action.Kind is ActionKind.Fire or ActionKind.AltFire;
            if (_heavySpun)
                _spunDecides++;
        }

        /// <summary>The ground the bot covered (a step longer than <see cref="MovedStepMost"/> is a respawn or a teleporter, not walked), and the decides it chased: the harness's measure of how much an archetype moves.</summary>
        private void NoteMoves()
        {
            float step = _heavyHasLast ? D._self.Origin.DistanceTo(_heavyLastAt) : 0f;
            if (step < MovedStepMost)
                _moved += step;
            (_heavyLastAt, _heavyHasLast) = (D._self.Origin, true);
            if (D._leaf == "chase")
                _chaseDecides++;
        }

        /// <summary>The nearest friendly dispenser in sight, remembered (its ammo; research §4.7.7's ask, the cart still missing).</summary>
        private void NoteDispensers()
        {
            float best = float.MaxValue;
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind != ThingKind.Building || (Senses.TFThingType)t.Type != Senses.TFThingType.Dispenser || t.Team != D._self.Team)
                    continue;
                float d = D._self.Origin.DistanceTo(t.Origin);
                if (d < best)
                    (best, _heavyDispenserAt, _heavyDispenserSeen) = (d, t.Origin, D._now);
            }
        }

        /// <summary>A decide's step longer than this is not walked (TF2's fastest run, 520 u/s, covers 47 u a decide).</summary>
        private const float MovedStepMost = 200f;

        /// <summary>
        /// Whether an enemy is in sight within the gun's reach of fire at will: only such a sighting holds the spin. Stock spins
        /// for any enemy seen (tf_bot_behavior.cpp:1284-1291); live on ctf_2fort the Heavies stood spun at 110 u/s for enemies
        /// across the courtyard they could not hurt, and reached the flag 135 s in against the Soldiers' 93 s.
        /// </summary>
        private bool EnemyInReach()
        {
            float reach = Travelling ? MathF.Min(Way.FullReach, TFHeavyClass.BandFar) : Way.FullReach;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (!TeamRule.Friends(D._self.Team, f.Team) && D._self.Origin.DistanceTo(f.Origin) <= reach)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The gun's own state against what the hands asked (review finding 3), by the HUD's <see cref="TFHeavyMeters.Aiming"/>
        /// meter: decides it spun, and of the decides with a target in sight within the reach of fire at will whose last decide
        /// asked for the gun spun, those it was idle at all (the trigger let go long enough for it to wind down).
        /// </summary>
        private void NoteGun()
        {
            float aiming = D.Kit?.Meter(TFHeavyMeters.Aiming) ?? -1f;
            if (aiming < 0f)
                return;
            if (aiming > 0f)
                _aimingDecides++;
            if (!_heavySpun || !D._targetSighted || D._targetDist > Way.FullReach)
                return;
            _wantedSpun++;
            if (aiming <= 0f)
                _wantedIdle++;
        }

        /// <summary>A target newly in sight: counted, and whether the gun was already spun when it showed (the pre-spin's measure).</summary>
        private void NoteSighting()
        {
            KnownActor? now = D._targetSighted ? D._target : null;
            if (now is not null && !ReferenceEquals(now, _heavySightedBefore))
            {
                _sightings++;
                if (_heavySpun)
                    _sightingsSpun++;
                if (D.Kit?.Meter(TFHeavyMeters.Aiming) > 0f)
                    _sightingsAiming++;
            }

            _heavySightedBefore = now;
        }

        private void Choose()
        {
            if (HoldingMinigun)
                _heavyMinigunAmmo = D._self.Reserve;
            if (D._action.Kind != ActionKind.None || D.Busy)
                return;
            Senses.TFKitItem? secondary = Item(TFLoadoutSlot.Secondary), melee = Item(TFLoadoutSlot.Melee);
            bool lunchbox = secondary is { ItemClass: TFHeavyClass.LunchboxClass };

            if (_heavyEatPending && Holding(secondary) && lunchbox)
            {
                if (D._self.Reserve > 0 && EatSafe() && (_heavyPressedAt.Value < 0 || Seconds(_heavyPressedAt) < TFHeavyClass.EatPressSeconds))
                {
                    // Pressed each decide till the taunt shows: a voice line may take one decide's press.
                    if (_heavyPressedAt.Value < 0)
                        _heavyPressedAt = D._now;
                    D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, ++D._oneShot);
                    D._verdict = "eating";
                    return;
                }

                (_heavyEatPending, _heavyPressedAt) = (false, new Tick(-1));
                if (D._self.Reserve > 0 && !EatSafe())
                {
                    // Called off: an enemy showed while it came out. The Sandvich is still there; it is put away (below).
                    _eatCalledOff++;
                }
                else
                {
                    // Nothing in it yet (its meter fills), or the press never took: not again for a while.
                    _eatMisses++;
                    _heavyLunchboxEmptyUntil = D._now + (int)(TFHeavyClass.LunchboxEmptySeconds / D._tick);
                }
            }
            else if (_heavyEatPending && !Holding(secondary) && Seconds(_heavySelectedAt) > TFHeavyClass.EatSelectSeconds)
            {
                // The game did not take it out (stock will not deploy an empty lunchbox): not asked again for a while.
                _heavyEatPending = false;
                _eatMisses++;
                _heavyLunchboxEmptyUntil = D._now + (int)(TFHeavyClass.LunchboxEmptySeconds / D._tick);
            }

            if (D._now < D._handsAt)
                return;

            if (D._targetSighted && HoldingMinigun && D._self.Reserve <= 0)
            {
                // Dry in a fight: the shotgun when the kit has one, else the fists.
                Senses.TFKitItem? next = secondary is not null && !lunchbox ? secondary : melee;
                if (next is not null)
                {
                    D.Select(next.WeaponKind);
                    _drySwaps++;
                    return;
                }
            }

            if (lunchbox && !Holding(secondary) && !_heavyEatPending && WantsEat(secondary!))
            {
                D.Select(secondary!.WeaponKind);
                (_heavyEatPending, _heavySelectedAt) = (true, D._now);
                return;
            }

            if (HoldingMinigun && SpinWanted())
            {
                D._action = new ActionIntent(ActionKind.AltFire, FireStyle.Hold, 0, 0);
                return;
            }

            if (lunchbox && Holding(secondary) && !_heavyEatPending)
            {
                // The lunchbox is no weapon: back to the gun at once, fight or not.
                if (Item(TFLoadoutSlot.Primary) is { } primary)
                    D.Select(primary.WeaponKind);
                return;
            }

            D.GeneralHands();
        }

        /// <summary>
        /// Whether the spin is held now: the minigun not low on ammo, the bot not hurrying, and an enemy seen in the last
        /// <see cref="TFHeavyClass.SpinHoldSeconds"/> or a lost target believed within <see cref="TFHeavyClass.PreSpinReach"/>.
        /// </summary>
        private bool SpinWanted()
        {
            if (D._self.Reserve < TFHeavyClass.AmmoLow || Hurrying)
                return false;
            if (Seconds(_heavyEnemySeen) <= Way.SpinHold)
                return true;
            return D._target is { } t && !D._targetSighted && D._targetDist <= Way.PreSpinReach
                   && Seconds(t.LastPerceivedTick) <= Way.PreSpinBelief;
        }

        /// <summary>Whether it is quiet enough to eat: nobody seen lately, no target believed near, nothing carried.</summary>
        private bool EatSafe()
            => Seconds(_heavyAnyEnemySeen) >= TFHeavyClass.EatQuietSeconds && !D.Carrying
               && (D._target is null || D._targetDist > TFHeavyClass.EatClearUnits);

        /// <summary>Whether the bot takes the lunchbox out to eat: hurt enough for its kind, quiet, and not known empty.</summary>
        private bool WantsEat(Senses.TFKitItem lunchbox)
        {
            int mode = (int)lunchbox.Attribute(TFHeavyClass.ModeAttribute, 0f);
            if (mode == TFHeavyClass.ModeSteak || D._now < _heavyLunchboxEmptyUntil || !EatSafe())
                return false;
            float under = mode is TFHeavyClass.ModeChocolate or TFHeavyClass.ModeFishcake ? TFHeavyClass.ChocolateShare
                          : mode == TFHeavyClass.ModeBanana ? TFHeavyClass.BananaShare : TFHeavyClass.EatShare;
            return D.HealthShare < under;
        }

        /// <summary>
        /// The fight's footwork with the minigun: close on a target past <see cref="TFHeavyClass.BandFar"/>, and never back off
        /// one for being near (the launcher's self-blast rule is not the Heavy's). Another weapon in hand: the general band.
        /// </summary>
        public override bool FightInSight()
        {
            if (!HoldingMinigun)
                return D.FightAtRange();
            // A Siege Heavy closes only on a target past its reach of fire at will: spun, at 44 u/s, it holds inside it.
            float approach = D._targetDist > Way.BandFar ? 0.8f : 0f;
            if (D.Feel.Fight)
                D.FightStep(approach);
            else
                D.Strafe(approach);
            D._verdict = approach > 0f ? "closing" : "holding range";
            return true;
        }

        /// <summary>Stock's threat by class (<see cref="TFHeavyClass.ThreatDanger"/>), by the archetype's factors for a Scout and a target in the air.</summary>
        public override float TargetBonus(KnownActor target, bool sighted, float distance)
        {
            var cls = (TFClass)target.ClassBelieved;
            bool mobile = cls == TFClass.Scout || MathF.Abs(target.LastSeenVel.Z) > GroundedSpeed;
            float factor = (cls == TFClass.Scout ? Way.ScoutFactor : 1f) * (mobile ? Way.MobileFactor : 1f);
            return TFHeavyClass.DangerBonus * TFHeavyClass.ThreatDanger(cls) * factor;
        }

        /// <summary>A chase after a target out of sight, weighed by the archetype (a Siege Heavy keeps its ground).</summary>
        public override float Weigh(string branch) => branch == Fight && !D._targetSighted && !D.SightJustLost ? Way.ChaseWeight : 1f;

        /// <summary>The share of its health under which the bot goes to heal: the archetype's.</summary>
        public override float HurtShare => Way.HurtShare;

        /// <summary>The ammo trip: with the minigun's pool known low and no enemy seen for a moment, a pack or the cabinet within reach.</summary>
        public override float PlaceUtility()
            => _heavyMinigunAmmo is >= 0 and < TFHeavyClass.AmmoLow && Seconds(_heavyAnyEnemySeen) >= TFHeavyClass.AmmoLullSeconds && !D.Carrying
               && AmmoPlace(out _) ? TFHeavyClass.AmmoUtility : 0f;

        public override bool GoPlace()
        {
            if (!AmmoPlace(out AiVector at))
                return false;
            if (!_heavyAmmoGoing || at.DistanceTo(_heavyAmmoAt) > 1f || D._now - _heavyAmmoLast > DecideSchedule.Period + 1)
            {
                (_heavyAmmoGoing, _heavyAmmoAt, _heavyAmmoSince) = (true, at, D._now);
                _ammoTrips++;
            }
            else if (Seconds(_heavyAmmoSince) > TFHeavyClass.AmmoPatienceSeconds)
            {
                // Not paid there in that long (the pack taken, or out of reach): another, for a while.
                (_heavyAmmoSkip, _heavyAmmoSkipUntil, _heavyAmmoGoing) = (at, D._now + (int)(TFHeavyClass.AmmoSkipSeconds / D._tick), false);
                return false;
            }

            _heavyAmmoLast = D._now;
            D.GoTo(at, MoveSpeed.Run);
            D._verdict = "ammo";
            return true;
        }

        /// <summary>The nearest ammo pack the bot does not know taken, or its team's resupply cabinet, within <see cref="TFHeavyClass.AmmoReach"/>.</summary>
        private bool AmmoPlace(out AiVector at)
        {
            at = default;
            float best = TFHeavyClass.AmmoReach;
            ReadOnlySpan<ItemBelief> beliefs = D._memory.Board.Items;
            bool skipping = D._now < _heavyAmmoSkipUntil;
            for (int i = 0; i < D._map.Items.Length; i++)
            {
                ItemSpawn item = D._map.Items[i];
                if (item.Kind is not (ItemKind.AmmoSmall or ItemKind.AmmoMedium or ItemKind.AmmoLarge)
                    || (i < beliefs.Length && beliefs[i].Present == false) || (skipping && item.Position.DistanceTo(_heavyAmmoSkip) <= 1f))
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
                if (skipping && mid.DistanceTo(_heavyAmmoSkip) <= 1f)
                    continue;
                float d = D._self.Origin.DistanceTo(mid);
                if (d < best)
                    (best, at) = (d, mid);
            }

            if (Seconds(_heavyDispenserSeen) <= TFHeavyClass.DispenserMemorySeconds && !(skipping && _heavyDispenserAt.DistanceTo(_heavyAmmoSkip) <= 1f))
            {
                float d = D._self.Origin.DistanceTo(_heavyDispenserAt);
                if (d < best)
                    (best, at) = (d, _heavyDispenserAt);
            }

            return best < TFHeavyClass.AmmoReach;
        }

        public override void Died()
        {
            _heavySpun = _heavyEatPending = _heavyAmmoGoing = false;
            _heavyPressedAt = new Tick(-1);
            _heavyMinigunAmmo = -1;
            _heavySightedBefore = null;
            _heavyHasLast = false;
        }

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("heavy.spun_decides", _spunDecides));
            into.Add(new("heavy.fire_decides", _fireDecides));
            into.Add(new("heavy.burst_decides", _burstDecides));
            into.Add(new("heavy.held_fire", _heldFire));
            into.Add(new("heavy.sightings", _sightings));
            into.Add(new("heavy.sightings_spun", _sightingsSpun));
            into.Add(new("heavy.dry_swaps", _drySwaps));
            into.Add(new("heavy.ammo_trips", _ammoTrips));
            into.Add(new("heavy.eats", _eats));
            into.Add(new("heavy.eat_misses", _eatMisses));
            into.Add(new("heavy.eat_called_off", _eatCalledOff));
            into.Add(new("heavy.aiming_decides", _aimingDecides));
            into.Add(new("heavy.sightings_aiming", _sightingsAiming));
            into.Add(new("heavy.wanted_spun", _wantedSpun));
            into.Add(new("heavy.wanted_idle", _wantedIdle));
            into.Add(new("heavy.travel_held", _travelHeld));
            into.Add(new("heavy.chase_decides", _chaseDecides));
            into.Add(new("heavy.fire_range_sum", _fireRange));
            into.Add(new("heavy.moved_u", (long)_moved));
        }
    }
}
