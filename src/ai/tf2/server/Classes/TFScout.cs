//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Scout's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Scout lane's file:
/// another class's lane never edits it, and it never edits another's. The numbers are scout.md §4.3's (the P7 research,
/// with its Fable review's fixes), each with its source there.
/// </summary>
public static class TFScoutClass
{
    /// <summary>
    /// Whether the composition fields the Scout (C.4): its play is the job (P7 Scout lane, ai-tf-class, 4 pinned Flanker Scouts
    /// against 4 Soldiers, 3 runs a map, 300 s, timed from the counted round's start: `tools/ai-tf-class --rescore`, the round
    /// restarting at 36-38 s): first on the objective on cp_granary at 23/22/23 s against 40/59/39 s, on
    /// ctf_2fort at 47/35/37 s against 46/116/72 s.
    /// </summary>
    public const bool Ready = true;

    /// <summary>
    /// The Scout's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only. None: a drink is the
    /// drink taken out and fired, as a player does it, and the double jump a move.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() => [];

    /// <summary>The archetypes' names (scout.md §2, §4.1).</summary>
    public const string Flanker = "Flanker", Bonk = "Bonk", ForceANature = "FaN", BabyFace = "BFB";

    /// <summary>The stock Scattergun, Pistol and Bat (each standing for its reskins); Bonk! Atomic Punch; the Force-a-Nature; the Baby Face's Blaster.</summary>
    public const int Scattergun = 13, Pistol = 23, Bat = 0, BonkDrink = 46, FaNGun = 45, BabyFaceGun = 772;

