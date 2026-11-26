//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Demoman's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Demoman lane's file:
/// another class's lane never edits it, and it never edits another's. The numbers are the P7 Demoman research's
/// (.scratch/p7-research/demoman.md §4.3), each naming its source: stock TFBot's where it has one.
/// </summary>
public static class TFDemomanClass
{
    /// <summary>
    /// Whether the composition fields the Demoman (C.4): yes. The stock Hybrid pinned against the all-Soldier control (tools/ai-tf-class,
    /// 4 of 8 bots, 300 s, three runs a map, at 3b469e97e) was first on the objective sooner: cp_granary 70.1/71.0/69.0 s against
    /// 79.0/77.0/77.0, ctf_2fort 84.0/112.8/95.0 s against 107.0/142.0/94.0.
    /// </summary>
    public const bool Ready = true;

    /// <summary>The Demoman's ability rows' names: the second trigger's two jobs, a launcher's detonation and a shield's charge.</summary>
    public const string Detonate = "demo_detonate", Charge = "demo_charge";

    /// <summary>
    /// The Demoman's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only. Both are the second
    /// trigger tapped (TFClassSpecialSkill: the launcher's detonation from any weapon, or with no launcher the shield's
    /// charge); two rows, so the harness counts each.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [
        new AbilityRow(Detonate, AbilityRun.Buttons, Buttons: (int)InputButtons.Attack2, HoldTicks: 2),
        new AbilityRow(Charge, AbilityRun.Buttons, Buttons: (int)InputButtons.Attack2, HoldTicks: 2),
    ];

    /// <summary>The archetypes' names (research §2).</summary>
    public const string Hybrid = "Hybrid", Trapper = "Trapper", Demoknight = "Demoknight", HybridKnight = "HybridKnight", Loch = "Loch", Combo = "Combo";

    /// <summary>
    /// The Demoman's archetypes (C.3.1), drawn by the bot's seed and weight (research §4.2's table, its symmetric column
    /// over the rows shipped, renormalised). The stock Hybrid (Grenade Launcher 19, Stickybomb Launcher 20, Bottle 1, each
    /// standing for its reskins); the Trapper (the Scottish Resistance 130 for the stickies: fourteen out, detonated in view;
    /// the stock pipes and bottle), who defends whatever role is dealt; the Demoknight (Ali Baba's Wee Booties 405 or the
    /// Bootlegger 608, the Chargin' Targe 131, the Splendid Screen 406 or the Festive Targe 1144, the Eyelander 132 or its
    /// three same-attribute reskins, then the Claidheamh Mor 327 and the Persian Persuader 404): no launcher, the charge;
    /// the hybrid knight (the pipes 19 or the Iron Bomber 1151, the Targe, the Eyelander): pipes at range, then the charge;
    /// the Loch-n-Load 308 (its pipes shatter on the world: direct hits only, nearer, and the sentry buster's pipes); the
    /// combo (the Iron Bomber 1151, whose pipes do not roll; the Quickiebomb 1150, first, so the draw fields it, else the stock
    /// stickies): the stickies in the fight, detonated in the air, by the team. The weights are research §4.2's symmetric column (the Loch's, 0 there,
    /// its attack column's) over the rows shipped. The combo's sticky jump waits on the jump simulator's sticky kind.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Demoman,
    [
        new TFArchetype(Hybrid, 0.35f, [Slot(TFLoadoutSlot.Primary, 19), Slot(TFLoadoutSlot.Secondary, 20), Slot(TFLoadoutSlot.Melee, 1)]),
        new TFArchetype(Trapper, 0.10f, [Slot(TFLoadoutSlot.Primary, 19), Slot(TFLoadoutSlot.Secondary, 130), Slot(TFLoadoutSlot.Melee, 1)]),
        new TFArchetype(Demoknight, 0.20f, [Slot(TFLoadoutSlot.Primary, 405, 608), Slot(TFLoadoutSlot.Secondary, 131, 406, 1144),
                                            Slot(TFLoadoutSlot.Melee, 132, 266, 482, 1082, 327, 404)]),
        new TFArchetype(HybridKnight, 0.05f, [Slot(TFLoadoutSlot.Primary, 19, 1151), Slot(TFLoadoutSlot.Secondary, 131), Slot(TFLoadoutSlot.Melee, 132)]),
        new TFArchetype(Loch, 0.05f, [Slot(TFLoadoutSlot.Primary, 308), Slot(TFLoadoutSlot.Secondary, 20), Slot(TFLoadoutSlot.Melee, 1)]),
        new TFArchetype(Combo, 0.30f, [Slot(TFLoadoutSlot.Primary, 1151), Slot(TFLoadoutSlot.Secondary, 1150, 20), Slot(TFLoadoutSlot.Melee, 1)]),
    ]);

    private static TFArchetypeSlot Slot(TFLoadoutSlot slot, params int[] prefer) => new(slot, [.. prefer]);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Demoman, Ready, Abilities(), Archetypes());

    // ------------------------------------------------------------------ the numbers

    /// <summary>A target this near is fought with the stickies, laid at it as it comes (research §4.3, M); inside <see cref="PointBlank"/> no blast weapon is fired.</summary>
    public const float StickyNear = 350f, PointBlank = 180f;

    /// <summary>The fight's range bands: the pipes' (400 to 1000, research §4.3; stock's desired range 500, tf_bot.cpp:3212) and the stickies'.</summary>
    public const float PipeNear = 400f, PipeFar = 1000f, StickyBandNear = 250f, StickyBandFar = 800f;

    /// <summary>
    /// An own armed sticky within this of an enemy in sight is detonated (stock's tactical monitor, tf_bot_tactical_monitor.cpp:102:
    /// 150); never with one the game would detonate (armed by its age) within <see cref="SelfSafe"/> of the nearest point of
    /// the bot's hull, feet to head (the blast's 146, TFProjectileDefs.GrenadeJumpRadius, and a decide's travel at a
    /// Demoman's 280 u/s before the tap goes). Live, a sticky on a ledge 197 u over the bot's feet passed a guard measured from
    /// the feet and killed him.
    /// </summary>
    public const float DetonateNear = 150f, SelfSafe = 146f + 28f;

    /// <summary>A sticky moving slower than this has come to rest (stuck).</summary>
    public const float StickyRestSpeed = 30f;

    /// <summary>A sticky the bot saw leave is its own when first seen within this of where it was aimed, or of the bot's eye just after the launch.</summary>
    public const float StickyClaimUnits = 300f, StickyClaimEyeUnits = 400f;

    /// <summary>
    /// The sentry kill (stock's CTFBotStickybombSentrygun, research §4.3): a sentry the bot saw within its shy time and
    /// within <see cref="SentryReach"/> is gone after while the bot's health is over <see cref="SentryHealthShare"/>; it stands
    /// <see cref="SentryStandOff"/> past the sentry's range, lays <see cref="SentryStickies"/> (stock's three, tf_bot_stickybomb_sentrygun.cpp:151; one more on a level 3, the research's for its 216 health)
    /// within <see cref="SentryNear"/> of it and detonates them at rest; it gives up after <see cref="SentryGiveUpSeconds"/>.
    /// </summary>
    public const float SentryReach = 2500f, SentryStandOff = 150f, SentryNear = 120f, SentryGiveUpSeconds = 20f, SentryHealthShare = 0.4f,
                       SentryUtility = 0.7f;

    // SentryReach is how far off a seen sentry the bot will go to its stand: every stand is SentryStandOff past the sentry's
    // range, where a tap falls short (StickyLevelReach) and the throw is charged. A full charge still has an arc onto a
    // sentry at SentryReach itself, with the game's launch, fall and drag (AFullChargeHasAnArcOntoASentryAtTheSentryReach).

    /// <summary>
    /// How far a sticky launched at <paramref name="speed"/> lands on level ground at its best angle, drag left out: the
    /// launch's speed with the game's up-kick (<c>forward * speed + up * 200</c>, <see cref="TFProjectileDefs.PipeLaunchUp"/>)
    /// squared over the grenade's fall (<see cref="TFKnowledge.ProjectileGravity"/>). An uncharged sticky (900 u/s) reaches
    /// about 1,060 u, short of a stand (1,250 u); a full charge (2,400 u/s) about 7,250, which the air's drag cuts well short.
    /// </summary>
    public static float StickyLevelReach(float speed)
        => (speed * speed + TFProjectileDefs.PipeLaunchUp * TFProjectileDefs.PipeLaunchUp)
           / Knowledge.TFKnowledge.ProjectileGravity(TFProjectile.PipebombRemote);

    /// <summary>
    /// Seconds a sticky launcher is held to throw at <paramref name="speed"/>: the inverse of the game's charge
    /// (<see cref="TFProjectileDefs.StickySpeed"/>, 900 to 2,400 u/s over <paramref name="chargeSeconds"/>).
    /// </summary>
    public static float StickyChargeFor(float speed, float chargeSeconds)
        => Math.Clamp((speed - TFProjectileDefs.StickyMinChargeSpeed) / (TFProjectileDefs.StickyMaxChargeSpeed - TFProjectileDefs.StickyMinChargeSpeed),
                      0f, 1f) * chargeSeconds;

    /// <summary>
    /// What a charged throw at a sentry adds to the least speed that reaches it: the port's own margin, so the throw is
    /// never at its speed's furthest reach, where the arc is steepest and least forgiving. Stock's tf_bot instead holds the
    /// trigger 1.1 times the charge time its search found and keeps that aim, to overshoot (tf_bot_stickybomb_sentrygun.cpp:183-196);
    /// here the arc is solved again for the margined speed, a flatter arc, not an overshoot.
    /// </summary>
    public const float SentryChargeMargin = 1.08f;

    /// <summary>
    /// The sticky thrown from <paramref name="eye"/> at a sentry's <paramref name="aim"/>: the least speed, from a tap's to a
    /// full charge's in 25 u/s steps, whose arc (the game's launch) lands on it, times <see cref="SentryChargeMargin"/>, and
    /// the look that lands it at that speed with the air's drag flown (<see cref="AimLead.LobLaunchDragged"/>). Null when no
    /// speed to a full charge reaches it.
    /// </summary>
    public static (float Speed, AiVector Look)? SentryThrow(AiVector eye, AiVector aim, in WeaponFacts own)
    {
        for (float s = own.ProjectileSpeed; s <= TFProjectileDefs.StickyMaxChargeSpeed; s += 25f)
        {
            if (AimLead.LobLaunch(eye, aim, s, own.ProjectileGravity, own.LaunchUp) is null)
                continue;
            float margined = MathF.Min(TFProjectileDefs.StickyMaxChargeSpeed, s * SentryChargeMargin);
            if (AimLead.LobLaunchDragged(eye, aim, margined, own.ProjectileGravity, own.LaunchUp, Knowledge.TFKnowledge.GrenadeDrag) is { } look)
                return (margined, look);
        }

        return null;
    }

    public const int SentryStickies = 3;

    /// <summary>A lob at a place (a trap's spot, a sentry) goes once the view has held it this long (a reaction and a flick), the place within this of where it was.</summary>
    public const float LobSettleSeconds = 0.3f, LobSamePoint = 40f;

    /// <summary>A sentry the bot looks at, within its sight, and does not see for this long is gone (destroyed or carried).</summary>
    public const float SentryGoneSeconds = 1.5f;

    /// <summary>
    /// The trap (stock's CTFBotPrepareStickybombTrap, research §4.3): laid on the defending role in a lull, no enemy in
    /// sight nor known within <see cref="TrapQuietUnits"/> (stock :188), not hit within <see cref="TrapHurtSeconds"/> (:86),
    /// at the objective the enemy may take next within <see cref="TrapReach"/>; the Hybrid's <see cref="HybridTrapStickies"/>
    /// one to a spot, the Trapper's all but two of his launcher's, two to a spot, the spots <see cref="TrapSpread"/> apart.
    /// Laid from within <see cref="TrapLayFrom"/>; the Trapper watches it from <see cref="TrapWatchUnits"/> back on his side.
    /// </summary>
    public const float TrapQuietUnits = 500f, TrapHurtSeconds = 1f, TrapReach = 3000f, TrapSpread = 150f, TrapLayFrom = 900f, TrapWatchUnits = 600f,
                       TrapWatchArrive = 120f, TrapUtility = 0.7f, WatchUtility = 0.66f;

    public const int HybridTrapStickies = 4;

    /// <summary>
    /// The trap's spots round <paramref name="centre"/>, <paramref name="i"/> from 0: the objective itself, then either
    /// side of it and further out on the enemy's side (<paramref name="enemySide"/>, flat), <see cref="TrapSpread"/> apart
    /// (spread so one blast cannot clear them, the wiki).
    /// </summary>
    public static AiVector TrapSpotAt(AiVector centre, AiVector enemySide, int i)
    {
        var across = new AiVector(-enemySide.Y, enemySide.X, 0f);
        const float s = TrapSpread;
        (float along, float side) = i switch
        {
            0 => (0f, 0f),
            1 => (s, 0f),
            2 => (0f, s),
            3 => (0f, -s),
            4 => (s, s),
            5 => (s, -s),
            _ => (2f * s, 0f),
        };
        return centre + enemySide * along + across * side;
    }

    /// <summary>The in-view detonation's cone (TFProjectileEffects.AimedDot, stock ModifyPipebombsInView's 0.975).</summary>
    public const float InViewDot = 0.975f;

    /// <summary>
    /// The shield's charge (research §4.3; TFShieldCharge, TFMaxSpeed.MaxChargeSpeed 750 u/s for 1.5 s): begun with the meter
    /// at <see cref="ChargeFull"/> on a target within <see cref="ChargeAimDeg"/> of the eye, <see cref="ChargeNear"/> to
    /// <see cref="ChargeFar"/> off (a crit wants the meter at 40 or under at the swing: 0.9 s and the swing's 0.2 s at
    /// 750 u/s, about 950 u; the Tide Turner never crits, its mini-crit band ends at <see cref="TideTurnerFar"/>); the
    /// swing goes within <see cref="ChargeSwingReach"/> (the sword's 128 u while charging and 0.2 s of the charge's run).
    /// </summary>
    public const float ChargeFull = 100f, ChargeNear = 450f, ChargeFar = 1000f, TideTurnerFar = 600f, ChargeAimDeg = 8f, ChargeSeconds = 1.5f,
                       ChargeSwingReach = 128f + 0.2f * 750f;

    /// <summary>A knight's bonus to the lone and the soft (the Medic's own bonus's size, TFDecider.MedicBonus); a sword's bearer with three heads heals at this share.</summary>
    public const float KnightTargetBonus = 0.35f, HeadsHurtShare = 0.55f;

    /// <summary>
    /// The Loch-n-Load's pipes break on the world (no roller, no bank: research A6), so they are fired at a body nearer:
    /// its band. A sentry it kills with them, direct, from past the range (its <c>mult_dmg_vs_buildings</c>).
    /// </summary>
    public const float LochNear = 300f, LochFar = 750f;

    /// <summary>
    /// The combo fights with the stickies further out (<see cref="ComboStickyNear"/>: the Quickiebomb's faster charge and
    /// air detonation, research A4) and detonates one in the air within <see cref="AirDetonateNear"/> of an enemy (M; the
    /// airborne blast is 85 % of the radius, TFProjectileDefs.StickyAirDetRadius). It keeps with the team: the team stance's
    /// branch weighs <see cref="ComboTeamWeight"/>, the points' <see cref="ComboPointsWeight"/> (6s: stays by his Medic).
    /// </summary>
    public const float ComboStickyNear = 650f, AirDetonateNear = 100f, ComboTeamWeight = 1.3f, ComboPointsWeight = 0.8f;

    /// <summary>The counters' names (<c>/ai/classes</c>, <c>tools/ai-tf-class</c>).</summary>
    public const string StickiesLaid = "demoman.stickies_laid", Detonations = "demoman.detonations", SentryDetonations = "demoman.sentry_detonations",
                        SentriesGone = "demoman.sentries_gone", FireDecides = "demoman.fire_decides", FireRangeSum = "demoman.fire_range_sum",
                        FireNear = "demoman.fire_lt200", FireMid = "demoman.fire_200_1000", FireFar = "demoman.fire_gt1000",
                        Travel = "demoman.travel_units", AliveDecides = "demoman.alive_decides", TrapLaid = "demoman.trap_taps",
                        WatchDecides = "demoman.trap_watch_decides", InViewTurns = "demoman.inview_turns", Charges = "demoman.charges",
                        ChargeSwings = "demoman.charge_swings", HeadsMost = "demoman.heads_most", AirDetonations = "demoman.air_detonations";
}

