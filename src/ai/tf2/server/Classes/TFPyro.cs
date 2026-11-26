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
/// The Pyro's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Pyro lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFPyroClass
{
    /// <summary>
    /// Whether the composition fields the Pyro (C.4): yes. On the harness (4 pinned Stock Pyros against 4 pinned Soldiers on
    /// the same names, times from the counted round's start) the Pyro reached cp_granary's objective first in every run
    /// (34.0 s against 41.3 s at 77dcae0dc, 32.0 against 40.3 after the airblast tactics at a1167312c). On ctf_2fort the
    /// two are level: 6 runs at 77dcae0dc gave the Pyro a median of 83.0 s against 70.5 s (means 74.7 and 71.9), inside the
    /// Soldier control's own 50-96 s, and 3 runs after the tactics 67.3 s against 83.0 s (means).
    /// </summary>
    public const bool Ready = true;

    /// <summary>The compression blast: the flamethrower's second trigger, pressed for two commands (the Über's shape).</summary>
    public const string Airblast = "pyro_airblast";

    /// <summary>
    /// The Pyro's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() =>
    [
        new AbilityRow(Airblast, AbilityRun.Buttons, Buttons: (int)SourceSharp.Abi.InputButtons.Attack2, HoldTicks: 2),
        new AbilityRow(Mmmph, AbilityRun.Buttons, Buttons: (int)SourceSharp.Abi.InputButtons.Attack2, HoldTicks: 2),
    ];

    /// <summary>The Phlogistinator's taunt into crits: the same second trigger as the blast, which that flamethrower lacks, named apart for the counters.</summary>
    public const string Mmmph = "pyro_mmmph";

    /// <summary>The flamethrower's item class, as the kit names it (every flamethrower: stock, Backburner, Degreaser, Phlogistinator, reskins).</summary>
    public const string FlamethrowerClass = "tf_weapon_flamethrower";

    /// <summary>The archetypes' names (research pyro.md §2): stock's "W+M1" ambusher (A1), the guard by a teammate (A2), the Degreaser/Flare/Axtinguisher combo (A3), the Backburner (A4), the Phlogistinator (A5).</summary>
    public const string Stock = "Stock", Guard = "Guard", Combo = "Combo", Backburner = "Backburner", Phlog = "Phlog";

    /// <summary>
    /// The Pyro's archetypes (C.3.1), drawn by the bot's seed and weight (research §4.2's starting weights): Stock 0.40
    /// (Flame Thrower 21, the Pyro's Shotgun 12, Fire Axe 2), Guard 0.25 (Flame Thrower, Shotgun, Homewrecker 153, Maul 466
    /// or Neon Annihilator 813/834), Combo 0.20 (Degreaser 215, Flare
    /// Gun 39/1081 or Scorch Shot 740, Axtinguisher 38/457/1000), Backburner 0.10 (40/1146, Shotgun 12, Axtinguisher),
    /// Phlog 0.05 (Phlogistinator 594, Shotgun 12 or Reserve Shooter 415, Powerjack 214 or Fire Axe 2). Each preferred item
    /// stands for its plain reskins.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Pyro,
    [
        new TFArchetype(Stock, 0.40f, [new(TFLoadoutSlot.Primary, [21]), new(TFLoadoutSlot.Secondary, [12]), new(TFLoadoutSlot.Melee, [2])]),
        new TFArchetype(Guard, 0.25f, [new(TFLoadoutSlot.Primary, [21]), new(TFLoadoutSlot.Secondary, [12]), new(TFLoadoutSlot.Melee, [153, 466, 813, 834])]),
        new TFArchetype(Combo, 0.20f, [new(TFLoadoutSlot.Primary, [215]), new(TFLoadoutSlot.Secondary, [39, 1081, 740]), new(TFLoadoutSlot.Melee, [38, 457, 1000])]),
        new TFArchetype(Backburner, 0.10f, [new(TFLoadoutSlot.Primary, [40, 1146]), new(TFLoadoutSlot.Secondary, [12]), new(TFLoadoutSlot.Melee, [38, 457, 1000])]),
        new TFArchetype(Phlog, 0.05f, [new(TFLoadoutSlot.Primary, [594]), new(TFLoadoutSlot.Secondary, [12, 415]), new(TFLoadoutSlot.Melee, [214, 2])]),
    ]);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Pyro, Ready, Abilities(), Archetypes());
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Pyro's play (research pyro.md §4.3, stock <c>tf_bot</c>'s Pyro): the flame only inside its reach, closing to
    /// stock's wanted range and circling there, never backing off a fight; the secondary past stock's 750 u, the
    /// flamethrower back against a Soldier or a Demoman (whose spam it reflects); and the compression blast
    /// (TFPyro.Airblast.cs): reflects, shoves and putting out a burning teammate.
    /// </summary>
    private sealed partial class PyroPlay(TFDecider d) : ClassPlay(d, TFClass.Pyro)
    {
        /// <summary>
        /// The flame is fired at a target this near (stock <c>CTFBot::GetMaxAttackRange</c>'s flamethrower, 250 u,
        /// tf_bot.cpp:3139-3156; the port's flame points travel about 287 u, TFFlameManager's drag, so 250 lands most of it).
        /// </summary>
        public const float FlameReach = 250f;

        /// <summary>The range the Pyro closes to (stock <c>GetDesiredAttackRange</c>'s flamethrower, tf_bot.cpp:3196-3199).</summary>
        public const float FlameDesired = 100f;

        /// <summary>Inside this share of <see cref="FlameDesired"/> the Pyro circles its target (stock tf_bot_attack.cpp:47).</summary>
        public const float CircleShare = 1.1f;

        /// <summary>The circle's side is drawn again this often (stock's <c>TransientlyConsistentRandomValue( 3.0f )</c>, tf_bot_attack.cpp:49).</summary>
        public const float CircleSeconds = 3f;

        /// <summary>Past this the secondary is out (stock <c>EquipBestWeaponForThreat</c>'s Pyro, tf_bot.cpp:3404-3408); back under <see cref="PrimaryUnder"/>.</summary>
        public const float SecondaryPast = 750f, PrimaryUnder = 650f;

        /// <summary>Once the flame is on a target it stays on this long (stock <c>tf_bot_fire_weapon_min_time</c>, tf_bot_behavior.cpp:39).</summary>
        public const float FlameHoldSeconds = 1f;

        /// <summary>How far ahead the flame is led on a moving target: its flight to <see cref="FlameReach"/>, about.</summary>
        public const float FlameLeadSeconds = 0.1f;

        /// <summary>
        /// A target in sight is closed on in a straight line only within this, within <see cref="StraightRise"/> of the bot's
        /// height and with room toward it; further, higher or lower, or past a wall, the bot goes by path.
        /// </summary>
        public const float StraightReach = 400f, StraightRise = 48f;

        /// <summary>The Pyro's weapon changes are spaced this far apart, not the general 1 s: a flamethrower and a shotgun change on the range.</summary>
        public const float PyroSwitchSeconds = 0.5f;

        private Tick _flameUntil;
        private bool _secondaryWanted;

        /// <summary>The flamethrower's reserve when it was last in hand (the HUD shows only the held weapon's); full at a spawn.</summary>
        private int _flameAmmo = int.MaxValue;
        private long _flameDecides, _nearFights, _farFights, _secondaryOuts, _pathChases;

        public override float SwitchSeconds => IsCombo ? ComboSwitchSeconds : PyroSwitchSeconds;

        /// <summary>Whether the kit's primary is a flamethrower, and it is in the bot's hands.</summary>
        private bool HoldingFlamethrower => Flamethrower(out Senses.TFKitItem p) && p.WeaponKind == D._self.ActiveWeapon;

        /// <summary>The flamethrower the kit carries, if its primary is one.</summary>
        private bool Flamethrower(out Senses.TFKitItem item)
        {
            item = null!;
            return D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Primary, out item) && item.ItemClass == TFPyroClass.FlamethrowerClass;
        }

        /// <summary>The class the bot's client draws the target as (a disguise's), Undefined out of sight.</summary>
        private TFClass TargetClass
            => D._target is { } t && D._view.TryActor(t.Id, out ActorFacts f) ? (TFClass)f.Class : TFClass.Undefined;

        /// <summary>
        /// The weapon, then the trigger, on a target in sight. The weapon first (stock <c>EquipBestWeaponForThreat</c>), so a
        /// held trigger never keeps the wrong one out: the flamethrower near, the secondary past <see cref="SecondaryPast"/>
        /// (back under <see cref="PrimaryUnder"/>), the flamethrower whatever the range against a Soldier or a Demoman (whose
        /// spam it reflects), the secondary against an enemy Pyro, whom afterburn does not hurt. Then the flame: aimed at the
        /// target's middle, led by its short flight, the trigger held within <see cref="FlameReach"/>, and for
        /// <see cref="FlameHoldSeconds"/> once begun while the target stays near. Any other weapon in hand fires as the general
        /// hands fire it.
        /// </summary>
        public override void Engage()
        {
            if (D._targetSighted && D._target is not null && !D.Busy && D._now >= D._handsAt && WeaponFor(out int want) && want != D._self.ActiveWeapon)
            {
                D.Select(want);
                return;
            }

            if (!HoldingFlamethrower)
            {
                D.GeneralEngage();
                return;
            }

            if (!D._targetSighted || D._target is null || D.Busy)
                return;
            var aim = new AiVector(D._targetPos.X, D._targetPos.Y, D.AimHeight(false)) + D._targetVel * FlameLeadSeconds;
            aim += D.AimError(aim, false);
            D._look = new LookIntent(LookKind.Actor, aim, D._target.Id, AimPoint.Body, 0f, 0);
            if (TargetClass == TFClass.Pyro && D.SecondaryKind >= 0)
                return;
            if (IsBackburner && !BackburnerMayFlame())
                return;
            bool held = D._now < _flameUntil && D._targetDist < FlameReach * 1.3f;
            if (D._targetDist > FlameReach && !held)
                return;
            if (D._targetDist <= FlameReach && D._now >= _flameUntil)
                _flameUntil = D._now + (int)(FlameHoldSeconds / D._tick);
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
            D.Shots++;
            _flameDecides++;
            if (IsBackburner && TargetFacesAway)
                _backFlames++;
        }

        /// <summary>The weapon the Pyro wants out against its target in sight (<see cref="Engage"/>'s choice); false with no flamethrower or a melee weapon in hand.</summary>
        private bool WeaponFor(out int want)
        {
            want = -1;
            if (!Flamethrower(out Senses.TFKitItem flame) || (D.HoldingMelee && !IsCombo))
                return false;
            int secondary = D.SecondaryKind;
            TFClass them = TargetClass;
            if (flame.WeaponKind == D._self.ActiveWeapon)
                _flameAmmo = D._self.Reserve;
            if (IsCombo)
            {
                want = ComboWeapon(in flame, them);
                return true;
            }

            _secondaryWanted = D._targetDist > SecondaryPast || (_secondaryWanted && D._targetDist > PrimaryUnder);
            bool wantSecondary = secondary >= 0
                                 && (_flameAmmo <= 0 || (them != TFClass.Soldier && them != TFClass.Demoman && (_secondaryWanted || them == TFClass.Pyro)));
            want = wantSecondary ? secondary : flame.WeaponKind;
            if (wantSecondary && want != D._self.ActiveWeapon)
                _secondaryOuts++;
            return true;
        }

        /// <summary>In a lull, the general hands (the primary back, a reload); with a target in sight the weapon is <see cref="Engage"/>'s.</summary>
        public override void Hands()
        {
            if (!D._targetSighted)
                D.GeneralHands();
        }

        /// <summary>
        /// The footwork in a fight: closing on the target to <see cref="FlameDesired"/>, and inside it circling, the side drawn
        /// every <see cref="CircleSeconds"/> (stock tf_bot_attack.cpp:47-60). Never backing off (stock's Pyro never retreats in
        /// a fight, tf_bot_seek_and_destroy.cpp:159-167); a melee weapon in hand keeps its reach as the general play does.
        /// </summary>
        public override bool FightInSight()
        {
            if (D.HoldingMelee)
                return D.FightAtRange();
            if ((IsCombo && HoldFlareRange()) || (IsBackburner && Flank()))
                return true;
            if (D._targetDist <= FlameReach)
                _nearFights++;
            else
                _farFights++;
            AiVector to = new AiVector(D._targetPos.X - D._self.Origin.X, D._targetPos.Y - D._self.Origin.Y, 0f).Normalised();
            var across = new AiVector(-to.Y, to.X, 0f);
            if (D._targetDist > CircleShare * FlameDesired)
            {
                // Stock chases by path past the wanted range (CTFBotAttack::Update, tf_bot_attack.cpp:63-79). Only a near
                // target on the bot's own level with room toward it is closed on in a straight line, the view on it.
                if (D._targetDist > StraightReach || MathF.Abs(D._targetPos.Z - D._self.Origin.Z) > StraightRise || D.NoRoom(to))
                {
                    D.GoTo(D._targetPos, MoveSpeed.Run);
                    D._verdict = "chasing by path";
                    _pathChases++;
                    return true;
                }

                D._move = new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, to, false, 0);
                D._verdict = "closing";
                return true;
            }

            int side = Consistent(CircleSeconds, 0x43495243) < 0.5f ? 1 : -1;
            if (D.NoRoom(across * side))
                side = -side;
            D._move = new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, (across * side + to * 0.2f).Normalised(), false, 0);
            D._verdict = "circling";
            return true;
        }

        /// <summary>
        /// A Pyro that can shove the Über in front of it this decide (skilled now, the flamethrower out with the blast's cost
        /// in it, the Über in reach: <see cref="OneShots"/>) does not back away from him; one that cannot backs off as every
        /// class does. Nor does it step out of a blast it is lining up to reflect.
        /// </summary>
        public override bool TakesReflex(string verdict)
            => verdict switch
            {
                InvulnerableVerdict => !_uberShovable,
                DodgeVerdict => !_reflectingNow,
                _ => true,
            };

        /// <summary>
        /// A value in [0, 1) the same for the bot over each <paramref name="period"/> seconds and new in the next (stock's
        /// <c>TransientlyConsistentRandomValue</c>): drawn from the bot's seed, <paramref name="salt"/> and the period's number.
        /// </summary>
        private float Consistent(float period, int salt)
        {
            long window = (long)(D._now.Value * D._tick / period);
            ulong mixed = AiRandom.SeedFor(D._seed ^ (ulong)(uint)salt, (int)window);
            return (mixed >> 40) * (1f / (1 << 24));
        }

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("pyro.flame_decides", _flameDecides));
            into.Add(new("pyro.near_fight_decides", _nearFights));
            into.Add(new("pyro.far_fight_decides", _farFights));
            into.Add(new("pyro.secondary_outs", _secondaryOuts));
            into.Add(new("pyro.path_chases", _pathChases));
            CountAirblasts(into);
            CountArchetypes(into);
            CountGuard(into);
            into.Add(new("pyro.taunts", _taunts));
        }

        public override void Died()
        {
            _flameUntil = default;
            _secondaryWanted = false;
            _flameAmmo = int.MaxValue;
            AirblastDied();
            GuardDied();
        }
    }
}