    /// <summary>
    /// The Scout's archetypes (C.3.1, scout.md §4.1-4.2), drawn by the bot's seed and weight. Flanker is stock (the comp
    /// and pub default), the others the research's next in its ship order: the Bonk runner, the Force-a-Nature brawler
    /// and the BFB runner. The weights are the research's starting point (Flanker 40, Bonk 20, FaN 20), not a measure. The BFB
    /// runner is drawn rarely (weight 1 of 81; a pin plays it): on ctf_2fort it took the flag later than the Flanker (first on
    /// the objective 40/37/60 s against 36/35/37 from the round's start over 3 runs; scout.md §4.3 E's boost-building before a run is not built, and its base
    /// speed is 360 to the Flanker's 400). Every slot is named, so a Flanker never finds a cleaver in its secondary.
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Scout,
    [
        new TFArchetype(Flanker, 40f, [new(TFLoadoutSlot.Primary, [Scattergun]), new(TFLoadoutSlot.Secondary, [Pistol]), new(TFLoadoutSlot.Melee, [Bat])]),
        new TFArchetype(Bonk, 20f, [new(TFLoadoutSlot.Primary, [Scattergun]), new(TFLoadoutSlot.Secondary, [BonkDrink]), new(TFLoadoutSlot.Melee, [Bat])]),
        new TFArchetype(ForceANature, 20f, [new(TFLoadoutSlot.Primary, [FaNGun]), new(TFLoadoutSlot.Secondary, [Pistol]), new(TFLoadoutSlot.Melee, [Bat])]),
        new TFArchetype(BabyFace, 1f, [new(TFLoadoutSlot.Primary, [BabyFaceGun]), new(TFLoadoutSlot.Secondary, [Pistol]), new(TFLoadoutSlot.Melee, [Bat])]),
    ]);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Scout, Ready, Abilities(), Archetypes());

    /// <summary>
    /// A Scattergun's band: closes when further than <see cref="ScatterFar"/> (two point-blank shots kill; at 1,024 u a
    /// pellet does half, scout.md §1), and never backs off inside it. The Force-a-Nature's: under <see cref="FaNFar"/>, so
    /// its knockback lands (stock's over 30 damage within 400 u, the review's S2; the research's hold under 200). The
    /// Shortstop's, longer (§4.3: 200-600).
    /// </summary>
    public const float ScatterFar = 350f, FaNFar = 200f, ShortstopFar = 600f, PistolFar = 900f;

    /// <summary>
    /// How far each is fired at (§4.3 "fire reach"): a Scattergun past <see cref="ScatterReach"/> does under 30 a shot; the
    /// FaN only within <see cref="FaNReach"/> (§4.3 C); the Pistol to <see cref="PistolReach"/>. Stock fires a bullet weapon
    /// at any range; the research rules that a bot of ours does not.
    /// </summary>
    public const float ScatterReach = 900f, FaNReach = 250f, PistolReach = 1500f;

    /// <summary>
    /// The weapon choice (§4.3): the Pistol when the target is further than <see cref="PistolFrom"/> (stock's 500 u bullet
    /// "desired range" plus a margin) or the primary is dry; back to the primary inside <see cref="PrimaryWithin"/> with
    /// shells in it; the Bat when both guns are dry and the target within <see cref="BatFrom"/> (stock's melee desired range,
    /// tf_bot.cpp:3190-3193).
    /// </summary>
    public const float PistolFrom = 600f, PrimaryWithin = 450f, BatFrom = 100f;

    /// <summary>
    /// The objectives' lean (§4.3 "objective"): a Scout caps a point at twice the rate (mp_capstyle 1,
    /// TFCapturePlayerRules), so the points stance weighs it <see cref="PointsLean"/> over a Soldier's (0.5 to 0.65: over a
    /// fight further than 900 u, under one nearer). The flag <see cref="FetchLean"/>: the research's 1.3 would lift the
    /// fetch's 0.7-0.9 over a near fight's 0.8 everywhere, and the attacker would never stop for an enemy within
    /// <see cref="TFDecider.AttackerStopsFor"/>; 1.1 keeps that stop at the far end (0.77) and runs on near the flag. The
    /// BFB runner's <see cref="BabyFaceFetchLean"/> (§4.3 E) runs on past a near fight from mid-way. Starting values to measure.
    /// </summary>
    public const float PointsLean = 1.3f, FetchLean = 1.1f, BabyFaceFetchLean = 1.2f;

    /// <summary>The share of its health under which a Scout in a lull goes for a pack: it has no sustain of its own (§1), and 125 HP.</summary>
    public const float HurtShare = 0.5f;

    /// <summary>
    /// Target priority (§4.3 "targets", over the general Medic bonus): a Sniper or a Demoman more (the comp Scout's main
    /// targets); a Heavy within <see cref="HeavyNear"/> or a Pyro within <see cref="PyroNear"/> less.
    /// </summary>
    public const float PickBonus = 0.2f, AvoidBonus = -0.3f, HeavyNear = 600f, PyroNear = 350f;

    /// <summary>
    /// The dodge (§4.3, C.3.2's Scout row): watched by its target, at a sidestep's change of side a Scout hops with chance <see cref="HopNear"/>
    /// under <see cref="HopNearUnits"/>, <see cref="HopMid"/> out to <see cref="HopMidUnits"/> (close favours the jump, mid
    /// range stays grounded), and a hop is a double jump: let go, then pressed again toward the other side in the air.
    /// Never the second press against a Soldier or a Demoman within <see cref="SplashNear"/> (their splash punishes it).
    /// </summary>
    public const float HopNear = 0.6f, HopMid = 0.1f, HopNearUnits = 250f, HopMidUnits = 600f, SplashNear = 400f;

    /// <summary>A target whose facing is within this of the line to the bot, inside this reach, is watching it: the dodge's cue (scout.md §4.3).</summary>
    public const float WatchedDegrees = 12f, WatchedReach = 1200f;

    /// <summary>The Bonk runner drinks to escape under this share of its health with an enemy in sight within <see cref="EscapeNear"/> (§4.3 B).</summary>
    public const float EscapeShare = 0.4f, EscapeNear = 900f;

    /// <summary>A full drink meter (stock's tf_bot.cpp:4593 gate), and how long the drink may take to come out and be drunk before the bot gives up on it.</summary>
    public const float DrinkFull = 100f, DrinkPatienceSeconds = 2f;
}