/// <summary>
/// What a Demoman's kit makes of his play (research §4.2's flags): read off the bot's own kit (its HUD's
/// <see cref="TFHud.Kit"/>), so a pinned or hand-picked loadout plays right whatever its archetype's name.
/// </summary>
public sealed record TFDemomanKit(bool Pipes, bool Stickies, bool Shield, bool InViewDetonate, bool NoRoll, bool Shatter, bool Sword, bool TideTurner,
                                  int MaxStickies, float ArmSeconds, float StickyChargeSeconds = TFProjectileBehaviour.MaxChargeTime)
{
    public const string GrenadeLauncher = "tf_weapon_grenadelauncher", Cannon = "tf_weapon_cannon", StickyLauncher = "tf_weapon_pipebomblauncher",
                        ShieldClass = "tf_wearable_demoshield", SwordClass = "tf_weapon_sword", KatanaClass = "tf_weapon_katana";

    /// <summary>Whether the stickies arm sooner than stock's (the Quickiebomb's <c>sticky_arm_time</c> −0.2): made to be detonated in the air.</summary>
    public bool AirDetonate => Stickies && ArmSeconds < TFProjectileDefs.StickyArmTime - 0.01f;

    /// <summary>The stock kit's: pipes and stickies, eight out, armed in 0.8 s (TFProjectileDefs).</summary>
    public static TFDemomanKit Stock => new(true, true, false, false, false, false, false, false, TFProjectileDefs.MaxStickies, TFProjectileDefs.StickyArmTime);

    /// <summary>The kit <paramref name="hud"/> shows; the stock kit's with none (a replay of an older recording).</summary>
    public static TFDemomanKit Of(TFHud? hud)
    {
        if (hud is null)
            return Stock;
        bool pipes = hud.TryWielded(TFLoadoutSlot.Primary, out TFKitItem primary) && primary.ItemClass is GrenadeLauncher or Cannon;
        bool stickies = hud.TryWielded(TFLoadoutSlot.Secondary, out TFKitItem secondary) && secondary.ItemClass == StickyLauncher;
        bool shield = hud.TryItem(TFLoadoutSlot.Secondary, out TFKitItem worn) && worn.ItemClass == ShieldClass;
        bool sword = hud.TryWielded(TFLoadoutSlot.Melee, out TFKitItem melee) && melee.ItemClass is SwordClass or KatanaClass;
        bool inView = stickies && (int)secondary.Attribute("set_detonate_mode", 0f) == TFProjectileDefs.DetonateModeDot;
        int most = stickies ? TFProjectileDefs.MaxStickies + (int)secondary.Attribute("add_max_pipebombs", 0f) : 0;
        float arm = TFProjectileDefs.StickyArmTime + (stickies ? secondary.Attribute("sticky_arm_time", 0f) : 0f);
        // GetChargeMaxTime (tf_weapon_pipebomblauncher.cpp): 4 s through the stickybomb_charge_rate hook, an inverted
        // percentage the window is multiplied by (the Quickiebomb's 0.3: 1.2 s).
        float charge = TFProjectileBehaviour.MaxChargeTime
                       * (stickies ? secondary.Attribute(TFProjectileBehaviour.ChargeRateAttribute, 1f) : 1f);
        return new TFDemomanKit(pipes, stickies, shield, inView, pipes && primary.Has("grenade_no_bounce"),
                                pipes && (int)primary.Attribute("set_detonate_mode", 0f) == TFProjectileDefs.DetonateModeAir, sword,
                                shield && worn.Has("lose_demo_charge_on_damage_when_charging"), most, MathF.Max(0.1f, arm),
                                MathF.Max(0.1f, charge));
    }
}

public sealed partial class TFDecider
{
    /// <summary>One sticky of the bot's own it knows is out: when it left, where (aimed, then seen), whether it rests.</summary>
    private struct DemomanSticky
    {
        public Tick Launched;
        public AiVector At;
        public int Thing;
        public bool Resting;
        public float Flight;
    }

    /// <summary>
    /// The Demoman's play (research §4.3, the stock Hybrid first): the pipes lobbed in the fight, the stickies laid at an
    /// enemy who comes close and detonated when one stands by an armed one, the weapon by range and ammunition, an enemy
    /// sentry killed with stickies from past its range. What he knows of his own stickies is what a player knows: the ones
    /// he saw leave, where he aimed them and saw them land, and his HUD's count (<see cref="TFDemomanMeters"/>).
    /// </summary>
    private sealed partial class DemomanPlay(TFDecider d) : ClassPlay(d, TFClass.Demoman)
    {
        private TFDemomanKit _kit = TFDemomanKit.Stock;
        private TFHud? _kitOf;
        private bool _kitRead;

        private readonly DemomanSticky[] _out = new DemomanSticky[16];
        private int _outCount;
        private int _lastWeapon = -1, _lastClip = -1;
        private bool _aimedSticky;
        private AiVector _stickyAim;
        private float _stickyFlight;