/// <summary>
/// The Scout's own HUD numbers (P7, scout.md §4.7 item 4): the drink meter (<c>m_flEnergyDrinkMeter</c>, its HUD's Bonk
/// bar), the hype meter, and whether it is phased (Bonk's own screen effect). Its own player's state only.
/// </summary>
// A hook of the meters chain: registered as itself, the container adds it to every chain it implements (an As list would
// claim ITFHudMeters alone, beside TFMedigunMeters's claim: "registered twice" at the side's composition).
[SideService(Side.Server)]
public sealed class TFScoutMeters(ITFPlayerData players, ITFPlayerConditions conditions) : ITFHudMeters, IOrderedHook
{
    public const string Drink = "scout.drink", Hype = "scout.hype", Phased = "scout.phased";

    /// <summary>
    /// The chain's order must be unique per reader (Hooks.All refuses two at one number, and live that stopped every bot's
    /// senses: TFMedigunMeters is 0 by default). 100 plus the class's number, a block a class's meters can each take.
    /// </summary>
    public const int Order = 100 + (int)TFClass.Scout;

    public int HookOrder => Order;

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass != TFClass.Scout || players.Networked(bot.EntryIndex) is not { } state)
            return;
        into.Add(new TFMeter(Drink, state.EnergyDrinkMeter));
        into.Add(new TFMeter(Hype, state.HypeMeter));
        into.Add(new TFMeter(Phased, conditions.InCond(bot, TFCond.Phase) ? 1f : 0f));
    }
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Scout's play (scout.md §4.3): close in with the Scattergun and not back off from the target, the Pistol past 600 u
    /// or when the primary is dry, each gun fired only within its reach, a double-jump dodge close in, the objectives weighed
    /// for the double cap, Snipers and Demomen before Heavies and Pyros. By archetype: the FaN brawler fights closer and
    /// reloads its two shells in hand; the Bonk runner drinks at a sentry's edge (or to escape) and walks through the range
    /// phased; the BFB runner never spends its boost on an air dash and leans harder on the flag.
    /// <para>A dry primary: the shared fight selector runs "launcher dry" before the class's footwork, but the Scout says no
    /// (<see cref="BacksOffDry"/>, round 2's S10) while its Pistol has shells or the target is within
    /// <see cref="TFScoutClass.BatFrom"/>, so on the decide the primary is first seen empty the fight stays the Scout's own.
    /// The hands answer on that same decide, past their spacing: the Pistol out, or the Bat within
    /// <see cref="TFScoutClass.BatFrom"/>, or (the FaN within its reach) a reload in hand. With neither (the Bonk runner past
    /// a swing's reach) the bot backs off and reloads, as stock does a barrage weapon at clip 1 or less
    /// (tf_bot_tactical_monitor.cpp:266-281).</para>
    /// </summary>
    private sealed class ScoutPlay(TFDecider d) : ClassPlay(d, TFClass.Scout)
    {
        private AiRandom? _scoutHopRng;
        private int _scoutPrimaryClip = -1, _scoutPrimaryReserve = -1, _scoutDashStage, _scoutDashSide;
        private bool _scoutGunEmpty;
        private bool _scoutDrinking;
        private Tick _scoutDrinkSince;
        private bool _scoutWasPhased;
        private long _scoutDrinks, _scoutGunSwaps, _scoutHops, _scoutDashes, _scoutWithheld, _scoutShots, _scoutShotUnits;

        private AiRandom HopRng => _scoutHopRng ??= new AiRandom(D._seed ^ 0x5343_4F55UL);

        /// <summary>The kit item in <paramref name="slot"/>, when the HUD shows one there.</summary>
        private TFKitItem? Item(TFLoadoutSlot slot) => D.Kit is { } kit && kit.TryWielded(slot, out TFKitItem item) ? item : null;

        /// <summary>The secondary's kind when it is a gun (the Pistol, the Winger, the Pocket Pistol); -1 for a drink, a jar, a cleaver or none.</summary>
        private int GunKind => Item(TFLoadoutSlot.Secondary) is { ItemClass: "tf_weapon_pistol" or "tf_weapon_handgun_scout_secondary" } gun ? gun.WeaponKind : -1;

        /// <summary>The secondary's kind when it is a drink (Bonk, Crit-a-Cola); -1 otherwise.</summary>
        private int DrinkKind => Item(TFLoadoutSlot.Secondary) is { ItemClass: "tf_weapon_lunchbox_drink" } drink ? drink.WeaponKind : -1;

        private int MeleeKind => Item(TFLoadoutSlot.Melee)?.WeaponKind ?? -1;

        private bool HoldingPrimary => D.PrimaryKind >= 0 && D._self.ActiveWeapon == D.PrimaryKind;

        private bool HoldingGun => GunKind is >= 0 and var gun && D._self.ActiveWeapon == gun;

        /// <summary>Whether the primary in hand is the Force-a-Nature (its knockback attribute) or the Shortstop.</summary>
        private bool PrimaryIsFaN => Item(TFLoadoutSlot.Primary)?.Has("set_scattergun_has_knockback") ?? false;

        /// <summary>A dry primary is no reason to back off while the Pistol has shells or the target is within a swing (round 2's S10).</summary>
        public override bool BacksOffDry => !GunUsable && D._targetDist >= TFScoutClass.BatFrom;

        private bool PrimaryIsShortstop => Item(TFLoadoutSlot.Primary)?.ItemClass == "tf_weapon_handgun_scout_primary";

        private bool Phased => (D.Kit?.Meter(TFScoutMeters.Phased, 0f) ?? 0f) > 0.5f;

        /// <summary>The primary's shells as the bot last saw them in its hand (no weapon reloads holstered): unknown, taken as loaded, until it has held it.</summary>
        private bool PrimaryLoaded => _scoutPrimaryClip != 0;

        /// <summary>Whether the primary has any shell left, in its clip or its reserve, as last seen: unknown counts as yes.</summary>
        private bool PrimaryHasAmmo => _scoutPrimaryClip != 0 || _scoutPrimaryReserve != 0;

        /// <summary>The Pistol out of shells and reserve both, as last seen in the hand.</summary>
        private bool GunUsable => GunKind >= 0 && !_scoutGunEmpty;

        /// <summary>The fight's band for the weapon in hand: never a near edge to back off from, only a far one to close to.</summary>
        internal float FarEdge()
        {
            if (D.HoldingMelee)
                return MeleeReach * 0.75f;
            if (HoldingPrimary)
                return PrimaryIsFaN ? TFScoutClass.FaNFar : PrimaryIsShortstop ? TFScoutClass.ShortstopFar : TFScoutClass.ScatterFar;
            // The Pistol out with shells in the primary, or the FaN brawler's (it reloads its FaN in hand inside the FaN's reach):
            // the bot closes, and the hands take the primary out on the way.
            if (HoldingGun && D.PrimaryKind >= 0 && (PrimaryLoaded || (PrimaryIsFaN && PrimaryHasAmmo)))
                return PrimaryIsFaN ? TFScoutClass.FaNFar : TFScoutClass.ScatterFar;
            return TFScoutClass.PistolFar;
        }

        /// <summary>How far the weapon in hand is fired at.</summary>
        internal float Reach()
        {
            if (D.HoldingMelee)
                return MeleeReach;
            if (HoldingPrimary)
                return PrimaryIsFaN ? TFScoutClass.FaNReach : TFScoutClass.ScatterReach;
            return HoldingGun ? TFScoutClass.PistolReach : FireReach;
        }

        public override void Engage()
        {
            D.GeneralEngage();
            if (D._action.Kind == ActionKind.Fire && D._targetDist > Reach())
            {
                D._action = ActionIntent.None;
                _scoutWithheld++;
            }
            else if (D._action.Kind == ActionKind.Fire && HoldingPrimary)
            {
                // How far its primary fires from, for the harness: the FaN brawler's shots should come from nearer than the
                // Flanker's Scattergun's (the Pistol's, fired to 1,500 u, are left out).
                _scoutShots++;
                _scoutShotUnits += (long)D._targetDist;
            }
        }

        public override bool FightInSight()
        {
            float far = FarEdge();
            float approach = D._targetDist > far ? 0.8f : 0f;
            int before = D._fightStep.Changes;
            if (D.Feel.Fight)
                D.FightStep(approach);
            else
                D.Strafe(approach);
            Dodge(before);
            D._verdict = approach > 0f ? "closing" : approach < 0f ? "backing off" : "holding range";
            return true;
        }

        /// <summary>
        /// The double-jump dodge: a hop at a change of side (the general step's own, or one of the Scout's by distance), then
        /// on the next decide the jump let go (the step goes on without it), and on the one after, still in the air, pressed
        /// again toward the other side: the Scout's air dash (stock CheckJumpButton dashes on a fresh press in the air).
        /// </summary>
        private void Dodge(int changesBefore)
        {
            bool grounded = (D._self.Flags & SelfFlags.Grounded) != 0;
            if (_scoutDashStage == 1)
            {
                _scoutDashStage = grounded ? 0 : 2;
                return;
            }

            if (_scoutDashStage == 2)
            {
                _scoutDashStage = 0;
                if (grounded || D.Busy)
                    return;
                AiVector to = new AiVector(D._targetPos.X - D._self.Origin.X, D._targetPos.Y - D._self.Origin.Y, 0f).Normalised();
                var across = new AiVector(-to.Y, to.X, 0f) * -_scoutDashSide;
                D._move = new MoveIntent(MoveKind.Jump, MoveSpeed.Run, 1, D._self.Origin + across * FightHopUnits, default, default, default, default, false, 0);
                _scoutDashes++;
                return;
            }

            bool hopped = D._move.Kind == MoveKind.Jump, watched = Watched();
            if (!hopped && watched && D._fightStep.Changes != changesBefore && grounded && !D.Busy && D._move.Kind == MoveKind.Strafe)
            {
                float share = D._targetDist < TFScoutClass.HopNearUnits ? TFScoutClass.HopNear : D._targetDist < TFScoutClass.HopMidUnits ? TFScoutClass.HopMid : 0f;
                if (HopRng.NextFloat() < share * FightStepWeight(in D._p))
                {
                    AiVector dir = D._move.Direction;
                    D._move = new MoveIntent(MoveKind.Jump, MoveSpeed.Run, 1, D._self.Origin + dir * FightHopUnits, default, default, default, default, false, 0);
                    hopped = true;
                }
            }

            if (!hopped)
                return;
            _scoutHops++;
            AiVector toTarget = new AiVector(D._targetPos.X - D._self.Origin.X, D._targetPos.Y - D._self.Origin.Y, 0f).Normalised();
            AiVector way = (D._move.Corner0 - D._self.Origin) with { Z = 0f };
            _scoutDashSide = way.X * -toTarget.Y + way.Y * toTarget.X >= 0f ? 1 : -1;
            bool splash = D._target is { } t && t.ClassBelieved is (int)TFClass.Soldier or (int)TFClass.Demoman && D._targetDist < TFScoutClass.SplashNear;
            _scoutDashStage = !watched || splash || Archetype == TFScoutClass.BabyFace ? 0 : 1;
        }

        /// <summary>
        /// Whether the target in sight looks at the bot: its facing, as every client draws a player's head and aim, within
        /// <see cref="TFScoutClass.WatchedDegrees"/> of the line to the bot, inside <see cref="TFScoutClass.WatchedReach"/>
        /// (scout.md §4.3's dodge; the review's C2 sense, ActorFacts.Facing).
        /// </summary>
        private bool Watched()
        {
            if (D._target is not { } t || !D._targetSighted || D._targetDist > TFScoutClass.WatchedReach || !D._view.TryActor(t.Id, out ActorFacts f))
                return false;
            float toBot = MathF.Atan2(D._self.Origin.Y - f.Origin.Y, D._self.Origin.X - f.Origin.X) * (180f / MathF.PI);
            float off = MathF.Abs(((f.Facing.Yaw - toBot) % 360f + 540f) % 360f - 180f);
            return off <= TFScoutClass.WatchedDegrees;
        }

        public override void Hands()
        {
            if (HoldingPrimary)
                (_scoutPrimaryClip, _scoutPrimaryReserve) = (D._self.Clip, D._self.Reserve);
            if (HoldingGun)
                _scoutGunEmpty = D._self.Clip == 0 && D._self.Reserve == 0;
            // A weapon change wins over this decide's shot (the general hands wait for a decide with none): a Scattergun firing at
            // 800 u is the reason to take the Pistol out.
            if (_scoutDrinking || D._action.Kind is not (ActionKind.None or ActionKind.Fire) || D.Busy)
                return;
            if (D._targetSighted && DryAnswer())
                return;
            if (D._now < D._handsAt)
                return;
            if (!D._targetSighted)
            {
                // A lull: the Pistol reloaded first, when it is out (scout.md §4.3), then the general hands (the primary out, loaded).
                if (D._action.Kind != ActionKind.None)
                    return;
                if (HoldingGun && D.HeldFacts(out WeaponFacts own) && own.HasClip && D._self.Clip < own.ClipSize && D._self.Reserve > 0
                    && (D._self.Flags & SelfFlags.Reloading) == 0)
                {
                    D._action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++D._oneShot);
                    D._handsAt = D._now + (int)(SwitchSeconds / D._tick);
                    return;
                }

                D.GeneralHands();
                return;
            }

            float dist = D._targetDist;
            int primary = D.PrimaryKind, gun = GunKind;
            if (HoldingPrimary)
            {
                if (GunUsable && dist > TFScoutClass.PistolFrom)
                    Take(gun);
                return;
            }

            if (HoldingGun)
            {
                // Back to the primary inside its range with shells in it; the FaN brawler's even dry inside the FaN's reach (it
                // reloads it in hand: two shells are its fight, not the Pistol).
                if (primary >= 0 && (PrimaryLoaded || (PrimaryIsFaN && PrimaryHasAmmo && dist < TFScoutClass.FaNReach)) && dist < TFScoutClass.PrimaryWithin)
                    Take(primary);
                return;
            }

            // The Bat, or a drink handed back: a gun with shells when the target is out of a swing's reach.
            if (dist > TFScoutClass.BatFrom || !D.HoldingMelee)
            {
                if (primary >= 0 && PrimaryLoaded && dist < TFScoutClass.PistolFrom)
                    Take(primary);
                else if (GunUsable)
                    Take(gun);
                else if (primary >= 0 && PrimaryHasAmmo)
                    Take(primary);
            }
        }

        /// <summary>
        /// The hands' answer to the gun in hand gone dry with the target in sight, past the hands' spacing (the shared fight's
        /// "launcher dry" backs the bot off on this decide, TFDecider.cs): the FaN brawler within the FaN's reach reloads in
        /// hand; else the Pistol, when it has shells; else the Bat within <see cref="TFScoutClass.BatFrom"/>; else a reload. The
        /// Pistol dry with no reserve: the primary back out while it has any shell (it reloads in hand), else the Bat.
        /// </summary>
        private bool DryAnswer()
        {
            float dist = D._targetDist;
            int primary = D.PrimaryKind, gun = GunKind, melee = MeleeKind;
            bool reloading = (D._self.Flags & SelfFlags.Reloading) != 0;
            if (HoldingPrimary && D._self.Clip == 0)
            {
                if (PrimaryIsFaN && dist < TFScoutClass.FaNReach && D._self.Reserve > 0)
                    return Reload(reloading);
                if (GunUsable)
                    Take(gun);
                else if (melee >= 0 && dist < TFScoutClass.BatFrom)
                    D.Select(melee);
                else
                    return Reload(reloading);
                return true;
            }

            if (HoldingGun && D._self.Clip == 0 && D._self.Reserve == 0)
            {
                if (primary >= 0 && PrimaryHasAmmo)
                    Take(primary);
                else if (melee >= 0)
                    D.Select(melee);
                else
                    return false;
                return true;
            }

            return false;
        }

        /// <summary>A reload, unless one is under way; true either way (the hands have answered).</summary>
        private bool Reload(bool reloading)
        {
            if (!reloading && D._self.Reserve > 0)
                D._action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++D._oneShot);
            return true;
        }

        private void Take(int kind)
        {
            D.Select(kind);
            _scoutGunSwaps++;
        }

        public override void OneShots()
        {
            bool phased = Phased;
            if (phased && !_scoutWasPhased)
                _scoutDrinks++;
            _scoutWasPhased = phased;
            if (Archetype != TFScoutClass.Bonk || DrinkKind is not (>= 0 and var drink))
                return;
            if (_scoutDrinking)
            {
                if (phased || (D._now - _scoutDrinkSince) * D._tick > TFScoutClass.DrinkPatienceSeconds)
                    _scoutDrinking = false;
                else if (D._self.ActiveWeapon == drink && D._action.Kind == ActionKind.None)
                    D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
                return;
            }

            if (phased || D.Carrying || D.Busy || (D.Kit?.Meter(TFScoutMeters.Drink, 0f) ?? 0f) < TFScoutClass.DrinkFull
                || D._action.Kind is not (ActionKind.None or ActionKind.Fire))
                return;
            bool escape = D._targetSighted && D._targetDist < TFScoutClass.EscapeNear && D.HealthShare < TFScoutClass.EscapeShare;
            if (!escape && !D.WalksIntoASentry())
                return;
            D.Select(drink);
            _scoutDrinking = true;
            _scoutDrinkSince = D._now;
        }

        /// <summary>Phased, the Bonk runner walks through a sentry's range: the sentry cannot hurt it (scout.md §4.3 B).</summary>
        public override bool AvoidsSentries => !Phased;

        public override float Weigh(string branch) => branch switch
        {
            PointsBranch => TFScoutClass.PointsLean,
            Fetch => Archetype == TFScoutClass.BabyFace ? TFScoutClass.BabyFaceFetchLean : TFScoutClass.FetchLean,
            _ => 1f,
        };

        public override float TargetBonus(KnownActor target, bool sighted, float distance) => target.ClassBelieved switch
        {
            (int)TFClass.Sniper or (int)TFClass.Demoman => TFScoutClass.PickBonus,
            (int)TFClass.HeavyWeapons when distance < TFScoutClass.HeavyNear => TFScoutClass.AvoidBonus,
            (int)TFClass.Pyro when distance < TFScoutClass.PyroNear => TFScoutClass.AvoidBonus,
            _ => 0f,
        };

        public override float HurtShare => TFScoutClass.HurtShare;

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("scout.drinks", _scoutDrinks));
            into.Add(new("scout.gun_swaps", _scoutGunSwaps));
            into.Add(new("scout.hops", _scoutHops));
            into.Add(new("scout.dashes", _scoutDashes));
            into.Add(new("scout.withheld", _scoutWithheld));
            into.Add(new("scout.primary_shots", _scoutShots));
            into.Add(new("scout.primary_units", _scoutShotUnits));
        }

        public override void Died()
        {
            (_scoutPrimaryClip, _scoutPrimaryReserve, _scoutGunEmpty) = (-1, -1, false);
            _scoutDrinking = false;
            _scoutDashStage = 0;
            _scoutWasPhased = false;
        }
    }
}