        // The other weapons' clips as last seen in hand (-1: not seen this life, full).
        private int _primaryClip = -1, _secondaryClip = -1;

        private int _detonateRow = -2;

        private bool _travelKnown;
        private AiVector _travelFrom;

        // The sentry kill.
        private int _sentryId = int.MinValue;
        private AiVector _sentryAt;
        private int _sentryLevel;
        private Tick _sentrySince, _sentryLookedSince;
        private bool _sentryLooking;
        private readonly List<(int Id, Tick Until)> _sentryGiveUps = new();

        private long _airDetonations;
        private long _laid, _detonations, _sentryDetonations, _sentriesGone, _fires, _fireRange, _fireNear, _fireMid, _fireFar, _travel, _alive;

        /// <summary>The kit's flags, worked out once per HUD.</summary>
        public TFDemomanKit Kit
        {
            get
            {
                TFHud? hud = D.Kit;
                if (!_kitRead || !ReferenceEquals(hud, _kitOf))
                    (_kit, _kitOf, _kitRead) = (TFDemomanKit.Of(hud), hud, true);
                return _kit;
            }
        }

        /// <summary>The stickies the bot knows are out.</summary>
        public int StickiesOut => _outCount;

        private int DetonateRow => _detonateRow == -2 ? _detonateRow = D.AbilityRow(TFDemomanClass.Detonate) : _detonateRow;

        private float Age(in DemomanSticky s) => (D._now - s.Launched) * D._tick;

        private bool Armed(in DemomanSticky s) => Age(in s) >= Kit.ArmSeconds && (s.Resting || Age(in s) >= s.Flight + 0.15f);

        private int StickyKind => Kit.Stickies ? D.SecondaryKind : -1;

        private bool HoldingStickies => StickyKind >= 0 && D._self.ActiveWeapon == StickyKind;

        // ------------------------------------------------------------------ what the bot knows of its stickies

        /// <summary>
        /// Brings the bot's own stickies up to date, once a decide before the fire: a clip that fell with the launcher in
        /// hand left as many stickies, toward where the last decide aimed; one of the team's stickies first seen near such
        /// an aim (or by the eye just after a launch) is that one, followed while in sight; the HUD's count drops the oldest
        /// the bot thought out past it (a ninth removes the first; an enemy's bullet takes one).
        /// </summary>
        private Tick _readAt = new(-1);

        /// <summary>What the play reads first in a decide, once, before the tree weighs its branch (<see cref="PlaceUtility"/>) or the hands fire.</summary>
        private void Refresh()
        {
            if (_readAt == D._now)
                return;
            _readAt = D._now;
            ReadStickies();
            NoteHurt();
        }

        private void ReadStickies()
        {
            SelfState self = D._self;
            int kind = StickyKind;
            if (kind >= 0 && self.ActiveWeapon == kind && _lastWeapon == kind && self.Clip < _lastClip && _aimedSticky)
            {
                for (int n = _lastClip - self.Clip; n > 0; n--)
                    AddSticky();
            }

            (_lastWeapon, _lastClip) = (self.ActiveWeapon, self.Clip);
            AiVector eye = D.OwnEye;
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind != ThingKind.Projectile || t.Type != (int)TFThingType.Sticky || t.Team != self.Team)
                    continue;
                int at = -1;
                float best = float.MaxValue;
                for (int i = 0; i < _outCount; i++)
                {
                    if (_out[i].Thing == t.Id)
                    {
                        at = i;
                        break;
                    }

                    if (_out[i].Thing != 0 || Age(in _out[i]) > 3f)
                        continue;
                    float d = _out[i].At.DistanceTo(t.Origin);
                    bool near = d <= TFDemomanClass.StickyClaimUnits || (Age(in _out[i]) < 0.4f && eye.DistanceTo(t.Origin) <= TFDemomanClass.StickyClaimEyeUnits);
                    if (near && d < best)
                        (best, at) = (d, i);
                }

                if (at < 0)
                    continue;
                _out[at].Thing = t.Id;
                _out[at].At = t.Origin;
                _out[at].Resting = t.Velocity.Length < TFDemomanClass.StickyRestSpeed;
            }

            float count = D.Kit?.Meter(TFDemomanMeters.Stickies) ?? -1f;
            if (count < 0f)
                return;
            int old = 0;
            for (int i = 0; i < _outCount; i++)
                old += Age(in _out[i]) > 0.5f ? 1 : 0;
            for (int i = 0; i < _outCount && old > count;)
            {
                if (Age(in _out[i]) > 0.5f)
                {
                    RemoveSticky(i);
                    old--;
                    continue;
                }

                i++;
            }
        }

        /// <summary>
        /// A sticky the bot saw leave. Past the launcher's most, the game removes the oldest out (the tray's cap,
        /// tf_weapon_pipebomblauncher.cpp:381-392), and so does the bot's list: a sticky is never refused for the cap, the
        /// oldest (a stale trap's, one by an objective gone by) gives way, as a player's does.
        /// </summary>
        private void AddSticky()
        {
            if (_outCount == _out.Length)
                RemoveSticky(0);
            _out[_outCount++] = new DemomanSticky { Launched = D._now, At = _stickyAim, Flight = _stickyFlight };
            _laid++;
            while (Kit.MaxStickies > 0 && _outCount > Kit.MaxStickies)
                RemoveSticky(0);
        }

        private void RemoveSticky(int i)
        {
            Array.Copy(_out, i + 1, _out, i, _outCount - i - 1);
            _outCount--;
        }

        // ------------------------------------------------------------------ the fire

        public override void Engage()
        {
            Refresh();
            _aimedSticky = false;
            if (_sentryId != int.MinValue)
                SeeSentryGone();
            if (D.Busy)
                return;
            if (!D._targetSighted && SentryInSight(out AiVector sentry))
            {
                // The sentry's own weapon, once in hand (the hands take it out): the Loch's pipes, else the stickies.
                int want = SentryWeapon();
                if (want >= 0 && D._self.ActiveWeapon == want)
                {
                    if (want == StickyKind)
                        StickyAtSentry(sentry);
                    else
                        PipeAtSentry(sentry);
                }

                return;
            }

            if (HoldingStickies)
            {
                if (D._targetSighted && D._target is not null)
                    StickyAtTarget();
                else if (LayingTrap)
                    StickyAtSpot();
                return;
            }

            D.GeneralEngage();
            // A pipe at point blank would take the bot in its blast too: the hands change weapon instead.
            if (D._action.Kind == ActionKind.Fire && D._targetSighted && D._targetDist < TFDemomanClass.PointBlank && !D.HoldingMelee)
                D._action = ActionIntent.None;
        }

        /// <summary>
        /// A sticky at the target in sight: lobbed on the low arc at its feet (on the ground) or its body, led by the flight,
        /// tapped (an uncharged sticky, 900 u/s, lands at most about 1,060 u off on level ground: <see cref="TFDemomanClass.StickyLevelReach"/>);
        /// never inside <see cref="TFDemomanClass.PointBlank"/>, where
        /// the blast would take the bot too.
        /// </summary>
        private void StickyAtTarget()
        {
            if (!D.HeldFacts(out WeaponFacts own) || own.ProjectileSpeed <= 0f)
                return;
            bool feet = MathF.Abs(D._targetVel.Z) < GroundedSpeed;
            var aim = new AiVector(D._targetPos.X, D._targetPos.Y, D.AimHeight(feet));
            AiVector eye = D.OwnEye;
            AiVector led = AimLead.Point(eye, aim, D._targetVel, own.ProjectileSpeed, D.LeadNow);
            AiVector lobbed = AimLead.LobLaunch(eye, led, own.ProjectileSpeed, own.ProjectileGravity, own.LaunchUp) ?? led;
            AiVector point = lobbed + D.AimError(lobbed, feet);
            D._look = new LookIntent(LookKind.Actor, point, D._target!.Id, feet ? AimPoint.Feet : AimPoint.Body, (led - aim).Length, 0);
            if (D._targetDist < TFDemomanClass.PointBlank || D._self.Clip <= 0)
                return;
            Fire(led, eye, own.ProjectileSpeed);
        }

        private AiVector _lobPoint;
        private Tick _lobSince = new(-1);

        /// <summary>
        /// Whether the view has had <see cref="TFDemomanClass.LobSettleSeconds"/> on a lob at <paramref name="point"/> (a look at a
        /// place, not a body: the applier's hands wait for the eye only on a body, BotCommandApplier.HandsWaitForTheEye). A point
        /// moved more than <see cref="TFDemomanClass.LobSamePoint"/> is a new look, and its wait starts again; so the first sticky
        /// of a trap or at a sentry goes where it is aimed, not where the view was.
        /// </summary>
        private bool LobSettled(AiVector point)
        {
            // A look not held the last decide (another branch had the view) starts again too.
            if (_lobSince.Value < 0 || point.DistanceTo(_lobPoint) > TFDemomanClass.LobSamePoint || D._now - _lobSeenAt > DecideSchedule.Period * 2)
                (_lobPoint, _lobSince) = (point, D._now);
            _lobSeenAt = D._now;
            return (D._now - _lobSince) * D._tick >= TFDemomanClass.LobSettleSeconds;
        }

        private Tick _lobSeenAt;

        /// <summary>Taps the trigger with a sticky aimed to land at <paramref name="landing"/>: the launch the next decide's clip shows is laid there.</summary>
        private void Fire(AiVector landing, AiVector eye, float speed)
        {
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0);
            D.Shots++;
            _aimedSticky = true;
            _stickyAim = landing;
            _stickyFlight = eye.DistanceTo(landing) / MathF.Max(1f, speed) * 1.2f + 0.1f;
        }

        // ------------------------------------------------------------------ the detonation

        /// <summary>
        /// The detonation, over the fire: an own armed sticky within <see cref="TFDemomanClass.DetonateNear"/> of an enemy the
        /// bot sees (not one who cannot be hurt), or the sentry kill's stickies laid and at rest by it; never with an own
        /// sticky within <see cref="TFDemomanClass.SelfSafe"/> of the bot.
        /// </summary>
        public override void OneShots()
        {
            NoteHeads();
            if (!D.Busy)
            {
                ChargeSwing();
                ChargeShot();
            }

            if (!Kit.Stickies || _outCount == 0 || D.Busy || DetonateRow < 0)
                return;
            for (int i = 0; i < _outCount; i++)
            {
                if (HullDistance(_out[i].At) < TFDemomanClass.SelfSafe && Age(in _out[i]) >= Kit.ArmSeconds)
                    return;
            }

            int due = StickyDue();
            if (due < 0 || !InViewOf(_out[due].At))
                return;
            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(DetonateRow, 0), ++D._oneShot);
            D._verdict = "detonate";
            _detonations++;
            if (_sentryDue)
                _sentryDetonations++;
            if (_airDue)
                _airDetonations++;
            AiVector eye = D.OwnEye, forward = VisionMath.Forward(D._self.Eye);
            int kept = 0;
            for (int i = 0; i < _outCount; i++)
            {
                AiVector to = _out[i].At - eye;
                bool goes = (Armed(in _out[i]) || (_airDue && _out[i].Thing != 0 && Age(in _out[i]) >= Kit.ArmSeconds)) && (!Kit.InViewDetonate || to.Length < 1f || forward.Dot(to.Normalised()) > TFDemomanClass.InViewDot);
                if (!goes)
                    _out[kept++] = _out[i];
            }

            _outCount = kept;
        }

        private bool _sentryDue, _airDue;

        /// <summary>An own sticky seen in flight, armed, within <see cref="TFDemomanClass.AirDetonateNear"/> of an enemy in sight, or -1.</summary>
        private int AirDue()
        {
            for (int i = 0; i < _outCount; i++)
            {
                if (_out[i].Thing == 0 || _out[i].Resting || Age(in _out[i]) < Kit.ArmSeconds)
                    continue;
                foreach (ref readonly ActorFacts f in D._view.Sighted)
                {
                    if (!TeamRule.Friends(D._self.Team, f.Team) && (f.Conditions & ActorConditions.Invulnerable) == 0
                        && _out[i].At.DistanceTo(f.Origin + new AiVector(0f, 0f, 36f)) <= TFDemomanClass.AirDetonateNear)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>The armed sticky a detonation is due for (by an enemy in sight, or the sentry kill's laid by it), or -1.</summary>
        private int StickyDue()
        {
            _sentryDue = _airDue = false;
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.Friends(D._self.Team, f.Team) || (f.Conditions & ActorConditions.Invulnerable) != 0)
                    continue;
                for (int i = 0; i < _outCount; i++)
                {
                    if (Armed(in _out[i]) && _out[i].At.DistanceTo(f.Origin) <= TFDemomanClass.DetonateNear)
                        return i;
                }
            }

            if (Kit.AirDetonate && AirDue() is >= 0 and var air)
            {
                _airDue = true;
                return air;
            }

            if (_sentryId == int.MinValue)
                return -1;
            int laid = 0, last = -1;
            for (int i = 0; i < _outCount; i++)
            {
                if (Armed(in _out[i]) && _out[i].Resting && Flat(_out[i].At - _sentryAt) <= TFDemomanClass.SentryNear)
                    (laid, last) = (laid + 1, i);
            }

            if (laid < SentryNeed)
                return -1;
            _sentryDue = true;
            return last;
        }

        /// <summary>How far <paramref name="at"/> is from the nearest point of the bot's hull (its box, feet to head; ducked, lower).</summary>
        private float HullDistance(AiVector at)
        {
            AiVector o = D._self.Origin;
            float half = Knowledge.TFKnowledge.BodyHalfWidth;
            float top = (D._self.Flags & SelfFlags.Ducking) != 0 ? TFViewVectors.Params.DuckHullMax.Z : TFViewVectors.Params.HullMax.Z;
            var nearest = new AiVector(Math.Clamp(at.X, o.X - half, o.X + half), Math.Clamp(at.Y, o.Y - half, o.Y + half), Math.Clamp(at.Z, o.Z, o.Z + top));
            return at.DistanceTo(nearest);
        }

        private int SentryNeed => TFDemomanClass.SentryStickies + (_sentryLevel >= 3 ? 1 : 0);

        private static float Flat(AiVector v) => new AiVector(v.X, v.Y, 0f).Length;

        // ------------------------------------------------------------------ the hands

        /// <summary>
        /// The weapon by range and ammunition (research §4.3): in a fight, the stickies for a target that comes within
        /// <see cref="TFDemomanClass.StickyNear"/>, the pipes past it, the other launcher when the one in hand is dry, the
        /// melee weapon inside <see cref="TFDemomanClass.PointBlank"/>; the stickies for a sentry; in a lull, the stickies
        /// reloaded before the pipes are taken out and reloaded. At most once a <see cref="SwitchSeconds"/>.
        /// </summary>
        public override void Hands()
        {
            Note();
            // In a fight the right weapon comes before a shot from the wrong one (the fire's own, which Engage set); a
            // detonation or another one-shot stands.
            bool fireOnly = D._action.Kind == ActionKind.Fire && D._targetSighted;
            if ((D._action.Kind != ActionKind.None && !fireOnly) || D.Busy || D._now < D._handsAt)
                return;
            int primary = Kit.Pipes ? D.PrimaryKind : -1, sticky = StickyKind, active = D._self.ActiveWeapon;
            int melee = D.Kit is { } hud && hud.TryWielded(TFLoadoutSlot.Melee, out TFKitItem m) ? m.WeaponKind : -1;
            if (primary < 0 && sticky < 0)
            {
                D.GeneralHands();
                return;
            }

            if (fireOnly || D._targetSighted)
            {
                int want = FightWeapon(primary, sticky, melee);
                if (want >= 0 && want != active)
                    D.Select(want);
                return;
            }

            if (_sentryId != int.MinValue && SentryWeapon() is >= 0 and var forSentry)
            {
                if (active != forSentry)
                    D.Select(forSentry);
                else if (D._self.Clip <= 0)
                    ReloadHeld();
                return;
            }

            if (LayingTrap && sticky >= 0 && HasAmmo(sticky))
            {
                if (active != sticky)
                    D.Select(sticky);
                else if (D._self.Clip <= 0)
                    ReloadHeld();
                return;
            }


            if (active == sticky && sticky >= 0 && ReloadHeld())
                return;
            if (primary >= 0 && active != primary)
            {
                D.Select(primary);
                return;
            }

            ReloadHeld();
        }

        /// <summary>
        /// The weapon for the target in sight at its distance: inside <see cref="TFDemomanClass.PointBlank"/> the melee
        /// weapon; within <see cref="TFDemomanClass.StickyNear"/> the stickies when loaded; else the pipes when loaded, the
        /// stickies when the pipes are dry; with both dry, the launcher in hand loads (-1: no change) or the melee weapon.
        /// </summary>
        private int FightWeapon(int primary, int sticky, int melee)
        {
            float d = D._targetDist;
            if (d < TFDemomanClass.PointBlank)
                return melee;
            // The hybrid knight: the sword out for a charge when the meter is full and the target comes into its band.
            (float near, float far) = ChargeBand;
            if (Kit.Shield && melee >= 0 && (Charging || (ChargeMeter >= TFDemomanClass.ChargeFull && d >= near && d <= far && ChargeTarget())))
                return melee;
            bool pipes = primary >= 0 && Loaded(primary), stickies = sticky >= 0 && Loaded(sticky);
            if (d < StickyNear && stickies)
                return sticky;
            if (pipes)
                return primary;
            if (stickies)
                return sticky;
            int active = D._self.ActiveWeapon;
            return (active == primary || active == sticky) && D._self.Reserve > 0 ? -1 : melee;
        }

        /// <summary>Whether the bot plays the combo (the archetype's name).</summary>
        private bool Combo => string.Equals(Archetype, TFDemomanClass.Combo, StringComparison.OrdinalIgnoreCase);

        /// <summary>How near a target is fought with the stickies: the combo's (or an air-detonating launcher's) further.</summary>
        private float StickyNear => Combo || Kit.AirDetonate ? TFDemomanClass.ComboStickyNear : TFDemomanClass.StickyNear;

        /// <summary>The combo keeps with the team (its stance's branch over the points'); the rest weigh as the general play.</summary>
        public override float Weigh(string branch)
            => !Combo ? 1f : branch == TeamBranch ? TFDemomanClass.ComboTeamWeight : branch == PointsBranch ? TFDemomanClass.ComboPointsWeight : 1f;

        /// <summary>Whether the weapon of <paramref name="kind"/> has a shot loaded: in hand, its clip; holstered, its clip as last seen (full when not seen this life).</summary>
        private bool Loaded(int kind)
        {
            if (D._self.ActiveWeapon == kind)
                return D._self.Clip > 0;
            int seen = kind == D.PrimaryKind ? _primaryClip : kind == StickyKind ? _secondaryClip : 1;
            return seen != 0;
        }

        /// <summary>Whether the weapon of <paramref name="kind"/> has a shot to fire: loaded, or in hand with a reserve to load.</summary>
        private bool HasAmmo(int kind) => Loaded(kind) || (D._self.ActiveWeapon == kind && D._self.Reserve > 0);

        /// <summary>A lull's reload of the weapon in hand when its clip is not full; true when one was asked.</summary>
        private bool ReloadHeld()
        {
            if (!D.HeldFacts(out WeaponFacts own) || !own.HasClip || D._self.Clip >= own.ClipSize || D._self.Reserve <= 0
                || (D._self.Flags & SelfFlags.Reloading) != 0)
                return false;
            D._action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++D._oneShot);
            D._handsAt = D._now + (int)(SwitchSeconds / D._tick);
            return true;
        }

        /// <summary>What the hands note every decide before they choose: each launcher's clip as seen in hand, and the counters.</summary>
        private void Note()
        {
            SelfState self = D._self;
            if (self.ActiveWeapon == D.PrimaryKind && D.PrimaryKind >= 0)
                _primaryClip = self.Clip;
            else if (self.ActiveWeapon == StickyKind && StickyKind >= 0)
                _secondaryClip = self.Clip;
            _alive++;
            if (_travelKnown)
            {
                float step = Flat(self.Origin - _travelFrom);
                if (step < 300f)
                    _travel += (long)step;
            }

            (_travelFrom, _travelKnown) = (self.Origin, true);
            if (D._action.Kind != ActionKind.Fire || !D._targetSighted)
                return;
            float range = D._targetDist;
            _fires++;
            _fireRange += (long)range;
            if (range < 200f)
                _fireNear++;
            else if (range <= 1000f)
                _fireMid++;
            else
                _fireFar++;
        }

        // ------------------------------------------------------------------ the footwork

        /// <summary>In sight: the band of the weapon in hand, the pipes' (400 to 1000) or the stickies', the melee weapon's reach.</summary>
        public override bool FightInSight()
        {
            if (D.HoldingMelee || (!Kit.Pipes && !Kit.Stickies))
                return D.FightAtRange();
            (float near, float far) = HoldingStickies ? (TFDemomanClass.StickyBandNear, TFDemomanClass.StickyBandFar)
                                      : Kit.Shatter ? (TFDemomanClass.LochNear, TFDemomanClass.LochFar) : (TFDemomanClass.PipeNear, TFDemomanClass.PipeFar);
            float d = D._targetDist;
            float approach = d > far ? 0.8f : d < near ? -0.6f : 0f;
            if (D.Feel.Fight)
                D.FightStep(approach);
            else
                D.Strafe(approach);
            D._verdict = approach > 0f ? "closing" : approach < 0f ? "backing off" : "holding range";
            return true;
        }

        // ------------------------------------------------------------------ the sentry kill

        /// <summary>
        /// The sentry kill's score: <see cref="TFDemomanClass.SentryUtility"/> while a sentry the bot saw within its shy time
        /// stands within <see cref="TFDemomanClass.SentryReach"/>, the bot carries stickies (or pipes, with none), is not
        /// carrying the flag, and its health is over <see cref="TFDemomanClass.SentryHealthShare"/>; under a fight near and
        /// the heal, over the objectives' branches (stock picks the Demoman against a sentry, tf_bot.cpp:1212-1235; going for it before the point is this lane's).
        /// </summary>
        public override float PlaceUtility()
        {
            Refresh();
            return PickSentry() ? TFDemomanClass.SentryUtility : TrapUtility();
        }

        /// <summary>To the sentry's stand, past its range on the bot's side; there, held, the hands lay the stickies.</summary>
        public override bool GoPlace()
        {
            if (_sentryId == int.MinValue)
                return GoTrap();
            float range = Knowledge.TFKnowledge.SentryRange;
            AiVector self = D._self.Origin;
            float d = Flat(self - _sentryAt);
            if (SentryInSight(out _) && d > range && d < range + TFDemomanClass.SentryStandOff * 3f)
            {
                D._move = MoveIntent.Hold;
                D._verdict = "stickies at a sentry";
                return true;
            }

            AiVector away = new AiVector(self.X - _sentryAt.X, self.Y - _sentryAt.Y, 0f).Normalised();
            if (away.LengthSquared < 0.5f)
                away = new AiVector(1f, 0f, 0f);
            D.GoTo(_sentryAt + away * (range + TFDemomanClass.SentryStandOff), MoveSpeed.Run);
            D._verdict = "to a sentry's stand";
            return true;
        }

        /// <summary>Chooses the sentry to kill (the nearest the rule allows), keeps it while it holds, gives it up after its time.</summary>
        private bool PickSentry()
        {
            _sentryGiveUps.RemoveAll(g => D._now >= g.Until);
            if ((!Kit.Stickies && !Kit.Pipes) || D.Carrying || D.HealthShare <= TFDemomanClass.SentryHealthShare)
                return ForgetSentry();
            SeenSentry? best = null;
            float bestD = TFDemomanClass.SentryReach;
            foreach (SeenSentry s in D.Sentries)
            {
                if (s.Id < 0 || (D._now - s.Last) * D._tick >= SentryShySeconds || GivenUp(s.Id))
                    continue;
                float d = D._self.Origin.DistanceTo(s.At);
                if (s.Id == _sentryId)
                    d *= 0.5f;
                if (d < bestD)
                    (bestD, best) = (d, s);
            }

            if (best is not { } pick)
                return ForgetSentry();
            if (pick.Id != _sentryId)
                (_sentryId, _sentrySince, _sentryLooking) = (pick.Id, D._now, false);
            (_sentryAt, _sentryLevel) = (pick.At, pick.Level);
            if ((D._now - _sentrySince) * D._tick > TFDemomanClass.SentryGiveUpSeconds)
            {
                _sentryGiveUps.Add((pick.Id, D._now + (int)(SentryShySeconds / D._tick)));
                return ForgetSentry();
            }

            return true;
        }

        private bool GivenUp(int id)
        {
            foreach ((int Id, Tick _) g in _sentryGiveUps)
            {
                if (g.Id == id)
                    return true;
            }

            return false;
        }

        private bool ForgetSentry()
        {
            _sentryId = int.MinValue;
            return false;
        }

        /// <summary>
        /// A sentry the bot looks at from within its sight range, its place in clear sight (<see cref="PlaceInClearSight"/>: no
        /// wall between, the view not full), and does not see for <see cref="TFDemomanClass.SentryGoneSeconds"/> is gone
        /// (destroyed, or carried off): the decider forgets it, so no branch keeps out of a range nothing covers.
        /// </summary>
        private void SeeSentryGone()
        {
            AiVector eye = D.OwnEye;
            AiVector to = _sentryAt + new AiVector(0f, 0f, 32f) - eye;
            float dist = to.Length;
            bool facing = dist > 1f && dist < 2000f && VisionMath.Forward(D._self.Eye).Dot(to * (1f / dist)) > 0.9f;
            if (!facing || SentryInSight(out _) || !PlaceInClearSight(_sentryAt))
            {
                _sentryLooking = false;
                return;
            }

            if (!_sentryLooking)
                (_sentryLooking, _sentryLookedSince) = (true, D._now);
            if ((D._now - _sentryLookedSince) * D._tick < TFDemomanClass.SentryGoneSeconds)
                return;
            D.DemomanForgetSentry(_sentryId);
            _sentriesGone++;
            ForgetSentry();
        }

        /// <summary>The weapon a sentry is killed with: the Loch-n-Load's pipes (direct hits, its building bonus), else the stickies, else the pipes; -1 for none with a shot.</summary>
        private int SentryWeapon()
        {
            int primary = Kit.Pipes ? D.PrimaryKind : -1, sticky = StickyKind;
            if (Kit.Shatter && primary >= 0 && HasAmmo(primary))
                return primary;
            if (sticky >= 0 && HasAmmo(sticky))
                return sticky;
            return primary >= 0 && HasAmmo(primary) ? primary : -1;
        }

        /// <summary>
        /// Whether the bot would see a building at <paramref name="at"/> now: the map's sight table says its cell sees that
        /// cell (no wall between), and its view is not full (a view of as many things as it holds may have left the building
        /// out). With no table, never: nothing is forgotten on a guess.
        /// </summary>
        private bool PlaceInClearSight(AiVector at)
        {
            if (D._graph is not { } graph || D._memory.World.Sight is not { } sight || D._view.ThingCount >= D._view.ThingCapacity
                || D._self.Origin.DistanceTo(at) > sight.Range)
                return false;
            CellId from = graph.FindCell(D._self.Origin), there = graph.FindCell(at);
            return from.IsValid && there.IsValid && sight.Visible(from.Index, there.Index);
        }

        /// <summary>Whether the sentry being killed is in the bot's sight now, and where.</summary>
        private bool SentryInSight(out AiVector at)
        {
            at = default;
            if (_sentryId == int.MinValue)
                return false;
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind == ThingKind.Building && t.Id == _sentryId)
                {
                    at = t.Origin;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Stickies lobbed onto the sentry in sight from past its range, until enough lie by it (the detonation waits for them at
        /// rest). From the stand a tap cannot reach (<see cref="TFDemomanClass.StickyLevelReach"/>), so the throw is charged as
        /// stock's tf_bot charges it (<c>CTFBotStickybombSentrygun</c>, tf_bot_stickybomb_sentrygun.cpp:183-200, 225-259): the
        /// least speed whose arc lands on it, with <see cref="TFDemomanClass.SentryChargeMargin"/>, held for its charge
        /// (<see cref="TFDemomanClass.StickyChargeFor"/>) and let go; the look is that speed's arc, the air's drag flown
        /// (<see cref="AimLead.LobLaunchDragged"/>: a drag-free aim falls about 120 u short from the stand). No speed reaching it, no throw.
        /// </summary>
        private void StickyAtSentry(AiVector sentry)
        {
            if (!D.HeldFacts(out WeaponFacts own) || own.ProjectileSpeed <= 0f)
                return;
            AiVector eye = D.OwnEye;
            AiVector aim = sentry + new AiVector(0f, 0f, 8f);
            if (TFDemomanClass.SentryThrow(eye, aim, in own) is not (float speed, AiVector point))
            {
                _chargeSince = new Tick(-1);
                D._look = new LookIntent(LookKind.Point, aim, ActorId.None, AimPoint.Body, 0f, 0);
                D._verdict = "no arc reaches the sentry";
                return;
            }

            D._look = new LookIntent(LookKind.Point, point, ActorId.None, AimPoint.Body, 0f, 0);
            int near = 0;
            for (int i = 0; i < _outCount; i++)
                near += Flat(_out[i].At - _sentryAt) <= TFDemomanClass.SentryNear ? 1 : 0;
            if (near >= SentryNeed || D._self.Clip <= 0 || Flat(eye - sentry) < Knowledge.TFKnowledge.SentryRange || !LobSettled(point))
            {
                _chargeSince = new Tick(-1);
                return;
            }

            // The held launcher's own full-charge time (the kit's GetChargeMaxTime), not the catalog's 4 s for every sticky launcher.
            float hold = TFDemomanClass.StickyChargeFor(speed, Kit.StickyChargeSeconds);
            if (hold <= 0f)
            {
                Fire(aim, eye, speed);
                return;
            }

            // The charge: the trigger held from the first decide that settled on the arc, let go once the charge is in.
            if (_chargeSince.Value < 0 || D._now - _chargeSeenAt > DecideSchedule.Period * 2)
                _chargeSince = D._now;
            _chargeSeenAt = D._now;
            if ((D._now - _chargeSince) * D._tick < hold)
            {
                D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
                D._verdict = "charging a sticky at a sentry";
                return;
            }

            // Let go: no fire this decide throws it; booked as the shot the clip's drop names.
            _chargeSince = new Tick(-1);
            D.Shots++;
            _aimedSticky = true;
            _stickyAim = aim;
            _stickyFlight = eye.DistanceTo(aim) / MathF.Max(1f, speed) * 1.2f + 0.1f;
        }

        private Tick _chargeSince = new(-1), _chargeSeenAt;

        /// <summary>Pipes lobbed at the sentry in sight from past its range (a kit with no stickies, or none loaded).</summary>
        private void PipeAtSentry(AiVector sentry)
        {
            if (!D.HeldFacts(out WeaponFacts own) || own.ProjectileSpeed <= 0f)
                return;
            AiVector eye = D.OwnEye;
            AiVector aim = sentry + new AiVector(0f, 0f, 24f);
            AiVector point = AimLead.LobLaunchDragged(eye, aim, own.ProjectileSpeed, own.ProjectileGravity, own.LaunchUp, Knowledge.TFKnowledge.GrenadeDrag)
                             ?? AimLead.LobLaunch(eye, aim, own.ProjectileSpeed, own.ProjectileGravity, own.LaunchUp) ?? aim;
            D._look = new LookIntent(LookKind.Point, point, ActorId.None, AimPoint.Body, 0f, 0);
            if ((own.HasClip && D._self.Clip <= 0) || Flat(eye - sentry) < Knowledge.TFKnowledge.SentryRange || !LobSettled(point))
                return;
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
            D.Shots++;
        }

        // ------------------------------------------------------------------ the life

        public override void Died()
        {
            _outCount = 0;
            (_primaryClip, _secondaryClip, _lastWeapon, _lastClip) = (-1, -1, -1, -1);
            _travelKnown = false;
            _lastHealth = -1;
            _chargeUntil = new Tick(-1);
            ForgetSentry();
        }

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new(TFDemomanClass.StickiesLaid, _laid));
            into.Add(new(TFDemomanClass.Detonations, _detonations));
            into.Add(new(TFDemomanClass.SentryDetonations, _sentryDetonations));
            into.Add(new(TFDemomanClass.SentriesGone, _sentriesGone));
            into.Add(new(TFDemomanClass.FireDecides, _fires));
            into.Add(new(TFDemomanClass.FireRangeSum, _fireRange));
            into.Add(new(TFDemomanClass.FireNear, _fireNear));
            into.Add(new(TFDemomanClass.FireMid, _fireMid));
            into.Add(new(TFDemomanClass.FireFar, _fireFar));
            into.Add(new(TFDemomanClass.Travel, _travel));
            into.Add(new(TFDemomanClass.AliveDecides, _alive));
            into.Add(new(TFDemomanClass.TrapLaid, _trapLaid));
            into.Add(new(TFDemomanClass.WatchDecides, _watchDecides));
            into.Add(new(TFDemomanClass.InViewTurns, _inViewTurns));
            into.Add(new(TFDemomanClass.Charges, _charges));
            into.Add(new(TFDemomanClass.ChargeSwings, _chargeSwings));
            into.Add(new(TFDemomanClass.HeadsMost, _headsMost));
            into.Add(new(TFDemomanClass.AirDetonations, _airDetonations));
        }
    }

    /// <summary>The Demoman's: forgets the remembered sentry <paramref name="id"/>, which the bot looked for where it stood and did not see.</summary>
    private void DemomanForgetSentry(int id)
    {
        int n = 0;
        for (int i = 0; i < _sentryCount; i++)
        {
            if (_sentries[i].Id != id)
                _sentries[n++] = _sentries[i];
        }

        _sentryCount = n;
    }
}
